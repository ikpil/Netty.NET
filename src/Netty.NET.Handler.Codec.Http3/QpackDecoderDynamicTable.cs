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
using System.Globalization;
using static Netty.NET.Handler.Codec.Http3.QpackHeaderField;
using static Netty.NET.Handler.Codec.Http3.QpackUtil;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is internal sealed and its package-private members are internal. The checked
// QpackException needs no throws clause; IllegalArgumentException is ArgumentException and the capacity prints with the
// invariant culture. Arrays.fill(fields, null) is Array.Clear and System.arraycopy is Array.Copy. Math.floorDiv is
// QpackUtil.FloorDiv (Java's floor division).
internal sealed class QpackDecoderDynamicTable
{
    private static readonly QpackException GET_ENTRY_ILLEGAL_INDEX_VALUE =
            QpackException.NewStatic(typeof(QpackDecoderDynamicTable), "getEntry(...)",
                    "QPACK - illegal decoder dynamic table index value");
    private static readonly QpackException HEADER_TOO_LARGE =
            QpackException.NewStatic(typeof(QpackDecoderDynamicTable), "add(...)", "QPACK - header entry too large.");

    // a circular queue of header fields
    private QpackHeaderField[] fields;
    private int head;
    private int tail;
    private long size;
    private long capacity = -1; // ensure setCapacity creates the array
    private int insertCount;

    internal int Length()
    {
        return head < tail ? fields.Length - tail + head : head - tail;
    }

    internal long Size()
    {
        return size;
    }

    internal int InsertCount()
    {
        return insertCount;
    }

    internal QpackHeaderField GetEntry(int index)
    {
        if (index < 0 || fields == null || index >= fields.Length)
        {
            throw GET_ENTRY_ILLEGAL_INDEX_VALUE;
        }

        QpackHeaderField entry = fields[index];
        if (entry == null)
        {
            throw GET_ENTRY_ILLEGAL_INDEX_VALUE;
        }

        return entry;
    }

    internal QpackHeaderField GetEntryRelativeEncodedField(int index)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-relative-indexing
        return GetEntry(ModuloIndex(index));
    }

    internal QpackHeaderField GetEntryRelativeEncoderInstructions(int index)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-relative-indexing
        // Name index is the relative index, relative to the last added entry.
        return GetEntry(index > tail ? fields.Length - index + tail : tail - index);
    }

    internal void Add(QpackHeaderField header)
    {
        long headerSize = header.Size();
        if (headerSize > capacity)
        {
            throw HEADER_TOO_LARGE;
        }

        while (capacity - size < headerSize)
        {
            Remove();
        }

        insertCount++;
        fields[GetAndIncrementHead()] = header;
        size += headerSize;
    }

    private void Remove()
    {
        QpackHeaderField removed = fields[tail];
        if (removed == null)
        {
            return;
        }

        size -= removed.Size();
        fields[GetAndIncrementTail()] = null;
    }

    internal void Clear()
    {
        if (fields != null)
        {
            Array.Clear(fields);
        }

        head = 0;
        tail = 0;
        size = 0;
    }

    internal void SetCapacity(long capacity)
    {
        if (capacity < MIN_HEADER_TABLE_SIZE || capacity > MAX_HEADER_TABLE_SIZE)
        {
            throw new ArgumentException("capacity is invalid: " + capacity.ToString(CultureInfo.InvariantCulture));
        }
        // initially capacity will be -1 so init won't return here
        if (this.capacity == capacity)
        {
            return;
        }

        this.capacity = capacity;

        if (capacity == 0)
        {
            Clear();
        }
        else
        {
            // initially size will be 0 so remove won't be called
            while (size > capacity)
            {
                Remove();
            }
        }

        int maxEntries = ToIntOrThrow(2 * FloorDiv(capacity, ENTRY_OVERHEAD));

        // check if capacity change requires us to reallocate the array
        if (fields != null && fields.Length == maxEntries)
        {
            return;
        }

        QpackHeaderField[] tmp = new QpackHeaderField[maxEntries];

        // initially length will be 0 so there will be no copy
        int len = Length();

        if (fields != null && tail != head)
        {
            if (head > tail)
            {
                Array.Copy(fields, tail, tmp, 0, head - tail);
            }
            else
            {
                Array.Copy(fields, 0, tmp, 0, head);
                Array.Copy(fields, tail, tmp, head, fields.Length - tail);
            }
        }

        tail = 0;
        head = tail + len;
        fields = tmp;
    }

    private int GetAndIncrementHead()
    {
        int val = this.head;
        this.head = SafeIncrementIndex(val);
        return val;
    }

    private int GetAndIncrementTail()
    {
        int val = this.tail;
        this.tail = SafeIncrementIndex(val);
        return val;
    }

    private int SafeIncrementIndex(int index)
    {
        return ++index % fields.Length;
    }

    private int ModuloIndex(int index)
    {
        return fields == null ? index : index % fields.Length;
    }
}
