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
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression.PerMessageDeflateServerExtensionHandshaker;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.Compression;

// CLR: WebSocketServerExtension.RSV1 is the constant IWebSocketExtension.RSV1.
public class PerMessageDeflateServerExtensionHandshakerTest
{

    [Fact]
    public void TestNormalHandshake()
    {
        IWebSocketServerExtension extension;
        WebSocketExtensionData data;
        IDictionary<string, string> parameters;

        // initialize
        PerMessageDeflateServerExtensionHandshaker handshaker =
                new PerMessageDeflateServerExtensionHandshaker(0);

        // execute
        extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, ImmutableDictionary<string, string>.Empty));

        // test
        Assert.NotNull(extension);
        Assert.Equal(IWebSocketExtension.RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());

        // execute
        data = extension.NewReponseData();

        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, data.Name());
        Assert.True(data.Parameters().Count == 0);

        // initialize
        parameters = new Dictionary<string, string>();
        parameters[CLIENT_MAX_WINDOW] = null;
        parameters[CLIENT_NO_CONTEXT] = null;

        // execute
        extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, ImmutableDictionary<string, string>.Empty));

        // test
        Assert.NotNull(extension);
        Assert.Equal(IWebSocketExtension.RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());

        // execute
        data = extension.NewReponseData();

        // test
        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, data.Name());
        Assert.True(data.Parameters().Count == 0);

        // initialize
        parameters = new Dictionary<string, string>();
        parameters[SERVER_MAX_WINDOW] = "12";
        parameters[SERVER_NO_CONTEXT] = null;

        // execute
        extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // test
        Assert.Null(extension);
    }

    [Fact]
    public void TestCustomHandshake()
    {
        IWebSocketServerExtension extension;
        IDictionary<string, string> parameters;
        WebSocketExtensionData data;

        // initialize
        PerMessageDeflateServerExtensionHandshaker handshaker =
                new PerMessageDeflateServerExtensionHandshaker(6, true, 10, true, true, 0);

        parameters = new Dictionary<string, string>();
        parameters[CLIENT_MAX_WINDOW] = null;
        parameters[SERVER_MAX_WINDOW] = "12";
        parameters[CLIENT_NO_CONTEXT] = null;
        parameters[SERVER_NO_CONTEXT] = null;

        // execute
        extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // test
        Assert.NotNull(extension);
        Assert.Equal(IWebSocketExtension.RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());

        // execute
        data = extension.NewReponseData();

        // test
        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, data.Name());
        Assert.True(data.Parameters().ContainsKey(CLIENT_MAX_WINDOW));
        Assert.Equal("10", data.Parameters()[CLIENT_MAX_WINDOW]);
        Assert.True(data.Parameters().ContainsKey(SERVER_MAX_WINDOW));
        Assert.Equal("12", data.Parameters()[SERVER_MAX_WINDOW]);
        Assert.True(data.Parameters().ContainsKey(CLIENT_MAX_WINDOW));
        Assert.True(data.Parameters().ContainsKey(SERVER_MAX_WINDOW));

        // initialize
        parameters = new Dictionary<string, string>();
        parameters[SERVER_MAX_WINDOW] = "12";
        parameters[SERVER_NO_CONTEXT] = null;

        // execute
        extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // test
        Assert.NotNull(extension);
        Assert.Equal(IWebSocketExtension.RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());

        // execute
        data = extension.NewReponseData();

        // test
        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, data.Name());
        Assert.Equal(2, data.Parameters().Count);
        Assert.True(data.Parameters().ContainsKey(SERVER_MAX_WINDOW));
        Assert.Equal("12", data.Parameters()[SERVER_MAX_WINDOW]);
        Assert.True(data.Parameters().ContainsKey(SERVER_NO_CONTEXT));

        // initialize
        parameters = new Dictionary<string, string>();

        // execute
        extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));
        // test
        Assert.NotNull(extension);

        // execute
        data = extension.NewReponseData();

        // test
        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, data.Name());
        Assert.True(data.Parameters().Count == 0);
    }

    [Fact]
    public void TestClientMaxWindowWithValue()
    {
        PerMessageDeflateServerExtensionHandshaker handshaker =
                new PerMessageDeflateServerExtensionHandshaker(6, true, 10, true, true, 0);

        IDictionary<string, string> parameters = new Dictionary<string, string>();
        parameters[CLIENT_MAX_WINDOW] = "12";

        IWebSocketServerExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        Assert.NotNull(extension);
        Assert.Equal(IWebSocketExtension.RSV1, extension.Rsv());

        WebSocketExtensionData data = extension.NewReponseData();
        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, data.Name());
        // Server should use the client's requested value (12) not the preferred (10)
        Assert.True(data.Parameters().ContainsKey(CLIENT_MAX_WINDOW));
        Assert.Equal("12", data.Parameters()[CLIENT_MAX_WINDOW]);
    }

    [Fact]
    public void TestClientMaxWindowWithInvalidValue()
    {
        PerMessageDeflateServerExtensionHandshaker handshaker =
                new PerMessageDeflateServerExtensionHandshaker(6, true, 10, true, true, 0);

        IDictionary<string, string> parameters = new Dictionary<string, string>();
        parameters[CLIENT_MAX_WINDOW] = "7"; // Below MIN_WINDOW_SIZE (8)

        IWebSocketServerExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // Handshake should fail when client_max_window_bits is out of range
        Assert.Null(extension);
    }

    [Fact]
    public void TestServerWindowSizeAdvertisedWhenLowerThanMax()
    {
        // Server prefers a 10-bit window; client didn't request server_max_window_bits.
        // RFC 7692 §7.1.2: server MAY include server_max_window_bits unilaterally.
        PerMessageDeflateServerExtensionHandshaker handshaker =
                new PerMessageDeflateServerExtensionHandshaker(6, false, MAX_WINDOW_SIZE,
                        false, false, 10, 8, 0);

        IWebSocketServerExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION,
                        ImmutableDictionary<string, string>.Empty));

        Assert.NotNull(extension);
        WebSocketExtensionData data = extension.NewReponseData();
        Assert.True(data.Parameters().ContainsKey(SERVER_MAX_WINDOW));
        Assert.Equal("10", data.Parameters()[SERVER_MAX_WINDOW]);
    }

    [Fact]
    public void TestServerWindowSizeCapsClientOffer()
    {
        // Server limit is 10. Client offers server_max_window_bits=12.
        // RFC 7692: server accepts with same or smaller value → 10.
        PerMessageDeflateServerExtensionHandshaker handshaker =
                new PerMessageDeflateServerExtensionHandshaker(6, true, MAX_WINDOW_SIZE,
                        false, false, 10, 8, 0);

        IDictionary<string, string> parameters = new Dictionary<string, string>();
        parameters[SERVER_MAX_WINDOW] = "12";

        IWebSocketServerExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        Assert.NotNull(extension);
        WebSocketExtensionData data = extension.NewReponseData();
        Assert.Equal("10", data.Parameters()[SERVER_MAX_WINDOW]);
    }

    [Fact]
    public void TestServerWindowSizeHonorsSmallerClientOffer()
    {
        // Server limit is 12. Client offers server_max_window_bits=9. Server uses 9.
        PerMessageDeflateServerExtensionHandshaker handshaker =
                new PerMessageDeflateServerExtensionHandshaker(6, true, MAX_WINDOW_SIZE,
                        false, false, 12, 8, 0);

        IDictionary<string, string> parameters = new Dictionary<string, string>();
        parameters[SERVER_MAX_WINDOW] = "9";

        IWebSocketServerExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        Assert.NotNull(extension);
        WebSocketExtensionData data = extension.NewReponseData();
        Assert.Equal("9", data.Parameters()[SERVER_MAX_WINDOW]);
    }

    [Fact]
    public void TestServerWindowSizeOutOfRangeRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new PerMessageDeflateServerExtensionHandshaker(6, true, MAX_WINDOW_SIZE,
                    false, false, 16, 8, 0);
        });
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new PerMessageDeflateServerExtensionHandshaker(6, true, MAX_WINDOW_SIZE,
                    false, false, 7, 8, 0);
        });
    }

    [Fact]
    public void TestMemLevelOutOfRangeRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new PerMessageDeflateServerExtensionHandshaker(6, true, MAX_WINDOW_SIZE,
                    false, false, MAX_WINDOW_SIZE, 10, 0);
        });
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new PerMessageDeflateServerExtensionHandshaker(6, true, MAX_WINDOW_SIZE,
                    false, false, MAX_WINDOW_SIZE, 0, 0);
        });
    }
}
