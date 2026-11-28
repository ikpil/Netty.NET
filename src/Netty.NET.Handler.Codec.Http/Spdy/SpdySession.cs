/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;

namespace Netty.NET.Handler.Codec.Spdy;

// CLR: AtomicInteger is an int field updated with System.Threading.Interlocked (Increment, Decrement, Add,
// Volatile.Read for get(); Add returns the new value as addAndGet does,
// https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.add?view=net-10.0).
// ConcurrentHashMap<Integer, StreamState> is ConcurrentDictionary<int, StreamState> (put is AddOrUpdate capturing
// the previous value, remove is TryRemove and get is TryGetValue), and ConcurrentLinkedQueue is ConcurrentQueue
// (offer is Enqueue, which always succeeds as offer does on an unbounded queue, peek is TryPeek and poll is
// TryDequeue). The priority-ordered TreeMap of activeStreams() is a SortedDictionary<int, StreamState> with the
// StreamComparator as its IComparer<int>
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.sorteddictionary-2.-ctor?view=net-10.0);
// it is returned as IDictionary<int, StreamState>, so StreamState is internal instead of private (a private nested
// type cannot appear in the signature of an internal method, CS0050). The Java byte priority is the CLR byte.
internal sealed class SpdySession
{

    private int activeLocalStreams;
    private int activeRemoteStreams;
    private readonly ConcurrentDictionary<int, StreamState> activeStreams =
            new ConcurrentDictionary<int, StreamState>();
    private readonly StreamComparator streamComparator;
    private int sendWindowSize;
    private int receiveWindowSize;

    internal SpdySession(int sendWindowSize, int receiveWindowSize)
    {
        streamComparator = new StreamComparator(this);
        this.sendWindowSize = sendWindowSize;
        this.receiveWindowSize = receiveWindowSize;
    }

    internal int NumActiveStreams(bool remote)
    {
        if (remote)
        {
            return Volatile.Read(ref activeRemoteStreams);
        }
        else
        {
            return Volatile.Read(ref activeLocalStreams);
        }
    }

    internal bool NoActiveStreams()
    {
        return activeStreams.IsEmpty;
    }

    internal bool IsActiveStream(int streamId)
    {
        return activeStreams.ContainsKey(streamId);
    }

    // Stream-IDs should be iterated in priority order
    internal IDictionary<int, StreamState> ActiveStreams()
    {
        SortedDictionary<int, StreamState> streams = new SortedDictionary<int, StreamState>(streamComparator);

        foreach (KeyValuePair<int, StreamState> e in activeStreams)
        {
            streams[e.Key] = e.Value;
        }

        return streams;
    }

    internal void AcceptStream(
            int streamId, byte priority, bool remoteSideClosed, bool localSideClosed,
            int sendWindowSize, int receiveWindowSize, bool remote)
    {
        if (!remoteSideClosed || !localSideClosed)
        {
            StreamState newState = new StreamState(
                    priority, remoteSideClosed, localSideClosed, sendWindowSize, receiveWindowSize);
            StreamState state = null;
            activeStreams.AddOrUpdate(streamId, newState, (_, old) =>
            {
                state = old;
                return newState;
            });

            if (state == null)
            {
                if (remote)
                {
                    Interlocked.Increment(ref activeRemoteStreams);
                }
                else
                {
                    Interlocked.Increment(ref activeLocalStreams);
                }
            }
        }
    }

    private StreamState RemoveActiveStream(int streamId, bool remote)
    {
        activeStreams.TryRemove(streamId, out StreamState state);

        if (state != null)
        {
            if (remote)
            {
                Interlocked.Decrement(ref activeRemoteStreams);
            }
            else
            {
                Interlocked.Decrement(ref activeLocalStreams);
            }
        }

        return state;
    }

    internal void RemoveStream(int streamId, Exception cause, bool remote)
    {
        StreamState state = RemoveActiveStream(streamId, remote);
        if (state != null)
        {
            state.ClearPendingWrites(cause);
        }
    }

    internal bool IsRemoteSideClosed(int streamId)
    {
        StreamState state = Get(streamId);
        return state == null || state.IsRemoteSideClosed();
    }

