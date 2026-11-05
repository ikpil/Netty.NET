using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.ManualIoEventLoopSourceTest;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // The tests drive the pump from the same physical owner thread.
public class DatagramReadBatchContractTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadBudgetAppliesToQueuedPacketsIncludingEmptyDatagrams(bool autoRead)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new DatagramChannel();
        channel.Config.SetAutoRead(autoRead);
#pragma warning disable CS0612, CS0618 // Match the source option for the message budget.
        channel.Config.SetOption(ChannelOption.MAX_MESSAGES_PER_READ, 2);
#pragma warning restore CS0612, CS0618
        var capture = new Capture();
        using var sender = NewSender();
        try
        {
            Bind(loop, channel, capture, cancellation);
            Send(sender, channel, 1);
            Assert.Equal(0, sender.SendTo(Array.Empty<byte>(), channel.LocalAddress));
            Send(sender, channel, 2);
            Send(sender, channel, 3);
            Send(sender, channel, 4);

            if (autoRead)
                PumpUntil(loop, () => capture.Batches.Count == 3);
            else
            {
                for (int read = 1; read <= 5; read++)
                {
                    channel.Read();
                    int count = read;
                    PumpUntil(loop, () => capture.Batches.Count == count);
                    Assert.Equal(0, loop.Run(10_000_000, -1));
                    Assert.Equal(count, capture.Messages.Count);
                }
            }
            Assert.Equal(new int?[] { 1, null, 2, 3, 4 }, capture.Messages);
            Assert.Equal(autoRead ? new[] { 2, 2, 1 } : new[] { 1, 1, 1, 1, 1 }, capture.Batches);
        }
        finally { Terminate(loop); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialBatchPrecedesCompletionAndError(bool fatal)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        Exception error = fatal ? new IOException("receive failed") : new SocketException((int)SocketError.ConnectionReset);
        var channel = new FailingSecondReceive(error);
        var capture = new Capture();
        using var sender = NewSender();
        try
        {
            Bind(loop, channel, capture, cancellation);
            Send(sender, channel, 1);
            Send(sender, channel, 2);
            PumpUntil(loop, () => capture.Failure != null);

            Assert.Equal(new[] { "read", "complete", "error" }, capture.Events);
            Assert.Same(error, capture.Failure);
            Assert.Equal(!fatal, channel.IsOpen);
            if (!fatal)
            {
                PumpUntil(loop, () => capture.Batches.Count == 2);
                Assert.Equal(new int?[] { 1, 2 }, capture.Messages);
                Assert.Equal(new[] { "read", "complete", "error", "read", "complete" }, capture.Events);
            }
        }
        finally { Terminate(loop); }
    }

    [Fact]
    public void AnIncompleteFollowupDoesNotDelayTheBatchOrBypassManualRead()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        var channel = new DeferredSecondReceive();
        var capture = new Capture();
        using var sender = NewSender();
        try
        {
            Bind(loop, channel, capture, cancellation);
            Send(sender, channel, 1);
            Send(sender, channel, 2);
            PumpUntil(loop, () => capture.Batches.Count == 1);
            Assert.True(channel.SecondStarted);
            Assert.Equal(new int?[] { 1 }, capture.Messages);

            channel.Config.SetAutoRead(false);
            channel.ContinueSecond.SetResult();
            channel.SecondReceived.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellation).GetAwaiter().GetResult();
            Assert.Equal(0, loop.Run(10_000_000, -1));
            Assert.Single(capture.Messages);

            channel.Read();
            PumpUntil(loop, () => capture.Batches.Count == 2);
            Assert.Equal(new int?[] { 1, 2 }, capture.Messages);
            Assert.Equal(new[] { 1, 1 }, capture.Batches);
        }
        finally
        {
            channel.ContinueSecond.TrySetResult();
            Terminate(loop);
        }
    }

    private static void Bind(ManualIoEventLoop loop, DatagramChannel channel, Capture capture, CancellationToken cancellation)
    {
        channel.Pipeline.AddLast(capture);
        loop.RegisterAsync(channel, cancellation).GetAwaiter().GetResult();
        channel.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellation).GetAwaiter().GetResult();
    }

    private static Socket NewSender() => new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private static void Send(Socket sender, IChannel channel, byte value) =>
        Assert.Equal(1, sender.SendTo(new[] { value }, channel.LocalAddress));

    private static void PumpUntil(ManualIoEventLoop loop, Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(3)) loop.Run(10_000_000, -1);
        Assert.True(condition(), "The UDP read did not settle on its owner.");
    }

    private sealed class Capture : SimpleChannelInboundHandler<DatagramPacket>
    {
        internal readonly List<int?> Messages = new();
        internal readonly List<int> Batches = new();
        internal readonly List<string> Events = new();
        internal Exception Failure;
        private int _batchSize;

        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket packet)
        {
            Messages.Add(packet.Content.IsReadable ? packet.Content.ReadByte() : null);
            _batchSize++;
            Events.Add("read");
        }

        public override void ChannelReadComplete(ChannelHandlerContext context)
        {
            Batches.Add(_batchSize);
            _batchSize = 0;
            Events.Add("complete");
        }

        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        {
            Failure = cause;
            Events.Add("error");
        }
    }

    private sealed class FailingSecondReceive(Exception error) : DatagramChannel
    {
        private int _attempts;
        protected override Task<SocketReceiveFromResult> ReceiveDatagramAsync(Memory<byte> memory, EndPoint remote) =>
            ++_attempts == 2 ? Task.FromException<SocketReceiveFromResult>(error) : base.ReceiveDatagramAsync(memory, remote);
    }

    private sealed class DeferredSecondReceive : DatagramChannel
    {
        internal readonly TaskCompletionSource ContinueSecond = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SecondReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool SecondStarted;
        private int _attempts;

        protected override async Task<SocketReceiveFromResult> ReceiveDatagramAsync(Memory<byte> memory, EndPoint remote)
        {
            bool second = ++_attempts == 2;
            if (second)
            {
                SecondStarted = true;
                await ContinueSecond.Task.ConfigureAwait(false);
            }
            var result = await base.ReceiveDatagramAsync(memory, remote).ConfigureAwait(false);
            if (second) SecondReceived.SetResult();
            return result;
        }
    }
}
