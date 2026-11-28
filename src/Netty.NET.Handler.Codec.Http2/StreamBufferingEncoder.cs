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
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Implementation of a {@link Http2ConnectionEncoder} that dispatches all method call to another
 * {@link Http2ConnectionEncoder}, until {@code SETTINGS_MAX_CONCURRENT_STREAMS} is reached.
 * <p/>
 * <p>When this limit is hit, instead of rejecting any new streams this implementation buffers newly
 * created streams and their corresponding frames. Once an active stream gets closed or the maximum
 * number of concurrent streams is increased, this encoder will automatically try to empty its
 * buffer and create as many new streams as possible.
 * <p/>
 * <p>
 * If a {@code GOAWAY} frame is received from the remote endpoint, all buffered writes for streams
 * with an ID less than the specified {@code lastStreamId} will immediately fail with a
 * {@link Http2GoAwayException}.
 * <p/>
 * <p>
 * If the channel/encoder gets closed, all new and buffered writes will immediately fail with a
 * {@link Http2ChannelClosedException}.
 * </p>
 * <p>This implementation makes the buffering mostly transparent and is expected to be used as a
 * drop-in decorator of {@link DefaultHttp2ConnectionEncoder}.
 * </p>
 */
// CLR: The Task-returning methods take the Async suffix, and promise.setFailure(e)/setSuccess() returning the
// promise are SetException(e)/SetResult() followed by the promise's Task. TreeMap<Integer, PendingStream> is a
// SortedDictionary<int, PendingStream> : pollFirstEntry() takes and removes the first (smallest) key, and the
// removing iterator of cancelGoAwayStreams walks a snapshot of the values in key order and removes each cancelled
// stream by its id. ArrayDeque<Frame> is a Queue<Frame>, iterated in insertion order. The anonymous
// Http2ConnectionAdapter is the private nested GoAwayAndCloseListener, and the inner HeadersFrame/DataFrame
// classes get the encoder as a constructor argument (a C# nested class has no implicit outer instance).
// serialVersionUID has no counterpart; byte[].clone() is Array.Clone(). GoAwayDetail is internal instead of
// private because the internal Http2GoAwayException(GoAwayDetail) constructor takes it (a parameter type cannot be
// less accessible than its method, CS0051). close() overrides the virtual DecoratingHttp2FrameWriter.Close().
public class StreamBufferingEncoder : DecoratingHttp2ConnectionEncoder
{

    /**
     * Thrown if buffered streams are terminated due to this encoder being closed.
     */
    public sealed class Http2ChannelClosedException : Http2Exception
    {

        public Http2ChannelClosedException() : base(Http2Error.REFUSED_STREAM, "Connection closed")
        {
        }
    }

    internal sealed class GoAwayDetail
    {
        internal readonly int lastStreamId;
        internal readonly long errorCode;
        internal readonly byte[] debugData;

        internal GoAwayDetail(int lastStreamId, long errorCode, byte[] debugData)
        {
            this.lastStreamId = lastStreamId;
            this.errorCode = errorCode;
            this.debugData = (byte[]) debugData.Clone();
        }
    }

    /**
     * Thrown by {@link StreamBufferingEncoder} if buffered streams are terminated due to
     * receipt of a {@code GOAWAY}.
     */
    public sealed class Http2GoAwayException : Http2Exception
    {
        private readonly GoAwayDetail goAwayDetail;

        public Http2GoAwayException(int lastStreamId, long errorCode, byte[] debugData)
            : this(new GoAwayDetail(lastStreamId, errorCode, debugData))
        {
        }

        internal Http2GoAwayException(GoAwayDetail goAwayDetail) : base(Http2Error.STREAM_CLOSED)
        {
            this.goAwayDetail = goAwayDetail;
        }

        public int LastStreamId()
        {
            return goAwayDetail.lastStreamId;
        }

        public long ErrorCode()
        {
            return goAwayDetail.errorCode;
        }

        public byte[] DebugData()
        {
            return (byte[]) goAwayDetail.debugData.Clone();
        }
    }

    /**
     * Buffer for any streams and corresponding frames that could not be created due to the maximum
     * concurrent stream limit being hit.
     */
    private readonly SortedDictionary<int, PendingStream> pendingStreams = new SortedDictionary<int, PendingStream>();
    private int maxConcurrentStreams;
    private bool closed;
    private GoAwayDetail goAwayDetail;

    public StreamBufferingEncoder(IHttp2ConnectionEncoder @delegate) : this(@delegate, SMALLEST_MAX_CONCURRENT_STREAMS)
    {
    }

    public StreamBufferingEncoder(IHttp2ConnectionEncoder @delegate, int initialMaxConcurrentStreams) : base(@delegate)
    {
        maxConcurrentStreams = initialMaxConcurrentStreams;
        Connection().AddListener(new GoAwayAndCloseListener(this));
    }

    private sealed class GoAwayAndCloseListener : Http2ConnectionAdapter
    {
        private readonly StreamBufferingEncoder encoder;

        internal GoAwayAndCloseListener(StreamBufferingEncoder encoder)
        {
            this.encoder = encoder;
        }

