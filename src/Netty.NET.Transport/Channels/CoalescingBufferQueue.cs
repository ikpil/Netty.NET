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
using System.Threading.Tasks;
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels;

/**
 * A FIFO queue of bytes where producers add bytes by repeatedly adding {@link ByteBuf} and consumers take bytes in
 * arbitrary lengths. This allows producers to add lots of small buffers and the consumer to take all the bytes
 * out in a single buffer. Conversely the producer may add larger buffers and the consumer could take the bytes in
 * many small buffers.
 *
 * <p>Bytes are added and removed with promises. If the last byte of a buffer added with a promise is removed then
 * that promise will complete when the promise passed to {@link #remove} completes.
 *
 * <p>This functionality is useful for aggregating or partitioning writes into fixed size buffers for framing protocols
 * such as HTTP2.
 */
public sealed class CoalescingBufferQueue : AbstractCoalescingBufferQueue
{
    private readonly IChannel _channel;
    public CoalescingBufferQueue(IChannel channel, int initialSize = 4, bool updateWritability = false)
        : base(updateWritability ? channel : null, initialSize)
    { _channel = channel ?? throw new ArgumentNullException(nameof(channel)); }
    /**
     * Remove a {@link ByteBuf} from the queue with the specified number of bytes. Any added buffer who's bytes are
     * fully consumed during removal will have it's promise completed when the passed aggregate {@link ChannelPromise}
     * completes.
     *
     * @param bytes the maximum number of readable bytes in the returned {@link ByteBuf}, if {@code bytes} is greater
     *              than {@link #readableBytes} then a buffer of length {@link #readableBytes} is returned.
     * @param aggregatePromise used to aggregate the promises and listeners for the constituent buffers.
     * @return a {@link ByteBuf} composed of the enqueued buffers.
     */
    public ByteBuf Remove(int bytes, TaskCompletionSource aggregateProducer) => Remove(_channel.Allocator, bytes, aggregateProducer);
    protected override ByteBuf Compose(IByteBufAllocator allocator, ByteBuf cumulation, ByteBuf next)
    {
        if (cumulation is CompositeByteBuf composite)
        { composite.AddComponent(next, true); return composite; }
        return ComposeIntoComposite(allocator, cumulation, next);
    }
    protected override ByteBuf RemoveEmptyValue() => Unpooled.EmptyBuffer;
}
