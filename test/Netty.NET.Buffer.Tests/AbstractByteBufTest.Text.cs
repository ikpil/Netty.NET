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
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// CLR: string and ReadOnlySpan<char> replace CharBuffer/CharSequence; ReadString serves both
// readCharSequence and readString. CLR Encoding writes the exact byte length without an automatic
// BOM, so Encoding.BigEndianUnicode stands in for CharsetUtil.UTF_16
// (https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.bigendianunicode?view=net-10.0).
// ByteBufTextSourceTest keeps the additional five-provider CLR wire/cursor checks.
public abstract partial class AbstractByteBufTest
{
    // CLR: Encoding.BigEndianUnicode stands in for CharsetUtil.UTF_16 without its BOM.
    private static readonly Encoding UTF_16 = Encoding.BigEndianUnicode;

    [Fact]
    public void TestToString()
    {
        ByteBuf copied = Unpooled.CopiedBuffer("Hello, World!", Encoding.Latin1);
        buffer.Clear();
        buffer.WriteBytes(copied);
        Assert.Equal("Hello, World!", buffer.ToString(Encoding.Latin1));
        copied.Release();
    }

    [Fact]
    public void TestSetUsAsciiCharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestSetCharSequenceAfterRelease0(Encoding.ASCII);
        });
    }

    [Fact]
    public void TestSetIso88591CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestSetCharSequenceAfterRelease0(Encoding.Latin1);
        });
    }

    [Fact]
    public void TestSetUtf8CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestSetCharSequenceAfterRelease0(Encoding.UTF8);
        });
    }

    [Fact]
    public void TestSetUtf16CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestSetCharSequenceAfterRelease0(UTF_16);
        });
    }

    private void TestSetCharSequenceAfterRelease0(Encoding charset)
    {
        ReleasedBuffer().SetCharSequence(0, "x", charset);
    }

    [Fact]
    public void TestWriteUsAsciiCharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestWriteCharSequenceAfterRelease0(Encoding.ASCII);
        });
    }

    [Fact]
    public void TestWriteIso88591CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestWriteCharSequenceAfterRelease0(Encoding.Latin1);
        });
    }

    [Fact]
    public void TestWriteUtf8CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestWriteCharSequenceAfterRelease0(Encoding.UTF8);
        });
    }

    [Fact]
    public void TestWriteUtf16CharSequenceAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            TestWriteCharSequenceAfterRelease0(UTF_16);
        });
    }

    private void TestWriteCharSequenceAfterRelease0(Encoding charset)
    {
        ReleasedBuffer().WriteCharSequence("x", charset);
    }

    [Fact]
    public virtual void TestWriteUsAsciiCharSequenceExpand()
    {
        TestWriteCharSequenceExpand(Encoding.ASCII);
    }

    [Fact]
    public virtual void TestWriteUtf8CharSequenceExpand()
    {
        TestWriteCharSequenceExpand(Encoding.UTF8);
    }

    [Fact]
    public virtual void TestWriteIso88591CharSequenceExpand()
    {
        TestWriteCharSequenceExpand(Encoding.Latin1);
    }

    [Fact]
    public virtual void TestWriteUtf16CharSequenceExpand()
    {
        TestWriteCharSequenceExpand(UTF_16);
    }

    private void TestWriteCharSequenceExpand(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1);
        try
        {
            int writerIndex = buf.Capacity - 1;
            buf.WriterIndex = writerIndex;
            int written = buf.WriteCharSequence("AB", charset);
            Assert.Equal(writerIndex, buf.WriterIndex - written);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetUsAsciiCharSequenceNoExpand()
    {
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            TestSetCharSequenceNoExpand(Encoding.ASCII);
        });
    }

    [Fact]
    public void TestSetUtf8CharSequenceNoExpand()
    {
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            TestSetCharSequenceNoExpand(Encoding.UTF8);
        });
    }

    [Fact]
    public void TestSetIso88591CharSequenceNoExpand()
    {
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            TestSetCharSequenceNoExpand(Encoding.Latin1);
        });
    }

    [Fact]
    public void TestSetUtf16CharSequenceNoExpand()
    {
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            TestSetCharSequenceNoExpand(UTF_16);
        });
    }

    private void TestSetCharSequenceNoExpand(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1);
        try
        {
            buf.SetCharSequence(0, "AB", charset);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestSetUsAsciiCharSequence()
    {
        TestSetGetCharSequence(Encoding.ASCII);
    }

    [Fact]
    public void TestSetUtf8CharSequence()
    {
        TestSetGetCharSequence(Encoding.UTF8);
    }

    [Fact]
    public void TestSetIso88591CharSequence()
    {
        TestSetGetCharSequence(Encoding.Latin1);
    }

    [Fact]
    public void TestSetUtf16CharSequence()
    {
        TestSetGetCharSequence(UTF_16);
    }

    private static readonly string EXTENDED_ASCII_CHARS, ASCII_CHARS;

    static AbstractByteBufTest()
    {
        char[] chars = new char[256];
        for (char c = (char)0; c < chars.Length; c++)
        {
            chars[c] = c;
        }
        EXTENDED_ASCII_CHARS = new string(chars);
        ASCII_CHARS = new string(chars, 0, 128);
    }

    private void TestSetGetCharSequence(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1024);
        string sequence = Encoding.ASCII.Equals(charset)
                ? ASCII_CHARS : EXTENDED_ASCII_CHARS;
        int bytes = buf.SetCharSequence(1, sequence, charset);
        // CLR: the ICharSequence content is compared through ToString(), as CharBuffer.wrap does in the source.
        Assert.Equal(sequence, buf.GetCharSequence(1, bytes, charset).ToString());
        buf.Release();
    }

    [Fact]
    public void TestWriteReadUsAsciiCharSequence()
    {
        TestWriteReadCharSequence(Encoding.ASCII);
    }

    [Fact]
    public void TestWriteReadUtf8CharSequence()
    {
        TestWriteReadCharSequence(Encoding.UTF8);
    }

    [Fact]
    public void TestWriteReadIso88591CharSequence()
    {
        TestWriteReadCharSequence(Encoding.Latin1);
    }

    [Fact]
    public void TestWriteReadUtf16CharSequence()
    {
        TestWriteReadCharSequence(UTF_16);
    }

    private void TestWriteReadCharSequence(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1024);
        string sequence = Encoding.ASCII.Equals(charset)
                ? ASCII_CHARS : EXTENDED_ASCII_CHARS;
        buf.WriterIndex = 1;
        int bytes = buf.WriteCharSequence(sequence, charset);
        buf.ReaderIndex = 1;
        // CLR: the ICharSequence content is compared through ToString(), as CharBuffer.wrap does in the source.
        Assert.Equal(sequence, buf.ReadCharSequence(bytes, charset).ToString());
        buf.Release();
    }

    [Fact]
    public void TestWriteReadUsAsciiString()
    {
        TestWriteReadString(Encoding.ASCII);
    }

    [Fact]
    public void TestWriteReadUtf8String()
    {
        TestWriteReadString(Encoding.UTF8);
    }

    [Fact]
    public void TestWriteReadIso88591String()
    {
        TestWriteReadString(Encoding.Latin1);
    }

    [Fact]
    public void TestWriteReadUtf16String()
    {
        TestWriteReadString(UTF_16);
    }

    private void TestWriteReadString(Encoding charset)
    {
        ByteBuf buf = NewBuffer(1024);
        string sequence = Encoding.ASCII.Equals(charset)
                ? ASCII_CHARS : EXTENDED_ASCII_CHARS;
        buf.WriterIndex = 1;
        int bytes = buf.WriteCharSequence(sequence, charset);
        buf.ReaderIndex = 1;
        Assert.Equal(sequence, buf.ReadString(bytes, charset));
        buf.Release();
    }

    [Fact]
    public void TestSetCharSequenceWithTooLongSequence()
    {
        ByteBuf buffer = Unpooled.Buffer(4, 128);
        const string sequence = "ÖÄÜ€";
        int maxBytes = ByteBufUtil.Utf8MaxBytes(sequence);
        Assert.True(buffer.WritableBytes < maxBytes);
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetCharSequence(0, sequence, Encoding.UTF8));
        buffer.Release();
    }

    [Fact]
    public void TestWriteCharSequence()
    {
        ByteBuf buffer = Unpooled.Buffer(4, 128);
        const string sequence = "ÖÄÜ€";
        int maxBytes = ByteBufUtil.Utf8MaxBytes(sequence);
        Assert.True(buffer.WritableBytes < maxBytes);
        int capacity = buffer.Capacity;
        // This should expand the buffer.
        buffer.WriteCharSequence(sequence, Encoding.UTF8);
        Assert.NotEqual(capacity, buffer.Capacity);
        buffer.Release();
    }
}
