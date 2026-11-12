using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental CLR producer tests. Original ChannelOutboundBuffer/NioSocketChannel fixtures remain separate.
#pragma warning disable xUnit1031 // The manual I/O loop is pumped only by its owning thread.
public class SocketGatheringWriteBudgetContractTest
{
    [Theory]
    [InlineData(8192, 8192, 1024, 16384)]
    [InlineData(8192, 1024, 16384, 4096)]
    [InlineData(4096, 1024, 16384, 16384)]
    [InlineData(4097, 1024, 16384, 2048)]
    [InlineData(8192, 4096, 16384, 16384)]
    [InlineData(100, 100, 4096, 4096)]
    public void ScalarResultsDetermineTheNextGatheringBudget(int attempted, int written,
        int oldMaxBytesPerGatheringWrite, int expected)
    {
        using var fixture = new Fixture();
        fixture.Channel.SetMaxBytesPerGatheringWrite(oldMaxBytesPerGatheringWrite);
        fixture.Channel.Written = written;
        var buf = Unpooled.Buffer(attempted).WriteZero(attempted);
        Task future = fixture.Channel.WriteAndFlushAsync(buf, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { attempted }, fixture.Channel.Attempts.Single());
        Assert.Equal(expected, fixture.Channel.GetMaxBytesPerGatheringWrite());
        if (attempted == written)
        {
            Assert.True(future.IsCompletedSuccessfully);
            Assert.Equal(0, buf.ReferenceCount);
        }
        else
        {
            Assert.False(future.IsCompleted);
            Assert.Equal(written, buf.ReaderIndex);
            Assert.Equal(1, buf.ReferenceCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFirstMessageMayExceedTheHintWithoutSplittingItsSegments(bool composite)
    {
        using var fixture = new Fixture();
        fixture.Channel.SetMaxBytesPerGatheringWrite(512);
        ByteBuf buf = composite
            ? Unpooled.CompositeBuffer().AddComponent(Unpooled.Buffer(4000).WriteZero(4000), true)
                .AddComponent(Unpooled.Buffer(4000).WriteZero(4000), true)
            : Unpooled.Buffer(8000).WriteZero(8000);
        Task future = fixture.Channel.WriteAndFlushAsync(buf, TestContext.Current.CancellationToken);

        Assert.Equal(composite ? new[] { 4000, 4000 } : new[] { 8000 }, fixture.Channel.Attempts.Single());
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Equal(16000, fixture.Channel.GetMaxBytesPerGatheringWrite());
        Assert.Equal(0, buf.ReferenceCount);
    }

    [Fact]
    public void LaterMessagesAreExcludedWholeWhenTheyWouldExceedTheHint()
    {
        using var fixture = new Fixture();
        fixture.Channel.SetMaxBytesPerGatheringWrite(1500);
        var buf = Unpooled.Buffer(1024).WriteZero(1024);
        var comp = Unpooled.CompositeBuffer().AddComponent(Unpooled.Buffer(400).WriteZero(400), true)
            .AddComponent(Unpooled.Buffer(400).WriteZero(400), true);
        Task f1 = fixture.Channel.WriteAsync(buf, TestContext.Current.CancellationToken);
        Task f2 = fixture.Channel.WriteAndFlushAsync(comp, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 1024 }, fixture.Channel.Attempts.Single());
        Assert.True(f1.IsCompletedSuccessfully);
        Assert.False(f2.IsCompleted);
        Assert.Equal(0, comp.ReaderIndex);
        Assert.Equal(1, comp.ReferenceCount);
        Assert.Equal(0, buf.ReferenceCount);
    }

    [Fact]
    public void APartialVectorUsesTheFullAttemptToReduceTheBudget()
    {
        using var fixture = new Fixture();
        fixture.Channel.SetMaxBytesPerGatheringWrite(16384);
        fixture.Channel.Written = 1024;
        var buf = Unpooled.Buffer(4096).WriteZero(4096);
        var buf2 = Unpooled.Buffer(4096).WriteZero(4096);
        Task f1 = fixture.Channel.WriteAsync(buf, TestContext.Current.CancellationToken);
        Task f2 = fixture.Channel.WriteAndFlushAsync(buf2, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 4096, 4096 }, fixture.Channel.Attempts.Single());
        Assert.Equal(4096, fixture.Channel.GetMaxBytesPerGatheringWrite());
        Assert.Equal(1024, buf.ReaderIndex);
        Assert.Equal(0, buf2.ReaderIndex);
        Assert.False(f1.IsCompleted);
        Assert.False(f2.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExplicitSendBufferChangeWinsOverAnOlderPendingCompletion(bool vector)
    {
        using var fixture = new Fixture();
        fixture.Channel.SetMaxBytesPerGatheringWrite(512);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Channel.Pending = completion.Task;
        if (vector) fixture.Channel.WriteAsync(Unpooled.Buffer(256).WriteZero(256), TestContext.Current.CancellationToken);
        Task future = fixture.Channel.WriteAndFlushAsync(Unpooled.Buffer(256).WriteZero(256), TestContext.Current.CancellationToken);
        Assert.False(future.IsCompleted);
        fixture.Channel.Config.SetOption(ChannelOption.SO_SNDBUF, 8192);
        int expected = fixture.Channel.GetMaxBytesPerGatheringWrite();
        Assert.True(expected > 0);
        Assert.NotEqual(512, expected);
        completion.SetResult(vector ? 512 : 256);
        fixture.PumpUntil(() => future.IsCompleted);
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Equal(expected, fixture.Channel.GetMaxBytesPerGatheringWrite());
    }

    [Theory]
    [InlineData(1073741823, 1073741823, 100, 2147483646)]
    [InlineData(1073741824, 1073741824, 100, 100)]
    [InlineData(2147483647, 2147483647, 100, 100)]
    [InlineData(2147483647, 1, 2147483647, 1073741823)]
    public void AdaptiveArithmeticRetainsTheOriginalSignedIntBoundaries(int attempted, int written,
        int oldMaxBytesPerGatheringWrite, int expected)
    {
        using var fixture = new Fixture();
        fixture.Channel.SetMaxBytesPerGatheringWrite(oldMaxBytesPerGatheringWrite);
        // No multi-gigabyte allocation: invoke the same private decision with source-verified byte counts.
        typeof(SocketStreamChannel).GetMethod("AdjustMaxBytesPerGatheringWrite", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Channel, new object[] { attempted, written, oldMaxBytesPerGatheringWrite });
        Assert.Equal(expected, fixture.Channel.GetMaxBytesPerGatheringWrite());
    }

    [Fact]
    public void SendBufferConfigurationRecalculatesTheBudgetFromTheNativeValue()
    {
        using var fixture = new Fixture();
        Assert.Equal(unchecked(fixture.Channel.NativeSendBufferSize << 1), fixture.Channel.GetMaxBytesPerGatheringWrite());
        fixture.Channel.SetMaxBytesPerGatheringWrite(100);
        fixture.Channel.Config.SetOption(ChannelOption.SO_SNDBUF, 4096);
        Assert.Equal(unchecked(fixture.Channel.NativeSendBufferSize << 1), fixture.Channel.GetMaxBytesPerGatheringWrite());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Socket _peer;
        internal readonly Probe Channel;
        private readonly ManualIoEventLoop _loop = new(Thread.CurrentThread);

        internal Fixture()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            _peer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _peer.Connect(listener.LocalEndPoint);
            Channel = new Probe(listener.Accept());
            Channel.Config.SetAutoRead(false).SetWriteSpinCount(1);
            _loop.RegisterAsync(Channel, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }

        internal void PumpUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "The native write completion did not reach its owner.");
                _loop.RunNow();
                Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            ManualIoEventLoopSourceTest.Terminate(_loop);
            _peer.Dispose();
        }
    }

    private sealed class Probe(Socket socket) : TcpSocketChannel(socket)
    {
        internal readonly List<int[]> Attempts = new();
        internal int? Written;
        internal Task<int> Pending;
        internal int NativeSendBufferSize => Socket.SendBufferSize;
        internal override bool SupportsGatheringWrites => true;
        protected override Task<int> DoWriteBytes(ByteBuf buf)
        {
            int attemptedBytes = buf.ReadableSequence.First.Length;
            Attempts.Add(new[] { attemptedBytes });
            return Pending ?? Task.FromResult(Written ?? attemptedBytes);
        }
        internal override Task<int> SendGatheredAsync(IList<ArraySegment<byte>> segments)
        {
            Attempts.Add(segments.Select(segment => segment.Count).ToArray());
            return Pending ?? Task.FromResult(Written ?? segments.Sum(segment => segment.Count));
        }
    }
}
