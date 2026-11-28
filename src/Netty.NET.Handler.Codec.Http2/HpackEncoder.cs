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

/*
 * Copyright 2014 Twitter, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Diagnostics;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.HpackUtil;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.MathUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * An HPACK encoder.
 *
 * <p>Implementation note:  This class is security sensitive, and depends on users correctly identifying their headers
 * as security sensitive or not.  If a header is considered not sensitive, methods names "insensitive" are used which
 * are fast, but don't provide any security guarantees.
 */
// CLR: the package-private final class is internal sealed and its package-private members are internal (the tests
// reach them through InternalsVisibleTo). Http2Headers is IEnumerable<IEntry<ICharSequence, ICharSequence>>, so
// for (Map.Entry<...> header : headers) is a foreach over codec-base IEntry (docs/codec-base-porting.md, Headers).
// SensitivityDetector is IHttp2HeadersEncoder.ISensitivityDetector. The field initializer latest = head reads an
// instance field, which a C# field initializer cannot do (CS0236,
// https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236), so the constructor assigns it. The simple enum
// IndexType is switched as a C# enum; throw new Error(...) is UnreachableException (docs/codec-http-porting.md,
// Shared CLR adaptations). AsciiString.array()/arrayOffset() are Array()/ArrayOffset();
// out.writeCharSequence(string, ISO_8859_1) writes AsciiString.c2b of each char (Java's
// AbstractByteBuf.setCharSequence0 takes the ByteBufUtil.writeAscii path for ISO_8859_1), which is
// ByteBufUtil.WriteAscii (docs/codec-http-porting.md: writeCharSequence(..., US_ASCII) is ByteBufUtil.WriteAscii).
// hashMask is a byte below 128 (the array length is at most 128), so the Java byte and System.Byte masks are
// equal; the non-negative mask is OR-ed into the long as uint (zero extension, which avoids the sign-extension
// warning CS0675) with the value of Java's widening. The private nested entry classes' fields are internal so the
// encoder reads them.
internal sealed class HpackEncoder
{
    internal const int NOT_FOUND = -1;
    internal const int HUFF_CODE_THRESHOLD = 512;
    // a hash map of header fields keyed by header name
    private readonly NameEntry[] nameEntries;

    // a hash map of header fields keyed by header name and value
    private readonly NameValueEntry[] nameValueEntries;

    private readonly NameValueEntry head = new NameValueEntry(-1, AsciiString.EMPTY_STRING,
      AsciiString.EMPTY_STRING, int.MaxValue, null);

    private NameValueEntry latest;

    private readonly HpackHuffmanEncoder hpackHuffmanEncoder = new HpackHuffmanEncoder();
    private readonly byte hashMask;
    private readonly bool ignoreMaxHeaderListSize;
    private readonly int huffCodeThreshold;
    private long size;
    private long maxHeaderTableSize;
    private long maxHeaderListSize;

    private bool pendingHeaderTableSizeUpdate;
    private long pendingMinHeaderTableSize = long.MaxValue;

    /**
     * Creates a new encoder.
     */
    internal HpackEncoder() : this(false)
    {
    }

    /**
     * Creates a new encoder.
     */
    internal HpackEncoder(bool ignoreMaxHeaderListSize) : this(ignoreMaxHeaderListSize, 64, HUFF_CODE_THRESHOLD)
    {
    }

    /**
     * Creates a new encoder.
     */
    internal HpackEncoder(bool ignoreMaxHeaderListSize, int arraySizeHint, int huffCodeThreshold)
    {
        latest = head;
        this.ignoreMaxHeaderListSize = ignoreMaxHeaderListSize;
        maxHeaderTableSize = DEFAULT_HEADER_TABLE_SIZE;
        maxHeaderListSize = MAX_HEADER_LIST_SIZE;
        // Enforce a bound of [2, 128] because hashMask is a byte. The max possible value of hashMask is one less
        // than the length of this array, and we want the mask to be > 0. There is also simply an
        // upper limit on how many entries can be safely held, as nameEntries will have duplicate
        // entries every time a header name is used, and thus form a long collision chain.
        nameEntries = new NameEntry[FindNextPositivePowerOfTwo(Math.Max(2, Math.Min(arraySizeHint, 128)))];
        nameValueEntries = new NameValueEntry[nameEntries.Length];
        hashMask = (byte) (nameEntries.Length - 1);
        this.huffCodeThreshold = huffCodeThreshold;
    }

