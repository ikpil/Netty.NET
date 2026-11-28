/*
 * Copyright 2024 The Netty Project
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: Supplier<ByteBuf> and Function<ByteBuf, ByteBuf> are Func<ByteBuf> and Func<ByteBuf, ByteBuf>; the Java enum with
// fields is a sealed class with static readonly instances (docs/codec-base-porting.md, Base64Dialect), and
// @EnumSource is [MemberData] over its values, as codec-mqtt passes MqttVersion
// (test/Netty.NET.Handler.Codec.Mqtt.Tests/MqttCodecTest.cs). A direct java.nio.ByteBuffer is a Memory<byte> over an
// array with the longs written big-endian, and asReadOnlyBuffer() is the ReadOnlyMemory<byte> view
// (docs/buffer-porting.md: "NIO ByteBuffer inputs/views use Span and ReadOnlyMemory"). channel.newPromise() is a
// TaskCompletionSource, the aggregate producer of AbstractCoalescingBufferQueue.Remove. The anonymous queue subclass is a
// named nested class.
public class SslHandlerCoalescingBufferQueueTest
{
    internal static readonly Func<ByteBuf> BYTEBUF_SUPPLIER = () =>
    {
        ByteBuf buf = Unpooled.DirectBuffer(128);
        buf.WriteLong(1);
        buf.WriteLong(2);
        return buf;
    };
    internal static readonly Func<ByteBuf, ByteBuf> NO_WRAPPER = byteBuf => byteBuf;
    internal static readonly Func<ByteBuf, ByteBuf> DUPLICATE_WRAPPER = byteBuf => byteBuf.Duplicate();
    internal static readonly Func<ByteBuf, ByteBuf> SLICE_WRAPPER = byteBuf => byteBuf.Slice();

    internal static Memory<byte> CreateNioBuffer()
    {
        byte[] nioBuffer = new byte[128];
        BinaryPrimitives.WriteInt64BigEndian(nioBuffer.AsSpan(0), 1);
        BinaryPrimitives.WriteInt64BigEndian(nioBuffer.AsSpan(8), 2);
        return nioBuffer;
    }

    public sealed class CumulationTestScenario
    {
        public static readonly CumulationTestScenario BASIC_NIO_BUFFER = new CumulationTestScenario(
                nameof(BASIC_NIO_BUFFER), () => Unpooled.WrappedBuffer(CreateNioBuffer()), NO_WRAPPER);
        public static readonly CumulationTestScenario READ_ONLY_AND_DUPLICATE_NIO_BUFFER = new CumulationTestScenario(
                nameof(READ_ONLY_AND_DUPLICATE_NIO_BUFFER),
                () => Unpooled.WrappedBuffer((ReadOnlyMemory<byte>) CreateNioBuffer()), DUPLICATE_WRAPPER);
        public static readonly CumulationTestScenario BASIC_DIRECT_BUFFER = new CumulationTestScenario(
                nameof(BASIC_DIRECT_BUFFER), BYTEBUF_SUPPLIER, NO_WRAPPER);
        public static readonly CumulationTestScenario DUPLICATE_DIRECT_BUFFER = new CumulationTestScenario(
                nameof(DUPLICATE_DIRECT_BUFFER), BYTEBUF_SUPPLIER, DUPLICATE_WRAPPER);
        public static readonly CumulationTestScenario SLICED_DIRECT_BUFFER = new CumulationTestScenario(
                nameof(SLICED_DIRECT_BUFFER), BYTEBUF_SUPPLIER, SLICE_WRAPPER);

        private readonly string name;
        internal readonly Func<ByteBuf> bufferSupplier;
        internal readonly Func<ByteBuf, ByteBuf> bufferWrapper;

        private CumulationTestScenario(string name, Func<ByteBuf> bufferSupplier, Func<ByteBuf, ByteBuf> bufferWrapper)
        {
            this.name = name;
            this.bufferSupplier = bufferSupplier;
            this.bufferWrapper = bufferWrapper;
        }

        internal static CumulationTestScenario[] Values()
        {
            return new[]
            {
                BASIC_NIO_BUFFER, READ_ONLY_AND_DUPLICATE_NIO_BUFFER, BASIC_DIRECT_BUFFER, DUPLICATE_DIRECT_BUFFER,
                SLICED_DIRECT_BUFFER,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    public static IEnumerable<object[]> CumulationTestScenarios()
    {
        return CumulationTestScenario.Values().Select(scenario => new object[] { scenario });
    }

    [Theory]
    [MemberData(nameof(CumulationTestScenarios))]
    public void TestCumulation(CumulationTestScenario testScenario)
    {
        EmbeddedChannel channel = new EmbeddedChannel();
        SslHandlerCoalescingBufferQueue queue = new TestQueue(channel, 16, false);

        ByteBuf original = testScenario.bufferSupplier();
        original.WriterIndex = 8;
        ByteBuf first = testScenario.bufferWrapper(original);
        first.Retain();
        queue.Add(first);
        ByteBuf second = Unpooled.CopyLong(3);
        queue.Add(second);

        TaskCompletionSource promise = new TaskCompletionSource();
        Assert.False(queue.IsEmpty);
        ByteBuf buffer = queue.Remove(UnpooledByteBufAllocator.Default, 128, promise);

        try
        {
            Assert.Equal(16, buffer.ReadableBytes);
            Assert.Equal(1, buffer.ReadLong());
            Assert.Equal(3, buffer.ReadLong());
        }
        finally
        {
            buffer.Release();
        }

        Assert.True(queue.IsEmpty);
        Assert.Equal(8, original.WriterIndex);
        original.WriterIndex = original.Capacity;
        Assert.Equal(2, original.GetLong(8));
        first.Release();
        Assert.Equal(0, first.RefCnt);
        Assert.Equal(0, second.RefCnt);
    }

    private sealed class TestQueue : SslHandlerCoalescingBufferQueue
    {
        internal TestQueue(IChannel channel, int initSize, bool wantsDirectBuffer)
            : base(channel, initSize, wantsDirectBuffer)
        {
        }

        protected override int WrapDataSize()
        {
            return 128;
        }
    }
}