    internal void CloseRemoteSide(int streamId, bool remote)
    {
        StreamState state = Get(streamId);
        if (state != null)
        {
            state.CloseRemoteSide();

            if (state.IsLocalSideClosed())
            {
                RemoveActiveStream(streamId, remote);
            }
        }
    }

    internal bool IsLocalSideClosed(int streamId)
    {
        StreamState state = Get(streamId);
        return state == null || state.IsLocalSideClosed();
    }

    internal void CloseLocalSide(int streamId, bool remote)
    {
        StreamState state = Get(streamId);
        if (state != null)
        {
            state.CloseLocalSide();

            if (state.IsRemoteSideClosed())
            {
                RemoveActiveStream(streamId, remote);
            }
        }
    }

    /*
     * hasReceivedReply and receivedReply are only called from channelRead()
     * no need to synchronize access to the StreamState
     */
    internal bool HasReceivedReply(int streamId)
    {
        StreamState state = Get(streamId);
        return state != null && state.HasReceivedReply();
    }

    internal void ReceivedReply(int streamId)
    {
        StreamState state = Get(streamId);
        if (state != null)
        {
            state.ReceivedReply();
        }
    }

    internal int GetSendWindowSize(int streamId)
    {
        if (streamId == SPDY_SESSION_STREAM_ID)
        {
            return Volatile.Read(ref sendWindowSize);
        }

        StreamState state = Get(streamId);
        return state != null ? state.GetSendWindowSize() : -1;
    }

    internal int UpdateSendWindowSize(int streamId, int deltaWindowSize)
    {
        if (streamId == SPDY_SESSION_STREAM_ID)
        {
            return Interlocked.Add(ref sendWindowSize, deltaWindowSize);
        }

        StreamState state = Get(streamId);
        return state != null ? state.UpdateSendWindowSize(deltaWindowSize) : -1;
    }

    internal int UpdateReceiveWindowSize(int streamId, int deltaWindowSize)
    {
        if (streamId == SPDY_SESSION_STREAM_ID)
        {
            return Interlocked.Add(ref receiveWindowSize, deltaWindowSize);
        }

        StreamState state = Get(streamId);
        if (state == null)
        {
            return -1;
        }

        if (deltaWindowSize > 0)
        {
            state.SetReceiveWindowSizeLowerBound(0);
        }

        return state.UpdateReceiveWindowSize(deltaWindowSize);
    }

    internal int GetReceiveWindowSizeLowerBound(int streamId)
    {
        if (streamId == SPDY_SESSION_STREAM_ID)
        {
            return 0;
        }

        StreamState state = Get(streamId);
        return state != null ? state.GetReceiveWindowSizeLowerBound() : 0;
    }

    internal void UpdateAllSendWindowSizes(int deltaWindowSize)
    {
        foreach (StreamState state in activeStreams.Values)
        {
            state.UpdateSendWindowSize(deltaWindowSize);
        }
    }

    internal void UpdateAllReceiveWindowSizes(int deltaWindowSize)
    {
        foreach (StreamState state in activeStreams.Values)
        {
            state.UpdateReceiveWindowSize(deltaWindowSize);

            if (deltaWindowSize < 0)
            {
                state.SetReceiveWindowSizeLowerBound(deltaWindowSize);
            }
        }
    }

    internal bool PutPendingWrite(int streamId, PendingWrite pendingWrite)
    {
        StreamState state = Get(streamId);
        return state != null && state.PutPendingWrite(pendingWrite);
    }

    internal PendingWrite GetPendingWrite(int streamId)
    {
        if (streamId == SPDY_SESSION_STREAM_ID)
        {
            foreach (KeyValuePair<int, StreamState> e in ActiveStreams())
            {
                StreamState state = e.Value;
                if (state.GetSendWindowSize() > 0)
                {
                    PendingWrite pendingWrite = state.GetPendingWrite();
                    if (pendingWrite != null)
                    {
                        return pendingWrite;
                    }
                }
            }

            return null;
        }

        StreamState stream = Get(streamId);
        return stream != null ? stream.GetPendingWrite() : null;
    }

