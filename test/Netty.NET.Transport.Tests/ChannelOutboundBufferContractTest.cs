using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

// Supplemental tests of the ported ChannelOutboundBuffer structure: the flushedEntry/unflushedEntry/tailEntry list,
// the per-entry nioBuffers cache over borrowed ReadOnlyMemory segments, the thread-local NIO_BUFFERS array and the
// close(...)/failFlushed(...) contract. The original fixture is ChannelOutboundBufferTest.
#pragma warning disable xUnit1051 // Buffer-only operations complete synchronously; no cancellation is observed.
public class ChannelOutboundBufferContractTest
{
    [Fact]
    public void NioBuffersAfterAPartialWriteExposeOnlyTheUnwrittenBytes()
    {
        var channel = new EmbeddedChannel();
        var buffer = new ChannelOutboundBuffer(channel);
        var single = Unpooled.CopiedBuffer("abcdef", Encoding.ASCII);
        var composite = Unpooled.CompositeBuffer();
        composite.AddComponent(Unpooled.CopiedBuffer("gh", Encoding.ASCII), true);
        composite.AddComponent(Unpooled.CopiedBuffer("ij", Encoding.ASCII), true);
        composite.AddComponent(Unpooled.CopiedBuffer("kl", Encoding.ASCII), true);
        Task first = buffer.AddMessage(single, single.ReadableBytes);
        Task second = buffer.AddMessage(composite, composite.ReadableBytes);
        buffer.AddFlush();

        var nioBuffers = buffer.NioBuffers();
        Assert.Equal(4, buffer.NioBufferCount);
        Assert.Equal(12, buffer.NioBufferSize);
        Assert.Equal(new[] { "abcdef", "gh", "ij", "kl" }, Texts(nioBuffers, buffer.NioBufferCount));

        // The socket accepted "ab": the cached view of the first entry keeps only its unwritten tail.
        buffer.RemoveBytes(2);
        nioBuffers = buffer.NioBuffers();
        Assert.Equal(new[] { "cdef", "gh", "ij", "kl" }, Texts(nioBuffers, buffer.NioBufferCount));
        Assert.Equal(10, buffer.NioBufferSize);

        // "cdef" and "ghi" were written: the cached segments of the composite skip the written bytes.
        buffer.RemoveBytes(7);
        Assert.True(first.IsCompletedSuccessfully);
        Assert.Equal(0, single.RefCnt);
        nioBuffers = buffer.NioBuffers();
        Assert.Equal(new[] { "j", "kl" }, Texts(nioBuffers, buffer.NioBufferCount));
        Assert.Equal(3, buffer.NioBufferSize);

        buffer.RemoveBytes(3);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(0, composite.RefCnt);
        Assert.True(buffer.IsEmpty);
        Assert.Equal(0, buffer.NioBufferCount);
        Assert.True(nioBuffers[0].IsEmpty);
        Assert.True(nioBuffers[1].IsEmpty);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void NioBuffersShareOneArrayPerThreadAndClearItWhenDrained()
    {
        var channel = new EmbeddedChannel();
        var first = new ChannelOutboundBuffer(channel);
        var second = new ChannelOutboundBuffer(channel);
        first.AddMessage(Unpooled.CopiedBuffer("a", Encoding.ASCII), 1);
        first.AddFlush();
        second.AddMessage(Unpooled.CopiedBuffer("b", Encoding.ASCII), 1);
        second.AddFlush();

        var nioBuffers = first.NioBuffers();
        Assert.Same(nioBuffers, second.NioBuffers());
        Assert.Equal("b", Encoding.ASCII.GetString(nioBuffers[0].Span));

        // remove() clears the entries of the last nioBuffers() call once nothing flushed is left.
        while (second.Remove())
        {
        }
        Assert.True(nioBuffers[0].IsEmpty);
        while (first.Remove())
        {
        }
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void CloseRequiresAClosedChannelAndHandledFlushedWrites()
    {
        var channel = new EmbeddedChannel();

        var openChannel = new ChannelOutboundBuffer(channel);
        var flushed = Unpooled.Buffer(1).WriteByte(1);
        Task write = openChannel.AddMessage(flushed, 1);
        openChannel.AddFlush();
        var error = Assert.Throws<InvalidOperationException>(() => openChannel.Close(new ClosedChannelException()));
        Assert.Equal("close() must be invoked after the channel is closed.", error.Message);
        Assert.Same(flushed, openChannel.Current);
        Assert.False(write.IsCompleted);
        Assert.True(openChannel.Remove());
        Assert.True(write.IsCompletedSuccessfully);

        var pendingFlush = new ChannelOutboundBuffer(channel);
        var pending = Unpooled.Buffer(1).WriteByte(2);
        Task pendingWrite = pendingFlush.AddMessage(pending, 1);
        pendingFlush.AddFlush();
        error = Assert.Throws<InvalidOperationException>(() => pendingFlush.Close(new ClosedChannelException(), true));
        Assert.Equal("close() must be invoked after all flushed writes are handled.", error.Message);
        Assert.False(pendingWrite.IsCompleted);
        Assert.True(pendingFlush.Remove());
        Assert.Equal(0, pending.RefCnt);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public async Task CloseFailsAndReleasesOnlyUnflushedWritesWithoutWritabilityEvents()
    {
        int writabilityChanges = 0;
        var channel = new EmbeddedChannel(new WritabilityCounter(() => writabilityChanges++));
        channel.Config.SetWriteBufferWaterMark(new WriteBufferWaterMark(1, 2));
        var buffer = new ChannelOutboundBuffer(channel);
        var unflushed = Unpooled.Buffer(4).WriteZero(4);
        Task write = buffer.AddMessage(unflushed, 4);
        Assert.False(buffer.IsWritable);
        Assert.Equal(1, writabilityChanges);
        var cause = new ClosedChannelException();

        buffer.Close(cause, true);

        Assert.Same(cause, await Assert.ThrowsAsync<ClosedChannelException>(() => write));
        Assert.Equal(0, unflushed.RefCnt);
        Assert.Equal(0, buffer.TotalPendingWriteBytes);
        Assert.True(buffer.IsClosed);
        // close(...) only decreases the pending bytes; it does not trigger writability events.
        channel.RunPendingTasks();
        Assert.Equal(1, writabilityChanges);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public async Task CloseFromAFailFlushedReleaseRunsLaterOnTheEventLoop()
    {
        var channel = new EmbeddedChannel();
        var buffer = new ChannelOutboundBuffer(channel);
        var cause = new ClosedChannelException();
        var reentrant = new ReleaseCallback(() => buffer.Close(cause, true));
        Task first = buffer.AddMessage(reentrant, 1);
        buffer.AddFlush();
        var unflushed = Unpooled.Buffer(1).WriteByte(2);
        Task second = buffer.AddMessage(unflushed, 1);

        buffer.FailFlushed(cause, false);

        Assert.Same(cause, await Assert.ThrowsAsync<ClosedChannelException>(() => first));
        // close(...) was called while failFlushed(...) was running, so it was handed to the event loop.
        Assert.False(second.IsCompleted);
        Assert.Equal(1, unflushed.RefCnt);
        channel.RunPendingTasks();
        Assert.Same(cause, await Assert.ThrowsAsync<ClosedChannelException>(() => second));
        Assert.Equal(0, unflushed.RefCnt);
        Assert.Equal(0, buffer.TotalPendingWriteBytes);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void ForEachFlushedMessageSkipsCancelledWritesAndStopsWhenTheProcessorReturnsFalse()
    {
        var channel = new EmbeddedChannel();
        var buffer = new ChannelOutboundBuffer(channel);
        using var cancellation = new CancellationTokenSource();
        object a = "a", b = "b", c = "c", d = "d";
        buffer.AddMessage(a, 0);
        buffer.AddMessage(b, 0, cancellation.Token);
        buffer.AddMessage(c, 0);
        cancellation.Cancel();
        buffer.AddFlush();
        buffer.AddMessage(d, 0);

        var seen = new List<object>();
        buffer.ForEachFlushedMessage(new Processor(msg => { seen.Add(msg); return true; }));
        Assert.Equal(new[] { a, c }, seen);
        seen.Clear();
        buffer.ForEachFlushedMessage(new Processor(msg => { seen.Add(msg); return false; }));
        Assert.Equal(new[] { a }, seen);

        Assert.Equal(3, buffer.Size);
        buffer.AddFlush();
        Assert.Equal(4, buffer.Size);
        while (buffer.Remove())
        {
        }
        Assert.Equal(0, buffer.TotalPendingWriteBytes);
        channel.FinishAndReleaseAll();
    }

    private static string[] Texts(ReadOnlyMemory<byte>[] buffers, int count)
    {
        var texts = new string[count];
        for (int i = 0; i < count; i++)
            texts[i] = Encoding.ASCII.GetString(buffers[i].Span);
        return texts;
    }

    private sealed class Processor(Func<object, bool> process) : ChannelOutboundBuffer.IMessageProcessor
    {
        public bool ProcessMessage(object msg) => process(msg);
    }

    private sealed class ReleaseCallback(Action deallocated) : AbstractReferenceCounted
    {
        protected override void Deallocate() => deallocated();
        public override IReferenceCounted Touch(object hint) => this;
    }

    private sealed class WritabilityCounter(Action changed) : ChannelInboundHandlerAdapter
    {
        public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
        {
            changed();
            ctx.FireChannelWritabilityChanged();
        }
    }
}
#pragma warning restore xUnit1051
