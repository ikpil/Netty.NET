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

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: ChannelPromise is Task : InStackFrameHandler's write(ctx, msg, promise) is WriteAsync, whose deferred
// ctx.write(msg, promise) completes a TaskCompletionSource that stands for the promise from the deferred write's
// Task (TrySetFromTask, the PromiseNotifier mapping of docs/common-task-composition.md), and
// promise.addListener(...) is a synchronous continuation of that Task; eventLoop().execute(Runnable) is
// Execute(Action), which EmbeddedChannel runs in FlushOutbound() (RunPendingTasks).
#pragma warning disable xUnit1051 // The source's write(...) observes no test cancellation.
public class HttpServerUpgradeHandlerTest
{

    private class TestUpgradeCodec : HttpServerUpgradeHandler.IUpgradeCodec
    {
        public ICollection<ICharSequence> RequiredUpgradeHeaders()
        {
            return Array.Empty<ICharSequence>();
        }

        public bool PrepareUpgradeResponse(IChannelHandlerContext ctx, IFullHttpRequest upgradeRequest,
                                              HttpHeaders upgradeHeaders)
        {
            return true;
        }

        public void UpgradeTo(IChannelHandlerContext ctx, IFullHttpRequest upgradeRequest)
        {
            // Ensure that the HttpServerUpgradeHandler is still installed when this is called
            Assert.Equal(ctx.Pipeline.Context(typeof(HttpServerUpgradeHandler)), ctx);
            Assert.NotNull(ctx.Pipeline.Get<HttpServerUpgradeHandler>());

            // Add a marker handler to signal that the upgrade has happened
            ctx.Pipeline.AddAfter(ctx.Name, "marker", new ChannelInboundHandlerAdapter());
          }
    }

    private sealed class TestUpgradeCodecFactory : HttpServerUpgradeHandler.IUpgradeCodecFactory
    {
        public HttpServerUpgradeHandler.IUpgradeCodec NewUpgradeCodec(ICharSequence protocol)
        {
            return new TestUpgradeCodec();
        }
    }

