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
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // Run and inspect the I/O pump on its owning thread.
public class SocketGatheringWriteContractTest
{
    [Fact]
    public void APartialVectorAdvancesOnlySentBytesAndDoesNotAdmitNewWrites()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var (peer, socket) = ConnectPair(AddressFamily.InterNetwork);
        using var rawPeer = peer;
        using var rawSocket = socket;
        var channel = new VectorProbe(socket);
        channel.Config.SetWriteSpinCount(1).SetAutoRead(false);
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        try
        {
            loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            channel.SendVector = segments =>
            {
                channel.SendVector = null;
                Assert.Equal(new[] { 2, 2, 2 }, segments.Select(segment => segment.Count));
                var partial = new[] { segments[0], segments[1].Slice(0, 1) };
                return Task.FromResult(socket.Send(partial, SocketFlags.None));
            };
            var a = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
            var b = Unpooled.WrappedBuffer(new byte[] { 3, 4 });
            var c = Unpooled.WrappedBuffer(new byte[] { 5, 6 });
            var first = channel.WriteAsync(a, cancellation);
            var second = channel.WriteAsync(b, cancellation);
            var third = channel.WriteAsync(c, cancellation);
            channel.Flush();

            Assert.True(first.IsCompletedSuccessfully);
            Assert.Equal(0, a.ReferenceCount);
            Assert.Equal(1, b.ReaderIndex);
            Assert.False(second.IsCompleted);
            Assert.False(third.IsCompleted);
            var later = channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 7 }), cancellation);

            PumpUntil(loop, () => second.IsCompleted && third.IsCompleted);
            Assert.True(second.IsCompletedSuccessfully);
            Assert.True(third.IsCompletedSuccessfully);
            Assert.False(later.IsCompleted);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, ReadExactly(peer, 6, cancellation));
            channel.Flush();
            PumpUntil(loop, () => later.IsCompleted);
            Assert.Equal(new byte[] { 7 }, ReadExactly(peer, 1, cancellation));
            Assert.True(channel.VectorCalls >= 2);
        }
        finally { Terminate(loop); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllGatheredBuffersStayBorrowedUntilTheNativeOperationReturns(bool stop)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var (peer, socket) = ConnectPair(AddressFamily.InterNetwork);
        using var rawPeer = peer;
        using var rawSocket = socket;
        var channel = new VectorProbe(socket);
        channel.Config.SetAutoRead(false);
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            channel.SendVector = _ => pending.Task;
            var a = Unpooled.WrappedBuffer(new byte[] { 1 });
            var b = Unpooled.WrappedBuffer(new byte[] { 2 });
            var first = channel.WriteAsync(a, cancellation);
            var second = channel.WriteAndFlushAsync(b, cancellation);
            Assert.Equal(2, a.ReferenceCount);
            Assert.Equal(2, b.ReferenceCount);

            if (stop)
            {
                Terminate(loop);
                Assert.Throws<IOException>(() => first.WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult());
                Assert.Throws<IOException>(() => second.WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult());
                Assert.Equal(1, a.ReferenceCount);
                Assert.Equal(1, b.ReferenceCount);
                pending.SetException(new IOException("Socket closed during gathering."));
                Assert.True(SpinWait.SpinUntil(() => a.ReferenceCount == 0 && b.ReferenceCount == 0, TimeSpan.FromSeconds(2)));
                Assert.Equal(0, loop.RunNow(-1));
            }
            else
            {
                var error = new IOException("Gathered write failed.");
                pending.SetException(error);
                PumpUntil(loop, () => first.IsCompleted && second.IsCompleted);
                Assert.Same(error, Assert.Throws<IOException>(() => first.GetAwaiter().GetResult()));
                Assert.Same(error, Assert.Throws<IOException>(() => second.GetAwaiter().GetResult()));
                Assert.Equal(0, a.ReferenceCount);
                Assert.Equal(0, b.ReferenceCount);
            }
        }
        finally
        {
            pending.TrySetException(new IOException("Test ended."));
            Terminate(loop);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GatheringPreservesDirectBuffersAndFileRegionOrder(bool domain)
    {
        if (domain) DomainSocketSourceTest.RequireDomainSockets();
        var cancellation = TestContext.Current.CancellationToken;
        var (peer, socket) = ConnectPair(domain ? AddressFamily.Unix : AddressFamily.InterNetwork);
        using var rawPeer = peer;
        using var rawSocket = socket;
        SocketStreamChannel channel = domain ? new DomainSocketChannel(socket) : new VectorProbe(socket);
        channel.Config.SetAutoRead(false);
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, new byte[] { 4, 5 });
            loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
            var region = new DefaultFileRegion(new FileInfo(file), 0, 2);
            Task[] writes =
            [
                channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 1 }), cancellation),
                channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 2 }), cancellation),
                channel.WriteAsync(Unpooled.DirectBuffer(1).WriteByte(3), cancellation),
                channel.WriteAsync(region, cancellation),
                channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 6 }), cancellation),
                channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 7 }), cancellation)
            ];
            var sent = Task.WhenAll(writes);
            PumpUntil(loop, () => sent.IsCompleted);
            sent.GetAwaiter().GetResult();
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, ReadExactly(peer, 7, cancellation));
            Assert.Equal(0, region.ReferenceCount);
            if (channel is VectorProbe probe) Assert.Equal(2, probe.VectorCalls);
        }
        finally
        {
            Terminate(loop);
            File.Delete(file);
        }
    }

    private static (Socket Peer, Socket Accepted) ConnectPair(AddressFamily family)
    {
        var protocol = family == AddressFamily.Unix ? ProtocolType.Unspecified : ProtocolType.Tcp;
        using var listener = new Socket(family, SocketType.Stream, protocol);
        listener.Bind(family == AddressFamily.Unix ? DomainSocketSourceTest.NewAddress()
            : new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var peer = new Socket(family, SocketType.Stream, protocol);
        try
        {
            peer.Connect(listener.LocalEndPoint);
            return (peer, listener.Accept());
        }
        catch { peer.Dispose(); throw; }
    }

    private static void PumpUntil(ManualIoEventLoop loop, Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            loop.Run(10_000_000, -1);
            Thread.Yield();
        }
        Assert.True(condition(), "The gathered write did not settle during the I/O phase.");
    }

    private static byte[] ReadExactly(Socket socket, int length, CancellationToken cancellation)
    {
        var data = new byte[length];
        for (int offset = 0; offset < data.Length;)
        {
            int read = socket.ReceiveAsync(data.AsMemory(offset), SocketFlags.None, cancellation).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult();
            Assert.True(read > 0);
            offset += read;
        }
        return data;
    }

    private sealed class VectorProbe(Socket socket) : TcpSocketChannel(socket)
    {
        internal override bool SupportsGatheringWrites => true;
        internal Func<IList<ArraySegment<byte>>, Task<int>> SendVector;
        internal int VectorCalls;

        internal override Task<int> SendGatheredAsync(IList<ArraySegment<byte>> segments)
        {
            VectorCalls++;
            return SendVector?.Invoke(segments) ?? base.SendGatheredAsync(segments);
        }
    }
}
