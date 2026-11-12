using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// CLR-only dispatch tests: preserve native virtual-slot inheritance and the existing whole-message extension contract.
#pragma warning disable xUnit1031 // All manual-loop operations are driven by the owning test thread.
public class SocketInheritedWriteContractTest
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    public void VirtualSlotInheritanceDeterminesNativeWriteOwnership(int kind, bool native)
    {
        using var fixture = new Fixture(kind);
        Assert.Equal(native, fixture.Channel.SupportsGatheringWrites);
        var buf = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 });
        Task future = fixture.Channel.WriteAndFlushAsync(buf, TestContext.Current.CancellationToken);
        Assert.Equal(native ? 1 : 4, fixture.Channel.ScalarCalls);
        Assert.Equal(native ? 0 : 1, fixture.Channel.WholeCalls);
        Assert.Equal(0, fixture.Channel.HiddenCalls);
        if (native)
        {
            Assert.False(future.IsCompleted);
            Assert.Equal(1, buf.ReaderIndex);
            bool ran = false;
            fixture.Loop.Execute(() => ran = true);
            fixture.Loop.RunNow();
            Assert.True(ran);
            fixture.PumpUntil(() => future.IsCompleted);
        }
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Equal(0, buf.ReferenceCount);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, fixture.Receive(4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void NativeSubclassesInheritRealGatheredSends(int kind)
    {
        using var fixture = new Fixture(kind);
        var buf = Unpooled.WrappedBuffer(new byte[] { 1 });
        var buf2 = Unpooled.WrappedBuffer(new byte[] { 2 });
        Task f1 = fixture.Channel.WriteAsync(buf, TestContext.Current.CancellationToken);
        Task f2 = fixture.Channel.WriteAndFlushAsync(buf2, TestContext.Current.CancellationToken);
        fixture.PumpUntil(() => f1.IsCompleted && f2.IsCompleted);
        Assert.True(f1.IsCompletedSuccessfully);
        Assert.True(f2.IsCompletedSuccessfully);
        Assert.Equal(1, fixture.Channel.VectorCalls);
        Assert.Equal(0, fixture.Channel.ScalarCalls);
        Assert.Equal(0, fixture.Channel.WholeCalls);
        Assert.Equal(0, fixture.Channel.HiddenCalls);
        Assert.Equal(0, buf.ReferenceCount);
        Assert.Equal(0, buf2.ReferenceCount);
        Assert.Equal(new byte[] { 1, 2 }, fixture.Receive(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InheritedFileProducersRetainThePerAttemptWriteQuantum(int kind)
    {
        using var fixture = new Fixture(kind);
        var region = new Region(new byte[] { 1, 2, 3, 4 });
        Task future = fixture.Channel.WriteAndFlushAsync(region, TestContext.Current.CancellationToken);
        Assert.Equal(1, fixture.Channel.FileCalls);
        Assert.Equal(1, region.Transferred());
        Assert.False(future.IsCompleted);
        Assert.Equal(1, region.ReferenceCount);
        fixture.PumpUntil(() => future.IsCompleted);
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Equal(4, fixture.Channel.FileCalls);
        Assert.Equal(0, region.ReferenceCount);
        Assert.Equal(1, region.Deallocations);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, fixture.Receive(4));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Socket _peer;
        internal readonly NativeProbe Channel;
        internal readonly ManualIoEventLoop Loop = new(Thread.CurrentThread);
        internal Fixture(int kind)
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            _peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _peer.Connect(listener.LocalEndPoint);
            Socket socket = listener.Accept();
            Channel = kind switch
            {
                0 => new NativeProbe(socket), 1 => new DerivedProbe(socket),
                2 => new HiddenProbe(socket), 3 => new HiddenVirtualProbe(socket),
                4 => new OverrideHiddenProbe(socket), 5 => new OverloadProbe(socket),
                6 => new WholeProbe(socket), 7 => new DerivedWholeProbe(socket),
                8 => new HiddenOverWholeProbe(socket), 9 => new ReoverrideWholeProbe(socket),
                10 => new CovariantWholeProbe(socket),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            Channel.Config.SetAutoRead(false).SetWriteSpinCount(1);
            Loop.RegisterAsync(Channel, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }
        internal void PumpUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "The inherited native write did not settle.");
                Loop.RunNow();
                Thread.Yield();
            }
        }
        internal byte[] Receive(int length)
        {
            var bytes = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                Assert.True(_peer.Poll(2_000_000, SelectMode.SelectRead));
                int read = _peer.Receive(bytes.AsSpan(offset));
                Assert.True(read > 0);
                offset += read;
            }
            return bytes;
        }
        public void Dispose() { ManualIoEventLoopSourceTest.Terminate(Loop); _peer.Dispose(); }
    }

    private class NativeProbe(Socket socket) : TcpSocketChannel(socket)
    {
        internal int ScalarCalls, VectorCalls, WholeCalls, HiddenCalls, FileCalls;
        protected override Task<int> DoWriteBytes(ByteBuf buf)
        {
            ScalarCalls++;
            return Task.FromResult(Socket.Send(buf.ReadableSequence.First.Span[..1], SocketFlags.None));
        }
        protected override Task<long> DoWriteFileRegion(IFileRegion region)
        {
            FileCalls++;
            using var target = new NetworkStream(Socket, false);
            long position = region.Transferred();
            return Task.FromResult(region.TransferTo(target, position));
        }
        internal override Task<int> SendGatheredAsync(IList<ArraySegment<byte>> segments)
        {
            VectorCalls++;
            return Task.FromResult(Socket.Send(segments, SocketFlags.None));
        }
    }
    private class DerivedProbe(Socket socket) : NativeProbe(socket);
    private class HiddenProbe(Socket socket) : NativeProbe(socket)
    {
        protected new Task DoWrite(object msg) { HiddenCalls++; throw new InvalidOperationException("Hidden method invoked."); }
    }
    private class HiddenVirtualProbe(Socket socket) : NativeProbe(socket)
    {
        protected new virtual Task DoWrite(object msg) { HiddenCalls++; throw new InvalidOperationException("New slot invoked."); }
    }
    private class OverrideHiddenProbe(Socket socket) : HiddenVirtualProbe(socket)
    {
        protected override Task DoWrite(object msg) { HiddenCalls++; throw new InvalidOperationException("Unrelated slot invoked."); }
    }
    private class OverloadProbe(Socket socket) : NativeProbe(socket)
    {
        protected Task DoWrite(string msg) { HiddenCalls++; throw new InvalidOperationException("Overload invoked."); }
    }
    private class WholeProbe(Socket socket) : NativeProbe(socket)
    {
        protected override Task DoWrite(object msg) { WholeCalls++; return base.DoWrite(msg); }
    }
    private class DerivedWholeProbe(Socket socket) : WholeProbe(socket);
    private class HiddenOverWholeProbe(Socket socket) : WholeProbe(socket)
    {
        protected new Task DoWrite(object msg) { HiddenCalls++; throw new InvalidOperationException("Hidden method invoked."); }
    }
    private class ReoverrideWholeProbe(Socket socket) : WholeProbe(socket)
    {
        protected override Task DoWrite(object msg) => base.DoWrite(msg);
    }
    private class CovariantWholeProbe(Socket socket) : NativeProbe(socket)
    {
        protected override async Task<int> DoWrite(object msg)
        {
            WholeCalls++;
            await base.DoWrite(msg);
            return 0;
        }
    }
    private sealed class Region(byte[] data) : AbstractReferenceCounted, IFileRegion
    {
        private long _transferred;
        internal int Deallocations;
        public long Position() => 0;
        public long Count() => data.Length;
        public long Transferred() => _transferred;
        public long TransferTo(Stream target, long position)
        {
            Assert.Equal(_transferred, position);
            target.WriteByte(data[(int)position]);
            _transferred++;
            return 1;
        }
        public override IReferenceCounted Touch(object hint) => this;
        IFileRegion IFileRegion.Retain() { Retain(); return this; }
        IFileRegion IFileRegion.Retain(int increment) { Retain(increment); return this; }
        IFileRegion IFileRegion.Touch() => this;
        IFileRegion IFileRegion.Touch(object hint) => this;
        protected override void Deallocate() => Deallocations++;
    }
}
