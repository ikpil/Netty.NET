/*
 * Copyright 2017 The Netty Project
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
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Testsuite.Http2;

/**
 * HTTP handler that responds with a "Hello World"
 */
// CLR: FullHttpRequest/FullHttpResponse are IFullHttpRequest/IFullHttpResponse and ctx.alloc() is ctx.Alloc(), as in
// codec-http. ctx.write(msg) is WriteAsync, whose Task is discarded as Java discards the ChannelFuture.
// ChannelFutureListener.CLOSE on the write future is a Task continuation that closes the channel on the channel's
// executor (Common's EventExecutorTaskScheduler), as codec-http's CorsHandler maps it. cause.printStackTrace() writes
// the exception to Console.Error (Exception.ToString() holds the type, the message and the stack trace), as codec-http2's
// LastInboundHandler. The package-private constructor is internal.
public class HelloWorldHttp1Handler : SimpleChannelInboundHandler<IFullHttpRequest>
{
    private readonly string establishApproach;

    internal HelloWorldHttp1Handler(string establishApproach)
    {
        this.establishApproach = CheckNotNull(establishApproach, "establishApproach");
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest req)
    {
        if (HttpUtil.Is100ContinueExpected(req))
        {
            _ = ctx.WriteAsync(new DefaultFullHttpResponse(HTTP_1_1, CONTINUE, ctx.Alloc().Buffer(0)));
        }
        bool keepAlive = HttpUtil.IsKeepAlive(req);

        ByteBuf content = ctx.Alloc().Buffer();
        content.WriteBytes(HelloWorldHttp2Handler.RESPONSE_BYTES.Duplicate());
        ByteBufUtil.WriteAscii(content, " - via " + req.ProtocolVersion() + " (" + establishApproach + ")");

        IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, OK, content);
        response.Headers().Set(CONTENT_TYPE, "text/plain; charset=UTF-8");
        response.Headers().SetInt(CONTENT_LENGTH, response.Content.ReadableBytes);

        if (!keepAlive)
        {
            IChannel channel = ctx.Channel;
            _ = ctx.WriteAsync(response).ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
        else
        {
            response.Headers().Set(CONNECTION, HttpHeaderValues.KEEP_ALIVE);
            _ = ctx.WriteAsync(response);
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
