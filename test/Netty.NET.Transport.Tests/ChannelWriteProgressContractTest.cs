using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Source-based CLR progress contracts. These are supplemental; the original outbound-buffer/socket
// fixtures retain their own mappings and unchanged assertions.
#pragma warning disable xUnit1051 // Queue ownership and native producer cleanup are independent.
public class ChannelWriteProgressContractTest
{
    [Theory]
    [InlineData("bytes", 7)]
    [InlineData("holder", 7)]
    [InlineData("file", 12345678901L)]
    [InlineData("unknown", -1)]
    public async Task QueueProgressUsesTheOriginalMessageTotalAndNativeTask(string kind, long total)
    {
        var channel = new EmbeddedChannel();
        var reports = new List<ChannelWriteProgress>(); channel.Config.WriteProgress = new Observer(reports.Add);
        var queue = new ChannelOutboundBuffer(channel);
        object message = kind switch
        {
            "bytes" => Unpooled.Buffer(9).WriteZero(9).SetIndex(2, 9),
            "holder" => new DefaultByteBufHolder(Unpooled.Buffer(7).WriteZero(7)),
            "file" => new DefaultFileRegion(new FileInfo(Path.GetTempPath() + Guid.NewGuid().ToString("N")), 0, total),
            _ => new object()
        };
        Task operation = queue.AddMessage(message, 999); queue.AddFlush(); queue.Progress(2); queue.Progress(3);
        Assert.Equal(5, queue.CurrentProgress); Assert.Equal(new long[] { 2, 5 }, reports.Select(r => r.Progress.Completed));
        foreach (var report in reports)
        {
            Assert.Same(channel, report.Channel); Assert.Same(operation, report.Operation); Assert.False(report.Operation.IsCompleted);
            Assert.Equal(total < 0 ? (long?)null : total, report.Progress.Total);
        }
        queue.Remove(); await operation; Assert.True(reports[0].Operation.IsCompletedSuccessfully);
        channel.FinishAndReleaseAll();
    }
    [Fact]
    public async Task EachEntryCapturesItsObserverAndResetsCountsForTheNextWrite()
    {
        var channel = new EmbeddedChannel(); var queue = new ChannelOutboundBuffer(channel);
        var first = new List<ChannelWriteProgress>(); var second = new List<ChannelWriteProgress>();
        channel.Config.WriteProgress = new Observer(first.Add); Task a = queue.AddMessage(Unpooled.Buffer(3).WriteZero(3), 3);
        channel.Config.WriteProgress = new Observer(second.Add); Task b = queue.AddMessage(Unpooled.Buffer(2).WriteZero(2), 2);
        channel.Config.WriteProgress = null; Task c = queue.AddMessage(Unpooled.Buffer(1).WriteZero(1), 1);
        queue.AddFlush(); queue.RemoveBytes(6); await Task.WhenAll(a, b, c);
        Assert.Same(a, Assert.Single(first).Operation); Assert.Equal(3, first[0].Progress.Completed);
        Assert.Same(b, Assert.Single(second).Operation); Assert.Equal(2, second[0].Progress.Completed);
        channel.FinishAndReleaseAll();
    }
    [Fact]
    public async Task CanceledUnflushedEntriesNeverReportAndReleaseTheirBuffers()
    {
        var channel = new EmbeddedChannel(); var queue = new ChannelOutboundBuffer(channel);
        using var cancellation = new CancellationTokenSource(); int reports = 0;
        channel.Config.WriteProgress = new Observer(_ => reports++);
        var buffer = Unpooled.Buffer(3).WriteZero(3); Task write = queue.AddMessage(buffer, 3, cancellation.Token);
        cancellation.Cancel(); queue.AddFlush(); queue.Progress(0); queue.Remove();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.Equal(0, reports); Assert.Equal(0, buffer.ReferenceCount); channel.FinishAndReleaseAll();
    }
    [Fact]
    public async Task AReportFailureDoesNotFailTheWriteOrSkipLaterEntries()
    {
        var channel = new EmbeddedChannel(); var queue = new ChannelOutboundBuffer(channel); int reports = 0;
        channel.Config.WriteProgress = new Observer(_ => { reports++; throw new InvalidOperationException("observer"); });
        Task a = queue.AddMessage(Unpooled.Buffer(1).WriteByte(1), 1), b = queue.AddMessage(Unpooled.Buffer(1).WriteByte(2), 1);
        queue.AddFlush(); queue.RemoveBytes(2); await Task.WhenAll(a, b);
        Assert.Equal(2, reports); channel.FinishAndReleaseAll();
    }
    [Fact]
    public async Task FailureRemainsOnTheSameTaskAfterAcceptedProgress()
    {
        var channel = new EmbeddedChannel(); var queue = new ChannelOutboundBuffer(channel);
        ChannelWriteProgress report = default; channel.Config.WriteProgress = new Observer(value => report = value);
        Task write = queue.AddMessage(Unpooled.Buffer(3).WriteZero(3), 3); queue.AddFlush(); queue.Progress(1);
        var cause = new IOException("write failure"); queue.Remove(cause);
        Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => write)); Assert.Same(write, report.Operation);
        Assert.Equal(1, report.Progress.Completed); Assert.True(report.Operation.IsFaulted); channel.FinishAndReleaseAll();
    }
    [Fact]
    public async Task AProgressObserverCanCloseTheQueueWithoutDoubleRemovalOrRelease()
    {
        var channel = new EmbeddedChannel(); var queue = new ChannelOutboundBuffer(channel);
        var a = Unpooled.Buffer(2).WriteZero(2); var b = Unpooled.Buffer(2).WriteZero(2);
        var closed = new IOException("closed from observer"); int calls = 0;
        channel.Config.WriteProgress = new Observer(_ => { calls++; queue.Close(closed); });
        Task first = queue.AddMessage(a, 2), second = queue.AddMessage(b, 2);
        queue.AddFlush(); queue.RemoveBytes(4);
        Assert.Same(closed, await Assert.ThrowsAsync<IOException>(() => first));
        Assert.Same(closed, await Assert.ThrowsAsync<IOException>(() => second));
        Assert.Equal(1, calls); Assert.Equal(0, a.ReferenceCount); Assert.Equal(0, b.ReferenceCount);
        Assert.Equal(0, queue.TotalPendingWriteBytes); Assert.True(queue.IsEmpty); channel.FinishAndReleaseAll();
    }
    [Fact]
    public async Task InvalidRangesAreNotDeliveredAndEmptyRangesAreNotWholeFileReports()
    {
        var channel = new EmbeddedChannel(); var queue = new ChannelOutboundBuffer(channel);
        var reports = new List<ChannelWriteProgress>(); channel.Config.WriteProgress = new Observer(reports.Add);
        Task empty = queue.AddMessage(Unpooled.EmptyBuffer, 0); queue.AddFlush(); queue.Progress(0); queue.Progress(1); queue.Remove();
        await empty; Assert.Equal(0, Assert.Single(reports).Progress.Completed); Assert.Equal(0, reports[0].Progress.Total);
        channel.FinishAndReleaseAll();
    }
    [Fact]
    public async Task UnknownLengthCounterOverflowKeepsJavaWraparoundWithoutAnInvalidReport()
    {
        var channel = new EmbeddedChannel(); var queue = new ChannelOutboundBuffer(channel);
        var reports = new List<ChannelWriteProgress>(); channel.Config.WriteProgress = new Observer(reports.Add);
        Task write = queue.AddMessage(new object(), 0); queue.AddFlush();
        queue.Progress(long.MaxValue); queue.Progress(1);
        Assert.Equal(long.MinValue, queue.CurrentProgress);
        Assert.Equal(long.MaxValue, Assert.Single(reports).Progress.Completed);
        Assert.Null(reports[0].Progress.Total);
        queue.Remove(); await write; channel.FinishAndReleaseAll();
    }
    [Fact]
    public async Task TheEmbeddedTransportDoesNotInventProgressFromSuccessfulCompletion()
    {
        var channel = new EmbeddedChannel(); int reports = 0; channel.Config.WriteProgress = new Observer(_ => reports++);
        await channel.WriteAndFlushAsync(Unpooled.Buffer(3).WriteZero(3));
        Assert.Equal(0, reports); channel.FinishAndReleaseAll();
    }
    [Fact(Timeout = 10000)]
    public async Task RealGatheringWritesKeepPerEntryCountsAndOwnerDispatchAcrossPartialVectors()
    {
        await using var connection = await Connected.Open();
        var reports = new ConcurrentQueue<ChannelWriteProgress>();
        connection.Channel.Config.WriteProgress = new Observer(value =>
        {
            Assert.True(connection.Channel.EventLoop.InEventLoop()); Assert.False(value.Operation.IsCompleted); reports.Enqueue(value);
        });
        Task first = null, second = null, last = null;
        await connection.Channel.EventLoop.SubmitAsync(() =>
        {
            first = connection.Channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
            second = connection.Channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 4, 5, 6, 7, 8 }));
            last = connection.Channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 9, 10, 11, 12 }));
        });
        var actual = new byte[12]; await connection.Peer.GetStream().ReadExactlyAsync(actual, TestContext.Current.CancellationToken);
        await Task.WhenAll(first, second, last).WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(1, 12).Select(n => (byte)n), actual);
        var groups = reports.GroupBy(r => r.Operation).ToArray(); Assert.Equal(3, groups.Length);
        Assert.Equal(new long[] { 3, 5, 4 }, groups.Select(g => g.Last().Progress.Completed));
        Assert.Equal(new long?[] { 3, 5, 4 }, groups.Select(g => g.Last().Progress.Total));
        foreach (var group in groups) { Assert.True(group.Key.IsCompletedSuccessfully); Assert.Equal(group.Select(r => r.Progress.Completed).OrderBy(n => n), group.Select(r => r.Progress.Completed)); }
        Assert.True(connection.Channel.GatheredCalls >= 2);
    }
    [Theory(Timeout = 15000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAndPortableFilePathsReportFileCountsInsteadOfMemoryEstimates(bool synchronousFile)
    {
        await using var connection = await Connected.Open(); string path = Path.GetTempFileName();
        var bytes = new byte[1024 * 1024 + 31]; Random.Shared.NextBytes(bytes);
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1,
                synchronousFile ? FileOptions.None : FileOptions.Asynchronous);
            var reports = new ConcurrentQueue<ChannelWriteProgress>(); bool wrongOwner = false;
            connection.Channel.Config.WriteProgress = new Observer(value => { if (!connection.Channel.EventLoop.InEventLoop()) wrongOwner = true; reports.Enqueue(value); });
            var region = new DefaultFileRegion(file, 7, bytes.Length - 7); Task write = connection.Channel.WriteAndFlushAsync(region, TestContext.Current.CancellationToken);
            var actual = new byte[bytes.Length - 7]; await connection.Peer.GetStream().ReadExactlyAsync(actual, TestContext.Current.CancellationToken);
            await write; Assert.Equal(bytes.AsSpan(7).ToArray(), actual); Assert.False(wrongOwner);
            Assert.NotEmpty(reports); Assert.Equal(bytes.Length - 7, reports.Last().Progress.Completed);
            Assert.All(reports, r => { Assert.Equal(bytes.Length - 7, r.Progress.Total); Assert.True(r.Operation.IsCompletedSuccessfully); });
            _ = Assert.Single(reports.Select(r => r.Operation).Distinct()); Assert.Equal(0, region.ReferenceCount);
        }
        finally { File.Delete(path); }
    }
    [Fact(Timeout = 10000)]
    public async Task DatagramHolderProgressRetainsTheSinglePacketBoundary()
    {
        var group = new DefaultEventLoopGroup(1); using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        try
        {
            var channel = (DatagramChannel)await new Bootstrap().Group(group).ChannelFactory(() => new DatagramChannel(AddressFamily.InterNetwork))
                .Handler(new ChannelInboundHandlerAdapter()).BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
            var reports = new List<ChannelWriteProgress>(); channel.Config.WriteProgress = new Observer(reports.Add);
            byte[] bytes = { 1, 2, 3, 4, 5 };
            await channel.WriteAndFlushAsync(new DatagramPacket(Unpooled.WrappedBuffer(bytes), (IPEndPoint)peer.Client.LocalEndPoint), TestContext.Current.CancellationToken);
            Assert.Equal(bytes, (await peer.ReceiveAsync(TestContext.Current.CancellationToken)).Buffer);
            var report = Assert.Single(reports); Assert.Equal(5, report.Progress.Completed); Assert.Equal(5, report.Progress.Total); Assert.True(report.Operation.IsCompletedSuccessfully);
        }
        finally { await group.StopAsync(); }
    }
    [Fact(Timeout = 10000)]
    public async Task TransformingHandlersOnAnotherExecutorKeepProgressAndCompletionOnTheirActualQueueTask()
    {
        await using var connection = await Connected.Open();
        var extra = new DefaultEventExecutorGroup(1);
        ExecutorProgress observation = null;
        var notifications = new ConcurrentQueue<string>();
        try
        {
            connection.Channel.Pipeline.AddLast(extra, "encode", new Encode());
            connection.Channel.Config.WriteProgress = new Observer(value =>
            {
                observation ??= new ExecutorProgress(connection.Channel.EventLoop, value.Operation,
                    progress => notifications.Enqueue("progress:" + progress.Completed),
                    completed => notifications.Enqueue(completed.IsCompletedSuccessfully ? "success" : "failure"));
                observation.Report(value.Progress);
            });
            Task outer = connection.Channel.WriteAndFlushAsync("abc", TestContext.Current.CancellationToken);
            var actual = new byte[8]; await connection.Peer.GetStream().ReadExactlyAsync(actual, TestContext.Current.CancellationToken);
            await outer; Assert.Equal("wire:abc"u8.ToArray(), actual);
            Assert.NotNull(observation);
            await observation.NotificationsCompleted.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "progress:8", "success" }, notifications);
        }
        finally { observation?.Dispose(); await connection.DisposeAsync(); await extra.StopAsync(); }
    }
    private sealed class Encode : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken cancellationToken) =>
            context.WriteAsync(message is string text ? Unpooled.CopiedBuffer("wire:" + text, System.Text.Encoding.ASCII) : message, cancellationToken);
    }
    internal sealed class Observer(Action<ChannelWriteProgress> report) : IProgress<ChannelWriteProgress>
    { public void Report(ChannelWriteProgress value) => report(value); }
    private sealed class ProbeChannel : TcpSocketChannel
    {
        internal int GatheredCalls;
        internal override bool SupportsGatheringWrites => true;
        internal override Task<int> SendGatheredAsync(IList<ArraySegment<byte>> segments)
        {
            GatheredCalls++; int remaining = 4; var prefix = new List<ArraySegment<byte>>();
            foreach (var segment in segments)
            {
                int count = Math.Min(remaining, segment.Count); prefix.Add(new ArraySegment<byte>(segment.Array, segment.Offset, count)); remaining -= count;
                if (remaining == 0) break;
            }
            return Socket.SendAsync(prefix, SocketFlags.None);
        }
    }
    private sealed class Connected : IAsyncDisposable
    {
        private bool _disposed;
        private readonly ShutdownFixture _fixture = new(false);
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        internal ProbeChannel Channel;
        internal TcpClient Peer;
        internal static async Task<Connected> Open()
        {
            var result = new Connected(); result._listener.Start(); result.Channel = new ProbeChannel();
            try
            {
                await new Bootstrap().Group(result._fixture.Client.Config.Group).ChannelFactory(() => result.Channel)
                    .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(result._listener.LocalEndpoint, cancellationToken: TestContext.Current.CancellationToken);
                result.Peer = await result._listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken); return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (Channel != null) await Channel.CloseAsync();
            Peer?.Dispose(); _listener.Stop(); await _fixture.DisposeAsync();
        }
    }
}
#pragma warning restore xUnit1051
