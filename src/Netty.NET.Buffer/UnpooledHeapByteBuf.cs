/*
 * Copyright 2012 The Netty Project
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

namespace Netty.NET.Buffer;

/**
 * Big endian Java heap buffer implementation. It is recommended to use
 * {@link UnpooledByteBufAllocator#heapBuffer(int, int)}, {@link Unpooled#buffer(int)} and
 * {@link Unpooled#wrappedBuffer(byte[])} instead of calling the constructor explicitly.
 */
// CLR: this stage uses a direct heap constructor and Memory<byte> rather than
// Java allocator/provider and ByteBuffer hierarchies. Allocator policy is later work.
public class UnpooledHeapByteBuf : AbstractReferenceCountedByteBuf
{
    private byte[] _array;
/**
     * Creates a new heap buffer with a newly allocated byte array.
     *
     * @param initialCapacity the initial capacity of the underlying byte array
     * @param maxCapacity the max capacity of the underlying byte array
     */
    public UnpooledHeapByteBuf(int initialCapacity = 256, int maxCapacity = int.MaxValue) : base(maxCapacity)
    {
        if (initialCapacity < 0 || initialCapacity > maxCapacity)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _array = new byte[initialCapacity];
    }
/**
     * Creates a new heap buffer with an existing byte array.
     *
     * @param initialArray the initial underlying byte array
     * @param maxCapacity the max capacity of the underlying byte array
     */
    internal UnpooledHeapByteBuf(byte[] initialArray) : base(GetLength(initialArray))
    {
        _array = initialArray;
        SetIndex(0, initialArray.Length);
    }
    private static int GetLength(byte[] array)
    { ArgumentNullException.ThrowIfNull(array); return array.Length; }
    protected override Memory<byte> GetMemoryCore(int index, int length) => _array.AsMemory(index, length);
    public override bool IsDirect => false;
    public override int Capacity
    {
        get => _array.Length;
        set
        {
            CheckNewCapacity(value);
            if (value == _array.Length) return;
            byte[] replacement = new byte[value];
            _array.AsSpan(0, Math.Min(value, _array.Length)).CopyTo(replacement);
            // Publish only after successful allocation/copy; failure retains old data and indices.
            _array = replacement;
            TrimIndicesToCapacity(value);
        }
    }
    protected override void Deallocate()
    {
        // NOOP
        // CLR GC owns the array; final release drops this buffer's storage reference.
        _array = Array.Empty<byte>();
    }
}
