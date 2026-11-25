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
using Netty.NET.Handler.Codec.Spdy;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;

namespace Netty.NET.Handler.Codec.Http.Tests.Spdy;

#pragma warning disable xUnit2013 // assertEquals(n, collection.size()) of the source stays Assert.Equal(n, Count).

// CLR: @BeforeEach is the constructor and @AfterEach is Dispose(): xUnit "creates a new instance of the test class for
// every test that is run", and cleanup code goes in Dispose() (https://xunit.net/docs/shared-context).
// ByteBufAllocator.DEFAULT is IByteBufAllocator.Default. String.getBytes() (the default charset) of the ASCII name and
// value is Encoding.UTF8. The Strings that Java passes as CharSequence header names are wrapped by the core test helper
// HttpHeadersTestUtils.Of (StringCharSequence). assertEquals(String, headers.get(name)) compares the String with the
// stored value through equals; the C# value is a StringCharSequence, so the assertions read it as text with
// GetAsString/GetAllAsString (the ISpdyHeaders members that "convert the result to a String").
public class SpdyHeaderBlockRawDecoderTest : IDisposable
{

    private const int maxHeaderSize = 16;

    private const string name = "name";
    private const string value = "value";
    private static readonly byte[] nameBytes = Encoding.UTF8.GetBytes(name);
    private static readonly byte[] valueBytes = Encoding.UTF8.GetBytes(value);

    private SpdyHeaderBlockRawDecoder decoder;
    private ISpdyHeadersFrame frame;

    public SpdyHeaderBlockRawDecoderTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        decoder = new SpdyHeaderBlockRawDecoder(SpdyVersion.SPDY_3_1, maxHeaderSize);
        frame = new DefaultSpdyHeadersFrame(1);
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        decoder.End();
    }

    [Fact]
    public void TestEmptyHeaderBlock()
    {
        ByteBuf headerBlock = Unpooled.EmptyBuffer;
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestZeroNameValuePairs()
    {
        ByteBuf headerBlock = Unpooled.Buffer(4);
        headerBlock.WriteInt(0);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.False(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestNegativeNameValuePairs()
    {
        ByteBuf headerBlock = Unpooled.Buffer(4);
        headerBlock.WriteInt(-1);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestOneNameValuePair()
    {
        ByteBuf headerBlock = Unpooled.Buffer(21);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5);
        headerBlock.WriteBytes(valueBytes);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.False(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAsString(Of(name)));
        headerBlock.Release();
    }

    [Fact]
    public void TestMissingNameLength()
    {
        ByteBuf headerBlock = Unpooled.Buffer(4);
        headerBlock.WriteInt(1);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestZeroNameLength()
    {
        ByteBuf headerBlock = Unpooled.Buffer(8);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(0);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestNegativeNameLength()
    {
        ByteBuf headerBlock = Unpooled.Buffer(8);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(-1);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestMissingName()
    {
        ByteBuf headerBlock = Unpooled.Buffer(8);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestIllegalNameOnlyNull()
    {
        ByteBuf headerBlock = Unpooled.Buffer(18);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(1);
        headerBlock.WriteByte(0);
        headerBlock.WriteInt(5);
        headerBlock.WriteBytes(valueBytes);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestMissingValueLength()
    {
        ByteBuf headerBlock =  Unpooled.Buffer(12);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestZeroValueLength()
    {
        ByteBuf headerBlock = Unpooled.Buffer(16);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(0);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.False(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal("", frame.Headers().GetAsString(Of(name)));
        headerBlock.Release();
    }

    [Fact]
    public void TestNegativeValueLength()
    {
        ByteBuf headerBlock = Unpooled.Buffer(16);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(-1);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestMissingValue()
    {
        ByteBuf headerBlock = Unpooled.Buffer(16);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestIllegalValueOnlyNull()
    {
        ByteBuf headerBlock = Unpooled.Buffer(17);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(1);
        headerBlock.WriteByte(0);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestIllegalValueStartsWithNull()
    {
        ByteBuf headerBlock = Unpooled.Buffer(22);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(6);
        headerBlock.WriteByte(0);
        headerBlock.WriteBytes(valueBytes);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestIllegalValueEndsWithNull()
    {
        ByteBuf headerBlock = Unpooled.Buffer(22);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(6);
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteByte(0);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestMultipleValues()
    {
        ByteBuf headerBlock = Unpooled.Buffer(27);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(11);
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteByte(0);
        headerBlock.WriteBytes(valueBytes);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.False(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(2, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAllAsString(Of(name))[0]);
        Assert.Equal(value, frame.Headers().GetAllAsString(Of(name))[1]);
        headerBlock.Release();
    }

    [Fact]
    public void TestMultipleValuesEndsWithNull()
    {
        ByteBuf headerBlock = Unpooled.Buffer(28);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(12);
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteByte(0);
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteByte(0);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAsString(Of(name)));
        headerBlock.Release();
    }

    [Fact]
    public void TestIllegalValueMultipleNulls()
    {
        ByteBuf headerBlock = Unpooled.Buffer(28);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(12);
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteByte(0);
        headerBlock.WriteByte(0);
        headerBlock.WriteBytes(valueBytes);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestMissingNextNameValuePair()
    {
        ByteBuf headerBlock = Unpooled.Buffer(21);
        headerBlock.WriteInt(2);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5);
        headerBlock.WriteBytes(valueBytes);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAsString(Of(name)));
        headerBlock.Release();
    }

    [Fact]
    public void TestMultipleNames()
    {
        ByteBuf headerBlock = Unpooled.Buffer(38);
        headerBlock.WriteInt(2);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5);
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5);
        headerBlock.WriteBytes(valueBytes);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAsString(Of(name)));
        headerBlock.Release();
    }

    [Fact]
    public void TestExtraData()
    {
        ByteBuf headerBlock = Unpooled.Buffer(22);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5);
        headerBlock.WriteBytes(valueBytes);
        headerBlock.WriteByte(0);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAsString(Of(name)));
        headerBlock.Release();
    }

    [Fact]
    public void TestMultipleDecodes()
    {
        ByteBuf headerBlock = Unpooled.Buffer(21);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(5);
        headerBlock.WriteBytes(valueBytes);

        int readableBytes = headerBlock.ReadableBytes;
        for (int i = 0; i < readableBytes; i++)
        {
            ByteBuf headerBlockSegment = headerBlock.Slice(i, 1);
            decoder.Decode(IByteBufAllocator.Default, headerBlockSegment, frame);
            Assert.False(headerBlockSegment.IsReadable());
        }
        decoder.EndHeaderBlock(frame);

        Assert.False(frame.IsInvalid());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAsString(Of(name)));
        headerBlock.Release();
    }

    [Fact]
    public void TestContinueAfterInvalidHeaders()
    {
        ByteBuf numHeaders = Unpooled.Buffer(4);
        numHeaders.WriteInt(1);

        ByteBuf nameBlock = Unpooled.Buffer(8);
        nameBlock.WriteInt(4);
        nameBlock.WriteBytes(nameBytes);

        ByteBuf valueBlock = Unpooled.Buffer(9);
        valueBlock.WriteInt(5);
        valueBlock.WriteBytes(valueBytes);

        decoder.Decode(IByteBufAllocator.Default, numHeaders, frame);
        decoder.Decode(IByteBufAllocator.Default, nameBlock, frame);
        frame.SetInvalid();
        decoder.Decode(IByteBufAllocator.Default, valueBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(numHeaders.IsReadable());
        Assert.False(nameBlock.IsReadable());
        Assert.False(valueBlock.IsReadable());
        Assert.Equal(1, frame.Headers().Names().Count);
        Assert.True(frame.Headers().Contains(Of(name)));
        Assert.Equal(1, frame.Headers().GetAll(Of(name)).Count);
        Assert.Equal(value, frame.Headers().GetAsString(Of(name)));
        numHeaders.Release();
        nameBlock.Release();
        valueBlock.Release();
    }

    [Fact]
    public void TestTruncatedHeaderName()
    {
        ByteBuf headerBlock = Unpooled.Buffer(maxHeaderSize + 18);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(maxHeaderSize + 1);
        for (int i = 0; i < maxHeaderSize + 1; i++)
        {
            headerBlock.WriteByte('a');
        }
        headerBlock.WriteInt(5);
        headerBlock.WriteBytes(valueBytes);
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsTruncated());
        Assert.False(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }

    [Fact]
    public void TestTruncatedHeaderValue()
    {
        ByteBuf headerBlock = Unpooled.Buffer(maxHeaderSize + 13);
        headerBlock.WriteInt(1);
        headerBlock.WriteInt(4);
        headerBlock.WriteBytes(nameBytes);
        headerBlock.WriteInt(13);
        for (int i = 0; i < maxHeaderSize - 3; i++)
        {
            headerBlock.WriteByte('a');
        }
        decoder.Decode(IByteBufAllocator.Default, headerBlock, frame);
        decoder.EndHeaderBlock(frame);

        Assert.False(headerBlock.IsReadable());
        Assert.True(frame.IsTruncated());
        Assert.False(frame.IsInvalid());
        Assert.Equal(0, frame.Headers().Names().Count);
        headerBlock.Release();
    }
}
