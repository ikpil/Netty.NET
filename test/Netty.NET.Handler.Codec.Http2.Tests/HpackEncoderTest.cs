/*
 * Copyright 2017 The Netty Project
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
using System.Globalization;
using System.Text;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: The Strings that Java passes as CharSequence are StringCharSequence values built by the private Of(string),
// because a C# string is not an ICharSequence. Header equality calls Equals directly, as Java's assertEquals calls
// equals (xUnit would compare the headers as an IEnumerable of entries; docs/codec-http-porting.md, Shared CLR
// adaptations). java.util.Random(0) is System.Random(0) and nextInt(n)/nextBoolean() are Next(n)/Next(2) != 0 (the
// private NextBoolean); the round-trip test does not depend on the exact sequence, which is not reproduced.
// Numbers in the generated names use the invariant culture and String.valueOf(boolean) is "true"/"false". The
// expected encodings are Java's signed byte values; (byte) of a negative int keeps the same eight bits in C#, so
// the compared octets are the same. int... is params int[].
public class HpackEncoderTest : IDisposable
{
    private HpackDecoder hpackDecoder;
    private HpackEncoder hpackEncoder;
    private IHttp2Headers mockHeaders;
    private ByteBuf buf;

    public HpackEncoderTest()
    {
        SetUp();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void SetUp()
    {
        hpackEncoder = new HpackEncoder();
        hpackDecoder = new HpackDecoder(DEFAULT_HEADER_LIST_SIZE);
        mockHeaders = new Mock<IHttp2Headers>().Object;
        buf = Unpooled.Buffer();
    }

    private void Teardown()
    {
        buf.Release();
    }

    [Fact]
    public void TestSetMaxHeaderTableSizeToMaxValue()
    {
        hpackEncoder = new HpackEncoder(
            false, int.MaxValue, HpackEncoder.HUFF_CODE_THRESHOLD);
        hpackEncoder.SetMaxHeaderTableSize(MAX_HEADER_TABLE_SIZE);
        hpackDecoder.SetMaxHeaderTableSize(MAX_HEADER_TABLE_SIZE);
        hpackEncoder.EncodeHeaders(0, buf, EmptyHttp2Headers.INSTANCE, IHttp2HeadersEncoder.NEVER_SENSITIVE);
        hpackDecoder.Decode(0, buf, mockHeaders, true);
        Assert.Equal(128 * 64, hpackDecoder.GetMaxHeaderTableSize());
    }

    [Fact]
    public void TestSetMaxHeaderTableSizeBelowCap()
    {
        hpackEncoder.SetMaxHeaderTableSize(2048);
        hpackDecoder.SetMaxHeaderTableSize(2048);
        hpackEncoder.EncodeHeaders(0, buf, EmptyHttp2Headers.INSTANCE, IHttp2HeadersEncoder.NEVER_SENSITIVE);
        hpackDecoder.Decode(0, buf, mockHeaders, true);
        Assert.Equal(2048, hpackEncoder.GetMaxHeaderTableSize());
        Assert.Equal(2048, hpackDecoder.GetMaxHeaderTableSize());

        buf.Clear();
        hpackEncoder.SetMaxHeaderTableSize(0);
        hpackDecoder.SetMaxHeaderTableSize(0);
        hpackEncoder.EncodeHeaders(0, buf, EmptyHttp2Headers.INSTANCE, IHttp2HeadersEncoder.NEVER_SENSITIVE);
        hpackDecoder.Decode(0, buf, mockHeaders, true);
        Assert.Equal(0, hpackEncoder.GetMaxHeaderTableSize());
        Assert.Equal(0, hpackDecoder.GetMaxHeaderTableSize());
    }

    [Fact]
    public void TestSetMaxHeaderTableSizeCapScalesWithSizeHint()
    {
        HpackEncoder encoder = new HpackEncoder(false, 16, HpackEncoder.HUFF_CODE_THRESHOLD);
        encoder.SetMaxHeaderTableSize(MAX_HEADER_TABLE_SIZE);
        Assert.Equal(16 * 64, encoder.GetMaxHeaderTableSize());

        encoder = new HpackEncoder(false, 128, HpackEncoder.HUFF_CODE_THRESHOLD);
        encoder.SetMaxHeaderTableSize(MAX_HEADER_TABLE_SIZE);
        Assert.Equal(128 * 64, encoder.GetMaxHeaderTableSize());
    }

    [Fact]
    public void TestSetMaxHeaderTableSizeOverflow()
    {
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            hpackEncoder.SetMaxHeaderTableSize(MAX_HEADER_TABLE_SIZE + 1);
        });
    }

    /**
     * The encoder should not impose an arbitrary limit on the header size if
     * the server has not specified any limit.
     * @throws Http2Exception
     */
    [Fact]
    public void TestWillEncode16MBHeaderByDefault()
    {
        string bigHeaderName = "x-big-header";
        int bigHeaderSize = 1024 * 1024 * 16;
        string bigHeaderVal = new string(new char[bigHeaderSize]).Replace('\0', 'X');
        IHttp2Headers headersIn = new DefaultHttp2Headers().Add(
                Of("x-big-header"), Of(bigHeaderVal));
        IHttp2Headers headersOut = new DefaultHttp2Headers();

        hpackEncoder.EncodeHeaders(0, buf, headersIn, IHttp2HeadersEncoder.NEVER_SENSITIVE);
        hpackDecoder.SetMaxHeaderListSize(bigHeaderSize + 1024);
        hpackDecoder.Decode(0, buf, headersOut, false);
        Assert.Equal(headersOut.Get(Of(bigHeaderName)).ToString(), bigHeaderVal);
    }

    [Fact]
    public void TestSetMaxHeaderListSizeEnforcedAfterSet()
    {
        IHttp2Headers headers = new DefaultHttp2Headers().Add(
                Of("x-big-header"),
                Of(new string(new char[1024 * 16]).Replace('\0', 'X')));

        hpackEncoder.SetMaxHeaderListSize(1000);

        Assert.ThrowsAny<Http2Exception>(() =>
        {
            hpackEncoder.EncodeHeaders(0, buf, headers, IHttp2HeadersEncoder.NEVER_SENSITIVE);
        });
    }

    [Fact]
    public void TestEncodeUsingBothStaticAndDynamicTable()
    {
        IHttp2Headers headers = new DefaultHttp2Headers()
          // :method -> POST is found in the static table.
          .Add(Of(":method"), Of("POST"))

          // ":path" is found in the static table but only matches "/" and "/index.html".
          .Add(Of(":path"), Of("/dev/null"))

          // "accept-language" is found in the static table, but with no matching value.
          .Add(Of("accept-language"), Of("fr"))

          // k -> x is not in the static table.
          .Add(Of("k"), Of("x"));

        // :method -> POST gets encoded by reference.
        // :path -> /dev/null
        //      :path gets encoded by reference, /dev/null literally.
        // accept-language -> fr
        //      accept-language gets encoded by reference, fr literally.
        // k -> x
        //      both k and x get encoded literally.
        VerifyEncoding(headers,
          -125, 68, 9, 47, 100, 101, 118, 47, 110, 117, 108, 108, 81, 2, 102, 114, 64, 1, 107, 1, 120);

        // encoded using references to previous headers.
        VerifyEncoding(headers, -125, -64, -65, -66);
    }

    [Fact]
    public void TestSameHeaderNameMultipleValues()
    {
        IHttp2Headers headers = new DefaultHttp2Headers()
          .Add(Of("k"), Of("x"))
          .Add(Of("k"), Of("y"));

        // k -> x encoded literally, k -> y encoded by referencing k of the k -> x header,
        // y gets encoded literally.
        VerifyEncoding(headers, 64, 1, 107, 1, 120, 126, 1, 121);

        // both k -> x and k -> y encoded by reference.
        VerifyEncoding(headers, -65, -66);
    }

    [Fact]
    public void TestEviction()
    {
        SetMaxTableSize(2 * HpackHeaderField.HEADER_ENTRY_OVERHEAD + 3);

        // k -> x encoded literally
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")), 63, 36, 64, 1, 107, 1, 120);

        // k -> x encoded by referencing the previously encoded k -> x.
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")), -66);

        // k -> x gets evicted
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("y")), 64, 1, 107, 1, 121);

        // k -> x was evicted, so we are back to literal encoding.
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")), 64, 1, 107, 1, 120);
    }

    [Fact]
    public void TestTableResize()
    {
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")).Add(Of("k"), Of("y")), 64, 1, 107, 1, 120, 126, 1, 121);

        // k -> x gets encoded by referencing the previously encoded k -> x.
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")), -65);

        // k -> x gets evicted
        SetMaxTableSize(2 * HpackHeaderField.HEADER_ENTRY_OVERHEAD + 3);

        // k -> x header was evicted, so we are back to literal encoding.
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")), 63, 36, 64, 1, 107, 1, 120);

        // make room for k -> y
        SetMaxTableSize(1000);

        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("y")), 63, -55, 7, 126, 1, 121);

        // both k -> x and k -> y are encoded by reference.
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")).Add(Of("k"), Of("y")), -65, -66);
    }

    [Fact]
    public void TestMultipleTableResize()
    {
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")).Add(Of("k"), Of("y")), 64, 1, 107, 1, 120, 126, 1, 121);

        // Multiple decreases should only emit the final minimum table size update (1000).
        SetMaxTableSize(2000);
        SetMaxTableSize(1000);
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")), 63, -55, 7, -65);

        // Shrinking and then increasing multiple times should only emit the minimum (67) and final (1000) sizes.
        // At 67 bytes, k -> x is evicted while k -> y remains in the table.
        SetMaxTableSize(800);
        SetMaxTableSize(2 * HpackHeaderField.HEADER_ENTRY_OVERHEAD + 3);
        SetMaxTableSize(500);
        SetMaxTableSize(1000);

        // 63, 36 encodes table size 67; 63, -55, 7 encodes table size 1000.
        // k -> x was evicted so it is encoded as literal with incremental indexing (referencing name k from k -> y),
        // and k -> y is still in the table so it is encoded by reference.
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")).Add(Of("k"), Of("y")),
                63, 36, 63, -55, 7, 126, 1, 120, -65);

        // Subsequent resize should not reuse the previous minimum table size.
        SetMaxTableSize(2000);
        VerifyEncoding(new DefaultHttp2Headers().Add(Of("k"), Of("x")).Add(Of("k"), Of("y")), 63, -79, 15, -66, -65);
    }

    [Fact]
    public void TestManyHeaderCombinations()
    {
        Random r = new Random(0);

        for (int i = 0; i < 50000; i++)
        {
            if (r.Next(10) == 0)
            {
                SetMaxTableSize(NextBoolean(r) ? 0 : r.Next(4096));
            }

            VerifyRoundTrip(new DefaultHttp2Headers()
              .Add(Of("k" + r.Next(20).ToString(CultureInfo.InvariantCulture)), Of("x" + r.Next(500).ToString(CultureInfo.InvariantCulture)))
              .Add(Of(":method"), Of(NextBoolean(r) ? "GET" : "POST"))
              .Add(Of(":path"), Of("/dev/null"))
              .Add(Of("accept-language"), Of(NextBoolean(r) ? "true" : "false"))
            );
            buf.Clear();
        }
    }

    [Fact]
    public void TestSanitization()
    {
        int headerValueSize = 300;
        // Prefix the header values with a legal character.
        // The first value character has different rules than the rest.
        StringBuilder actualHeaderValueBuilder = new StringBuilder("a");
        StringBuilder expectedHeaderValueBuilder = new StringBuilder("a");

        for (int i = 0; i < headerValueSize; i++)
        {
            if (i <= 255 && HttpHeaderValidationUtil.ValidateValidHeaderValue(Of("a" + ((char) i))) != -1)
            {
                // This is not a legal header value character. Skip it.
                continue;
            }

            actualHeaderValueBuilder.Append((char) i); // Use the index as the code point value of the character.

            if (i <= 255)
            {
                expectedHeaderValueBuilder.Append((char) i);
            }
            else
            {
                expectedHeaderValueBuilder.Append('?'); // Expect this character to be sanitized.
            }
        }

        string actualHeaderValue = actualHeaderValueBuilder.ToString();
        string expectedHeaderValue = expectedHeaderValueBuilder.ToString();
        HpackEncoder encoderWithHuffmanEncoding =
                new HpackEncoder(false, 64, 0); // Low Huffman code threshold.
        HpackEncoder encoderWithoutHuffmanEncoding =
                new HpackEncoder(false, 64, int.MaxValue); // High Huffman code threshold.

        // Expect the same decoded header value regardless of whether Huffman encoding is enabled or not.
        VerifyHeaderValueSanitization(encoderWithHuffmanEncoding, actualHeaderValue, expectedHeaderValue);
        VerifyHeaderValueSanitization(encoderWithoutHuffmanEncoding, actualHeaderValue, expectedHeaderValue);
    }

    private void VerifyHeaderValueSanitization(
            HpackEncoder encoder,
            string actualHeaderValue,
            string expectedHeaderValue
    )
    {

        string headerKey = "some-key";
        IHttp2Headers toBeEncodedHeaders = new DefaultHttp2Headers().Add(Of(headerKey), Of(actualHeaderValue));
        encoder.EncodeHeaders(0, buf, toBeEncodedHeaders, IHttp2HeadersEncoder.NEVER_SENSITIVE);
        DefaultHttp2Headers decodedHeaders = new DefaultHttp2Headers();
        hpackDecoder.Decode(0, buf, decodedHeaders, true);
        buf.Clear();
        string decodedHeaderValue = decodedHeaders.Get(Of(headerKey)).ToString();
        Assert.Equal(expectedHeaderValue, decodedHeaderValue);
    }

    private void SetMaxTableSize(int maxHeaderTableSize)
    {
        hpackEncoder.SetMaxHeaderTableSize(maxHeaderTableSize);
        hpackDecoder.SetMaxHeaderTableSize(maxHeaderTableSize);
    }

    private void VerifyEncoding(IHttp2Headers encodedHeaders, params int[] encoding)
    {
        VerifyRoundTrip(encodedHeaders);
        VerifyEncodedBytes(encoding);
        buf.Clear();
    }

    private void VerifyRoundTrip(IHttp2Headers encodedHeaders)
    {
        hpackEncoder.EncodeHeaders(0, buf, encodedHeaders, IHttp2HeadersEncoder.NEVER_SENSITIVE);
        DefaultHttp2Headers decodedHeaders = new DefaultHttp2Headers();
        hpackDecoder.Decode(0, buf, decodedHeaders, true);
        Assert.True(encodedHeaders.Equals(decodedHeaders));
    }

    private void VerifyEncodedBytes(params int[] expectedEncoding)
    {
        // We want to copy everything that was written to the buffer.
        byte[] actualEncoding = new byte[buf.WriterIndex];
        buf.GetBytes(0, actualEncoding);
        Assert.Equal(ToByteArray(expectedEncoding), actualEncoding);
    }

    private byte[] ToByteArray(int[] encoding)
    {
        byte[] expectedEncoding = new byte[encoding.Length];

        for (int i = 0; i < encoding.Length; i++)
        {
            expectedEncoding[i] = (byte) encoding[i];
        }

        return expectedEncoding;
    }

    // CLR: a String passed as a CharSequence.
    private static ICharSequence Of(string s)
    {
        return new StringCharSequence(s);
    }

    private static bool NextBoolean(Random r)
    {
        return r.Next(2) != 0;
    }
}
