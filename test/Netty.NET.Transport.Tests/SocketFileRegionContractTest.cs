using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental contracts from AbstractNioByteChannel.doWriteInternal and NioSocketChannel.doWriteFileRegion,
// plus the explicit lifetime of the CLR's offloaded synchronous Stream fallback.
#pragma warning disable xUnit1051 // Native file producers and cleanup keep independent lifetimes.
public class SocketFileRegionContractTest
{
    [Fact(Timeout = 10000)]
    public async Task AnAlreadyTransferredRegionDoesNotTransferAgain()
    {
        await using var socket = await Connected.Open();
        var region = new Region(4, 4, (_, _, _) => throw new IOException("already complete"));
        await socket.Channel.WriteAndFlushAsync(region, TestContext.Current.CancellationToken);
        Assert.Empty(region.Positions);
        Assert.Equal(0, region.RefCnt);
    }

    [Fact(Timeout = 10000)]
    public async Task APartlyTransferredRegionResumesAtItsOwnRelativeOffset()
    {
        await using var socket = await Connected.Open();
        var region = new Region(4, 2, (self, target, position) =>
        {
            if (position != 2)
            {
                throw new IOException("incorrect resume offset");
            }
            target.Write(new byte[] { 3, 4 }); self.TransferredBytes = 4; return 2;
        });
        await socket.Channel.WriteAndFlushAsync(region, TestContext.Current.CancellationToken);
        var actual = new byte[2];
        await socket.Peer.GetStream().ReadExactlyAsync(actual, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 3, 4 }, actual);
        Assert.Equal(new long[] { 2 }, region.Positions);
        Assert.Equal(0, region.RefCnt);
    }

    [Fact(Timeout = 10000)]
    public async Task LogicalCompletionMayAdvanceWithoutWritingAnotherTargetByte()
    {
        await using var socket = await Connected.Open();
        var region = new Region(16, 0, (self, _, _) => { self.TransferredBytes = 16; return 0; });
        await socket.Channel.WriteAndFlushAsync(region, TestContext.Current.CancellationToken);
        Assert.Equal(new long[] { 0 }, region.Positions);
        Assert.Equal(0, region.RefCnt);
    }

    [Fact(Timeout = 10000)]
    public async Task ZeroProgressWaitsForAnotherWriteTurnAndCloseReleasesTheQueue()
    {
        await using var socket = await Connected.Open();
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        var region = new Region(1, 0, (_, _, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 2)
            {
                retried.TrySetResult();
            }
            return 0;
        });
        Task write = socket.Channel.WriteAndFlushAsync(region, TestContext.Current.CancellationToken);
        try
        {
            await retried.Task.WaitAsync(TestContext.Current.CancellationToken);
            await socket.Channel.EventLoop.SubmitAsync(static () => { }, TestContext.Current.CancellationToken);
            Assert.False(write.IsCompleted);
            Assert.True(socket.Channel.IsOpen);
        }
        finally
        {
            await socket.Channel.CloseAsync();
        }
        await Assert.ThrowsAnyAsync<ClosedChannelException>(() => write);
        await region.Deallocated.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(region.Positions.Count >= 2);
        Assert.Equal(0, region.RefCnt);
    }

    [Fact(Timeout = 10000)]
    public async Task AProducerExceptionKeepsItsIdentityAndReleasesTheNativeBorrow()
    {
        await using var socket = await Connected.Open();
        var failure = new IOException("producer failure");
        var region = new Region(1, 0, (_, _, _) => throw failure);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => socket.Channel.WriteAndFlushAsync(region, TestContext.Current.CancellationToken)));
        Assert.Equal(0, region.RefCnt);
        Assert.True(region.Deallocated.Task.IsCompleted);
        Assert.False(socket.Channel.IsOpen);
    }

    [Fact(Timeout = 15000)]
    public async Task AnOwnedNonblockingSocketTransfersAFileThroughBackpressure()
    {
        await using var socket = await Connected.Open(true);
        string path = Path.GetTempFileName();
        var data = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(data);
        try
        {
            await File.WriteAllBytesAsync(path, data, TestContext.Current.CancellationToken);
            var region = new DefaultFileRegion(new FileInfo(path), 0, data.Length);
            Task write = socket.Channel.WriteAndFlushAsync(region, TestContext.Current.CancellationToken);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(write.IsCompleted);
            var actual = new byte[data.Length];
            await socket.Peer.GetStream().ReadExactlyAsync(actual, TestContext.Current.CancellationToken);
            await write;
            Assert.Equal(data, actual);
            Assert.Equal(data.Length, region.Transferred());
            Assert.Equal(0, region.RefCnt);
            Assert.False(socket.OwnedSocket.Blocking);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task ClosingANonblockingBackpressuredTransferEndsItsNativeBorrow()
    {
        await using var socket = await Connected.Open(true, true);
        var chunk = new byte[64 * 1024];
        // A long, chunked producer cannot finish in the close race even if the loopback stack buffers eagerly.
        var region = new Region(1L << 40, 0, (self, target, _) =>
        { target.Write(chunk); self.TransferredBytes += chunk.Length; return chunk.Length; });
        Task write = socket.Channel.WriteAndFlushAsync(region, TestContext.Current.CancellationToken);
        var firstByte = new byte[1];
        await socket.Peer.GetStream().ReadExactlyAsync(firstByte, TestContext.Current.CancellationToken);
        Assert.False(write.IsCompleted);
        await socket.Channel.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<ClosedChannelException>(() => write);
        await region.Deallocated.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, region.RefCnt);
    }

    private sealed class Region(long count, long initialTransferred, Func<Region, Stream, long, long> transfer) : AbstractReferenceCounted, IFileRegion
    {
        internal long TransferredBytes = initialTransferred;
        internal readonly ConcurrentQueue<long> Positions = new();
        internal readonly TaskCompletionSource Deallocated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Position()
        {
            return 0;
        }

        public long Count()
        {
            return count;
        }

        public long Transferred()
        {
            return TransferredBytes;
        }

        public long TransferTo(Stream target, long position)
        {
            Positions.Enqueue(position);
            return transfer(this, target, position);
        }

        protected override void Deallocate()
        {
            Deallocated.TrySetResult();
        }

        public new IFileRegion Retain()
        {
            base.Retain();
            return this;
        }

        public override IFileRegion Retain(int increment)
        {
            base.Retain(increment);
            return this;
        }

        public new IFileRegion Touch()
        {
            return this;
        }

        public override IFileRegion Touch(object hint)
        {
            return this;
        }
    }
    private sealed class Connected : IAsyncDisposable
    {
        private readonly ShutdownFixture _fixture = new(false);
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        internal NioSocketChannel Channel;
        internal TcpClient Peer;
        internal Socket OwnedSocket;
        internal static async Task<Connected> Open(bool nonblocking = false, bool limitedReceiveBuffer = false)
        {
            var connected = new Connected();
            if (limitedReceiveBuffer)
            {
                connected._listener.Server.ReceiveBufferSize = 1024;
            }
            connected._listener.Start();
            try
            {
                var bootstrap = connected._fixture.Client;
                if (nonblocking)
                {
                    connected.OwnedSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false, SendBufferSize = 1024 };
                    var channel = new NioSocketChannel(connected.OwnedSocket);
                    bootstrap = new Bootstrap().Group(bootstrap.Config.Group).ChannelFactory(() => channel)
                        .Option(ChannelOption.ALLOCATOR, (IByteBufAllocator)bootstrap.Config.Options[ChannelOption.ALLOCATOR]);
                }
                connected.Channel = (NioSocketChannel)await bootstrap.Handler(new ChannelInboundHandlerAdapter())
                    .ConnectAsync(connected._listener.LocalEndpoint, cancellationToken: TestContext.Current.CancellationToken);
                connected.Peer = await connected._listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                return connected;
            }
            catch
            {
                await connected.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Channel != null)
                await Channel.CloseAsync();
            else
                OwnedSocket?.Dispose();
            Peer?.Dispose();
            _listener.Stop();
            await _fixture.DisposeAsync();
        }
    }
}
#pragma warning restore xUnit1051
