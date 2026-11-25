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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression.DeflateFrameServerExtensionHandshaker;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.Compression;

// CLR: the static import of DeflateFrameServerExtensionHandshaker.* is a using static. WebSocketServerExtension.RSV1 is
// the constant IWebSocketExtension.RSV1, assertInstanceOf is Assert.IsAssignableFrom, assertThrows(T.class, Executable)
// is Assert.ThrowsAny<T> with a lambda for the anonymous Executable, and IllegalArgumentException is ArgumentException.
// Collections.emptyMap() is ImmutableDictionary<string, string>.Empty and HashMap is Dictionary.
public class DeflateFrameServerExtensionHandshakerTest
{

    [Fact]
    public void TestNormalHandshake()
    {
        // initialize
        DeflateFrameServerExtensionHandshaker handshaker =
                new DeflateFrameServerExtensionHandshaker(
                        DeflateFrameServerExtensionHandshaker.DEFAULT_COMPRESSION_LEVEL, 0);

        // execute
        IWebSocketServerExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(DEFLATE_FRAME_EXTENSION, ImmutableDictionary<string, string>.Empty));

        // test
        Assert.NotNull(extension);
        Assert.Equal(IWebSocketExtension.RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerFrameDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerFrameDeflateEncoder>(extension.NewExtensionEncoder());
    }

    [Fact]
    public void TestWebkitHandshake()
    {
        // initialize
        DeflateFrameServerExtensionHandshaker handshaker =
                new DeflateFrameServerExtensionHandshaker(
                        DeflateFrameServerExtensionHandshaker.DEFAULT_COMPRESSION_LEVEL, 0);

        // execute
        IWebSocketServerExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(X_WEBKIT_DEFLATE_FRAME_EXTENSION,
                        ImmutableDictionary<string, string>.Empty));

        // test
        Assert.NotNull(extension);
        Assert.Equal(IWebSocketExtension.RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerFrameDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerFrameDeflateEncoder>(extension.NewExtensionEncoder());
    }

    [Fact]
    public void TestFailedHandshake()
    {
        // initialize
        DeflateFrameServerExtensionHandshaker handshaker =
                new DeflateFrameServerExtensionHandshaker(
                        DeflateFrameServerExtensionHandshaker.DEFAULT_COMPRESSION_LEVEL, 0);

        IDictionary<string, string> parameters;
        parameters = new Dictionary<string, string>();
        parameters["unknown"] = "11";

        // execute
        IWebSocketServerExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(DEFLATE_FRAME_EXTENSION, parameters));

        // test
        Assert.Null(extension);
    }

    [Fact]
    public void TestCustomWindowAndMemLevel()
    {
        DeflateFrameServerExtensionHandshaker handshaker =
                new DeflateFrameServerExtensionHandshaker(
                        DeflateFrameServerExtensionHandshaker.DEFAULT_COMPRESSION_LEVEL, 10, 4, 0);

        IWebSocketServerExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(DEFLATE_FRAME_EXTENSION, ImmutableDictionary<string, string>.Empty));

        Assert.NotNull(extension);
        Assert.Equal(IWebSocketExtension.RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerFrameDeflateEncoder>(extension.NewExtensionEncoder());
    }

    [Fact]
    public void TestWindowSizeOutOfRangeRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new DeflateFrameServerExtensionHandshaker(
                    DeflateFrameServerExtensionHandshaker.DEFAULT_COMPRESSION_LEVEL, 16, 8, 0);
        });
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new DeflateFrameServerExtensionHandshaker(
                    DeflateFrameServerExtensionHandshaker.DEFAULT_COMPRESSION_LEVEL, 7, 8, 0);
        });
    }

    [Fact]
    public void TestMemLevelOutOfRangeRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new DeflateFrameServerExtensionHandshaker(
                    DeflateFrameServerExtensionHandshaker.DEFAULT_COMPRESSION_LEVEL, 15, 10, 0);
        });
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new DeflateFrameServerExtensionHandshaker(
                    DeflateFrameServerExtensionHandshaker.DEFAULT_COMPRESSION_LEVEL, 15, 0, 0);
        });
    }

}
