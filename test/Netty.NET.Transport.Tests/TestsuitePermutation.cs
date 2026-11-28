/*
 * Copyright 2014 The Netty Project
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
using System.Collections.Generic;
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Tests;

// CLR: the source's final class with a private constructor is a static class.
public static class TestsuitePermutation
{
    private static readonly AdaptiveByteBufAllocator DEFAULT_ADAPTIVE_ALLOCATOR = new AdaptiveByteBufAllocator();

    public static List<IByteBufAllocator> Allocator()
    {
        List<IByteBufAllocator> allocators = new List<IByteBufAllocator>();
        allocators.Add(PooledByteBufAllocator.Default);
        allocators.Add(DEFAULT_ADAPTIVE_ALLOCATOR);
        return allocators;
    }

    public interface IBootstrapFactory<CB> where CB : class
    {
        CB NewInstance();
    }

    public interface IBootstrapComboFactory<SB, CB> where SB : class where CB : class
    {
        SB NewServerInstance();

        CB NewClientInstance();
    }

    public static ByteBuf RandomBufferType(IByteBufAllocator allocator, byte[] data, int offset, int length)
    {
        // CLR: Random.Shared ("a thread-safe Random instance that may be used concurrently from any thread",
        // https://learn.microsoft.com/en-us/dotnet/api/system.random.shared) replaces
        // ThreadLocalRandom.current(); Next(2) == 1 is nextBoolean(). This port's ByteBuf writes a byte range from a
        // ReadOnlySpan<byte> instead of writeBytes(byte[], int, int); the span slice keeps the source bounds check.
        if (Random.Shared.Next(2) == 1)
        {
            return allocator.DirectBuffer().WriteBytes(data.AsSpan(offset, length));
        }

        return Unpooled.WrappedBuffer(data, offset, length);
    }
}
