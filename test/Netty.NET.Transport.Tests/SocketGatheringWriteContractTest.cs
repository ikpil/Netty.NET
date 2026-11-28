using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental tests of the gathering NioSocketChannel.doWrite(ChannelOutboundBuffer) over a non-blocking Socket.
#pragma warning disable xUnit1031 // Drive the manual event loop on its owning thread.
#pragma warning disable xUnit1051 // Native sockets and the loop have independent lifetimes.
public class SocketGatheringWriteContractTest
{
    [Fact]
    public void APartialVectorAdvancesOnlySentBytesAndDoesNotAdmitNewWrites()
    {
        using var fixture = new StreamFixture<VectorProbe>(socket => new VectorProbe(socket));
        fixture.Channel.Budget = 3;
        var first = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        var second = Unpooled.WrappedBuffer(new byte[] { 3, 4 });
        Task f1 = fixture.Channel.WriteAsync(first);
        Task f2 = fixture.Channel.WriteAndFlushAsync(second);
        // in.removeBytes(3) completes the first buffer and advances the second one by a single byte.
        Assert.Equal(1, fixture.Channel.Vectors);
        Assert.True(f1.IsCompletedSuccessfully);
        Assert.False(f2.IsCompleted);
        Assert.Equal(1, second.ReaderIndex);
        Task late = fixture.Channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 5 }));
        fixture.Channel.Budget = int.MaxValue;
        fixture.PumpUntil(() => f2.IsCompleted);
        Assert.True(f2.IsCompletedSuccessfully);
        Assert.False(late.IsCompleted);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, fixture.Receive(4));
        fixture.Channel.Flush();
        fixture.PumpUntil(() => late.IsCompleted);
        Assert.Equal(new byte[] { 5 }, fixture.Receive(1));
    }

    [Fact]
    public void GatheringPreservesBufferAndFileRegionOrder()
    {
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, new byte[] { 10, 11, 12, 13 });
            using var fixture = new StreamFixture<VectorProbe>(socket => new VectorProbe(socket));
            Task a = fixture.Channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 1, 2 }));
            Task b = fixture.Channel.WriteAsync(new DefaultFileRegion(new FileInfo(path), 1, 2));
            Task c = fixture.Channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 3 }));
            fixture.PumpUntil(() => a.IsCompleted && b.IsCompleted && c.IsCompleted);
            Assert.All(new[] { a, b, c }, task => Assert.True(task.IsCompletedSuccessfully));
            Assert.Equal(new byte[] { 1, 2, 11, 12, 3 }, fixture.Receive(5));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Accepts at most Budget bytes in total, as a socket whose send buffer fills up would.
    internal sealed class VectorProbe(Socket socket) : NioSocketChannel(socket)
    {
        internal int Budget = int.MaxValue;
        internal int Vectors;

        internal override int WriteSocket(ReadOnlyMemory<byte> buffer)
        {
            var part = buffer[..Math.Min(Budget, buffer.Length)];
            Budget -= part.Length;
            return part.IsEmpty ? 0 : base.WriteSocket(part);
        }

        internal override long WriteSocket(ReadOnlyMemory<byte>[] buffers, int offset, int length)
        {
            Vectors++;
            var prefix = buffers.Skip(offset).Take(length).Select(buffer =>
            {
                var part = buffer[..Math.Min(Budget, buffer.Length)];
                Budget -= part.Length;
                return part;
            }).Where(part => !part.IsEmpty).ToArray();
            return prefix.Length == 0 ? 0 : base.WriteSocket(prefix, 0, prefix.Length);
        }
    }
}

// A connected NioSocketChannel driven by a ManualIoEventLoop with NioIoHandler and its raw peer socket.
internal sealed class StreamFixture<T> : IDisposable where T : NioSocketChannel
{
    internal readonly ManualIoEventLoop Loop = new(Thread.CurrentThread, NioIoHandler.NewFactory());
    internal readonly T Channel;
    private readonly Socket _peer;

    internal StreamFixture(Func<Socket, T> factory)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        _peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _peer.Connect(listener.LocalEndPoint);
        Channel = factory(listener.Accept());
        Channel.Config.SetAutoRead(false);
        Loop.RegisterAsync(Channel).GetAwaiter().GetResult();
    }

    internal void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The write did not complete on its owner.");
            Loop.Run(1_000_000);
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

    public void Dispose()
    {
        ManualIoEventLoopSourceTest.Terminate(Loop);
        _peer.Dispose();
    }
}
