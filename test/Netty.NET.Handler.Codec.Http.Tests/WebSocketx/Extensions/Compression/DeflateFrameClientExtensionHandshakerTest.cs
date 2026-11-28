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
using System.Collections.Immutable;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression.DeflateFrameServerExtensionHandshaker;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.Compression;

// CLR: WebSocketClientExtension.RSV1 is the constant IWebSocketExtension.RSV1, because a C# interface's static
// members are not inherited by derived interfaces' names. assertInstanceOf is Assert.IsAssignableFrom,
// Collections.emptyMap() is ImmutableDictionary<string, string>.Empty, HashMap is Dictionary and isEmpty() is
// Count == 0.
public class DeflateFrameClientExtensionHandshakerTest
{

    [Fact]
    public void TestWebkitDeflateFrameData()
    {
        DeflateFrameClientExtensionHandshaker handshaker =
                new DeflateFrameClientExtensionHandshaker(true, 0);

        WebSocketExtensionData data = handshaker.NewRequestData();

        Assert.Equal(X_WEBKIT_DEFLATE_FRAME_EXTENSION, data.Name());
        Assert.True(data.Parameters().Count == 0);
    }

    [Fact]
    public void TestDeflateFrameData()
    {
        DeflateFrameClientExtensionHandshaker handshaker =
                new DeflateFrameClientExtensionHandshaker(false, 0);

        WebSocketExtensionData data = handshaker.NewRequestData();

        Assert.Equal(DEFLATE_FRAME_EXTENSION, data.Name());
        Assert.True(data.Parameters().Count == 0);
    }

    [Fact]
    public void TestNormalHandshake()
    {
        DeflateFrameClientExtensionHandshaker handshaker =
                new DeflateFrameClientExtensionHandshaker(false, 0);

        IWebSocketClientExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(DEFLATE_FRAME_EXTENSION, ImmutableDictionary<string, string>.Empty));

        Assert.NotNull(extension);
        Assert.Equal(IWebSocketExtension.RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerFrameDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerFrameDeflateEncoder>(extension.NewExtensionEncoder());
    }

    [Fact]
    public void TestFailedHandshake()
    {
        // initialize
        DeflateFrameClientExtensionHandshaker handshaker =
                new DeflateFrameClientExtensionHandshaker(false, 0);

        IDictionary<string, string> parameters = new Dictionary<string, string>();
        parameters["invalid"] = "12";

        // execute
        IWebSocketClientExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(DEFLATE_FRAME_EXTENSION, parameters));

        // test
        Assert.Null(extension);
    }
}
