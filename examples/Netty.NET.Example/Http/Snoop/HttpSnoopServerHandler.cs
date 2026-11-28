/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.Cookie;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Example.Http.Snoop;

// CLR: HttpRequest/HttpObject/HttpContent/LastHttpContent/FullHttpResponse are the I-prefixed codec-http interfaces,
// Cookie is ICookie and Map.Entry<String, String> of the headers is IEntry<string, string>; Map<String, List<String>> is
// IDictionary<string, IList<string>>. StringBuilder.setLength(0) is Clear(). CharsetUtil.UTF_8 is Encoding.UTF8,
// Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer and content() is the Content property. StringBuilder.append(Throwable)
// appends Throwable.toString() (the class name, ": " and the message), not the CLR Exception.ToString() with its stack
// trace. ctx.write(...) returns a Task, discarded as Java discards the future. ChannelFutureListener.CLOSE is a
// synchronous Task continuation that closes the channel on its event loop (Common's EventExecutorTaskScheduler), as
// codec-http's CorsHandler maps it; a Task has no channel, so the channel is ctx.Channel. printStackTrace() writes the
// exception to Console.Error.
public class HttpSnoopServerHandler : SimpleChannelInboundHandler<object>
{
    private IHttpRequest request;
    /** Buffer that stores the response content */
    private readonly StringBuilder buf = new StringBuilder();

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IHttpRequest)
        {
            IHttpRequest request = this.request = (IHttpRequest) msg;

            if (HttpUtil.Is100ContinueExpected(request))
            {
                Send100Continue(ctx);
            }

            buf.Clear();
            buf.Append("WELCOME TO THE WILD WILD WEB SERVER\r\n");
            buf.Append("===================================\r\n");

            buf.Append("VERSION: ").Append(request.ProtocolVersion()).Append("\r\n");
            buf.Append("HOSTNAME: ").Append(request.Headers().Get(HttpHeaderNames.HOST, "unknown")).Append("\r\n");
            buf.Append("REQUEST_URI: ").Append(request.Uri()).Append("\r\n\r\n");

            HttpHeaders headers = request.Headers();
            if (!headers.IsEmpty())
            {
                foreach (IEntry<string, string> h in headers)
                {
                    string key = h.GetKey();
                    string value = h.GetValue();
                    buf.Append("HEADER: ").Append(key).Append(" = ").Append(value).Append("\r\n");
                }

                buf.Append("\r\n");
            }

            QueryStringDecoder queryStringDecoder = new QueryStringDecoder(request.Uri());
            IDictionary<string, IList<string>> @params = queryStringDecoder.Parameters();
            if (@params.Count != 0)
            {
                foreach (KeyValuePair<string, IList<string>> p in @params)
                {
                    string key = p.Key;
                    IList<string> vals = p.Value;

                    foreach (string val in vals)
                    {
                        buf.Append("PARAM: ").Append(key).Append(" = ").Append(val).Append("\r\n");
                    }
                }

                buf.Append("\r\n");
            }

            AppendDecoderResult(buf, request);
        }

        if (msg is IHttpContent)
        {
            IHttpContent httpContent = (IHttpContent) msg;

            ByteBuf content = httpContent.Content;
            if (content.IsReadable())
            {
                buf.Append("CONTENT: ");
                buf.Append(content.ToString(Encoding.UTF8));
                buf.Append("\r\n");
                AppendDecoderResult(buf, request);
            }

            if (msg is ILastHttpContent)
            {
                buf.Append("END OF CONTENT\r\n");

                ILastHttpContent trailer = (ILastHttpContent) msg;
                if (!trailer.TrailingHeaders().IsEmpty())
                {
                    buf.Append("\r\n");

                    foreach (string name in trailer.TrailingHeaders().Names())
                    {
                        foreach (string value in trailer.TrailingHeaders().GetAll(name))
                        {
                            buf.Append("TRAILING HEADER: ");
                            buf.Append(name).Append(" = ").Append(value).Append("\r\n");
                        }
                    }

                    buf.Append("\r\n");
                }

                if (!WriteResponse(trailer, ctx))
                {
                    // If keep-alive is off, close the connection once the content is fully written.
                    IChannel channel = ctx.Channel;
                    _ = ctx.WriteAndFlushAsync(Unpooled.EmptyBuffer).ContinueWith(_ => channel.CloseAsync(),
                            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                            new EventExecutorTaskScheduler(() => channel.EventLoop));
                }
            }
        }
    }

    private static void AppendDecoderResult(StringBuilder buf, IHttpObject o)
    {
        DecoderResult result = o.DecoderResult();
        if (result.IsSuccess())
        {
            return;
        }

        buf.Append(".. WITH DECODER FAILURE: ");
        buf.Append(result.Cause().GetType().FullName + ": " + result.Cause().Message);
        buf.Append("\r\n");
    }

    private bool WriteResponse(IHttpObject currentObj, IChannelHandlerContext ctx)
    {
        // Decide whether to close the connection or not.
        bool keepAlive = HttpUtil.IsKeepAlive(request);
        // Build the response object.
        IFullHttpResponse response = new DefaultFullHttpResponse(
                HTTP_1_1, currentObj.DecoderResult().IsSuccess()? OK : BAD_REQUEST,
                Unpooled.CopiedBuffer(buf.ToString(), Encoding.UTF8));

        response.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "text/plain; charset=UTF-8");

        if (keepAlive)
        {
            // Add 'Content-Length' header only for a keep-alive connection.
            response.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, response.Content.ReadableBytes);
            // Add keep alive header as per:
            // - https://www.w3.org/Protocols/HTTP/1.1/draft-ietf-http-v11-spec-01.html#Connection
            response.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.KEEP_ALIVE);
        }

        // Encode the cookie.
        string cookieString = request.Headers().Get(HttpHeaderNames.COOKIE);
        if (cookieString != null)
        {
            ISet<ICookie> cookies = ServerCookieDecoder.STRICT.Decode(cookieString);
            if (cookies.Count != 0)
            {
                // Reset the cookies if necessary.
                foreach (ICookie cookie in cookies)
                {
                    response.Headers().Add(HttpHeaderNames.SET_COOKIE, ServerCookieEncoder.STRICT.Encode(cookie));
                }
            }
        }
        else
        {
            // Browser sent no cookie.  Add some.
            response.Headers().Add(HttpHeaderNames.SET_COOKIE, ServerCookieEncoder.STRICT.Encode("key1", "value1"));
            response.Headers().Add(HttpHeaderNames.SET_COOKIE, ServerCookieEncoder.STRICT.Encode("key2", "value2"));
        }

        // Write the response.
        _ = ctx.WriteAsync(response);

        return keepAlive;
    }

    private static void Send100Continue(IChannelHandlerContext ctx)
    {
        IFullHttpResponse response = new DefaultFullHttpResponse(HTTP_1_1, CONTINUE, Unpooled.EmptyBuffer);
        _ = ctx.WriteAsync(response);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
