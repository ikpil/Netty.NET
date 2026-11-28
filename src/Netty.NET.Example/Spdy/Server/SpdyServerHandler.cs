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
using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValues;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpUtil;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Example.Spdy.Server;

/**
 * HTTP handler that responds with a "Hello World"
 */
// CLR: new Date() in a string is Date.toString(), the pattern "EEE MMM dd HH:mm:ss zzz yyyy" in the local time zone;
// .NET has no time zone abbreviation, so it is DateTimeOffset.Now formatted with the invariant culture and the UTC
// offset (zzz) in the zone's place. A using static directive imports a whole class, and HttpHeaderValues also declares
// CONTINUE, so the status is written HttpResponseStatus.CONTINUE. HttpRequest/FullHttpResponse are
// IHttpRequest/IFullHttpResponse, content() is the Content property, CharsetUtil.UTF_8 is Encoding.UTF8 and
// Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer. ctx.write returns a Task, discarded as Java discards the future;
// ChannelFutureListener.CLOSE is a synchronous Task continuation that closes the channel on its event loop (Common's
// EventExecutorTaskScheduler), as codec-http's CorsHandler maps it; a Task has no channel, so the channel is
// ctx.Channel. printStackTrace() writes the exception to Console.Error and ctx.close() is CloseAsync().
public class SpdyServerHandler : SimpleChannelInboundHandler<object>
{
    protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IHttpRequest)
        {
            IHttpRequest req = (IHttpRequest) msg;

            if (Is100ContinueExpected(req))
            {
                _ = ctx.WriteAsync(new DefaultFullHttpResponse(HTTP_1_1, HttpResponseStatus.CONTINUE, Unpooled.EmptyBuffer));
            }

            bool keepAlive = IsKeepAlive(req);

            ByteBuf content = Unpooled.CopiedBuffer("Hello World " + DateTimeOffset.Now.ToString("ddd MMM dd HH:mm:ss zzz yyyy", CultureInfo.InvariantCulture), Encoding.UTF8);

            IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, OK, content);
            response.Headers().Set(CONTENT_TYPE, "text/plain; charset=UTF-8");
            response.Headers().SetInt(CONTENT_LENGTH, response.Content.ReadableBytes);

            if (keepAlive)
            {
                if (req.ProtocolVersion().Equals(HTTP_1_0))
                {
                    response.Headers().Set(CONNECTION, KEEP_ALIVE);
                }

                _ = ctx.WriteAsync(response);
            }
            else
            {
                // Tell the client we're going to close the connection.
                response.Headers().Set(CONNECTION, CLOSE);
                IChannel channel = ctx.Channel;
                _ = ctx.WriteAsync(response).ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
            }
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
