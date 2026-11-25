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

// CLR: java.net.URI is System.Uri and CharSequence is ICharSequence (see WebSocketClientHandshakerTest); the
// package-private eight-argument constructor of WebSocketClientHandshaker00 is internal and reached through
// InternalsVisibleTo.
public class WebSocketClientHandshaker00Test : WebSocketClientHandshakerTest
{
    protected override WebSocketClientHandshaker NewHandshaker(Uri uri, string subprotocol, HttpHeaders headers,
                                                               bool absoluteUpgradeUrl, bool generateOriginHeader)
    {
        return new WebSocketClientHandshaker00(uri, WebSocketVersion.V00, subprotocol, headers,
          1024, 10000, absoluteUpgradeUrl, generateOriginHeader);
    }

    protected override ICharSequence GetOriginHeaderName()
    {
        return HttpHeaderNames.ORIGIN;
    }

    protected override ICharSequence GetProtocolHeaderName()
    {
        return HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL;
    }

    protected override ICharSequence[] GetHandshakeRequiredHeaderNames()
    {
        return new ICharSequence[] {
                HttpHeaderNames.CONNECTION,
                HttpHeaderNames.UPGRADE,
                HttpHeaderNames.HOST,
                HttpHeaderNames.SEC_WEBSOCKET_KEY1,
                HttpHeaderNames.SEC_WEBSOCKET_KEY2,
        };
    }
}
