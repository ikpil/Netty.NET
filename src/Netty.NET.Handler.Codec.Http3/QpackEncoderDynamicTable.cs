/*
 * Copyright 2021 The Netty Project
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
using System.Diagnostics;
using System.Globalization;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http3.QpackHeaderField;
using static Netty.NET.Handler.Codec.Http3.QpackUtil;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is internal sealed and its package-private members are internal; the public
// constant NOT_FOUND stays public. The checked QpackException needs no throws clause; IllegalArgumentException is
// ArgumentException and the index prints with the invariant culture; assert is Debug.Assert. AsciiString.hashCode
// (CharSequence) is the Common AsciiString.HashCode, which keeps Java's hashing. MathUtil.findNextPositivePowerOfTwo,
// Math.max and Math.min are MathUtil.FindNextPositivePowerOfTwo, Math.Max and Math.Min; Math.toIntExact is a checked
// conversion (OverflowException for Java's ArithmeticException). hashMask is the CLR byte: it is at most 127, so Java's
// sign extension in h & hashMask does not change the value. The @Nullable (org.jetbrains.annotations) annotations have
// no counterpart. The private static nested class HeaderEntry derives from the ported QpackHeaderField; its fields are
// internal so the table reads them.
internal sealed class QpackEncoderDynamicTable
{
    private static readonly QpackException INVALID_KNOW_RECEIVED_COUNT_INCREMENT =
            QpackException.NewStatic(typeof(QpackDecoder), "incrementKnownReceivedCount(...)",
                    "QPACK - invalid known received count increment.");
    private static readonly QpackException INVALID_REQUIRED_INSERT_COUNT_INCREMENT =
            QpackException.NewStatic(typeof(QpackDecoder), "acknowledgeInsertCount(...)",
                    "QPACK - invalid required insert count acknowledgment.");
    private static readonly QpackException INVALID_TABLE_CAPACITY =
            QpackException.NewStatic(typeof(QpackDecoder), "validateCapacity(...)",
                    "QPACK - dynamic table capacity is invalid.");
    private static readonly QpackException CAPACITY_ALREADY_SET =
            QpackException.NewStatic(typeof(QpackDecoder), "maxTableCapacity(...)",
                    "QPACK - dynamic table capacity is already set.");
    /**
     * Special return value of {@link #getEntryIndex(CharSequence, CharSequence)} when the entry is not found.
     */
    public const int NOT_FOUND = int.MinValue;

    /**
     * A hashmap of header entries.
     */
    private readonly HeaderEntry[] fields;

    /**
     * Percentage of capacity that we expect to be free after eviction of old entries.
     */
    private readonly int expectedFreeCapacityPercentage;

    /**
     * Hash mask for all entries in the hashmap.
     */
    private readonly byte hashMask;

    /**
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-dynamic-table-size">Current size of the table</a>.
     */
    private long size;

    /**
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-maximum-dynamic-table-capac">
     *     Maximum capacity of the table</a>. This is set once based on the
     *     {@link Http3SettingIdentifier#HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY} received by the remote peer.
     */
    private long maxTableCapacity = -1;

    /*
     * The below indexes follow the suggested heuristics in Section 2.1.1.1 Avoiding Prohibited insertions
     * https://www.rfc-editor.org/rfc/rfc9204.html#name-avoiding-prohibited-inserti
     *
     *                Tail                             Drain       Head
     *                 |                                |           |
     *                 v                                v           v
     *       +--------+---------------------------------+----------+
     *       | Unused |          Referenceable          | Draining |
     *       | Space  |             Entries             | Entries  |
     *       +--------+---------------------------------+----------+
     *                ^                                 ^          ^
     *                |                                 |          |
     *          Insertion Index                 Draining Index  Dropping Index
     */

    /**
     * Head of the entries, such that {@link HeaderEntry#index} is the {@code droppingIndex}.
     */
    private readonly HeaderEntry head;

    /**
     * Pointer before which entries are marked for eviction post {@link #incrementKnownReceivedCount(int)}.
     * {@link HeaderEntry#index} is the {@code drainingIndex}.
     */
    private HeaderEntry drain;

     /**
     * Pointer to the entry representing the <a
     * href="https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#name-known-received-count">
     * known received count</a>.
     */
    private HeaderEntry knownReceived;

    /**
     * Tail of the entries, such that {@link HeaderEntry#index} is the {@code insertionIndex}.
     */
    private HeaderEntry tail;

    internal QpackEncoderDynamicTable() : this(16, 10)
    {
    }

    internal QpackEncoderDynamicTable(int arraySizeHint, int expectedFreeCapacityPercentage)
    {
        // Enforce a bound of [2, 128] because hashMask is a byte. The max possible value of hashMask is one less
        // than the length of this array, and we want the mask to be > 0.
        fields = new HeaderEntry[MathUtil.FindNextPositivePowerOfTwo(Math.Max(2, Math.Min(arraySizeHint, 128)))];
        hashMask = (byte) (fields.Length - 1);
        // Start with index -1 so the first added header will have the index of 0.
        // See https://www.rfc-editor.org/rfc/rfc9204.html#name-absolute-indexing
        head = new HeaderEntry(-1, AsciiString.EMPTY_STRING, AsciiString.EMPTY_STRING, -1, null);
        this.expectedFreeCapacityPercentage = expectedFreeCapacityPercentage;
        ResetIndicesToHead();
    }

    /**
     * Add a name - value pair to the dynamic table and returns the index.
     *
     * @param name          the name.
     * @param value         the value.
     * @param headerSize    the size of the header.
     * @return              the absolute index or {@code -1) if it could not be added.
     */
    internal int Add(ICharSequence name, ICharSequence value, long headerSize)
    {
        if (maxTableCapacity - size < headerSize)
        {
            return -1;
        }

        if (tail.index == int.MaxValue)
        {
            // Wait for all entries to evict before we restart indexing from zero
            EvictUnreferencedEntries();
            return -1;
        }

        int h = AsciiString.HashCode(name);
        int i = Index(h);
        HeaderEntry old = fields[i];
        HeaderEntry e = new HeaderEntry(h, name, value, tail.index + 1, old);
        fields[i] = e;
        e.AddNextTo(tail);
        tail = e;
        size += headerSize;

        EnsureFreeCapacity();
        return e.index;
    }

    /**
     * Callback when a header block which had a {@link #insertCount()}} greater than {@code 0} is
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-section-acknowledgment">acknowledged</a>
     * by the decoder.
     *
     * @param entryIndex For the entry corresponding to the {@link #insertCount()}.
     * @throws QpackException If the count is invalid.
     */
    internal void AcknowledgeInsertCountOnAck(int entryIndex)
    {
        AcknowledgeInsertCount(entryIndex, true);
    }

    /**
     * Callback when a header block which had a {@link #insertCount()}} greater than {@code 0} is still not processed
     * and the stream is <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-stream-cancellation">cancelled</a>
     * by the decoder.
     *
     * @param entryIndex For the entry corresponding to the {@link #insertCount()}.
     * @throws QpackException If the count is invalid.
     */
    internal void AcknowledgeInsertCountOnCancellation(int entryIndex)
    {
        AcknowledgeInsertCount(entryIndex, false);
    }

    private void AcknowledgeInsertCount(int entryIndex, bool updateKnownReceived)
    {
        if (entryIndex < 0)
        {
            throw INVALID_REQUIRED_INSERT_COUNT_INCREMENT;
        }

        for (HeaderEntry e = head.next; e != null; e = e.next)
        {
            if (e.index == entryIndex)
            {
                Debug.Assert(e.refCount > 0);
                e.refCount--;

                if (updateKnownReceived && e.index > knownReceived.index)
                {
                    // https://www.rfc-editor.org/rfc/rfc9204.html#name-known-received-count
                    // If the Required Insert Count of the acknowledged field section is greater than the current Known
                    // Received Count, Known Received Count is updated to that Required Insert Count value.
                    knownReceived = e;
                }

                EvictUnreferencedEntries();
                return;
            }
        }

        // We have reached the end of the linked list so the index was invalid and hence the connection should
        // be closed.
        // https://www.rfc-editor.org/rfc/rfc9204.html#section-4.4
        throw INVALID_REQUIRED_INSERT_COUNT_INCREMENT;
    }

    /**
     * Callback when a decoder <a
     * href="https://www.rfc-editor.org/rfc/rfc9204.html#name-insert-count-increment">increments its
     * insert count.</a>
     *
     * @param knownReceivedCountIncr Increment count.
     * @throws QpackException If the increment count is invalid.
     */
    internal void IncrementKnownReceivedCount(int knownReceivedCountIncr)
    {
        if (knownReceivedCountIncr <= 0)
        {
            throw INVALID_KNOW_RECEIVED_COUNT_INCREMENT;
        }

        while (knownReceived.next != null && knownReceivedCountIncr > 0)
        {
            knownReceived = knownReceived.next;
            knownReceivedCountIncr--;
        }

        if (knownReceivedCountIncr == 0)
        {
            EvictUnreferencedEntries();
            return;
        }

        // We have reached the end of the linked list so the index was invalid and hence the connection should be
        // closed.
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-decoder-instructions
        throw INVALID_KNOW_RECEIVED_COUNT_INCREMENT;
    }

    /**
     * Returns the number of entries inserted to this dynamic table.
     *
     * @return number the added entries.
     */
    internal int InsertCount()
    {
        return tail.index + 1;
    }

    /**
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-required-insert-count">
     *     Encodes the required insert count.</a>
     * @param reqInsertCount    the required insert count.
     * @return                  the encoded count.
     */
    internal int EncodedRequiredInsertCount(int reqInsertCount)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-required-insert-count
        // if ReqInsertCount == 0:
        //      EncInsertCount = 0
        // else:
        //      EncInsertCount = (ReqInsertCount mod (2 * MaxEntries)) + 1
        //
        return reqInsertCount == 0 ? 0 :
                reqInsertCount % checked((int) (2 * QpackUtil.MaxEntries(maxTableCapacity))) + 1;
    }

    // Visible for tests
    internal int EncodedKnownReceivedCount()
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-known-received-count
        return EncodedRequiredInsertCount(knownReceived.index + 1);
    }

    /**
     * Set the maximum capacity of the dynamic table. This can only be set once.
     * @param capacity          the capacity
     * @throws QpackException   if capacity was set before.
     */
    internal void MaxTableCapacity(long capacity)
    {
        ValidateCapacity(capacity);
        if (this.maxTableCapacity >= 0)
        {
            throw CAPACITY_ALREADY_SET;
        }

        this.maxTableCapacity = capacity;
    }

    /**
     * Transforms the passed {@code entryIndex} as a <a
     * href="https://www.rfc-editor.org/rfc/rfc9204.html#name-relative-indexing">relative index for
     * encoder instructions</a>.
     *
     * @param entryIndex to transform.
     * @return Relative index for the passed {@code entryIndex}.
     */
    internal int RelativeIndexForEncoderInstructions(int entryIndex)
    {
        Debug.Assert(entryIndex >= 0);
        Debug.Assert(entryIndex <= tail.index);
        return tail.index - entryIndex;
    }

    /**
     * Finds an entry with the passed {@code name} and {@code value} in this dynamic table.
     *
     * @param name of the entry to find.
     * @param value of the entry to find.
     * @return {@link #NOT_FOUND} if the entry does not exist. If an entry with matching {@code name} and {@code value}
     * exists, then the index is returned. If an entry with only matching name exists then {@code -index-1} is
     * returned.
     */
    internal int GetEntryIndex(ICharSequence name, ICharSequence value)
    {
        if (tail != head && name != null && value != null)
        {
            int h = AsciiString.HashCode(name);
            int i = Index(h);
            HeaderEntry firstNameMatch = null;
            HeaderEntry entry = null;

            for (HeaderEntry e = fields[i]; e != null; e = e.nextSibling)
            {
                if (e.hash == h && EqualsVariableTime(value, e.value))
                {
                    if (EqualsVariableTime(name, e.name))
                    {
                        entry = e;
                        break;
                    }
                }
                else if (firstNameMatch == null && EqualsVariableTime(name, e.name))
                {
                    firstNameMatch = e;
                }
            }

            if (entry != null)
            {
                return entry.index;
            }
            if (firstNameMatch != null)
            {
                return -firstNameMatch.index - 1;
            }
        }

        return NOT_FOUND;
    }

    /**
     * Adds a reference to an entry at the passed {@code idx}.
     *
     * @param name of the entry for lookups, not verified for the entry at the pased {@code idx}
     * @param value of the entry for lookups, not verified for the entry at the pased {@code idx}
     * @param idx of the entry.
     * @return <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-required-insert-count">Required
     * insert count</a> if the passed entry has to be referenced in a header block.
     */
    internal int AddReferenceToEntry(ICharSequence name, ICharSequence value, int idx)
    {
        if (tail != head && name != null && value != null)
        {
            int h = AsciiString.HashCode(name);
            int i = Index(h);

            for (HeaderEntry e = fields[i]; e != null; e = e.nextSibling)
            {
                if (e.hash == h && idx == e.index)
                {
                    e.refCount++;
                    return e.index + 1;
                }
            }
        }

        throw new ArgumentException("Index " + idx.ToString(CultureInfo.InvariantCulture) + " not found");
    }

    internal bool RequiresDuplication(int idx, long size)
    {
        Debug.Assert(head != tail);

        if (this.size + size > maxTableCapacity || head == drain)
        {
            return false;
        }

        return idx >= head.next.index && idx <= drain.index;
    }

    private void EvictUnreferencedEntries()
    {
        if (head == knownReceived || head == drain)
        {
            return;
        }

        while (head.next != null && head.next != knownReceived.next && head.next != drain.next)
        {
            if (!RemoveIfUnreferenced())
            {
                return;
            }
        }
    }

    private bool RemoveIfUnreferenced()
    {
        HeaderEntry toRemove = head.next;
        if (toRemove.refCount != 0)
        {
            return false;
        }

        size -= toRemove.Size();

        // Remove from the hash map
        int i = Index(toRemove.hash);
        HeaderEntry e = fields[i];
        HeaderEntry prev = null;

        while (e != null && e != toRemove)
        {
            prev = e;
            e = e.nextSibling;
        }

        if (e == toRemove)
        {
            if (prev == null)
            {
                fields[i] = e.nextSibling;
            }
            else
            {
                prev.nextSibling = e.nextSibling;
            }
        }

        // Remove from the linked list
        toRemove.Remove(head);

        if (toRemove == tail)
        {
            ResetIndicesToHead();
        }

        if (toRemove == drain)
        {
            drain = head;
        }

        if (toRemove == knownReceived)
        {
            knownReceived = head;
        }

        return true;
    }

    private void ResetIndicesToHead()
    {
        tail = head;
        drain = head;
        knownReceived = head;
    }

    private void EnsureFreeCapacity()
    {
        long maxDesiredSize = Math.Max(ENTRY_OVERHEAD, ((100 - expectedFreeCapacityPercentage) * maxTableCapacity) / 100);
        long cSize = size;
        HeaderEntry nDrain;

        for (nDrain = head; nDrain.next != null && cSize > maxDesiredSize; nDrain = nDrain.next)
        {
            cSize -= nDrain.next.Size();
        }

        if (cSize != size)
        {
            drain = nDrain;
            EvictUnreferencedEntries();
        }
    }

    private int Index(int h)
    {
        return h & hashMask;
    }

    private static void ValidateCapacity(long capacity)
    {
        if (capacity < MIN_HEADER_TABLE_SIZE || capacity > MAX_HEADER_TABLE_SIZE)
        {
            throw INVALID_TABLE_CAPACITY;
        }
    }

    /**
     * An entry for the {@link #fields} HashMap. This entry provides insertion order iteration using {@link #next}.
     */
    private sealed class HeaderEntry : QpackHeaderField
    {
        /**
         * Pointer to the next entry in insertion order with a different {@link #hash} than this entry.
         */
        internal HeaderEntry next;

        /**
         * Pointer to the next entry in insertion order with the same {@link #hash} as this entry, a.k.a hash collisions
         */
        internal HeaderEntry nextSibling;

        /**
         * Number of header blocks that refer to this entry as the value for its <a
         * href="https://www.rfc-editor.org/rfc/rfc9204.html#name-required-insert-count">
         * required insert count</a>
         */
        internal int refCount;

        /**
         * Hashcode for this entry.
         */
        internal readonly int hash;

        /**
         * Insertion index for this entry.
         */
        internal readonly int index;

        internal HeaderEntry(int hash, ICharSequence name, ICharSequence value, int index, HeaderEntry nextSibling)
            : base(name, value)
        {
            this.index = index;
            this.hash = hash;
            this.nextSibling = nextSibling;
        }

        internal void Remove(HeaderEntry prev)
        {
            Debug.Assert(prev != this);
            prev.next = next;
            next = null; // null references to prevent nepotism in generational GC.
            nextSibling = null;
        }

        internal void AddNextTo(HeaderEntry prev)
        {
            Debug.Assert(prev != this);
            this.next = prev.next;
            prev.next = this;
        }
    }
}
