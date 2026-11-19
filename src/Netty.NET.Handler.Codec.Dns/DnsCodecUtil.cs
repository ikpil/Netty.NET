/*
 * Copyright 2019 The Netty Project
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
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using static Netty.NET.Handler.Codec.Dns.DefaultDnsRecordDecoder;

namespace Netty.NET.Handler.Codec.Dns;

// CLR: the final utility class with a private constructor is an internal static class; its package-private members
// are internal. CharsetUtil.UTF_8 is Encoding.UTF8, IllegalArgumentException is ArgumentException
// (docs/codec-base-porting.md, Shared CLR adaptations). Numbers in messages use the invariant culture
// (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
internal static class DnsCodecUtil
{

    // See https://datatracker.ietf.org/doc/html/rfc1035#section-2.3.4
    internal const int MAX_DOMAIN_NAME_LENGTH = 255;

    // A decoded name is at most 255 characters and every label contributes at least 2 ("x."), so a valid name has
    // at most 127 labels and therefore never needs to follow more compression pointers than that. Bounding the hops
    // per name keeps the decoding cost of each name constant instead of proportional to the message size.
    internal const int MAX_COMPRESSION_POINTERS = 127;

    // The RFC does not define a maximum number of records per section (the count is a 16-bit value). The cost of
    // decoding a single record is bounded (see MAX_DOMAIN_NAME_LENGTH and MAX_COMPRESSION_POINTERS), so this only
    // limits the total work per message. It is high enough that a full 64 KiB TCP message of minimal records with
    // compressed names (e.g. A records, 16 bytes each) still decodes.
    internal const int MAX_RECORDS_PER_SECTION = 4096;

    // CLR: a static class has no instance constructor; the private constructor's only statement was the comment:
    // Util class

    internal static void EncodeDomainName(string name, ByteBuf buf)
    {
        if (ROOT.Equals(name))
        {
            // Root domain
            buf.WriteByte(0);
            return;
        }

        int totalLength = 0;
        string[] labels = Split(name);
        for (int i = 0; i < labels.Length; i++)
        {
            string label = labels[i];
            int labelLen = label.Length;
            if (labelLen == 0)
            {
                if (i == labels.Length - 1)
                {
                    // zero-length label at the end means the end of the name.
                    break;
                }
                else
                {
                    throw new ArgumentException("DNS name contains empty label: " + name);
                }
            }
            if (labelLen > 63)
            {
                throw new ArgumentException(
                        "DNS label length " + labelLen.ToString(CultureInfo.InvariantCulture) +
                        " exceeds maximum of 63: " + name);
            }
            int idx = label.IndexOf('\0');
            if (idx != -1)
            {
                throw new ArgumentException(
                        "DNS label contains null byte at index " + idx.ToString(CultureInfo.InvariantCulture));
            }
            totalLength += 1 + labelLen;
            if (totalLength > MAX_DOMAIN_NAME_LENGTH)
            {
                throw new ArgumentException(
                        "DNS name exceeds maximum length of " +
                        MAX_DOMAIN_NAME_LENGTH.ToString(CultureInfo.InvariantCulture) + ": " + name);
            }
            buf.WriteByte(labelLen);
            ByteBufUtil.WriteAscii(buf, label);
        }

        buf.WriteByte(0); // marks end of name field
    }

    // CLR: String.split("\\.") works "as if by invoking the two-argument split method with the given expression and a
    // limit argument of zero. Trailing empty strings are therefore not included in the resulting array", and "if the
    // expression does not match any part of the input then the resulting array has just one element, namely this
    // string" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html). string.Split keeps
    // trailing empty strings, so they are removed here: "netty.io.." gives { "netty", "io" } as in Java.
    private static string[] Split(string name)
    {
        List<string> labels = new List<string>(name.Split('.'));
        int count = labels.Count;
        while (count > 0 && labels[count - 1].Length == 0)
        {
            count--;
        }
        if (labels.Count == 1)
        {
            return labels.ToArray();
        }
        return labels.GetRange(0, count).ToArray();
    }

    /**
     * Returns the initial capacity to use for the {@link StringBuilder} that accumulates a decoded domain name.
     * A decoded name can never exceed {@link #MAX_DOMAIN_NAME_LENGTH} characters (see the check in
     * {@link #decodeDomainName(ByteBuf)}), so the capacity must not scale with {@code readable}, the number of
     * bytes left in the whole message: doing so would let a message with a huge amount of trailing data (e.g.
     * many more records after this name) force an oversized allocation for every single name decoded from it.
     */
    internal static int CalculateMaxNameLength(int readable)
    {
        return Math.Min(readable, MAX_DOMAIN_NAME_LENGTH);
    }

    /**
     * Ensures the record count of a section, as announced in the header of a DNS message, does not exceed
     * {@link #MAX_RECORDS_PER_SECTION}.
     *
     * @throws TooLongFrameException if the count is too large.
     */
    internal static int CheckRecordCount(DnsSection section, int count)
    {
        if (count > MAX_RECORDS_PER_SECTION)
        {
            throw new TooLongFrameException(
                    section + " record count must be <= " +
                    MAX_RECORDS_PER_SECTION.ToString(CultureInfo.InvariantCulture) + " but was " +
                    count.ToString(CultureInfo.InvariantCulture));
        }
        return count;
    }

    // CLR: readerIndex(int) is the ByteBuf.ReaderIndex property setter of the Buffer port, and readUnsignedByte() is
    // ReadByte(), which returns the unsigned System.Byte (docs/buffer-porting.md).
    internal static string DecodeDomainName(ByteBuf @in)
    {
        int position = -1;
        int pointers = 0;
        int end = @in.WriterIndex;
        int readable = @in.ReadableBytes;

        // Looking at the spec we should always have at least enough readable bytes to read a byte here but it seems
        // some servers do not respect this for empty names. So just workaround this and return an empty name in this
        // case.
        //
        // See:
        // - https://github.com/netty/netty/issues/5014
        // - https://www.ietf.org/rfc/rfc1035.txt , Section 3.1
        if (readable == 0)
        {
            return ROOT;
        }

        StringBuilder name = new StringBuilder(CalculateMaxNameLength(readable));
        while (@in.IsReadable())
        {
            int len = @in.ReadByte();
            bool pointer = (len & 0xc0) == 0xc0;
            if (pointer)
            {
                if (position == -1)
                {
                    position = @in.ReaderIndex + 1;
                }

                if (!@in.IsReadable())
                {
                    throw new CorruptedFrameException("truncated pointer in a name");
                }

                int next = (len & 0x3f) << 8 | @in.ReadByte();
                if (next >= end)
                {
                    throw new CorruptedFrameException("name has an out-of-range pointer");
                }
                @in.ReaderIndex = next;

                // Bounds the work per name, which also terminates loops.
                if (++pointers > MAX_COMPRESSION_POINTERS)
                {
                    throw new CorruptedFrameException(
                            "name contains more than " + MAX_COMPRESSION_POINTERS.ToString(CultureInfo.InvariantCulture) +
                            " compression pointers");
                }
            }
            else if (len != 0)
            {
                if (!@in.IsReadable(len))
                {
                    throw new CorruptedFrameException("truncated label in a name");
                }
                // See https://datatracker.ietf.org/doc/html/rfc1035#section-2.3.4
                if (len > 63)
                {
                    throw new TooLongFrameException("label must be <= 63 but was " + len.ToString(CultureInfo.InvariantCulture));
                }
                name.Append(@in.ToString(@in.ReaderIndex, len, Encoding.UTF8)).Append('.');
                @in.SkipBytes(len);
                // See https://datatracker.ietf.org/doc/html/rfc1035#section-2.3.4
                if (name.Length > MAX_DOMAIN_NAME_LENGTH)
                {
                    throw new TooLongFrameException(
                            "domain name must be <= " + MAX_DOMAIN_NAME_LENGTH.ToString(CultureInfo.InvariantCulture) +
                            " but was " + name.Length.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            { // len == 0
                break;
            }
        }

        if (position != -1)
        {
            @in.ReaderIndex = position;
        }

        if (name.Length == 0)
        {
            return ROOT;
        }

        if (name[name.Length - 1] != '.')
        {
            name.Append('.');
        }

        return name.ToString();
    }

    /**
     * Decompress pointer data.
     * @param compression compressed data
     * @return decompressed data
     */
    // CLR: catch (Throwable) with rethrow is catch (Exception) with throw.
    internal static ByteBuf DecompressDomainName(ByteBuf compression)
    {
        string domainName = DecodeDomainName(compression);
        ByteBuf result = compression.Alloc().Buffer(domainName.Length << 1);
        try
        {
            EncodeDomainName(domainName, result);
        }
        catch (Exception)
        {
            result.Release();
            throw;
        }
        return result;
    }
}
