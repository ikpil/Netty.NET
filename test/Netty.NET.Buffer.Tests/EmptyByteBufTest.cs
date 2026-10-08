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

namespace Netty.NET.Buffer.Tests;

public class EmptyByteBufTest
{
    [Fact]
    public void EmptyFactoriesAndViewsReturnTheSharedSentinel()
    {
        ByteBuf empty = Unpooled.EmptyBuffer;
        Assert.Same(empty, Unpooled.Buffer(0, 0));
        Assert.Same(empty, Unpooled.DirectBuffer(0, 0));
        Assert.Same(empty, Unpooled.WrappedBuffer(Array.Empty<byte>()));
        Assert.Same(empty, Unpooled.CopiedBuffer(ReadOnlySpan<byte>.Empty));
        Assert.Same(empty, empty.Copy());
        Assert.Same(empty, empty.Slice());
        Assert.Same(empty, empty.Duplicate());
        Assert.Same(empty, empty.RetainedDuplicate());
        Assert.Same(empty, empty.RetainedSlice());
        Assert.Same(empty, empty.ReadBytes(0));
        Assert.Same(empty, empty.ReadSlice(0));
        Assert.Same(empty, empty.ReadRetainedSlice(0));
    }

    [Fact]
    public void EmptyOwnershipIsPermanentlyAccessible()
    {
        ByteBuf empty = Unpooled.EmptyBuffer;
        Assert.Equal(1, empty.ReferenceCount);
        Assert.Same(empty, empty.Retain());
        Assert.Same(empty, empty.Retain(int.MaxValue));
        Assert.Same(empty, empty.Retain(0));
        Assert.Same(empty, empty.Retain(-1));
        Assert.False(empty.Release());
        Assert.False(empty.Release(int.MaxValue));
        Assert.False(empty.Release(0));
        Assert.False(empty.Release(-1));
        Assert.Equal(1, empty.ReferenceCount);
        Assert.Equal(0, empty.AsMemory(0, 0).Length);
    }

    [Fact]
    public void EmptyReadableWritableAndBoundsContracts()
    {
        ByteBuf empty = Unpooled.EmptyBuffer;
        Assert.Equal(0, empty.Capacity);
        Assert.Equal(0, empty.MaxCapacity);
        Assert.False(empty.IsReadable);
        Assert.False(empty.IsWritable);
        Assert.True(empty.IsDirect);
        Assert.Equal(0, empty.EnsureWritable(0, false));
        Assert.Equal(1, empty.EnsureWritable(1, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.GetByte(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.SetIndex(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.EnsureWritable(1));
        Assert.Throws<NotSupportedException>(() => empty.Capacity = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Slice(1, 0));
    }

    [Fact]
    public void ZeroLengthReadUsesSentinelWithoutAcquiringOwnership()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            Assert.Same(Unpooled.EmptyBuffer, buffer.ReadBytes(0));
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.False(Unpooled.EmptyBuffer.Release());
        }
        finally { buffer.Release(); }
    }
}
