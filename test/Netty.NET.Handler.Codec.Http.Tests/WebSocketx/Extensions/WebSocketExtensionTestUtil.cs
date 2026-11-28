/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version
 * 2.0 (the "License"); you may not use this file except in compliance with the
 * License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations under
 * the License.
 */

using System.Collections.Generic;
using Moq;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions;

// CLR: HttpRequest/HttpResponse are the core IHttpRequest/IHttpResponse; toString().toLowerCase() of the ASCII
// header value is ToLowerInvariant(). Mockito's ArgumentMatcher is the nested WebSocketExtensionDataMatcher with
// its matches method, and argThat(matcher) is Moq's Match.Create, which builds a custom matcher from a predicate
// when the helper is called inside a setup or verify expression (https://github.com/devlooped/moq/wiki/Quickstart,
// custom matchers). The static webSocketExtensionDataMatcher(String) is WebSocketExtensionDataMatcher_(string),
// because a method cannot be named like the nested WebSocketExtensionDataMatcher class (CS0102,
// https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102; owner rule, as Builder_()).
public sealed class WebSocketExtensionTestUtil
{

    public static IHttpRequest NewUpgradeRequest(string ext)
    {
        IHttpRequest req = new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "/chat");
        AddUpgradeHeaders(req, ext);
        return req;
    }

    internal static void AddUpgradeHeaders(IHttpRequest req, string ext)
    {
        req.Headers().Set(HttpHeaderNames.HOST, "server.example.com");
        req.Headers().Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET.ToString().ToLowerInvariant());
        req.Headers().Set(HttpHeaderNames.CONNECTION, "Upgrade");
        req.Headers().Set(HttpHeaderNames.ORIGIN, "http://example.com");

        if (ext != null)
        {
            req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS, ext);
        }
    }

    public static IHttpResponse NewUpgradeResponse(string ext)
    {
        IHttpResponse res = new DefaultHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.SWITCHING_PROTOCOLS);

        res.Headers().Set(HttpHeaderNames.HOST, "server.example.com");
        res.Headers().Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET.ToString().ToLowerInvariant());
        res.Headers().Set(HttpHeaderNames.CONNECTION, "Upgrade");
        res.Headers().Set(HttpHeaderNames.ORIGIN, "http://example.com");

        if (ext != null)
        {
            res.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS, ext);
        }

        return res;
    }

    internal sealed class WebSocketExtensionDataMatcher
    {

        private readonly string name;

        internal WebSocketExtensionDataMatcher(string name)
        {
            this.name = name;
        }

        public bool Matches(WebSocketExtensionData data)
        {
            return data != null && name.Equals(data.Name());
        }
    }

    internal static WebSocketExtensionData WebSocketExtensionDataMatcher_(string text)
    {
        return Match.Create<WebSocketExtensionData>(new WebSocketExtensionDataMatcher(text).Matches);
    }

    private WebSocketExtensionTestUtil()
    {
        // unused
    }

    internal class DummyEncoder : WebSocketExtensionEncoder
    {
        protected override void Encode(IChannelHandlerContext ctx, WebSocketFrame msg,
                IList<object> @out)
        {
            // unused
        }
    }

    internal class DummyDecoder : WebSocketExtensionDecoder
    {
        protected override void Decode(IChannelHandlerContext ctx, WebSocketFrame msg,
                IList<object> @out)
        {
            // unused
        }
    }

    internal class Dummy2Encoder : WebSocketExtensionEncoder
    {
        protected override void Encode(IChannelHandlerContext ctx, WebSocketFrame msg,
                IList<object> @out)
        {
            // unused
        }
    }

    internal class Dummy2Decoder : WebSocketExtensionDecoder
    {
        protected override void Decode(IChannelHandlerContext ctx, WebSocketFrame msg,
                IList<object> @out)
        {
            // unused
        }
    }
}
