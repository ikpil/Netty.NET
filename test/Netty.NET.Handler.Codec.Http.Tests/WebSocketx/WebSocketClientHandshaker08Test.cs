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
using Netty.NET.Handler.Codec.Http.WebSocketx;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: java.net.URI is System.Uri (see WebSocketClientHandshakerTest).
public class WebSocketClientHandshaker08Test : WebSocketClientHandshaker07Test
{
    protected override WebSocketClientHandshaker NewHandshaker(Uri uri, string subprotocol, HttpHeaders headers,
                                                               bool absoluteUpgradeUrl, bool generateOriginHeader)
    {
        return new WebSocketClientHandshaker08(uri, WebSocketVersion.V08, subprotocol, false, headers,
          1024, true, true, 10000,
          absoluteUpgradeUrl, generateOriginHeader);
    }
}
