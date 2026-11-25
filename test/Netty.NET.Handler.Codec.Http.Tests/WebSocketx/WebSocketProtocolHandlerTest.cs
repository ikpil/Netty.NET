/*
 * Copyright 2018 The Netty Project
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

using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Flow;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

#pragma warning disable xUnit1051 // The source's writeAndFlush(...)/close(...) observe no test cancellation.

/**
 * Tests common, abstract class functionality in {@link WebSocketClientProtocolHandler}.
 */
// CLR: the anonymous WebSocketProtocolHandler subclasses are the named nested class ProtocolHandler (the
// package-private constructors of the abstract class are internal and reached through InternalsVisibleTo), and the
// anonymous ChannelOutboundHandlerAdapter subclasses are StallingOutboundHandler and DroppingOutboundHandler. The Java
// write(ctx, msg, promise) that keeps the promise without completing it is a WriteAsync that returns the Task of a
// TaskCompletionSource it never completes; AtomicReference<ChannelPromise> is that TaskCompletionSource kept in the
// handler (isDone() is Task.IsCompleted). ChannelFuture is Task: cause() is the InnerException of Task.Exception and
// isDone() is IsCompleted; handler.close(ctx, ctx.newPromise()) is CloseAsync(ctx, CancellationToken.None) with the
// Task discarded. channel.config().setAutoRead(false) is Config.SetAutoRead(false), Thread.sleep is Thread.Sleep,
// CharsetUtil.UTF_8 is Encoding.UTF8 and readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>().
// assertInstanceOf is Assert.IsAssignableFrom.
public class WebSocketProtocolHandlerTest
{

    private sealed class ProtocolHandler : WebSocketProtocolHandler
    {
        internal ProtocolHandler()
        {
        }

        internal ProtocolHandler(bool dropPongFrames) : base(dropPongFrames)
        {
        }

        internal ProtocolHandler(bool dropPongFrames, WebSocketCloseStatus closeStatus, long forceCloseTimeoutMillis)
            : base(dropPongFrames, closeStatus, forceCloseTimeoutMillis)
        {
        }
    }

    [Fact]
    public void TestPingFrame()
    {
        ByteBuf pingData = Unpooled.CopiedBuffer("Hello, world", Encoding.UTF8);
        EmbeddedChannel channel = new EmbeddedChannel(new ProtocolHandler());

        PingWebSocketFrame inputMessage = new PingWebSocketFrame(pingData);
        Assert.False(channel.WriteInbound(inputMessage)); // the message was not propagated inbound

        // a Pong frame was written to the channel
        PongWebSocketFrame response = channel.ReadOutbound<PongWebSocketFrame>();
        Assert.Equal(pingData, response.Content);

        pingData.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestPingPongFlowControlWhenAutoReadIsDisabled()
    {
        string text1 = "Hello, world #1";
        string text2 = "Hello, world #2";
        string text3 = "Hello, world #3";
        string text4 = "Hello, world #4";

        EmbeddedChannel channel = new EmbeddedChannel();
        channel.Config.SetAutoRead(false);
        channel.Pipeline.AddLast(new FlowControlHandler());
        channel.Pipeline.AddLast(new ProtocolHandler());

        // When
        Assert.False(channel.WriteInbound(
            new PingWebSocketFrame(Unpooled.CopiedBuffer(text1, Encoding.UTF8)),
            new TextWebSocketFrame(text2),
            new TextWebSocketFrame(text3),
            new PingWebSocketFrame(Unpooled.CopiedBuffer(text4, Encoding.UTF8))
        ));

        // Then - no messages were handled or propagated
        Assert.Null(channel.ReadInbound<object>());
        Assert.Null(channel.ReadOutbound<object>());

        // When
        channel.Read();

        // Then - pong frame was written to the outbound
        PongWebSocketFrame response1 = channel.ReadOutbound<PongWebSocketFrame>();
        Assert.Equal(text1, response1.Content.ToString(Encoding.UTF8));

        // And - one requested message was handled and propagated inbound
        TextWebSocketFrame message2 = channel.ReadInbound<TextWebSocketFrame>();
        Assert.Equal(text2, message2.Text());

        // And - no more messages were handled or propagated
        Assert.Null(channel.ReadInbound<object>());
        Assert.Null(channel.ReadOutbound<object>());

        // When
        channel.Read();

        // Then - one requested message was handled and propagated inbound
        TextWebSocketFrame message3 = channel.ReadInbound<TextWebSocketFrame>();
        Assert.Equal(text3, message3.Text());

        // And - no more messages were handled or propagated
        // Precisely, ping frame 'text4' was NOT read or handled.
        // It would be handle ONLY on the next 'channel.read()' call.
        Assert.Null(channel.ReadInbound<object>());
        Assert.Null(channel.ReadOutbound<object>());

        // Cleanup
        response1.Release();
        message2.Release();
        message3.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestPongFrameDropFrameFalse()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new ProtocolHandler(false));

        PongWebSocketFrame pingResponse = new PongWebSocketFrame();
        Assert.True(channel.WriteInbound(pingResponse));

        AssertPropagatedInbound(pingResponse, channel);

        pingResponse.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestPongFrameDropFrameTrue()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new ProtocolHandler(true));

