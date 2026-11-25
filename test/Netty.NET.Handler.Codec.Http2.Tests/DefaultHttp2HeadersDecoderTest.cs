/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.IHttp2HeadersEncoder;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link DefaultHttp2HeadersDecoder}.
 */
// CLR: @BeforeEach is the constructor ("xUnit creates a new instance of the test class for every test that is run",
// docs/codec-http-porting.md, Shared CLR adaptations), which calls the Java-named setup(). The anonymous Executables
// are lambdas and assertThrows(T.class) is Assert.ThrowsAny<T> (docs/codec-http-porting.md, Core test notes).
// @ParameterizedTest @MethodSource methods that return List<Integer> are [Theory] [MemberData] sources that return
// TheoryData<int> (the same rows). AssertJ isEqualToIgnoringCase(s) is Assert.Equal(s, actual, ignoreCase: true) on
// the text, and isEqualTo(new AsciiString(bytes)) calls Equals, as AssertJ does. String.format("%09000d", 0) is
// 0.ToString("D9000") (zero-padded to 9000 digits). getBytes(UTF_8) is Encoding.UTF8.GetBytes and byte[]... is
// params byte[][]. The Strings that Java passes as CharSequence are StringCharSequence values (the private Cs). The
// obs-text rows pass (byte) i for i in 0x80..0xFF, the same octets as Java's signed bytes.
public class DefaultHttp2HeadersDecoderTest
{

    private DefaultHttp2HeadersDecoder decoder;

    public DefaultHttp2HeadersDecoderTest()
    {
        Setup();
    }

    private void Setup()
    {
        decoder = new DefaultHttp2HeadersDecoder(false);
    }

