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
using System.Globalization;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: IndexOutOfBoundsException is ArgumentOutOfRangeException and IllegalArgumentException is ArgumentException;
// numbers in messages use the invariant culture.
internal sealed class HpackDynamicTable
{

    // a circular queue of header fields
    internal HpackHeaderField[] hpackHeaderFields;
    internal int head;
    internal int tail;
    private long size;
    private long capacity = -1; // ensure setCapacity creates the array

    /**
     * Creates a new dynamic table with the specified initial capacity.
     */
    internal HpackDynamicTable(long initialCapacity)
    {
        SetCapacity(initialCapacity);
    }

    /**
     * Return the number of header fields in the dynamic table.
     */
    public int Length()
    {
        int length;

        if (head < tail)
        {
            length = hpackHeaderFields.Length - tail + head;
        }
        else
        {
            length = head - tail;
        }

        return length;
    }

    /**
     * Return the current size of the dynamic table. This is the sum of the size of the entries.
     */
    public long Size()
    {
        return size;
    }

    /**
     * Return the maximum allowable size of the dynamic table.
     */
    public long Capacity()
    {
        return capacity;
    }

    /**
     * Return the header field at the given index. The first and newest entry is always at index 1,
     * and the oldest entry is at the index length().
     */
    public HpackHeaderField GetEntry(int index)
    {
        if (index <= 0 || index > Length())
        {
            throw new ArgumentOutOfRangeException(null, "Index " + index.ToString(CultureInfo.InvariantCulture) +
                " out of bounds for length " + Length().ToString(CultureInfo.InvariantCulture));
        }

        int i = head - index;
        if (i < 0)
        {
            return hpackHeaderFields[i + hpackHeaderFields.Length];
        }
        else
        {
            return hpackHeaderFields[i];
        }
    }

    /**
     * Add the header field to the dynamic table. Entries are evicted from the dynamic table until
     * the size of the table and the new header field is less than or equal to the table's capacity.
     * If the size of the new entry is larger than the table's capacity, the dynamic table will be
     * cleared.
     */
    public void Add(HpackHeaderField header)
    {
        int headerSize = header.Size();
        if (headerSize > capacity)
        {
            Clear();
            return;
        }

        while (capacity - size < headerSize)
        {
            Remove();
        }

        hpackHeaderFields[head++] = header;
        size += headerSize;

        if (head == hpackHeaderFields.Length)
        {
            head = 0;
        }
    }

    /**
     * Remove and return the oldest header field from the dynamic table.
     */
    public HpackHeaderField Remove()
    {
        HpackHeaderField removed = hpackHeaderFields[tail];
        if (removed == null)
        {
            return null;
        }

        size -= removed.Size();
        hpackHeaderFields[tail++] = null;

        if (tail == hpackHeaderFields.Length)
        {
            tail = 0;
        }

        return removed;
    }

    /**
     * Remove all entries from the dynamic table.
     */
    public void Clear()
    {
        while (tail != head)
        {
            hpackHeaderFields[tail++] = null;

            if (tail == hpackHeaderFields.Length)
            {
                tail = 0;
            }
        }

        head = 0;
        tail = 0;
        size = 0;
    }

    /**
     * Set the maximum size of the dynamic table. Entries are evicted from the dynamic table until
     * the size of the table is less than or equal to the maximum size.
     */
    public void SetCapacity(long capacity)
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

        int maxEntries = (int) (capacity / HpackHeaderField.HEADER_ENTRY_OVERHEAD);

        if (capacity % HpackHeaderField.HEADER_ENTRY_OVERHEAD != 0)
        {
            maxEntries++;
        }

        // check if capacity change requires us to reallocate the array
        if (hpackHeaderFields != null && hpackHeaderFields.Length == maxEntries)
        {
            return;
        }

        HpackHeaderField[] tmp = new HpackHeaderField[maxEntries];

        // initially length will be 0 so there will be no copy
        int len = Length();

        if (hpackHeaderFields != null)
        {
            int cursor = tail;

            for (int i = 0; i < len; i++)
            {
                HpackHeaderField entry = hpackHeaderFields[cursor++];
                tmp[i] = entry;

                if (cursor == hpackHeaderFields.Length)
                {
                    cursor = 0;
                }
            }
        }

        tail = 0;
        head = tail + len;
        hpackHeaderFields = tmp;
    }
}
