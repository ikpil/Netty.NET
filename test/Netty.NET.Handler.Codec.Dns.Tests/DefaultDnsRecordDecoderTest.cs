/*
 * Copyright 2016 The Netty Project
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
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Dns.Tests;

// CLR: the char literals of the byte[] initializers are cast to byte, which Java narrows implicitly; the record
// accessors are the C# methods and properties of the port (content() is Content); decodeRecord(ByteBuf) states its
// type argument because C# does not infer it from the assignment target. The tests reach the protected decodeName0 and
// decodeRecord(String, ...) of the same package through their protected internal C# declarations.
public class DefaultDnsRecordDecoderTest
{

    [Fact]
    public void TestDecodeName()
    {
        TestDecodeName0("netty.io.", Unpooled.WrappedBuffer(new byte[] {
                5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o', 0
        }));
    }

    [Fact]
    public void TestDecodeNameWithoutTerminator()
    {
        TestDecodeName0("netty.io.", Unpooled.WrappedBuffer(new byte[] {
                5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o'
        }));
    }

    [Fact]
    public void TestDecodeNameWithExtraTerminator()
    {
        // Should not be decoded as 'netty.io..'
        TestDecodeName0("netty.io.", Unpooled.WrappedBuffer(new byte[] {
                5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o', 0, 0
        }));
    }

    [Fact]
    public void TestDecodeEmptyName()
    {
        TestDecodeName0(".", Unpooled.Buffer().WriteByte(0));
    }

    [Fact]
    public void TestDecodeEmptyNameFromEmptyBuffer()
    {
        TestDecodeName0(".", Unpooled.EmptyBuffer);
    }

    [Fact]
    public void TestDecodeEmptyNameFromExtraZeroes()
    {
        TestDecodeName0(".", Unpooled.WrappedBuffer(new byte[] { 0, 0 }));
    }

    // CLR: the private helper overload testDecodeName(String, ByteBuf) is TestDecodeName0 because xUnit1024 forbids a
    // helper overload with the name of a test method (as codec-base's ReleaseWhenMergeCumulateThrowsInExpand0).
    private static void TestDecodeName0(string expected, ByteBuf buffer)
    {
        try
        {
            DefaultDnsRecordDecoder decoder = new DefaultDnsRecordDecoder();
            Assert.Equal(expected, decoder.DecodeName0(buffer));
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestDecodePtrRecord()
    {
        DefaultDnsRecordDecoder decoder = new DefaultDnsRecordDecoder();
        ByteBuf buffer = Unpooled.Buffer().WriteByte(0);
        int readerIndex = buffer.ReaderIndex;
        int writerIndex = buffer.WriterIndex;

        try
        {
            IDnsPtrRecord record = (IDnsPtrRecord) decoder.DecodeRecord(
                    "netty.io", DnsRecordType.PTR, IDnsRecord.CLASS_IN, 60, buffer, 0, 1);
            Assert.Equal("netty.io.", record.Name());
            Assert.Equal(IDnsRecord.CLASS_IN, record.DnsClass());
            Assert.Equal(60, record.TimeToLive());
            Assert.Equal(DnsRecordType.PTR, record.Type());
            Assert.Equal(readerIndex, buffer.ReaderIndex);
            Assert.Equal(writerIndex, buffer.WriterIndex);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestdecompressCompressPointer()
    {
        byte[] compressionPointer = {
                5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o', 0,
                (byte) 0xC0, 0
        };
        ByteBuf buffer = Unpooled.WrappedBuffer(compressionPointer);
        ByteBuf uncompressed = null;

        try
        {
            uncompressed = DnsCodecUtil.DecompressDomainName(buffer.Duplicate().SetIndex(10, 12));
            Assert.Equal(0, ByteBufUtil.Compare(buffer.Duplicate().SetIndex(0, 10), uncompressed));
        }
        finally
        {
            buffer.Release();

            if (uncompressed != null)
            {
                uncompressed.Release();
            }
        }
    }

    [Fact]
    public void TestdecompressNestedCompressionPointer()
    {
        byte[] nestedCompressionPointer = {
                6, (byte) 'g', (byte) 'i', (byte) 't', (byte) 'h', (byte) 'u', (byte) 'b', 2, (byte) 'i', (byte) 'o', 0, // github.io
                5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', (byte) 0xC0, 0, // netty.github.io
                (byte) 0xC0, 11, // netty.github.io
        };
        ByteBuf buffer = Unpooled.WrappedBuffer(nestedCompressionPointer);
        ByteBuf uncompressed = null;

        try
        {
            uncompressed = DnsCodecUtil.DecompressDomainName(buffer.Duplicate().SetIndex(19, 21));
            Assert.Equal(0, ByteBufUtil.Compare(
                    Unpooled.WrappedBuffer(new byte[] {
                            5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y',
                            6, (byte) 'g', (byte) 'i', (byte) 't', (byte) 'h', (byte) 'u', (byte) 'b',
                            2, (byte) 'i', (byte) 'o', 0
                    }), uncompressed));
        }
        finally
        {
            buffer.Release();

            if (uncompressed != null)
            {
                uncompressed.Release();
            }
        }
    }

    [Fact]
    public void TestDecodeCompressionRDataPointer()
    {
        DefaultDnsRecordDecoder decoder = new DefaultDnsRecordDecoder();
        byte[] compressionPointer = {
                5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o', 0,
                (byte) 0xC0, 0
        };
        ByteBuf buffer = Unpooled.WrappedBuffer(compressionPointer);
        DefaultDnsRawRecord cnameRecord = null;
        DefaultDnsRawRecord nsRecord = null;

        try
        {
            cnameRecord = (DefaultDnsRawRecord) decoder.DecodeRecord(
                    "netty.github.io", DnsRecordType.CNAME, IDnsRecord.CLASS_IN, 60, buffer, 10, 2);
            Assert.Equal(0, ByteBufUtil.Compare(buffer.Duplicate().SetIndex(0, 10), cnameRecord.Content),
                "The rdata of CNAME-type record should be decompressed in advance");
            Assert.Equal("netty.io.", DnsCodecUtil.DecodeDomainName(cnameRecord.Content));
            nsRecord = (DefaultDnsRawRecord) decoder.DecodeRecord(
                    "netty.github.io", DnsRecordType.NS, IDnsRecord.CLASS_IN, 60, buffer, 10, 2);
            Assert.Equal(0, ByteBufUtil.Compare(buffer.Duplicate().SetIndex(0, 10), nsRecord.Content),
                        "The rdata of NS-type record should be decompressed in advance");
            Assert.Equal("netty.io.", DnsCodecUtil.DecodeDomainName(nsRecord.Content));
        }
        finally
        {
            buffer.Release();

            if (cnameRecord != null)
            {
                cnameRecord.Release();
            }

            if (nsRecord != null)
            {
                nsRecord.Release();
            }
        }
    }

    [Fact]
    public void TestDecodeCompressionRDataPointerMX()
    {
        DefaultDnsRecordDecoder decoder = new DefaultDnsRecordDecoder();
        byte[] compressionPointer = {
                5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o', 0,
                0, 10,          // preference = 10
                (byte) 0xC0, 0  // record is a pointer to netty.io
        };

        byte[] expected = {
                0, 10, // pref = 10
                5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o', 0
        };
        ByteBuf buffer = Unpooled.WrappedBuffer(compressionPointer);
        DefaultDnsRawRecord mxRecord = null;
        ByteBuf expectedBuf = null;

        try
        {
            mxRecord = (DefaultDnsRawRecord) decoder.DecodeRecord(
                    "mail.example.com",
                    DnsRecordType.MX,
                    IDnsRecord.CLASS_IN,
                    60,
                    buffer,
                    10,
                    4);

            expectedBuf = Unpooled.WrappedBuffer(expected);

            Assert.Equal(0, ByteBufUtil.Compare(expectedBuf, mxRecord.Content),
                         "The rdata of MX-type record should be decompressed in advance");
            Assert.Equal(10, mxRecord.Content.GetUnsignedShort(0));

            ByteBuf exchangerName = mxRecord.Content.Duplicate().SetIndex(2, mxRecord.Content.WriterIndex);
            Assert.Equal("netty.io.", DnsCodecUtil.DecodeDomainName(exchangerName));
        }
        finally
        {
            buffer.Release();

            if (expectedBuf != null)
            {
                expectedBuf.Release();
            }

            if (mxRecord != null)
            {
                mxRecord.Release();
            }
        }
    }

    [Fact]
    public void TestDecodeMessageCompression()
    {
        // See https://www.ietf.org/rfc/rfc1035 [4.1.4. Message compression]
        DefaultDnsRecordDecoder decoder = new DefaultDnsRecordDecoder();
        byte[] rfcExample = { 1, (byte) 'F', 3, (byte) 'I', (byte) 'S', (byte) 'I', 4, (byte) 'A', (byte) 'R', (byte) 'P', (byte) 'A',
                0, 3, (byte) 'F', (byte) 'O', (byte) 'O',
                (byte) 0xC0, 0, // this is 20 in the example
                (byte) 0xC0, 6, // this is 26 in the example
        };
        DefaultDnsRawRecord rawPlainRecord = null;
        DefaultDnsRawRecord rawUncompressedRecord = null;
        DefaultDnsRawRecord rawUncompressedIndexedRecord = null;
        ByteBuf buffer = Unpooled.WrappedBuffer(rfcExample);

        try
        {
            // First lets test that our utility function can correctly handle index references and decompression.
            string plainName = DefaultDnsRecordDecoder.DecodeName(buffer.Duplicate());
            Assert.Equal("F.ISI.ARPA.", plainName);
            string uncompressedPlainName = DefaultDnsRecordDecoder.DecodeName(buffer.Duplicate().SetIndex(16, 20));
            Assert.Equal(plainName, uncompressedPlainName);
            string uncompressedIndexedName = DefaultDnsRecordDecoder.DecodeName(buffer.Duplicate().SetIndex(12, 20));
            Assert.Equal("FOO." + plainName, uncompressedIndexedName);

            // Now lets make sure out object parsing produces the same results for non PTR type (just use CNAME).
            rawPlainRecord = (DefaultDnsRawRecord) decoder.DecodeRecord(
                    plainName, DnsRecordType.CNAME, IDnsRecord.CLASS_IN, 60, buffer, 0, 11);
            Assert.Equal(plainName, rawPlainRecord.Name());
            Assert.Equal(plainName, DefaultDnsRecordDecoder.DecodeName(rawPlainRecord.Content));

            rawUncompressedRecord = (DefaultDnsRawRecord) decoder.DecodeRecord(
                    uncompressedPlainName, DnsRecordType.CNAME, IDnsRecord.CLASS_IN, 60, buffer, 16, 4);
            Assert.Equal(uncompressedPlainName, rawUncompressedRecord.Name());
            Assert.Equal(uncompressedPlainName, DefaultDnsRecordDecoder.DecodeName(rawUncompressedRecord.Content));

            rawUncompressedIndexedRecord = (DefaultDnsRawRecord) decoder.DecodeRecord(
                    uncompressedIndexedName, DnsRecordType.CNAME, IDnsRecord.CLASS_IN, 60, buffer, 12, 8);
            Assert.Equal(uncompressedIndexedName, rawUncompressedIndexedRecord.Name());
            Assert.Equal(uncompressedIndexedName,
                         DefaultDnsRecordDecoder.DecodeName(rawUncompressedIndexedRecord.Content));

            // Now lets make sure out object parsing produces the same results for PTR type.
            IDnsPtrRecord ptrRecord = (IDnsPtrRecord) decoder.DecodeRecord(
                    plainName, DnsRecordType.PTR, IDnsRecord.CLASS_IN, 60, buffer, 0, 11);
            Assert.Equal(plainName, ptrRecord.Name());
            Assert.Equal(plainName, ptrRecord.Hostname());

            ptrRecord = (IDnsPtrRecord) decoder.DecodeRecord(
                    uncompressedPlainName, DnsRecordType.PTR, IDnsRecord.CLASS_IN, 60, buffer, 16, 4);
            Assert.Equal(uncompressedPlainName, ptrRecord.Name());
            Assert.Equal(uncompressedPlainName, ptrRecord.Hostname());

            ptrRecord = (IDnsPtrRecord) decoder.DecodeRecord(
                    uncompressedIndexedName, DnsRecordType.PTR, IDnsRecord.CLASS_IN, 60, buffer, 12, 8);
            Assert.Equal(uncompressedIndexedName, ptrRecord.Name());
            Assert.Equal(uncompressedIndexedName, ptrRecord.Hostname());
        }
        finally
        {
            if (rawPlainRecord != null)
            {
                rawPlainRecord.Release();
            }

            if (rawUncompressedRecord != null)
            {
                rawUncompressedRecord.Release();
            }

            if (rawUncompressedIndexedRecord != null)
            {
                rawUncompressedIndexedRecord.Release();
            }

            buffer.Release();
        }
    }

    [Fact]
    public void TestTruncatedPacket()
    {
        ByteBuf buffer = Unpooled.Buffer();
        buffer.WriteByte(0);
        buffer.WriteShort(DnsRecordType.A.IntValue());
        buffer.WriteShort(1);
        buffer.WriteInt(32);

        // Write a truncated last value.
        buffer.WriteByte(0);
        DefaultDnsRecordDecoder decoder = new DefaultDnsRecordDecoder();

        try
        {
            int readerIndex = buffer.ReaderIndex;
            Assert.Null(decoder.DecodeRecord<IDnsRecord>(buffer));
            Assert.Equal(readerIndex, buffer.ReaderIndex);
        }
        finally
        {
            buffer.Release();
        }
    }
}
