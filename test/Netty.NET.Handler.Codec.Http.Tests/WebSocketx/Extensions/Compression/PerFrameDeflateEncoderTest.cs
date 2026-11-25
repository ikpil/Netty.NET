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
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.IWebSocketExtensionFilter;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.Compression;

// CLR: WebSocketExtension.RSV1/RSV3 are the constants of IWebSocketExtension and the static import of
// WebSocketExtensionFilter.ALWAYS_SKIP is a using static of IWebSocketExtensionFilter. java.util.Random and the
// SplittableRandom seeded from it are an unseeded System.Random filled with NextBytes (the payloads are arbitrary).
// readOutbound()/readInbound() are ReadOutbound<T>()/ReadInbound<T>(), content() is the Content property,
// assertArrayEquals is Assert.Equal of the arrays, ByteBufUtil.getBytes is ByteBufUtil.GetBytes, and
// ZlibCodecFactory.newZlibDecoder(ZlibWrapper.NONE, 0) is codec-compression's JdkZlibDecoder on the BCL raw
// DeflateStream.
public class PerFrameDeflateEncoderTest
{

    private static readonly Random random = new Random();

    [Fact]
    public void TestCompressedFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerFrameDeflateEncoder(9, 15, false));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.NONE, 0));

        // initialize
        byte[] payload = new byte[300];
        random.NextBytes(payload);
        BinaryWebSocketFrame frame = new BinaryWebSocketFrame(true,
                IWebSocketExtension.RSV3, Unpooled.WrappedBuffer(payload));

        // execute
        Assert.True(encoderChannel.WriteOutbound(frame));
        BinaryWebSocketFrame compressedFrame = encoderChannel.ReadOutbound<BinaryWebSocketFrame>();

        // test
        Assert.NotNull(compressedFrame);
        Assert.NotNull(compressedFrame.Content);
        Assert.Equal(IWebSocketExtension.RSV1 | IWebSocketExtension.RSV3, compressedFrame.Rsv());

        Assert.True(decoderChannel.WriteInbound(compressedFrame.Content));
        Assert.True(decoderChannel.WriteInbound(DeflateDecoder.FRAME_TAIL.Duplicate()));
        ByteBuf uncompressedPayload = decoderChannel.ReadInbound<ByteBuf>();
        Assert.Equal(300, uncompressedPayload.ReadableBytes);

        byte[] finalPayload = new byte[300];
        uncompressedPayload.ReadBytes(finalPayload);
        Assert.Equal(finalPayload, payload);
        uncompressedPayload.Release();
    }

    [Fact]
    public void TestAlreadyCompressedFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerFrameDeflateEncoder(9, 15, false));

        // initialize
        byte[] payload = new byte[300];
        random.NextBytes(payload);

        BinaryWebSocketFrame frame = new BinaryWebSocketFrame(true,
                IWebSocketExtension.RSV3 | IWebSocketExtension.RSV1, Unpooled.WrappedBuffer(payload));

        // execute
        Assert.True(encoderChannel.WriteOutbound(frame));
        BinaryWebSocketFrame newFrame = encoderChannel.ReadOutbound<BinaryWebSocketFrame>();

        // test
        Assert.NotNull(newFrame);
        Assert.NotNull(newFrame.Content);
        Assert.Equal(IWebSocketExtension.RSV3 | IWebSocketExtension.RSV1, newFrame.Rsv());
        Assert.Equal(300, newFrame.Content.ReadableBytes);

        byte[] finalPayload = new byte[300];
        newFrame.Content.ReadBytes(finalPayload);
        Assert.Equal(finalPayload, payload);
        newFrame.Release();
    }

    [Fact]
    public void TestFramementedFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerFrameDeflateEncoder(9, 15, false));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.NONE, 0));

        // initialize
        byte[] payload1 = new byte[100];
        Random rng = new Random(random.Next());
        rng.NextBytes(payload1);
        byte[] payload2 = new byte[100];
        rng.NextBytes(payload2);
        byte[] payload3 = new byte[100];
        rng.NextBytes(payload3);

        BinaryWebSocketFrame frame1 = new BinaryWebSocketFrame(false,
                IWebSocketExtension.RSV3, Unpooled.WrappedBuffer(payload1));
        ContinuationWebSocketFrame frame2 = new ContinuationWebSocketFrame(false,
                IWebSocketExtension.RSV3, Unpooled.WrappedBuffer(payload2));
        ContinuationWebSocketFrame frame3 = new ContinuationWebSocketFrame(true,
                IWebSocketExtension.RSV3, Unpooled.WrappedBuffer(payload3));

        // execute
        Assert.True(encoderChannel.WriteOutbound(frame1));
        Assert.True(encoderChannel.WriteOutbound(frame2));
        Assert.True(encoderChannel.WriteOutbound(frame3));
        BinaryWebSocketFrame compressedFrame1 = encoderChannel.ReadOutbound<BinaryWebSocketFrame>();
        ContinuationWebSocketFrame compressedFrame2 = encoderChannel.ReadOutbound<ContinuationWebSocketFrame>();
        ContinuationWebSocketFrame compressedFrame3 = encoderChannel.ReadOutbound<ContinuationWebSocketFrame>();

        // test
        Assert.NotNull(compressedFrame1);
        Assert.NotNull(compressedFrame2);
        Assert.NotNull(compressedFrame3);
        Assert.Equal(IWebSocketExtension.RSV1 | IWebSocketExtension.RSV3, compressedFrame1.Rsv());
        Assert.Equal(IWebSocketExtension.RSV1 | IWebSocketExtension.RSV3, compressedFrame2.Rsv());
        Assert.Equal(IWebSocketExtension.RSV1 | IWebSocketExtension.RSV3, compressedFrame3.Rsv());
        Assert.False(compressedFrame1.IsFinalFragment());
        Assert.False(compressedFrame2.IsFinalFragment());
        Assert.True(compressedFrame3.IsFinalFragment());

        Assert.True(decoderChannel.WriteInbound(compressedFrame1.Content));
        Assert.True(decoderChannel.WriteInbound(DeflateDecoder.FRAME_TAIL.Duplicate()));
        ByteBuf uncompressedPayload1 = decoderChannel.ReadInbound<ByteBuf>();
        byte[] finalPayload1 = new byte[100];
        uncompressedPayload1.ReadBytes(finalPayload1);
        Assert.Equal(finalPayload1, payload1);
        uncompressedPayload1.Release();

        Assert.True(decoderChannel.WriteInbound(compressedFrame2.Content));
        Assert.True(decoderChannel.WriteInbound(DeflateDecoder.FRAME_TAIL.Duplicate()));
        ByteBuf uncompressedPayload2 = decoderChannel.ReadInbound<ByteBuf>();
        byte[] finalPayload2 = new byte[100];
        uncompressedPayload2.ReadBytes(finalPayload2);
        Assert.Equal(finalPayload2, payload2);
        uncompressedPayload2.Release();

        Assert.True(decoderChannel.WriteInbound(compressedFrame3.Content));
        Assert.True(decoderChannel.WriteInbound(DeflateDecoder.FRAME_TAIL.Duplicate()));
        ByteBuf uncompressedPayload3 = decoderChannel.ReadInbound<ByteBuf>();
        byte[] finalPayload3 = new byte[100];
        uncompressedPayload3.ReadBytes(finalPayload3);
        Assert.Equal(finalPayload3, payload3);
        uncompressedPayload3.Release();
    }

    [Fact]
    public void TestCompressionSkip()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                new PerFrameDeflateEncoder(9, 15, false, ALWAYS_SKIP));
        byte[] payload = new byte[300];
        random.NextBytes(payload);
        BinaryWebSocketFrame binaryFrame = new BinaryWebSocketFrame(true,
                                                                    0, Unpooled.WrappedBuffer(payload));

        // execute
        Assert.True(encoderChannel.WriteOutbound(binaryFrame.Copy()));
        BinaryWebSocketFrame outboundFrame = encoderChannel.ReadOutbound<BinaryWebSocketFrame>();

        // test
        Assert.NotNull(outboundFrame);
        Assert.NotNull(outboundFrame.Content);
        Assert.Equal(payload, ByteBufUtil.GetBytes(outboundFrame.Content));
        Assert.Equal(0, outboundFrame.Rsv());
        Assert.True(outboundFrame.Release());

        Assert.False(encoderChannel.Finish());
    }

}
