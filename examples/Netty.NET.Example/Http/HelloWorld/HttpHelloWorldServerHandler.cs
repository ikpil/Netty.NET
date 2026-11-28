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
using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValues;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Example.Http.HelloWorld;

// CLR: HttpObject/HttpRequest/FullHttpResponse are the I-prefixed codec-http interfaces and content() is the Content
// property. A Java char literal in a byte[] initializer is narrowed by the compiler; C# needs the explicit (byte) cast.
// ctx.write(...) returns a Task (ChannelFuture f); ChannelFutureListener.CLOSE is a synchronous Task continuation that
// closes the channel on its event loop (Common's EventExecutorTaskScheduler), as codec-http's CorsHandler maps it; a Task
// has no channel, so the channel is ctx.Channel. printStackTrace() writes the exception to Console.Error and ctx.close()
// is CloseAsync(), whose Task is discarded as Java discards the future.
public class HttpHelloWorldServerHandler : SimpleChannelInboundHandler<IHttpObject>
{
    private static readonly byte[] CONTENT = { (byte) 'H', (byte) 'e', (byte) 'l', (byte) 'l', (byte) 'o', (byte) ' ', (byte) 'W', (byte) 'o', (byte) 'r', (byte) 'l', (byte) 'd' };

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpObject msg)
    {
        if (msg is IHttpRequest)
        {
            IHttpRequest req = (IHttpRequest) msg;

            bool keepAlive = HttpUtil.IsKeepAlive(req);
            IFullHttpResponse response = new DefaultFullHttpResponse(req.ProtocolVersion(), OK,
                                                                    Unpooled.WrappedBuffer(CONTENT));
            response.Headers()
                    .Set(CONTENT_TYPE, TEXT_PLAIN)
                    .SetInt(CONTENT_LENGTH, response.Content.ReadableBytes);

            if (keepAlive)
            {
                if (!req.ProtocolVersion().IsKeepAliveDefault())
                {
                    response.Headers().Set(CONNECTION, KEEP_ALIVE);
                }
            }
            else
            {
                // Tell the client we're going to close the connection.
                response.Headers().Set(CONNECTION, CLOSE);
            }

            Task f = ctx.WriteAsync(response);

            if (!keepAlive)
            {
                IChannel channel = ctx.Channel;
                _ = f.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
            }
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
