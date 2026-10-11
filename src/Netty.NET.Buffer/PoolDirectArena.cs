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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

internal sealed class PoolDirectArena : PoolArena
{
    private readonly NativeMemoryAllocator _nativeAllocator;

    internal PoolDirectArena(IByteBufAllocator parent, SizeClasses sizeClass,
        NativeMemoryAllocator nativeAllocator = null) : base(parent, sizeClass, true)
        => _nativeAllocator = nativeAllocator ?? NativeMemoryAllocator.Shared;

    internal override PooledDirectByteBuf Allocate(int initialCapacity, int maxCapacity)
        => new(this, initialCapacity, maxCapacity);

    protected override Memory<byte> AllocateBacking(int length, out NativeMemoryOwner owner)
    {
        int alignment = SizeClass.DirectMemoryCacheAlignment;
        long reservation = (long)length + alignment;
        if (reservation > int.MaxValue) throw new OutOfMemoryException("Aligned native reservation is too large.");
        owner = _nativeAllocator.Allocate((int)reservation);
        try
        {
            return alignment == 0 ? owner.Memory : owner.GetAlignedMemory(alignment).Slice(0, length);
        }
        catch { owner.Dispose(); throw; }
    }
}
