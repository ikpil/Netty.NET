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

namespace Netty.NET.Example.Http.Upload;

/**
 * Handler that just dumps the contents of the response from the server
 */
// CLR: HttpObject/HttpResponse/HttpContent/LastHttpContent are the I-prefixed codec-http interfaces; names() is a set of
// strings and getAll(name) a list of strings. CharsetUtil.UTF_8 is Encoding.UTF8 and content() is the Content property.
// System.err.println is Console.Error.WriteLine, printStackTrace() writes the exception to Console.Error and
// ctx.channel().close() is CloseAsync(), whose Task is discarded as Java discards the future.
public class HttpUploadClientHandler : SimpleChannelInboundHandler<IHttpObject>
{
    private bool readingChunks;

    protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpObject msg)
    {
        if (msg is IHttpResponse)
        {
            IHttpResponse response = (IHttpResponse) msg;

            Console.Error.WriteLine("STATUS: " + response.Status());
            Console.Error.WriteLine("VERSION: " + response.ProtocolVersion());

            if (!response.Headers().IsEmpty())
            {
                foreach (string name in response.Headers().Names())
                {
                    foreach (string value in response.Headers().GetAll(name))
                    {
                        Console.Error.WriteLine("HEADER: " + name + " = " + value);
                    }
                }
            }

            if (response.Status().Code() == 200 && HttpUtil.IsTransferEncodingChunked(response))
            {
                readingChunks = true;
                Console.Error.WriteLine("CHUNKED CONTENT {");
            }
            else
            {
                Console.Error.WriteLine("CONTENT {");
            }
        }

        if (msg is IHttpContent)
        {
            IHttpContent chunk = (IHttpContent) msg;
            Console.Error.WriteLine(chunk.Content.ToString(Encoding.UTF8));

            if (chunk is ILastHttpContent)
            {
                if (readingChunks)
                {
                    Console.Error.WriteLine("} END OF CHUNKED CONTENT");
                }
                else
                {
                    Console.Error.WriteLine("} END OF CONTENT");
                }

                readingChunks = false;
            }
            else
            {
                Console.Error.WriteLine(chunk.Content.ToString(Encoding.UTF8));
            }
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.Channel.CloseAsync();
    }
}
