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
//The MIT License
//
//Copyright (c) 2009 Carl Bystršm
//
//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:
//
//The above copyright notice and this permission notice shall be included in
//all copies or substantial portions of the Software.
//
//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//THE SOFTWARE.
using System;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Http.WebSocketx.Client;

// CLR: ChannelPromise is TaskCompletionSource and ChannelFuture is Task : handshakeFuture() returns the promise's
// Task, setSuccess/setFailure are SetResult/SetException and isDone() is Task.IsCompleted. ctx.newPromise() is a
// TaskCompletionSource created with RunContinuationsAsynchronously: Java's main thread blocks in
// handshakeFuture().sync() on its own thread, and the C# Main awaits the Task, so its continuation (which then
// blocks reading the console) must not run inline on the event loop that completes the promise. The handshaker
// methods are HandshakeAsync and FinishHandshake; the Task of HandshakeAsync is discarded as Java discards the
// future. FullHttpResponse is IFullHttpResponse, content() is the Content property, CharsetUtil.UTF_8 is
// Encoding.UTF8 and IllegalStateException is InvalidOperationException. System.out.println is Console.WriteLine,
// printStackTrace() writes the exception to Console.Error and ctx.close()/ch.close() are CloseAsync(), whose Task
// is discarded.
public class WebSocketClientHandler : SimpleChannelInboundHandler<object>
{
    private readonly WebSocketClientHandshaker handshaker;
    private TaskCompletionSource handshakeFuture;

    public WebSocketClientHandler(WebSocketClientHandshaker handshaker)
    {
        this.handshaker = handshaker;
    }

    public Task HandshakeFuture()
    {
        return handshakeFuture.Task;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        handshakeFuture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        _ = handshaker.HandshakeAsync(ctx.Channel);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        Console.WriteLine("WebSocket Client disconnected!");
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
    {
        IChannel ch = ctx.Channel;

        if (!handshaker.IsHandshakeComplete())
        {
            try
            {
                handshaker.FinishHandshake(ch, (IFullHttpResponse) msg);
                Console.WriteLine("WebSocket Client connected!");
                handshakeFuture.SetResult();
            }
            catch (WebSocketHandshakeException e)
            {
                Console.WriteLine("WebSocket Client failed to connect");
                handshakeFuture.SetException(e);
            }

            return;
        }

        if (msg is IFullHttpResponse)
        {
            IFullHttpResponse response = (IFullHttpResponse) msg;
            throw new InvalidOperationException(
                    "Unexpected FullHttpResponse (getStatus=" + response.Status() +
                            ", content=" + response.Content.ToString(Encoding.UTF8) + ')');
        }

        WebSocketFrame frame = (WebSocketFrame) msg;
        if (frame is TextWebSocketFrame)
        {
            TextWebSocketFrame textFrame = (TextWebSocketFrame) frame;
            Console.WriteLine("WebSocket Client received message: " + textFrame.Text());
        }
        else if (frame is PongWebSocketFrame)
        {
            Console.WriteLine("WebSocket Client received pong");
        }
        else if (frame is CloseWebSocketFrame)
        {
            Console.WriteLine("WebSocket Client received closing");
            _ = ch.CloseAsync();
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);

        if (!handshakeFuture.Task.IsCompleted)
        {
            handshakeFuture.SetException(cause);
        }

        _ = ctx.CloseAsync();
    }
}
