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

internal sealed class PoolHeapArena : PoolArena
{
    internal PoolHeapArena(IByteBufAllocator parent, SizeClasses sizeClass) : base(parent, sizeClass, false) { }

    internal override PooledHeapByteBuf Allocate(int initialCapacity, int maxCapacity, PoolThreadCache cache = null)
        => new(this, initialCapacity, maxCapacity, cache);

    protected override Memory<byte> AllocateBacking(int length, out NativeMemoryOwner owner)
    {
        owner = null;
        return GC.AllocateUninitializedArray<byte>(length);
    }
}
