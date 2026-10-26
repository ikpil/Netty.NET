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
using System.IO;
using System.Net;
using System.Net.Sockets;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufGatheringTest : IDisposable
{
    private readonly NativeMemoryAllocator _native = new();
    private readonly UnpooledByteBufAllocator _unpooled;
    private PooledByteBufAllocator _pooled;
    private PooledByteBufAllocator Pooled => _pooled ??= new PooledByteBufAllocator(false,
        maxOrder: 4, smallCacheSize: 0, nativeAllocator: _native);
    public CompositeByteBufGatheringTest() => _unpooled = new UnpooledByteBufAllocator(false, _native);
    private ByteBuf Buffer(int capacity = 256) => _unpooled.HeapBuffer(capacity);
    private ByteBuf DirectBuffer(int capacity = 256) => _unpooled.DirectBuffer(capacity);
    private CompositeByteBuf CompositeBuffer() => _unpooled.CompositeHeapBuffer();
    public void Dispose()
    {
        _pooled?.Dispose();
        Assert.Equal(0, _unpooled.Metric.UsedHeapMemory);
        Assert.Equal(0, _unpooled.Metric.UsedDirectMemory);
        Assert.Equal(0, _native.ReservedBytes);
    }

    // Socket.Send is not overridable. This test-only virtual output hook models the
    // original channel's one-byte limit. Production ReadBytes must advance by the
    // real returned count, not the original request; every byte still goes over TCP.
    private sealed class ShortWriteComposite(IByteBufAllocator allocator) : CompositeByteBuf(allocator, false)
    {
        public override int GetBytes(int index, Socket destination, int length)
            => base.GetBytes(index, destination, Math.Min(1, length));
    }

    private sealed class TestGatheringByteChannel : IDisposable
    {
        internal Socket Socket { get; }
        private readonly Socket _peer;
        internal TestGatheringByteChannel()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Socket.Connect(listener.LocalEndPoint);
            _peer = listener.Accept();
            Socket.NoDelay = _peer.NoDelay = true;
            Socket.SendTimeout = _peer.ReceiveTimeout = 5000;
        }
        internal byte[] WrittenBytes(int length)
        {
            Socket.Shutdown(SocketShutdown.Send);
            using var output = new MemoryStream();
            byte[] chunk = new byte[256];
            int count;
            while ((count = _peer.Receive(chunk)) != 0)
            {
                output.Write(chunk, 0, count);
                Assert.InRange(output.Length, 0, length);
            }
            Assert.Equal(length, output.Length);
            return output.ToArray();
        }
        public void Dispose() { Socket.Dispose(); _peer.Dispose(); }
    }

    [Fact]
    public void TestCompositeWrappedBuffer()
    {
        ByteBuf header = Buffer(12);
        ByteBuf payload = Buffer(512);

        header.WriteBytes(new byte[12]);
        payload.WriteBytes(new byte[512]);

        ByteBuf buffer = Unpooled.WrappedBuffer(header, payload);

        Assert.Equal(12, header.ReadableBytes);
        Assert.Equal(512, payload.ReadableBytes);

        Assert.Equal(12 + 512, buffer.ReadableBytes);
        int segments = 0;
        foreach (ReadOnlyMemory<byte> segment in buffer.ReadableSequence)
            ++segments;
        Assert.Equal(2, segments);

        buffer.Release();

    }

    [Fact]
    public void TestSeveralBuffersEquals()
    {
        ByteBuf a, b;
        // XXX Same tests with several buffers in wrappedCheckedBuffer
        // Different length.
        a = Unpooled.WrappedBuffer(new byte[] { 1 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 1 }),
                Unpooled.WrappedBuffer(new byte[] { 2 }));
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();

        // Same content, same firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 1 }),
                Unpooled.WrappedBuffer(new byte[] { 2 }),
                Unpooled.WrappedBuffer(new byte[] { 3 }));
        Assert.True(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();

        // Same content, different firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4 }, 1, 2),
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4 }, 3, 1));
        Assert.True(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();

        // Different content, same firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 1, 2 }),
                Unpooled.WrappedBuffer(new byte[] { 4 }));
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();

        // Different content, different firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 4, 5 }, 1, 2),
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 4, 5 }, 3, 1));
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();

        // Same content, same firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }),
                Unpooled.WrappedBuffer(new byte[] { 4, 5, 6 }),
                Unpooled.WrappedBuffer(new byte[] { 7, 8, 9, 10 }));
        Assert.True(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();

        // Same content, different firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 1, 5),
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 6, 5));
        Assert.True(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();

        // Different content, same firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 6 }),
                Unpooled.WrappedBuffer(new byte[] { 7, 8, 5, 9, 10 }));
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();

        // Different content, different firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 6, 7, 8, 5, 9, 10, 11 }, 1, 5),
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 6, 7, 8, 5, 9, 10, 11 }, 6, 5));
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();

    }

    [Fact]
    public void TestWrappedBuffer()
    {
        ByteBuf a = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(DirectBuffer(16).SetIndex(0, 16)));
        Assert.Equal(16, a.Capacity);
        a.Release();

        a = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
        ByteBuf b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[][] { new byte[] { 1, 2, 3 } }));
        Assert.Equal(a, b);

        a.Release();
        b.Release();

        a = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(
                new byte[] { 1 },
                new byte[] { 2 },
                new byte[] { 3 }));
        Assert.Equal(a, b);

        a.Release();
        b.Release();

        a = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
        b = Unpooled.WrappedBuffer(new ByteBuf[] {
                Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 })
        });
        Assert.Equal(a, b);

        a.Release();
        b.Release();

        a = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 1 }),
                Unpooled.WrappedBuffer(new byte[] { 2 }),
                Unpooled.WrappedBuffer(new byte[] { 3 }));
        Assert.Equal(a, b);

        a.Release();
        b.Release();

        a = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new Memory<byte>[] {
                (new byte[] { 1, 2, 3 }).AsMemory()
        }));
        Assert.Equal(a, b);

        a.Release();
        b.Release();

        a = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }));
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(
                (new byte[] { 1 }).AsMemory(),
                (new byte[] { 2 }).AsMemory(),
                (new byte[] { 3 }).AsMemory()));
        Assert.Equal(a, b);

        a.Release();
        b.Release();

    }

    [Fact]
    public void TestWrittenBuffersEquals()
    {
        //XXX Same tests than testEquals with written AggregateChannelBuffers
        ByteBuf a, b, c;
        // Different length.
        a = Unpooled.WrappedBuffer(new byte[] { 1 });
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1 }, new byte[1]));
        c = Unpooled.WrappedBuffer(new byte[] { 2 });

        // to enable writeBytes
        b.WriterIndex = b.WriterIndex - 1;
        b.WriteBytes(c);
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();
        c.Release();

        // Same content, same firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1 }, new byte[2]));
        c = Unpooled.WrappedBuffer(new byte[] { 2 });

        // to enable writeBytes
        b.WriterIndex = b.WriterIndex - 2;
        b.WriteBytes(c);
        c.Release();
        c = Unpooled.WrappedBuffer(new byte[] { 3 });

        b.WriteBytes(c);
        Assert.True(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();
        c.Release();

        // Same content, different firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4 }, 1, 3));
        c = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4 }, 3, 1);
        // to enable writeBytes
        b.WriterIndex = b.WriterIndex - 1;
        b.WriteBytes(c);
        Assert.True(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();
        c.Release();

        // Different content, same firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1, 2 }, new byte[1]));
        c = Unpooled.WrappedBuffer(new byte[] { 4 });
        // to enable writeBytes
        b.WriterIndex = b.WriterIndex - 1;
        b.WriteBytes(c);
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();
        c.Release();

        // Different content, different firstIndex, short length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 });
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 4, 5 }, 1, 3));
        c = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 4, 5 }, 3, 1);
        // to enable writeBytes
        b.WriterIndex = b.WriterIndex - 1;
        b.WriteBytes(c);
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();
        c.Release();

        // Same content, same firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3 }, new byte[7]));
        c = Unpooled.WrappedBuffer(new byte[] { 4, 5, 6 });

        // to enable writeBytes
        b.WriterIndex = b.WriterIndex - 7;
        b.WriteBytes(c);
        c.Release();
        c = Unpooled.WrappedBuffer(new byte[] { 7, 8, 9, 10 });
        b.WriteBytes(c);
        Assert.True(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();
        c.Release();

        // Same content, different firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 1, 10));
        c = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, 6, 5);
        // to enable writeBytes
        b.WriterIndex = b.WriterIndex - 5;
        b.WriteBytes(c);
        Assert.True(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();
        c.Release();

        // Different content, same firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 6 }, new byte[5]));
        c = Unpooled.WrappedBuffer(new byte[] { 7, 8, 5, 9, 10 });
        // to enable writeBytes
        b.WriterIndex = b.WriterIndex - 5;
        b.WriteBytes(c);
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();
        c.Release();

        // Different content, different firstIndex, long length.
        a = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        b = Unpooled.WrappedBuffer(
                Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 6, 7, 8, 5, 9, 10, 11 }, 1, 10));
        c = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 6, 7, 8, 5, 9, 10, 11 }, 6, 5);
        // to enable writeBytes
        b.WriterIndex = b.WriterIndex - 5;
        b.WriteBytes(c);
        Assert.False(ByteBufUtil.Equals(a, b));

        a.Release();
        b.Release();
        c.Release();

    }

    [Fact]
    public void TestGatheringWritesHeap()
    {
        TestGatheringWrites(Buffer(), Buffer());

    }

    [Fact]
    public void TestGatheringWritesDirect()
    {
        TestGatheringWrites(DirectBuffer(), DirectBuffer());

    }

    [Fact]
    public void TestGatheringWritesMixes()
    {
        TestGatheringWrites(Buffer(), DirectBuffer());

    }

    [Fact]
    public void TestGatheringWritesHeapPooled()
    {
        TestGatheringWrites(Pooled.HeapBuffer(),
                Pooled.HeapBuffer());

    }

    [Fact]
    public void TestGatheringWritesDirectPooled()
    {
        TestGatheringWrites(Pooled.DirectBuffer(),
                Pooled.DirectBuffer());

    }

    [Fact]
    public void TestGatheringWritesMixesPooled()
    {
        TestGatheringWrites(Pooled.HeapBuffer(),
                Pooled.DirectBuffer());

    }

    [Fact]
    public void TestGatheringWritesPartialHeap()
    {
        TestGatheringWritesPartial(Buffer(), Buffer(), false);

    }

    [Fact]
    public void TestGatheringWritesPartialDirect()
    {
        TestGatheringWritesPartial(DirectBuffer(), DirectBuffer(), false);

    }

    [Fact]
    public void TestGatheringWritesPartialMixes()
    {
        TestGatheringWritesPartial(Buffer(), DirectBuffer(), false);

    }

    [Fact]
    public void TestGatheringWritesPartialHeapSlice()
    {
        TestGatheringWritesPartial(Buffer(), Buffer(), true);

    }

    [Fact]
    public void TestGatheringWritesPartialDirectSlice()
    {
        TestGatheringWritesPartial(DirectBuffer(), DirectBuffer(), true);

    }

    [Fact]
    public void TestGatheringWritesPartialMixesSlice()
    {
        TestGatheringWritesPartial(Buffer(), DirectBuffer(), true);

    }

    [Fact]
    public void TestGatheringWritesPartialHeapPooled()
    {
        TestGatheringWritesPartial(Pooled.HeapBuffer(),
                Pooled.HeapBuffer(), false);

    }

    [Fact]
    public void TestGatheringWritesPartialDirectPooled()
    {
        TestGatheringWritesPartial(Pooled.DirectBuffer(),
                Pooled.DirectBuffer(), false);

    }

    [Fact]
    public void TestGatheringWritesPartialMixesPooled()
    {
        TestGatheringWritesPartial(Pooled.HeapBuffer(),
                Pooled.DirectBuffer(), false);

    }

    [Fact]
    public void TestGatheringWritesPartialHeapPooledSliced()
    {
        TestGatheringWritesPartial(Pooled.HeapBuffer(),
                Pooled.HeapBuffer(), true);

    }

    [Fact]
    public void TestGatheringWritesPartialDirectPooledSliced()
    {
        TestGatheringWritesPartial(Pooled.DirectBuffer(),
                Pooled.DirectBuffer(), true);

    }

    [Fact]
    public void TestGatheringWritesPartialMixesPooledSliced()
    {
        TestGatheringWritesPartial(Pooled.HeapBuffer(),
                Pooled.DirectBuffer(), true);

    }

    [Fact]
    public void TestGatheringWritesSingleHeap()
    {
        TestGatheringWritesSingleBuf(Buffer());

    }

    [Fact]
    public void TestGatheringWritesSingleDirect()
    {
        TestGatheringWritesSingleBuf(DirectBuffer());

    }

    [Fact]
    public void TestInternalNioBuffer()
    {
        CompositeByteBuf buf = CompositeBuffer();
        Assert.True(buf.AsMemory(0, 0).IsEmpty);

        // If non-derived buffer is added, its internal buffer should be returned
        ByteBuf concreteBuffer = DirectBuffer().WriteByte(1);
        buf.AddComponent(concreteBuffer);
        // CLR Memory has no mutable NIO cursor/cache identity. Preserve live shared
        // storage and bounded independent views instead, without fabricating identity.
        Memory<byte> borrow = buf.AsMemory(0, 1);
        concreteBuffer.SetByte(0, 2);
        Assert.Equal((byte)2, borrow.Span[0]);
        Assert.True(buf.AsMemory(0, 0).IsEmpty);
        Assert.Equal(1, borrow.Length);
        Assert.Equal((byte)2, borrow.Span[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buf.AsMemory(0, 2));
        buf.Release();

        // In derived cases, the original internal buffer must not be used
        buf = CompositeBuffer();
        concreteBuffer = DirectBuffer().WriteByte(1);
        buf.AddComponent(concreteBuffer.Slice());
        borrow = buf.AsMemory(0, 1);
        concreteBuffer.SetByte(0, 2);
        Assert.Equal((byte)2, borrow.Span[0]);
        Assert.True(buf.AsMemory(0, 0).IsEmpty);
        Assert.Equal(1, borrow.Length);
        Assert.Equal((byte)2, borrow.Span[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buf.AsMemory(0, 2));
        buf.Release();

        buf = CompositeBuffer();
        concreteBuffer = DirectBuffer().WriteByte(1);
        buf.AddComponent(concreteBuffer.Duplicate());
        borrow = buf.AsMemory(0, 1);
        concreteBuffer.SetByte(0, 2);
        Assert.Equal((byte)2, borrow.Span[0]);
        Assert.True(buf.AsMemory(0, 0).IsEmpty);
        Assert.Equal(1, borrow.Length);
        Assert.Equal((byte)2, borrow.Span[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buf.AsMemory(0, 2));
        buf.Release();
    }

    [Fact]
    public void TestisDirectMultipleBufs()
    {
        CompositeByteBuf buf = CompositeBuffer();
        Assert.False(buf.IsDirect);

        buf.AddComponent(DirectBuffer().WriteByte(1));

        Assert.True(buf.IsDirect);
        buf.AddComponent(DirectBuffer().WriteByte(1));
        Assert.True(buf.IsDirect);

        buf.AddComponent(Buffer().WriteByte(1));
        Assert.False(buf.IsDirect);

        buf.Release();

    }

    private void TestGatheringWrites(ByteBuf buf1, ByteBuf buf2)
    {
        CompositeByteBuf buf = CompositeBuffer();
        buf.AddComponent(buf1.WriteBytes(new byte[] { 1, 2 }));
        buf.AddComponent(buf2.WriteBytes(new byte[] { 1, 2 }));
        buf.WriterIndex = 3;
        buf.ReaderIndex = 1;
        using var channel = new TestGatheringByteChannel();
        try
        {
            Assert.Equal(2, buf.ReadBytes(channel.Socket, 2));
            byte[] data = new byte[2];
            buf.GetBytes(1, data);
            Assert.Equal(data, channel.WrittenBytes(2));
        }
        finally { buf.Release(); }
    }

    private void TestGatheringWritesPartial(ByteBuf buf1, ByteBuf buf2, bool slice)
    {
        CompositeByteBuf buf = new ShortWriteComposite(_unpooled);
        buf1.WriteBytes(new byte[] { 1, 2, 3, 4 });
        buf2.WriteBytes(new byte[] { 1, 2, 3, 4 });
        if (slice)
        {
            buf1.ReaderIndex = 1;
            buf1 = buf1.Slice();
            buf2.WriterIndex = 3;
            buf2 = buf2.Slice();
            buf.AddComponent(buf1);
            buf.AddComponent(buf2);
            buf.WriterIndex = 6;
        }
        else
        {
            buf.AddComponent(buf1);
            buf.AddComponent(buf2);
            buf.WriterIndex = 7;
            buf.ReaderIndex = 1;
        }
        using var channel = new TestGatheringByteChannel();
        try
        {
            while (buf.IsReadable)
            {
                int before = buf.ReaderIndex;
                int written = buf.ReadBytes(channel.Socket, buf.ReadableBytes);
                Assert.Equal(1, written);
                Assert.Equal(before + written, buf.ReaderIndex);
            }
            byte[] data = new byte[6];
            if (slice)
                buf.GetBytes(0, data);
            else
                buf.GetBytes(1, data);
            Assert.Equal(data, channel.WrittenBytes(6));
        }
        finally { buf.Release(); }
    }

    private void TestGatheringWritesSingleBuf(ByteBuf buf1)
    {
        CompositeByteBuf buf = CompositeBuffer();
        buf.AddComponent(buf1.WriteBytes(new byte[] { 1, 2, 3, 4 }));
        buf.WriterIndex = 3;
        buf.ReaderIndex = 1;
        using var channel = new TestGatheringByteChannel();
        try
        {
            Assert.Equal(2, buf.ReadBytes(channel.Socket, 2));
            byte[] data = new byte[2];
            buf.GetBytes(1, data);
            Assert.Equal(data, channel.WrittenBytes(2));
        }
        finally { buf.Release(); }
    }
}