    internal PendingWrite RemovePendingWrite(int streamId)
    {
        StreamState state = Get(streamId);
        return state != null ? state.RemovePendingWrite() : null;
    }

    // CLR: activeStreams.get(streamId), which is null when the stream is not active.
    private StreamState Get(int streamId)
    {
        activeStreams.TryGetValue(streamId, out StreamState state);
        return state;
    }

    internal sealed class StreamState
    {

        private readonly byte priority;
        private bool remoteSideClosed;
        private bool localSideClosed;
        private bool receivedReply;
        private int sendWindowSize;
        private int receiveWindowSize;
        private int receiveWindowSizeLowerBound;
        private readonly ConcurrentQueue<PendingWrite> pendingWriteQueue = new ConcurrentQueue<PendingWrite>();

        internal StreamState(
                byte priority, bool remoteSideClosed, bool localSideClosed,
                int sendWindowSize, int receiveWindowSize)
        {
            this.priority = priority;
            this.remoteSideClosed = remoteSideClosed;
            this.localSideClosed = localSideClosed;
            this.sendWindowSize = sendWindowSize;
            this.receiveWindowSize = receiveWindowSize;
        }

        internal byte GetPriority()
        {
            return priority;
        }

        internal bool IsRemoteSideClosed()
        {
            return remoteSideClosed;
        }

        internal void CloseRemoteSide()
        {
            remoteSideClosed = true;
        }

        internal bool IsLocalSideClosed()
        {
            return localSideClosed;
        }

        internal void CloseLocalSide()
        {
            localSideClosed = true;
        }

        internal bool HasReceivedReply()
        {
            return receivedReply;
        }

        internal void ReceivedReply()
        {
            receivedReply = true;
        }

        internal int GetSendWindowSize()
        {
            return Volatile.Read(ref sendWindowSize);
        }

        internal int UpdateSendWindowSize(int deltaWindowSize)
        {
            return Interlocked.Add(ref sendWindowSize, deltaWindowSize);
        }

        internal int UpdateReceiveWindowSize(int deltaWindowSize)
        {
            return Interlocked.Add(ref receiveWindowSize, deltaWindowSize);
        }

        internal int GetReceiveWindowSizeLowerBound()
        {
            return receiveWindowSizeLowerBound;
        }

        internal void SetReceiveWindowSizeLowerBound(int receiveWindowSizeLowerBound)
        {
            this.receiveWindowSizeLowerBound = receiveWindowSizeLowerBound;
        }

        internal bool PutPendingWrite(PendingWrite msg)
        {
            pendingWriteQueue.Enqueue(msg);
            return true;
        }

        internal PendingWrite GetPendingWrite()
        {
            return pendingWriteQueue.TryPeek(out PendingWrite pendingWrite) ? pendingWrite : null;
        }

        internal PendingWrite RemovePendingWrite()
        {
            return pendingWriteQueue.TryDequeue(out PendingWrite pendingWrite) ? pendingWrite : null;
        }

        internal void ClearPendingWrites(Exception cause)
        {
            for (;;)
            {
                PendingWrite pendingWrite = RemovePendingWrite();
                if (pendingWrite == null)
                {
                    break;
                }

                pendingWrite.Fail(cause);
            }
        }
    }

    private sealed class StreamComparator : IComparer<int>
    {
        private readonly SpdySession session;

        internal StreamComparator(SpdySession session)
        {
            this.session = session;
        }

        public int Compare(int id1, int id2)
        {
            StreamState state1 = session.Get(id1);
            StreamState state2 = session.Get(id2);

            int result = state1.GetPriority() - state2.GetPriority();
            if (result != 0)
            {
                return result;
            }

            return id1 - id2;
        }
    }

    public sealed class PendingWrite
    {
        internal readonly ISpdyDataFrame spdyDataFrame;
        internal readonly TaskCompletionSource promise;

        internal PendingWrite(ISpdyDataFrame spdyDataFrame, TaskCompletionSource promise)
        {
            this.spdyDataFrame = spdyDataFrame;
            this.promise = promise;
        }

        internal void Fail(Exception cause)
        {
            spdyDataFrame.Release();
            promise.TrySetException(cause);
        }
    }
}
