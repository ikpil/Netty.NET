/*
 * Copyright 2017 The Netty Project
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
using System.IO;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public class AdaptiveRecvByteBufAllocatorTest
{

    public AdaptiveRecvByteBufAllocatorTest() => Setup();

    private IChannelConfig config;
    private readonly IByteBufAllocator alloc = UnpooledByteBufAllocator.Default;
    private IRecvByteBufAllocator.IExtendedHandle handle;


    private void Setup()
    {
        var mocked = new Mock<IChannelConfig>();
        mocked.Setup(c => c.IsAutoRead()).Returns(true);
        config = mocked.Object;
        AdaptiveRecvByteBufAllocator recvByteBufAllocator = new AdaptiveRecvByteBufAllocator(64, 512, 1024 * 1024 * 10);
        handle = (IRecvByteBufAllocator.IExtendedHandle)recvByteBufAllocator.NewHandle();
        handle.Reset(config);
    }

    [Fact]
    public void RampUpBeforeReadCompleteWhenLargeDataPending()
    {
        // Simulate that there is always more data when we attempt to read so we should always ramp up.
        AllocReadExpected(handle, alloc, 512);
        AllocReadExpected(handle, alloc, 8192);
        AllocReadExpected(handle, alloc, 131072);
        AllocReadExpected(handle, alloc, 2097152);
        handle.ReadComplete();

        handle.Reset(config);
        AllocReadExpected(handle, alloc, 8388608);
    }

    [Fact]
    public void MemoryAllocationIntervalsTest()
    {
        ComputingNext(512, 512);
        ComputingNext(8192, 1110);
        ComputingNext(8192, 1200);
        ComputingNext(4096, 1300);
        ComputingNext(4096, 1500);
        ComputingNext(2048, 1700);
        ComputingNext(2048, 1550);
        ComputingNext(2048, 2000);
        ComputingNext(2048, 1900);
    }

    private void ComputingNext(long expectedSize, int actualReadBytes)
    {
        Assert.Equal(expectedSize, handle.Guess());
        handle.Reset(config);
        handle.LastBytesRead(actualReadBytes);
        handle.ReadComplete();
    }

    [Fact]
    public void LastPartialReadDoesNotRampDown()
    {
        AllocReadExpected(handle, alloc, 512);
        // Simulate there is just 1 byte remaining which is unread. However the total bytes in the current read cycle
        // means that we should stay at the current step for the next ready cycle.
        AllocRead(handle, alloc, 8192, 1);
        handle.ReadComplete();

        handle.Reset(config);
        AllocReadExpected(handle, alloc, 8192);
    }

    [Fact]
    public void LastPartialReadCanRampUp()
    {
        AllocReadExpected(handle, alloc, 512);
        // We simulate there is just 1 less byte than we try to read, but because of the adaptive steps the total amount
        // of bytes read for this read cycle steps up to prepare for the next read cycle.
        AllocRead(handle, alloc, 8192, 8191);
        handle.ReadComplete();

        handle.Reset(config);
        AllocReadExpected(handle, alloc, 131072);
    }

    [Fact]
    public void DoesNotExceedMaximum()
    {
        AdaptiveRecvByteBufAllocator recvByteBufAllocator = new AdaptiveRecvByteBufAllocator(64, 9000, 9000);
        IRecvByteBufAllocator.IExtendedHandle handle =
                (IRecvByteBufAllocator.IExtendedHandle)recvByteBufAllocator.NewHandle();
        handle.Reset(config);
        AllocReadExpected(handle, alloc, 8192);
    }

    [Fact]
    public void DoesSetCorrectMinBounds()
    {
        AdaptiveRecvByteBufAllocator recvByteBufAllocator = new AdaptiveRecvByteBufAllocator(81, 95, 95);
        IRecvByteBufAllocator.IExtendedHandle handle =
                (IRecvByteBufAllocator.IExtendedHandle)recvByteBufAllocator.NewHandle();
        handle.Reset(config);
        AllocReadExpected(handle, alloc, 81);
    }

    [Fact]
    public void ThrowsIfInitialIsBiggerThenMaximum()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new AdaptiveRecvByteBufAllocator(64, 4096, 1024);
        });
    }

    [Fact]
    public void ThrowsIfInitialIsSmallerThenMinimum()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new AdaptiveRecvByteBufAllocator(512, 64, 1024);
        });
    }

    [Fact]
    public void ThrowsIfMinimumIsBiggerThenMaximum()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new AdaptiveRecvByteBufAllocator(2048, 64, 1024);
        });
    }

    private static void AllocReadExpected(IRecvByteBufAllocator.IExtendedHandle handle,
                                          IByteBufAllocator alloc,
                                          int expectedSize)
    {
        AllocRead(handle, alloc, expectedSize, expectedSize);
    }

    private static void AllocRead(IRecvByteBufAllocator.IExtendedHandle handle,
                                  IByteBufAllocator alloc,
                                  int expectedBufferSize,
                                  int lastRead)
    {
        ByteBuf buf = handle.Allocate(alloc);
        Assert.Equal(expectedBufferSize, buf.Capacity);
        handle.AttemptedBytesRead(expectedBufferSize);
        handle.LastBytesRead(lastRead);
        handle.IncMessagesRead(1);
        buf.Release();
    }
}