        PongWebSocketFrame pingResponse = new PongWebSocketFrame();
        Assert.False(channel.WriteInbound(pingResponse)); // message was not propagated inbound
    }

    [Fact]
    public void TestTextFrame()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new ProtocolHandler());

        TextWebSocketFrame textFrame = new TextWebSocketFrame();
        Assert.True(channel.WriteInbound(textFrame));

        AssertPropagatedInbound(textFrame, channel);

        textFrame.Release();
        Assert.False(channel.Finish());
    }

    private sealed class StallingOutboundHandler : ChannelOutboundHandlerAdapter
    {
        internal TaskCompletionSource promise;

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            promise = new TaskCompletionSource();
            ReferenceCountUtil.Release(msg);
            return promise.Task;
        }
    }

    [Fact]
    public void TestTimeout()
    {
        StallingOutboundHandler @ref = new StallingOutboundHandler();
        WebSocketProtocolHandler handler = new ProtocolHandler(
                false, WebSocketCloseStatus.NORMAL_CLOSURE, 1);
        EmbeddedChannel channel = new EmbeddedChannel(@ref, handler);

        IChannelHandlerContext ctx = channel.Pipeline.Context(typeof(WebSocketProtocolHandler));
        Task future = channel.WriteAndFlushAsync(new CloseWebSocketFrame());
        _ = handler.CloseAsync(ctx, CancellationToken.None);

        do
        {
            Thread.Sleep(10);
            channel.RunPendingTasks();
        } while (!future.IsCompleted);

        Assert.IsAssignableFrom<WebSocketHandshakeException>(future.Exception.InnerException);
        Assert.False(@ref.promise.Task.IsCompleted);
        Assert.False(channel.Finish());
    }

    private sealed class DroppingOutboundHandler : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            // Simulate a stalled write: never complete the promise.
            ReferenceCountUtil.Release(msg);
            return new TaskCompletionSource().Task;
        }
    }

    [Fact]
    public void TestTimeoutWithoutExplicitClose()
    {
        // A bare write(CloseWebSocketFrame) is not followed by any close() call: the force-close
        // timeout itself must be the thing that closes the channel once it fires.
        WebSocketProtocolHandler handler = new ProtocolHandler(
                false, WebSocketCloseStatus.NORMAL_CLOSURE, 1);
        EmbeddedChannel channel = new EmbeddedChannel(new DroppingOutboundHandler(), handler);

        _ = channel.WriteAndFlushAsync(new CloseWebSocketFrame());

        while (channel.IsOpen)
        {
            Thread.Sleep(10);
            channel.RunPendingTasks();
        }

        Assert.False(channel.IsOpen);
        Assert.False(channel.Finish());
    }

    /**
     * Asserts that a message was propagated inbound through the channel.
     */
    private static void AssertPropagatedInbound<T>(T message, EmbeddedChannel channel) where T : WebSocketFrame
    {
        T propagatedResponse = channel.ReadInbound<T>();
        Assert.Equal(message, propagatedResponse);
    }
}
