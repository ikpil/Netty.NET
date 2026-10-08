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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Original AbstractReferenceCountedByteBufTest's six scenarios, plus CLR ownership races.
public class AbstractReferenceCountedByteBufTest
{
    [Fact]
    public void TestRetainOverflow()
    {
        var buffer = new CountingBuffer();
        buffer.SetCount(int.MaxValue);
        Assert.Equal(int.MaxValue, buffer.ReferenceCount);
        Assert.Throws<IllegalReferenceCountException>(() => buffer.Retain());
        Assert.Equal(int.MaxValue, buffer.ReferenceCount);
        Assert.True(buffer.Release(int.MaxValue));
    }

    [Fact]
    public void TestRetainOverflow2()
    {
        var buffer = new CountingBuffer();
        Assert.Equal(1, buffer.ReferenceCount);
        Assert.Throws<IllegalReferenceCountException>(() => buffer.Retain(int.MaxValue));
        Assert.Equal(1, buffer.ReferenceCount);
        Assert.True(buffer.Release());
    }

    [Fact]
    public void TestReleaseOverflow()
    {
        var buffer = new CountingBuffer();
        buffer.SetCount(0);
        Assert.Equal(0, buffer.ReferenceCount);
        Assert.Throws<IllegalReferenceCountException>(() => buffer.Release(int.MaxValue));
        buffer.SetCount(1);
        Assert.True(buffer.Release());
    }

    [Fact]
    public void TestReleaseErrorMessage()
    {
        var buffer = new CountingBuffer();
        Assert.True(buffer.Release());
        var error = Assert.Throws<IllegalReferenceCountException>(() => buffer.Release());
        Assert.Equal("refCnt: 0, decrement: 1", error.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TestRetainResurrect(int increment)
    {
        var buffer = new CountingBuffer();
        Assert.True(buffer.Release());
        Assert.Equal(0, buffer.ReferenceCount);
        Assert.Throws<IllegalReferenceCountException>(() => buffer.Retain(increment));
        Assert.Equal(1, buffer.Deallocations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void InvalidOwnershipChangesKeepTheLiveCount(int change)
    {
        var buffer = new CountingBuffer();
        Assert.Throws<ArgumentException>(() => buffer.Retain(change));
        Assert.Throws<ArgumentException>(() => buffer.Release(change));
        Assert.Equal(1, buffer.ReferenceCount);
        Assert.True(buffer.Release());
    }

    [Fact]
    public async Task CompetingFinalReleasesDeallocateExactlyOnce()
    {
        var buffer = new CountingBuffer();
        buffer.Retain(49);
        bool[] releases = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => buffer.Release())));
        Assert.Single(releases, final => final);
        Assert.Equal(0, buffer.ReferenceCount);
        Assert.Equal(1, buffer.Deallocations);
        Assert.Throws<IllegalReferenceCountException>(() => buffer.GetByte(0));
    }

    [Fact]
    public void CommonReferenceCountedInterfaceUsesTheSameOwnership()
    {
        var buffer = new CountingBuffer();
        IReferenceCounted counted = buffer;
        Assert.Same(buffer, counted.Retain());
        Assert.Same(buffer, counted.Touch());
        Assert.Same(buffer, counted.Touch("test"));
        Assert.False(counted.Release());
        Assert.True(buffer.Release());
        Assert.Equal(1, buffer.Deallocations);
    }

    private sealed class CountingBuffer : UnpooledHeapByteBuf
    {
        public int Deallocations;
        public CountingBuffer() : base(8, int.MaxValue) { }
        public void SetCount(int value) => SetReferenceCount(value);
        protected override void Deallocate()
        {
            // Original fixture's stub deallocator:
            // NOOP
            // CLR: use real storage and count final deallocation instead of a throwing stub buffer.
            Interlocked.Increment(ref Deallocations);
            base.Deallocate();
        }
    }
}
