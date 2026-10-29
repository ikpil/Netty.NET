/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

/**
 * Tests for {@link CoalescingBufferQueue}.
 */
public class CoalescingBufferQueueTest : IDisposable
{
    private readonly ByteBuf _cat = Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes("cat"));
    private readonly ByteBuf _mouse = Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes("mouse"));
    private readonly TaskCompletionSource _catProducer = NewProducer(), _emptyProducer = NewProducer();
    private readonly Action<Task> _mouseCompletion;
    private bool _mouseDone, _mouseSuccess;
    private readonly EmbeddedChannel _channel = new();
    private readonly CoalescingBufferQueue _queue;
    public CoalescingBufferQueueTest()
    {
        _queue = new CoalescingBufferQueue(_channel, 16, true);
        _mouseCompletion = task => { _mouseDone = true; _mouseSuccess = task.IsCompletedSuccessfully; };
    }
    public void Dispose() => Assert.False(_channel.Finish());
    [Fact]
    public void TestAddFirstPromiseRetained()
    {
        _queue.Add(_cat, _catProducer);
        AssertQueueSize(3, false);
        _queue.Add(_mouse, _mouseCompletion);
        AssertQueueSize(8, false);
        var aggregate = NewProducer();
        Assert.Equal("catmous", Dequeue(7, aggregate));
        var remainder = Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes("mous"));
        _queue.AddFirst(remainder, aggregate);
        var second = NewProducer();
        Assert.Equal("mouse", Dequeue(5, second));
        second.SetResult();
        Assert.True(_catProducer.Task.IsCompletedSuccessfully);
        Assert.True(_mouseSuccess);
        Assert.Equal(0, _cat.ReferenceCount);
        Assert.Equal(0, _mouse.ReferenceCount);
    }
    [Fact]
    public void TestAddFirstVoidPromise()
    {
        _queue.Add(_cat, _catProducer);
        AssertQueueSize(3, false);
        _queue.Add(_mouse, _mouseCompletion);
        AssertQueueSize(8, false);
        var aggregate = NewProducer();
        Assert.Equal("catmous", Dequeue(7, aggregate));
        var remainder = Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes("mous"));
        // CLR: no completion producer is the native equivalent of a void promise.
        _queue.AddFirst(remainder);
        var second = NewProducer();
        Assert.Equal("mouse", Dequeue(5, second));
        second.SetResult();
        // Because we used a void promise above, we shouldn't complete catPromise until aggregatePromise is completed.
        Assert.False(_catProducer.Task.IsCompletedSuccessfully);
        Assert.True(_mouseSuccess);
        aggregate.SetResult();
        Assert.True(_catProducer.Task.IsCompletedSuccessfully);
        Assert.True(_mouseSuccess);
        Assert.Equal(0, _cat.ReferenceCount);
        Assert.Equal(0, _mouse.ReferenceCount);
    }
    [Fact]
    public void TestAggregateWithFullRead()
    {
        _queue.Add(_cat, _catProducer);
        AssertQueueSize(3, false);
        _queue.Add(_mouse, _mouseCompletion);
        AssertQueueSize(8, false);
        var aggregate = NewProducer();
        Assert.Equal("catmouse", Dequeue(8, aggregate));
        AssertQueueSize(0, true);
        Assert.False(_catProducer.Task.IsCompletedSuccessfully);
        Assert.False(_mouseDone);
        aggregate.SetResult();
        Assert.True(_catProducer.Task.IsCompletedSuccessfully);
        Assert.True(_mouseSuccess);
        Assert.Equal(0, _cat.ReferenceCount);
        Assert.Equal(0, _mouse.ReferenceCount);
    }
    [Fact]
    public void TestWithVoidPromise()
    {
        _queue.Add(_cat);
        _queue.Add(_mouse);
        AssertQueueSize(8, false);
        Assert.Equal("catm", Dequeue(4, NewProducer()));
        AssertQueueSize(4, false);
        Assert.Equal("ouse", Dequeue(4, NewProducer()));
        AssertQueueSize(0, true);
        Assert.Equal(0, _cat.ReferenceCount);
        Assert.Equal(0, _mouse.ReferenceCount);
    }
    [Fact]
    public void TestAggregateWithPartialRead()
    {
        _queue.Add(_cat, _catProducer);
        _queue.Add(_mouse, _mouseCompletion);
        var aggregate = NewProducer();
        Assert.Equal("catm", Dequeue(4, aggregate));
        AssertQueueSize(4, false);
        Assert.False(_catProducer.Task.IsCompletedSuccessfully);
        Assert.False(_mouseDone);
        aggregate.SetResult();
        Assert.True(_catProducer.Task.IsCompletedSuccessfully);
        Assert.False(_mouseDone);
        aggregate = NewProducer();
        Assert.Equal("ouse", Dequeue(int.MaxValue, aggregate));
        AssertQueueSize(0, true);
        Assert.False(_mouseDone);
        aggregate.SetResult();
        Assert.True(_mouseSuccess);
        Assert.Equal(0, _cat.ReferenceCount);
        Assert.Equal(0, _mouse.ReferenceCount);
    }
    [Fact]
    public void TestReadExactAddedBufferSizeReturnsOriginal()
    {
        _queue.Add(_cat, _catProducer);
        _queue.Add(_mouse, _mouseCompletion);
        var aggregate = NewProducer();
        Assert.Same(_cat, _queue.Remove(3, aggregate));
        Assert.False(_catProducer.Task.IsCompletedSuccessfully);
        aggregate.SetResult();
        Assert.True(_catProducer.Task.IsCompletedSuccessfully);
        Assert.Equal(1, _cat.ReferenceCount);
        _cat.Release();
        aggregate = NewProducer();
        Assert.Same(_mouse, _queue.Remove(5, aggregate));
        Assert.False(_mouseDone);
        aggregate.SetResult();
        Assert.True(_mouseSuccess);
        Assert.Equal(1, _mouse.ReferenceCount);
        _mouse.Release();
    }
    [Fact]
    public void TestReadEmptyQueueReturnsEmptyBuffer()
    {
        // Not used in this test.
        _cat.Release();
        _mouse.Release();
        AssertQueueSize(0, true);
        Assert.Equal("", Dequeue(int.MaxValue, NewProducer()));
        AssertQueueSize(0, true);
    }
    [Fact]
    public void TestReleaseAndFailAll()
    {
        _queue.Add(_cat, _catProducer);
        _queue.Add(_mouse, _mouseCompletion);
        var cause = new Exception("ooops");
        _queue.ReleaseAndFailAll(cause);
        AssertQueueSize(0, true);
        Assert.Equal(0, _cat.ReferenceCount);
        Assert.Equal(0, _mouse.ReferenceCount);
        Assert.Same(cause, _catProducer.Task.Exception.InnerException);
        Assert.Equal("", Dequeue(int.MaxValue, NewProducer()));
        AssertQueueSize(0, true);
    }
    [Fact]
    public void TestEmptyBuffersAreCoalesced()
    {
        var empty = Unpooled.Buffer(0, 1);
        AssertQueueSize(0, true);
        _queue.Add(_cat, _catProducer);
        _queue.Add(empty, _emptyProducer);
        AssertQueueSize(3, false);
        var aggregate = NewProducer();
        Assert.Equal("cat", Dequeue(3, aggregate));
        AssertQueueSize(0, true);
        Assert.False(_catProducer.Task.IsCompletedSuccessfully);
        Assert.False(_emptyProducer.Task.IsCompletedSuccessfully);
        aggregate.SetResult();
        Assert.True(_catProducer.Task.IsCompletedSuccessfully);
        Assert.True(_emptyProducer.Task.IsCompletedSuccessfully);
        Assert.Equal(0, _cat.ReferenceCount);
        Assert.Equal(0, empty.ReferenceCount);
    }
    // If a queued buffer's readable bytes shrink after enqueue (released or consumed out from under the queue),
    // the queue must still report 0 readable bytes once drained.
    // See https://github.com/netty/netty/issues/16946
    [Fact]
    public void TestReadableBytesResetWhenQueuedBufferConsumedExternally()
    {
        // Not used in this test.
        _cat.Release();
        _mouse.Release();
        var buffer = Unpooled.Buffer().WriteZero(292);
        _queue.Add(buffer);
        Assert.Equal(292, _queue.ReadableBytes);
        // Consume the buffer out from under the queue (e.g. a pooled buffer released and reused).
        buffer.SkipBytes(buffer.ReadableBytes);
        var removed = _queue.Remove(int.MaxValue, NewProducer());
        Assert.False(removed.IsReadable);
        removed.Release();
        AssertQueueSize(0, true);
        // A subsequent remove must not resurrect phantom readable bytes.
        removed = _queue.Remove(int.MaxValue, NewProducer());
        Assert.False(removed.IsReadable);
        removed.Release();
        AssertQueueSize(0, true);
    }
    [Fact]
    public void TestReadableBytesResetWhenReleaseAndFailAllSeesConsumedBuffer()
    {
        _cat.Release();
        _mouse.Release();
        var buffer = Unpooled.Buffer().WriteZero(292);
        _queue.Add(buffer);
        Assert.Equal(292, _queue.ReadableBytes);
        buffer.SkipBytes(buffer.ReadableBytes);
        _queue.ReleaseAndFailAll(new Exception());
        AssertQueueSize(0, true);
        Assert.Equal(0, buffer.ReferenceCount);
    }
    [Fact]
    public async Task TestReadableBytesResetWhenWriteAndRemoveAllSeesConsumedBuffer()
    {
        _cat.Release();
        _mouse.Release();
        var channel = new EmbeddedChannel(new ReleasingWriter(), new EmptyHandler());
        var queue = new CoalescingBufferQueue(channel);
        var buffer = Unpooled.Buffer().WriteZero(292);
        queue.Add(buffer);
        Assert.Equal(292, queue.ReadableBytes);
        buffer.SkipBytes(buffer.ReadableBytes);
        await queue.WriteAndRemoveAllAsync(channel.Pipeline.LastContext);
        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.ReadableBytes);
        Assert.False(channel.Finish());
    }
    [Fact]
    public void TestReadableBytesResetWhenRemoveFirstSeesConsumedBuffer()
    {
        _cat.Release();
        _mouse.Release();
        var buffer = Unpooled.Buffer().WriteZero(292);
        _queue.Add(buffer);
        Assert.Equal(292, _queue.ReadableBytes);
        buffer.SkipBytes(buffer.ReadableBytes);
        var removed = _queue.RemoveFirst(NewProducer());
        Assert.Same(buffer, removed);
        removed.Release();
        AssertQueueSize(0, true);
    }
    [Fact]
    public void TestMerge()
    {
        _queue.Add(_cat, _catProducer);
        var other = new CoalescingBufferQueue(_channel);
        other.Add(_mouse, _mouseCompletion);
        other.CopyTo(_queue);
        AssertQueueSize(8, false);
        var aggregate = NewProducer();
        Assert.Equal("catmouse", Dequeue(8, aggregate));
        AssertQueueSize(0, true);
        Assert.False(_catProducer.Task.IsCompletedSuccessfully);
        Assert.False(_mouseDone);
        aggregate.SetResult();
        Assert.True(_catProducer.Task.IsCompletedSuccessfully);
        Assert.True(_mouseSuccess);
        Assert.Equal(0, _cat.ReferenceCount);
        Assert.Equal(0, _mouse.ReferenceCount);
    }
    [Fact]
    public void TestWritabilityChanged() => TestWritabilityChanged0(false);
    [Fact]
    public void TestWritabilityChangedFailAll() => TestWritabilityChanged0(true);
    private void TestWritabilityChanged0(bool fail)
    {
        _channel.Config.SetWriteBufferWaterMark(new WriteBufferWaterMark(3, 4));
        Assert.True(_channel.IsWritable);
        _queue.Add(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
        Assert.True(_channel.IsWritable);
        _queue.Add(Unpooled.WrappedBuffer(new byte[] { 4, 5 }));
        Assert.False(_channel.IsWritable);
        Assert.Equal(5, _queue.ReadableBytes);
        if (fail)
            _queue.ReleaseAndFailAll(new InvalidOperationException());
        else
        {
            // CLR: no producer is needed for a buffer with no completion metadata.
            var buffer = _queue.RemoveFirst(NewProducer());
            Assert.Equal(1, buffer.ReadByte());
            Assert.Equal(2, buffer.ReadByte());
            Assert.Equal(3, buffer.ReadByte());
            Assert.False(buffer.IsReadable);
            buffer.Release();
            Assert.True(_channel.IsWritable);
            buffer = _queue.RemoveFirst(NewProducer());
            Assert.Equal(4, buffer.ReadByte());
            Assert.Equal(5, buffer.ReadByte());
            Assert.False(buffer.IsReadable);
            buffer.Release();
        }
        Assert.True(_channel.IsWritable);
        Assert.True(_queue.IsEmpty);
    }
    private static TaskCompletionSource NewProducer() => new();
    private void AssertQueueSize(int size, bool isEmpty) { Assert.Equal(size, _queue.ReadableBytes); if (isEmpty) Assert.True(_queue.IsEmpty); else Assert.False(_queue.IsEmpty); }
    private string Dequeue(int count, TaskCompletionSource aggregate)
    {
        var removed = _queue.Remove(count, aggregate);
        string text = removed.GetString(removed.ReaderIndex, removed.ReadableBytes, Encoding.ASCII);
        ReferenceCountUtil.SafeRelease(removed);
        return text;
    }
    private sealed class EmptyHandler : ChannelHandlerAdapter { }
    private sealed class ReleasingWriter : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token)
        { ReferenceCountUtil.Release(message); return Task.CompletedTask; }
    }
}
