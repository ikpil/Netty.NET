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
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.IWebSocketExtension;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.IWebSocketExtensionFilter;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.Compression;

// CLR: the static imports of WebSocketExtension.RSV1/RSV3 and WebSocketExtensionFilter.ALWAYS_SKIP are using static
// directives of the I-prefixed interfaces. java.util.Random and the SplittableRandom seeded from it
// (PlatformDependent.splittableRandomNextBytes) are an unseeded System.Random filled with NextBytes: the payloads are
// arbitrary, and no test depends on their exact bytes. EmbeddedChannel readOutbound()/readInbound() are
// ReadOutbound<T>()/ReadInbound<T>(), content() is the Content property, assertArrayEquals is Assert.Equal of the
// arrays and assertEquals of two ByteBufs is Assert.Equal through ByteBuf's IEquatable<ByteBuf> (readable bytes).
// ZlibCodecFactory.newZlibEncoder(ZlibWrapper.NONE, 9, 15, 8) is codec-compression's JdkZlibEncoder on the BCL raw
// DeflateStream.
public class PerFrameDeflateDecoderTest
{

    private static readonly Random random = new Random();

    [Fact]
    public void TestCompressedFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerFrameDeflateDecoder(false, 0));

        // initialize
        byte[] payload = new byte[300];
        random.NextBytes(payload);

        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(payload)));
        ByteBuf compressedPayload = encoderChannel.ReadOutbound<ByteBuf>();

        BinaryWebSocketFrame compressedFrame = new BinaryWebSocketFrame(true,
                RSV1 | RSV3,
                compressedPayload.Slice(0, compressedPayload.ReadableBytes - 4));

        // execute
        Assert.True(decoderChannel.WriteInbound(compressedFrame));
        BinaryWebSocketFrame uncompressedFrame = decoderChannel.ReadInbound<BinaryWebSocketFrame>();

        // test
        Assert.NotNull(uncompressedFrame);
        Assert.NotNull(uncompressedFrame.Content);
        Assert.Equal(RSV3, uncompressedFrame.Rsv());
        Assert.Equal(300, uncompressedFrame.Content.ReadableBytes);

        byte[] finalPayload = new byte[300];
        uncompressedFrame.Content.ReadBytes(finalPayload);
        Assert.Equal(finalPayload, payload);
        uncompressedFrame.Release();
    }

    [Fact]
    public void TestNormalFrame()
    {
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerFrameDeflateDecoder(false, 0));

        // initialize
        byte[] payload = new byte[300];
        random.NextBytes(payload);

        BinaryWebSocketFrame frame = new BinaryWebSocketFrame(true,
                RSV3, Unpooled.WrappedBuffer(payload));

        // execute
        Assert.True(decoderChannel.WriteInbound(frame));
        BinaryWebSocketFrame newFrame = decoderChannel.ReadInbound<BinaryWebSocketFrame>();

        // test
        Assert.NotNull(newFrame);
        Assert.NotNull(newFrame.Content);
        Assert.Equal(RSV3, newFrame.Rsv());
        Assert.Equal(300, newFrame.Content.ReadableBytes);

        byte[] finalPayload = new byte[300];
        newFrame.Content.ReadBytes(finalPayload);
        Assert.Equal(finalPayload, payload);
        newFrame.Release();
    }

    // See https://github.com/netty/netty/issues/4348
    [Fact]
    public void TestCompressedEmptyFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerFrameDeflateDecoder(false, 0));

        Assert.True(encoderChannel.WriteOutbound(Unpooled.EmptyBuffer));
        ByteBuf compressedPayload = encoderChannel.ReadOutbound<ByteBuf>();
        BinaryWebSocketFrame compressedFrame =
                new BinaryWebSocketFrame(true, RSV1 | RSV3, compressedPayload);

        // execute
        Assert.True(decoderChannel.WriteInbound(compressedFrame));
        BinaryWebSocketFrame uncompressedFrame = decoderChannel.ReadInbound<BinaryWebSocketFrame>();

        // test
        Assert.NotNull(uncompressedFrame);
        Assert.NotNull(uncompressedFrame.Content);
        Assert.Equal(RSV3, uncompressedFrame.Rsv());
        Assert.Equal(0, uncompressedFrame.Content.ReadableBytes);
        uncompressedFrame.Release();
    }

    [Fact]
    public void TestDecompressionSkip()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerFrameDeflateDecoder(false, ALWAYS_SKIP, 0));

        byte[] payload = new byte[300];
        random.NextBytes(payload);

        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(payload)));
        ByteBuf compressedPayload = encoderChannel.ReadOutbound<ByteBuf>();

        BinaryWebSocketFrame compressedBinaryFrame = new BinaryWebSocketFrame(
                true, IWebSocketExtension.RSV1 | IWebSocketExtension.RSV3, compressedPayload);

        Assert.True(decoderChannel.WriteInbound(compressedBinaryFrame));

        BinaryWebSocketFrame inboundBinaryFrame = decoderChannel.ReadInbound<BinaryWebSocketFrame>();

        Assert.NotNull(inboundBinaryFrame);
        Assert.NotNull(inboundBinaryFrame.Content);
        Assert.Equal(compressedPayload, inboundBinaryFrame.Content);
        Assert.Equal(5, inboundBinaryFrame.Rsv());

        Assert.True(inboundBinaryFrame.Release());

        Assert.True(encoderChannel.FinishAndReleaseAll());
        Assert.False(decoderChannel.Finish());
    }

}
