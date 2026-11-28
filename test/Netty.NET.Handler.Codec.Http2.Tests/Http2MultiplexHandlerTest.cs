/*
 * Copyright 2019 The Netty Project
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
using System.Security.Authentication;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using State = Netty.NET.Handler.Codec.Http2.IHttp2Stream.State;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Unit tests for {@link Http2MultiplexHandler}.
 */
// CLR: the inherited tests of the abstract Http2MultiplexTest<C> run for this class (owner rule 9).
// new Http2FrameCodecBuilder(true) is the internal constructor and frameWriter(...) the internal FrameWriter(...) that
// the package's tests use. RuntimeException is Exception (docs/codec-base-porting.md, Shared CLR adaptations,
// Exceptions; new RuntimeException(cause) keeps Java's message cause.toString() through Common's
// ThrowableUtil.ThrowableToString, docs/codec-http-porting.md, Shared CLR adaptations) and SSLException is
// AuthenticationException (docs/handler-porting.md, JSSE and JCA on the BCL); assertThrows is Assert.ThrowsAny
// (docs/codec-http-porting.md, Core test notes). channel.parent() is the Parent property, parent.close() discards the
// Task, open().syncUninterruptibly().getNow() is an await of OpenAsync() (the EmbeddedChannel completes it on the
// calling thread) and closeFuture().isDone() is CloseFuture.IsCompleted. The anonymous classes are private nested
// classes; the two exception filters share one.
public class Http2MultiplexHandlerTest : Http2MultiplexTest<Http2FrameCodec>
{

    protected override Http2FrameCodec NewCodec(TestChannelInitializer childChannelInitializer,
                                                IHttp2FrameWriter frameWriter)
    {
        return new Http2FrameCodecBuilder(true).FrameWriter(frameWriter).Build();
    }

    protected override IChannelHandler NewMultiplexer(TestChannelInitializer childChannelInitializer)
    {
        return new Http2MultiplexHandler(childChannelInitializer, null);
    }

    protected override bool UseUserEventForResetFrame()
    {
        return true;
    }

    protected override bool IgnoreWindowUpdateFrames()
    {
        return true;
    }

    protected override bool UseUserEventForPriorityFrame()
    {
        return true;
    }

    [Fact]
    public void SslExceptionTriggersChildChannelException()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel channel = NewInboundStream(3, false, inboundHandler);
        Assert.True(channel.IsActive);
        AuthenticationException sslException = new AuthenticationException("foo");
        Exception testExc = new Exception(ThrowableUtil.ThrowableToString(sslException), sslException);
        channel.Parent.Pipeline.AddLast(new TestExceptionFilter(testExc, false));
        channel.Parent.Pipeline.FireExceptionCaught(testExc);

        Assert.True(channel.IsActive);
        Exception exc = Assert.ThrowsAny<Exception>(() => inboundHandler.CheckException());
        Assert.Equal(testExc, exc);
    }

    [Fact]
    public void CustomExceptionForwarding()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel channel = NewInboundStream(3, false, inboundHandler);
        Assert.True(channel.IsActive);
        Exception testExc = new Exception("xyz");
        channel.Parent.Pipeline.AddLast(new TestExceptionFilter(testExc, true));
        channel.Parent.Pipeline.FireExceptionCaught(testExc);

        Assert.True(channel.IsActive);
        Exception exc = Assert.ThrowsAny<Exception>(() => inboundHandler.CheckException());
        Assert.Equal(testExc, exc);
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of sslExceptionTriggersChildChannelException (forward false)
    // and of customExceptionForwarding (forward true, which fires an Http2MultiplexActiveStreamsException).
    private sealed class TestExceptionFilter : ChannelInboundHandlerAdapter
    {
        private readonly Exception testExc;
        private readonly bool forward;

        internal TestExceptionFilter(Exception testExc, bool forward)
        {
            this.testExc = testExc;
            this.forward = forward;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause != testExc)
            {
                base.ExceptionCaught(ctx, cause);
            }
            else if (forward)
            {
                ctx.Pipeline.FireExceptionCaught(new Http2MultiplexActiveStreamsException(cause));
            }
        }
    }

    [Fact]
    public async Task IdleOutboundStreamClosedWhenParentCloses()
    {
        EmbeddedChannel parent = new EmbeddedChannel(Http2FrameCodecBuilder.ForClient().Build(),
                new Http2MultiplexHandler(new ChannelInboundHandlerAdapter()));

        try
        {
            AtomicInteger inactive = new AtomicInteger();
            IHttp2StreamChannel childChannel = await new Http2StreamChannelBootstrap(parent)
                    .Handler(new InactiveCounter(inactive)).OpenAsync();
            // No HEADERS frame was written yet, so the stream is not known to the connection.
            Assert.True(childChannel.IsActive);
            Assert.Equal(State.IDLE, childChannel.Stream().State());

            _ = parent.CloseAsync(TestContext.Current.CancellationToken);
            parent.RunPendingTasks();

            Assert.False(childChannel.IsOpen);
            Assert.True(childChannel.CloseFuture.IsCompleted);
            Assert.Equal(1, inactive.Get());
        }
        finally
        {
            parent.FinishAndReleaseAll();
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of idleOutboundStreamClosedWhenParentCloses.
    private sealed class InactiveCounter : ChannelInboundHandlerAdapter
    {
        private readonly AtomicInteger inactive;

        internal InactiveCounter(AtomicInteger inactive)
        {
            this.inactive = inactive;
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            inactive.IncrementAndGet();
            ctx.FireChannelInactive();
        }
    }
}