    /**
     * Encode the header field into the header block.
     * <p>
     * <strong>The given {@link CharSequence}s must be immutable!</strong>
     */
    public void EncodeHeaders(int streamId, ByteBuf @out, IHttp2Headers headers,
                              IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector)
    {
        if (ignoreMaxHeaderListSize)
        {
            EncodeHeadersIgnoreMaxHeaderListSize(@out, headers, sensitivityDetector);
        }
        else
        {
            EncodeHeadersEnforceMaxHeaderListSize(streamId, @out, headers, sensitivityDetector);
        }
    }

    private void EncodeHeadersEnforceMaxHeaderListSize(int streamId, ByteBuf @out, IHttp2Headers headers,
                                                       IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector)
                                                       {
        long headerSize = 0;

        // To ensure we stay consistent with our peer check the size is valid before we potentially modify HPACK state.
        foreach (IEntry<ICharSequence, ICharSequence> header in headers)
        {
            ICharSequence name = header.GetKey();
            ICharSequence value = header.GetValue();
            // OK to increment now and check for bounds after because this value is limited to unsigned int and will not
            // overflow.
            headerSize += HpackHeaderField.SizeOf(name, value);
            if (headerSize > maxHeaderListSize)
            {
                HeaderListSizeExceeded(streamId, maxHeaderListSize, false);
            }
        }

        EncodeHeadersIgnoreMaxHeaderListSize(@out, headers, sensitivityDetector);
    }

    private void EncodeHeadersIgnoreMaxHeaderListSize(ByteBuf @out, IHttp2Headers headers,
                                                      IHttp2HeadersEncoder.ISensitivityDetector sensitivityDetector)
                                                      {
        if (this.pendingHeaderTableSizeUpdate)
        {
            this.pendingHeaderTableSizeUpdate = false;

            // Casting to integer is safe as we verified the table size is a valid unsigned int in
            // setMaxHeaderTableSize().
            if (this.pendingMinHeaderTableSize < this.maxHeaderTableSize)
            {
                // The table size shrunk and increased again.
                EncodeInteger(@out, 0x20, 5, this.pendingMinHeaderTableSize);
            }

            this.pendingMinHeaderTableSize = long.MaxValue;
            EncodeInteger(@out, 0x20, 5, this.maxHeaderTableSize);
        }

        foreach (IEntry<ICharSequence, ICharSequence> header in headers)
        {
            ICharSequence name = header.GetKey();
            ICharSequence value = header.GetValue();
            EncodeHeader(@out, name, value, sensitivityDetector.IsSensitive(name, value),
              HpackHeaderField.SizeOf(name, value));
        }
    }

    /**
     * Encode the header field into the header block.
     * <p>
     * <strong>The given {@link CharSequence}s must be immutable!</strong>
     */
    private void EncodeHeader(ByteBuf @out, ICharSequence name, ICharSequence value, bool sensitive, long headerSize)
    {
        // If the header value is sensitive then it must never be indexed
        if (sensitive)
        {
            int nameIndex = GetNameIndex(name);
            EncodeLiteral(@out, name, value, IndexType.NEVER, nameIndex);
            return;
        }

        // If the peer will only use the static table
        if (maxHeaderTableSize == 0)
        {
            int staticTableIndex = HpackStaticTable.GetIndexInsensitive(name, value);
            if (staticTableIndex == HpackStaticTable.NOT_FOUND)
            {
                int nameIndex = HpackStaticTable.GetIndex(name);
                EncodeLiteral(@out, name, value, IndexType.NONE, nameIndex);
            }
            else
            {
                EncodeInteger(@out, 0x80, 7, staticTableIndex);
            }

            return;
        }

        // If the headerSize is greater than the max table size then it must be encoded literally
        if (headerSize > maxHeaderTableSize)
        {
            int nameIndex = GetNameIndex(name);
            EncodeLiteral(@out, name, value, IndexType.NONE, nameIndex);
            return;
        }

        int nameHash = AsciiString.HashCode(name);
        int valueHash = AsciiString.HashCode(value);
        NameValueEntry headerField = GetEntryInsensitive(name, nameHash, value, valueHash);
        if (headerField != null)
        {
            // Section 6.1. Indexed Header Field Representation
            EncodeInteger(@out, 0x80, 7, GetIndexPlusOffset(headerField.counter));
        }
        else
        {
            int staticTableIndex = HpackStaticTable.GetIndexInsensitive(name, value);
            if (staticTableIndex != HpackStaticTable.NOT_FOUND)
            {
                // Section 6.1. Indexed Header Field Representation
                EncodeInteger(@out, 0x80, 7, staticTableIndex);
            }
            else
            {
                EnsureCapacity(headerSize);
                EncodeAndAddEntries(@out, name, nameHash, value, valueHash);
                size += headerSize;
            }
        }
    }

