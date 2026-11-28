/*
 * Copyright 2020 The Netty Project
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
using System.Net.Security;
using System.Security.Authentication;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the anonymous ApplicationProtocolNegotiationHandler and ChannelInboundHandlerAdapter subclasses are named nested
// classes and AtomicBoolean/AtomicReference are the Common types. SSLContext.getDefault().createSSLEngine() with
// setUseClientMode(true) is a default SslClientAuthenticationOptions, which SslHandler(options) takes in place of a
// client-mode SSLEngine (owner decision 2026-11-24, docs/handler-ssl-design.md). SSLHandshakeException is
// AuthenticationException (ssl design decision 3) and IllegalStateException is InvalidOperationException
// (docs/codec-base-porting.md); the private Consumer interface is IConsumer (interfaces take the I prefix).
public class ApplicationProtocolNegotiationHandlerTest
{

    [Fact]
    public void TestRemoveItselfIfNoSslHandlerPresent()
    {
        IChannelHandler alpnHandler = new FailingAlpnHandler();

        // CLR: the engine is created but not used by the test (no SslHandler is added), as in Java.
        SslClientAuthenticationOptions engine = NewClientModeSSLEngine();
        // This test is mocked/simulated and doesn't go through full TLS handshake. Currently only JDK SSLEngineImpl
        // client mode will generate a close_notify.
        _ = engine;

        EmbeddedChannel channel = new EmbeddedChannel(alpnHandler);
        string msg = "msg";
        string msg2 = "msg2";

        Assert.True(channel.WriteInbound(msg));
        Assert.True(channel.WriteInbound(msg2));
        Assert.Null(channel.Pipeline.Context(alpnHandler));
        Assert.Equal(msg, channel.ReadInbound<string>());
        Assert.Equal(msg2, channel.ReadInbound<string>());

        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestHandshakeFailure()
    {
        IChannelHandler alpnHandler = new FailingAlpnHandler();

        EmbeddedChannel channel = new EmbeddedChannel(alpnHandler);
        AuthenticationException exception = new AuthenticationException("error");
        SslHandshakeCompletionEvent completionEvent = new SslHandshakeCompletionEvent(exception);
        channel.Pipeline.FireUserEventTriggered(completionEvent);
        channel.Pipeline.FireExceptionCaught(new DecoderException(exception));
        Assert.Null(channel.Pipeline.Context(alpnHandler));
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestHandshakeSuccess()
    {
        TestHandshakeSuccess0(false);
    }

    [Fact]
    public void TestHandshakeSuccessWithSslHandlerAddedLater()
    {
        TestHandshakeSuccess0(true);
    }

    private static void TestHandshakeSuccess0(bool addLater)
    {
        AtomicBoolean configureCalled = new AtomicBoolean(false);
        IChannelHandler alpnHandler = new ConfigureCalledAlpnHandler(configureCalled);

        SslClientAuthenticationOptions engine = NewClientModeSSLEngine();
        // This test is mocked/simulated and doesn't go through full TLS handshake. Currently only JDK SSLEngineImpl
        // client mode will generate a close_notify.

        EmbeddedChannel channel = new EmbeddedChannel();

        if (addLater)
        {
            channel.Pipeline.AddLast(alpnHandler);
            channel.Pipeline.AddFirst(new SslHandler(engine));
        }
        else
        {
            channel.Pipeline.AddLast(new SslHandler(engine));
            channel.Pipeline.AddLast(alpnHandler);
        }

        channel.Pipeline.FireUserEventTriggered(SslHandshakeCompletionEvent.SUCCESS);
        Assert.Null(channel.Pipeline.Context(alpnHandler));
        // Should produce the close_notify messages
        channel.ReleaseOutbound();
        _ = channel.CloseAsync();
        // CLR: SslStream writes close_notify only on an authenticated stream (ShutdownAsync: "Shuts down this
        // SslStream", https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.shutdownasync?view=net-10.0),
        // and SslHandler.EngineCloseOutbound calls it only once the handshake succeeded; this simulated handshake never
        // completed, so the close writes the empty buffer that SslHandler writes for a close without engine output
        // (measured) where the JDK client engine wrote its close_notify alert.
        AssertEmptyCloseBuffer(channel.ReadOutbound<ByteBuf>());
        channel.FinishAndReleaseAll();
        Assert.True(configureCalled.Get());
    }

    [Fact]
    public void TestHandshakeSuccessButNoSslHandler()
    {
        IChannelHandler alpnHandler = new FailingAlpnHandler();
        EmbeddedChannel channel = new EmbeddedChannel(alpnHandler);
        channel.Pipeline.FireUserEventTriggered(SslHandshakeCompletionEvent.SUCCESS);
        Assert.Null(channel.Pipeline.Context(alpnHandler));
        Assert.Throws<InvalidOperationException>(() => channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestBufferMessagesUntilHandshakeComplete()
    {
        TestBufferMessagesUntilHandshakeComplete0(null);
    }

    [Fact]
    public void TestBufferMessagesUntilHandshakeCompleteWithClose()
    {
        TestBufferMessagesUntilHandshakeComplete0(new CloseConsumer());
    }

    [Fact]
    public void TestBufferMessagesUntilHandshakeCompleteWithInputShutdown()
    {
        TestBufferMessagesUntilHandshakeComplete0(new InputShutdownConsumer());
    }

    // CLR: xUnit1024 forbids overloading a test name, so the helper testBufferMessagesUntilHandshakeComplete(Consumer)
    // ends in 0, as CipherSuiteConverterTest's helpers do.
    private void TestBufferMessagesUntilHandshakeComplete0(IConsumer<IChannelHandlerContext> pipelineConfigurator)
    {
        AtomicReference<byte[]> channelReadData = new AtomicReference<byte[]>();
        AtomicBoolean channelReadCompleteCalled = new AtomicBoolean(false);
        IChannelHandler alpnHandler = new BufferingAlpnHandler(channelReadData, channelReadCompleteCalled,
                pipelineConfigurator);

        SslClientAuthenticationOptions engine = NewClientModeSSLEngine();
        // This test is mocked/simulated and doesn't go through full TLS handshake. Currently only JDK SSLEngineImpl
        // client mode will generate a close_notify.

        byte[] someBytes = new byte[1024];

        EmbeddedChannel channel = new EmbeddedChannel(new SslHandler(engine), new SuccessBytesHandler(someBytes),
                alpnHandler);
        channel.Pipeline.FireUserEventTriggered(SslHandshakeCompletionEvent.SUCCESS);
        Assert.Null(channel.Pipeline.Context(alpnHandler));
        Assert.Equal(someBytes, channelReadData.Get());
        Assert.True(channelReadCompleteCalled.Get());
        Assert.Null(channel.ReadInbound());
        Assert.True(channel.FinishAndReleaseAll());
    }

    private interface IConsumer<T>
    {
        void Consume(T t);
    }

    // CLR-only helper: SSLContext.getDefault().createSSLEngine() followed by engine.setUseClientMode(true).
    private static SslClientAuthenticationOptions NewClientModeSSLEngine()
    {
        return new SslClientAuthenticationOptions();
    }

    // CLR-only helper: the assertCloseNotify(ByteBuf) adaptation described in TestHandshakeSuccess0.
    private static void AssertEmptyCloseBuffer(ByteBuf closeBuffer)
    {
        Assert.NotNull(closeBuffer);

        try
        {
            Assert.False(closeBuffer.IsReadable());
        }
        finally
        {
            closeBuffer.Release();
        }
    }

    private sealed class FailingAlpnHandler : ApplicationProtocolNegotiationHandler
    {
        internal FailingAlpnHandler() : base(ApplicationProtocolNames.HTTP_1_1)
        {
        }

        protected override void ConfigurePipeline(IChannelHandlerContext ctx, string protocol)
        {
            Assert.Fail();
        }
    }

    private sealed class ConfigureCalledAlpnHandler : ApplicationProtocolNegotiationHandler
    {
        private readonly AtomicBoolean configureCalled;

        internal ConfigureCalledAlpnHandler(AtomicBoolean configureCalled) : base(ApplicationProtocolNames.HTTP_1_1)
        {
            this.configureCalled = configureCalled;
        }

        protected override void ConfigurePipeline(IChannelHandlerContext ctx, string protocol)
        {
            configureCalled.Set(true);
            Assert.Equal(ApplicationProtocolNames.HTTP_1_1, protocol);
        }
    }

    private sealed class CloseConsumer : IConsumer<IChannelHandlerContext>
    {
        public void Consume(IChannelHandlerContext ctx)
        {
            _ = ctx.Channel.CloseAsync();
        }
    }

    private sealed class InputShutdownConsumer : IConsumer<IChannelHandlerContext>
    {
        public void Consume(IChannelHandlerContext ctx)
        {
            ctx.FireUserEventTriggered(ChannelInputShutdownEvent.INSTANCE);
        }
    }

    private sealed class BufferingAlpnHandler : ApplicationProtocolNegotiationHandler
    {
        private readonly AtomicReference<byte[]> channelReadData;
        private readonly AtomicBoolean channelReadCompleteCalled;
        private readonly IConsumer<IChannelHandlerContext> pipelineConfigurator;

        internal BufferingAlpnHandler(AtomicReference<byte[]> channelReadData, AtomicBoolean channelReadCompleteCalled,
                                      IConsumer<IChannelHandlerContext> pipelineConfigurator)
            : base(ApplicationProtocolNames.HTTP_1_1)
        {
            this.channelReadData = channelReadData;
            this.channelReadCompleteCalled = channelReadCompleteCalled;
            this.pipelineConfigurator = pipelineConfigurator;
        }

        protected override void ConfigurePipeline(IChannelHandlerContext ctx, string protocol)
        {
            Assert.Equal(ApplicationProtocolNames.HTTP_1_1, protocol);
            ctx.Pipeline.AddLast(new ReadRecorder(channelReadData, channelReadCompleteCalled));

            if (pipelineConfigurator != null)
            {
                pipelineConfigurator.Consume(ctx);
            }
        }
    }

    private sealed class ReadRecorder : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<byte[]> channelReadData;
        private readonly AtomicBoolean channelReadCompleteCalled;

        internal ReadRecorder(AtomicReference<byte[]> channelReadData, AtomicBoolean channelReadCompleteCalled)
        {
            this.channelReadData = channelReadData;
            this.channelReadCompleteCalled = channelReadCompleteCalled;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            channelReadData.Set((byte[]) msg);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            channelReadCompleteCalled.Set(true);
        }
    }

    private sealed class SuccessBytesHandler : ChannelInboundHandlerAdapter
    {
        private readonly byte[] someBytes;

        internal SuccessBytesHandler(byte[] someBytes)
        {
            this.someBytes = someBytes;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == SslHandshakeCompletionEvent.SUCCESS)
            {
                ctx.FireChannelRead(someBytes);
            }

            ctx.FireUserEventTriggered(evt);
        }
    }
}
