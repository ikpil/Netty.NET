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

using System.Collections.Generic;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.WebSocketExtensionUtil;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions;

// CLR: the package-private isWebsocketUpgrade and computeMergeExtensionsHeaderValue are internal and reached through
// InternalsVisibleTo; List is IList.
public class WebSocketExtensionUtilTest
{

    [Fact]
    public void TestIsWebsocketUpgrade()
    {
        HttpHeaders headers = new DefaultHttpHeaders();
        Assert.False(IsWebsocketUpgrade(headers));

        headers.Add(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET);
        Assert.False(IsWebsocketUpgrade(headers));

        headers.Add(HttpHeaderNames.CONNECTION, "Keep-Alive, Upgrade");
        Assert.True(IsWebsocketUpgrade(headers));
    }

    [Fact]
    public void ComputeMergeExtensionsHeaderValueWhenNoUserDefinedHeader()
    {
        IList<WebSocketExtensionData> extras = ExtractExtensions("permessage-deflate; client_max_window_bits," +
          "permessage-deflate; client_no_context_takeover; client_max_window_bits," +
          "deflate-frame," +
          "x-webkit-deflate-frame");
        string newHeaderValue = ComputeMergeExtensionsHeaderValue(null, extras);
        Assert.Equal("permessage-deflate;client_max_window_bits," +
          "permessage-deflate;client_no_context_takeover;client_max_window_bits," +
          "deflate-frame," +
          "x-webkit-deflate-frame", newHeaderValue);
    }

    [Fact]
    public void ComputeMergeExtensionsHeaderValueWhenNoConflictingUserDefinedHeader()
    {
        IList<WebSocketExtensionData> extras = ExtractExtensions("permessage-deflate; client_max_window_bits," +
          "permessage-deflate; client_no_context_takeover; client_max_window_bits," +
          "deflate-frame," +
          "x-webkit-deflate-frame");
        string newHeaderValue = ComputeMergeExtensionsHeaderValue("foo, bar", extras);
        Assert.Equal("permessage-deflate;client_max_window_bits," +
          "permessage-deflate;client_no_context_takeover;client_max_window_bits," +
          "deflate-frame," +
          "x-webkit-deflate-frame," +
          "foo," +
          "bar", newHeaderValue);
    }

    [Fact]
    public void ComputeMergeExtensionsHeaderValueWhenConflictingUserDefinedHeader()
    {
        IList<WebSocketExtensionData> extras = ExtractExtensions("permessage-deflate; client_max_window_bits," +
          "permessage-deflate; client_no_context_takeover; client_max_window_bits," +
          "deflate-frame," +
          "x-webkit-deflate-frame");
        string newHeaderValue = ComputeMergeExtensionsHeaderValue("permessage-deflate; client_max_window_bits", extras);
        Assert.Equal("permessage-deflate;client_max_window_bits," +
          "permessage-deflate;client_no_context_takeover;client_max_window_bits," +
          "deflate-frame," +
          "x-webkit-deflate-frame", newHeaderValue);
    }
}
