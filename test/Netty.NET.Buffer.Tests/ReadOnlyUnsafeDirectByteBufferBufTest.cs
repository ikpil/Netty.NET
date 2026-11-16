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
using System.Buffers;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ReadOnlyUnsafeDirectByteBufferBufTest : ReadOnlyDirectByteBufferBufTest
{
    /**
     * Needs unsafe to run
     */
    // CLR: native memory and pointers need no sun.misc.Unsafe availability probe, so there is no
    // assumeConditions step; the fixture always runs.

    // CLR: ReadOnlyUnsafeDirectByteBuf is replaced by ReadOnlyMemoryByteBuf over NativeMemoryOwner memory
    // (src/Netty.NET.Buffer/ReadOnlyMemoryByteBuf.cs); the inherited direct allocate() supplies that memory.
    protected override ByteBuf Buffer(ReadOnlyMemory<byte> buffer)
    {
        return new ReadOnlyMemoryByteBuf(UnpooledByteBufAllocator.Default, buffer);
    }

    [Fact]
    public unsafe void TestMemoryAddress()
    {
        Memory<byte> memory = Allocate(8);
        ByteBuf buf = Buffer(memory);
        try
        {
            // CLR: hasMemoryAddress()/memoryAddress() become a scoped pin of the borrowed range;
            // ReadOnlyMemory<T>.Pin "enables taking and using the memory's address"
            // (https://learn.microsoft.com/en-us/dotnet/api/system.readonlymemory-1.pin?view=net-10.0).
            using MemoryHandle address = buf.AsReadOnlyMemory(0, buf.Capacity).Pin();
            Assert.True(address.Pointer != null);
            using MemoryHandle source = memory.Pin();
            Assert.Equal((nint)source.Pointer, (nint)address.Pointer);
        }
        finally
        {
            buf.Release();
        }
    }
}
