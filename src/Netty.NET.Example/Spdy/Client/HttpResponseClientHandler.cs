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
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Spdy.Client;

/**
 * This is a modified version of {@link HttpSnoopClientHandler} that uses a {@link BlockingQueue} to wait until an
 * HTTPResponse is received.
 */
// CLR: HttpSnoopClientHandler is Netty.NET.Example.Http.Snoop.HttpSnoopClientHandler. BlockingQueue/LinkedBlockingQueue
// of ChannelFuture is a BlockingCollection of Task (a ConcurrentQueue by default) and add() is Add();
// channel.newSucceededFuture() is Task.CompletedTask and newFailedFuture(cause) is Task.FromException(cause), since a
// Task replaces ChannelFuture (docs/transport-porting.md). HttpObject/HttpResponse/HttpContent/LastHttpContent are the
// I-prefixed codec-http interfaces, CharsetUtil.UTF_8 is Encoding.UTF8 and content() is the Content property.
// System.out.println/print/flush are Console.WriteLine/Write/Out.Flush, printStackTrace() writes the exception to
// Console.Error and ctx.close() is CloseAsync(), whose Task is discarded as Java discards the future.
public class HttpResponseClientHandler : SimpleChannelInboundHandler<IHttpObject>
{
    private readonly BlockingCollection<Task> queue = new BlockingCollection<Task>();

    protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpObject msg)
    {
        if (msg is IHttpResponse)
        {
            IHttpResponse response = (IHttpResponse) msg;

            Console.WriteLine("STATUS: " + response.Status());
            Console.WriteLine("VERSION: " + response.ProtocolVersion());
            Console.WriteLine();

            if (!response.Headers().IsEmpty())
            {
                foreach (string name in response.Headers().Names())
                {
                    foreach (string value in response.Headers().GetAll(name))
                    {
                        Console.WriteLine("HEADER: " + name + " = " + value);
                    }
                }

                Console.WriteLine();
            }

            if (HttpUtil.IsTransferEncodingChunked(response))
            {
                Console.WriteLine("CHUNKED CONTENT {");
            }
            else
            {
                Console.WriteLine("CONTENT {");
            }
        }

        if (msg is IHttpContent)
        {
            IHttpContent content = (IHttpContent) msg;

            Console.Write(content.Content.ToString(Encoding.UTF8));
            Console.Out.Flush();

            if (content is ILastHttpContent)
            {
                Console.WriteLine("} END OF CONTENT");
                queue.Add(Task.CompletedTask);
            }
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        queue.Add(Task.FromException(cause));
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }

    public BlockingCollection<Task> Queue()
    {
        return queue;
    }
}
