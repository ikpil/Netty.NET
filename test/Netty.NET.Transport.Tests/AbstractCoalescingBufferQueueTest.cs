/*
 * Copyright 2020 The Netty Project

 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:

 * https://www.apache.org/licenses/LICENSE-2.0

 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

public class AbstractCoalescingBufferQueueTest
{
    // See https://github.com/netty/netty/issues/10286
    [Fact]
    public async Task TestDecrementAllWhenWriteAndRemoveAll() => await TestDecrementAll(true);
    // See https://github.com/netty/netty/issues/10286
    [Fact]
    public async Task TestDecrementAllWhenReleaseAndFailAll() => await TestDecrementAll(false);
    private static async Task TestDecrementAll(bool write)
    {
        var channel = new EmbeddedChannel(new ReleasingWriter(), new EmptyHandler());
        var queue = new Queue(channel);
        byte[] bytes = new byte[128];
        queue.Add(Unpooled.WrappedBuffer(bytes), task =>
        { queue.Add(Unpooled.WrappedBuffer(bytes)); Assert.Equal(bytes.Length, queue.ReadableBytes); });
        Assert.Equal(bytes.Length, queue.ReadableBytes);
        if (write)
            await queue.WriteAndRemoveAllAsync(channel.Pipeline.LastContext);
        else
            queue.ReleaseAndFailAll(new IOException("Channel is closed."));
        var buffer = queue.Remove(channel.Allocator, 128, new TaskCompletionSource());
        Assert.False(buffer.IsReadable);
        buffer.Release();
        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.ReadableBytes);
        Assert.False(channel.Finish());
    }
    [Fact]
    public void TestKeepStateConsistentOnError()
    {
        var error = new IllegalReferenceCountException();
        var channel = new EmbeddedChannel();
        var queue = new Queue(channel, error);
        var first = new TaskCompletionSource();
        var buffer = Unpooled.Buffer().WriteLong(0);
        queue.Add(buffer, first);
        var second = new TaskCompletionSource();
        var buffer2 = Unpooled.Buffer().WriteLong(0);
        queue.Add(buffer2, second);
        // Size should be 4 as its 2 buffers and 2 listeners.
        Assert.Equal(4, queue.Size);
        Assert.Equal(16, queue.ReadableBytes);
        // JUnit assertThrows accepts the original IllegalReferenceCountException subtype of IllegalStateException.
        Assert.ThrowsAny<InvalidOperationException>(() => queue.Remove(channel.Allocator, 128, new TaskCompletionSource()));
        // This should result in have the first buf and first listener removed from the queue.
        Assert.Equal(2, queue.Size);
        Assert.Equal(8, queue.ReadableBytes);
        Assert.Same(error, first.Task.Exception.InnerException);
        Assert.Equal(0, buffer.ReferenceCount);
        Assert.False(second.Task.IsCompleted);
        Assert.Equal(1, buffer2.ReferenceCount);
        var removed = queue.Remove(channel.Allocator, 128, new TaskCompletionSource());
        Assert.Equal(8, removed.ReadableBytes);
        removed.Release();
        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.Size);
        Assert.Equal(0, queue.ReadableBytes);
        removed = queue.Remove(channel.Allocator, 128, new TaskCompletionSource());
        Assert.Equal(0, removed.ReadableBytes);
        removed.Release();
    }
    private sealed class Queue(IChannel channel, Exception error = null) : AbstractCoalescingBufferQueue(channel, 128)
    {
        protected override ByteBuf Compose(IByteBufAllocator allocator, ByteBuf cumulation, ByteBuf next)
        {
            if (error != null)
            {
                // Simulate throwing an IllegalReferenceCountException.
                throw error;
            }
            return ComposeIntoComposite(allocator, cumulation, next);
        }
        protected override ByteBuf ComposeFirst(IByteBufAllocator allocator, ByteBuf first, int bufferSize)
        {
            if (error != null)
            {
                // Simulate throwing an IllegalReferenceCountException.
                throw error;
            }
            return base.ComposeFirst(allocator, first, bufferSize);
        }
        protected override ByteBuf RemoveEmptyValue() => Unpooled.EmptyBuffer;
    }
    private sealed class EmptyHandler : ChannelHandlerAdapter { }
    private sealed class ReleasingWriter : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token)
        { ReferenceCountUtil.Release(message); return Task.CompletedTask; }
    }
}
