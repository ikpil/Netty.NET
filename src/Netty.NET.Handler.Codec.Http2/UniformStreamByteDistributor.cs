/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A {@link StreamByteDistributor} that ignores stream priority and uniformly allocates bytes to all
 * streams. This class uses a minimum chunk size that will be allocated to each stream. While
 * fewer streams may be written to in each call to {@link #distribute(int, Writer)}, doing this
 * should improve the goodput on each written stream.
 */
// CLR: the Deque<State> is a LinkedList (pollFirst/addFirst/addLast/remove(Object) are the first node, AddFirst,
// AddLast and Remove(T), which is O(n) as Deque.remove(Object) is). The private method state(Http2Stream) is State_():
// it cannot share the name of the nested State class (CS0102, owner rule: trailing underscore). The inner State class
// and the anonymous Http2ConnectionAdapter (the private nested StreamListener) receive the distributor explicitly,
// because a C# nested class has no enclosing instance. Throwable is Exception; assert is Debug.Assert and its message
// prints the boolean as true/false and the number with the invariant culture.
public sealed class UniformStreamByteDistributor : IStreamByteDistributor
{
    private readonly IHttp2Connection.IPropertyKey stateKey;
    private readonly LinkedList<State> queue = new LinkedList<State>();

    /**
     * The minimum number of bytes that we will attempt to allocate to a stream. This is to
     * help improve goodput on a per-stream basis.
     */
    private int minAllocationChunk = DEFAULT_MIN_ALLOCATION_CHUNK;
    private long totalStreamableBytes;

    public UniformStreamByteDistributor(IHttp2Connection connection)
    {
        // Add a state for the connection.
        stateKey = connection.NewKey();
        IHttp2Stream connectionStream = connection.ConnectionStream();
        connectionStream.SetProperty(stateKey, new State(this, connectionStream));

        // Register for notification of new streams.
        connection.AddListener(new StreamListener(this));
    }

    // CLR: the anonymous Http2ConnectionAdapter registered by the constructor.
    private sealed class StreamListener : Http2ConnectionAdapter
    {
        private readonly UniformStreamByteDistributor distributor;

        internal StreamListener(UniformStreamByteDistributor distributor)
        {
            this.distributor = distributor;
        }

        public override void OnStreamAdded(IHttp2Stream stream)
        {
            stream.SetProperty(distributor.stateKey, new State(distributor, stream));
        }

        public override void OnStreamClosed(IHttp2Stream stream)
        {
            distributor.State_(stream).Close();
        }
    }

    /**
     * Sets the minimum allocation chunk that will be allocated to each stream. Defaults to 1KiB.
     *
     * @param minAllocationChunk the minimum number of bytes that will be allocated to each stream.
     * Must be > 0.
     */
    public void MinAllocationChunk(int minAllocationChunk)
    {
        CheckPositive(minAllocationChunk, "minAllocationChunk");

        this.minAllocationChunk = minAllocationChunk;
    }

    public void UpdateStreamableBytes(IStreamByteDistributor.IStreamState streamState)
    {
        State_(streamState.Stream()).UpdateStreamableBytes(StreamableBytes(streamState),
                                                          streamState.HasFrame(),
                                                          streamState.WindowSize());
    }

    public void UpdateDependencyTree(int childStreamId, int parentStreamId, short weight, bool exclusive)
    {
        // This class ignores priority and dependency!
    }

    public bool Distribute(int maxBytes, IStreamByteDistributor.IWriter writer)
    {
        int size = queue.Count;
        if (size == 0)
        {
            return totalStreamableBytes > 0;
        }

        int chunkSize = Math.Max(minAllocationChunk, maxBytes / size);

        State state = PollFirst();

        do
        {
            state.enqueued = false;
            if (state.windowNegative)
            {
                continue;
            }

            if (maxBytes == 0 && state.streamableBytes > 0)
            {
                // Stop at the first state that can't send. Add this state back to the head of the queue. Note
                // that empty frames at the head of the queue will always be written, assuming the stream window
                // is not negative.
                queue.AddFirst(state);
                state.enqueued = true;
                break;
            }

            // Allocate as much data as we can for this stream.
            int chunk = Math.Min(chunkSize, Math.Min(maxBytes, state.streamableBytes));
            maxBytes -= chunk;

            // Write the allocated bytes and enqueue as necessary.
            state.Write(chunk, writer);
        } while ((state = PollFirst()) != null);

        return totalStreamableBytes > 0;
    }

    // CLR: Deque.pollFirst(): removes and returns the head, or null when the queue is empty.
    private State PollFirst()
    {
        LinkedListNode<State> first = queue.First;
        if (first == null)
        {
            return null;
        }

        queue.RemoveFirst();
        return first.Value;
    }

    private State State_(IHttp2Stream stream)
    {
        return CheckNotNull(stream, "stream").GetProperty<State>(stateKey);
    }

    /**
     * The remote flow control state for a single stream.
     */
    private sealed class State
    {
        private readonly UniformStreamByteDistributor distributor;
        internal readonly IHttp2Stream stream;
        internal int streamableBytes;
        internal bool windowNegative;
        internal bool enqueued;
        internal bool writing;

        internal State(UniformStreamByteDistributor distributor, IHttp2Stream stream)
        {
            this.distributor = distributor;
            this.stream = stream;
        }

        internal void UpdateStreamableBytes(int newStreamableBytes, bool hasFrame, int windowSize)
        {
            Debug.Assert(hasFrame || newStreamableBytes == 0,
                "hasFrame: " + ToJavaString(hasFrame) + " newStreamableBytes: " +
                newStreamableBytes.ToString(CultureInfo.InvariantCulture));

            int delta = newStreamableBytes - streamableBytes;
            if (delta != 0)
            {
                streamableBytes = newStreamableBytes;
                distributor.totalStreamableBytes += delta;
            }

            // In addition to only enqueuing state when they have frames we enforce the following restrictions:
            // 1. If the window has gone negative. We never want to queue a state. However we also don't want to
            //    Immediately remove the item if it is already queued because removal from deque is O(n). So
            //    we allow it to stay queued and rely on the distribution loop to remove this state.
            // 2. If the window is zero we only want to queue if we are not writing. If we are writing that means
            //    we gave the state a chance to write zero length frames. We wait until updateStreamableBytes is
            //    called again before this state is allowed to write.
            windowNegative = windowSize < 0;

            if (hasFrame && (windowSize > 0 || windowSize == 0 && !writing))
            {
                AddToQueue();
            }
        }

        /**
         * Write any allocated bytes for the given stream and updates the streamable bytes,
         * assuming all of the bytes will be written.
         */
        internal void Write(int numBytes, IStreamByteDistributor.IWriter writer)
        {
            writing = true;

            try
            {
                // Write the allocated bytes.
                writer.Write(stream, numBytes);
            }
            catch (Exception t)
            {
                throw ConnectionError(INTERNAL_ERROR, t, "byte distribution write error");
            }
            finally
            {
                writing = false;
            }
        }

        internal void AddToQueue()
        {
            if (!enqueued)
            {
                enqueued = true;
                distributor.queue.AddLast(this);
            }
        }

        internal void RemoveFromQueue()
        {
            if (enqueued)
            {
                enqueued = false;
                distributor.queue.Remove(this);
            }
        }

        internal void Close()
        {
            // Remove this state from the queue.
            RemoveFromQueue();

            // Clear the streamable bytes.
            UpdateStreamableBytes(0, false, 0);
        }
    }
}
