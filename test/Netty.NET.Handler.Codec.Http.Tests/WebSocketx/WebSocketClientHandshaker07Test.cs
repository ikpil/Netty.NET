/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http.WebSocketx;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: java.net.URI is System.Uri, CharSequence is ICharSequence and newHandshakeRequest() is reached through the
// internal forwarder InvokeNewHandshakeRequest() (see WebSocketClientHandshakerTest); the package-private
// eleven-argument constructor of WebSocketClientHandshaker07 is internal and reached through InternalsVisibleTo.
public class WebSocketClientHandshaker07Test : WebSocketClientHandshakerTest
{

    [Fact]
    public void TestHostHeaderPreserved()
    {
        Uri uri = new Uri("ws://localhost:9999", UriKind.RelativeOrAbsolute);
        WebSocketClientHandshaker handshaker = NewHandshaker(uri, null,
                new DefaultHttpHeaders().Set(HttpHeaderNames.HOST, "test.netty.io"), false, true);

        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        try
        {
            Assert.Equal("/", request.Uri());
            Assert.Equal("test.netty.io", request.Headers().Get(HttpHeaderNames.HOST));
        }
        finally
        {
            request.Release();
        }
    }

    protected override WebSocketClientHandshaker NewHandshaker(Uri uri, string subprotocol, HttpHeaders headers,
                                                               bool absoluteUpgradeUrl, bool generateOriginHeader)
    {
        return new WebSocketClientHandshaker07(uri, WebSocketVersion.V07, subprotocol, false, headers,
          1024, true, false, 10000,
          absoluteUpgradeUrl, generateOriginHeader);
    }

    protected override ICharSequence GetOriginHeaderName()
    {
        return HttpHeaderNames.SEC_WEBSOCKET_ORIGIN;
    }

    protected override ICharSequence GetProtocolHeaderName()
    {
        return HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL;
    }

    protected override ICharSequence[] GetHandshakeRequiredHeaderNames()
    {
        return new ICharSequence[] {
                HttpHeaderNames.UPGRADE,
                HttpHeaderNames.CONNECTION,
                HttpHeaderNames.SEC_WEBSOCKET_KEY,
                HttpHeaderNames.HOST,
                HttpHeaderNames.SEC_WEBSOCKET_VERSION,
        };
    }
}
