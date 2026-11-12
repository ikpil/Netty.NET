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

namespace Netty.NET.Transport.Tests;

// Supplemental tests of the original AbstractNioByteChannel/NioSocketChannel write quantum.
// DoWrite0/DoWriteInternal/DoWriteBytes and their mapped identifiers retain the Java names.
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
        Assert.Equal(1, buf.ReferenceCount);
        fixture.PumpUntil(() => write.IsCompleted);
        write.GetAwaiter().GetResult();
        Assert.Equal(6, fixture.Channel.Calls);
        Assert.Equal(0, buf.ReferenceCount);
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
        Assert.All(buffers, buf => Assert.Equal(0, buf.ReferenceCount));
        Assert.Equal(3, fixture.Channel.Calls);
        Assert.Equal(new byte[] { 1, 2, 3 }, fixture.Receive(3));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void ConfigIsSnapshottedAtTheStartOfTheWritePass(int writeSpinCount, int nextWriteSpinCount)
    {
        using var fixture = new Fixture(writeSpinCount);
        fixture.Channel.BeforeWrite = () =>
        {
            if (fixture.Channel.Calls == 1) fixture.Channel.Config.SetWriteSpinCount(nextWriteSpinCount);
        };
        var write = fixture.Channel.WriteAndFlushAsync(Unpooled.Buffer(6).WriteZero(6));
        Assert.Equal(writeSpinCount, fixture.Channel.Calls);
        fixture.Loop.RunNow(-1);
        Assert.Equal(writeSpinCount + nextWriteSpinCount, fixture.Channel.Calls);
        fixture.PumpUntil(() => write.IsCompleted);
        write.GetAwaiter().GetResult();
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    public void InvalidNativeResultsFailTheWriteAndReleaseItsBuffer(int localFlushedAmount)
    {
        using var fixture = new Fixture(1);
        fixture.Channel.Write = _ => Task.FromResult(localFlushedAmount);
        var buf = Unpooled.Buffer(1).WriteByte(1);
        var write = fixture.Channel.WriteAndFlushAsync(buf);
        fixture.PumpUntil(() => write.IsCompleted);
        var error = Assert.Throws<IOException>(() => write.GetAwaiter().GetResult());
        Assert.Contains("invalid scalar write length", error.Message);
        Assert.Equal(0, buf.ReferenceCount);
        Assert.False(fixture.Channel.IsOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeFailuresPreserveTheOriginalExceptionAndBorrowLifetime(bool pending)
    {
        using var fixture = new Fixture(1);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new IOException("Native scalar send failed.");
        fixture.Channel.Write = _ => pending ? completion.Task : Task.FromException<int>(error);
        var buf = Unpooled.Buffer(1).WriteByte(1);
        try
        {
            var write = fixture.Channel.WriteAndFlushAsync(buf);
            if (pending)
            {
                Assert.False(write.IsCompleted);
                Assert.Equal(2, buf.ReferenceCount);
                completion.SetException(error);
            }
            fixture.PumpUntil(() => write.IsCompleted);
            Assert.Same(error, Assert.Throws<IOException>(() => write.GetAwaiter().GetResult()));
            Assert.Equal(0, buf.ReferenceCount);
        }
        finally { completion.TrySetException(error); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingKeepsTheBorrowUntilTheNativeProducerReturns(bool stop)
    {
        using var fixture = new Fixture(1);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Channel.Write = _ => completion.Task;
        var buf = Unpooled.Buffer(1).WriteByte(1);
        var write = fixture.Channel.WriteAndFlushAsync(buf);
        try
        {
            Assert.Equal(2, buf.ReferenceCount);
            if (stop) ManualIoEventLoopSourceTest.Terminate(fixture.Loop);
            else
            {
                var close = fixture.Channel.CloseAsync();
                fixture.PumpUntil(() => close.IsCompleted);
                close.GetAwaiter().GetResult();
            }
            Assert.Throws<IOException>(() => write.GetAwaiter().GetResult());
            Assert.Equal(1, buf.ReferenceCount);
            completion.SetResult(1);
            Assert.True(SpinWait.SpinUntil(() => buf.ReferenceCount == 0, TimeSpan.FromSeconds(3)));
        }
        finally { completion.TrySetResult(1); }
    }

    [Fact]
    public void AProgressObserverCanCloseWithoutTouchingAReleasedBuffer()
    {
        using var fixture = new Fixture(1);
        fixture.Channel.Config.WriteProgress = new Observer(value => { _ = fixture.Channel.CloseAsync(); });
        var buf = Unpooled.Buffer(3).WriteZero(3);
        var write = fixture.Channel.WriteAndFlushAsync(buf);
        fixture.PumpUntil(() => write.IsCompleted);
        Assert.Throws<IOException>(() => write.GetAwaiter().GetResult());
        Assert.Equal(1, fixture.Channel.Calls);
        Assert.Equal(0, buf.ReferenceCount);
        Assert.Equal(new byte[] { 0 }, fixture.Receive(1));
    }

    private sealed class Observer(Action<ChannelWriteProgress> report) : IProgress<ChannelWriteProgress>
    {
        public void Report(ChannelWriteProgress value) => report(value);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly ManualIoEventLoop Loop = new(Thread.CurrentThread);
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
        public void Dispose() { ManualIoEventLoopSourceTest.Terminate(Loop); _peer.Dispose(); }
    }

    private sealed class Probe(Socket socket) : TcpSocketChannel(socket)
    {
        internal int Calls;
        internal bool Gather;
        internal Action BeforeWrite;
        internal Func<ByteBuf, Task<int>> Write;
        internal override bool SupportsGatheringWrites => true;
        internal override Task<int> TryGatherWriteAsync() => Gather ? base.TryGatherWriteAsync() : null;
        protected override Task<int> DoWriteBytes(ByteBuf buf)
        {
            Calls++;
            BeforeWrite?.Invoke();
            return Write?.Invoke(buf) ?? Task.FromResult(Socket.Send(buf.ReadableSequence.First.Span[..1], SocketFlags.None));
        }
        internal override Task<int> SendGatheredAsync(IList<ArraySegment<byte>> segments)
        {
            Calls++;
            Gather = false;
            return Task.FromResult(Socket.Send(segments.Take(2).ToList(), SocketFlags.None));
        }
    }
}
