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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ReadOnlyByteBufferBufTest : ReadOnlyDirectByteBufferBufTest
{
    // CLR: ByteBuffer.allocate is a zero-filled managed array.
    protected override Memory<byte> Allocate(int size)
    {
        return new byte[size];
    }

    [Fact]
    public void TestCopyDirect()
    {
        TestCopy0(true);
    }

    [Fact]
    public void TestCopyHeap()
    {
        TestCopy0(false);
    }

    // CLR: xUnit1024 forbids overloading the inherited TestCopy test, so the private testCopy(boolean) helper is TestCopy0.
    private static void TestCopy0(bool direct)
    {
        byte[] bytes = new byte[1024];
        Random.Shared.NextBytes(bytes);

        // CLR: owned native memory or a managed array replaces allocateDirect/allocate, and the
        // ReadOnlyMemory<byte> constructor parameter replaces asReadOnlyBuffer().
        var native = new NativeMemoryAllocator();
        using NativeMemoryOwner owner = direct ? native.Allocate(bytes.Length) : null;
        Memory<byte> nioBuffer = direct ? owner.Memory : new byte[bytes.Length];
        bytes.CopyTo(nioBuffer.Span);

        ByteBuf buf = new ReadOnlyMemoryByteBuf(UnpooledByteBufAllocator.Default, nioBuffer);
        ByteBuf copy = buf.Copy();

        Assert.Equal(buf, copy);
        Assert.Equal(buf.Alloc(), copy.Alloc());
        Assert.Equal(buf.IsDirect, copy.IsDirect);
        // CLR: the storage policy is explicit; the copy is writable.
        Assert.Equal(direct, copy.IsDirect);
        Assert.False(copy.IsReadOnly);

        copy.Release();
        buf.Release();
    }
}
