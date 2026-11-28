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
using System.Text;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Http.Snoop;

// CLR: HttpObject/HttpResponse/HttpContent/LastHttpContent are the I-prefixed codec-http interfaces.
// System.err.println/print/flush are Console.Error.WriteLine/Write/Flush, printStackTrace() writes the exception
// to Console.Error and ctx.close() is CloseAsync(), whose Task is discarded as Java discards the future.
public class HttpSnoopClientHandler : SimpleChannelInboundHandler<IHttpObject>
{
    protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpObject msg)
    {
        if (msg is IHttpResponse)
        {
            IHttpResponse response = (IHttpResponse) msg;

            Console.Error.WriteLine("STATUS: " + response.Status());
            Console.Error.WriteLine("VERSION: " + response.ProtocolVersion());
            Console.Error.WriteLine();

            if (!response.Headers().IsEmpty())
            {
                foreach (string name in response.Headers().Names())
                {
                    foreach (string value in response.Headers().GetAll(name))
                    {
                        Console.Error.WriteLine("HEADER: " + name + " = " + value);
                    }
                }

                Console.Error.WriteLine();
            }

            if (HttpUtil.IsTransferEncodingChunked(response))
            {
                Console.Error.WriteLine("CHUNKED CONTENT {");
            }
            else
            {
                Console.Error.WriteLine("CONTENT {");
            }
        }

        if (msg is IHttpContent)
        {
            IHttpContent content = (IHttpContent) msg;

            Console.Error.Write(content.Content.ToString(Encoding.UTF8));
            Console.Error.Flush();

            if (content is ILastHttpContent)
            {
                Console.Error.WriteLine("} END OF CONTENT");
                _ = ctx.CloseAsync();
            }
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
