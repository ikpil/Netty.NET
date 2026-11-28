/*
 * Copyright 2014 The Netty Project
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
using System.Security.Cryptography;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: xUnit runs the inherited [Fact] methods of an abstract base class for each derived test class, as JUnit does.
// The shared java.util.Random, whose instances "are threadsafe"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Random.html), is Random.Shared, "a thread-safe
// Random instance that may be used concurrently from any thread"
// (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0): xUnit runs different test classes
// in parallel, and a plain System.Random is "not thread safe" (https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-10.0).
// SplittableRandom is not ported: new SplittableRandom(rand.nextLong()) is a Random seeded from the shared random, and
// PlatformDependent.splittableRandomNextBytes(...) is its NextBytes, as the Transport testsuite ports do
// (test/Netty.NET.Transport.Tests/SocketEchoSourceTest.cs); the tests need random data, not particular bytes.
// The conditional i % 4 != 0 ? 0 : (byte) ... casts its 0 to byte, because C# gives the conditional the type int where Java
// gives it the type byte. CharsetUtil.UTF_8 is Encoding.UTF8, char literals in byte[] initializers are cast to byte as Java narrows them
// implicitly, Arrays.fill is Array.Fill, readOutbound()/readInbound() are ReadOutbound<T>()/ReadInbound<T>(), the Buffer
// port's addComponent(true, buffer) is AddComponent(buffer, true), and close() is CloseAsync(), whose Task the source
// ignores. Non-final methods are virtual.
public abstract class AbstractIntegrationTest
{

    protected static readonly Random rand = Random.Shared;

    protected EmbeddedChannel encoder;
    protected EmbeddedChannel decoder;

    protected abstract EmbeddedChannel CreateEncoder();

    protected abstract EmbeddedChannel CreateDecoder();

    public virtual void InitChannels()
    {
        encoder = CreateEncoder();
        decoder = CreateDecoder();
    }

    public virtual void CloseChannels()
    {
        _ = encoder.CloseAsync();

        for (;;)
        {
            object msg = encoder.ReadOutbound();
            if (msg == null)
            {
                break;
            }

            ReferenceCountUtil.Release(msg);
        }

        _ = decoder.CloseAsync();

        for (;;)
        {
            object msg = decoder.ReadInbound();
            if (msg == null)
            {
                break;
            }

            ReferenceCountUtil.Release(msg);
        }
    }

    [Fact]
    public void TestEmpty()
    {
        TestIdentity(EmptyArrays.EMPTY_BYTES, true);
        TestIdentity(EmptyArrays.EMPTY_BYTES, false);
    }

    [Fact]
    public void TestOneByte()
    {
        byte[] data = { (byte) 'A' };
        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void TestTwoBytes()
    {
        byte[] data = { (byte) 'B', (byte) 'A' };
        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void TestRegular()
    {
        byte[] data = Encoding.UTF8.GetBytes("Netty is a NIO client server framework which enables " +
                "quick and easy development of network applications such as protocol " +
                "servers and clients.");
        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void TestLargeRandom()
    {
        byte[] data = new byte[1024 * 1024];
        new Random(rand.Next()).NextBytes(data);
        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void TestPartRandom()
    {
        byte[] data = new byte[10240];
        new Random(rand.Next()).NextBytes(data);

        for (int i = 0; i < 1024; i++)
        {
            data[i] = 2;
        }

        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void TestCompressible()
    {
        Random rng = new Random(rand.Next());
        byte[] data = new byte[10240];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = i % 4 != 0 ? (byte) 0 : (byte) rng.Next();
        }

        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void TestLongBlank()
    {
        byte[] data = new byte[102400];
        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void TestLongSame()
    {
        byte[] data = new byte[102400];
        Array.Fill(data, (byte) 123);
        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    [Fact]
    public void TestSequential()
    {
        byte[] data = new byte[1024];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte) i;
        }

        TestIdentity(data, true);
        TestIdentity(data, false);
    }

    protected virtual void TestIdentity(byte[] data, bool heapBuffer)
    {
        InitChannels();
        ByteBuf @in = heapBuffer? Unpooled.WrappedBuffer(data) :
                Unpooled.DirectBuffer(data.Length).WriteBytes(data);
        CompositeByteBuf compressed = Unpooled.CompositeBuffer();
        CompositeByteBuf decompressed = Unpooled.CompositeBuffer();

        try
        {
            Assert.True(encoder.WriteOutbound(@in.Retain()));
            Assert.True(encoder.Finish());

            ByteBuf msg;

            while ((msg = encoder.ReadOutbound<ByteBuf>()) != null)
            {
                compressed.AddComponent(msg, true);
            }

            decoder.WriteInbound(compressed.Retain());
            Assert.False(compressed.IsReadable());

            while ((msg = decoder.ReadInbound<ByteBuf>()) != null)
            {
                decompressed.AddComponent(msg, true);
            }

            @in.ReaderIndex = 0;
            Assert.Equal(@in, decompressed);
        }
        finally
        {
            compressed.Release();
            decompressed.Release();
            @in.Release();
            CloseChannels();
        }
    }

    // CLR: @DisabledForSlowLeakDetection only skips the source under paranoid leak detection; the CLR test always runs,
    // as for the Buffer port (test/Netty.NET.Buffer.Tests/AdaptiveByteBufAllocatorTest.cs). MessageDigest SHA-256 is
    // IncrementalHash.CreateHash(HashAlgorithmName.SHA256), which computes "a hash ... incrementally across several
    // segments" (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash?view=net-10.0);
    // digest() is GetHashAndReset(). A digest of a byte stream does not depend on how the stream is split, so each 1 MiB
    // chunk is built in an array and written and hashed at once instead of byte by byte (one native hash call per byte
    // would make the 256 MiB loop take minutes), and the handler hashes each received buffer's segments where the source
    // visits every byte with forEachByte. The Buffer port's PooledByteBufAllocator is IDisposable and is disposed at the
    // end. metric().usedHeapMemory()/usedDirectMemory() are the Metric.UsedHeapMemory/UsedDirectMemory properties, and
    // the AssertJ isLessThan is Assert.True with <.
    [Fact]
    public void TestHugeDecompress()
    {
        int chunkSize = 1024 * 1024;
        int numberOfChunks = 256;
        int memoryLimit = chunkSize * 128;
        using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        EmbeddedChannel compressChannel = CreateEncoder();
        ByteBuf compressed = compressChannel.Alloc().Buffer();
        byte[] chunk = new byte[chunkSize];

        for (int i = 0; i <= numberOfChunks; i++)
        {
            if (i < numberOfChunks)
            {
                ByteBuf @in = compressChannel.Alloc().Buffer(chunkSize);

                for (int j = 0; j < chunkSize; j++)
                {
                    byte byteValue = (byte) (i + (j & 0xA0));
                    chunk[j] = byteValue;
                }

                @in.WriteBytes(chunk);
                digest.AppendData(chunk);
                compressChannel.WriteOutbound(@in);
            }
            else
            {
#pragma warning disable xUnit1051 // The source's close() observes no cancellation; the EmbeddedChannel closes synchronously.
                _ = compressChannel.CloseAsync();
#pragma warning restore xUnit1051
            }

            while (true)
            {
                ByteBuf buf = compressChannel.ReadOutbound<ByteBuf>();
                if (buf == null)
                {
                    break;
                }

                compressed.WriteBytes(buf);
                buf.Release();
            }
        }

        byte[] expectedData = digest.GetHashAndReset();

        using PooledByteBufAllocator allocator = new PooledByteBufAllocator(false);

        HugeDecompressIncomingHandler endHandler = new HugeDecompressIncomingHandler(memoryLimit, digest);
        EmbeddedChannel decompressChannel = CreateDecoder();
        decompressChannel.Pipeline.AddLast(endHandler);
        decompressChannel.Config.SetAllocator(allocator);
        decompressChannel.WriteInbound(compressed);
        decompressChannel.FinishAndReleaseAll();
        Assert.Equal((long) chunkSize * numberOfChunks, endHandler.total);
        Assert.Equal(expectedData, digest.GetHashAndReset());
    }

    // CLR: the class does not implement ByteProcessor (a Func<byte, bool> in the Buffer port); process(byte) is the
    // per-segment digest update in channelRead (see TestHugeDecompress).
    private sealed class HugeDecompressIncomingHandler : ChannelInboundHandlerAdapter
    {
        internal readonly int memoryLimit;
        internal readonly IncrementalHash digest;
        internal long total;

        internal HugeDecompressIncomingHandler(int memoryLimit, IncrementalHash digest)
        {
            this.memoryLimit = memoryLimit;
            this.digest = digest;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf buf = (ByteBuf) msg;
            total += buf.ReadableBytes;

            try
            {
                foreach (ReadOnlyMemory<byte> segment in buf.AsReadOnlySequence(buf.ReaderIndex, buf.ReadableBytes))
                {
                    digest.AppendData(segment.Span);
                }

                PooledByteBufAllocator allocator = (PooledByteBufAllocator) ctx.Alloc();
                Assert.True(allocator.Metric.UsedHeapMemory < memoryLimit);
                Assert.True(allocator.Metric.UsedDirectMemory < memoryLimit);
            }
            finally
            {
                buf.Release();
            }
        }
    }
}
