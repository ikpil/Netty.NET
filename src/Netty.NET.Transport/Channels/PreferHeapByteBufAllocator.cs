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
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels;

/**
 * Wraps another {@link ByteBufAllocator} and use heapbuffers everywhere except when a direct buffer is explicit
 * requested.
 */
public sealed class PreferHeapByteBufAllocator : IByteBufAllocator
{
    private readonly IByteBufAllocator _allocator;
    public PreferHeapByteBufAllocator(IByteBufAllocator allocator) => _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
    public ByteBuf Buffer(int initialCapacity = 256, int maxCapacity = int.MaxValue) => _allocator.HeapBuffer(initialCapacity, maxCapacity);
    public ByteBuf IoBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue) => _allocator.HeapBuffer(initialCapacity, maxCapacity);
    public ByteBuf HeapBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue) => _allocator.HeapBuffer(initialCapacity, maxCapacity);
    public ByteBuf DirectBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue) => _allocator.DirectBuffer(initialCapacity, maxCapacity);
    public CompositeByteBuf CompositeBuffer(int maxNumComponents = 16) => _allocator.CompositeHeapBuffer(maxNumComponents);
    public CompositeByteBuf CompositeHeapBuffer(int maxNumComponents = 16) => _allocator.CompositeHeapBuffer(maxNumComponents);
    public CompositeByteBuf CompositeDirectBuffer(int maxNumComponents = 16) => _allocator.CompositeDirectBuffer(maxNumComponents);
    public bool IsDirectBufferPooled => _allocator.IsDirectBufferPooled;
    public int CalculateNewCapacity(int minimumNewCapacity, int maxCapacity) => _allocator.CalculateNewCapacity(minimumNewCapacity, maxCapacity);
}
