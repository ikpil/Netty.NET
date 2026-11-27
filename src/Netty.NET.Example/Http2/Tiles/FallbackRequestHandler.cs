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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Example.Http2.Tiles;

/**
 * Handles the exceptional case where HTTP 1.x was negotiated under TLS.
 */
// CLR: HttpRequest/FullHttpResponse are IHttpRequest/IFullHttpResponse, ctx.alloc() is ctx.Alloc() and
// Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer; UTF_8 is Encoding.UTF8. The static field and the local share the name
// "response"; the scope of a C# local is its whole block, so the field read before the local's declaration is qualified
// with the class name. ctx.write(msg) is WriteAsync, whose Task is discarded; ChannelFutureListener.CLOSE on the write future is
// a continuation of the write Task that closes ctx.Channel (a Task has no channel), as codec-base's MessageAggregator
// maps it. cause.printStackTrace() writes the exception to Console.Error.
public sealed class FallbackRequestHandler : SimpleChannelInboundHandler<IHttpRequest>
{

    private static readonly ByteBuf response = UnreleasableBuffer(CopiedBuffer("<!DOCTYPE html>"
            + "<html><body><h2>To view the example you need a browser that supports HTTP/2 ("
            + Http2CodecUtil.TLS_UPGRADE_PROTOCOL_NAME
            + ")</h2></body></html>", Encoding.UTF8)).AsReadOnly();

    protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpRequest req)
    {
        if (HttpUtil.Is100ContinueExpected(req))
        {
            _ = ctx.WriteAsync(new DefaultFullHttpResponse(HTTP_1_1, CONTINUE, EmptyBuffer));
        }

        ByteBuf content = ctx.Alloc().Buffer();
        content.WriteBytes(FallbackRequestHandler.response.Duplicate());

        IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, OK, content);
        response.Headers().Set(CONTENT_TYPE, "text/html; charset=UTF-8");
        response.Headers().SetInt(CONTENT_LENGTH, response.Content.ReadableBytes);

        _ = ctx.WriteAsync(response).ContinueWith(_ => ctx.Channel.CloseAsync(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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
