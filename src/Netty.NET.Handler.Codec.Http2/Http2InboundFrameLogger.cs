/*
 * Copyright 2014 The Netty Project
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

using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2FrameLogger.Direction;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Decorator around a {@link Http2FrameReader} that logs all inbound frames before calling
 * back the listener.
 */
// CLR: the anonymous Http2FrameListener of readFrame is the private nested LoggingListener. Closeable.close() is
// Close() (IDisposable.Dispose() comes from IHttp2FrameReader).
public class Http2InboundFrameLogger : IHttp2FrameReader
{
    private readonly IHttp2FrameReader reader;
    private readonly Http2FrameLogger logger;

    public Http2InboundFrameLogger(IHttp2FrameReader reader, Http2FrameLogger logger)
    {
        this.reader = CheckNotNull(reader, "reader");
        this.logger = CheckNotNull(logger, "logger");
    }

    public void ReadFrame(IChannelHandlerContext ctx, ByteBuf input, IHttp2FrameListener listener)
    {
        reader.ReadFrame(ctx, input, new LoggingListener(logger, listener));
    }

    public void Close()
    {
        reader.Close();
    }

    public IHttp2FrameReader.IConfiguration Configuration()
    {
        return reader.Configuration();
    }

    // CLR: the anonymous Http2FrameListener of readFrame, which captures logger and listener.
    private sealed class LoggingListener : IHttp2FrameListener
    {
        private readonly Http2FrameLogger logger;
        private readonly IHttp2FrameListener listener;

        internal LoggingListener(Http2FrameLogger logger, IHttp2FrameListener listener)
        {
            this.logger = logger;
            this.listener = listener;
        }

        public int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data,
                int padding, bool endOfStream)
        {
            logger.LogData(INBOUND, ctx, streamId, data, padding, endOfStream);

            return listener.OnDataRead(ctx, streamId, data, padding, endOfStream);
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId,
                IHttp2Headers headers, int padding, bool endStream)
        {
            logger.LogHeaders(INBOUND, ctx, streamId, headers, padding, endStream);
            listener.OnHeadersRead(ctx, streamId, headers, padding, endStream);
        }

        public void OnHeadersRead(IChannelHandlerContext ctx, int streamId,
                IHttp2Headers headers, int streamDependency, short weight, bool exclusive,
                int padding, bool endStream)
        {
            logger.LogHeaders(INBOUND, ctx, streamId, headers, streamDependency, weight, exclusive,
                    padding, endStream);
            listener.OnHeadersRead(ctx, streamId, headers, streamDependency, weight, exclusive,
                    padding, endStream);
        }

        public void OnPriorityRead(IChannelHandlerContext ctx, int streamId,
                int streamDependency, short weight, bool exclusive)
        {
            logger.LogPriority(INBOUND, ctx, streamId, streamDependency, weight, exclusive);
            listener.OnPriorityRead(ctx, streamId, streamDependency, weight, exclusive);
        }

        public void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
        {
            logger.LogRstStream(INBOUND, ctx, streamId, errorCode);
            listener.OnRstStreamRead(ctx, streamId, errorCode);
        }

        public void OnSettingsAckRead(IChannelHandlerContext ctx)
        {
            logger.LogSettingsAck(INBOUND, ctx);
            listener.OnSettingsAckRead(ctx);
        }

        public void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
        {
            logger.LogSettings(INBOUND, ctx, settings);
            listener.OnSettingsRead(ctx, settings);
        }

        public void OnPingRead(IChannelHandlerContext ctx, long data)
        {
            logger.LogPing(INBOUND, ctx, data);
            listener.OnPingRead(ctx, data);
        }

        public void OnPingAckRead(IChannelHandlerContext ctx, long data)
        {
            logger.LogPingAck(INBOUND, ctx, data);
            listener.OnPingAckRead(ctx, data);
        }

        public void OnPushPromiseRead(IChannelHandlerContext ctx, int streamId,
                int promisedStreamId, IHttp2Headers headers, int padding)
        {
            logger.LogPushPromise(INBOUND, ctx, streamId, promisedStreamId, headers, padding);
            listener.OnPushPromiseRead(ctx, streamId, promisedStreamId, headers, padding);
        }

        public void OnGoAwayRead(IChannelHandlerContext ctx, int lastStreamId, long errorCode,
                ByteBuf debugData)
        {
            logger.LogGoAway(INBOUND, ctx, lastStreamId, errorCode, debugData);
            listener.OnGoAwayRead(ctx, lastStreamId, errorCode, debugData);
        }

        public void OnWindowUpdateRead(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement)
        {
            logger.LogWindowsUpdate(INBOUND, ctx, streamId, windowSizeIncrement);
            listener.OnWindowUpdateRead(ctx, streamId, windowSizeIncrement);
        }

        public void OnUnknownFrame(IChannelHandlerContext ctx, byte frameType, int streamId,
                Http2Flags flags, ByteBuf payload)
        {
            logger.LogUnknownFrame(INBOUND, ctx, frameType, streamId, flags, payload);
            listener.OnUnknownFrame(ctx, frameType, streamId, flags, payload);
        }
    }
}
