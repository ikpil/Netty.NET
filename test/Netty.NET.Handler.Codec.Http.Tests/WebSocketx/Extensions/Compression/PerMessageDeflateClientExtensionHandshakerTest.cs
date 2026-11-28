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
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.IWebSocketExtension;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression.PerMessageDeflateServerExtensionHandshaker;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.Compression;

// CLR: The two text assertions pass the literal first, as xUnit expects the expected value first (analyzer
// xUnit2000), where Java passes it second.
public class PerMessageDeflateClientExtensionHandshakerTest
{

    private static byte[] Bytes(params sbyte[] values)
    {
        return Array.ConvertAll(values, v => unchecked((byte) v));
    }

    [Fact]
    public void TestNormalData()
    {
        PerMessageDeflateClientExtensionHandshaker handshaker =
                new PerMessageDeflateClientExtensionHandshaker(0);

        WebSocketExtensionData data = handshaker.NewRequestData();

        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, data.Name());
        Assert.Equal(ZlibCodecFactory.IsSupportingWindowSizeAndMemLevel() ? 1 : 0, data.Parameters().Count);
    }

    [Fact]
    public void TestCustomData()
    {
        PerMessageDeflateClientExtensionHandshaker handshaker =
                new PerMessageDeflateClientExtensionHandshaker(6, true, 10, true, true, 0);

        WebSocketExtensionData data = handshaker.NewRequestData();

        Assert.Equal(PERMESSAGE_DEFLATE_EXTENSION, data.Name());
        Assert.True(data.Parameters().ContainsKey(CLIENT_MAX_WINDOW));
        Assert.True(data.Parameters().ContainsKey(SERVER_MAX_WINDOW));
        Assert.Equal("10", data.Parameters()[SERVER_MAX_WINDOW]);
        Assert.True(data.Parameters().ContainsKey(CLIENT_MAX_WINDOW));
        Assert.True(data.Parameters().ContainsKey(SERVER_MAX_WINDOW));
    }

    [Fact]
    public void TestNormalHandshake()
    {
        PerMessageDeflateClientExtensionHandshaker handshaker =
                new PerMessageDeflateClientExtensionHandshaker(0);

        IWebSocketClientExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, ImmutableDictionary<string, string>.Empty));

        Assert.NotNull(extension);
        Assert.Equal(RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());
    }

    [Fact]
    public void TestCustomHandshake()
    {
        IWebSocketClientExtension extension;
        IDictionary<string, string> parameters;

        // initialize
        PerMessageDeflateClientExtensionHandshaker handshaker =
                new PerMessageDeflateClientExtensionHandshaker(6, true, 10, true, true, 0);

        parameters = new Dictionary<string, string>();
        parameters[CLIENT_MAX_WINDOW] = "12";
        parameters[SERVER_MAX_WINDOW] = "8";
        parameters[CLIENT_NO_CONTEXT] = null;
        parameters[SERVER_NO_CONTEXT] = null;

        // execute
        extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // test
        Assert.NotNull(extension);
        Assert.Equal(RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());

        // initialize
        parameters = new Dictionary<string, string>();
        parameters[SERVER_MAX_WINDOW] = "10";
        parameters[SERVER_NO_CONTEXT] = null;

        // execute
        extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // test
        Assert.NotNull(extension);
        Assert.Equal(RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());

        // initialize
        parameters = new Dictionary<string, string>();

        // execute
        extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // test
        Assert.Null(extension);
    }

    [Fact]
    public void TestParameterValidation()
    {
        IWebSocketClientExtension extension;
        IDictionary<string, string> parameters;

        PerMessageDeflateClientExtensionHandshaker handshaker =
                new PerMessageDeflateClientExtensionHandshaker(6, true, 15, true, false, 0);

        parameters = new Dictionary<string, string>();
        parameters[CLIENT_MAX_WINDOW] = "15";
        parameters[SERVER_MAX_WINDOW] = "8";
        extension = handshaker.HandshakeExtension(new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // Test that handshake succeeds when parameters are valid
        Assert.NotNull(extension);
        Assert.Equal(RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());

        parameters = new Dictionary<string, string>();
        parameters[CLIENT_MAX_WINDOW] = "15";
        parameters[SERVER_MAX_WINDOW] = "7";

        extension = handshaker.HandshakeExtension(new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // Test that handshake fails when parameters are invalid
        Assert.Null(extension);
    }

    [Fact]
    public void TestServerNoContextTakeover()
    {
        IWebSocketClientExtension extension;
        IDictionary<string, string> parameters;

        PerMessageDeflateClientExtensionHandshaker handshaker =
                new PerMessageDeflateClientExtensionHandshaker(6, true, 15, true, false, 0);

        parameters = new Dictionary<string, string>();
        parameters[SERVER_NO_CONTEXT] = null;
        extension = handshaker.HandshakeExtension(new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // Test that handshake succeeds when server responds with `server_no_context_takeover` that we didn't offer
        Assert.NotNull(extension);
        Assert.Equal(RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());

        // initialize
        handshaker = new PerMessageDeflateClientExtensionHandshaker(6, true, 15, true, true, 0);

        parameters = new Dictionary<string, string>();
        extension = handshaker.HandshakeExtension(new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // Test that handshake fails when client offers `server_no_context_takeover` but server doesn't support it
        Assert.Null(extension);
    }

    [Fact]
    public void TestDecoderNoClientContext()
    {
        PerMessageDeflateClientExtensionHandshaker handshaker =
                new PerMessageDeflateClientExtensionHandshaker(6, true, MAX_WINDOW_SIZE, true, false, 0);

        byte[] firstPayload = Bytes(
                76, -50, -53, 10, -62, 48, 20, 4, -48, 95, 41, 89, -37, 36, 77, 90, 31, -39, 41, -72, 112, 33, -120, 20,
                20, 119, -79, 70, 123, -95, 121, -48, 92, -116, 80, -6, -17, -58, -99, -37, -31, 12, 51, 19, 1, -9, -12,
                68, -111, -117, 25, 58, 111, 77, -127, -66, -64, -34, 20, 59, -64, -29, -2, 90, -100, -115, 30, 16, 114,
                -68, 61, 29, 40, 89, -112, -73, 25, 35, 120, -105, -67, -32, -43, -70, -84, 120, -55, 69, 43, -124, 106,
                -92, 18, -110, 114, -50, 111, 25, -3, 10, 17, -75, 13, 127, -84, 106, 90, -66, 84, -75, 84, 53, -89,
                -75, 92, -3, -40, -61, 119, 49, -117, 30, 49, 68, -59, 88, 74, -119, -34, 1, -83, -7, -48, 124, -124,
                -23, 16, 88, -118, 121, 54, -53, 1, 44, 32, 81, 19, 25, -115, -43, -32, -64, -67, -120, -110, -101, 121,
                -2, 2
        );

        byte[] secondPayload = Bytes(
                -86, 86, 42, 46, 77, 78, 78, 45, 6, 26, 83, 82, 84, -102, -86, 3, -28, 38, 21, 39, 23, 101, 38, -91, 2,
                -51, -51, 47, 74, 73, 45, 114, -54, -49, -49, -10, 49, -78, -118, 112, 10, 9, 13, 118, 1, -102, 84,
                -108, 90, 88, 10, 116, 27, -56, -84, 124, -112, -13, 16, 26, 116, -108, 18, -117, -46, -127, 6, 69, 99,
                -45, 24, 91, 91, 11, 0
        );

        IDictionary<string, string> parameters = new Dictionary<string, string> { [CLIENT_NO_CONTEXT] = null };

        IWebSocketClientExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));
        Assert.NotNull(extension);

        EmbeddedChannel decoderChannel = new EmbeddedChannel(extension.NewExtensionDecoder());
        Assert.True(
                decoderChannel.WriteInbound(new TextWebSocketFrame(true, RSV1, Unpooled.CopiedBuffer(firstPayload))));
        TextWebSocketFrame firstFrameDecompressed = decoderChannel.ReadInbound<TextWebSocketFrame>();
        Assert.True(
                decoderChannel.WriteInbound(new TextWebSocketFrame(true, RSV1, Unpooled.CopiedBuffer(secondPayload))));
        TextWebSocketFrame secondFrameDecompressed = decoderChannel.ReadInbound<TextWebSocketFrame>();

        Assert.NotNull(firstFrameDecompressed);
        Assert.NotNull(firstFrameDecompressed.Content);
        Assert.IsAssignableFrom<TextWebSocketFrame>(firstFrameDecompressed);
        Assert.Equal("{\"info\":\"Welcome to the BitMEX Realtime API.\",\"version\"" +
                     ":\"2018-10-02T22:53:23.000Z\",\"timestamp\":\"2018-10-15T06:43:40.437Z\"," +
                     "\"docs\":\"https://www.bitmex.com/app/wsAPI\",\"limit\":{\"remaining\":39}}",
                     firstFrameDecompressed.Text());
        Assert.True(firstFrameDecompressed.Release());

        Assert.NotNull(secondFrameDecompressed);
        Assert.NotNull(secondFrameDecompressed.Content);
        Assert.IsAssignableFrom<TextWebSocketFrame>(secondFrameDecompressed);
        Assert.Equal("{\"success\":true,\"subscribe\":\"orderBookL2:XBTUSD\"," +
                     "\"request\":{\"op\":\"subscribe\",\"args\":[\"orderBookL2:XBTUSD\"]}}",
                     secondFrameDecompressed.Text());
        Assert.True(secondFrameDecompressed.Release());

        Assert.False(decoderChannel.Finish());
    }

    [Fact]
    public void TestClientMaxWindowWithNoValue()
    {
        // Test that client handles client_max_window_bits with no value (null)
        // RFC 7692: client_max_window_bits may have no value
        PerMessageDeflateClientExtensionHandshaker handshaker =
                new PerMessageDeflateClientExtensionHandshaker(6, true, 15, true, false, 0);

        IDictionary<string, string> parameters = new Dictionary<string, string>();
        parameters[CLIENT_MAX_WINDOW] = null; // No value specified

        // Should not throw NumberFormatException
        IWebSocketClientExtension extension = handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));

        // Handshake should succeed, using MAX_WINDOW_SIZE (15) as default
        Assert.NotNull(extension);
        Assert.Equal(RSV1, extension.Rsv());
        Assert.IsAssignableFrom<PerMessageDeflateDecoder>(extension.NewExtensionDecoder());
        Assert.IsAssignableFrom<PerMessageDeflateEncoder>(extension.NewExtensionEncoder());
    }

    [Fact]
    public void TestClientMaxWindowWithInvalidValue()
    {
        // Test that client throws NumberFormatException for invalid client_max_window_bits value
        PerMessageDeflateClientExtensionHandshaker handshaker =
                new PerMessageDeflateClientExtensionHandshaker(6, true, 15, true, false, 0);

        IDictionary<string, string> parameters = new Dictionary<string, string>();
        parameters[CLIENT_MAX_WINDOW] = "invalid";

        // Should throw NumberFormatException
        Assert.ThrowsAny<FormatException>(() =>
        {
            handshaker.HandshakeExtension(
                new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters));
        });
    }
}
