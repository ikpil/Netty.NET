/*
 * Copyright 2012 The Netty Project
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
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: CharsetUtil.US_ASCII is Encoding.ASCII, and Charset.defaultCharset() ("The default charset is UTF-8, unless
// changed in an implementation specific manner",
// https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/nio/charset/Charset.html#defaultCharset()) is
// Encoding.Default, which on .NET "Always returns a UTF8Encoding object"
// (https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.default). Numbers in messages use the invariant
// culture.
#pragma warning disable xUnit1069 // The two @Timeout bodies have no blocking or cancellable calls for the token to end.
public class DelimiterBasedFrameDecoderTest
{
    [Fact]
    public void TestMultipleLinesStrippedDelimiters()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new DelimiterBasedFrameDecoder(8192, true,
                Delimiters.LineDelimiter()));
        ch.WriteInbound(Unpooled.CopiedBuffer("TestLine\r\ng\r\n", Encoding.Default));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("TestLine", buf.ToString(Encoding.Default));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("g", buf2.ToString(Encoding.Default));
        Assert.Null(ch.ReadInbound());
        ch.Finish();

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestIncompleteLinesStrippedDelimiters()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new DelimiterBasedFrameDecoder(8192, true,
                Delimiters.LineDelimiter()));
        ch.WriteInbound(Unpooled.CopiedBuffer("Test", Encoding.Default));
        Assert.Null(ch.ReadInbound());
        ch.WriteInbound(Unpooled.CopiedBuffer("Line\r\ng\r\n", Encoding.Default));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("TestLine", buf.ToString(Encoding.Default));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("g", buf2.ToString(Encoding.Default));
        Assert.Null(ch.ReadInbound());
        ch.Finish();

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestMultipleLines()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new DelimiterBasedFrameDecoder(8192, false,
                Delimiters.LineDelimiter()));
        ch.WriteInbound(Unpooled.CopiedBuffer("TestLine\r\ng\r\n", Encoding.Default));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("TestLine\r\n", buf.ToString(Encoding.Default));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("g\r\n", buf2.ToString(Encoding.Default));
        Assert.Null(ch.ReadInbound());
        ch.Finish();

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestIncompleteLines()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new DelimiterBasedFrameDecoder(8192, false,
                Delimiters.LineDelimiter()));
        ch.WriteInbound(Unpooled.CopiedBuffer("Test", Encoding.Default));
        Assert.Null(ch.ReadInbound());
        ch.WriteInbound(Unpooled.CopiedBuffer("Line\r\ng\r\n", Encoding.Default));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("TestLine\r\n", buf.ToString(Encoding.Default));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("g\r\n", buf2.ToString(Encoding.Default));
        Assert.Null(ch.ReadInbound());
        ch.Finish();

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestDecode()
    {
        EmbeddedChannel ch = new EmbeddedChannel(
                new DelimiterBasedFrameDecoder(8192, true, Delimiters.LineDelimiter()));

        ch.WriteInbound(Unpooled.CopiedBuffer("first\r\nsecond\nthird", Encoding.ASCII));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("first", buf.ToString(Encoding.ASCII));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("second", buf2.ToString(Encoding.ASCII));
        Assert.Null(ch.ReadInbound());
        ch.Finish();

        ReferenceCountUtil.Release(ch.ReadInbound());

        buf.Release();
        buf2.Release();
    }

    /**
     * A multi-byte, non-line delimiter that straddles the resumed-scan boundary must still be
     * found. This guards the scan-offset optimization added to fix the quadratic rescanning of
     * {@link DelimiterBasedFrameDecoder#decode(ChannelHandlerContext, ByteBuf)}: the decoder must
     * not skip past a match whose bytes span the previously-scanned region and newly-arrived data.
     */
    [Fact]
    public void TestMultiByteDelimiterFoundAcrossResumedScanBoundary()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new DelimiterBasedFrameDecoder(
                8192, true, Unpooled.CopiedBuffer("###", Encoding.ASCII)));

        // "AB#" - contains only a one-byte prefix of the delimiter; nothing to emit yet.
        ch.WriteInbound(Unpooled.CopiedBuffer("AB#", Encoding.ASCII));
        Assert.Null(ch.ReadInbound());

        // "#" - now "AB##"; still only a two-byte prefix of the delimiter.
        ch.WriteInbound(Unpooled.CopiedBuffer("#", Encoding.ASCII));
        Assert.Null(ch.ReadInbound());

        // "#" - now "AB###"; the delimiter is completed and spans the resumed-scan boundary.
        ch.WriteInbound(Unpooled.CopiedBuffer("#", Encoding.ASCII));
        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("AB", buf.ToString(Encoding.ASCII));
        Assert.Null(ch.ReadInbound());
        buf.Release();

        // A second frame after the first still decodes correctly.
        ch.WriteInbound(Unpooled.CopiedBuffer("CD###", Encoding.ASCII));
        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("CD", buf2.ToString(Encoding.ASCII));
        buf2.Release();

        Assert.False(ch.Finish());
    }

    /**
     * Reproduces the DoS scenario: a peer streams delimiter-free bytes one at a time. Before the
     * fix, {@link DelimiterBasedFrameDecoder#decode(ChannelHandlerContext, ByteBuf)} rescanned the
     * whole accumulated buffer from the reader index on every call (no persisted scan-progress
     * offset), making total work quadratic in the number of bytes dripped. With the fix, work is
     * linear, so growing the input 8x should grow the runtime roughly 8x, not roughly 64x.
     */
    // CLR: @Timeout(60) is the Timeout of the [Fact], which xUnit v3 supports for non-async tests
    // (https://xunit.net/docs/getting-started/v3/whats-new).
    [Fact(Timeout = 60000)]
    public void TestDripFeedWithoutDelimiterDoesNotScaleQuadratically()
    {
        // Warm up the JIT so timing isn't dominated by interpretation/compilation.
        DripFeedNulDelimited(5_000);

        long smallElapsedNanos = DripFeedNulDelimited(25_000);
        long largeElapsedNanos = DripFeedNulDelimited(200_000);

        double ratio = (double) largeElapsedNanos / Math.Max(1, smallElapsedNanos);
        // Linear scaling predicts ~8x for 8x more input; quadratic scaling predicts ~64x.
        // Use a generous threshold so the test isn't flaky, while still catching a regression.
        Assert.True(ratio < 24,
                "runtime scaled by " + ratio.ToString(CultureInfo.InvariantCulture) +
                "x for 8x the input; expected roughly linear growth "
                        + "(small=" + smallElapsedNanos.ToString(CultureInfo.InvariantCulture) +
                        "ns, large=" + largeElapsedNanos.ToString(CultureInfo.InvariantCulture) + "ns)");
    }

    /**
     * With more than one delimiter, a single large buffer made of many tiny frames must not cause every frame to
     * rescan the rest of the buffer for the delimiters that did not match first.
     */
    // CLR: @Timeout(60) is the Timeout of the [Fact], which xUnit v3 supports for non-async tests
    // (https://xunit.net/docs/getting-started/v3/whats-new).
    [Fact(Timeout = 60000)]
    public void TestManyFramesInOneBufferWithMultipleDelimitersDoesNotScaleQuadratically()
    {
        // Warm up the JIT so timing isn't dominated by interpretation/compilation.
        DecodeAllEmptyFrames(5_000);

        long smallElapsedNanos = DecodeAllEmptyFrames(20_000);
        long largeElapsedNanos = DecodeAllEmptyFrames(160_000);

        double ratio = (double) largeElapsedNanos / Math.Max(1, smallElapsedNanos);
        // Linear scaling predicts ~8x for 8x more input; quadratic scaling predicts ~64x.
        Assert.True(ratio < 24,
                "runtime scaled by " + ratio.ToString(CultureInfo.InvariantCulture) +
                "x for 8x the input; expected roughly linear growth "
                        + "(small=" + smallElapsedNanos.ToString(CultureInfo.InvariantCulture) +
                        "ns, large=" + largeElapsedNanos.ToString(CultureInfo.InvariantCulture) + "ns)");
    }

    // CLR: System.nanoTime() differences ("can only be used to measure elapsed time",
    // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/System.html#nanoTime()) are
    // Stopwatch.GetElapsedTime since a Stopwatch.GetTimestamp() value
    // (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch.getelapsedtime) in TotalNanoseconds
    // (https://learn.microsoft.com/en-us/dotnet/api/system.timespan.totalnanoseconds).
    private static long DecodeAllEmptyFrames(int byteCount)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new DelimiterBasedFrameDecoder(
                int.MaxValue, Unpooled.CopiedBuffer("a", Encoding.ASCII),
                Unpooled.CopiedBuffer("b", Encoding.ASCII)));
        byte[] bytes = new byte[byteCount];
        Array.Fill(bytes, (byte) 'a');

        try
        {
            long start = Stopwatch.GetTimestamp();
            ch.WriteInbound(Unpooled.WrappedBuffer(bytes));
            long elapsed = (long) Stopwatch.GetElapsedTime(start).TotalNanoseconds;
            Assert.Equal(byteCount, ch.InboundMessages().Count);
            return elapsed;
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    /**
     * The window based search used for multiple delimiters must pick the same delimiter as a full search, wherever
     * the delimiters are placed relative to the window boundaries.
     */
    [Fact]
    public void TestMultipleDelimitersChooseShortestFrameAcrossWindowBoundaries()
    {
        for (int pos = 0; pos < 600; pos++)
        {
            string prefix = Repeat('z', pos);
            // The longer delimiter comes first, but the shorter one is found earlier.
            AssertSingleFrame(prefix, prefix + "x" + "abc" + "zzz");
            // The longer delimiter is found earlier.
            AssertSingleFrame(prefix, prefix + "abc" + "zzz" + "x");
            // The longer delimiter starts before "x" but only partially precedes it, so "x" wins.
            AssertSingleFrame(prefix + "ab", prefix + "ab" + "x");
            // Both delimiters present, the one starting at the same offset as another match is the first one.
            AssertSingleFrame(prefix, prefix + "abc");
        }
    }

    private static string Repeat(char c, int count)
    {
        char[] chars = new char[count];
        Array.Fill(chars, c);

        return new string(chars);
    }

    private static void AssertSingleFrame(string expectedFrame, string input)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new DelimiterBasedFrameDecoder(
                int.MaxValue, Unpooled.CopiedBuffer("abc", Encoding.ASCII),
                Unpooled.CopiedBuffer("x", Encoding.ASCII)));

        try
        {
            ch.WriteInbound(Unpooled.CopiedBuffer(input, Encoding.ASCII));
            ByteBuf frame = ch.ReadInbound<ByteBuf>();
            Assert.Equal(expectedFrame, frame.ToString(Encoding.ASCII), "input: " + input);
            frame.Release();
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    // CLR: System.nanoTime() differences are Stopwatch.GetElapsedTime, as in DecodeAllEmptyFrames.
    private static long DripFeedNulDelimited(int byteCount)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new DelimiterBasedFrameDecoder(
                int.MaxValue, Delimiters.NulDelimiter()));
        ByteBuf single = Unpooled.Buffer(1);

        try
        {
            long start = Stopwatch.GetTimestamp();

            for (int i = 0; i < byteCount; i++)
            {
                single.SetIndex(0, 0);
                single.WriteByte('a');
                ch.WriteInbound(single.RetainedDuplicate());
                ReferenceCountUtil.Release(ch.ReadInbound());
            }

            return (long) Stopwatch.GetElapsedTime(start).TotalNanoseconds;
        }
        finally
        {
            single.Release();
            ch.FinishAndReleaseAll();
        }
    }
}
