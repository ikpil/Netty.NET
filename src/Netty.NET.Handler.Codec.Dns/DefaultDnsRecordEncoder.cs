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

using System;
using System.Globalization;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * The default {@link DnsRecordEncoder} implementation.
 *
 * @see DefaultDnsRecordDecoder
 */
// CLR: Java protected also admits the package: IDnsRecordEncoder.DEFAULT and the tests construct the encoder and call
// encodeName(String, ByteBuf), so these members are protected internal ("accessible from the current assembly or from
// types that are derived from the containing class",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal); the package-private
// calculateEcsAddressLength is internal. Class<?>[] is Type[] (UnsupportedMessageTypeException takes params Type[]),
// IllegalArgumentException is ArgumentException (docs/codec-base-porting.md, Shared CLR adaptations), and
// writeBytes(byte[], int, int) is WriteBytes(ReadOnlySpan<byte>) over the array range ("Span replaces array ranges",
// docs/buffer-porting.md). The ADDRESS bytes are System.Byte; the masks of padWithZeros select the same bits.
// Numbers in messages use the invariant culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class DefaultDnsRecordEncoder : IDnsRecordEncoder
{
    // CLR: Byte.SIZE is "the number of bits used to represent a byte value in two's complement binary form"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Byte.html), 8 = sizeof(byte) * 8.
    private const int PREFIX_MASK = sizeof(byte) * 8 - 1;

    /**
     * Creates a new instance.
     */
    protected internal DefaultDnsRecordEncoder()
    {
    }

    public void EncodeQuestion(IDnsQuestion question, ByteBuf @out)
    {
        EncodeName(question.Name(), @out);
        @out.WriteShort(question.Type().IntValue());
        @out.WriteShort(question.DnsClass());
    }

    private static readonly Type[] SUPPORTED_MESSAGES = new Type[] {
            typeof(IDnsQuestion), typeof(IDnsPtrRecord),
            typeof(IDnsOptEcsRecord), typeof(IDnsOptPseudoRecord), typeof(IDnsRawRecord) };

    public virtual void EncodeRecord(IDnsRecord record, ByteBuf @out)
    {
        if (record is IDnsQuestion)
        {
            EncodeQuestion((IDnsQuestion) record, @out);
        }
        else if (record is IDnsPtrRecord)
        {
            EncodePtrRecord((IDnsPtrRecord) record, @out);
        }
        else if (record is IDnsOptEcsRecord)
        {
            EncodeOptEcsRecord((IDnsOptEcsRecord) record, @out);
        }
        else if (record is IDnsOptPseudoRecord)
        {
            EncodeOptPseudoRecord((IDnsOptPseudoRecord) record, @out);
        }
        else if (record is IDnsRawRecord)
        {
            EncodeRawRecord((IDnsRawRecord) record, @out);
        }
        else
        {
            throw new UnsupportedMessageTypeException(record, SUPPORTED_MESSAGES);
        }
    }

    // CLR: the (int) narrowing of the TTL is unchecked like Java's.
    private void EncodeRecord0(IDnsRecord record, ByteBuf @out)
    {
        EncodeName(record.Name(), @out);
        @out.WriteShort(record.Type().IntValue());
        @out.WriteShort(record.DnsClass());
        @out.WriteInt(unchecked((int) record.TimeToLive()));
    }

    // CLR: writerIndex(int) is the ByteBuf.WriterIndex property setter of the Buffer port.
    private void EncodePtrRecord(IDnsPtrRecord record, ByteBuf @out)
    {
        EncodeRecord0(record, @out);
        int writerIndex = @out.WriterIndex;
        // Skip 2 bytes as these will be used to encode the rdataLen after we know how many bytes were written.
        // See https://www.rfc-editor.org/rfc/rfc1035.html#section-3.2.1
        @out.WriterIndex = writerIndex + 2;
        EncodeName(record.Hostname(), @out);
        int rdLength = @out.WriterIndex - (writerIndex + 2);
        @out.SetShort(writerIndex, rdLength);
    }

    private void EncodeOptPseudoRecord(IDnsOptPseudoRecord record, ByteBuf @out)
    {
        EncodeRecord0(record, @out);
        @out.WriteShort(0);
    }

    private void EncodeOptEcsRecord(IDnsOptEcsRecord record, ByteBuf @out)
    {
        EncodeRecord0(record, @out);

        int sourcePrefixLength = record.SourcePrefixLength();
        int scopePrefixLength = record.ScopePrefixLength();
        int lowOrderBitsToPreserve = sourcePrefixLength & PREFIX_MASK;

        byte[] bytes = record.Address();
        int addressBits = bytes.Length << 3;
        if (addressBits < sourcePrefixLength || sourcePrefixLength < 0)
        {
            throw new ArgumentException(sourcePrefixLength.ToString(CultureInfo.InvariantCulture) + ": " +
                    sourcePrefixLength.ToString(CultureInfo.InvariantCulture) + " (expected: 0 >= " +
                    addressBits.ToString(CultureInfo.InvariantCulture) + ')');
        }

        // See https://www.iana.org/assignments/address-family-numbers/address-family-numbers.xhtml
        short addressNumber = (short) (bytes.Length == 4 ? 1 : 2);
        int payloadLength = CalculateEcsAddressLength(sourcePrefixLength, lowOrderBitsToPreserve);

        int fullPayloadLength = 2 + // OPTION-CODE
                2 + // OPTION-LENGTH
                2 + // FAMILY
                1 + // SOURCE PREFIX-LENGTH
                1 + // SCOPE PREFIX-LENGTH
                payloadLength; //  ADDRESS...

        @out.WriteShort(fullPayloadLength);
        @out.WriteShort(8); // This is the defined type for ECS.

        @out.WriteShort(fullPayloadLength - 4); // Not include OPTION-CODE and OPTION-LENGTH
        @out.WriteShort(addressNumber);
        @out.WriteByte(sourcePrefixLength);
        @out.WriteByte(scopePrefixLength); // Must be 0 in queries.

        if (lowOrderBitsToPreserve > 0)
        {
            int bytesLength = payloadLength - 1;
            @out.WriteBytes(bytes.AsSpan(0, bytesLength));

            // Pad the leftover of the last byte with zeros.
            @out.WriteByte(PadWithZeros(bytes[bytesLength], lowOrderBitsToPreserve));
        }
        else
        {
            // The sourcePrefixLength align with Byte so just copy in the bytes directly.
            @out.WriteBytes(bytes.AsSpan(0, payloadLength));
        }
    }

    // Package-Private for testing
    internal static int CalculateEcsAddressLength(int sourcePrefixLength, int lowOrderBitsToPreserve)
    {
        return (sourcePrefixLength >>> 3) + (lowOrderBitsToPreserve != 0 ? 1 : 0);
    }

    private void EncodeRawRecord(IDnsRawRecord record, ByteBuf @out)
    {
        EncodeRecord0(record, @out);

        ByteBuf content = record.Content;
        int contentLen = content.ReadableBytes;

        @out.WriteShort(contentLen);
        @out.WriteBytes(content, content.ReaderIndex, contentLen);
    }

    protected internal virtual void EncodeName(string name, ByteBuf buf)
    {
        DnsCodecUtil.EncodeDomainName(name, buf);
    }

    private static byte PadWithZeros(byte b, int lowOrderBitsToPreserve)
    {
        switch (lowOrderBitsToPreserve)
        {
        case 0:
            return 0;
        case 1:
            return (byte) (0x80 & b);
        case 2:
            return (byte) (0xC0 & b);
        case 3:
            return (byte) (0xE0 & b);
        case 4:
            return (byte) (0xF0 & b);
        case 5:
            return (byte) (0xF8 & b);
        case 6:
            return (byte) (0xFC & b);
        case 7:
            return (byte) (0xFE & b);
        case 8:
            return b;
        default:
            throw new ArgumentException("lowOrderBitsToPreserve: " +
                                        lowOrderBitsToPreserve.ToString(CultureInfo.InvariantCulture));
        }
    }
}
