/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;
using Xunit.Sdk;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: ReplayingDecoder<Void> and the raw ReplayingDecoder are ReplayingDecoder<object> (C# needs a type
// argument), and ReplayingDecoder<Integer> is ReplayingDecoder<int>. The char literals of the byte[] initializers
// are cast to byte, which Java narrows implicitly.
public class ReplayingDecoderTest
{
    [Fact]
    public void TestLineProtocol()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineDecoder());

        // Ordinary input
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { (byte) 'A' }));
        Assert.Null(ch.ReadInbound());
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { (byte) 'B' }));
        Assert.Null(ch.ReadInbound());
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { (byte) 'C' }));
        Assert.Null(ch.ReadInbound());
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { (byte) '\n' }));

        ByteBuf buf = Unpooled.WrappedBuffer(new byte[] { (byte) 'A', (byte) 'B', (byte) 'C' });
        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();

        // Truncated input
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { (byte) 'A' }));
        Assert.Null(ch.ReadInbound());

        ch.Finish();
        Assert.Null(ch.ReadInbound());
    }

    private sealed class LineDecoder : ReplayingDecoder<object>
    {
        internal LineDecoder()
        {
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            ByteBuf msg = @in.ReadBytes(@in.BytesBefore((byte) '\n'));
            @out.Add(msg);
            @in.SkipBytes(1);
        }
    }

    [Fact]
    public void TestReplacement()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new BloatedLineDecoder());

        // "AB" should be forwarded to LineDecoder by BloatedLineDecoder.
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[]{(byte) 'A', (byte) 'B'}));
        Assert.Null(ch.ReadInbound());

        // "C\n" should be appended to "AB" so that LineDecoder decodes it correctly.
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[]{(byte) 'C', (byte) '\n'}));

        ByteBuf buf = Unpooled.WrappedBuffer(new byte[] { (byte) 'A', (byte) 'B', (byte) 'C' });
        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();

        ch.Finish();
        Assert.Null(ch.ReadInbound());
    }

    private sealed class BloatedLineDecoder : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ctx.Pipeline.Replace(this, "less-bloated", new LineDecoder());
            ctx.Pipeline.FireChannelRead(msg);
        }
    }

    [Fact]
    public void TestSingleDecode()
    {
        LineDecoder decoder = new LineDecoder();
        decoder.SetSingleDecode(true);
        EmbeddedChannel ch = new EmbeddedChannel(decoder);

        // "C\n" should be appended to "AB" so that LineDecoder decodes it correctly.
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[]{(byte) 'C', (byte) '\n' , (byte) 'B', (byte) '\n'}));

        ByteBuf buf  = Unpooled.WrappedBuffer(new byte[] {(byte) 'C'});
        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();

        Assert.Null(ch.ReadInbound(), "Must be null as it must only decode one frame");

        ch.Read();
        ch.Finish();

        buf  = Unpooled.WrappedBuffer(new byte[] {(byte) 'B'});
        buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal(buf, buf2);

        buf.Release();
        buf2.Release();

        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestRemoveItself()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new RemoveItselfDecoder());

        ByteBuf buf = Unpooled.WrappedBuffer(new byte[] {(byte) 'a', (byte) 'b', (byte) 'c'});
        channel.WriteInbound(buf.Copy());
        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(b, buf.SkipBytes(1));
        b.Release();
        buf.Release();
    }

    private sealed class RemoveItselfDecoder : ReplayingDecoder<object>
    {
        private bool removed;

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            Assert.False(removed);
            @in.ReadByte();
            ctx.Pipeline.Remove(this);
            removed = true;
        }
    }

    [Fact]
    public void TestRemoveItselfWithReplayError()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new RemoveItselfWithReplayErrorDecoder());

        ByteBuf buf = Unpooled.WrappedBuffer(new byte[] {(byte) 'a', (byte) 'b', (byte) 'c'});
        channel.WriteInbound(buf.Copy());
        ByteBuf b = channel.ReadInbound<ByteBuf>();

        Assert.Equal(b, buf, "Expect to have still all bytes in the buffer");
        b.Release();
        buf.Release();
    }

    private sealed class RemoveItselfWithReplayErrorDecoder : ReplayingDecoder<object>
    {
        private bool removed;

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            Assert.False(removed);
            ctx.Pipeline.Remove(this);

            @in.ReadBytes(1000);

            removed = true;
        }
    }

    [Fact]
    public void TestRemoveItselfWriteBuffer()
    {
        ByteBuf buf = Unpooled.Buffer().WriteBytes(new byte[] {(byte) 'a', (byte) 'b', (byte) 'c'});
        EmbeddedChannel channel = new EmbeddedChannel(new RemoveItselfWriteBufferDecoder(buf));

        channel.WriteInbound(buf.Copy());
        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(b, Unpooled.WrappedBuffer(new byte[] { (byte) 'b', (byte) 'c'}));
        b.Release();
        buf.Release();
    }

    private sealed class RemoveItselfWriteBufferDecoder(ByteBuf buf) : ReplayingDecoder<object>
    {
        private bool removed;

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            Assert.False(removed);
            @in.ReadByte();
            ctx.Pipeline.Remove(this);

            // This should not let it keep call decode
            buf.WriteByte('d');
            removed = true;
        }
    }

    // CLR: the BlockingQueue<Integer> (a LinkedBlockingDeque) is a BlockingCollection<int>, whose "default collection
    // type ... is ConcurrentQueue<T>" (FIFO) and whose Take blocks while it is empty
    // (https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.blockingcollection-1); take() is
    // Take with the test's cancellation token (xUnit1051), which only ends a wait the source would block in forever.
    [Fact]
    public void TestFireChannelReadCompleteOnInactive()
    {
        using BlockingCollection<int> queue = new BlockingCollection<int>();
        ByteBuf buf = Unpooled.Buffer().WriteBytes(new byte[]{(byte) 'a', (byte) 'b'});
        EmbeddedChannel channel = new EmbeddedChannel(new FireChannelReadCompleteDecoder(),
                new FireChannelReadCompleteHandler(queue));
        Assert.False(channel.WriteInbound(buf));
        channel.Finish();
        Assert.Equal(1, (int) queue.Take(TestContext.Current.CancellationToken));
        Assert.Equal(1, (int) queue.Take(TestContext.Current.CancellationToken));
        Assert.Equal(2, (int) queue.Take(TestContext.Current.CancellationToken));
        Assert.Equal(3, (int) queue.Take(TestContext.Current.CancellationToken));
        Assert.True(queue.Count == 0);
    }

    private sealed class FireChannelReadCompleteDecoder : ReplayingDecoder<int>
    {

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            int readable = @in.ReadableBytes;
            Assert.True(readable > 0);
            @in.SkipBytes(readable);
            @out.Add("data");
        }

        protected override void DecodeLast(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            Assert.False(@in.IsReadable());
            @out.Add("data");
        }
    }

    private sealed class FireChannelReadCompleteHandler(BlockingCollection<int> queue) : ChannelInboundHandlerAdapter
    {
        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            queue.Add(3);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            queue.Add(1);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!ctx.Channel.IsActive)
            {
                queue.Add(2);
            }
        }
    }

    // CLR: AtomicReference<Error> is the Common port's AtomicReference<Exception>, and the AssertionError is xUnit's
    // FailException, which Assert.Fail throws.
    [Fact]
    public void TestChannelInputShutdownEvent()
    {
        AtomicReference<Exception> error = new AtomicReference<Exception>();

        EmbeddedChannel channel = new EmbeddedChannel(new ChannelInputShutdownEventDecoder(error));

        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(new byte[] {0, 1})));
        channel.Pipeline.FireUserEventTriggered(ChannelInputShutdownEvent.INSTANCE);
        Assert.False(channel.FinishAndReleaseAll());

        Exception err = error.Get();
        if (err != null)
        {
            throw err;
        }
    }

    private sealed class ChannelInputShutdownEventDecoder : ReplayingDecoder<int>
    {
        private readonly AtomicReference<Exception> error;
        private bool decoded;

        internal ChannelInputShutdownEventDecoder(AtomicReference<Exception> error) : base(0)
        {
            this.error = error;
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            if (!(@in is ReplayingDecoderByteBuf))
            {
                error.Set(FailException.ForFailure("in must be of type " + typeof(ReplayingDecoderByteBuf)
                        + " but was " + @in.GetType()));
                return;
            }

            if (!decoded)
            {
                decoded = true;
                @in.ReadByte();
                State(1);
            }
            else
            {
                // This will throw an ReplayingError
                @in.SkipBytes(int.MaxValue);
            }
        }
    }

    // CLR: ThreadLocalRandom.current() is Random.Shared, "a thread-safe Random instance that may be used concurrently
    // from any thread" (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared).
    [Fact]
    public void HandlerRemovedWillNotReleaseBufferIfDecodeInProgress()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HandlerRemovedDecoder());
        byte[] bytes = new byte[1024];
        Random.Shared.NextBytes(bytes);

        Assert.True(channel.WriteInbound(Unpooled.WrappedBuffer(bytes)));
        Assert.True(channel.FinishAndReleaseAll());
    }

    private sealed class HandlerRemovedDecoder : ReplayingDecoder<int>
    {
        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            ctx.Pipeline.Remove(this);
            Assert.True(@in.RefCnt != 0);
        }

        protected override void HandlerRemoved0(IChannelHandlerContext ctx)
        {
            AssertCumulationReleased(InternalBuffer());
        }
    }

    private static void AssertCumulationReleased(ByteBuf byteBuf)
    {
        Assert.True(byteBuf == null || byteBuf == Unpooled.EmptyBuffer || byteBuf.RefCnt == 0,
                "unexpected value: " + byteBuf);
    }
}