    private void EncodeAndAddEntries(ByteBuf @out, ICharSequence name, int nameHash, ICharSequence value, int valueHash)
    {
        int staticTableIndex = HpackStaticTable.GetIndex(name);
        int nextCounter = LatestCounter() - 1;

        if (staticTableIndex == HpackStaticTable.NOT_FOUND)
        {
            NameEntry e = GetEntry(name, nameHash);
            if (e == null)
            {
                EncodeLiteral(@out, name, value, IndexType.INCREMENTAL, NOT_FOUND);
                AddNameEntry(name, nameHash, nextCounter);
                AddNameValueEntry(name, value, nameHash, valueHash, nextCounter);
            }
            else
            {
                EncodeLiteral(@out, name, value, IndexType.INCREMENTAL, GetIndexPlusOffset(e.counter));
                AddNameValueEntry(e.name, value, nameHash, valueHash, nextCounter);

                // The name entry should always point to the latest counter.
                e.counter = nextCounter;
            }
        }
        else
        {
            EncodeLiteral(@out, name, value, IndexType.INCREMENTAL, staticTableIndex);
            // use the name from the static table to optimize memory usage.
            AddNameValueEntry(
              HpackStaticTable.GetEntry(staticTableIndex).name, value, nameHash, valueHash, nextCounter);
        }
    }