    [Fact]
    public void UpgradesPipelineInSameMethodInvocation()
    {
        HttpServerCodec httpServerCodec = new HttpServerCodec();
        HttpServerUpgradeHandler.IUpgradeCodecFactory factory = new TestUpgradeCodecFactory();

        IChannelHandler testInStackFrame = new InStackFrameHandler();

        HttpServerUpgradeHandler upgradeHandler = new HttpServerUpgradeHandler(httpServerCodec, factory);

        EmbeddedChannel channel = new EmbeddedChannel(testInStackFrame, httpServerCodec, upgradeHandler);

        string upgradeString = "GET / HTTP/1.1\r\n" +
            "Host: example.com\r\n" +
            "Connection: Upgrade, HTTP2-Settings\r\n" +
            "Upgrade: nextprotocol\r\n" +
            "HTTP2-Settings: AAMAAABkAAQAAP__\r\n\r\n";
        ByteBuf upgrade = Unpooled.CopiedBuffer(upgradeString, Encoding.ASCII);

        Assert.False(channel.WriteInbound(upgrade));
        Assert.Null(channel.Pipeline.Get<HttpServerCodec>());
        Assert.NotNull(channel.Pipeline.Get("marker"));

        channel.FlushOutbound();
        ByteBuf upgradeMessage = channel.ReadOutbound<ByteBuf>();
        string expectedHttpResponse = "HTTP/1.1 101 Switching Protocols\r\n" +
            "connection: upgrade\r\n" +
            "upgrade: nextprotocol\r\n\r\n";
        Assert.Equal(expectedHttpResponse, upgradeMessage.ToString(Encoding.ASCII));
        Assert.True(upgradeMessage.Release());
        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class InStackFrameHandler : ChannelDuplexHandler
    {
        // marker boolean to signal that we're in the `channelRead` method
        private bool inReadCall;
        private bool writeUpgradeMessage;
        private bool writeFlushed;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            Assert.False(inReadCall);
            Assert.False(writeUpgradeMessage);

            inReadCall = true;

            try
            {
                base.ChannelRead(ctx, msg);
                // All in the same call stack, the upgrade codec should receive the message,
                // written the upgrade response, and upgraded the pipeline.
                Assert.True(writeUpgradeMessage);
                Assert.False(writeFlushed);
                Assert.Null(ctx.Pipeline.Get<HttpServerCodec>());
                Assert.NotNull(ctx.Pipeline.Get("marker"));
            }
            finally
            {
                inReadCall = false;
            }
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            // We ensure that we're in the read call and defer the write so we can
            // make sure the pipeline was reformed irrespective of the flush completing.
            Assert.True(inReadCall);
            writeUpgradeMessage = true;
            TaskCompletionSource promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ctx.Channel.EventLoop.Execute(() =>
            {
                _ = ctx.WriteAsync(msg, cancellationToken).ContinueWith(t => promise.TrySetFromTask(t),
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            });
            _ = promise.Task.ContinueWith(future => writeFlushed = true, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return promise.Task;
        }
    }

    [Fact]
    public void SkippedUpgrade()
    {
        HttpServerCodec httpServerCodec = new HttpServerCodec();
        HttpServerUpgradeHandler.IUpgradeCodecFactory factory = new FailingUpgradeCodecFactory();

        HttpServerUpgradeHandler upgradeHandler = new SkippingUpgradeHandler(httpServerCodec, factory);

        EmbeddedChannel channel = new EmbeddedChannel(httpServerCodec, upgradeHandler);

        string upgradeString = "GET / HTTP/1.1\r\n" +
                               "Host: example.com\r\n" +
                               "Connection: Upgrade\r\n" +
                               "Upgrade: do-not-upgrade\r\n\r\n";
        ByteBuf upgrade = Unpooled.CopiedBuffer(upgradeString, Encoding.ASCII);

        // The upgrade request should not be passed to the next handler without any processing.
        Assert.True(channel.WriteInbound(upgrade));
        Assert.NotNull(channel.Pipeline.Get<HttpServerCodec>());
        Assert.Null(channel.Pipeline.Get("marker"));

        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.False(req is IFullHttpRequest); // Should not be aggregated.
        Assert.True(req.Headers().Contains(HttpHeaderNames.CONNECTION, Of("Upgrade"), false));
        Assert.True(req.Headers().Contains(HttpHeaderNames.UPGRADE, Of("do-not-upgrade"), false));
        Assert.IsAssignableFrom<ILastHttpContent>(channel.ReadInbound());
        Assert.Null(channel.ReadInbound());

        // No response should be written because we're just passing through.
        channel.FlushOutbound();
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class FailingUpgradeCodecFactory : HttpServerUpgradeHandler.IUpgradeCodecFactory
    {
        public HttpServerUpgradeHandler.IUpgradeCodec NewUpgradeCodec(ICharSequence protocol)
        {
            Assert.Fail("Should never be invoked");
            return null;
        }
    }

    private sealed class SkippingUpgradeHandler : HttpServerUpgradeHandler
    {
        internal SkippingUpgradeHandler(ISourceCodec sourceCodec, IUpgradeCodecFactory upgradeCodecFactory)
            : base(sourceCodec, upgradeCodecFactory)
        {
        }

        protected override bool ShouldHandleUpgradeRequest(IHttpRequest req)
        {
            return !req.Headers().Contains(HttpHeaderNames.UPGRADE, Of("do-not-upgrade"), false);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpgradeFail(bool removeAfterFirst)
    {
        HttpServerCodec httpServerCodec = new HttpServerCodec();
        HttpServerUpgradeHandler.IUpgradeCodecFactory factory = new TestUpgradeCodecFactory();

        HttpServerUpgradeHandler upgradeHandler = new HttpServerUpgradeHandler(httpServerCodec, factory, 0,
                DefaultHttpHeadersFactory.HeadersFactory(), DefaultHttpHeadersFactory.TrailersFactory(),
                removeAfterFirst);

        EmbeddedChannel channel = new EmbeddedChannel(httpServerCodec, upgradeHandler);

        // Build a h2c upgrade request, but without connection header.
        string upgradeString = "GET / HTTP/1.1\r\n" +
                               "Host: example.com\r\n" +
                               "Upgrade: h2c\r\n\r\n";
        ByteBuf upgrade = Unpooled.CopiedBuffer(upgradeString, Encoding.ASCII);

        Assert.True(channel.WriteInbound(upgrade));
        Assert.NotNull(channel.Pipeline.Get<HttpServerCodec>());

        if (removeAfterFirst)
        {
            Assert.Null(channel.Pipeline.Get<HttpServerUpgradeHandler>()); // Should be removed.
        }
        else
        {
            Assert.NotNull(channel.Pipeline.Get<HttpServerUpgradeHandler>()); // Should not be removed.
        }

        Assert.Null(channel.Pipeline.Get("marker"));

        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpVersion.HTTP_1_1, req.ProtocolVersion());
        Assert.True(req.Headers().Contains(HttpHeaderNames.UPGRADE, Of("h2c"), false));
        Assert.False(req.Headers().Contains(HttpHeaderNames.CONNECTION));
        ReferenceCountUtil.Release(req);
        Assert.Null(channel.ReadInbound());

        // No response should be written because we're just passing through.
        channel.FlushOutbound();
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void UpgradeExpect()
    {
        HttpServerCodec httpServerCodec = new HttpServerCodec();
        HttpServerUpgradeHandler.IUpgradeCodecFactory factory = new TestUpgradeCodecFactory();

        HttpServerUpgradeHandler upgradeHandler = new HttpServerUpgradeHandler(httpServerCodec, factory);

        EmbeddedChannel channel = new EmbeddedChannel(httpServerCodec, upgradeHandler);

        // Build a h2c upgrade request, but without connection header.
        string upgradeString = "GET / HTTP/1.1\r\n" +
                "Expect: foo\r\n" +
                "Upgrade: h2c\r\n" +
                "\r\n" +
                "GET / HTTP/1.1\r\n" +
                "Content-Length: 0\r\n" +
                "\r\n";
        ByteBuf upgrade = Unpooled.CopiedBuffer(upgradeString, Encoding.ASCII);

        Assert.True(channel.WriteInbound(upgrade));
        channel.CheckException();

        Assert.True(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void UpgradePrematureClose()
    {
        HttpServerCodec httpServerCodec = new HttpServerCodec();
        HttpServerUpgradeHandler.IUpgradeCodecFactory factory = new TestUpgradeCodecFactory();

        HttpServerUpgradeHandler upgradeHandler = new HttpServerUpgradeHandler(httpServerCodec, factory);

        EmbeddedChannel channel = new EmbeddedChannel(httpServerCodec, upgradeHandler);

        channel.WriteInbound(Unpooled.CopiedBuffer("POST / HTTP/1.1\n" +
                "Upgrade:\n" +
                "Expect:\n" +
                "Content-Length: 8\n" +
                "\n" +
                "GET / HTTP/1.1\n", Encoding.ASCII));
        Assert.True(channel.FinishAndReleaseAll());
    }
}
