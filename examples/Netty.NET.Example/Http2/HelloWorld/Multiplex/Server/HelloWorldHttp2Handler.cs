/*
 * Copyright 2016 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Example.Http2.HelloWorld.Multiplex.Server;

/**
 * A simple handler that responds with the message "Hello World!".
 */
// CLR: @Sharable is the [Sharable] attribute. Http2HeadersFrame, Http2DataFrame and Http2Headers take the I prefix, as
// in codec-http2. CharsetUtil.UTF_8 is Encoding.UTF8, data.content() is the Content property and data.release()
// Release(). ctx.write(msg) is WriteAsync, whose Task is discarded as Java discards the ChannelFuture;
// cause.printStackTrace() writes the exception to Console.Error, as codec-http2's LastInboundHandler. The
// package-private static field is internal.
[Sharable]
public class HelloWorldHttp2Handler : ChannelDuplexHandler
{

    internal static readonly ByteBuf RESPONSE_BYTES = UnreleasableBuffer(
            CopiedBuffer("Hello World", Encoding.UTF8)).AsReadOnly();

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        base.ExceptionCaught(ctx, cause);
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IHttp2HeadersFrame)
        {
            OnHeadersRead(ctx, (IHttp2HeadersFrame) msg);
        }
        else if (msg is IHttp2DataFrame)
        {
            OnDataRead(ctx, (IHttp2DataFrame) msg);
        }
        else
        {
            base.ChannelRead(ctx, msg);
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    /**
     * If receive a frame with end-of-stream set, send a pre-canned response.
     */
    private static void OnDataRead(IChannelHandlerContext ctx, IHttp2DataFrame data)
    {
        if (data.IsEndStream())
        {
            SendResponse(ctx, data.Content);
        }
        else
        {
            // We do not send back the response to the remote-peer, so we need to release it.
            data.Release();
        }
    }

    /**
     * If receive a frame with end-of-stream set, send a pre-canned response.
     */
    private static void OnHeadersRead(IChannelHandlerContext ctx, IHttp2HeadersFrame headers)
    {
        if (headers.IsEndStream())
        {
            ByteBuf content = ctx.Alloc().Buffer();
            content.WriteBytes(RESPONSE_BYTES.Duplicate());
            ByteBufUtil.WriteAscii(content, " - via HTTP/2");
            SendResponse(ctx, content);
        }
    }

    /**
     * Sends a "Hello World" DATA frame to the client.
     */
    private static void SendResponse(IChannelHandlerContext ctx, ByteBuf payload)
    {
        // Send a frame for the response status
        IHttp2Headers headers = new DefaultHttp2Headers().Status(OK.CodeAsText());
        _ = ctx.WriteAsync(new DefaultHttp2HeadersFrame(headers));
        _ = ctx.WriteAsync(new DefaultHttp2DataFrame(payload, true));
    }
}
