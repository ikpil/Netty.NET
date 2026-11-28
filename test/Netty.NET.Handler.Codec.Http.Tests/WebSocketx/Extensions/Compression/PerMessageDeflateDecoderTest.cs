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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression.DeflateDecoder;
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.IWebSocketExtensionFilter;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.Compression;

// CLR: WebSocketExtension.RSV1/RSV3 are the constants of IWebSocketExtension. The anonymous
// WebSocketExtensionFilter classes are the named nested classes TextShorterThan100Filter and ShorterThan100Filter.
// java.util.Random and the SplittableRandom seeded from it are an unseeded System.Random filled with NextBytes
// (the payloads are arbitrary). assertThrows(T.class, Executable) is Assert.ThrowsAny<T> with a lambda;
// readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>(), content() is the Content property,
// slice/wrappedBuffer/decodeHexDump/getBytes are the Buffer port's Slice, WrappedBuffer, ByteBufUtil.DecodeHexDump
// and ByteBufUtil.GetBytes, and Java's equality of two ByteBufs is Assert.Equal through ByteBuf's
// IEquatable<ByteBuf>.
public class PerMessageDeflateDecoderTest
{

    private static readonly Random random = new Random();

    [Fact]
    public void TestCompressedFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerMessageDeflateDecoder(false, 0));

        // initialize
        byte[] payload = new byte[300];
        random.NextBytes(payload);

        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(payload)));
        ByteBuf compressedPayload = encoderChannel.ReadOutbound<ByteBuf>();

        BinaryWebSocketFrame compressedFrame = new BinaryWebSocketFrame(true,
                IWebSocketExtension.RSV1 | IWebSocketExtension.RSV3,
                compressedPayload.Slice(0, compressedPayload.ReadableBytes - 4));

        // execute
        Assert.True(decoderChannel.WriteInbound(compressedFrame));
        BinaryWebSocketFrame uncompressedFrame = decoderChannel.ReadInbound<BinaryWebSocketFrame>();

        // test
        Assert.NotNull(uncompressedFrame);
        Assert.NotNull(uncompressedFrame.Content);
        Assert.Equal(IWebSocketExtension.RSV3, uncompressedFrame.Rsv());
        Assert.Equal(300, uncompressedFrame.Content.ReadableBytes);

        byte[] finalPayload = new byte[300];
        uncompressedFrame.Content.ReadBytes(finalPayload);
        Assert.Equal(finalPayload, payload);
        uncompressedFrame.Release();
    }

    [Fact]
    public void TestNormalFrame()
    {
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerMessageDeflateDecoder(false, 0));

        // initialize
        byte[] payload = new byte[300];
        random.NextBytes(payload);

        BinaryWebSocketFrame frame = new BinaryWebSocketFrame(true,
                IWebSocketExtension.RSV3, Unpooled.WrappedBuffer(payload));

        // execute
        Assert.True(decoderChannel.WriteInbound(frame));
        BinaryWebSocketFrame newFrame = decoderChannel.ReadInbound<BinaryWebSocketFrame>();

        // test
        Assert.NotNull(newFrame);
        Assert.NotNull(newFrame.Content);
        Assert.Equal(IWebSocketExtension.RSV3, newFrame.Rsv());
        Assert.Equal(300, newFrame.Content.ReadableBytes);

        byte[] finalPayload = new byte[300];
        newFrame.Content.ReadBytes(finalPayload);
        Assert.Equal(finalPayload, payload);
        newFrame.Release();
    }

    [Fact]
    public void TestFragmentedFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerMessageDeflateDecoder(false, 0));

        // initialize
        byte[] payload = new byte[300];
        random.NextBytes(payload);

        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(payload)));
        ByteBuf compressedPayload = encoderChannel.ReadOutbound<ByteBuf>();
        compressedPayload = compressedPayload.Slice(0, compressedPayload.ReadableBytes - 4);

        int oneThird = compressedPayload.ReadableBytes / 3;
        BinaryWebSocketFrame compressedFrame1 = new BinaryWebSocketFrame(false,
                IWebSocketExtension.RSV1 | IWebSocketExtension.RSV3,
                compressedPayload.Slice(0, oneThird));
        ContinuationWebSocketFrame compressedFrame2 = new ContinuationWebSocketFrame(false,
                IWebSocketExtension.RSV3, compressedPayload.Slice(oneThird, oneThird));
        ContinuationWebSocketFrame compressedFrame3 = new ContinuationWebSocketFrame(true,
                IWebSocketExtension.RSV3, compressedPayload.Slice(oneThird * 2,
                        compressedPayload.ReadableBytes - oneThird * 2));

        // execute
        Assert.True(decoderChannel.WriteInbound(compressedFrame1.Retain()));
        Assert.True(decoderChannel.WriteInbound(compressedFrame2.Retain()));
        Assert.True(decoderChannel.WriteInbound(compressedFrame3));
        BinaryWebSocketFrame uncompressedFrame1 = decoderChannel.ReadInbound<BinaryWebSocketFrame>();
        ContinuationWebSocketFrame uncompressedFrame2 = decoderChannel.ReadInbound<ContinuationWebSocketFrame>();
        ContinuationWebSocketFrame uncompressedFrame3 = decoderChannel.ReadInbound<ContinuationWebSocketFrame>();

        // test
        Assert.NotNull(uncompressedFrame1);
        Assert.NotNull(uncompressedFrame2);
        Assert.NotNull(uncompressedFrame3);
        Assert.Equal(IWebSocketExtension.RSV3, uncompressedFrame1.Rsv());
        Assert.Equal(IWebSocketExtension.RSV3, uncompressedFrame2.Rsv());
        Assert.Equal(IWebSocketExtension.RSV3, uncompressedFrame3.Rsv());

        ByteBuf finalPayloadWrapped = Unpooled.WrappedBuffer(uncompressedFrame1.Content,
                uncompressedFrame2.Content, uncompressedFrame3.Content);
        Assert.Equal(300, finalPayloadWrapped.ReadableBytes);

        byte[] finalPayload = new byte[300];
        finalPayloadWrapped.ReadBytes(finalPayload);
        Assert.Equal(finalPayload, payload);
        finalPayloadWrapped.Release();
    }

    [Fact]
    public void TestMultiCompressedPayloadWithinFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerMessageDeflateDecoder(false, 0));

        // initialize
        byte[] payload1 = new byte[100];
        Random rng = new Random(random.Next());
        rng.NextBytes(payload1);
        byte[] payload2 = new byte[100];
        rng.NextBytes(payload2);

        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(payload1)));
        ByteBuf compressedPayload1 = encoderChannel.ReadOutbound<ByteBuf>();
        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(payload2)));
        ByteBuf compressedPayload2 = encoderChannel.ReadOutbound<ByteBuf>();

        BinaryWebSocketFrame compressedFrame = new BinaryWebSocketFrame(true,
                IWebSocketExtension.RSV1 | IWebSocketExtension.RSV3,
                Unpooled.WrappedBuffer(
                        compressedPayload1,
                        compressedPayload2.Slice(0, compressedPayload2.ReadableBytes - 4)));

        // execute
        Assert.True(decoderChannel.WriteInbound(compressedFrame));
        BinaryWebSocketFrame uncompressedFrame = decoderChannel.ReadInbound<BinaryWebSocketFrame>();

        // test
        Assert.NotNull(uncompressedFrame);
        Assert.NotNull(uncompressedFrame.Content);
        Assert.Equal(IWebSocketExtension.RSV3, uncompressedFrame.Rsv());
        Assert.Equal(200, uncompressedFrame.Content.ReadableBytes);

        byte[] finalPayload1 = new byte[100];
        uncompressedFrame.Content.ReadBytes(finalPayload1);
        Assert.Equal(finalPayload1, payload1);
        byte[] finalPayload2 = new byte[100];
        uncompressedFrame.Content.ReadBytes(finalPayload2);
        Assert.Equal(finalPayload2, payload2);
        uncompressedFrame.Release();
    }

    [Fact]
    public void TestDecompressionSkipForBinaryFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerMessageDeflateDecoder(false, ALWAYS_SKIP, 0));

        byte[] payload = new byte[300];
        random.NextBytes(payload);

        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(payload)));
        ByteBuf compressedPayload = encoderChannel.ReadOutbound<ByteBuf>();

        BinaryWebSocketFrame compressedBinaryFrame = new BinaryWebSocketFrame(true, IWebSocketExtension.RSV1,
                                                                              compressedPayload);
        Assert.True(decoderChannel.WriteInbound(compressedBinaryFrame));

        WebSocketFrame inboundFrame = decoderChannel.ReadInbound<WebSocketFrame>();

        Assert.Equal(IWebSocketExtension.RSV1, inboundFrame.Rsv());
        Assert.Equal(compressedPayload, inboundFrame.Content);
        Assert.True(inboundFrame.Release());

        Assert.True(encoderChannel.FinishAndReleaseAll());
        Assert.False(decoderChannel.Finish());
    }

    private sealed class TextShorterThan100Filter : IWebSocketExtensionFilter
    {
        public bool MustSkip(WebSocketFrame frame)
        {
            return frame is TextWebSocketFrame && frame.Content.ReadableBytes < 100;
        }
    }

    [Fact]
    public void TestSelectivityDecompressionSkip()
    {
        IWebSocketExtensionFilter selectivityDecompressionFilter = new TextShorterThan100Filter();
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(
                new PerMessageDeflateDecoder(false, selectivityDecompressionFilter, 0));

        string textPayload = "compressed payload";
        byte[] binaryPayload = new byte[300];
        random.NextBytes(binaryPayload);

        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(textPayload))));
        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(binaryPayload)));
        ByteBuf compressedTextPayload = encoderChannel.ReadOutbound<ByteBuf>();
        ByteBuf compressedBinaryPayload = encoderChannel.ReadOutbound<ByteBuf>();

        TextWebSocketFrame compressedTextFrame = new TextWebSocketFrame(true, IWebSocketExtension.RSV1,
                                                                        compressedTextPayload);
        BinaryWebSocketFrame compressedBinaryFrame = new BinaryWebSocketFrame(true, IWebSocketExtension.RSV1,
                                                                              compressedBinaryPayload);

        Assert.True(decoderChannel.WriteInbound(compressedTextFrame));
        Assert.True(decoderChannel.WriteInbound(compressedBinaryFrame));

        TextWebSocketFrame inboundTextFrame = decoderChannel.ReadInbound<TextWebSocketFrame>();
        BinaryWebSocketFrame inboundBinaryFrame = decoderChannel.ReadInbound<BinaryWebSocketFrame>();

        Assert.Equal(IWebSocketExtension.RSV1, inboundTextFrame.Rsv());
        Assert.Equal(compressedTextPayload, inboundTextFrame.Content);
        Assert.True(inboundTextFrame.Release());

        Assert.Equal(0, inboundBinaryFrame.Rsv());
        Assert.Equal(binaryPayload, ByteBufUtil.GetBytes(inboundBinaryFrame.Content));
        Assert.True(inboundBinaryFrame.Release());

        Assert.True(encoderChannel.FinishAndReleaseAll());
        Assert.False(decoderChannel.Finish());
    }

    private sealed class ShorterThan100Filter : IWebSocketExtensionFilter
    {
        public bool MustSkip(WebSocketFrame frame)
        {
            return frame.Content.ReadableBytes < 100;
        }
    }

    [Fact]
    public void TestIllegalStateWhenDecompressionInProgress()
    {
        IWebSocketExtensionFilter selectivityDecompressionFilter = new ShorterThan100Filter();

        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(
                new PerMessageDeflateDecoder(false, selectivityDecompressionFilter, 0));

        byte[] firstPayload = new byte[200];
        Random rng = new Random(random.Next());
        rng.NextBytes(firstPayload);

        byte[] finalPayload = new byte[50];
        rng.NextBytes(finalPayload);

        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(firstPayload)));
        Assert.True(encoderChannel.WriteOutbound(Unpooled.WrappedBuffer(finalPayload)));
        ByteBuf compressedFirstPayload = encoderChannel.ReadOutbound<ByteBuf>();
        ByteBuf compressedFinalPayload = encoderChannel.ReadOutbound<ByteBuf>();
        Assert.True(encoderChannel.FinishAndReleaseAll());

        BinaryWebSocketFrame firstPart = new BinaryWebSocketFrame(false, IWebSocketExtension.RSV1,
                                                                  compressedFirstPayload);
        ContinuationWebSocketFrame finalPart = new ContinuationWebSocketFrame(true, IWebSocketExtension.RSV1,
                                                                              compressedFinalPayload);
        Assert.True(decoderChannel.WriteInbound(firstPart));

        BinaryWebSocketFrame outboundFirstPart = decoderChannel.ReadInbound<BinaryWebSocketFrame>();
        //first part is decompressed
        Assert.Equal(0, outboundFirstPart.Rsv());
        Assert.Equal(firstPayload, ByteBufUtil.GetBytes(outboundFirstPart.Content));
        Assert.True(outboundFirstPart.Release());

        //final part throwing exception
        try
        {
            Assert.ThrowsAny<DecoderException>(() =>
            {
                decoderChannel.WriteInbound(finalPart);
            });
        }
        finally
        {
            Assert.True(finalPart.Release());
            Assert.False(encoderChannel.FinishAndReleaseAll());
        }
    }

    [Fact]
    public void TestEmptyFrameDecompression()
    {
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerMessageDeflateDecoder(false, 0));

        TextWebSocketFrame emptyDeflateBlockFrame = new TextWebSocketFrame(true, IWebSocketExtension.RSV1,
                                                                           EMPTY_DEFLATE_BLOCK.Duplicate());

        Assert.True(decoderChannel.WriteInbound(emptyDeflateBlockFrame));
        TextWebSocketFrame emptyBufferFrame = decoderChannel.ReadInbound<TextWebSocketFrame>();

        Assert.False(emptyBufferFrame.Content.IsReadable());

        // Composite empty buffer
        Assert.True(emptyBufferFrame.Release());
        Assert.False(decoderChannel.Finish());
    }

    [Fact]
    public void TestFragmentedFrameWithLeftOverInLastFragment()
    {
        string hexDump = "677170647a777a737574656b707a787a6f6a7561756578756f6b7868616371716c657a6d64697479766d726f6" +
                         "269746c6376777464776f6f72767a726f64667278676764687775786f6762766d776d706b76697773777a7072" +
                         "6a6a737279707a7078697a6c69616d7461656d646278626d786f66666e686e776a7a7461746d7a776668776b6" +
                         "f6f736e73746575637a6d727a7175707a6e74627578687871767771697a71766c64626d78726d6d7675756877" +
                         "62667963626b687a726d676e646263776e67797264706d6c6863626577616967706a78636a72697464756e627" +
                         "977616f79736475676f76736f7178746a7a7479626c64636b6b6778637768746c62";
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.NONE, 9, 15, 8));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerMessageDeflateDecoder(false, 0));

        ByteBuf originPayload = Unpooled.WrappedBuffer(ByteBufUtil.DecodeHexDump(hexDump));
        Assert.True(encoderChannel.WriteOutbound(originPayload.Duplicate().Retain()));

        ByteBuf compressedPayload = encoderChannel.ReadOutbound<ByteBuf>();
        compressedPayload = compressedPayload.Slice(0, compressedPayload.ReadableBytes - 4);

        int oneThird = compressedPayload.ReadableBytes / 3;

        TextWebSocketFrame compressedFrame1 = new TextWebSocketFrame(
                false, IWebSocketExtension.RSV1, compressedPayload.Slice(0, oneThird));
        ContinuationWebSocketFrame compressedFrame2 = new ContinuationWebSocketFrame(
                false, IWebSocketExtension.RSV3, compressedPayload.Slice(oneThird, oneThird));
        ContinuationWebSocketFrame compressedFrame3 = new ContinuationWebSocketFrame(
                false, IWebSocketExtension.RSV3, compressedPayload.Slice(oneThird * 2, oneThird));
        int offset = oneThird * 3;
        ContinuationWebSocketFrame compressedFrameWithExtraData = new ContinuationWebSocketFrame(
                true, IWebSocketExtension.RSV3, compressedPayload.Slice(offset,
                     compressedPayload.ReadableBytes - offset));

        // check that last fragment contains only one extra byte
        Assert.Equal(1, compressedFrameWithExtraData.Content.ReadableBytes);
        Assert.Equal(1, compressedFrameWithExtraData.Content.GetByte(0));

        // write compressed frames
        Assert.True(decoderChannel.WriteInbound(compressedFrame1.Retain()));
        Assert.True(decoderChannel.WriteInbound(compressedFrame2.Retain()));
        Assert.True(decoderChannel.WriteInbound(compressedFrame3.Retain()));
        Assert.True(decoderChannel.WriteInbound(compressedFrameWithExtraData));

        // read uncompressed frames
        TextWebSocketFrame uncompressedFrame1 = decoderChannel.ReadInbound<TextWebSocketFrame>();
        ContinuationWebSocketFrame uncompressedFrame2 = decoderChannel.ReadInbound<ContinuationWebSocketFrame>();
        ContinuationWebSocketFrame uncompressedFrame3 = decoderChannel.ReadInbound<ContinuationWebSocketFrame>();
        ContinuationWebSocketFrame uncompressedExtraData = decoderChannel.ReadInbound<ContinuationWebSocketFrame>();
        Assert.False(uncompressedExtraData.Content.IsReadable());

        ByteBuf uncompressedPayload = Unpooled.WrappedBuffer(uncompressedFrame1.Content, uncompressedFrame2.Content,
                                      uncompressedFrame3.Content, uncompressedExtraData.Content);
        Assert.Equal(originPayload, uncompressedPayload);

        Assert.True(originPayload.Release());
        Assert.True(uncompressedPayload.Release());

        Assert.True(encoderChannel.FinishAndReleaseAll());
        Assert.False(decoderChannel.Finish());
    }
}
