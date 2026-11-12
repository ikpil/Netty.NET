using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental source-contract tests for AbstractNioMessageChannel.doWrite and
// NioDatagramChannel.doWriteMessage. Original JUnit fixture mappings stay separate.
#pragma warning disable xUnit1031 // A manual event loop must be pumped on its owning thread.
#pragma warning disable xUnit1051 // Cleanup and held native operations have independent lifetimes.
public class DatagramWriteBudgetContractTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void EachIoPassConsumesAtMostItsMessageQuantum(int budget)
    {
        using var fixture = new Fixture(new RecordingDatagram(), budget);
        Task[] writes = Enqueue(fixture.Channel, 5);
        fixture.Channel.Flush();
        Assert.Equal(Math.Min(budget, 5), fixture.Channel.Attempts.Count);
        int completed = Math.Min(budget, 5);
        while (completed < writes.Length)
        {
            Assert.Equal(1, fixture.Loop.RunNow(-1));
            completed += Math.Min(budget, writes.Length - completed);
            Assert.Equal(completed, fixture.Channel.Attempts.Count);
            Assert.All(writes.Take(completed), write => Assert.True(write.IsCompletedSuccessfully));
            Assert.All(writes.Skip(completed), write => Assert.False(write.IsCompleted));
        }
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, fixture.Channel.Attempts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void WriteReadinessContinuationLeavesAnOrdinaryTaskTurn(int budget)
    {
        using var fixture = new Fixture(new RecordingDatagram(), budget);
        Enqueue(fixture.Channel, 6);
        fixture.Channel.Flush();
        int observed = 0;
        fixture.Loop.Execute(() => observed = fixture.Channel.Attempts.Count);
        Assert.Equal(2, fixture.Loop.RunNow()); // One channel I/O event and one ordinary task.
        Assert.Equal(2 * budget, observed);
        Assert.True(observed < 6);
        fixture.PumpUntil(() => fixture.Channel.Attempts.Count == 6);
    }

    [Theory(Timeout = 10000)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ThreadedLoopAlsoRunsOrdinaryWorkBetweenWriteQuanta(int budget)
    {
        var group = new DefaultEventLoopGroup(1);
        var channel = new RecordingDatagram();
        channel.Config.SetAutoRead(false);
        channel.Config.SetOption(ChannelOption.MAX_MESSAGES_PER_WRITE, budget);
        var marker = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await group.RegisterAsync(channel, TestContext.Current.CancellationToken);
            await channel.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
            Task[] writes = null;
            await channel.EventLoop.SubmitAsync(() =>
            {
                writes = Enqueue(channel, 6);
                channel.Flush();
                channel.EventLoop.Execute(() => marker.TrySetResult(channel.Attempts.Count));
            }, TestContext.Current.CancellationToken);
            Assert.Equal(2 * budget, await marker.Task.WaitAsync(TestContext.Current.CancellationToken));
            await Task.WhenAll(writes).WaitAsync(TestContext.Current.CancellationToken);
        }
        finally { await group.StopAsync(); }
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void AConfigChangeTakesEffectOnTheNextWritePass(int initial, int next)
    {
        using var fixture = new Fixture(new RecordingDatagram(), initial);
        fixture.Channel.Write = id =>
        {
            if (id == 1) fixture.Channel.Config.SetOption(ChannelOption.MAX_MESSAGES_PER_WRITE, next);
            return Task.CompletedTask;
        };
        Task[] writes = Enqueue(fixture.Channel, 6);
        fixture.Channel.Flush();
        Assert.Equal(initial, fixture.Channel.Attempts.Count);
        fixture.Loop.RunNow(-1);
        Assert.Equal(initial + next, fixture.Channel.Attempts.Count);
        fixture.PumpUntil(() => writes.All(write => write.IsCompleted));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RecoverableFailedMessagesConsumeTheQuantumAndKeepTheirCause(int budget)
    {
        using var fixture = new Fixture(new RecordingDatagram(), budget);
        var error = new IOException("One datagram failed.");
        fixture.Channel.Write = id => id == 1 ? Task.FromException(error) : Task.CompletedTask;
        var buffers = Enumerable.Range(1, 4).Select(id => Unpooled.Buffer(1).WriteByte(id)).ToArray();
        Task[] writes = buffers.Select(buffer => fixture.Channel.WriteAsync(buffer)).ToArray();
        fixture.Channel.Flush();
        Assert.Equal(budget, fixture.Channel.Attempts.Count);
        Assert.Same(error, Assert.Throws<IOException>(() => writes[0].GetAwaiter().GetResult()));
        fixture.PumpUntil(() => writes.All(write => write.IsCompleted));
        Assert.All(writes.Skip(1), write => Assert.True(write.IsCompletedSuccessfully));
        Assert.All(buffers, buffer => Assert.Equal(0, buffer.ReferenceCount));
        Assert.True(fixture.Channel.IsOpen);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CancelledQueuePlaceholdersConsumeOneMessageSlot(int budget)
    {
        using var fixture = new Fixture(new RecordingDatagram(), budget);
        using var cancel = new CancellationTokenSource();
        var buffer = Unpooled.Buffer(1).WriteByte(9);
        var cancelled = fixture.Channel.WriteAsync(buffer, cancel.Token);
        cancel.Cancel();
        var writes = Enqueue(fixture.Channel, 3);
        fixture.Channel.Flush();
        Assert.True(cancelled.IsCanceled);
        Assert.Equal(0, buffer.ReferenceCount);
        Assert.Equal(budget, fixture.Channel.Attempts.Count);
        Assert.Equal(-1, fixture.Channel.Attempts[0]);
        fixture.PumpUntil(() => writes.All(write => write.IsCompleted));
        Assert.Equal(new[] { -1, 1, 2, 3 }, fixture.Channel.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReentrantWritesRespectFlushAdmissionAcrossPasses(bool flush)
    {
        using var fixture = new Fixture(new RecordingDatagram(), 1);
        Task later = null;
        fixture.Channel.Write = id =>
        {
            if (id == 1)
            {
                later = fixture.Channel.WriteAsync(Unpooled.Buffer(1).WriteByte(4));
                if (flush) fixture.Channel.Flush();
            }
            return Task.CompletedTask;
        };
        Task[] first = Enqueue(fixture.Channel, 3);
        fixture.Channel.Flush();
        Assert.Single(fixture.Channel.Attempts);
        fixture.PumpUntil(() => first.All(write => write.IsCompleted) && (!flush || later.IsCompleted));
        Assert.Equal(flush ? new[] { 1, 2, 3, 4 } : new[] { 1, 2, 3 }, fixture.Channel.Attempts);
        if (!flush)
        {
            Assert.False(later.IsCompleted);
            fixture.Channel.Flush();
            Assert.True(later.IsCompletedSuccessfully);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AResumedNativeWriteRetainsSingleWriterAndBufferOwnership(bool fail)
    {
        using var fixture = new Fixture(new RecordingDatagram(), 1);
        var native = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Channel.Write = id => id == 2 ? native.Task : Task.CompletedTask;
        var second = Unpooled.Buffer(1).WriteByte(2);
        var first = fixture.Channel.WriteAsync(Unpooled.Buffer(1).WriteByte(1));
        var pending = fixture.Channel.WriteAsync(second);
        var third = fixture.Channel.WriteAsync(Unpooled.Buffer(1).WriteByte(3));
        try
        {
            fixture.Channel.Flush();
            Assert.True(first.IsCompletedSuccessfully);
            fixture.Loop.RunNow(-1);
            Assert.Equal(new[] { 1, 2 }, fixture.Channel.Attempts);
            Assert.Equal(2, second.ReferenceCount);
            var last = fixture.Channel.WriteAndFlushAsync(Unpooled.Buffer(1).WriteByte(4));
            fixture.Loop.RunNow();
            Assert.Equal(new[] { 1, 2 }, fixture.Channel.Attempts);
            var error = new IOException("Pending native send failed.");
            if (fail) native.SetException(error); else native.SetResult();
            fixture.PumpUntil(() => pending.IsCompleted && third.IsCompleted && last.IsCompleted);
            if (fail) Assert.Same(error, Assert.Throws<IOException>(() => pending.GetAwaiter().GetResult()));
            else Assert.True(pending.IsCompletedSuccessfully);
            Assert.True(third.IsCompletedSuccessfully);
            Assert.True(last.IsCompletedSuccessfully);
            Assert.Equal(0, second.ReferenceCount);
            Assert.Equal(new[] { 1, 2, 3, 4 }, fixture.Channel.Attempts);
        }
        finally { native.TrySetResult(); fixture.PumpUntil(() => second.ReferenceCount == 0); }
    }

    [Fact]
    public void ClosingBeforeAWriteReadinessTurnFailsAndReleasesTheRemainingQueue()
    {
        using var fixture = new Fixture(new RecordingDatagram(), 1);
        var second = Unpooled.Buffer(1).WriteByte(2);
        var first = fixture.Channel.WriteAsync(Unpooled.Buffer(1).WriteByte(1));
        var pending = fixture.Channel.WriteAsync(second);
        fixture.Channel.Flush();
        var close = fixture.Channel.CloseAsync();
        fixture.PumpUntil(() => close.IsCompleted);
        close.GetAwaiter().GetResult();
        Assert.True(first.IsCompletedSuccessfully);
        Assert.Throws<IOException>(() => pending.GetAwaiter().GetResult());
        Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(new[] { 1 }, fixture.Channel.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACustomConfigUsesItsOptionOrTheSourceUnlimitedFallback(bool supportsOption)
    {
        using var fixture = new Fixture(new CustomConfigDatagram(supportsOption), null);
        var writes = Enqueue(fixture.Channel, 3);
        fixture.Channel.Flush();
        Assert.Equal(supportsOption ? 1 : 3, fixture.Channel.Attempts.Count);
        fixture.PumpUntil(() => writes.All(write => write.IsCompleted));
    }

    [Fact]
    public void MessageQuantumDoesNotChangeTheEmbeddedTransportWritePolicy()
    {
        var channel = new EmbeddedChannel();
        try
        {
            channel.Config.SetOption(ChannelOption.MAX_MESSAGES_PER_WRITE, 1);
            Task[] writes = Enqueue(channel, 5);
            channel.Flush();
            Assert.All(writes, write => Assert.True(write.IsCompletedSuccessfully));
            Assert.Equal(5, channel.OutboundMessages.Count);
        }
        finally { channel.FinishAndReleaseAll(); }
    }

    [Theory]
    [InlineData(AddressFamily.InterNetwork, false)]
    [InlineData(AddressFamily.InterNetwork, true)]
    [InlineData(AddressFamily.InterNetworkV6, false)]
    [InlineData(AddressFamily.InterNetworkV6, true)]
    public void EmptyAndCancelledMessagesDoNotEmitNativePackets(AddressFamily family, bool connected)
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var sender = new DatagramChannel(family);
        sender.Config.SetAutoRead(false);
        sender.Config.SetOption(ChannelOption.MAX_MESSAGES_PER_WRITE, 1);
        using var receiver = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        var loopback = family == AddressFamily.InterNetwork ? IPAddress.Loopback : IPAddress.IPv6Loopback;
        receiver.Bind(new IPEndPoint(loopback, 0));
        var target = (IPEndPoint)receiver.LocalEndPoint;
        using var cancel = new CancellationTokenSource();
        try
        {
            loop.RegisterAsync(sender).GetAwaiter().GetResult();
            sender.BindAsync(new IPEndPoint(loopback, 0)).GetAwaiter().GetResult();
            if (connected)
            {
                var connect = sender.ConnectAsync(target);
                PumpUntil(loop, () => connect.IsCompleted);
                connect.GetAwaiter().GetResult();
            }
            var bareEmpty = sender.WriteAsync(Unpooled.Buffer(0));
            var addressedEmpty = sender.WriteAsync(new DatagramPacket(Unpooled.Buffer(0), target));
            var payload = Unpooled.Buffer(1).WriteByte(99);
            var cancelled = sender.WriteAsync(new DatagramPacket(payload, target), cancel.Token);
            cancel.Cancel();
            var sent = sender.WriteAndFlushAsync(new DatagramPacket(Unpooled.Buffer(1).WriteByte(42), target));
            PumpUntil(loop, () => bareEmpty.IsCompleted && addressedEmpty.IsCompleted && sent.IsCompleted);
            bareEmpty.GetAwaiter().GetResult();
            addressedEmpty.GetAwaiter().GetResult();
            sent.GetAwaiter().GetResult();
            Assert.True(cancelled.IsCanceled);
            Assert.Equal(0, payload.ReferenceCount);
            Assert.True(receiver.Poll(2_000_000, SelectMode.SelectRead));
            byte[] bytes = new byte[16];
            Assert.Equal(1, receiver.Receive(bytes));
            Assert.Equal(42, bytes[0]);
            Assert.False(receiver.Poll(10_000, SelectMode.SelectRead));
        }
        finally { ManualIoEventLoopSourceTest.Terminate(loop); }
    }

    private static Task[] Enqueue(IChannel channel, int count) => Enumerable.Range(1, count)
        .Select(id => channel.WriteAsync(Unpooled.Buffer(1).WriteByte(id))).ToArray();

    private static void PumpUntil(ManualIoEventLoop loop, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Native write completion did not reach its owner.");
            loop.RunNow();
            Thread.Sleep(1);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly ManualIoEventLoop Loop = new(Thread.CurrentThread);
        internal readonly RecordingDatagram Channel;
        internal Fixture(RecordingDatagram channel, int? budget)
        {
            Channel = channel;
            Channel.Config.SetAutoRead(false);
            if (budget.HasValue) Channel.Config.SetOption(ChannelOption.MAX_MESSAGES_PER_WRITE, budget.Value);
            Loop.RegisterAsync(Channel).GetAwaiter().GetResult();
            Channel.BindAsync(new IPEndPoint(IPAddress.Loopback, 0)).GetAwaiter().GetResult();
        }
        internal void PumpUntil(Func<bool> condition) => DatagramWriteBudgetContractTest.PumpUntil(Loop, condition);
        public void Dispose() => ManualIoEventLoopSourceTest.Terminate(Loop);
    }

    private class RecordingDatagram : DatagramChannel
    {
        internal readonly List<int> Attempts = new();
        internal Func<int, Task> Write;
        protected override Task DoWriteAsync(object message)
        {
            var buffer = Assert.IsAssignableFrom<ByteBuf>(message);
            int id = buffer.IsReadable ? buffer.GetByte(buffer.ReaderIndex) : -1;
            Attempts.Add(id);
            return Write?.Invoke(id) ?? Task.CompletedTask;
        }
    }

    private sealed class CustomConfigDatagram(bool supportsOption) : RecordingDatagram
    {
        protected override IChannelConfig NewConfig()
        {
            var underlying = base.NewConfig();
            var mock = new Mock<IChannelConfig>();
            int budget = 1;
            mock.Setup(config => config.TryGetOption(ChannelOption.MAX_MESSAGES_PER_WRITE, out budget)).Returns(supportsOption);
            mock.Setup(config => config.GetMessageSizeEstimator()).Returns(underlying.GetMessageSizeEstimator());
            mock.Setup(config => config.GetAllocator()).Returns(underlying.GetAllocator());
            mock.Setup(config => config.GetWriteBufferHighWaterMark()).Returns(65536);
            mock.Setup(config => config.GetWriteBufferLowWaterMark()).Returns(32768);
            return mock.Object;
        }
    }
}
