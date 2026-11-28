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
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental tests of the original AbstractNioByteChannel/NioSocketChannel write quantum.
// The probe stands in for SocketChannel.write(ByteBuffer)/write(ByteBuffer[], int, int) of NioSocketChannel.doWrite.
#pragma warning disable xUnit1031 // Drive the manual event loop on its owning thread.
#pragma warning disable xUnit1051 // Native borrows and cleanup have independent lifetimes.
public class SocketWriteSpinCountContractTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PartialScalarWritesConsumeOneSpinPerNativeCall(int writeSpinCount)
    {
        using var fixture = new Fixture(writeSpinCount);
        var buf = Unpooled.Buffer(6).WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6 });
        var write = fixture.Channel.WriteAndFlushAsync(buf);
        Assert.Equal(writeSpinCount, fixture.Channel.Calls);
        Assert.Equal(writeSpinCount, buf.ReaderIndex);
        Assert.False(write.IsCompleted);
        Assert.Equal(1, buf.RefCnt);
        fixture.PumpUntil(() => write.IsCompleted);
        write.GetAwaiter().GetResult();
        Assert.Equal(6, fixture.Channel.Calls);
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, fixture.Receive(6));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TheWriteQuantumSpansSeveralBuffers(int writeSpinCount)
    {
        using var fixture = new Fixture(writeSpinCount);
        var writes = Enumerable.Range(1, 4).Select(value => fixture.Channel.WriteAsync(Unpooled.Buffer(1).WriteByte(value))).ToArray();
        fixture.Channel.Flush();
        Assert.Equal(writeSpinCount, fixture.Channel.Calls);
        Assert.All(writes.Take(writeSpinCount), write => Assert.True(write.IsCompletedSuccessfully));
        Assert.All(writes.Skip(writeSpinCount), write => Assert.False(write.IsCompleted));
        fixture.PumpUntil(() => writes.All(write => write.IsCompleted));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, fixture.Receive(4));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptyBuffersDoNotConsumeANativeWriteSpin(int writeSpinCount)
    {
        using var fixture = new Fixture(writeSpinCount);
        var buffers = new[] { Unpooled.Buffer(1), Unpooled.Buffer(1).WriteByte(1), Unpooled.Buffer(1),
            Unpooled.Buffer(1).WriteByte(2), Unpooled.Buffer(1), Unpooled.Buffer(1).WriteByte(3) };
        var writes = buffers.Select(buf => fixture.Channel.WriteAsync(buf)).ToArray();
        fixture.Channel.Flush();
        Assert.Equal(writeSpinCount, fixture.Channel.Calls);
        fixture.PumpUntil(() => writes.All(write => write.IsCompleted));
        Assert.All(writes, write => Assert.True(write.IsCompletedSuccessfully));
        Assert.All(buffers, buf => Assert.Equal(0, buf.RefCnt));
        Assert.Equal(3, fixture.Channel.Calls);
        Assert.Equal(new byte[] { 1, 2, 3 }, fixture.Receive(3));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void GatheringAndScalarWritesUseTheSameQuantum(int writeSpinCount)
    {
        using var fixture = new Fixture(writeSpinCount);
        fixture.Channel.Gather = true;
        var writes = Enumerable.Range(1, 4).Select(value => fixture.Channel.WriteAsync(Unpooled.Buffer(1).WriteByte(value))).ToArray();
        fixture.Channel.Flush();
        Assert.Equal(writeSpinCount, fixture.Channel.Calls);
        Assert.True(writes[0].IsCompletedSuccessfully);
        Assert.True(writes[1].IsCompletedSuccessfully);
        Assert.Equal(writeSpinCount == 2, writes[2].IsCompletedSuccessfully);
        Assert.False(writes[3].IsCompleted);
        fixture.PumpUntil(() => writes.All(write => write.IsCompleted));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, fixture.Receive(4));
    }

    [Fact]
    public void NativeFailuresPreserveTheOriginalException()
    {
        using var fixture = new Fixture(1);
        var error = new IOException("Native scalar send failed.");
        fixture.Channel.Failure = error;
        var buf = Unpooled.Buffer(1).WriteByte(1);
        var write = fixture.Channel.WriteAndFlushAsync(buf);
        fixture.PumpUntil(() => write.IsCompleted);
        Assert.Same(error, Assert.Throws<IOException>(() => write.GetAwaiter().GetResult()));
        Assert.Equal(0, buf.RefCnt);
        Assert.False(fixture.Channel.IsOpen);
    }

    [Fact]
    public void AProgressObserverCanCloseWithoutTouchingAReleasedBuffer()
    {
        using var fixture = new Fixture(1);
        fixture.Channel.Config.WriteProgress = new Observer(value =>
        {
            _ = fixture.Channel.CloseAsync();
        });
        var buf = Unpooled.Buffer(3).WriteZero(3);
        var write = fixture.Channel.WriteAndFlushAsync(buf);
        fixture.PumpUntil(() => write.IsCompleted);
        Assert.ThrowsAny<ClosedChannelException>(() => write.GetAwaiter().GetResult());
        Assert.Equal(1, fixture.Channel.Calls);
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(new byte[] { 0 }, fixture.Receive(1));
    }

    private sealed class Observer(Action<ChannelWriteProgress> report) : IProgress<ChannelWriteProgress>
    {
        public void Report(ChannelWriteProgress value)
        {
            report(value);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly ManualIoEventLoop Loop = new(Thread.CurrentThread, NioIoHandler.NewFactory());
        internal readonly Probe Channel;
        private readonly Socket _peer;
        internal Fixture(int writeSpinCount)
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
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

                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Scalar write did not reach its owner.");
                Loop.RunNow();
                Thread.Sleep(1);
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

    // Accepts one byte per non-blocking write call, or the first two buffers of one gathered call.
    private sealed class Probe(Socket socket) : NioSocketChannel(socket)
    {
        internal int Calls;
        internal bool Gather;
        internal Exception Failure;

        internal override int WriteSocket(ReadOnlyMemory<byte> buffer)
        {
            Calls++;
            if (Failure != null)
            {
                throw Failure;
            }

            return JavaChannel().Send(buffer[..1].Span, SocketFlags.None);
        }

        internal override long WriteSocket(ReadOnlyMemory<byte>[] buffers, int offset, int length)
        {
            Calls++;
            if (Failure != null)
            {
                throw Failure;
            }

            if (Gather)
            {
                Gather = false;
                return base.WriteSocket(buffers, offset, Math.Min(2, length));
            }

            return JavaChannel().Send(buffers[offset][..1].Span, SocketFlags.None);
        }
    }
}