    /**
     * Set the maximum table size.
     */
    public void SetMaxHeaderTableSize(long maxHeaderTableSize)
    {
        if (maxHeaderTableSize < MIN_HEADER_TABLE_SIZE || maxHeaderTableSize > MAX_HEADER_TABLE_SIZE)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Header Table Size must be >= %d and <= %d but was %d",
              MIN_HEADER_TABLE_SIZE, MAX_HEADER_TABLE_SIZE, maxHeaderTableSize);
        }

        // While the receiver may allow a larger table, it is important to cap the local impact of
        // hash collisions and memory use. This allows the default 4 KiB table when using the
        // default 64 arraySizeHint.
        maxHeaderTableSize = Math.Min(maxHeaderTableSize, nameEntries.Length * 64);
        if (this.maxHeaderTableSize == maxHeaderTableSize)
        {
            return;
        }

        this.pendingHeaderTableSizeUpdate = true;
        this.pendingMinHeaderTableSize = Math.Min(this.pendingMinHeaderTableSize, maxHeaderTableSize);
        this.maxHeaderTableSize = maxHeaderTableSize;
        EnsureCapacity(0);
    }

    /**
     * Return the maximum table size.
     */
    public long GetMaxHeaderTableSize()
    {
        return maxHeaderTableSize;
    }

    public void SetMaxHeaderListSize(long maxHeaderListSize)
    {
        if (maxHeaderListSize < MIN_HEADER_LIST_SIZE || maxHeaderListSize > MAX_HEADER_LIST_SIZE)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Header List Size must be >= %d and <= %d but was %d",
              MIN_HEADER_LIST_SIZE, MAX_HEADER_LIST_SIZE, maxHeaderListSize);
        }

        this.maxHeaderListSize = maxHeaderListSize;
    }

    public long GetMaxHeaderListSize()
    {
        return maxHeaderListSize;
    }

    /**
     * Encode integer according to <a href="https://tools.ietf.org/html/rfc7541#section-5.1">Section 5.1</a>.
     */
    private static void EncodeInteger(ByteBuf @out, int mask, int n, int i)
    {
        EncodeInteger(@out, mask, n, (long) i);
    }

    /**
     * Encode integer according to <a href="https://tools.ietf.org/html/rfc7541#section-5.1">Section 5.1</a>.
     */
    private static void EncodeInteger(ByteBuf @out, int mask, int n, long i)
    {
        Debug.Assert(n >= 0 && n <= 8, "N: " + n);
        int nbits = 0xFF >>> 8 - n;
        if (i < nbits)
        {
            @out.WriteByte((int) ((uint) mask | i));
        }
        else
        {
            @out.WriteByte(mask | nbits);
            long length = i - nbits;

            for (; (length & ~0x7F) != 0; length >>>= 7)
            {
                @out.WriteByte((int) (length & 0x7F | 0x80));
            }

            @out.WriteByte((int) length);
        }
    }

    /**
     * Encode string literal according to Section 5.2.
     */
    private void EncodeStringLiteral(ByteBuf @out, ICharSequence @string)
    {
        int huffmanLength;
        if (@string.Length() >= huffCodeThreshold
          && (huffmanLength = hpackHuffmanEncoder.GetEncodedLength(@string)) < @string.Length())
        {
            EncodeInteger(@out, 0x80, 7, huffmanLength);
            hpackHuffmanEncoder.Encode(@out, @string);
        }
        else
        {
            EncodeInteger(@out, 0x00, 7, @string.Length());

            if (@string is AsciiString)
            {
                // Fast-path
                AsciiString asciiString = (AsciiString) @string;
                @out.WriteBytes(asciiString.Array().AsSpan(asciiString.ArrayOffset(), asciiString.Length()));
            }
            else
            {
                // Only ASCII is allowed in http2 headers, so it is fine to use this.
                // https://tools.ietf.org/html/rfc7540#section-8.1.2
                ByteBufUtil.WriteAscii(@out, @string.ToString());
            }
        }
    }

    /**
     * Encode literal header field according to Section 6.2.
     */
    private void EncodeLiteral(ByteBuf @out, ICharSequence name, ICharSequence value, IndexType indexType,
                               int nameIndex)
                               {
        bool nameIndexValid = nameIndex != NOT_FOUND;

        switch (indexType)
        {
            case IndexType.INCREMENTAL:
                EncodeInteger(@out, 0x40, 6, nameIndexValid ? nameIndex : 0);
                break;
            case IndexType.NONE:
                EncodeInteger(@out, 0x00, 4, nameIndexValid ? nameIndex : 0);
                break;
            case IndexType.NEVER:
                EncodeInteger(@out, 0x10, 4, nameIndexValid ? nameIndex : 0);
                break;
            default:
                throw new UnreachableException("Unexpected index type: " + indexType);
        }

        if (!nameIndexValid)
        {
            EncodeStringLiteral(@out, name);
        }

        EncodeStringLiteral(@out, value);
    }

    private int GetNameIndex(ICharSequence name)
    {
        int index = HpackStaticTable.GetIndex(name);
        if (index != HpackStaticTable.NOT_FOUND)
        {
            return index;
        }

        NameEntry e = GetEntry(name, AsciiString.HashCode(name));
        return e == null ? NOT_FOUND : GetIndexPlusOffset(e.counter);
    }

    /**
     * Ensure that the dynamic table has enough room to hold 'headerSize' more bytes. Removes the
     * oldest entry from the dynamic table until sufficient space is available.
     */
    private void EnsureCapacity(long headerSize)
    {
        while (maxHeaderTableSize - size < headerSize)
        {
            Remove();
        }
    }

    /**
     * Return the number of header fields in the dynamic table. Exposed for testing.
     */
    internal int Length()
    {
        return IsEmpty() ? 0 : GetIndex(head.after.counter);
    }

    /**
     * Return the size of the dynamic table. Exposed for testing.
     */
    internal long Size()
    {
        return size;
    }

    /**
     * Return the header field at the given index. Exposed for testing.
     */
    internal HpackHeaderField GetHeaderField(int index)
    {
        NameValueEntry entry = head;

        while (index++ < Length())
        {
            entry = entry.after;
        }

        return entry;
    }

    /**
     * Returns the header entry with the lowest index value for the header field. Returns null if
     * header field is not in the dynamic table.
     */
    private NameValueEntry GetEntryInsensitive(ICharSequence name, int nameHash, ICharSequence value, int valueHash)
    {
        int h = Hash(nameHash, valueHash);

        for (NameValueEntry e = nameValueEntries[Bucket(h)]; e != null; e = e.next)
        {
            if (e.hash == h && EqualsVariableTime(value, e.value) && EqualsVariableTime(name, e.name))
            {
                return e;
            }
        }

        return null;
    }

    /**
     * Returns the lowest index value for the header field name in the dynamic table. Returns -1 if
     * the header field name is not in the dynamic table.
     */
    private NameEntry GetEntry(ICharSequence name, int nameHash)
    {
        for (NameEntry e = nameEntries[Bucket(nameHash)]; e != null; e = e.next)
        {
            if (e.hash == nameHash && EqualsConstantTime(name, e.name) != 0)
            {
                return e;
            }
        }

        return null;
    }

    private int GetIndexPlusOffset(int counter)
    {
        return GetIndex(counter) + HpackStaticTable.length;
    }

    /**
     * Compute the index into the dynamic table given the counter in the header entry.
     */
    private int GetIndex(int counter)
    {
        return counter - LatestCounter() + 1;
    }

    private int LatestCounter()
    {
        return latest.counter;
    }

    private void AddNameEntry(ICharSequence name, int nameHash, int nextCounter)
    {
        int bucket = Bucket(nameHash);
        nameEntries[bucket] = new NameEntry(nameHash, name, nextCounter, nameEntries[bucket]);
    }

    private void AddNameValueEntry(ICharSequence name, ICharSequence value,
                                   int nameHash, int valueHash, int nextCounter)
                                   {
        int hash = Hash(nameHash, valueHash);
        int bucket = Bucket(hash);
        NameValueEntry e = new NameValueEntry(hash, name, value, nextCounter, nameValueEntries[bucket]);
        nameValueEntries[bucket] = e;
        latest.after = e;
        latest = e;
    }

    /**
     * Remove the oldest header field from the dynamic table.
     */
    private void Remove()
    {
        NameValueEntry eldest = head.after;
        RemoveNameValueEntry(eldest);
        RemoveNameEntryMatchingCounter(eldest.name, eldest.counter);
        head.after = eldest.after;
        eldest.Unlink();
        size -= eldest.Size();

        if (IsEmpty())
        {
            latest = head;
        }
    }

    private bool IsEmpty()
    {
        return size == 0;
    }

    private void RemoveNameValueEntry(NameValueEntry eldest)
    {
        int bucket = Bucket(eldest.hash);
        NameValueEntry e = nameValueEntries[bucket];
        if (e == eldest)
        {
            nameValueEntries[bucket] = eldest.next;
        }
        else
        {
            while (e.next != eldest)
            {
                e = e.next;
            }

            e.next = eldest.next;
        }
    }

    private void RemoveNameEntryMatchingCounter(ICharSequence name, int counter)
    {
        int hash = AsciiString.HashCode(name);
        int bucket = Bucket(hash);
        NameEntry e = nameEntries[bucket];
        if (e == null)
        {
            return;
        }

        if (counter == e.counter)
        {
            nameEntries[bucket] = e.next;
            e.Unlink();
        }
        else
        {
            NameEntry prev = e;
            e = e.next;

            while (e != null)
            {
                if (counter == e.counter)
                {
                    prev.next = e.next;
                    e.Unlink();
                    break;
                }

                prev = e;
                e = e.next;
            }
        }
    }

    /**
     * Returns the bucket of the hash table for the hash code h.
     */
    private int Bucket(int h)
    {
        return h & hashMask;
    }

    private static int Hash(int nameHash, int valueHash)
    {
        return 31 * nameHash + valueHash;
    }

    private sealed class NameEntry
    {
        internal NameEntry next;

        internal readonly ICharSequence name;

        internal readonly int hash;

        // This is used to compute the index in the dynamic table.
        internal int counter;

        internal NameEntry(int hash, ICharSequence name, int counter, NameEntry next)
        {
            this.hash = hash;
            this.name = name;
            this.counter = counter;
            this.next = next;
        }

        internal void Unlink()
        {
            next = null; // null references to prevent nepotism in generational GC.
        }
    }

    private sealed class NameValueEntry : HpackHeaderField
    {
        // This field comprises the linked list used for implementing the eviction policy.
        internal NameValueEntry after;

        internal NameValueEntry next;

        // hash of both name and value
        internal readonly int hash;

        // This is used to compute the index in the dynamic table.
        internal readonly int counter;

        internal NameValueEntry(int hash, ICharSequence name, ICharSequence value, int counter, NameValueEntry next)
            : base(name, value)
        {
            this.next = next;
            this.hash = hash;
            this.counter = counter;
        }

        internal void Unlink()
        {
            after = null; // null references to prevent nepotism in generational GC.
            next = null;
        }
    }
}
