/*
 * Copyright 2025 The Netty Project
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

using System.Collections.Generic;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: xUnit runs the inherited [Fact]/[Theory] methods of an abstract base class for each derived test class, as JUnit
// does. @TestInstance(PER_CLASS) creates "a new test instance ... once per test class"
// (https://docs.junit.org/current/writing-tests/test-instance-lifecycle.html), which lets the instance methods
// smallData()/largeData() be @MethodSource factories ("Factory methods within the test class must be static unless the
// test class is annotated with" PER_CLASS, https://docs.junit.org/current/writing-tests/parameterized-classes-and-tests.html).
// xUnit member data must be a "public static member on the test class"
// (https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/Attributes/MemberDataAttributeBase.cs) and is
// read before an instance exists, while the data depends on the subclass's createCompressor(). The instance methods
// smallData() and largeData() therefore keep their Java shape (subclasses call them), the static
// SmallDataIndexes()/LargeDataIndexes() supply the index of the heap (0) and the direct (1) buffer of their result, and
// each theory takes that buffer and releases the other one (DataAt). Decompressor is IDecompressor and try-with-resources
// is a using statement over its Dispose(), which calls Close() (see IDecompressor); status() is Status_().
// ByteBufAllocator.DEFAULT is IByteBufAllocator.Default, ChannelHandler is IChannelHandler, StandardCharsets.UTF_8 is
// Encoding.UTF8, ch.close().isSuccess() is CloseAsync().IsCompletedSuccessfully (the EmbeddedChannel completes the close
// before it returns), the Buffer port's addComponent(true, buffer) is AddComponent(buffer, true), and
// rand.nextInt(100) is rand.Next(100). assertDoesNotThrow(decompressor::close) is Assert.Null(Record.Exception(...)),
// which "Returns the exception that was thrown by the code; null, otherwise"
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/Record.cs), and the AssertionError of an unknown status is
// Assert.Fail.
public abstract class AbstractDecompressorTest : AbstractCompressionTest
{
    protected static readonly ByteBuf WRAPPED_BYTES_SMALL = Unpooled.UnreleasableBuffer(
            Unpooled.WrappedBuffer(BYTES_SMALL)).AsReadOnly();
    protected static readonly ByteBuf WRAPPED_BYTES_LARGE = Unpooled.UnreleasableBuffer(
            Unpooled.WrappedBuffer(BYTES_LARGE)).AsReadOnly();

    protected abstract IChannelHandler CreateCompressor();

    protected abstract IDecompressor.AbstractDecompressorBuilder CreateDecompressor();

    public ByteBuf[] SmallData()
    {
        return Data(CompressToByteArray(BYTES_SMALL));
    }

    public ByteBuf[] LargeData()
    {
        return Data(CompressToByteArray(BYTES_LARGE));
    }

    // CLR: the static member data of the theories: the indexes of the buffers that SmallData()/LargeData() return.
    public static IEnumerable<object[]> SmallDataIndexes()
    {
        yield return new object[] { 0 };
        yield return new object[] { 1 };
    }

    public static IEnumerable<object[]> LargeDataIndexes()
    {
        yield return new object[] { 0 };
        yield return new object[] { 1 };
    }

    // CLR: the buffer of one data row; the other buffers of the array, which JUnit passes to the other invocations, are
    // released.
    private static ByteBuf DataAt(ByteBuf[] data, int dataIndex)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (i != dataIndex)
            {
                data[i].Release();
            }
        }
        return data[dataIndex];
    }

    [Theory]
    [MemberData(nameof(SmallDataIndexes))]
    public void TestDecompressionOfSmallChunkOfData(int dataIndex)
    {
        TestDecompression(WRAPPED_BYTES_SMALL.Duplicate(), DataAt(SmallData(), dataIndex));
    }

    [Theory]
    [MemberData(nameof(LargeDataIndexes))]
    public void TestDecompressionOfLargeChunkOfData(int dataIndex)
    {
        TestDecompression(WRAPPED_BYTES_LARGE.Duplicate(), DataAt(LargeData(), dataIndex));
    }

    [Theory]
    [MemberData(nameof(LargeDataIndexes))]
    public void TestDecompressionOfBatchedFlowOfData(int dataIndex)
    {
        TestDecompressionOfBatchedFlow(WRAPPED_BYTES_LARGE.Duplicate(), DataAt(LargeData(), dataIndex));
    }

    private static ByteBuf[] Data(byte[] compressedBytes)
    {
        ByteBuf heap = Unpooled.WrappedBuffer((byte[]) compressedBytes.Clone());
        ByteBuf direct = Unpooled.DirectBuffer(compressedBytes.Length);
        direct.WriteBytes(compressedBytes);
        return new ByteBuf[] { heap, direct };
    }

    protected virtual void TestDecompression(ByteBuf expected, ByteBuf data)
    {
        using (IDecompressor decompressor = CreateDecompressor().Build(IByteBufAllocator.Default))
        {
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            decompressor.AddInput(data);
            ByteBuf decompressed = ReadDecompressed(decompressor);
            try
            {
                Assert.Equal(expected, decompressed);
            }
            finally
            {
                decompressed.Release();
            }

            // test that .close is idempotent.
            Assert.Null(Record.Exception(decompressor.Close));
        }
    }

    protected virtual void TestDecompressionOfBatchedFlow(ByteBuf expected, ByteBuf data)
    {
        using (IDecompressor decompressor = CreateDecompressor().Build(IByteBufAllocator.Default))
        {
            CompositeByteBuf decompressed = IByteBufAllocator.Default.CompositeBuffer();
            try
            {
                int compressedLength = data.ReadableBytes;
                int written = 0;
                int length = rand.Next(100);
                while (written + length < compressedLength)
                {
                    FeedInput(decompressor, decompressed, data.RetainedSlice(written, length));
                    written += length;
                    length = rand.Next(100);
                }
                FeedInput(decompressor, decompressed, data.RetainedSlice(written, compressedLength - written));
                FinishDecompression(decompressor, decompressed);
                Assert.Equal(expected, decompressed);
            }
            finally
            {
                decompressed.Release();
                data.Release();
            }
        }
    }

    private byte[] CompressToByteArray(byte[] data)
    {
        ByteBuf compressed = Compress(data);
        try
        {
            byte[] bytes = new byte[compressed.ReadableBytes];
            compressed.GetBytes(compressed.ReaderIndex, bytes);
            return bytes;
        }
        finally
        {
            compressed.Release();
        }
    }

    private ByteBuf Compress(byte[] data)
    {
        EmbeddedChannel ch = new EmbeddedChannel(CreateCompressor());
        ch.WriteOutbound(Unpooled.WrappedBuffer(data));
        Assert.True(ch.CloseAsync().IsCompletedSuccessfully);
        Assert.True(ch.Finish());

        CompositeByteBuf composite = ch.Alloc().CompositeBuffer();
        while (true)
        {
            ByteBuf b = ch.ReadOutbound<ByteBuf>();
            if (b == null)
            {
                break;
            }
            composite.AddComponent(b, true);
        }
        return composite;
    }

    private static ByteBuf ReadDecompressed(IDecompressor decompressor)
    {
        CompositeByteBuf decompressed = IByteBufAllocator.Default.CompositeBuffer();
        try
        {
            FinishDecompression(decompressor, decompressed);
            ByteBuf result = decompressed;
            decompressed = null;
            return result;
        }
        finally
        {
            if (decompressed != null)
            {
                decompressed.Release();
            }
        }
    }

    private static void FeedInput(IDecompressor decompressor, CompositeByteBuf decompressed, ByteBuf input)
    {
        Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
        decompressor.AddInput(input);
        DrainOutput(decompressor, decompressed);
    }

    private static void FinishDecompression(IDecompressor decompressor, CompositeByteBuf decompressed)
    {
        bool sentEof = false;
        while (true)
        {
            switch (decompressor.Status_())
            {
                case IDecompressor.Status.NEED_INPUT:
                    Assert.False(sentEof);
                    sentEof = true;
                    decompressor.EndOfInput();
                    break;
                case IDecompressor.Status.NEED_OUTPUT:
                    decompressed.AddComponent(decompressor.TakeOutput(), true);
                    break;
                case IDecompressor.Status.COMPLETE:
                    return;
                default:
                    Assert.Fail("Unknown status: " + decompressor.Status_());
                    break;
            }
        }
    }

    private static void DrainOutput(IDecompressor decompressor, CompositeByteBuf decompressed)
    {
        while (decompressor.Status_() == IDecompressor.Status.NEED_OUTPUT)
        {
            decompressed.AddComponent(decompressor.TakeOutput(), true);
        }
    }

    [Fact]
    public void CompleteAfterEndOfInput()
    {
        ByteBuf compressed = Compress(Encoding.UTF8.GetBytes("foo"));

        using (IDecompressor decompressor = CreateDecompressor().Build(IByteBufAllocator.Default))
        {
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            decompressor.AddInput(compressed);

            ByteBuf decompressed = ReadDecompressed(decompressor);
            try
            {
                Assert.Equal("foo", decompressed.ToString(Encoding.UTF8));
            }
            finally
            {
                decompressed.Release();
            }
        }
    }
}
