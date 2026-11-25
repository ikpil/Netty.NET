/*
 * Copyright 2015 The Netty Project
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

/*
 * Copyright 2014 Twitter, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Globalization;
using System.Text;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.HpackDecoder;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.IHttp2HeadersEncoder;
using static Netty.NET.Common.AsciiString;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: @BeforeEach is the constructor ("xUnit creates a new instance of the test class for every test that is run",
// docs/codec-http-porting.md, Shared CLR adaptations), which calls the Java-named setUp(). mock(Http2Headers.class) is a
// loose Moq mock (Mock<IHttp2Headers>, its Object passed to the decoder); verify(mock, times(n)).add(a, b) is
// Verify(m => m.Add(Cs(a), Cs(b)), Times.Exactly(n)), verifyNoMoreInteractions is VerifyNoOtherCalls, reset is Reset, and Moq
// matches the AsciiString arguments with Equals as Mockito does (https://github.com/devlooped/moq/wiki/Quickstart,
// recorded in docs/codec-http-porting.md). AsciiString.of(CharSequence) with a String is the private Of(string), and
// the Strings that Java passes as CharSequence to headers are StringCharSequence values (the private Cs(string)). The
// anonymous Executables are lambdas and assertThrows(T.class) is Assert.ThrowsAny<T>. @CsvSource rows are InlineData
// rows; '' is the empty string. String.format("%0100d", 0) is 0.ToString("D100") (zero-padded to 100 digits).
// String.getBytes() is Encoding.Default (UTF-8; the strings are ASCII). assertThat(a).isGreaterThanOrEqualTo(b) is
// Assert.True(a >= b). Integer.MAX_VALUE is int.MaxValue. Header equality calls Equals directly
// (docs/codec-http-porting.md, Shared CLR adaptations).
public class HpackDecoderTest
{

    private HpackDecoder hpackDecoder;
    private Mock<IHttp2Headers> mockHeaders;

    private static string Hex(string s)
    {
        return StringUtil.ToHexString(Encoding.Default.GetBytes(s));
    }

    private void Decode(string encoded)
    {
        byte[] b = StringUtil.DecodeHexDump(encoded);
        ByteBuf @in = Unpooled.WrappedBuffer(b);
        try
        {
            hpackDecoder.Decode(0, @in, mockHeaders.Object, true);
        }
        finally
        {
            @in.Release();
        }
    }

    public HpackDecoderTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        hpackDecoder = new HpackDecoder(8192);
        mockHeaders = new Mock<IHttp2Headers>();
    }

    [Fact]
    public void TestDecodeULE128IntMax()
    {
        byte[] input = {(byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0x07};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        try
        {
            Assert.Equal(int.MaxValue, DecodeULE128(@in, 0));
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestDecodeULE128IntOverflow1()
    {
        byte[] input = {(byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0x07};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        int readerIndex = @in.ReaderIndex;
        try
        {
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                DecodeULE128(@in, 1);
            });
        }
        finally
        {
            Assert.Equal(readerIndex, @in.ReaderIndex);
            @in.Release();
        }
    }

    [Fact]
    public void TestDecodeULE128IntOverflow2()
    {
        byte[] input = {(byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0x08};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        int readerIndex = @in.ReaderIndex;
        try
        {
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                DecodeULE128(@in, 0);
            });
        }
        finally
        {
            Assert.Equal(readerIndex, @in.ReaderIndex);
            @in.Release();
        }
    }

    [Fact]
    public void TestDecodeULE128LongMax()
    {
        byte[] input = {(byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF,
                        (byte) 0xFF, (byte) 0x7F};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        try
        {
            Assert.Equal(long.MaxValue, DecodeULE128(@in, 0L));
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestDecodeULE128LongOverflow1()
    {
        byte[] input = {(byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF,
                        (byte) 0xFF, (byte) 0xFF};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        int readerIndex = @in.ReaderIndex;
        try
        {
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                DecodeULE128(@in, 0L);
            });
        }
        finally
        {
            Assert.Equal(readerIndex, @in.ReaderIndex);
            @in.Release();
        }
    }

    [Fact]
    public void TestDecodeULE128LongOverflow2()
    {
        byte[] input = {(byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF,
                        (byte) 0xFF, (byte) 0x7F};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        int readerIndex = @in.ReaderIndex;
        try
        {
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                DecodeULE128(@in, 1L);
            });
        }
        finally
        {
            Assert.Equal(readerIndex, @in.ReaderIndex);
            @in.Release();
        }
    }

    [Fact]
    public void TestSetTableSizeWithMaxUnsigned32BitValueSucceeds()
    {
        byte[] input = {(byte) 0x3F, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0x0E};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        try
        {
            long expectedHeaderSize = 4026531870L; // based on the input above
            hpackDecoder.SetMaxHeaderTableSize(expectedHeaderSize);
            hpackDecoder.Decode(0, @in, mockHeaders.Object, true);
            Assert.Equal(expectedHeaderSize, hpackDecoder.GetMaxHeaderTableSize());
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestSetTableSizeOverLimitFails()
    {
        byte[] input = {(byte) 0x3F, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0x0E};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        try
        {
            hpackDecoder.SetMaxHeaderTableSize(4026531870L - 1); // based on the input above ... 1 less than is above.
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                hpackDecoder.Decode(0, @in, mockHeaders.Object, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestLiteralHuffmanEncodedWithEmptyNameAndValue()
    {
        byte[] input = {0, (byte) 0x80, 0};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        try
        {
            hpackDecoder.Decode(0, @in, mockHeaders.Object, true);
            mockHeaders.Verify(m => m.Add(EMPTY_STRING, EMPTY_STRING), Times.Exactly(1));
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestLiteralHuffmanEncodedWithPaddingGreaterThan7Throws()
    {
        // CLR: the Java byte literal -1 is the octet 0xFF, written as unchecked((byte) -1).
        byte[] input = {0, (byte) 0x81, unchecked((byte) -1)};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        try
        {
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                hpackDecoder.Decode(0, @in, mockHeaders.Object, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestLiteralHuffmanEncodedWithDecodingEOSThrows()
    {
        byte[] input = {0, (byte) 0x84, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF, (byte) 0xFF};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        try
        {
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                hpackDecoder.Decode(0, @in, mockHeaders.Object, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestLiteralHuffmanEncodedWithPaddingNotCorrespondingToMSBThrows()
    {
        byte[] input = {0, (byte) 0x81, 0};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        try
        {
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                hpackDecoder.Decode(0, @in, mockHeaders.Object, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestIncompleteIndex()
    {
        byte[] compressed = StringUtil.DecodeHexDump("FFF0");
        ByteBuf @in = Unpooled.WrappedBuffer(compressed);
        try
        {
            Assert.Equal(2, @in.ReadableBytes);
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                hpackDecoder.Decode(0, @in, mockHeaders.Object, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestUnusedIndex()
    {
        // Index 0 is not used
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode("80");
        });
    }

    [Fact]
    public void TestIllegalIndex()
    {
        // Index larger than the header table
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode("FF00");
        });
    }

    [Fact]
    public void TestInsidiousIndex()
    {
        // Insidious index so the last shift causes sign overflow
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode("FF8080808007");
        });
    }

    [Fact]
    public void TestDynamicTableSizeUpdate()
    {
        Decode("20");
        Assert.Equal(0, hpackDecoder.GetMaxHeaderTableSize());
        Decode("3FE11F");
        Assert.Equal(4096, hpackDecoder.GetMaxHeaderTableSize());
    }

    [Fact]
    public void TestDynamicTableSizeUpdateRequired()
    {
        hpackDecoder.SetMaxHeaderTableSize(32);
        Decode("3F00");
        Assert.Equal(31, hpackDecoder.GetMaxHeaderTableSize());
    }

    [Fact]
    public void TestIllegalDynamicTableSizeUpdate()
    {
        // max header table size = MAX_HEADER_TABLE_SIZE + 1
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode("3FE21F");
        });
    }

    [Fact]
    public void TestInsidiousMaxDynamicTableSize()
    {
        hpackDecoder.SetMaxHeaderTableSize(int.MaxValue);
        // max header table size sign overflow
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode("3FE1FFFFFF07");
        });
    }

    [Fact]
    public void TestMaxValidDynamicTableSize()
    {
        hpackDecoder.SetMaxHeaderTableSize(int.MaxValue);
        string baseValue = "3FE1FFFFFF0";
        for (int i = 0; i < 7; ++i)
        {
            Decode(baseValue + i);
        }
    }

    [Fact]
    public void TestReduceMaxDynamicTableSize()
    {
        hpackDecoder.SetMaxHeaderTableSize(0);
        Assert.Equal(0, hpackDecoder.GetMaxHeaderTableSize());
        Decode("2081");
    }

    [Fact]
    public void TestTooLargeDynamicTableSizeUpdate()
    {
        hpackDecoder.SetMaxHeaderTableSize(0);
        Assert.Equal(0, hpackDecoder.GetMaxHeaderTableSize());
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode("21"); // encoder max header table size not small enough
        });
    }

    [Fact]
    public void TestMissingDynamicTableSizeUpdate()
    {
        hpackDecoder.SetMaxHeaderTableSize(0);
        Assert.Equal(0, hpackDecoder.GetMaxHeaderTableSize());
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode("81");
        });
    }

    [Fact]
    public void TestDynamicTableSizeUpdateAfterTheBeginningOfTheBlock()
    {
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode("8120");
        });
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode("813FE11F");
        });
    }

    [Fact]
    public void TestLiteralWithIncrementalIndexingWithEmptyName()
    {
        Decode("400005" + Hex("value"));
        mockHeaders.Verify(m => m.Add(EMPTY_STRING, Of("value")), Times.Exactly(1));
    }

    [Fact]
    public void TestLiteralWithIncrementalIndexingCompleteEviction()
    {
        // Verify indexed host header
        Decode("4004" + Hex("name") + "05" + Hex("value"));
        mockHeaders.Verify(m => m.Add(Of("name"), Of("value")));
        mockHeaders.VerifyNoOtherCalls();

        mockHeaders.Reset();
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 4096; i++)
        {
            sb.Append('a');
        }
        string value = sb.ToString();
        sb = new StringBuilder();
        sb.Append("417F811F");
        for (int i = 0; i < 4096; i++)
        {
            sb.Append("61"); // 'a'
        }
        Decode(sb.ToString());
        mockHeaders.Verify(m => m.Add(Of(":authority"), Of(value)));
        // CLR: Mockito's loop over mockingDetails(...).getInvocations() marks the calls of authority() and of
        // contains(":authority") as verified; Moq marks the invocations that a Verify matches as verified, and
        // Times.AtMost(int.MaxValue) accepts any number of them.
        mockHeaders.Verify(m => m.Authority(), Times.AtMost(int.MaxValue));
        mockHeaders.Verify(m => m.Contains(Of(":authority")), Times.AtMost(int.MaxValue));
        mockHeaders.VerifyNoOtherCalls();
        mockHeaders.Reset();

        // Verify next header is inserted at index 62
        Decode("4004" + Hex("name") + "05" + Hex("value") + "BE");
        mockHeaders.Verify(m => m.Add(Of("name"), Of("value")), Times.Exactly(2));
        mockHeaders.VerifyNoOtherCalls();
    }

    [Fact]
    public void TestLiteralWithIncrementalIndexingWithLargeValue()
    {
        // Ignore header that exceeds max header size
        StringBuilder sb = new StringBuilder();
        sb.Append("4004");
        sb.Append(Hex("name"));
        sb.Append("7F813F");
        for (int i = 0; i < 8192; i++)
        {
            sb.Append("61"); // 'a'
        }
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(sb.ToString());
        });
    }

    [Fact]
    public void TestLiteralWithoutIndexingWithEmptyName()
    {
        Decode("000005" + Hex("value"));
        mockHeaders.Verify(m => m.Add(EMPTY_STRING, Of("value")), Times.Exactly(1));
    }

    [Fact]
    public void TestLiteralWithoutIndexingWithLargeName()
    {
        // Ignore header name that exceeds max header size
        StringBuilder sb = new StringBuilder();
        sb.Append("007F817F");
        for (int i = 0; i < 16384; i++)
        {
            sb.Append("61"); // 'a'
        }
        sb.Append("00");
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(sb.ToString());
        });
    }

    [Fact]
    public void TestLiteralWithoutIndexingWithLargeValue()
    {
        // Ignore header that exceeds max header size
        StringBuilder sb = new StringBuilder();
        sb.Append("0004");
        sb.Append(Hex("name"));
        sb.Append("7F813F");
        for (int i = 0; i < 8192; i++)
        {
            sb.Append("61"); // 'a'
        }
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(sb.ToString());
        });
    }

    [Fact]
    public void TestLiteralNeverIndexedWithEmptyName()
    {
        Decode("100005" + Hex("value"));
        mockHeaders.Verify(m => m.Add(EMPTY_STRING, Of("value")), Times.Exactly(1));
    }

    [Fact]
    public void TestLiteralNeverIndexedWithLargeName()
    {
        // Ignore header name that exceeds max header size
        StringBuilder sb = new StringBuilder();
        sb.Append("107F817F");
        for (int i = 0; i < 16384; i++)
        {
            sb.Append("61"); // 'a'
        }
        sb.Append("00");
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(sb.ToString());
        });
    }

    [Fact]
    public void TestLiteralNeverIndexedWithLargeValue()
    {
        // Ignore header that exceeds max header size
        StringBuilder sb = new StringBuilder();
        sb.Append("1004");
        sb.Append(Hex("name"));
        sb.Append("7F813F");
        for (int i = 0; i < 8192; i++)
        {
            sb.Append("61"); // 'a'
        }
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(sb.ToString());
        });
    }

    [Fact]
    public void TestDecodeLargerThanMaxHeaderListSizeUpdatesDynamicTable()
    {
        ByteBuf @in = Unpooled.Buffer(300);
        try
        {
            hpackDecoder.SetMaxHeaderListSize(200);
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            // encode headers that are slightly larger than maxHeaderListSize
            IHttp2Headers toEncode = new DefaultHttp2Headers();
            toEncode.Add(Cs("test_1"), Cs("1"));
            toEncode.Add(Cs("test_2"), Cs("2"));
            toEncode.Add(Cs("long"), Cs(0.ToString("D100", CultureInfo.InvariantCulture).Replace('0', 'A')));
            toEncode.Add(Cs("test_3"), Cs("3"));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            // decode the headers, we should get an exception
            IHttp2Headers decoded = new DefaultHttp2Headers();
            Assert.ThrowsAny<Http2Exception.HeaderListSizeException>(() =>
            {
                hpackDecoder.Decode(1, @in, decoded, true);
            });

            // but the dynamic table should have been updated, so that later blocks
            // can refer to earlier headers
            @in.Clear();
            // 0x80, "indexed header field representation"
            // index 62, the first (most recent) dynamic table entry
            @in.WriteByte(0x80 | 62);
            IHttp2Headers decoded2 = new DefaultHttp2Headers();
            hpackDecoder.Decode(1, @in, decoded2, true);

            IHttp2Headers golden = new DefaultHttp2Headers();
            golden.Add(Cs("test_3"), Cs("3"));
            Assert.True(golden.Equals(decoded2));
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestDecodeCountsNamesOnlyOnce()
    {
        ByteBuf @in = Unpooled.Buffer(200);
        try
        {
            hpackDecoder.SetMaxHeaderListSize(3500);
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            // encode headers that are slightly larger than maxHeaderListSize
            IHttp2Headers toEncode = new DefaultHttp2Headers();
            toEncode.Add(Cs(0.ToString("D3000", CultureInfo.InvariantCulture).Replace('0', 'f')), Cs("value"));
            toEncode.Add(Cs("accept"), Cs("value"));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers();
            hpackDecoder.Decode(1, @in, decoded, true);
            Assert.Equal(2, decoded.Size());
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestAccountForHeaderOverhead()
    {
        ByteBuf @in = Unpooled.Buffer(100);
        try
        {
            string headerName = "12345";
            string headerValue = "56789";
            long headerSize = headerName.Length + headerValue.Length;
            hpackDecoder.SetMaxHeaderListSize(headerSize);
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new DefaultHttp2Headers();
            toEncode.Add(Cs(headerName), Cs(headerValue));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers();

            // SETTINGS_MAX_HEADER_LIST_SIZE is big enough for the header to fit...
            Assert.True(hpackDecoder.GetMaxHeaderListSize() >= headerSize);

            // ... but decode should fail because we add some overhead for each header entry
            Assert.ThrowsAny<Http2Exception.HeaderListSizeException>(() =>
            {
                hpackDecoder.Decode(1, @in, decoded, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void TestIncompleteHeaderFieldRepresentation()
    {
        // Incomplete Literal Header Field with Incremental Indexing
        byte[] input = {(byte) 0x40};
        ByteBuf @in = Unpooled.WrappedBuffer(input);
        try
        {
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                hpackDecoder.Decode(0, @in, mockHeaders.Object, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void UnknownPseudoHeader()
    {
        ByteBuf @in = Unpooled.Buffer(200);
        try
        {
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new DefaultHttp2Headers(false);
            toEncode.Add(Cs(":test"), Cs("1"));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers(true);

            Assert.ThrowsAny<Http2Exception.StreamException>(() =>
            {
                hpackDecoder.Decode(1, @in, decoded, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void DisableHeaderValidation()
    {
        ByteBuf @in = Unpooled.Buffer(200);
        try
        {
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new DefaultHttp2Headers(false);
            toEncode.Add(Cs(":test"), Cs("1"));
            toEncode.Add(Cs(":status"), Cs("200"));
            toEncode.Add(Cs(":method"), Cs("GET"));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers(false);

            hpackDecoder.Decode(1, @in, decoded, false);

            Assert.Equal("1", decoded.ValueIterator(Cs(":test")).Next().ToString());
            Assert.Equal("200", decoded.Status().ToString());
            Assert.Equal("GET", decoded.Method().ToString());
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void RequestPseudoHeaderInResponse()
    {
        ByteBuf @in = Unpooled.Buffer(200);
        try
        {
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new DefaultHttp2Headers();
            toEncode.Add(Cs(":status"), Cs("200"));
            toEncode.Add(Cs(":method"), Cs("GET"));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers();

            Assert.ThrowsAny<Http2Exception.StreamException>(() =>
            {
                hpackDecoder.Decode(1, @in, decoded, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void ResponsePseudoHeaderInRequest()
    {
        ByteBuf @in = Unpooled.Buffer(200);
        try
        {
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new DefaultHttp2Headers();
            toEncode.Add(Cs(":method"), Cs("GET"));
            toEncode.Add(Cs(":status"), Cs("200"));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers();

            Assert.ThrowsAny<Http2Exception.StreamException>(() =>
            {
                hpackDecoder.Decode(1, @in, decoded, true);
            });
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void PseudoHeaderAfterRegularHeader()
    {
        ByteBuf @in = Unpooled.Buffer(200);
        try
        {
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new InOrderHttp2Headers();
            toEncode.Add(Cs("test"), Cs("1"));
            toEncode.Add(Cs(":method"), Cs("GET"));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers();

            Http2Exception.StreamException e = Assert.ThrowsAny<Http2Exception.StreamException>(() =>
            {
                hpackDecoder.Decode(3, @in, decoded, true);
            });
            Assert.Equal(3, e.StreamId());
            Assert.Equal(PROTOCOL_ERROR, e.Error());
        }
        finally
        {
            @in.Release();
        }
    }

    [Theory]
    [InlineData("upgrade", "protocol1")]
    [InlineData("connection", "close")]
    [InlineData("keep-alive", "timeout=5")]
    [InlineData("proxy-connection", "close")]
    [InlineData("transfer-encoding", "chunked")]
    [InlineData("te", "something-else")]
    public void ReceivedConnectionHeader(string name, string value)
    {
        ByteBuf @in = Unpooled.Buffer(200);
        try
        {
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new InOrderHttp2Headers();
            toEncode.Add(Cs(":method"), Cs("GET"));
            toEncode.Add(Cs(name), Cs(value));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers();

            Http2Exception.StreamException e = Assert.ThrowsAny<Http2Exception.StreamException>(() =>
            {
                hpackDecoder.Decode(3, @in, decoded, true);
            });
            Assert.Equal(3, e.StreamId());
            Assert.Equal(PROTOCOL_ERROR, e.Error());
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void FailedValidationDoesntCorruptHpack()
    {
        ByteBuf in1 = Unpooled.Buffer(200);
        ByteBuf in2 = Unpooled.Buffer(200);
        try
        {
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new DefaultHttp2Headers();
            toEncode.Add(Cs(":method"), Cs("GET"));
            toEncode.Add(Cs(":status"), Cs("200"));
            toEncode.Add(Cs("foo"), Cs("bar"));
            hpackEncoder.EncodeHeaders(1, in1, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers();

            Http2Exception.StreamException expected =
                    Assert.ThrowsAny<Http2Exception.StreamException>(() =>
            {
                hpackDecoder.Decode(1, in1, decoded, true);
            });
            Assert.Equal(1, expected.StreamId());

            // Do it again, this time without validation, to make sure the HPACK state is still sane.
            decoded.Clear();
            hpackEncoder.EncodeHeaders(1, in2, toEncode, NEVER_SENSITIVE);
            hpackDecoder.Decode(1, in2, decoded, false);

            Assert.Equal(3, decoded.Size());
            Assert.Equal("GET", decoded.Method().ToString());
            Assert.Equal("200", decoded.Status().ToString());
            Assert.Equal("bar", decoded.Get(Cs("foo")).ToString());
        }
        finally
        {
            in1.Release();
            in2.Release();
        }
    }

    [Theory]
    [InlineData(":method", "")]
    [InlineData(":scheme", "")]
    [InlineData(":authority", "")]
    [InlineData(":path", "")]
    public void TestPseudoHeaderEmptyValidationEnabled(string name, string value)
    {
        ByteBuf @in = Unpooled.Buffer(200);
        try
        {
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new InOrderHttp2Headers();
            toEncode.Add(Cs(name), Cs(value));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers();

            Http2Exception.StreamException e = Assert.ThrowsAny<Http2Exception.StreamException>(() =>
            {
                hpackDecoder.Decode(3, @in, decoded, true);
            });
            Assert.Equal(3, e.StreamId());
            Assert.Equal(PROTOCOL_ERROR, e.Error());
        }
        finally
        {
            @in.Release();
        }
    }

    [Theory]
    [InlineData(":method", "")]
    [InlineData(":scheme", "")]
    [InlineData(":authority", "")]
    [InlineData(":path", "")]
    public void TestPseudoHeaderEmptyValidationDisabled(string name, string value)
    {
        ByteBuf @in = Unpooled.Buffer(200);
        try
        {
            HpackEncoder hpackEncoder = new HpackEncoder(true);

            IHttp2Headers toEncode = new InOrderHttp2Headers();
            toEncode.Add(Cs(name), Cs(value));
            hpackEncoder.EncodeHeaders(1, @in, toEncode, NEVER_SENSITIVE);

            IHttp2Headers decoded = new DefaultHttp2Headers(false);
            hpackDecoder.Decode(3, @in, decoded, true);

            Assert.Same(AsciiString.EMPTY_STRING, decoded.Get(Cs(name)));
        }
        finally
        {
            @in.Release();
        }
    }

    // CLR: AsciiString.of(CharSequence) with a String argument.
    private static AsciiString Of(string s)
    {
        return AsciiString.Of(new StringCharSequence(s));
    }

    // CLR: a String passed as a CharSequence.
    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }
}