    [Fact]
    public void DecodeShouldSucceed()
    {
        ByteBuf buf = Encode(B(":method"), B("GET"), B("akey"), B("avalue"), RandomBytes(), RandomBytes());
        try
        {
            IHttp2Headers headers = decoder.DecodeHeaders(0, buf);
            Assert.Equal(3, headers.Size());
            Assert.Equal("GET", headers.Method().ToString());
            Assert.Equal("avalue", headers.Get(new AsciiString("akey")).ToString());
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestExceedHeaderSize()
    {
        int maxListSize = 100;
        decoder.Configuration().MaxHeaderListSize(maxListSize, maxListSize);
        ByteBuf buf = Encode(RandomBytes(maxListSize), RandomBytes(1));

        try
        {
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                decoder.DecodeHeaders(0, buf);
            });
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void DecodeLargerThanHeaderListSizeButLessThanGoAway()
    {
        decoder.MaxHeaderListSize(MIN_HEADER_LIST_SIZE, MAX_HEADER_LIST_SIZE);
        ByteBuf buf = Encode(B(":method"), B("GET"));
        int streamId = 1;
        Http2Exception.HeaderListSizeException e =
                Assert.ThrowsAny<Http2Exception.HeaderListSizeException>(() =>
        {
            decoder.DecodeHeaders(streamId, buf);
        });
        Assert.Equal(streamId, e.StreamId());
        buf.Release();
    }

    [Fact]
    public void DecodeLargerThanHeaderListSizeButLessThanGoAwayWithInitialDecoderSettings()
    {
        ByteBuf buf = Encode(B(":method"), B("GET"), B("test_header"),
            B(0.ToString("D9000", CultureInfo.InvariantCulture).Replace('0', 'A')));
        int streamId = 1;
        try
        {
            Http2Exception.HeaderListSizeException e = Assert.ThrowsAny<Http2Exception.HeaderListSizeException>(() =>
            {
                        decoder.DecodeHeaders(streamId, buf);
            });
            Assert.Equal(streamId, e.StreamId());
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void DecodeLargerThanHeaderListSizeGoAway()
    {
        decoder.MaxHeaderListSize(MIN_HEADER_LIST_SIZE, MIN_HEADER_LIST_SIZE);
        ByteBuf buf = Encode(B(":method"), B("GET"));
        int streamId = 1;
        try
        {
            Http2Exception e = Assert.ThrowsAny<Http2Exception>(() =>
            {
                decoder.DecodeHeaders(streamId, buf);
            });
            Assert.Equal(Http2Error.PROTOCOL_ERROR, e.Error());
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void DuplicatePseudoHeadersMustFailValidation()
    {
        DefaultHttp2HeadersDecoder decoder = new DefaultHttp2HeadersDecoder(true);
        VerifyValidationFails(decoder, Encode(B(":authority"), B("abc"), B(":authority"), B("def")));
    }

    [Fact]
    public void IllegalHeaderValuesMustFailValidation()
    {
        VerifyValidationFails(new DefaultHttp2HeadersDecoder(), Encode(B("headername"), B("fo\no")));
        VerifyValidationFails(new DefaultHttp2HeadersDecoder(true), Encode(B("headername"), B("fo\no")));
        VerifyValidationFails(new DefaultHttp2HeadersDecoder(true, MAX_HEADER_LIST_SIZE),
            Encode(B("headername"), B("fo\no")));
    }

    [Fact]
    public void DecodingTrailersTeHeaderMustNotFailValidation()
    {
        // The TE header is expressly allowed to have the value "trailers".
        ByteBuf buf = null;
        try
        {
            buf = Encode(B(":method"), B("GET"), B("te"), B("trailers"));
            IHttp2Headers headers = decoder.DecodeHeaders(1, buf); // This must not throw.
            Assert.Equal(HttpHeaderValues.TRAILERS.ToString(), headers.Get(HttpHeaderNames.TE).ToString(),
                ignoreCase: true);
        }
        finally
        {
            ReferenceCountUtil.Release(buf);
        }
    }

    [Fact]
    public void DecodingConnectionRelatedHeadersMustFailValidation()
    {
        DefaultHttp2HeadersDecoder decoder = new DefaultHttp2HeadersDecoder(true, true);
        // Standard connection related headers
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"), B("keep-alive"), B("timeout=5")));
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"),
                B("connection"), B("keep-alive"), B("keep-alive"), B("timeout=5")));
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"), B("transfer-encoding"), B("chunked")));
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"),
                B("connection"), B("transfer-encoding"), B("transfer-encoding"), B("chunked")));
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"), B("upgrade"), B("foo/2")));
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"),
                B("connection"), B("upgrade"), B("upgrade"), B("foo/2")));
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"), B("connection"), B("close")));

        // Non-standard connection related headers:
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"), B("proxy-connection"), B("keep-alive")));

        // Only "trailers" is allowed for the TE header:
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"), B("te"), B("compress")));
    }

    public static TheoryData<int> IllegalFirstChar()
    {
        TheoryData<int> list = new TheoryData<int>();
        for (int i = 0; i < 0x21; i++)
        {
            list.Add(i);
        }
        list.Add(0x7F);
        return list;
    }

