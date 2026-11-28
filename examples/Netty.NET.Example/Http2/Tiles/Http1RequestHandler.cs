/*
 * Copyright 2015 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpUtil;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Example.Http2.Tiles;

/**
 * Handles the requests for the tiled image using HTTP 1.x as a protocol.
 */
// CLR: FullHttpRequest/FullHttpResponse are IFullHttpRequest/IFullHttpResponse and Unpooled.EMPTY_BUFFER is
// Unpooled.EmptyBuffer. ctx.write/writeAndFlush are WriteAsync/WriteAndFlushAsync, whose Tasks are discarded;
// ChannelFutureListener.CLOSE on the write future is a continuation of the write Task that closes ctx.Channel (a
// Task has no channel), as codec-base's MessageAggregator maps it. ctx.executor().schedule(Runnable, latency,
// MILLISECONDS) is ctx.Executor.ScheduleAsync with a TimeSpan of the latency, whose Task is discarded. The
// statically imported HttpHeaderValues.KEEP_ALIVE/CLOSE are qualified: C# using static imports every member, and
// HttpHeaderNames.KEEP_ALIVE would make the simple name ambiguous.
public sealed class Http1RequestHandler : Http2RequestHandler
{

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest request)
    {
        if (HttpUtil.Is100ContinueExpected(request))
        {
            _ = ctx.WriteAsync(new DefaultFullHttpResponse(HTTP_1_1, CONTINUE, Unpooled.EmptyBuffer));
        }

        base.ChannelRead0(ctx, request);
    }

    protected override void SendResponse(IChannelHandlerContext ctx, string streamId, int latency,
            IFullHttpResponse response, IFullHttpRequest request)
    {
        HttpUtil.SetContentLength(response, response.Content.ReadableBytes);
        _ = ctx.Executor.ScheduleAsync(() =>
        {
            if (IsKeepAlive(request))
            {
                if (request.ProtocolVersion().Equals(HTTP_1_0))
                {
                    response.Headers().Set(CONNECTION, HttpHeaderValues.KEEP_ALIVE);
                }

                _ = ctx.WriteAndFlushAsync(response);
            }
            else
            {
                // Tell the client we're going to close the connection.
                response.Headers().Set(CONNECTION, HttpHeaderValues.CLOSE);
                _ = ctx.WriteAndFlushAsync(response).ContinueWith(_ => ctx.Channel.CloseAsync(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }, TimeSpan.FromMilliseconds(latency));
    }
}
