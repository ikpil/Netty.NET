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

using System.Collections.Generic;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.WebSocketExtensionTestUtil;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression.PerMessageDeflateServerExtensionHandshaker;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.Compression;

// CLR: the static imports of PerMessageDeflateServerExtensionHandshaker.* and WebSocketExtensionTestUtil.* are using
// static directives. List is IList, Map.get(key) is the indexer (the key is present in every case; a missing key would
// fail with KeyNotFoundException instead of the assertion) and isEmpty() is Count == 0; pipeline().get(Class) is
// Pipeline.Get<T>(). The handshakers negotiate the window sizes only; no frame is compressed, so the 10-bit server
// window of testServerWindowSizeSuccess, which needs jzlib to encode, is never used (see DeflateEncoder).
public class WebSocketServerCompressionHandlerTest
{

    [Fact]
    public void TestNormalSuccess()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketServerCompressionHandler(0));

        IHttpRequest req = NewUpgradeRequest(PERMESSAGE_DEFLATE_EXTENSION);
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();
        IList<WebSocketExtensionData> exts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, exts[0].Name());
        Assert.True(exts[0].Parameters().Count == 0);
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateDecoder>());
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateEncoder>());
    }

    [Fact]
    public void TestClientWindowSizeSuccess()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketServerExtensionHandler(
                new PerMessageDeflateServerExtensionHandshaker(6, false, 10, false, false, 0)));

        IHttpRequest req = NewUpgradeRequest(PERMESSAGE_DEFLATE_EXTENSION + "; " + CLIENT_MAX_WINDOW);
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();
        IList<WebSocketExtensionData> exts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, exts[0].Name());
        Assert.Equal("10", exts[0].Parameters()[CLIENT_MAX_WINDOW]);
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateDecoder>());
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateEncoder>());
    }

    [Fact]
    public void TestClientWindowSizeUnavailable()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketServerExtensionHandler(
                new PerMessageDeflateServerExtensionHandshaker(6, false, 10, false, false, 0)));

        IHttpRequest req = NewUpgradeRequest(PERMESSAGE_DEFLATE_EXTENSION);
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();
        IList<WebSocketExtensionData> exts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, exts[0].Name());
        Assert.True(exts[0].Parameters().Count == 0);
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateDecoder>());
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateEncoder>());
    }

    [Fact]
    public void TestServerWindowSizeSuccess()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketServerExtensionHandler(
                new PerMessageDeflateServerExtensionHandshaker(6, true, 15, false, false, 0)));

        IHttpRequest req = NewUpgradeRequest(PERMESSAGE_DEFLATE_EXTENSION + "; " + SERVER_MAX_WINDOW + "=10");
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();
        IList<WebSocketExtensionData> exts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, exts[0].Name());
        Assert.Equal("10", exts[0].Parameters()[SERVER_MAX_WINDOW]);
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateDecoder>());
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateEncoder>());
    }

    [Fact]
    public void TestServerWindowSizeDisable()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketServerExtensionHandler(
                new PerMessageDeflateServerExtensionHandshaker(6, false, 15, false, false, 0)));

        IHttpRequest req = NewUpgradeRequest(PERMESSAGE_DEFLATE_EXTENSION + "; " + SERVER_MAX_WINDOW + "=10");
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();

        Assert.False(res2.Headers().Contains(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));
        Assert.Null(ch.Pipeline.Get<PerMessageDeflateDecoder>());
        Assert.Null(ch.Pipeline.Get<PerMessageDeflateEncoder>());
    }

    [Fact]
    public void TestServerNoContext()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketServerCompressionHandler(0));

        IHttpRequest req = NewUpgradeRequest(PERMESSAGE_DEFLATE_EXTENSION + "; " + SERVER_NO_CONTEXT);
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();

        Assert.False(res2.Headers().Contains(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));
        Assert.Null(ch.Pipeline.Get<PerMessageDeflateDecoder>());
        Assert.Null(ch.Pipeline.Get<PerMessageDeflateEncoder>());
    }

    [Fact]
    public void TestClientNoContext()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketServerCompressionHandler(0));

        IHttpRequest req = NewUpgradeRequest(PERMESSAGE_DEFLATE_EXTENSION + "; " + CLIENT_NO_CONTEXT);
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();
        IList<WebSocketExtensionData> exts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, exts[0].Name());
        Assert.True(exts[0].Parameters().Count == 0);
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateDecoder>());
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateEncoder>());
    }

    [Fact]
    public void TestServerWindowSizeDisableThenFallback()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketServerExtensionHandler(
                new PerMessageDeflateServerExtensionHandshaker(6, false, 15, false, false, 0)));

        IHttpRequest req = NewUpgradeRequest(PERMESSAGE_DEFLATE_EXTENSION + "; " + SERVER_MAX_WINDOW + "=10, " +
                PERMESSAGE_DEFLATE_EXTENSION);
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();
        IList<WebSocketExtensionData> exts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, exts[0].Name());
        Assert.True(exts[0].Parameters().Count == 0);
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateDecoder>());
        Assert.NotNull(ch.Pipeline.Get<PerMessageDeflateEncoder>());
    }

}
