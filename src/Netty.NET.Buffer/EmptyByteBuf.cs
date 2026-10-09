/*
 * Copyright 2013 The Netty Project
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
 * An empty {@link ByteBuf} whose capacity and maximum capacity are all {@code 0}.
 */
/// <summary>The shared, permanently accessible zero-capacity buffer.</summary>
internal sealed class EmptyByteBuf : ByteBuf
{
    private readonly IByteBufAllocator _allocator;
    internal EmptyByteBuf(IByteBufAllocator allocator) : base(0)
    { ArgumentNullException.ThrowIfNull(allocator); _allocator = allocator; }
    public override IByteBufAllocator Allocator => _allocator;
    protected override Memory<byte> GetMemoryCore(int index, int length) => Memory<byte>.Empty;
    public override bool IsDirect => true;
    public override bool CanWrite(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        return false;
    }
    public override int Capacity
    {
        get => 0;
        set => throw new NotSupportedException("The empty buffer's capacity cannot change.");
    }
    public override int ReferenceCount => 1;
    // Original EmptyByteBuf ignores ownership counts, including nonpositive values.
    // There is no owned allocation or lifetime transition to mutate.
    public override ByteBuf Retain(int increment = 1) => this;
    public override bool Release(int decrement = 1) => false;
    public override ByteBuf Copy(int index, int length) { CheckIndex(index, length); return this; }
    public override ByteBuf Slice(int index, int length) { CheckIndex(index, length); return this; }
    public override ByteBuf Duplicate() => this;
    // Unlike an owned zero-capacity buffer, Netty's shared empty sentinel validates both search endpoints.
    public override int IndexOf(int fromIndex, int toIndex, byte value)
    {
        CheckIndex(fromIndex, 0);
        CheckIndex(toIndex, 0);
        return -1;
    }
}
