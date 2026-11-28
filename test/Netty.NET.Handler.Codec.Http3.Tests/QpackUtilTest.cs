/*
 * Copyright 2026 The Netty Project
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

using Netty.NET.Buffer;
using static Netty.NET.Handler.Codec.Http3.QpackUtil;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: The static imports of decodePrefixedInteger/encodePrefixedInteger are using static QpackUtil. The Hamcrest
// assertThat(reason, actual, is(expected)) is Assert.Equal(expected, actual, reason). assertEquals(int, long)
// widens the int as Java does (a long literal). The Java byte literals and (byte) casts are the unsigned CLR byte
// with the same eight bits ((byte) 0x80 is 0x80, (byte) 0x9a is 0x9a); decodePrefixedInteger masks every byte, so
// the sign of the Java byte does not matter. in is a C# keyword, so the buffer is @in.
// assertThrows(QpackException.class,...) is Assert.ThrowsAny<QpackException>.
public class QpackUtilTest
{

    // A remainder of exactly 128 while encoding a multi-byte prefixed integer must still be encoded as a
    // continuation byte (it does not fit in the final, non-continuation byte, which can only hold 0-127).
    // For a 6-bit prefix (nbits = 63) this first happens at value 191 (63 + 128).
    [Fact]
    public void EncodeDecodeRoundTripsAtContinuationByteBoundary()
    {
        ByteBuf buf = Unpooled.Buffer();

        try
        {
            for (int prefixLength = 1; prefixLength <= 8; prefixLength++)
            {
                int nbits = (1 << prefixLength) - 1;
                long value = nbits + 128;
                AssertRoundTrip(buf, prefixLength, value);
                buf.Clear();
            }
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void DecodesValueEncodedOnPrefixOnly()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] { 10 });

        try
        {
            Assert.Equal(10L, QpackUtil.DecodePrefixedInteger(@in, 5));
            Assert.Equal(0, @in.ReadableBytes);
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void EncodeDecodeRoundTripsOverWideRange()
    {
        ByteBuf buf = Unpooled.Buffer();

        try
        {
            foreach (int prefixLength in new int[] { 4, 5, 6, 7, 8 })
            {
                for (long value = 0; value <= 20_000; value++)
                {
                    AssertRoundTrip(buf, prefixLength, value);
                    buf.Clear();
                }
            }
        }
        finally
        {
            buf.Release();
        }
    }

    private static void AssertRoundTrip(ByteBuf buf, int prefixLength, long value)
    {
        EncodePrefixedInteger(buf, (byte) 0, prefixLength, value);
        Assert.Equal(value, DecodePrefixedInteger(buf, prefixLength),
            "Round trip failed for prefixLength=" + prefixLength + ", value=" + value);
    }

    [Fact]
    public void DecodesValueEncodedWithContinuationBytes()
    {
        // 5-bit prefix (nbits = 31), followed by continuation bytes encoding 1337 - 31 = 1306.
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] { 0x1f, 0x9a, 0x0a });

        try
        {
            Assert.Equal(1337L, QpackUtil.DecodePrefixedInteger(@in, 5));
            Assert.Equal(0, @in.ReadableBytes);
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void ReturnsMinusOneWhenNotEnoughReadableBytes()
    {
        // Prefix is all-ones (needs continuation), but no continuation byte is available yet.
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] { 0x1f });

        try
        {
            int readerIndex = @in.ReaderIndex;
            Assert.Equal(-1L, QpackUtil.DecodePrefixedInteger(@in, 5));
            // readerIndex must be reset so a subsequent decode attempt re-reads the same bytes.
            Assert.Equal(readerIndex, @in.ReaderIndex);
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void RejectsUnboundedContinuationByteRun()
    {
        // First byte with all prefix bits set, followed by a very long run of 0x80 continuation
        // bytes that never terminates (high bit never clear). Without a cap this would previously
        // make decodePrefixedInteger keep rescanning the buffer indefinitely, returning -1 forever
        // and letting the caller's cumulator grow without bound.
        byte[] bytes = new byte[4096];
        bytes[0] = 0x7f; // 7-bit prefix, all bits set.

        for (int i = 1; i < bytes.Length; i++)
        {
            bytes[i] = 0x80;
        }

        ByteBuf @in = Unpooled.WrappedBuffer(bytes);

        try
        {
            Assert.ThrowsAny<QpackException>(() => QpackUtil.DecodePrefixedInteger(@in, 7));
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void RejectsContinuationRunThatWouldOverflowLong()
    {
        // 9 continuation bytes (factor reaches 56) with the high bit always set is already enough
        // to trip the guard, even though the buffer is finite and would otherwise terminate.
        byte[] bytes = new byte[11];
        bytes[0] = 0x0f; // 4-bit prefix, all bits set.

        for (int i = 1; i < bytes.Length - 1; i++)
        {
            bytes[i] = 0x80;
        }

        bytes[bytes.Length - 1] = 0x01; // terminator, never reached because the guard fires first.
        ByteBuf @in = Unpooled.WrappedBuffer(bytes);

        try
        {
            Assert.ThrowsAny<QpackException>(() => QpackUtil.DecodePrefixedInteger(@in, 4));
        }
        finally
        {
            @in.Release();
        }
    }
}
