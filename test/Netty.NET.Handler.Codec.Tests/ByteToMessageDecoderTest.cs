/*
 * Copyright 2013 The Netty Project
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
using System.Diagnostics;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Assert = XunitAssertMessages.AssertM;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Buffer.Unpooled;

namespace Netty.NET.Handler.Codec.Tests;

public class ByteToMessageDecoderTest
{
    [Fact]
    public void TestRemoveItself()
    {
        bool removed = false;
        EmbeddedChannel channel = new EmbeddedChannel(new TestDecoder((self, ctx, @in, @out) =>
        {
            Assert.False(removed);
            @in.ReadByte();
            ctx.Pipeline.Remove(self);
            removed = true;
        }));

        ByteBuf buf = Unpooled.WrappedBuffer(new byte[] { (byte) 'a', (byte) 'b', (byte) 'c' });
        channel.WriteInbound(buf.Copy());
        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(b, buf.SkipBytes(1));
        b.Release();
        buf.Release();
    }

    [Fact]
    public void TestRemoveItselfWriteBuffer()
    {
        ByteBuf buf = Unpooled.Buffer().WriteBytes(new byte[] { (byte) 'a', (byte) 'b', (byte) 'c' });
        bool removed = false;
        EmbeddedChannel channel = new EmbeddedChannel(new TestDecoder((self, ctx, @in, @out) =>
        {
            Assert.False(removed);
            @in.ReadByte();
            ctx.Pipeline.Remove(self);

            // This should not let it keep call decode
            buf.WriteByte('d');
            removed = true;
        }));

        channel.WriteInbound(buf.Copy());
        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] { (byte) 'b', (byte) 'c' });
        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(expected, b);
        expected.Release();
        buf.Release();
        b.Release();
    }

    /**
     * Verifies that internal buffer of the ByteToMessageDecoder is released once decoder is removed from pipeline. In
     * this case input is read fully.
     */
    [Fact]
    public void TestInternalBufferClearReadAll()
    {
        ByteBuf buf = Unpooled.Buffer().WriteBytes(new byte[] { (byte) 'a' });
        EmbeddedChannel channel = NewInternalBufferTestChannel();
        Assert.False(channel.WriteInbound(buf));
        Assert.False(channel.Finish());
    }

    /**
     * Verifies that internal buffer of the ByteToMessageDecoder is released once decoder is removed from pipeline. In
     * this case input was not fully read.
     */
    [Fact]
    public void TestInternalBufferClearReadPartly()
    {
        ByteBuf buf = Unpooled.Buffer().WriteBytes(new byte[] { (byte) 'a', (byte) 'b' });
        EmbeddedChannel channel = NewInternalBufferTestChannel();
        Assert.True(channel.WriteInbound(buf));
        Assert.True(channel.Finish());
        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] { (byte) 'b' });
        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(expected, b);
        Assert.Null(channel.ReadInbound());
        expected.Release();
        b.Release();
    }

    private EmbeddedChannel NewInternalBufferTestChannel()
    {
        return new EmbeddedChannel(new TestDecoder((self, ctx, @in, @out) =>
        {
            ByteBuf byteBuf = self.CallInternalBuffer();
            Assert.Equal(1, byteBuf.RefCnt);
            @in.ReadByte();
            // Removal from pipeline should clear internal buffer
            ctx.Pipeline.Remove(self);
        })
        {
            handlerRemoved0 = (self, ctx) => AssertCumulationReleased(self.CallInternalBuffer()),
        });
    }

    [Fact]
    public void HandlerRemovedWillNotReleaseBufferIfDecodeInProgress()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new TestDecoder((self, ctx, @in, @out) =>
        {
            ctx.Pipeline.Remove(self);
            Assert.True(@in.RefCnt != 0);
        })
        {
            handlerRemoved0 = (self, ctx) => AssertCumulationReleased(self.CallInternalBuffer()),
        });
        byte[] bytes = new byte[1024];
        Random.Shared.NextBytes(bytes);

        Assert.True(channel.WriteInbound(Unpooled.WrappedBuffer(bytes)));
        Assert.True(channel.FinishAndReleaseAll());
    }

    private static void AssertCumulationReleased(ByteBuf byteBuf)
    {
        Assert.True(byteBuf == null || ReferenceEquals(byteBuf, Unpooled.EmptyBuffer) || byteBuf.RefCnt == 0,
                "unexpected value: " + byteBuf);
    }

    [Fact]
    public void TestFireChannelReadCompleteOnInactive()
    {
        // CLR: LinkedBlockingDeque is a BlockingCollection over a ConcurrentQueue; take() is Take().
        BlockingCollection<int> queue = new BlockingCollection<int>(new ConcurrentQueue<int>());
        ByteBuf buf = Unpooled.Buffer().WriteBytes(new byte[] { (byte) 'a', (byte) 'b' });
        EmbeddedChannel channel = new EmbeddedChannel(new TestDecoder((self, ctx, @in, @out) =>
        {
            int readable = @in.ReadableBytes;
            Assert.True(readable > 0);
            @in.SkipBytes(readable);
        })
        {
            decodeLast = (self, ctx, @in, @out) =>
            {
                Assert.False(@in.IsReadable());
                @out.Add("data");
            },
        }, new ReadCompleteOnInactiveHandler(queue));
        Assert.False(channel.WriteInbound(buf));
        channel.Finish();
        Assert.Equal(1, queue.Take(TestContext.Current.CancellationToken));
        Assert.Equal(2, queue.Take(TestContext.Current.CancellationToken));
        Assert.Equal(3, queue.Take(TestContext.Current.CancellationToken));
        Assert.True(queue.Count == 0);
    }

    // CLR: the anonymous handler is a named nested class.
    private sealed class ReadCompleteOnInactiveHandler(BlockingCollection<int> queue) : ChannelInboundHandlerAdapter
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

    // See https://github.com/netty/netty/issues/4635
    [Fact]
    public void TestRemoveWhileInCallDecode()
    {
        object upgradeMessage = new object();
        ByteToMessageDecoder decoder = new TestDecoder((self, ctx, @in, @out) =>
        {
            Assert.Equal((byte) 'a', @in.ReadByte());
            @out.Add(upgradeMessage);
        });

        EmbeddedChannel channel = new EmbeddedChannel(decoder, new UpgradeHandler(upgradeMessage, decoder));

        ByteBuf buf = Unpooled.WrappedBuffer(new byte[] { (byte) 'a', (byte) 'b', (byte) 'c' });
        Assert.True(channel.WriteInbound(buf.Copy()));
        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(b, buf.SkipBytes(1));
        Assert.False(channel.Finish());
        buf.Release();
        b.Release();
    }

    private sealed class UpgradeHandler(object upgradeMessage, ByteToMessageDecoder decoder)
        : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg == upgradeMessage)
            {
                ctx.Pipeline.Remove(decoder);
                return;
            }

            ctx.FireChannelRead(msg);
        }
    }

    [Fact]
    public void TestDecodeLastEmptyBuffer()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new TestDecoder((self, ctx, @in, @out) =>
        {
            int readable = @in.ReadableBytes;
            Assert.True(readable > 0);
            @out.Add(@in.ReadBytes(readable));
        }));
        byte[] bytes = new byte[1024];
        Random.Shared.NextBytes(bytes);

        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(bytes)));
        AssertBuffer(Unpooled.WrappedBuffer(bytes), channel.ReadInbound<ByteBuf>());
        Assert.Null(channel.ReadInbound());
        Assert.False(channel.Finish());
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestDecodeLastNonEmptyBuffer()
    {
        bool decodeLast = false;
        EmbeddedChannel channel = new EmbeddedChannel(new TestDecoder((self, ctx, @in, @out) =>
        {
            int readable = @in.ReadableBytes;
            Assert.True(readable > 0);
            if (!decodeLast && readable == 1)
            {
                return;
            }

            @out.Add(@in.ReadBytes(decodeLast ? readable : readable - 1));
        })
        {
            decodeLast = (self, ctx, @in, @out) =>
            {
                Assert.False(decodeLast);
                decodeLast = true;
                self.CallBaseDecodeLast(ctx, @in, @out);
            },
        });
        byte[] bytes = new byte[1024];
        Random.Shared.NextBytes(bytes);

        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(bytes)));
        AssertBuffer(Unpooled.WrappedBuffer(bytes, 0, bytes.Length - 1), channel.ReadInbound<ByteBuf>());
        Assert.Null(channel.ReadInbound());
        Assert.True(channel.Finish());
        AssertBuffer(Unpooled.WrappedBuffer(bytes, bytes.Length - 1, 1), channel.ReadInbound<ByteBuf>());
        Assert.Null(channel.ReadInbound());
    }

    private static void AssertBuffer(ByteBuf expected, ByteBuf buffer)
    {
        try
        {
            Assert.Equal(expected, buffer);
        }
        finally
        {
            buffer.Release();
            expected.Release();
        }
    }

    [Fact]
    public void TestReadOnlyBuffer()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new TestDecoder((self, ctx, @in, @out) =>
        {
        }));
        Assert.False(channel.WriteInbound(Unpooled.Buffer(8).WriteByte(1).AsReadOnly()));
        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(new byte[] { (byte) 2 })));
        Assert.False(channel.Finish());
    }

    internal class WriteFailingByteBuf : UnpooledHeapByteBuf
    {
        // CLR: java.lang.Error has no separate CLR hierarchy; a plain Exception instance plays its role.
        private readonly Exception error = new Exception();
        private int untilFailure;

        internal WriteFailingByteBuf(int untilFailure, int capacity)
            : base(UnpooledByteBufAllocator.Default, capacity, capacity)
        {
            this.untilFailure = untilFailure;
        }

        public override ByteBuf SetBytes(int index, ByteBuf src, int srcIndex, int length)
        {
            if (--untilFailure <= 0)
            {
                throw error;
            }

            return base.SetBytes(index, src, srcIndex, length);
        }

        internal Exception WriteError()
        {
            return error;
        }
    }

    [Fact]
    public void ReleaseWhenMergeCumulateThrows()
    {
        WriteFailingByteBuf oldCumulation = new WriteFailingByteBuf(1, 64);
        oldCumulation.WriteZero(1);
        ByteBuf @in = Unpooled.Buffer().WriteZero(12);

        Exception thrown = null;

        try
        {
            ByteToMessageDecoder.MERGE_CUMULATOR.Cumulate(UnpooledByteBufAllocator.Default, oldCumulation, @in);
        }
        catch (Exception t)
        {
            thrown = t;
        }

        Assert.Same(oldCumulation.WriteError(), thrown);
        Assert.Equal(0, @in.RefCnt);
        Assert.Equal(1, oldCumulation.RefCnt);
        oldCumulation.Release();
    }

    [Fact]
    public void ReleaseWhenMergeCumulateThrowsInExpand()
    {
        ReleaseWhenMergeCumulateThrowsInExpand0(1, true);
        ReleaseWhenMergeCumulateThrowsInExpand0(2, true);
        ReleaseWhenMergeCumulateThrowsInExpand0(3, false); // sentinel test case
    }

    // CLR: xUnit1024 forbids a helper overload with the test's name, hence the 0 suffix.
    private void ReleaseWhenMergeCumulateThrowsInExpand0(int untilFailure, bool shouldFail)
    {
        ByteBuf oldCumulation = UnpooledByteBufAllocator.Default.HeapBuffer(8, 8).WriteZero(1);
        WriteFailingByteBuf newCumulation = new WriteFailingByteBuf(untilFailure, 16);

        IByteBufAllocator allocator = new SingleBufferAllocator(newCumulation);

        ByteBuf @in = Unpooled.Buffer().WriteZero(12);
        Exception thrown = null;

        try
        {
            ByteToMessageDecoder.MERGE_CUMULATOR.Cumulate(allocator, oldCumulation, @in);
        }
        catch (Exception t)
        {
            thrown = t;
        }

        Assert.Equal(0, @in.RefCnt);

        if (shouldFail)
        {
            Assert.Same(newCumulation.WriteError(), thrown);
            Assert.Equal(1, oldCumulation.RefCnt);
            oldCumulation.Release();
            Assert.Equal(0, newCumulation.RefCnt);
        }
        else
        {
            Assert.Null(thrown);
            Assert.Equal(0, oldCumulation.RefCnt);
            Assert.Equal(1, newCumulation.RefCnt);
            newCumulation.Release();
        }
    }

    // CLR: the anonymous allocator is a named nested class.
    private sealed class SingleBufferAllocator(ByteBuf newCumulation) : AbstractByteBufAllocator(false)
    {
        public override bool IsDirectBufferPooled => false;

        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        {
            return newCumulation;
        }

        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            throw new NotSupportedException();
        }
    }

    [Fact]
    public void ReleaseWhenCompositeCumulateThrows()
    {
        Exception error = new Exception();

        ByteBuf cumulation = new FailingCompositeByteBuf(error).WriteZero(1);
        ByteBuf @in = Unpooled.Buffer().WriteZero(12);
        Exception expected = Assert.Throws<Exception>(() =>
                ByteToMessageDecoder.COMPOSITE_CUMULATOR.Cumulate(UnpooledByteBufAllocator.Default, cumulation, @in));
        Assert.Same(error, expected);
        Assert.Equal(0, @in.RefCnt);
        cumulation.Release();
    }

    // CLR: the anonymous CompositeByteBuf is a named nested class; addComponent(boolean, ByteBuf) and
    // addFlattenedComponents(boolean, ByteBuf) are AddComponent(ByteBuf, bool) and AddFlattenedComponents(ByteBuf, bool).
    private sealed class FailingCompositeByteBuf(Exception error)
        : CompositeByteBuf(UnpooledByteBufAllocator.Default, false, 64)
    {
        public override CompositeByteBuf AddComponent(ByteBuf buffer, bool increaseWriterIndex = false)
        {
            throw error;
        }

        public override CompositeByteBuf AddFlattenedComponents(ByteBuf buffer, bool increaseWriterIndex = false)
        {
            throw error;
        }
    }

    private sealed class ReadInterceptingHandler : ChannelOutboundHandlerAdapter
    {
        internal int readsTriggered;

        public override void Read(IChannelHandlerContext ctx)
        {
            readsTriggered++;
            base.Read(ctx);
        }
    }

    [Fact]
    public void TestDoesNotOverRead()
    {
        ReadInterceptingHandler interceptor = new ReadInterceptingHandler();

        EmbeddedChannel channel = new EmbeddedChannel();
        channel.Config.SetAutoRead(false);
        channel.Pipeline.AddLast(interceptor, new FixedLengthFrameDecoder(3));
        Assert.Equal(0, interceptor.readsTriggered);

        // 0 complete frames, 1 partial frame: SHOULD trigger a read
        channel.WriteInbound(WrappedBuffer(new byte[] { 0, 1 }));
        Assert.Equal(1, interceptor.readsTriggered);

        // 2 complete frames, 0 partial frames: should NOT trigger a read
        channel.WriteInbound(WrappedBuffer(new byte[] { 2 }), WrappedBuffer(new byte[] { 3, 4, 5 }));
        Assert.Equal(1, interceptor.readsTriggered);

        // 1 complete frame, 1 partial frame: should NOT trigger a read
        channel.WriteInbound(WrappedBuffer(new byte[] { 6, 7, 8 }), WrappedBuffer(new byte[] { 9 }));
        Assert.Equal(1, interceptor.readsTriggered);

        // 1 complete frame, 1 partial frame: should NOT trigger a read
        channel.WriteInbound(WrappedBuffer(new byte[] { 10, 11 }), WrappedBuffer(new byte[] { 12 }));
        Assert.Equal(1, interceptor.readsTriggered);

        // 0 complete frames, 1 partial frame: SHOULD trigger a read
        channel.WriteInbound(WrappedBuffer(new byte[] { 13 }));
        Assert.Equal(2, interceptor.readsTriggered);

        // 1 complete frame, 0 partial frames: should NOT trigger a read
        channel.WriteInbound(WrappedBuffer(new byte[] { 14 }));
        Assert.Equal(2, interceptor.readsTriggered);

        for (int i = 0; i < 5; i++)
        {
            ByteBuf read = channel.ReadInbound<ByteBuf>();
            Assert.Equal(i * 3 + 0, read.GetByte(0));
            Assert.Equal(i * 3 + 1, read.GetByte(1));
            Assert.Equal(i * 3 + 2, read.GetByte(2));
            read.Release();
        }

        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestDoesNotOverReadOnChannelReadComplete()
    {
        ReadInterceptingHandler interceptor = new ReadInterceptingHandler();
        EmbeddedChannel channel = new EmbeddedChannel(interceptor, new TestDecoder((self, ctx, @in, @out) =>
        {
            // NOOP
        }));
        channel.Config.SetAutoRead(false);
        Assert.Equal(1, interceptor.readsTriggered);
        channel.Pipeline.FireChannelReadComplete();
        Assert.Equal(1, interceptor.readsTriggered);
        channel.Pipeline.FireChannelRead(Unpooled.Buffer().WriteZero(8));
        Assert.Equal(1, interceptor.readsTriggered);
        // This should trigger a read() as we did not forward any message.
        channel.Pipeline.FireChannelReadComplete();
        Assert.Equal(2, interceptor.readsTriggered);
        // Explicit calling fireChannelReadComplete() again without calling fireChannelRead(...) before should
        // not trigger another read()
        channel.Pipeline.FireChannelReadComplete();
        Assert.Equal(2, interceptor.readsTriggered);
        channel.Pipeline.FireChannelRead(Unpooled.Buffer().WriteZero(8));
        Assert.Equal(2, interceptor.readsTriggered);

        // This should trigger a read() as we did not forward any message.
        channel.Pipeline.FireChannelReadComplete();
        Assert.Equal(3, interceptor.readsTriggered);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void TestDisorder()
    {
        int count = 0;
        ByteToMessageDecoder decoder = new TestDecoder((self, ctx, @in, @out) =>
        {
            //read 4 byte then remove this decoder
            @out.Add(@in.ReadByte());

            if (++count >= 4)
            {
                ctx.Pipeline.Remove(self);
            }
        });
        EmbeddedChannel channel = new EmbeddedChannel(decoder);
        Assert.True(channel.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5 })));
        Assert.Equal((byte) 1, channel.ReadInbound<byte>());
        Assert.Equal((byte) 2, channel.ReadInbound<byte>());
        Assert.Equal((byte) 3, channel.ReadInbound<byte>());
        Assert.Equal((byte) 4, channel.ReadInbound<byte>());
        ByteBuf buffer5 = channel.ReadInbound<ByteBuf>();
        Assert.Equal((byte) 5, buffer5.ReadByte());
        Assert.False(buffer5.IsReadable());
        Assert.True(buffer5.Release());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestDecodeLast()
    {
        // CLR: AtomicBoolean is a captured bool (the channel runs every handler on the test thread).
        bool removeHandler = false;
        EmbeddedChannel channel = new EmbeddedChannel(new TestDecoder((self, ctx, @in, @out) =>
        {
            if (removeHandler)
            {
                ctx.Pipeline.Remove(self);
            }
        }));
        byte[] bytes = new byte[1024];
        Random.Shared.NextBytes(bytes);

        Assert.False(channel.WriteInbound(Unpooled.CopiedBuffer(bytes)));
        Assert.Null(channel.ReadInbound());
        removeHandler = true;
        // This should trigger channelInputClosed(...)
        channel.Pipeline.FireUserEventTriggered(ChannelInputShutdownEvent.INSTANCE);

        Assert.True(channel.Finish());
        AssertBuffer(Unpooled.WrappedBuffer(bytes), channel.ReadInbound<ByteBuf>());
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestUnexpectRead()
    {
        EmbeddedChannel channel = new EmbeddedChannel();
        channel.Config.SetAutoRead(false);
        ReadInterceptingHandler interceptor = new ReadInterceptingHandler();
        channel.Pipeline.AddLast(
                interceptor,
                new ReplacingHandler()
        );

        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 1 })));
        Assert.Equal(0, interceptor.readsTriggered);
        Assert.NotNull(channel.Pipeline.Get<FixedLengthFrameDecoder>());
        Assert.False(channel.Finish());
    }

    // CLR: the anonymous SimpleChannelInboundHandler is a named nested class.
    private sealed class ReplacingHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            ctx.Pipeline.Replace(this, "fix", new FixedLengthFrameDecoder(3));
        }
    }

    [Fact]
    public void TestReuseInputBufferJustLargeEnoughToContainMessage_MergeCumulator()
    {
        TestReusedBuffer(Unpooled.Buffer(16), false, ByteToMessageDecoder.MERGE_CUMULATOR);
    }

    [Fact]
    public void TestReuseInputBufferJustLargeEnoughToContainMessagePartiallyReceived2x_MergeCumulator()
    {
        TestReusedBuffer(Unpooled.Buffer(16), true, ByteToMessageDecoder.MERGE_CUMULATOR);
    }

    [Fact]
    public void TestReuseInputBufferSufficientlyLargeToContainDuplicateMessage_MergeCumulator()
    {
        TestReusedBuffer(Unpooled.Buffer(1024), false, ByteToMessageDecoder.MERGE_CUMULATOR);
    }

    [Fact]
    public void TestReuseInputBufferSufficientlyLargeToContainDuplicateMessagePartiallyReceived2x_MergeCumulator()
    {
        TestReusedBuffer(Unpooled.Buffer(1024), true, ByteToMessageDecoder.MERGE_CUMULATOR);
    }

    [Fact]
    public void TestReuseInputBufferJustLargeEnoughToContainMessage_CompositeCumulator()
    {
        TestReusedBuffer(Unpooled.Buffer(16), false, ByteToMessageDecoder.COMPOSITE_CUMULATOR);
    }

    [Fact]
    public void TestReuseInputBufferJustLargeEnoughToContainMessagePartiallyReceived2x_CompositeCumulator()
    {
        TestReusedBuffer(Unpooled.Buffer(16), true, ByteToMessageDecoder.COMPOSITE_CUMULATOR);
    }

    [Fact]
    public void TestReuseInputBufferSufficientlyLargeToContainDuplicateMessage_CompositeCumulator()
    {
        TestReusedBuffer(Unpooled.Buffer(1024), false, ByteToMessageDecoder.COMPOSITE_CUMULATOR);
    }

    [Fact]
    public void TestReuseInputBufferSufficientlyLargeToContainDuplicateMessagePartiallyReceived2x_CompositeCumulator()
    {
        TestReusedBuffer(Unpooled.Buffer(1024), true, ByteToMessageDecoder.COMPOSITE_CUMULATOR);
    }

    internal static void TestReusedBuffer(ByteBuf buffer, bool secondPartial, ByteToMessageDecoder.ICumulator cumulator)
    {
        ByteToMessageDecoder decoder = new TestDecoder((self, ctx, @in, @out) =>
        {
            while (@in.ReadableBytes >= 4)
            {
                int index = @in.ReaderIndex;
                int len = @in.ReadInt();
                Debug.Assert(len < (1 << 30), "In-plausibly long message: " + len);

                if (@in.ReadableBytes >= len)
                {
                    byte[] bytes = new byte[len];
                    @in.ReadBytes(bytes);
                    string message = Encoding.UTF8.GetString(bytes);
                    @out.Add(message);
                }
                else
                {
                    @in.ReaderIndex = index;
                    return;
                }
            }
        });
        decoder.SetCumulator(cumulator);
        EmbeddedChannel channel = new EmbeddedChannel(decoder);

        buffer.Retain(); // buffer is allocated from the pool, the pool would call retain()
        buffer.WriteInt(11); // total length of message
        buffer.WriteByte('h').WriteByte('e').WriteByte('l').WriteByte('l');

        if (secondPartial)
        {
            Assert.False(channel.WriteInbound(buffer)); // try reading incomplete message
            Assert.True(channel.InboundMessages().Count == 0);
            Assert.Equal(0, buffer.ReaderIndex, "Incomplete message should still be readable in buffer");
            buffer.Retain(); // buffer is allocated from the pool - reusing same buffer, the pool would call retain()
        }

        buffer.WriteByte('o').WriteByte(' ');
        Assert.False(channel.WriteInbound(buffer)); // try reading incomplete message
        Assert.True(channel.InboundMessages().Count == 0);
        Assert.Equal(0, buffer.ReaderIndex, "Incomplete message should still be readable in buffer");

        buffer.Retain(); // buffer is allocated from the pool - reusing same buffer, the pool would call retain()
        buffer.WriteByte('w').WriteByte('o').WriteByte('r').WriteByte('l').WriteByte('d');
        Assert.True(channel.WriteInbound(buffer));
        Assert.False(channel.InboundMessages().Count == 0, "Message should be received");
        Assert.Equal("hello world", channel.InboundMessages().Dequeue(), "Message should be received correctly");
        Assert.True(channel.InboundMessages().Count == 0, "Only a single message should be received");
        Assert.False(buffer.IsReadable(), "Buffer should not have remaining data after reading complete message");

        buffer.Release(); // we are done with the buffer - release it from the pool
        Assert.Equal(0, buffer.RefCnt, "Buffer should be released");
        Assert.False(channel.Finish());
    }

    [Fact]
    public void ReentrantReadSafety()
    {
        EmbeddedChannel channel = new EmbeddedChannel();
        int reentrancy = 0;
        ByteToMessageDecoder decoder = new TestDecoder((self, ctx, @in, @out) =>
        {
            reentrancy++;

            if (reentrancy == 1)
            {
                ByteBuf buf2 = channel.Alloc().Buffer();
                buf2.WriteLong(42); // Adding 8 bytes.
                Assert.False(channel.WriteInbound(buf2)); // Reentrant call back into ByteToMessageDecoder
                ctx.Read();
            }

            int bytes = @in.ReadableBytes;
            @out.Add(bytes);
            @in.SkipBytes(bytes);
        });
        channel.Pipeline.AddLast(decoder);
        ByteBuf buf1 = channel.Alloc().Buffer();
        buf1.WriteInt(42); // Adding 4 bytes.
        Assert.True(channel.WriteInbound(buf1));
        int first = channel.ReadInbound<int>();
        int second = channel.ReadInbound<int>();
        Assert.Equal(4, first);
        Assert.Equal(8, second);
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void ReentrantReadThenRemoveSafety()
    {
        EmbeddedChannel channel = new EmbeddedChannel();
        bool removed = false;
        int reentrancy = 0;
        ByteToMessageDecoder decoder = new TestDecoder((self, ctx, @in, @out) =>
        {
            Assert.False(removed);
            reentrancy++;

            if (reentrancy == 1)
            {
                ByteBuf buf2 = channel.Alloc().Buffer();
                buf2.WriteLong(42); // Adding 8 bytes.
                Assert.False(channel.WriteInbound(buf2)); // Reentrant call back into ByteToMessageDecoder
                ByteBuf buf3 = channel.Alloc().Buffer();
                buf3.WriteShort(42); // Adding 2 bytes.
                Assert.False(channel.WriteInbound(buf3)); // Reentrant call back into ByteToMessageDecoder
                ctx.Read();
            }
            else if (reentrancy == 2)
            {
                ctx.Pipeline.Remove(self);
            }

            int bytes = @in.ReadableBytes;
            @out.Add(bytes);
            @in.SkipBytes(bytes);
        })
        {
            handlerRemoved0 = (self, ctx) => removed = true,
        };
        channel.Pipeline.AddLast(decoder);
        ByteBuf buf1 = channel.Alloc().Buffer();
        buf1.WriteInt(42); // Adding 4 bytes.
        Assert.True(channel.WriteInbound(buf1));
        int first = channel.ReadInbound<int>();
        int second = channel.ReadInbound<int>();
        int third = channel.ReadInbound<int>();
        Assert.Equal(4, first);
        Assert.Equal(8, second);
        Assert.Equal(2, third);
        Assert.False(channel.FinishAndReleaseAll());
    }

    // CLR: the source's anonymous ByteToMessageDecoder subclasses are one delegate-driven subclass. Each delegate gets
    // the decoder itself for the source's 'this', internalBuffer() and super.decodeLast(...); fields of the anonymous
    // classes are captured locals.
    private sealed class TestDecoder : ByteToMessageDecoder
    {
        private readonly Action<TestDecoder, IChannelHandlerContext, ByteBuf, IList<object>> decode;
        internal Action<TestDecoder, IChannelHandlerContext, ByteBuf, IList<object>> decodeLast;
        internal Action<TestDecoder, IChannelHandlerContext> handlerRemoved0;

        internal TestDecoder(Action<TestDecoder, IChannelHandlerContext, ByteBuf, IList<object>> decode)
        {
            this.decode = decode;
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            decode(this, ctx, @in, @out);
        }

        protected override void DecodeLast(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            if (decodeLast == null)
            {
                base.DecodeLast(ctx, @in, @out);
            }
            else
            {
                decodeLast(this, ctx, @in, @out);
            }
        }

        protected override void HandlerRemoved0(IChannelHandlerContext ctx)
        {
            handlerRemoved0?.Invoke(this, ctx);
        }

        internal ByteBuf CallInternalBuffer()
        {
            return InternalBuffer();
        }

        internal void CallBaseDecodeLast(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            base.DecodeLast(ctx, @in, @out);
        }
    }
}
