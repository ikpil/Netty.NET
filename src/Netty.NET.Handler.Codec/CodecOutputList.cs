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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec;

/**
 * Special {@link AbstractList} implementation which is used within our codec base classes.
 */
// CLR: java.util.List<Object> is IList<object> (Java collection interfaces map to the BCL interfaces). get/set are the
// indexer, size() is Count, add(int, Object) is Insert, remove(int) is RemoveAt (which returns nothing), and the
// RandomAccess marker has no counterpart. The members AbstractList derives from get/size (indexOf, contains, toArray,
// iterator, remove(Object)) are implemented over the backing array.
internal sealed class CodecOutputList : IList<object>
{
    private static readonly ICodecOutputListRecycler NOOP_RECYCLER = new NoopRecycler();

    // CLR: the anonymous recycler is a named nested class.
    private sealed class NoopRecycler : ICodecOutputListRecycler
    {
        public void Recycle(CodecOutputList @object)
        {
            // drop on the floor and let the GC handle it.
        }
    }

    private static readonly FastThreadLocal<CodecOutputLists> CODEC_OUTPUT_LISTS_POOL = new CodecOutputListsPool();

    // CLR: the anonymous FastThreadLocal is a named nested subclass.
    private sealed class CodecOutputListsPool : FastThreadLocal<CodecOutputLists>
    {
        protected override CodecOutputLists InitialValue()
        {
            // 16 CodecOutputList per Thread are cached.
            return new CodecOutputLists(16);
        }
    }

    private interface ICodecOutputListRecycler
    {
        void Recycle(CodecOutputList codecOutputList);
    }

    private sealed class CodecOutputLists : ICodecOutputListRecycler
    {
        private readonly CodecOutputList[] elements;
        private readonly int mask;

        private int currentIdx;
        private int count;

        internal CodecOutputLists(int numElements)
        {
            elements = new CodecOutputList[MathUtil.SafeFindNextPositivePowerOfTwo(numElements)];
            for (int i = 0; i < elements.Length; ++i)
            {
                // Size of 16 should be good enough for the majority of all users as an initial capacity.
                elements[i] = new CodecOutputList(this, 16);
            }
            count = elements.Length;
            currentIdx = elements.Length;
            mask = elements.Length - 1;
        }

        public CodecOutputList GetOrCreate()
        {
            if (count == 0)
            {
                // Return a new CodecOutputList which will not be cached. We use a size of 4 to keep the overhead
                // low.
                return new CodecOutputList(NOOP_RECYCLER, 4);
            }
            --count;

            int idx = (currentIdx - 1) & mask;
            CodecOutputList list = elements[idx];
            currentIdx = idx;
            return list;
        }

        public void Recycle(CodecOutputList codecOutputList)
        {
            int idx = currentIdx;
            elements[idx] = codecOutputList;
            currentIdx = (idx + 1) & mask;
            ++count;
            Debug.Assert(count <= elements.Length);
        }
    }

    internal static CodecOutputList NewInstance()
    {
        return CODEC_OUTPUT_LISTS_POOL.Get().GetOrCreate();
    }

    private readonly ICodecOutputListRecycler recycler;
    private int size;
    private int maxSeenSize;
    private object[] array;
    private bool insertSinceRecycled;

    private CodecOutputList(ICodecOutputListRecycler recycler, int size)
    {
        this.recycler = recycler;
        array = new object[size];
    }

    public object this[int index]
    {
        get
        {
            CheckIndex(index);
            return array[index];
        }
        set
        {
            CheckNotNull(value, "element");
            CheckIndex(index);

            Insert0(index, value);
        }
    }

    public int Count => size;

    public void Add(object element)
    {
        CheckNotNull(element, "element");
        try
        {
            Insert0(size, element);
        }
        // CLR: an array store past the end throws IndexOutOfRangeException (the runtime array check).
        catch (IndexOutOfRangeException)
        {
            // This should happen very infrequently so we just catch the exception and try again.
            ExpandArray();
            Insert0(size, element);
        }
        ++ size;
    }

    public void Insert(int index, object element)
    {
        CheckNotNull(element, "element");
        CheckIndex(index);

        if (size == array.Length)
        {
            ExpandArray();
        }

        if (index != size)
        {
            Array.Copy(array, index, array, index + 1, size - index);
        }

        Insert0(index, element);
        ++ size;
    }

    public void RemoveAt(int index)
    {
        CheckIndex(index);

        int len = size - index - 1;
        if (len > 0)
        {
            Array.Copy(array, index + 1, array, index, len);
        }
        array[-- size] = null;
    }

    public void Clear()
    {
        // We only set the size to 0 and not null out the array. Null out the array will explicit requested by
        // calling recycle()
        maxSeenSize = Math.Max(maxSeenSize, size);
        size = 0;
    }

    public bool IsReadOnly => false;

    public int IndexOf(object item)
    {
        for (int i = 0; i < size; i++)
        {
            if (Equals(item, array[i]))
            {
                return i;
            }
        }
        return -1;
    }

    public bool Contains(object item) => IndexOf(item) >= 0;

    public void CopyTo(object[] destination, int arrayIndex) => Array.Copy(array, 0, destination, arrayIndex, size);

    public bool Remove(object item)
    {
        int index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }
        RemoveAt(index);
        return true;
    }

    public IEnumerator<object> GetEnumerator()
    {
        for (int i = 0; i < size; i++)
        {
            yield return array[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /**
     * Returns {@code true} if any elements where added or set. This will be reset once {@link #recycle()} was called.
     */
    internal bool InsertSinceRecycled()
    {
        return insertSinceRecycled;
    }

    /**
     * Recycle the array which will clear it and null out all entries in the internal storage.
     */
    internal void Recycle()
    {
        int len = Math.Max(maxSeenSize, size);
        for (int i = 0; i < len; i ++)
        {
            array[i] = null;
        }
        size = 0;
        maxSeenSize = 0;
        insertSinceRecycled = false;

        recycler.Recycle(this);
    }

    /**
     * Returns the element on the given index. This operation will not do any range-checks and so is considered unsafe.
     */
    internal object GetUnsafe(int index)
    {
        return array[index];
    }

    // CLR: IndexOutOfBoundsException is ArgumentOutOfRangeException, with the source text as the message.
    private void CheckIndex(int index)
    {
        if (index >= size)
        {
            throw new ArgumentOutOfRangeException(null, "expected: index < ("
                    + size.ToString(CultureInfo.InvariantCulture) + "),but actual is ("
                    + size.ToString(CultureInfo.InvariantCulture) + ")");
        }
    }

    // CLR: the private insert(int, Object) is Insert0 because IList<T>.Insert(int, T) is the public add(int, Object).
    private void Insert0(int index, object element)
    {
        array[index] = element;
        insertSinceRecycled = true;
    }

    private void ExpandArray()
    {
        // double capacity
        int newCapacity = array.Length << 1;

        if (newCapacity < 0)
        {
            // CLR: OutOfMemoryError is OutOfMemoryException.
            throw new OutOfMemoryException();
        }

        object[] newArray = new object[newCapacity];
        Array.Copy(array, 0, newArray, 0, array.Length);

        array = newArray;
    }
}