        public override void OnGoAwayReceived(int lastStreamId, long errorCode, ByteBuf debugData)
        {
            encoder.goAwayDetail = new GoAwayDetail(
                // Using getBytes(..., false) is safe here as GoAwayDetail(...) will clone the byte[].
                lastStreamId, errorCode,
                ByteBufUtil.GetBytes(debugData, debugData.ReaderIndex, debugData.ReadableBytes, false));
            encoder.CancelGoAwayStreams(encoder.goAwayDetail);
        }

        public override void OnStreamClosed(IHttp2Stream stream)
        {
            encoder.TryCreatePendingStreams();
        }
    }

    /**
     * Indicates the number of streams that are currently buffered, awaiting creation.
     */
    public int NumBufferedStreams()
    {
        return pendingStreams.Count;
    }

    public override Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding,
                                      bool endStream, TaskCompletionSource promise)
    {
        return WriteHeaders0(ctx, streamId, headers, false, 0, (short) 0,
                             false, padding, endStream, promise);
    }

    public override Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
                                      int streamDependency, short weight, bool exclusive, int padding,
                                      bool endOfStream, TaskCompletionSource promise)
    {
        return WriteHeaders0(ctx, streamId, headers, true, streamDependency, weight, exclusive, padding,
                             endOfStream, promise);
    }

    private Task WriteHeaders0(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
                                        bool hasPriority, int streamDependency, short weight, bool exclusive,
                                        int padding, bool endOfStream, TaskCompletionSource promise)
    {
        if (closed)
        {
            promise.SetException(new Http2ChannelClosedException());
            return promise.Task;
        }

        if (IsExistingStream(streamId) || CanCreateStream())
        {
            if (hasPriority)
            {
                return base.WriteHeadersAsync(ctx, streamId, headers, streamDependency, weight,
                                          exclusive, padding, endOfStream, promise);
            }

            return base.WriteHeadersAsync(ctx, streamId, headers, padding, endOfStream, promise);
        }

        if (goAwayDetail != null)
        {
            promise.SetException(new Http2GoAwayException(goAwayDetail));
            return promise.Task;
        }

        pendingStreams.TryGetValue(streamId, out PendingStream pendingStream);

        if (pendingStream == null)
        {
            pendingStream = new PendingStream(ctx, streamId);
            pendingStreams[streamId] = pendingStream;
        }

        pendingStream.frames.Enqueue(new HeadersFrame(this, headers, hasPriority, streamDependency, weight, exclusive,
                padding, endOfStream, promise));
        return promise.Task;
    }

    public override Task WriteRstStreamAsync(IChannelHandlerContext ctx, int streamId, long errorCode,
                                        TaskCompletionSource promise)
    {
        if (IsExistingStream(streamId))
        {
            return base.WriteRstStreamAsync(ctx, streamId, errorCode, promise);
        }

        // Since the delegate doesn't know about any buffered streams we have to handle cancellation
        // of the promises and releasing of the ByteBufs here.
        pendingStreams.Remove(streamId, out PendingStream stream);

        if (stream != null)
        {
            // Sending a RST_STREAM to a buffered stream will succeed the promise of all frames
            // associated with the stream, as sending a RST_STREAM means that someone "doesn't care"
            // about the stream anymore and thus there is not point in failing the promises and invoking
            // error handling routines.
            stream.Close(null);
            promise.SetResult();
        }
        else
        {
            promise.SetException(ConnectionError(PROTOCOL_ERROR, "Stream does not exist %d", streamId));
        }

        return promise.Task;
    }

    public override Task WriteDataAsync(IChannelHandlerContext ctx, int streamId, ByteBuf data,
                                   int padding, bool endOfStream, TaskCompletionSource promise)
    {
        if (IsExistingStream(streamId))
        {
            return base.WriteDataAsync(ctx, streamId, data, padding, endOfStream, promise);
        }

        pendingStreams.TryGetValue(streamId, out PendingStream pendingStream);

        if (pendingStream != null)
        {
            pendingStream.frames.Enqueue(new DataFrame(this, data, padding, endOfStream, promise));
        }
        else
        {
            ReferenceCountUtil.SafeRelease(data);
            promise.SetException(ConnectionError(PROTOCOL_ERROR, "Stream does not exist %d", streamId));
        }

        return promise.Task;
    }

    public override Task WriteSettingsAckAsync(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        Task future = base.WriteSettingsAckAsync(ctx, promise);
        // In case autoAckSettings was set to false, decorated DefaultHttp2ConnectionEncoder will dequeue pending
        // settings and call remoteSettings on its own instance. Therefore, we need to consume potentially updated value
        // after this method returns.
        UpdateMaxConcurrentStreams();
        return future;
    }

    public override void RemoteSettings(Http2Settings settings)
    {
        // Need to let the delegate decoder handle the settings first, so that it sees the
        // new setting before we attempt to create any new streams.
        base.RemoteSettings(settings);
        UpdateMaxConcurrentStreams();
    }

    private void UpdateMaxConcurrentStreams()
    {
        // Get the updated value for SETTINGS_MAX_CONCURRENT_STREAMS.
        maxConcurrentStreams = Connection().Local().MaxActiveStreams();
        // Try to create new streams up to the new threshold.
        TryCreatePendingStreams();
    }

    public override void Close()
    {
        try
        {
            if (!closed)
            {
                closed = true;

                // Fail all buffered streams.
                Http2ChannelClosedException e = new Http2ChannelClosedException();

                while (pendingStreams.Count != 0)
                {
                    PendingStream stream = PollFirstEntry().Value;
                    stream.Close(e);
                }
            }
        }
        finally
        {
            base.Close();
        }
    }

    private void TryCreatePendingStreams()
    {
        while (pendingStreams.Count != 0 && CanCreateStream())
        {
            KeyValuePair<int, PendingStream> entry = PollFirstEntry();
            PendingStream pendingStream = entry.Value;

            try
            {
                pendingStream.SendFrames();
            }
            catch (Exception t)
            {
                pendingStream.Close(t);
            }
        }
    }

    // CLR: TreeMap.pollFirstEntry(): removes and returns the entry with the smallest key.
    private KeyValuePair<int, PendingStream> PollFirstEntry()
    {
        KeyValuePair<int, PendingStream> first = pendingStreams.First();
        pendingStreams.Remove(first.Key);
        return first;
    }

    private void CancelGoAwayStreams(GoAwayDetail goAwayDetail)
    {
        IEnumerator<PendingStream> iter = pendingStreams.Values.ToList().GetEnumerator();
        Exception e = new Http2GoAwayException(goAwayDetail);

        while (iter.MoveNext())
        {
            PendingStream stream = iter.Current;
            if (stream.streamId > goAwayDetail.lastStreamId)
            {
                pendingStreams.Remove(stream.streamId);
                stream.Close(e);
            }
        }
    }

    /**
     * Determines whether or not we're allowed to create a new stream right now.
     */
    private bool CanCreateStream()
    {
        return Connection().Local().NumActiveStreams() < maxConcurrentStreams;
    }

    private bool IsExistingStream(int streamId)
    {
        return streamId <= Connection().Local().LastStreamCreated();
    }

    private sealed class PendingStream
    {
        internal readonly IChannelHandlerContext ctx;
        internal readonly int streamId;
        internal readonly Queue<Frame> frames = new Queue<Frame>(2);

        internal PendingStream(IChannelHandlerContext ctx, int streamId)
        {
            this.ctx = ctx;
            this.streamId = streamId;
        }

        internal void SendFrames()
        {
            foreach (Frame frame in frames)
            {
                frame.Send(ctx, streamId);
            }
        }

        internal void Close(Exception t)
        {
            foreach (Frame frame in frames)
            {
                frame.Release(t);
            }
        }
    }

    private abstract class Frame
    {
        internal readonly TaskCompletionSource promise;

        protected Frame(TaskCompletionSource promise)
        {
            this.promise = promise;
        }

        /**
         * Release any resources (features, buffers, ...) associated with the frame.
         */
        internal virtual void Release(Exception t)
        {
            if (t == null)
            {
                promise.SetResult();
            }
            else
            {
                promise.SetException(t);
            }
        }

        internal abstract void Send(IChannelHandlerContext ctx, int streamId);
    }

    private sealed class HeadersFrame : Frame
    {
        private readonly StreamBufferingEncoder encoder;
        internal readonly IHttp2Headers headers;
        internal readonly int streamDependency;
        internal readonly bool hasPriority;
        internal readonly short weight;
        internal readonly bool exclusive;
        internal readonly int padding;
        internal readonly bool endOfStream;

        internal HeadersFrame(StreamBufferingEncoder encoder, IHttp2Headers headers, bool hasPriority,
                     int streamDependency, short weight, bool exclusive,
                     int padding, bool endOfStream, TaskCompletionSource promise) : base(promise)
        {
            this.encoder = encoder;
            this.headers = headers;
            this.hasPriority = hasPriority;
            this.streamDependency = streamDependency;
            this.weight = weight;
            this.exclusive = exclusive;
            this.padding = padding;
            this.endOfStream = endOfStream;
        }

        internal override void Send(IChannelHandlerContext ctx, int streamId)
        {
            encoder.WriteHeaders0(ctx, streamId, headers, hasPriority, streamDependency, weight, exclusive, padding,
                          endOfStream,
                          promise);
        }
    }

    private sealed class DataFrame : Frame
    {
        private readonly StreamBufferingEncoder encoder;
        internal readonly ByteBuf data;
        internal readonly int padding;
        internal readonly bool endOfStream;

        internal DataFrame(StreamBufferingEncoder encoder, ByteBuf data, int padding, bool endOfStream,
                TaskCompletionSource promise) : base(promise)
        {
            this.encoder = encoder;
            this.data = data;
            this.padding = padding;
            this.endOfStream = endOfStream;
        }

        internal override void Release(Exception t)
        {
            base.Release(t);
            ReferenceCountUtil.SafeRelease(data);
        }

        internal override void Send(IChannelHandlerContext ctx, int streamId)
        {
            encoder.WriteDataAsync(ctx, streamId, data, padding, endOfStream, promise);
        }
    }
}
