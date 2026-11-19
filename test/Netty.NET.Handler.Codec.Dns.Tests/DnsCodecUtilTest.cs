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

using System;
using System.Text;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Dns.Tests;

// CLR: the package-private JUnit 5 test methods are public xUnit facts; readerIndex(int) is the ByteBuf.ReaderIndex
// property setter; Integer.MAX_VALUE is int.MaxValue and IllegalArgumentException is ArgumentException
// (docs/codec-base-porting.md). The char literals of the byte[] initializer are cast to byte, which Java narrows
// implicitly.
public class DnsCodecUtilTest
{

    [Fact]
    public void CalculateMaxNameLengthIsBoundedRegardlessOfRemainingBytes()
    {
        // A decoded name can never exceed MAX_DOMAIN_NAME_LENGTH characters, so the working buffer must stay
        // bounded even when the message has a huge number of bytes still readable after the name (e.g. many
        // more records in a large DNS message). Without this bound, a single message could force an oversized
        // allocation for every name decoded from it.
        Assert.Equal(DnsCodecUtil.MAX_DOMAIN_NAME_LENGTH,
                DnsCodecUtil.CalculateMaxNameLength(int.MaxValue));
        Assert.Equal(DnsCodecUtil.MAX_DOMAIN_NAME_LENGTH,
                DnsCodecUtil.CalculateMaxNameLength(8 * 1024 * 1024));
        Assert.Equal(DnsCodecUtil.MAX_DOMAIN_NAME_LENGTH,
                DnsCodecUtil.CalculateMaxNameLength(DnsCodecUtil.MAX_DOMAIN_NAME_LENGTH + 1));

        // For buffers that cannot possibly contain more than a valid name, behavior is unchanged.
        Assert.Equal(5, DnsCodecUtil.CalculateMaxNameLength(5));
        Assert.Equal(0, DnsCodecUtil.CalculateMaxNameLength(0));
    }

    [Fact]
    public void RejectTooLongLabelWhileDecoding()
    {
        ByteBuf buf = Unpooled.Buffer(256);
        // 63 is the maximum label length
        WriteLabel(buf, 64);
        WriteLabel(buf, 3);
        buf.WriteByte(0);

        Assert.Throws<TooLongFrameException>(() => DnsCodecUtil.DecodeDomainName(buf));
        buf.Release();
    }

    [Fact]
    public void RejectTooLongDomainNameWhileDecoding()
    {
        // 255 is the maximum domain name
        ByteBuf buf = Unpooled.Buffer(512);
        WriteLabel(buf, 50);
        WriteLabel(buf, 50);
        WriteLabel(buf, 50);
        WriteLabel(buf, 50);
        WriteLabel(buf, 56);
        buf.WriteByte(0);

        Assert.Throws<TooLongFrameException>(() => DnsCodecUtil.DecodeDomainName(buf));
        buf.Release();
    }

    [Fact]
    public void AcceptMaxCompressionPointers()
    {
        ByteBuf buf = NewPointerChain(DnsCodecUtil.MAX_COMPRESSION_POINTERS);
        try
        {
            Assert.Equal("abc.", DnsCodecUtil.DecodeDomainName(buf));
            // The reader index must be just after the first pointer.
            Assert.Equal(buf.WriterIndex, buf.ReaderIndex);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void RejectTooManyCompressionPointers()
    {
        ByteBuf buf = NewPointerChain(DnsCodecUtil.MAX_COMPRESSION_POINTERS + 1);
        try
        {
            Assert.Throws<CorruptedFrameException>(() => DnsCodecUtil.DecodeDomainName(buf));
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void RejectCompressionPointerLoop()
    {
        ByteBuf buf = Unpooled.Buffer();
        // A pointer to itself.
        buf.WriteShort(0xc000);
        try
        {
            Assert.Throws<CorruptedFrameException>(() => DnsCodecUtil.DecodeDomainName(buf));
        }
        finally
        {
            buf.Release();
        }
    }

    /**
     * Writes the name {@code abc.} followed by a chain of {@code pointers} compression pointers, each pointing to the
     * previous one, and positions the reader index at the last pointer so that decoding it follows all of them.
     */
    private static ByteBuf NewPointerChain(int pointers)
    {
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteByte(3).WriteBytes(new byte[] { (byte) 'a', (byte) 'b', (byte) 'c' }).WriteByte(0);
        int previous = 0;
        for (int i = 0; i < pointers; i++)
        {
            int current = buf.WriterIndex;
            buf.WriteShort(0xc000 | previous);
            previous = current;
        }
        buf.ReaderIndex = previous;
        return buf;
    }

    [Fact]
    public void RejectTooLongLabelWhileEncoding()
    {
        ByteBuf buf = Unpooled.Buffer(256);
        // 63 is the maximum label length
        StringBuilder sb = new StringBuilder();
        AppendLabel(sb, 64);
        Assert.Throws<ArgumentException>(() => DnsCodecUtil.EncodeDomainName(sb.ToString(), buf));
        buf.Release();
    }

    [Fact]
    public void RejectEmptyLabelWhileEncoding()
    {
        ByteBuf buf = Unpooled.Buffer(256);
        // 63 is the maximum label length
        StringBuilder sb = new StringBuilder();
        AppendLabel(sb, 5);
        AppendLabel(sb, 0);
        AppendLabel(sb, 5);
        Assert.Throws<ArgumentException>(() => DnsCodecUtil.EncodeDomainName(sb.ToString(), buf));
        buf.Release();
    }

    [Fact]
    public void RejectTooLongDomainNameWhileEncoding()
    {
        ByteBuf buf = Unpooled.Buffer(256);
        // 255 is the maximum domain name
        StringBuilder sb = new StringBuilder();
        AppendLabel(sb, 50);
        AppendLabel(sb, 50);
        AppendLabel(sb, 50);
        AppendLabel(sb, 50);
        AppendLabel(sb, 56);

        Assert.Throws<ArgumentException>(() => DnsCodecUtil.EncodeDomainName(sb.ToString(), buf));
        buf.Release();
    }

    private static void WriteLabel(ByteBuf buf, int length)
    {
        buf.WriteByte(length);
        for (int i = 1; i <= length; i++)
        {
            buf.WriteByte(i);
        }
    }

    private static void AppendLabel(StringBuilder sb, int length)
    {
        for (int i = 0; i < length; i++)
        {
            sb.Append('a');
        }
        sb.Append('.');
    }
}
