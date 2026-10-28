using System;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

// CLR regression cases remain separate from original Java fixtures.
public class ReceiveAllocatorContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MessageBudgetHonorsIgnoreBytesRead(bool ignoreBytesRead)
    {
        var config = new Mock<IChannelConfig>();
        config.Setup(c => c.IsAutoRead()).Returns(true);
        var allocator = new ProbeAllocator(2, ignoreBytesRead);
        var handle = (ProbeAllocator.ProbeHandle)allocator.NewHandle();
        handle.Reset(config.Object);
        handle.IncMessagesRead(1);
        Assert.Equal(ignoreBytesRead, handle.ContinueReading());
        handle.AttemptedBytesRead(1);
        handle.LastBytesRead(1);
        Assert.True(handle.ContinueReading());
        handle.IncMessagesRead(1);
        Assert.False(handle.ContinueReading());
        handle.Reset(config.Object);
        Assert.Equal(0, handle.Total());
        Assert.Equal(1, handle.LastBytesRead()); // Reset clears totals, not the previous read observation.
        Assert.Equal(1, handle.AttemptedBytesRead());
    }

    [Fact]
    public void AutoReadIsObservedLiveAndShortCircuitsSupplier()
    {
        bool autoRead = true;
        var config = new Mock<IChannelConfig>();
        config.Setup(c => c.IsAutoRead()).Returns(() => autoRead);
        var handle = (IRecvByteBufAllocator.IExtendedHandle)new FixedRecvByteBufAllocator(512).NewHandle();
        handle.Reset(config.Object);
        handle.LastBytesRead(1);
        Assert.True(handle.ContinueReading(() => true));
        autoRead = false;
        Assert.False(handle.ContinueReading(() => throw new Exception("supplier must not run")));
        autoRead = true;
        Assert.False(handle.ContinueReading(() => false));
    }

    [Fact]
    public void RespectMoreDataIsCapturedOnHandleCreationAndMessageLimitOnReset()
    {
        var config = new Mock<IChannelConfig>();
        config.Setup(c => c.IsAutoRead()).Returns(true);
        var allocator = new FixedRecvByteBufAllocator(128);
        var old = (IRecvByteBufAllocator.IExtendedHandle)allocator.NewHandle();
        Assert.Same(allocator, allocator.RespectMaybeMoreData(false));
        var next = (IRecvByteBufAllocator.IExtendedHandle)allocator.NewHandle();
        foreach (var handle in new[] { old, next })
        {
            handle.Reset(config.Object);
            handle.LastBytesRead(1);
        }
        Assert.False(old.ContinueReading(() => false));
        Assert.True(next.ContinueReading(() => throw new Exception("ignored supplier")));
        next.IncMessagesRead(1);
        Assert.False(next.ContinueReading());
        allocator.MaxMessagesPerRead(2);
        Assert.False(next.ContinueReading());
        next.Reset(config.Object);
        next.IncMessagesRead(1);
        next.LastBytesRead(1);
        Assert.True(next.ContinueReading());
    }

    [Fact]
    public void PositiveByteTotalsUseOriginalInt32OverflowAndSaturatingObservation()
    {
        var config = new Mock<IChannelConfig>();
        config.Setup(c => c.IsAutoRead()).Returns(true);
        var handle = (ProbeAllocator.ProbeHandle)new ProbeAllocator(2, false).NewHandle();
        handle.Reset(config.Object);
        handle.LastBytesRead(int.MaxValue);
        handle.LastBytesRead(1);
        Assert.Equal(int.MaxValue, handle.Total());
        Assert.False(handle.ContinueReading(() => true));
        handle.LastBytesRead(-1);
        Assert.Equal(-1, handle.LastBytesRead());
        Assert.Equal(int.MaxValue, handle.Total());
    }

    [Fact]
    public void FixedAllocatorCreatesIndependentHandlesAndIoBuffers()
    {
        var allocator = new FixedRecvByteBufAllocator(513);
        var first = allocator.NewHandle();
        var second = allocator.NewHandle();
        Assert.NotSame(first, second);
        first.LastBytesRead(500);
        first.ReadComplete();
        Assert.Equal(513, first.Guess());
        Assert.Equal(513, second.Guess());
        var alloc = new Mock<IByteBufAllocator>();
        ByteBuf buffer = Unpooled.Buffer(513);
        alloc.Setup(a => a.IoBuffer(513, int.MaxValue)).Returns(buffer);
        try
        {
            Assert.Same(buffer, first.Allocate(alloc.Object));
            alloc.Verify(a => a.IoBuffer(513, int.MaxValue), Times.Once());
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(81)]
    public void AdaptiveAllocatorUsesCommonClrBoundedSmallRangePolicy(int size)
    {
        var handle = new AdaptiveRecvByteBufAllocator(size, size, size).NewHandle();
        Assert.Equal(size, handle.Guess());
        handle.AttemptedBytesRead(size);
        handle.LastBytesRead(size);
        handle.ReadComplete();
        Assert.Equal(size, handle.Guess());
    }

    [Fact]
    public void ByteBudgetLimitsIndividualAllocationsAndStopsAtExhaustion()
    {
        var allocator = new DefaultMaxBytesRecvByteBufAllocator(10, 6);
        var handle = (IRecvByteBufAllocator.IExtendedHandle)allocator.NewHandle();
        handle.Reset(null); // This implementation does not consult channel config, as in the original.
        Assert.Equal(6, handle.Guess());
        handle.AttemptedBytesRead(6);
        handle.LastBytesRead(6);
        Assert.True(handle.ContinueReading());
        Assert.Equal(4, handle.Guess());
        handle.AttemptedBytesRead(4);
        handle.LastBytesRead(4);
        Assert.Equal(0, handle.Guess());
        Assert.False(handle.ContinueReading(() => throw new Exception("budget exhausted")));
        allocator.MaxBytesPerReadPair(20, 8);
        Assert.Equal(0, handle.Guess());
        handle.Reset(null);
        Assert.Equal(8, handle.Guess());
    }

    [Fact]
    public void ByteBudgetDistinguishesPartialReadsAndExternalEofSignal()
    {
        var handle = (IRecvByteBufAllocator.IExtendedHandle)new DefaultMaxBytesRecvByteBufAllocator(10, 6).NewHandle();
        handle.Reset(null);
        handle.AttemptedBytesRead(6);
        handle.LastBytesRead(3);
        Assert.False(handle.ContinueReading());
        Assert.True(handle.ContinueReading(() => true));
        handle.LastBytesRead(-1);
        Assert.Equal(-1, handle.LastBytesRead());
        Assert.False(handle.ContinueReading());
    }

    [Fact]
    public void InvalidByteBudgetUpdatesDoNotMutateConfiguration()
    {
        IMaxBytesRecvByteBufAllocator allocator = new DefaultMaxBytesRecvByteBufAllocator(20, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.MaxBytesPerRead(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.MaxBytesPerIndividualRead(21));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.MaxBytesPerReadPair(0, 0));
        Assert.Equal((20, 10), allocator.MaxBytesPerReadPair());
        Assert.Same(allocator, allocator.MaxBytesPerReadPair(30, 15));
        Assert.Same(allocator, allocator.MaxBytesPerRead(40));
        Assert.Same(allocator, allocator.MaxBytesPerIndividualRead(20));
        Assert.Equal((40, 20), allocator.MaxBytesPerReadPair());
    }

    [Fact]
    public void ByteBudgetPairHasAtomicSnapshotsDuringConcurrentUpdates()
    {
        var allocator = new DefaultMaxBytesRecvByteBufAllocator(20, 10);
        Parallel.Invoke(
            () => { for (int i = 0; i < 10000; i++) allocator.MaxBytesPerReadPair(20, 10); },
            () => { for (int i = 0; i < 10000; i++) allocator.MaxBytesPerReadPair(40, 30); },
            () =>
            {
                for (int i = 0; i < 10000; i++)
                    Assert.Contains(allocator.MaxBytesPerReadPair(), new[] { (20, 10), (40, 30) });
            });
    }

    [Fact]
    public void DelegatingHandleForwardsEveryBaseOperation()
    {
        var handle = new Mock<IRecvByteBufAllocator.IHandle>(MockBehavior.Strict);
        var config = new Mock<IChannelConfig>().Object;
        IByteBufAllocator alloc = UnpooledByteBufAllocator.Default;
        ByteBuf buffer = Unpooled.Buffer(1);
        handle.Setup(h => h.Allocate(alloc)).Returns(buffer);
        handle.Setup(h => h.Guess()).Returns(1);
        handle.Setup(h => h.Reset(config));
        handle.Setup(h => h.IncMessagesRead(2));
        handle.Setup(h => h.LastBytesRead(3));
        handle.Setup(h => h.LastBytesRead()).Returns(3);
        handle.Setup(h => h.AttemptedBytesRead(4));
        handle.Setup(h => h.AttemptedBytesRead()).Returns(4);
        handle.Setup(h => h.ContinueReading()).Returns(true);
        handle.Setup(h => h.ReadComplete());
        var wrapper = new IRecvByteBufAllocator.DelegatingHandle(handle.Object);
        try
        {
            Assert.Same(buffer, wrapper.Allocate(alloc));
            Assert.Equal(1, wrapper.Guess());
            wrapper.Reset(config);
            wrapper.IncMessagesRead(2);
            wrapper.LastBytesRead(3);
            Assert.Equal(3, wrapper.LastBytesRead());
            wrapper.AttemptedBytesRead(4);
            Assert.Equal(4, wrapper.AttemptedBytesRead());
            Assert.True(wrapper.ContinueReading());
            wrapper.ReadComplete();
            handle.VerifyAll();
        }
        finally { buffer.Release(); }
        Assert.Throws<ArgumentNullException>(() => new IRecvByteBufAllocator.DelegatingHandle(null));
    }

    [Fact]
    public void ReceiveAllocatorsRejectNonpositiveLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedRecvByteBufAllocator(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedRecvByteBufAllocator(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedRecvByteBufAllocator(1).MaxMessagesPerRead(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultMaxBytesRecvByteBufAllocator(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultMaxBytesRecvByteBufAllocator(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultMaxBytesRecvByteBufAllocator(1, 2));
    }

    private sealed class ProbeAllocator(int limit, bool ignoreBytes) : DefaultMaxMessagesRecvByteBufAllocator(limit, ignoreBytes)
    {
        public override IRecvByteBufAllocator.IHandle NewHandle() => new ProbeHandle(this);
        internal sealed class ProbeHandle(ProbeAllocator owner) : MaxMessageHandle(owner)
        {
            public override int Guess() => 512;
            internal int Total() => TotalBytesRead();
        }
    }
}
