/*
 * Copyright 2016 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

#pragma warning disable xUnit1051 // The source's writeAndFlush(...) observes no test cancellation.

// CLR: the nested interfaces are HttpClientUpgradeHandler.ISourceCodec/IUpgradeCodec (owner rule: I prefix); the
// anonymous ChannelInboundHandlerAdapter is the named nested class UpgradeSuccessfulWriter. ChannelPromise is Task
// (docs/transport-porting.md): channel.newPromise() plus ctx.writeAndFlush(msg, promise) is the Task that
// ctx.WriteAndFlushAsync(msg) returns, kept in a field of the writer, and promise.isSuccess() is
// IsCompletedSuccessfully. The String values that Java passes as CharSequence (protocol(),
// headers().contains(AsciiString, String, boolean)) are wrapped in StringCharSequence (HttpHeadersTestUtils.Of),
// Collections.emptyList() is Array.Empty, assertThrows with an Executable or lambda is Assert.Throws and
// IllegalStateException is InvalidOperationException (docs/codec-base-porting.md). Messages are read with
// ReadInbound<T>/ReadOutbound<T>.
public class HttpClientUpgradeHandlerTest
{

    private sealed class FakeSourceCodec : HttpClientUpgradeHandler.ISourceCodec
    {
        public void PrepareUpgradeFrom(IChannelHandlerContext ctx)
        {
        }

        public void UpgradeFrom(IChannelHandlerContext ctx)
        {
        }
    }

    private sealed class FakeUpgradeCodec : HttpClientUpgradeHandler.IUpgradeCodec
    {
        public ICharSequence Protocol()
        {
            return Of("fancyhttp");
        }

        public ICollection<ICharSequence> SetUpgradeHeaders(IChannelHandlerContext ctx, IHttpRequest upgradeRequest)
        {
            return Array.Empty<ICharSequence>();
        }

        public void UpgradeTo(IChannelHandlerContext ctx, IFullHttpResponse upgradeResponse)
        {
        }
    }

    private sealed class UserEventCatcher : ChannelInboundHandlerAdapter
    {
        private object evt;

        public object GetUserEvent()
        {
            return evt;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            this.evt = evt;
        }
    }

    [Fact]
    public void TestSuccessfulUpgrade()
    {
        HttpClientUpgradeHandler.ISourceCodec sourceCodec = new FakeSourceCodec();
        HttpClientUpgradeHandler.IUpgradeCodec upgradeCodec = new FakeUpgradeCodec();
        HttpClientUpgradeHandler handler = new HttpClientUpgradeHandler(sourceCodec, upgradeCodec, 1024);
        UserEventCatcher catcher = new UserEventCatcher();
        EmbeddedChannel channel = new EmbeddedChannel(catcher);
        IHttpRequest afterUpgradeMessage =
                new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "netty.io");
        UpgradeSuccessfulWriter writer = new UpgradeSuccessfulWriter(afterUpgradeMessage);
        channel.Pipeline.AddFirst(writer);

        channel.Pipeline.AddFirst("upgrade", handler);

        Assert.True(
            channel.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "netty.io")));
        IFullHttpRequest request = channel.ReadOutbound<IFullHttpRequest>();

        Assert.Equal(2, request.Headers().Size());
        Assert.True(request.Headers().Contains(HttpHeaderNames.UPGRADE, Of("fancyhttp"), false));
        Assert.True(request.Headers().Contains("connection", "upgrade", false));
        Assert.True(request.Release());
        Assert.Equal(HttpClientUpgradeHandler.UpgradeEvent.UPGRADE_ISSUED, catcher.GetUserEvent());

        IHttpResponse upgradeResponse =
            new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.SWITCHING_PROTOCOLS);

        upgradeResponse.Headers().Add(HttpHeaderNames.UPGRADE, "fancyhttp");
        Assert.False(channel.WriteInbound(upgradeResponse));
        Assert.False(channel.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        Assert.Equal(HttpClientUpgradeHandler.UpgradeEvent.UPGRADE_SUCCESSFUL, catcher.GetUserEvent());
        Assert.Null(channel.Pipeline.Get("upgrade"));

        Assert.True(channel.WriteInbound(new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK)));
        IFullHttpResponse response = channel.ReadInbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.OK, response.Status());
        Assert.True(response.Release());

        Assert.True(writer.promise.IsCompletedSuccessfully);
        Assert.Equal(afterUpgradeMessage, channel.ReadOutbound());

        Assert.False(channel.Finish());
    }

    private sealed class UpgradeSuccessfulWriter : ChannelInboundHandlerAdapter
    {
        private readonly IHttpRequest afterUpgradeMessage;
        internal Task promise;

        internal UpgradeSuccessfulWriter(IHttpRequest afterUpgradeMessage)
        {
            this.afterUpgradeMessage = afterUpgradeMessage;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is HttpClientUpgradeHandler.UpgradeEvent upgradeEvent &&
                    upgradeEvent == HttpClientUpgradeHandler.UpgradeEvent.UPGRADE_SUCCESSFUL)
            {
                promise = ctx.WriteAndFlushAsync(afterUpgradeMessage);
            }

            base.UserEventTriggered(ctx, evt);
        }
    }

    [Fact]
    public void TestUpgradeRejected()
    {
        HttpClientUpgradeHandler.ISourceCodec sourceCodec = new FakeSourceCodec();
        HttpClientUpgradeHandler.IUpgradeCodec upgradeCodec = new FakeUpgradeCodec();
        HttpClientUpgradeHandler handler = new HttpClientUpgradeHandler(sourceCodec, upgradeCodec, 1024);
        UserEventCatcher catcher = new UserEventCatcher();
        EmbeddedChannel channel = new EmbeddedChannel(catcher);
        channel.Pipeline.AddFirst("upgrade", handler);

        Assert.True(
            channel.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "netty.io")));
        IFullHttpRequest request = channel.ReadOutbound<IFullHttpRequest>();

        Assert.Equal(2, request.Headers().Size());
        Assert.True(request.Headers().Contains(HttpHeaderNames.UPGRADE, Of("fancyhttp"), false));
        Assert.True(request.Headers().Contains("connection", "upgrade", false));
        Assert.True(request.Release());
        Assert.Equal(HttpClientUpgradeHandler.UpgradeEvent.UPGRADE_ISSUED, catcher.GetUserEvent());

        IHttpResponse upgradeResponse =
            new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.SWITCHING_PROTOCOLS);
        upgradeResponse.Headers().Add(HttpHeaderNames.UPGRADE, "fancyhttp");
        Assert.True(channel.WriteInbound(new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK)));
        Assert.True(channel.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        Assert.Equal(HttpClientUpgradeHandler.UpgradeEvent.UPGRADE_REJECTED, catcher.GetUserEvent());
        Assert.Null(channel.Pipeline.Get("upgrade"));

        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.Equal(HttpResponseStatus.OK, response.Status());

        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(ILastHttpContent.EMPTY_LAST_CONTENT, last);
        Assert.False(last.Release());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestEarlyBailout()
    {
        HttpClientUpgradeHandler.ISourceCodec sourceCodec = new FakeSourceCodec();
        HttpClientUpgradeHandler.IUpgradeCodec upgradeCodec = new FakeUpgradeCodec();
        HttpClientUpgradeHandler handler = new HttpClientUpgradeHandler(sourceCodec, upgradeCodec, 1024);
        UserEventCatcher catcher = new UserEventCatcher();
        EmbeddedChannel channel = new EmbeddedChannel(catcher);
        channel.Pipeline.AddFirst("upgrade", handler);

        Assert.True(
            channel.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "netty.io")));
        IFullHttpRequest request = channel.ReadOutbound<IFullHttpRequest>();

        Assert.Equal(2, request.Headers().Size());
        Assert.True(request.Headers().Contains(HttpHeaderNames.UPGRADE, Of("fancyhttp"), false));
        Assert.True(request.Headers().Contains("connection", "upgrade", false));
        Assert.True(request.Release());
        Assert.Equal(HttpClientUpgradeHandler.UpgradeEvent.UPGRADE_ISSUED, catcher.GetUserEvent());

        IHttpResponse upgradeResponse =
            new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.SWITCHING_PROTOCOLS);
        upgradeResponse.Headers().Add(HttpHeaderNames.UPGRADE, "fancyhttp");
        Assert.True(channel.WriteInbound(new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK)));

        Assert.Equal(HttpClientUpgradeHandler.UpgradeEvent.UPGRADE_REJECTED, catcher.GetUserEvent());
        Assert.Null(channel.Pipeline.Get("upgrade"));

        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.Equal(HttpResponseStatus.OK, response.Status());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void DontStripConnectionHeaders()
    {
        HttpClientUpgradeHandler.ISourceCodec sourceCodec = new FakeSourceCodec();
        HttpClientUpgradeHandler.IUpgradeCodec upgradeCodec = new FakeUpgradeCodec();
        HttpClientUpgradeHandler handler = new HttpClientUpgradeHandler(sourceCodec, upgradeCodec, 1024);
        UserEventCatcher catcher = new UserEventCatcher();
        EmbeddedChannel channel = new EmbeddedChannel(catcher);
        channel.Pipeline.AddFirst("upgrade", handler);

        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "netty.io");
        request.Headers().Add("connection", "extra");
        request.Headers().Add("extra", "value");
        Assert.True(channel.WriteOutbound(request));
        IFullHttpRequest readRequest = channel.ReadOutbound<IFullHttpRequest>();

        IList<string> connectionHeaders = readRequest.Headers().GetAll("connection");
        Assert.True(connectionHeaders.Contains("extra"));
        Assert.True(readRequest.Release());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestMultipleUpgradeRequestsFail()
    {
        HttpClientUpgradeHandler.ISourceCodec sourceCodec = new FakeSourceCodec();
        HttpClientUpgradeHandler.IUpgradeCodec upgradeCodec = new FakeUpgradeCodec();
        HttpClientUpgradeHandler handler = new HttpClientUpgradeHandler(sourceCodec, upgradeCodec, 1024);
        EmbeddedChannel channel = new EmbeddedChannel();
        channel.Pipeline.AddFirst("upgrade", handler);

        Assert.True(
                channel.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "netty.io")));
        IFullHttpRequest request = channel.ReadOutbound<IFullHttpRequest>();
        Assert.True(request.Release());

        IFullHttpRequest secondReq = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "netty.io");
        Assert.Throws<InvalidOperationException>(() => channel.WriteOutbound(secondReq));

        Assert.Equal(0, secondReq.RefCnt);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void ForwardOnFailure()
    {
        HttpClientUpgradeHandler.ISourceCodec sourceCodec = new FakeSourceCodec();
        HttpClientUpgradeHandler.IUpgradeCodec upgradeCodec = new FakeUpgradeCodec();
        HttpClientUpgradeHandler handler = new HttpClientUpgradeHandler(sourceCodec, upgradeCodec, 1024);
        EmbeddedChannel channel = new EmbeddedChannel();
        channel.Pipeline.AddFirst("upgrade", handler);

        Assert.True(channel.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "netty.io")));
        IFullHttpRequest request = channel.ReadOutbound<IFullHttpRequest>();
        Assert.True(request.Release());

        DefaultHttpResponse response = new DefaultHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.SWITCHING_PROTOCOLS);
        response.Headers().Add(HttpHeaderNames.UPGRADE, "");
        Assert.False(channel.WriteInbound(response));
        Assert.Throws<InvalidOperationException>(() => channel.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        IFullHttpResponse full = channel.ReadInbound<IFullHttpResponse>();
        Assert.True(full.Release());

        Assert.False(channel.Finish());
    }
}
