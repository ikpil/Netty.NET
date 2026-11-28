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
using System.Net;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * Provides some utility methods for DNS message implementations.
 */
// CLR: Numbers in text use the invariant culture
// (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
internal static class DnsMessageUtil
{

    internal static StringBuilder AppendQuery(StringBuilder buf, IDnsQuery query)
    {
        AppendQueryHeader(buf, query);
        AppendAllRecords(buf, query);
        return buf;
    }

    internal static StringBuilder AppendResponse(StringBuilder buf, IDnsResponse response)
    {
        AppendResponseHeader(buf, response);
        AppendAllRecords(buf, response);
        return buf;
    }

    internal static StringBuilder AppendRecordClass(StringBuilder buf, int dnsClass)
    {
        string name;

        switch (dnsClass &= 0xFFFF)
        {
        case IDnsRecord.CLASS_IN:
            name = "IN";
            break;
        case IDnsRecord.CLASS_CSNET:
            name = "CSNET";
            break;
        case IDnsRecord.CLASS_CHAOS:
            name = "CHAOS";
            break;
        case IDnsRecord.CLASS_HESIOD:
            name = "HESIOD";
            break;
        case IDnsRecord.CLASS_NONE:
            name = "NONE";
            break;
        case IDnsRecord.CLASS_ANY:
            name = "ANY";
            break;
        default:
            name = null;
            break;
        }

        if (name != null)
        {
            buf.Append(name);
        }
        else
        {
            buf.Append("UNKNOWN(").Append(dnsClass.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        return buf;
    }

    private static void AppendQueryHeader(StringBuilder buf, IDnsQuery msg)
    {
        buf.Append(StringUtil.SimpleClassName(msg))
           .Append('(');

        AppendAddresses(buf, msg)
           .Append("id: ")
           .Append(msg.Id().ToString(CultureInfo.InvariantCulture))
           .Append(", ")
           .Append(msg.OpCode());

        if (msg.IsRecursionDesired())
        {
            buf.Append(", RD");
        }

        if (msg.Z() != 0)
        {
            buf.Append(", Z: ")
               .Append(msg.Z().ToString(CultureInfo.InvariantCulture));
        }

        buf.Append(')');
    }

    private static void AppendResponseHeader(StringBuilder buf, IDnsResponse msg)
    {
        buf.Append(StringUtil.SimpleClassName(msg))
           .Append('(');

        AppendAddresses(buf, msg)
           .Append("id: ")
           .Append(msg.Id().ToString(CultureInfo.InvariantCulture))
           .Append(", ")
           .Append(msg.OpCode())
           .Append(", ")
           .Append(msg.Code())
           .Append(',');

        bool hasComma = true;

        if (msg.IsRecursionDesired())
        {
            hasComma = false;
            buf.Append(" RD");
        }

        if (msg.IsAuthoritativeAnswer())
        {
            hasComma = false;
            buf.Append(" AA");
        }

        if (msg.IsTruncated())
        {
            hasComma = false;
            buf.Append(" TC");
        }

        if (msg.IsRecursionAvailable())
        {
            hasComma = false;
            buf.Append(" RA");
        }

        if (msg.Z() != 0)
        {
            if (!hasComma)
            {
                buf.Append(',');
            }

            buf.Append(" Z: ")
               .Append(msg.Z().ToString(CultureInfo.InvariantCulture));
        }

        if (hasComma)
        {
            buf[buf.Length - 1] = ')';
        }
        else
        {
            buf.Append(')');
        }
    }

    // CLR: AddressedEnvelope<?, SocketAddress> is IAddressedEnvelope<object, EndPoint>, which the covariant
    // IAddressedEnvelope<out T, out TAddress> matches for every reference content type
    // (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance). An address is appended
    // with EndPoint.ToString(): IPEndPoint prints the address and port "(for example, 192.168.1.2:80)"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipendpoint.tostring?view=net-10.0), while Java's
    // InetSocketAddress.toString() prints InetAddress.toString() (host name, '/', literal) and the port
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html), as in Transport's
    // DefaultAddressedEnvelope.ToString().
    private static StringBuilder AppendAddresses(StringBuilder buf, IDnsMessage msg)
    {

        if (!(msg is IAddressedEnvelope<object, EndPoint>))
        {
            return buf;
        }

        IAddressedEnvelope<object, EndPoint> envelope = (IAddressedEnvelope<object, EndPoint>) msg;

        EndPoint addr = envelope.Sender;
        if (addr != null)
        {
            buf.Append("from: ")
               .Append(addr)
               .Append(", ");
        }

        addr = envelope.Recipient;
        if (addr != null)
        {
            buf.Append("to: ")
               .Append(addr)
               .Append(", ");
        }

        return buf;
    }

    private static void AppendAllRecords(StringBuilder buf, IDnsMessage msg)
    {
        AppendRecords(buf, msg, DnsSection.QUESTION);
        AppendRecords(buf, msg, DnsSection.ANSWER);
        AppendRecords(buf, msg, DnsSection.AUTHORITY);
        AppendRecords(buf, msg, DnsSection.ADDITIONAL);
    }

    private static void AppendRecords(StringBuilder buf, IDnsMessage message, DnsSection section)
    {
        int count = message.Count(section);

        for (int i = 0; i < count; i ++)
        {
            buf.Append(StringUtil.NEWLINE)
               .Append(StringUtil.TAB)
               .Append(message.RecordAt<IDnsRecord>(section, i));
        }
    }

    internal static IDnsQuery DecodeDnsQuery(IDnsRecordDecoder decoder, ByteBuf buf, IDnsQueryFactory supplier)
    {
        IDnsQuery query = NewQuery(buf, supplier);
        bool success = false;

        try
        {
            int questionCount = DnsCodecUtil.CheckRecordCount(DnsSection.QUESTION, buf.ReadUnsignedShort());
            int answerCount = DnsCodecUtil.CheckRecordCount(DnsSection.ANSWER, buf.ReadUnsignedShort());
            int authorityRecordCount = DnsCodecUtil.CheckRecordCount(DnsSection.AUTHORITY, buf.ReadUnsignedShort());
            int additionalRecordCount = DnsCodecUtil.CheckRecordCount(DnsSection.ADDITIONAL, buf.ReadUnsignedShort());
            DecodeQuestions(decoder, query, buf, questionCount);
            DecodeRecords(decoder, query, DnsSection.ANSWER, buf, answerCount);
            DecodeRecords(decoder, query, DnsSection.AUTHORITY, buf, authorityRecordCount);
            DecodeRecords(decoder, query, DnsSection.ADDITIONAL, buf, additionalRecordCount);
            success = true;
            return query;
        }
        finally
        {
            if (!success)
            {
                query.Release();
            }
        }
    }

    private static IDnsQuery NewQuery(ByteBuf buf, IDnsQueryFactory supplier)
    {
        int id = buf.ReadUnsignedShort();
        int flags = buf.ReadUnsignedShort();
        if (flags >> 15 == 1)
        {
            throw new CorruptedFrameException("not a query");
        }

        IDnsQuery query = supplier.NewQuery(id, DnsOpCode.ValueOf((byte) (flags >> 11 & 0xf)));
        query.SetRecursionDesired((flags >> 8 & 1) == 1);
        query.SetZ(flags >> 4 & 0x7);
        return query;
    }

    private static void DecodeQuestions(IDnsRecordDecoder decoder,
                                        IDnsQuery query, ByteBuf buf, int questionCount)
    {
        for (int i = questionCount; i > 0; --i)
        {
            query.AddRecord(DnsSection.QUESTION, decoder.DecodeQuestion(buf));
        }
    }

    private static void DecodeRecords(IDnsRecordDecoder decoder,
                                      IDnsQuery query, DnsSection section, ByteBuf buf, int count)
    {
        for (int i = count; i > 0; --i)
        {
            IDnsRecord r = decoder.DecodeRecord<IDnsRecord>(buf);
            if (r == null)
            {
                break;
            }

            query.AddRecord(section, r);
        }
    }

    internal static void EncodeDnsResponse(IDnsRecordEncoder encoder, IDnsResponse response, ByteBuf buf)
    {
        bool success = false;

        try
        {
            EncodeHeader(response, buf);
            EncodeQuestions(encoder, response, buf);
            EncodeRecords(encoder, response, DnsSection.ANSWER, buf);
            EncodeRecords(encoder, response, DnsSection.AUTHORITY, buf);
            EncodeRecords(encoder, response, DnsSection.ADDITIONAL, buf);
            success = true;
        }
        finally
        {
            if (!success)
            {
                buf.Release();
            }
        }
    }

    /**
     * Encodes the header that is always 12 bytes long.
     *
     * @param response the response header being encoded
     * @param buf      the buffer the encoded data should be written to
     */
    private static void EncodeHeader(IDnsResponse response, ByteBuf buf)
    {
        buf.WriteShort(response.Id());
        int flags = 32768;
        flags |= (response.OpCode().ByteValue() & 0xFF) << 11;

        if (response.IsAuthoritativeAnswer())
        {
            flags |= 1 << 10;
        }

        if (response.IsTruncated())
        {
            flags |= 1 << 9;
        }

        if (response.IsRecursionDesired())
        {
            flags |= 1 << 8;
        }

        if (response.IsRecursionAvailable())
        {
            flags |= 1 << 7;
        }

        flags |= response.Z() << 4;
        flags |= response.Code().IntValue();
        buf.WriteShort(flags);
        buf.WriteShort(response.Count(DnsSection.QUESTION));
        buf.WriteShort(response.Count(DnsSection.ANSWER));
        buf.WriteShort(response.Count(DnsSection.AUTHORITY));
        buf.WriteShort(response.Count(DnsSection.ADDITIONAL));
    }

    private static void EncodeQuestions(IDnsRecordEncoder encoder, IDnsResponse response, ByteBuf buf)
    {
        int count = response.Count(DnsSection.QUESTION);

        for (int i = 0; i < count; ++i)
        {
            encoder.EncodeQuestion(response.RecordAt<IDnsQuestion>(DnsSection.QUESTION, i), buf);
        }
    }

    private static void EncodeRecords(IDnsRecordEncoder encoder,
                                      IDnsResponse response, DnsSection section, ByteBuf buf)
    {
        int count = response.Count(section);

        for (int i = 0; i < count; ++i)
        {
            encoder.EncodeRecord(response.RecordAt<IDnsRecord>(section, i), buf);
        }
    }

    // CLR: the nested Java interface takes the I prefix.
    internal interface IDnsQueryFactory
    {
        IDnsQuery NewQuery(int id, DnsOpCode dnsOpCode);
    }
}
