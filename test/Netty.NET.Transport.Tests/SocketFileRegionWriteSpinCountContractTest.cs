using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental source-contract tests for the FileRegion branch of AbstractNioByteChannel.doWriteInternal.
#pragma warning disable xUnit1031 // The manual event loop is owned by this thread.
#pragma warning disable xUnit1051 // Native borrows and shutdown have independent lifetimes.
public class SocketFileRegionWriteSpinCountContractTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FileRegionAttemptsConsumeTheConfiguredWriteQuantum(int writeSpinCount)
    {
        using var fixture = new Fixture(writeSpinCount);
        var region = new Region(5);
        var write = fixture.Channel.WriteAndFlushAsync(region);
        Assert.Equal(writeSpinCount, fixture.Channel.FileCalls);
        Assert.Equal(writeSpinCount, region.Transferred());
        Assert.False(write.IsCompleted);
        Assert.Equal(1, region.ReferenceCount);
        fixture.PumpUntil(() => write.IsCompleted);
        write.GetAwaiter().GetResult();
        Assert.Equal(new long[] { 0, 1, 2, 3, 4 }, region.Positions);
        Assert.Equal(0, region.ReferenceCount);
        Assert.Equal(new byte[] { 42, 42, 42, 42, 42 }, fixture.Receive(5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ZeroProgressEndsThePassAndLeavesAnOrdinaryTaskTurn(int writeSpinCount)
    {
        using var fixture = new Fixture(writeSpinCount);
        var region = new Region(1, transfer: (self, target, position) =>
        {
            if (self.Positions.Count <= 2) return 0;
            target.WriteByte(42); self.Advance(1); return 1;
        });
        var write = fixture.Channel.WriteAndFlushAsync(region);
        Assert.Equal(1, fixture.Channel.FileCalls);
        Assert.False(write.IsCompleted);
        int observed = 0;
        fixture.Loop.Execute(() => observed = fixture.Channel.FileCalls);
        fixture.Loop.RunNow();
        Assert.Equal(2, observed);
        Assert.False(write.IsCompleted);
        fixture.PumpUntil(() => write.IsCompleted);
        write.GetAwaiter().GetResult();
        Assert.Equal(3, fixture.Channel.FileCalls);
        Assert.Equal(new byte[] { 42 }, fixture.Receive(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyAndAlreadyTransferredRegionsHaveZeroQuantumCost(bool alreadyTransferred)
    {
        using var fixture = new Fixture(1);
        var region = new Region(alreadyTransferred ? 3 : 0, alreadyTransferred ? 3 : 0);
        var first = fixture.Channel.WriteAsync(region);
        var last = fixture.Channel.WriteAndFlushAsync(Unpooled.Buffer(1).WriteByte(7));
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(last.IsCompletedSuccessfully);
        Assert.Equal(0, fixture.Channel.FileCalls);
        Assert.Equal(1, fixture.Channel.ByteCalls);
        Assert.Equal(0, region.ReferenceCount);
        Assert.Equal(new byte[] { 7 }, fixture.Receive(1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void BytesAndFileRegionsShareTheSameWriteQuantum(int writeSpinCount)
    {
        using var fixture = new Fixture(writeSpinCount);
        var first = fixture.Channel.WriteAsync(Unpooled.Buffer(1).WriteByte(1));
        var region = new Region(2);
        var file = fixture.Channel.WriteAsync(region);
        var last = fixture.Channel.WriteAndFlushAsync(Unpooled.Buffer(1).WriteByte(4));
        Assert.True(first.IsCompletedSuccessfully);
        Assert.Equal(1, fixture.Channel.ByteCalls);
        Assert.Equal(writeSpinCount - 1, fixture.Channel.FileCalls);
        Assert.False(file.IsCompleted);
        Assert.False(last.IsCompleted);
        fixture.PumpUntil(() => file.IsCompleted && last.IsCompleted);
        Assert.True(file.IsCompletedSuccessfully);
        Assert.True(last.IsCompletedSuccessfully);
        Assert.Equal(new byte[] { 1, 42, 42, 4 }, fixture.Receive(4));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void AFileProducerCannotChangeTheCurrentPassQuantum(int writeSpinCount, int nextWriteSpinCount)
    {
        using var fixture = new Fixture(writeSpinCount);
        var region = new Region(6, transfer: (self, target, position) =>
        {
            if (position == 0) fixture.Channel.Config.SetWriteSpinCount(nextWriteSpinCount);
            target.WriteByte(42); self.Advance(1); return 1;
        });
        var write = fixture.Channel.WriteAndFlushAsync(region);
        Assert.Equal(writeSpinCount, fixture.Channel.FileCalls);
        fixture.Loop.RunNow(-1);
        Assert.Equal(writeSpinCount + nextWriteSpinCount, fixture.Channel.FileCalls);
        fixture.PumpUntil(() => write.IsCompleted);
        write.GetAwaiter().GetResult();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LogicalCompletionCanExceedCountWithoutMatchingPhysicalBytes(bool physicalBytes)
    {
        using var fixture = new Fixture(1);
        var region = new Region(1, transfer: (self, target, position) =>
        {
            self.Advance(10);
            if (physicalBytes) target.WriteByte(42);
            return physicalBytes ? 1 : 0;
        });
        var write = fixture.Channel.WriteAndFlushAsync(region);
        Assert.Equal(physicalBytes, write.IsCompletedSuccessfully);
        fixture.PumpUntil(() => write.IsCompleted);
        write.GetAwaiter().GetResult();
        Assert.Equal(1, fixture.Channel.FileCalls);
        Assert.Equal(10, region.Transferred());
        Assert.Equal(0, region.ReferenceCount);
        if (physicalBytes) Assert.Equal(new byte[] { 42 }, fixture.Receive(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProducerFailuresPreserveTheirCauseAndReleaseTheBorrow(bool throwSynchronously)
    {
        using var fixture = new Fixture(1);
        var cause = new IOException("File producer failed.");
        fixture.Channel.Write = region => throwSynchronously ? throw cause : Task.FromException<long>(cause);
        var region = new Region(1);
        var write = fixture.Channel.WriteAndFlushAsync(region);
        fixture.PumpUntil(() => write.IsCompleted);
        Assert.Same(cause, Assert.Throws<IOException>(() => write.GetAwaiter().GetResult()));
        Assert.Equal(0, region.ReferenceCount);
        Assert.True(region.Deallocated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingRetainsAFileProducerUntilItsTaskFinishes(bool stop)
    {
        using var fixture = new Fixture(1);
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Channel.Write = region => completion.Task;
        var region = new Region(1);
        var write = fixture.Channel.WriteAndFlushAsync(region);
        try
        {
            Assert.Equal(2, region.ReferenceCount);
            if (stop) ManualIoEventLoopSourceTest.Terminate(fixture.Loop);
            else
            {
                var close = fixture.Channel.CloseAsync();
                fixture.PumpUntil(() => close.IsCompleted);
                close.GetAwaiter().GetResult();
            }
            Assert.Throws<IOException>(() => write.GetAwaiter().GetResult());
            Assert.Equal(1, region.ReferenceCount);
            Assert.False(region.Deallocated);
            region.Advance(1);
            completion.SetResult(1);
            Assert.True(SpinWait.SpinUntil(() => region.ReferenceCount == 0 && region.Deallocated, TimeSpan.FromSeconds(3)));
            Assert.True(region.Deallocated);
        }
        finally { completion.TrySetResult(1); }
    }

    [Fact]
    public void AProgressObserverCanCloseTheCurrentFileRegion()
    {
        using var fixture = new Fixture(1);
        fixture.Channel.Config.WriteProgress = new Observer(value => { _ = fixture.Channel.CloseAsync(); });
        var region = new Region(3);
        var write = fixture.Channel.WriteAndFlushAsync(region);
        fixture.PumpUntil(() => write.IsCompleted);
        Assert.Throws<IOException>(() => write.GetAwaiter().GetResult());
        Assert.Equal(1, fixture.Channel.FileCalls);
        Assert.Equal(0, region.ReferenceCount);
        Assert.Equal(new byte[] { 42 }, fixture.Receive(1));
    }

    [Fact]
    public void ThePortableWorkerRetriesFromTheUnchangedRelativePosition()
    {
        using var fixture = new Fixture(1);
        fixture.Channel.UseBase = true;
        bool onOwner = false;
        var region = new Region(1, transfer: (self, target, position) =>
        {
            onOwner |= fixture.Loop.InEventLoop();
            if (self.Positions.Count == 1) return 0;
            target.WriteByte(42); self.Advance(1); return 1;
        });
        var write = fixture.Channel.WriteAndFlushAsync(region);
        fixture.PumpUntil(() => write.IsCompleted);
        write.GetAwaiter().GetResult();
        Assert.Equal(new long[] { 0, 0 }, region.Positions);
        Assert.False(onOwner);
        Assert.Equal(0, region.ReferenceCount);
        Assert.Equal(new byte[] { 42 }, fixture.Receive(1));
    }

    private sealed class Observer(Action<ChannelWriteProgress> report) : IProgress<ChannelWriteProgress>
    { public void Report(ChannelWriteProgress value) => report(value); }

    private sealed class Region(long count, long transferred = 0, Func<Region, Stream, long, long> transfer = null)
        : AbstractReferenceCounted, IFileRegion
    {
        private long _transferred = transferred;
        internal readonly List<long> Positions = new();
        internal volatile bool Deallocated;
        public long Position() => 0;
        public long Count() => count;
        public long Transferred() => _transferred;
        internal void Advance(long count) => _transferred += count;
        public long TransferTo(Stream target, long position)
        {
            Positions.Add(position);
            Assert.Equal(_transferred, position);
            if (transfer != null) return transfer(this, target, position);
            target.WriteByte(42); _transferred++; return 1;
        }
        protected override void Deallocate() => Deallocated = true;
        public new IFileRegion Retain() { base.Retain(); return this; }
        public override IFileRegion Retain(int increment) { base.Retain(increment); return this; }
        public new IFileRegion Touch() => this;
        public override IFileRegion Touch(object hint) => this;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly ManualIoEventLoop Loop = new(Thread.CurrentThread);
        internal readonly Probe Channel;
        private readonly Socket _peer;
        internal Fixture(int writeSpinCount)
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0)); listener.Listen(1);
            _peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _peer.Connect(listener.LocalEndPoint);
            Channel = new Probe(listener.Accept());
            Channel.Config.SetAutoRead(false).SetWriteSpinCount(writeSpinCount);
            Loop.RegisterAsync(Channel).GetAwaiter().GetResult();
        }
        internal void PumpUntil(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "File write did not reach its owner.");
                Loop.RunNow(); Thread.Sleep(1);
            }
        }
        internal byte[] Receive(int length)
        {
            var bytes = new byte[length]; int offset = 0;
            while (offset < length)
            {
                Assert.True(_peer.Poll(2_000_000, SelectMode.SelectRead));
                int read = _peer.Receive(bytes.AsSpan(offset)); Assert.True(read > 0); offset += read;
            }
            return bytes;
        }
        public void Dispose() { ManualIoEventLoopSourceTest.Terminate(Loop); _peer.Dispose(); }
    }

    private sealed class Probe(Socket socket) : TcpSocketChannel(socket)
    {
        internal int FileCalls, ByteCalls;
        internal bool UseBase;
        internal Func<IFileRegion, Task<long>> Write;
        internal override bool SupportsGatheringWrites => true;
        internal override Task<int> TryGatherWriteAsync() => null;
        protected override Task<int> DoWriteBytes(ByteBuf buf)
        { ByteCalls++; return Task.FromResult(Socket.Send(buf.ReadableSequence.First.Span[..1], SocketFlags.None)); }
        protected override Task<long> DoWriteFileRegion(IFileRegion region)
        {
            FileCalls++;
            if (UseBase) return base.DoWriteFileRegion(region);
            if (Write != null) return Write(region);
            using var target = new NetworkStream(Socket, ownsSocket: false);
            long position = region.Transferred();
            return Task.FromResult(region.TransferTo(target, position));
        }
    }
}
