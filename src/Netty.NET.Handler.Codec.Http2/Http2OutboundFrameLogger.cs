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

using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2FrameLogger.Direction;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Decorator around a {@link Http2FrameWriter} that logs all outbound frames before calling the
 * writer.
 */
// CLR: the Task-returning methods take the Async suffix, ChannelPromise is TaskCompletionSource and ChannelFuture is
// Task (docs/codec-http-porting.md, Shared CLR adaptations). Closeable.close() is Close() (IDisposable.Dispose() comes
// from IHttp2FrameWriter).
public class Http2OutboundFrameLogger : IHttp2FrameWriter
{
    private readonly IHttp2FrameWriter writer;
    private readonly Http2FrameLogger logger;

    public Http2OutboundFrameLogger(IHttp2FrameWriter writer, Http2FrameLogger logger)
    {
        this.writer = CheckNotNull(writer, "writer");
        this.logger = CheckNotNull(logger, "logger");
    }

    public Task WriteDataAsync(IChannelHandlerContext ctx, int streamId, ByteBuf data,
            int padding, bool endStream, TaskCompletionSource promise)
    {
        logger.LogData(OUTBOUND, ctx, streamId, data, padding, endStream);
        return writer.WriteDataAsync(ctx, streamId, data, padding, endStream, promise);
    }

    public Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId,
            IHttp2Headers headers, int padding, bool endStream, TaskCompletionSource promise)
    {
        logger.LogHeaders(OUTBOUND, ctx, streamId, headers, padding, endStream);
        return writer.WriteHeadersAsync(ctx, streamId, headers, padding, endStream, promise);
    }

    public Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId,
            IHttp2Headers headers, int streamDependency, short weight, bool exclusive,
            int padding, bool endStream, TaskCompletionSource promise)
    {
        logger.LogHeaders(OUTBOUND, ctx, streamId, headers, streamDependency, weight, exclusive,
                padding, endStream);
        return writer.WriteHeadersAsync(ctx, streamId, headers, streamDependency, weight,
                exclusive, padding, endStream, promise);
    }

    public Task WritePriorityAsync(IChannelHandlerContext ctx, int streamId,
            int streamDependency, short weight, bool exclusive, TaskCompletionSource promise)
    {
        logger.LogPriority(OUTBOUND, ctx, streamId, streamDependency, weight, exclusive);
        return writer.WritePriorityAsync(ctx, streamId, streamDependency, weight, exclusive, promise);
    }

    public Task WriteRstStreamAsync(IChannelHandlerContext ctx,
            int streamId, long errorCode, TaskCompletionSource promise)
    {
        logger.LogRstStream(OUTBOUND, ctx, streamId, errorCode);
        return writer.WriteRstStreamAsync(ctx, streamId, errorCode, promise);
    }

    public Task WriteSettingsAsync(IChannelHandlerContext ctx,
            Http2Settings settings, TaskCompletionSource promise)
    {
        logger.LogSettings(OUTBOUND, ctx, settings);
        return writer.WriteSettingsAsync(ctx, settings, promise);
    }

    public Task WriteSettingsAckAsync(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        logger.LogSettingsAck(OUTBOUND, ctx);
        return writer.WriteSettingsAckAsync(ctx, promise);
    }

    public Task WritePingAsync(IChannelHandlerContext ctx, bool ack,
            long data, TaskCompletionSource promise)
    {
        if (ack)
        {
            logger.LogPingAck(OUTBOUND, ctx, data);
        }
        else
        {
            logger.LogPing(OUTBOUND, ctx, data);
        }
        return writer.WritePingAsync(ctx, ack, data, promise);
    }

    public Task WritePushPromiseAsync(IChannelHandlerContext ctx, int streamId,
            int promisedStreamId, IHttp2Headers headers, int padding, TaskCompletionSource promise)
    {
        logger.LogPushPromise(OUTBOUND, ctx, streamId, promisedStreamId, headers, padding);
        return writer.WritePushPromiseAsync(ctx, streamId, promisedStreamId, headers, padding, promise);
    }

    public Task WriteGoAwayAsync(IChannelHandlerContext ctx, int lastStreamId, long errorCode,
            ByteBuf debugData, TaskCompletionSource promise)
    {
        logger.LogGoAway(OUTBOUND, ctx, lastStreamId, errorCode, debugData);
        return writer.WriteGoAwayAsync(ctx, lastStreamId, errorCode, debugData, promise);
    }

    public Task WriteWindowUpdateAsync(IChannelHandlerContext ctx,
            int streamId, int windowSizeIncrement, TaskCompletionSource promise)
    {
        logger.LogWindowsUpdate(OUTBOUND, ctx, streamId, windowSizeIncrement);
        return writer.WriteWindowUpdateAsync(ctx, streamId, windowSizeIncrement, promise);
    }

    public Task WriteFrameAsync(IChannelHandlerContext ctx, byte frameType, int streamId,
            Http2Flags flags, ByteBuf payload, TaskCompletionSource promise)
    {
        logger.LogUnknownFrame(OUTBOUND, ctx, frameType, streamId, flags, payload);
        return writer.WriteFrameAsync(ctx, frameType, streamId, flags, payload, promise);
    }

    public void Close()
    {
        writer.Close();
    }

    public IHttp2FrameWriter.IConfiguration Configuration()
    {
        return writer.Configuration();
    }
}