    [Theory]
    [MemberData(nameof(IllegalFirstChar))]
    public void DecodingInvalidHeaderValueMustFailValidationIfFirstCharIsIllegal(int illegalFirstChar)
    {
        DefaultHttp2HeadersDecoder decoder = new DefaultHttp2HeadersDecoder(true, true);
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"),
                B("test_header"), new byte[]{ (byte) illegalFirstChar, (byte) 'a' }));
    }

    public static TheoryData<int> IllegalNotFirstChar()
    {
        TheoryData<int> list = new TheoryData<int>();
        for (int i = 0; i < 0x21; i++)
        {
            if (i == ' ' || i == '\t')
            {
                continue; // Space and horizontal tab are only illegal as first chars.
            }
            list.Add(i);
        }
        list.Add(0x7F);
        return list;
    }

    [Theory]
    [MemberData(nameof(IllegalNotFirstChar))]
    public void DecodingInvalidHeaderValueMustFailValidationIfANotFirstCharIsIllegal(int illegalSecondChar)
    {
        DefaultHttp2HeadersDecoder decoder = new DefaultHttp2HeadersDecoder(true, true);
        VerifyValidationFails(decoder, Encode(B(":method"), B("GET"),
                B("test_header"), new byte[]{ (byte) 'a', (byte) illegalSecondChar }));
    }

    [Fact]
    public void HeaderValuesAllowSpaceAfterFirstCharacter()
    {
        DefaultHttp2HeadersDecoder decoder = new DefaultHttp2HeadersDecoder(true);
        ByteBuf buf = null;
        try
        {
            buf = Encode(B(":method"), B("GET"), B("test_header"), B("a b"));
            IHttp2Headers headers = decoder.DecodeHeaders(1, buf); // This must not throw.
            Assert.Equal("a b", headers.Get(Cs("test_header")).ToString(), ignoreCase: true);
        }
        finally
        {
            ReferenceCountUtil.Release(buf);
        }
    }

    [Fact]
    public void HeaderValuesAllowHorzontalTabAfterFirstCharacter()
    {
        DefaultHttp2HeadersDecoder decoder = new DefaultHttp2HeadersDecoder(true);
        ByteBuf buf = null;
        try
        {
            buf = Encode(B(":method"), B("GET"), B("test_header"), B("a\tb"));
            IHttp2Headers headers = decoder.DecodeHeaders(1, buf); // This must not throw.
            Assert.Equal("a\tb", headers.Get(Cs("test_header")).ToString(), ignoreCase: true);
        }
        finally
        {
            ReferenceCountUtil.Release(buf);
        }
    }

    public static TheoryData<int> ValidObsText()
    {
        TheoryData<int> list = new TheoryData<int>();
        for (int i = 0x80; i <= 0xFF; i++)
        {
            list.Add(i);
        }
        return list;
    }

    [Theory]
    [MemberData(nameof(ValidObsText))]
    public void HeaderValuesAllowObsTextInFirstChar(int i)
    {
        DefaultHttp2HeadersDecoder decoder = new DefaultHttp2HeadersDecoder(true);
        ByteBuf buf = null;
        try
        {
            byte[] bytes = {(byte) i, (byte) 'a'};
            buf = Encode(B(":method"), B("GET"), B("test_header"), bytes);
            IHttp2Headers headers = decoder.DecodeHeaders(1, buf); // This must not throw.
            Assert.True(new AsciiString(bytes).Equals(headers.Get(Cs("test_header"))));
        }
        finally
        {
            ReferenceCountUtil.Release(buf);
        }
    }

    [Theory]
    [MemberData(nameof(ValidObsText))]
    public void HeaderValuesAllowObsTextInNonFirstChar(int i)
    {
        DefaultHttp2HeadersDecoder decoder = new DefaultHttp2HeadersDecoder(true);
        ByteBuf buf = null;
        try
        {
            byte[] bytes = {(byte) 'a', (byte) i};
            buf = Encode(B(":method"), B("GET"), B("test_header"), bytes);
            IHttp2Headers headers = decoder.DecodeHeaders(1, buf); // This must not throw.
            Assert.True(new AsciiString(bytes).Equals(headers.Get(Cs("test_header"))));
        }
        finally
        {
            ReferenceCountUtil.Release(buf);
        }
    }

    private static void VerifyValidationFails(DefaultHttp2HeadersDecoder decoder, ByteBuf buf)
    {
        try
        {
            Http2Exception e = Assert.ThrowsAny<Http2Exception>(() =>
            {
                decoder.DecodeHeaders(1, buf);
            });
            Assert.Equal(Http2Error.PROTOCOL_ERROR, e.Error());
        }
        finally
        {
            buf.Release();
        }
    }

    private static byte[] B(string @string)
    {
        return Encoding.UTF8.GetBytes(@string);
    }

    private static ByteBuf Encode(params byte[][] entries)
    {
        HpackEncoder hpackEncoder = NewTestEncoder();
        ByteBuf @out = Unpooled.Buffer();
        IHttp2Headers http2Headers = new DefaultHttp2Headers(false);
        for (int ix = 0; ix < entries.Length;)
        {
            http2Headers.Add(new AsciiString(entries[ix++], false), new AsciiString(entries[ix++], false));
        }
        hpackEncoder.EncodeHeaders(3 /* randomly chosen */, @out, http2Headers, NEVER_SENSITIVE);
        return @out;
    }

    // CLR: a String passed as a CharSequence.
    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }
}
