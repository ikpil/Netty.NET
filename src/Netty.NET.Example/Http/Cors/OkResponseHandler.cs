/*
 * Copyright 2013 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Http.Cors;

/**
 * A simple handler which will simple return a successful Http
 * response for any request.
 */
// CLR: FullHttpResponse is IFullHttpResponse and Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer. ctx.writeAndFlush
// returns a Task; ChannelFutureListener.CLOSE is a synchronous Task continuation that closes the channel on its event
// loop (Common's EventExecutorTaskScheduler), as codec-http's CorsHandler maps it; a Task has no channel, so the
// channel is ctx.Channel.
public class OkResponseHandler : SimpleChannelInboundHandler<object>
{
    protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
    {
        IFullHttpResponse response = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer);
        response.Headers().Set("custom-response-header", "Some value");
        IChannel channel = ctx.Channel;
        _ = ctx.WriteAndFlushAsync(response).ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
    }
}
