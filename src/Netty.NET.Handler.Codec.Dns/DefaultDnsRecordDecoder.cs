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

using System.Globalization;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * The default {@link DnsRecordDecoder} implementation.
 *
 * @see DefaultDnsRecordEncoder
 */
// CLR: Java protected also admits the package: IDnsRecordDecoder.DEFAULT and the tests construct the decoder and call
// decodeRecord(String, ...) and decodeName0(ByteBuf), so these members are protected internal ("accessible from the
// current assembly or from types that are derived from the containing class",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal), as codec-base's
// LineBasedFrameDecoder.Decode. The final decodeQuestion/decodeRecord are non-virtual. <T extends DnsRecord> is
// T : IDnsRecord; the unchecked Java cast of the record is a checked C# cast that throws InvalidCastException for a
// mismatched T, and "no record" is default(T), null for every record type. readUnsignedShort()/readUnsignedInt()
// return the unsigned ushort/uint of the Buffer port, widened to int/long. 'throws Exception' has no C# counterpart.
public class DefaultDnsRecordDecoder : IDnsRecordDecoder
{

    internal const string ROOT = ".";

    /**
     * Creates a new instance.
     */
    protected internal DefaultDnsRecordDecoder() { }

    public IDnsQuestion DecodeQuestion(ByteBuf @in)
    {
        string name = DecodeName(@in);
        DnsRecordType type = DnsRecordType.ValueOf(@in.ReadUnsignedShort());
        int qClass = @in.ReadUnsignedShort();
        return new DefaultDnsQuestion(name, type, qClass);
    }

    public T DecodeRecord<T>(ByteBuf @in) where T : IDnsRecord
    {
        int startOffset = @in.ReaderIndex;
        string name = DecodeName(@in);

        int endOffset = @in.WriterIndex;
        if (endOffset - @in.ReaderIndex < 10)
        {
            // Not enough data
            @in.ReaderIndex = startOffset;
            return default;
        }

        DnsRecordType type = DnsRecordType.ValueOf(@in.ReadUnsignedShort());
        int aClass = @in.ReadUnsignedShort();
        long ttl = @in.ReadUnsignedInt();
        int length = @in.ReadUnsignedShort();
        int offset = @in.ReaderIndex;

        if (endOffset - offset < length)
        {
            // Not enough data
            @in.ReaderIndex = startOffset;
            return default;
        }

        T record = (T) DecodeRecord(name, type, aClass, ttl, @in, offset, length);
        @in.ReaderIndex = offset + length;
        return record;
    }

    /**
     * Decodes a record from the information decoded so far by {@link #decodeRecord(ByteBuf)}.
     *
     * @param name the domain name of the record
     * @param type the type of the record
     * @param dnsClass the class of the record
     * @param timeToLive the TTL of the record
     * @param in the {@link ByteBuf} that contains the RDATA
     * @param offset the start offset of the RDATA in {@code in}
     * @param length the length of the RDATA
     *
     * @return a {@link DnsRawRecord}. Override this method to decode RDATA and return other record implementation.
     */
    protected internal virtual IDnsRecord DecodeRecord(
            string name, DnsRecordType type, int dnsClass, long timeToLive,
            ByteBuf @in, int offset, int length)
    {

        // DNS message compression means that domain names may contain "pointers" to other positions in the packet
        // to build a full message. This means the indexes are meaningful and we need the ability to reference the
        // indexes un-obstructed, and thus we cannot use a slice here.
        // See https://www.ietf.org/rfc/rfc1035 [4.1.4. Message compression]
        if (type == DnsRecordType.PTR)
        {
            return new DefaultDnsPtrRecord(
                    name, dnsClass, timeToLive, DecodeName0(@in.Duplicate().SetIndex(offset, offset + length)));
        }
        if (type == DnsRecordType.CNAME || type == DnsRecordType.NS)
        {
            ByteBuf decompressed = DnsCodecUtil.DecompressDomainName(
                    @in.Duplicate().SetIndex(offset, offset + length));
            try
            {
                IDnsRecord record = new DefaultDnsRawRecord(name, type, dnsClass, timeToLive, decompressed);
                decompressed = null;
                return record;
            }
            finally
            {
                if (decompressed != null)
                {
                    decompressed.Release();
                }
            }
        }
        if (type ==  DnsRecordType.MX)
        {
            // MX RDATA: 16-bit preference + exchange (domain name, possibly compressed)
            if (length < 3)
            {
                throw new CorruptedFrameException("MX record RDATA is too short: " +
                                                  length.ToString(CultureInfo.InvariantCulture));
            }
            int pref = @in.GetUnsignedShort(offset);
            ByteBuf exchange = null;
            ByteBuf @out = null;
            try
            {
                exchange = DnsCodecUtil.DecompressDomainName(
                        @in.Duplicate().SetIndex(offset + 2, offset + length));

                // Build decompressed RDATA = [preference][expanded exchange name]
                @out = @in.Alloc().Buffer(2 + exchange.ReadableBytes);
                @out.WriteShort(pref);
                @out.WriteBytes(exchange);

                IDnsRecord record = new DefaultDnsRawRecord(name, type, dnsClass, timeToLive, @out);
                @out = null;
                return record;
            }
            finally
            {
                if (exchange != null)
                {
                    exchange.Release();
                }
                if (@out != null)
                {
                    @out.Release();
                }
            }
        }

        ByteBuf content = @in.RetainedDuplicate();
        try
        {
            content.SetIndex(offset, offset + length);
            IDnsRecord record = new DefaultDnsRawRecord(name, type, dnsClass, timeToLive, content);
            content = null;
            return record;
        }
        finally
        {
            if (content != null)
            {
                content.Release();
            }
        }
    }

    /**
     * Retrieves a domain name given a buffer containing a DNS packet. If the
     * name contains a pointer, the position of the buffer will be set to
     * directly after the pointer's index after the name has been read.
     *
     * @param in the byte buffer containing the DNS packet
     * @return the domain name for an entry
     */
    protected internal virtual string DecodeName0(ByteBuf @in)
    {
        return DecodeName(@in);
    }

    /**
     * Retrieves a domain name given a buffer containing a DNS packet. If the
     * name contains a pointer, the position of the buffer will be set to
     * directly after the pointer's index after the name has been read.
     *
     * @param in the byte buffer containing the DNS packet
     * @return the domain name for an entry
     */
    public static string DecodeName(ByteBuf @in)
    {
        return DnsCodecUtil.DecodeDomainName(@in);
    }
}
