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
using System.Linq;
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

// CLR: WebSocketExtension.RSV1/RSV3 are the constants of IWebSocketExtension; the static imports of
// ALWAYS_SKIP/NEVER_SKIP and EMPTY_DEFLATE_BLOCK are using static directives of IWebSocketExtensionFilter and
// DeflateDecoder, and CharsetUtil.UTF_8 is Encoding.UTF8 (docs/codec-smtp-porting.md). The anonymous
// WebSocketExtensionFilter classes are the named nested classes DataShorterThan100Filter and ShorterThan100Filter.
// java.util.Random and the SplittableRandom seeded from it are an unseeded System.Random filled with NextBytes (the
// payloads are arbitrary). assertThrows(T.class, Executable) is Assert.ThrowsAny<T> with a lambda, AssertJ
// assertThat(a).isEqualTo(b) is Assert.Equal(b, a), Arrays.equals is SequenceEqual, @ParameterizedTest @ValueSource
// (booleans) is a [Theory] with InlineData rows, and StringBuilder is System.Text.StringBuilder.
public class PerMessageDeflateEncoderTest
{

    private static readonly Random random = new Random();

    [Fact]
    public void TestCompressedFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerMessageDeflateEncoder(9, 15, false));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.NONE, 0));

        // initialize
        byte[] payload = new byte[300];
        random.NextBytes(payload);
        BinaryWebSocketFrame frame = new BinaryWebSocketFrame(true,
                                                              IWebSocketExtension.RSV3,
                                                              Unpooled.WrappedBuffer(payload));

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
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerMessageDeflateEncoder(9, 15, false));

        // initialize
        byte[] payload = new byte[300];
        random.NextBytes(payload);

        BinaryWebSocketFrame frame = new BinaryWebSocketFrame(true,
                                                              IWebSocketExtension.RSV3 | IWebSocketExtension.RSV1,
                                                              Unpooled.WrappedBuffer(payload));

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
    public void TestFragmentedFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerMessageDeflateEncoder(9, 15, false,
                                                                                          NEVER_SKIP));
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
                                                               IWebSocketExtension.RSV3,
                                                               Unpooled.WrappedBuffer(payload1));
        ContinuationWebSocketFrame frame2 = new ContinuationWebSocketFrame(false,
                                                                           IWebSocketExtension.RSV3,
                                                                           Unpooled.WrappedBuffer(payload2));
        ContinuationWebSocketFrame frame3 = new ContinuationWebSocketFrame(true,
                                                                           IWebSocketExtension.RSV3,
                                                                           Unpooled.WrappedBuffer(payload3));

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
        Assert.Equal(IWebSocketExtension.RSV3, compressedFrame2.Rsv());
        Assert.Equal(IWebSocketExtension.RSV3, compressedFrame3.Rsv());
        Assert.False(compressedFrame1.IsFinalFragment());
        Assert.False(compressedFrame2.IsFinalFragment());
        Assert.True(compressedFrame3.IsFinalFragment());

        Assert.True(decoderChannel.WriteInbound(compressedFrame1.Content));
        ByteBuf uncompressedPayload1 = decoderChannel.ReadInbound<ByteBuf>();
        byte[] finalPayload1 = new byte[100];
        uncompressedPayload1.ReadBytes(finalPayload1);
        Assert.Equal(finalPayload1, payload1);
        uncompressedPayload1.Release();

        Assert.True(decoderChannel.WriteInbound(compressedFrame2.Content));
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
    public void TestCompressionSkipForBinaryFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerMessageDeflateEncoder(9, 15, false,
                                                                                          ALWAYS_SKIP));
        byte[] payload = new byte[300];
        random.NextBytes(payload);

        WebSocketFrame binaryFrame = new BinaryWebSocketFrame(Unpooled.WrappedBuffer(payload));

        Assert.True(encoderChannel.WriteOutbound(binaryFrame.Copy()));
        WebSocketFrame outboundFrame = encoderChannel.ReadOutbound<WebSocketFrame>();

        Assert.Equal(0, outboundFrame.Rsv());
        Assert.Equal(payload, ByteBufUtil.GetBytes(outboundFrame.Content));
        Assert.True(outboundFrame.Release());

        Assert.False(encoderChannel.Finish());
    }

    private sealed class DataShorterThan100Filter : IWebSocketExtensionFilter
    {
        public bool MustSkip(WebSocketFrame frame)
        {
            return  (frame is TextWebSocketFrame || frame is BinaryWebSocketFrame)
                && frame.Content.ReadableBytes < 100;
        }
    }

    [Fact]
    public void TestSelectivityCompressionSkip()
    {
        IWebSocketExtensionFilter selectivityCompressionFilter = new DataShorterThan100Filter();
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                new PerMessageDeflateEncoder(9, 15, false, selectivityCompressionFilter));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.NONE, 0));

        string textPayload = "not compressed payload";
        byte[] binaryPayload = new byte[101];
        random.NextBytes(binaryPayload);

        WebSocketFrame textFrame = new TextWebSocketFrame(textPayload);
        BinaryWebSocketFrame binaryFrame = new BinaryWebSocketFrame(Unpooled.WrappedBuffer(binaryPayload));

        Assert.True(encoderChannel.WriteOutbound(textFrame));
        Assert.True(encoderChannel.WriteOutbound(binaryFrame));

        WebSocketFrame outboundTextFrame = encoderChannel.ReadOutbound<WebSocketFrame>();

        //compression skipped for textFrame
        Assert.Equal(0, outboundTextFrame.Rsv());
        Assert.Equal(textPayload, outboundTextFrame.Content.ToString(Encoding.UTF8));
        Assert.True(outboundTextFrame.Release());

        WebSocketFrame outboundBinaryFrame = encoderChannel.ReadOutbound<WebSocketFrame>();

        //compression not skipped for binaryFrame
        Assert.Equal(IWebSocketExtension.RSV1, outboundBinaryFrame.Rsv());

        Assert.True(decoderChannel.WriteInbound(outboundBinaryFrame.Content.Retain()));
        ByteBuf uncompressedBinaryPayload = decoderChannel.ReadInbound<ByteBuf>();

        Assert.Equal(binaryPayload, ByteBufUtil.GetBytes(uncompressedBinaryPayload));

        Assert.True(outboundBinaryFrame.Release());
        Assert.True(uncompressedBinaryPayload.Release());

        Assert.False(encoderChannel.Finish());
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
    public void TestIllegalStateWhenCompressionInProgress()
    {
        IWebSocketExtensionFilter selectivityCompressionFilter = new ShorterThan100Filter();
        EmbeddedChannel encoderChannel = new EmbeddedChannel(
                new PerMessageDeflateEncoder(9, 15, false, selectivityCompressionFilter));

        byte[] firstPayload = new byte[200];
        Random rng = new Random(random.Next());
        rng.NextBytes(firstPayload);

        byte[] finalPayload = new byte[90];
        rng.NextBytes(finalPayload);

        BinaryWebSocketFrame firstPart = new BinaryWebSocketFrame(false, 0, Unpooled.WrappedBuffer(firstPayload));
        ContinuationWebSocketFrame finalPart = new ContinuationWebSocketFrame(true, 0,
                                                                              Unpooled.WrappedBuffer(finalPayload));
        Assert.True(encoderChannel.WriteOutbound(firstPart));

        BinaryWebSocketFrame outboundFirstPart = encoderChannel.ReadOutbound<BinaryWebSocketFrame>();
        //first part is compressed
        Assert.Equal(IWebSocketExtension.RSV1, outboundFirstPart.Rsv());
        Assert.False(firstPayload.SequenceEqual(ByteBufUtil.GetBytes(outboundFirstPart.Content)));
        Assert.True(outboundFirstPart.Release());

        //final part throwing exception
        try
        {
            Assert.ThrowsAny<EncoderException>(() =>
            {
                encoderChannel.WriteOutbound(finalPart);
            });
        }
        finally
        {
            Assert.True(finalPart.Release());
            Assert.False(encoderChannel.FinishAndReleaseAll());
        }
    }

    [Fact]
    public void TestEmptyFrameCompression()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerMessageDeflateEncoder(9, 15, false));

        TextWebSocketFrame emptyFrame = new TextWebSocketFrame("");

        Assert.True(encoderChannel.WriteOutbound(emptyFrame));
        TextWebSocketFrame emptyDeflateFrame = encoderChannel.ReadOutbound<TextWebSocketFrame>();

        Assert.Equal(IWebSocketExtension.RSV1, emptyDeflateFrame.Rsv());
        Assert.True(ByteBufUtil.Equals(EMPTY_DEFLATE_BLOCK, emptyDeflateFrame.Content));
        // Unreleasable buffer
        Assert.False(emptyDeflateFrame.Release());

        Assert.False(encoderChannel.Finish());
    }

    [Fact]
    public void TestCodecExceptionForNotFinEmptyFrame()
    {
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerMessageDeflateEncoder(9, 15, false));

        TextWebSocketFrame emptyNotFinFrame = new TextWebSocketFrame(false, 0, "");

        try
        {
            Assert.ThrowsAny<EncoderException>(() =>
            {
                encoderChannel.WriteOutbound(emptyNotFinFrame);
            });
        }
        finally
        {
            // EmptyByteBuf buffer
            Assert.False(emptyNotFinFrame.Release());
            Assert.False(encoderChannel.Finish());
        }
    }

    [Fact]
    public void TestNoContextNextMessageAfterEmptyFinalFragmentIsSelfContained()
    {
        string text = "Hello world, hello world, hello world!";
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerMessageDeflateEncoder(9, 15, true));
        WebSocketFrame[] frames = EncodeMessageWithEmptyFinalFragmentThenMessage(encoderChannel, text);
        frames[0].Release();
        frames[1].Release();

        // With no context takeover the peer may use an empty sliding window for every message,
        // so the second message must decompress on its own.
        EmbeddedChannel decoderChannel = new EmbeddedChannel(
                ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.NONE, 0));
        decoderChannel.WriteInbound(frames[2].Content);
        decoderChannel.WriteInbound(DeflateDecoder.FRAME_TAIL.Duplicate());
        ByteBuf uncompressed = decoderChannel.ReadInbound<ByteBuf>();
        Assert.Equal(text, uncompressed.ToString(Encoding.UTF8));
        uncompressed.Release();

        Assert.False(encoderChannel.Finish());
        Assert.False(decoderChannel.Finish());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestRoundTripWithEmptyFinalFragment(bool noContext)
    {
        string text = "Hello world, hello world, hello world!";
        EmbeddedChannel encoderChannel = new EmbeddedChannel(new PerMessageDeflateEncoder(9, 15, noContext));
        EmbeddedChannel decoderChannel = new EmbeddedChannel(new PerMessageDeflateDecoder(noContext, 0));

        StringBuilder messages = new StringBuilder();
        foreach (WebSocketFrame frame in EncodeMessageWithEmptyFinalFragmentThenMessage(encoderChannel, text))
        {
            decoderChannel.WriteInbound(frame);
            for (;;)
            {
                WebSocketFrame decoded = decoderChannel.ReadInbound<WebSocketFrame>();
                if (decoded == null)
                {
                    break;
                }
                messages.Append(decoded.Content.ToString(Encoding.UTF8));
                if (decoded.IsFinalFragment())
                {
                    messages.Append('|');
                }
                decoded.Release();
            }
        }
        Assert.Equal(text + '|' + text + '|', messages.ToString());

        Assert.False(encoderChannel.Finish());
        Assert.False(decoderChannel.Finish());
    }

    /**
     * Encodes a message whose size is not known up front: its content, then an empty final fragment
     * (<a href="https://tools.ietf.org/html/rfc7692#section-7.2.3.6">RFC 7692, 7.2.3.6</a>). Then it encodes a
     * second message with the same content.
     */
    private static WebSocketFrame[] EncodeMessageWithEmptyFinalFragmentThenMessage(
            EmbeddedChannel encoderChannel, string text)
    {
        Assert.True(encoderChannel.WriteOutbound(new TextWebSocketFrame(false, 0, text)));
        Assert.True(encoderChannel.WriteOutbound(new ContinuationWebSocketFrame(true, 0, Unpooled.EmptyBuffer)));
        Assert.True(encoderChannel.WriteOutbound(new TextWebSocketFrame(true, 0, text)));
        WebSocketFrame[] frames = new WebSocketFrame[3];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = encoderChannel.ReadOutbound<WebSocketFrame>();
            Assert.NotNull(frames[i]);
        }
        Assert.True(ByteBufUtil.Equals(EMPTY_DEFLATE_BLOCK, frames[1].Content));
        return frames;
    }
}
