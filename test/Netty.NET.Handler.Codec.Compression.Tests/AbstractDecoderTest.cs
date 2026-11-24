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

using System.Collections.Generic;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: xUnit creates one test class instance per test and runs the inherited [Fact]/[Theory] methods of an abstract base
// class for each derived test class, as JUnit does. The source constructor (compress) stays the constructor. The final
// @BeforeEach initChannel() runs from IAsyncLifetime.InitializeAsync, "Called immediately after the class has been
// created, before it is used" (https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/IAsyncLifetime.cs),
// so that, as in JUnit, createChannel() runs after every constructor of the derived class; the @AfterEach destroyChannel()
// runs from DisposeAsync and stays virtual for the subclasses that override it.
// @TestInstance(PER_CLASS) creates "a new test instance ... once per test class"
// (https://docs.junit.org/current/writing-tests/test-instance-lifecycle.html), which lets the instance methods
// smallData()/largeData() be @MethodSource factories ("Factory methods within the test class must be static unless the
// test class is annotated with" PER_CLASS, https://docs.junit.org/current/writing-tests/parameterized-classes-and-tests.html).
// xUnit member data must be a "public static member on the test class"
// (https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/Attributes/MemberDataAttributeBase.cs) and is
// read before an instance exists, while the data depends on the subclass's compress(). The instance methods smallData()
// and largeData() therefore keep their Java shape, the static SmallDataIndexes()/LargeDataIndexes() supply the index of
// the heap (0) and the direct (1) buffer of their result, and each theory takes that buffer and releases the other one
// (DataAt). The compressed bytes are computed per test instance instead of once per class.
// rand.nextInt(100) is rand.Next(100), readInbound() is ReadInbound<ByteBuf>(), and the Buffer port's
// addComponent(true, buffer) is AddComponent(buffer, true).
public abstract class AbstractDecoderTest : AbstractCompressionTest, IAsyncLifetime
{

    protected static readonly ByteBuf WRAPPED_BYTES_SMALL = Unpooled.UnreleasableBuffer(
            Unpooled.WrappedBuffer(BYTES_SMALL)).AsReadOnly();
    protected static readonly ByteBuf WRAPPED_BYTES_LARGE = Unpooled.UnreleasableBuffer(
            Unpooled.WrappedBuffer(BYTES_LARGE)).AsReadOnly();

    protected EmbeddedChannel channel;

    protected byte[] compressedBytesSmall;
    protected byte[] compressedBytesLarge;

    protected AbstractDecoderTest()
    {
        compressedBytesSmall = Compress(BYTES_SMALL);
        compressedBytesLarge = Compress(BYTES_LARGE);
    }

    /**
     * Compresses data with some external library.
     */
    protected abstract byte[] Compress(byte[] data);

    public ValueTask InitializeAsync()
    {
        InitChannel();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DestroyChannel();
        return ValueTask.CompletedTask;
    }

    public void InitChannel()
    {
        channel = CreateChannel();
    }

    protected abstract EmbeddedChannel CreateChannel();

    public virtual void DestroyChannel()
    {
        if (channel != null)
        {
            channel.FinishAndReleaseAll();
            channel = null;
        }
    }

    public ByteBuf[] SmallData()
    {
        ByteBuf heap = Unpooled.WrappedBuffer(compressedBytesSmall);
        ByteBuf direct = Unpooled.DirectBuffer(compressedBytesSmall.Length);
        direct.WriteBytes(compressedBytesSmall);
        return new ByteBuf[] {heap, direct};
    }

    public ByteBuf[] LargeData()
    {
        ByteBuf heap = Unpooled.WrappedBuffer(compressedBytesLarge);
        ByteBuf direct = Unpooled.DirectBuffer(compressedBytesLarge.Length);
        direct.WriteBytes(compressedBytesLarge);
        return new ByteBuf[] {heap, direct};
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

    protected virtual void TestDecompression(ByteBuf expected, ByteBuf data)
    {
        Assert.True(channel.WriteInbound(data));

        ByteBuf decompressed = ReadDecompressed(channel);
        Assert.Equal(expected, decompressed);

        decompressed.Release();
    }

    // CLR: the loop's compressedBuf and the later compressedBuf are one local declared before the loop, because C# reports
    // a nested local of the same name "regardless of the textual order of the variable declarations"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0136).
    protected virtual void TestDecompressionOfBatchedFlow(ByteBuf expected, ByteBuf data)
    {
        int compressedLength = data.ReadableBytes;
        int written = 0, length = rand.Next(100);
        ByteBuf compressedBuf;
        while (written + length < compressedLength)
        {
            compressedBuf = data.RetainedSlice(written, length);
            channel.WriteInbound(compressedBuf);
            written += length;
            length = rand.Next(100);
        }
        compressedBuf = data.Slice(written, compressedLength - written);
        Assert.True(channel.WriteInbound(compressedBuf.Retain()));

        ByteBuf decompressedBuf = ReadDecompressed(channel);
        Assert.Equal(expected, decompressedBuf);

        decompressedBuf.Release();
        data.Release();
    }

    protected static ByteBuf ReadDecompressed(EmbeddedChannel channel)
    {
        CompositeByteBuf decompressed = Unpooled.CompositeBuffer();
        ByteBuf msg;
        while ((msg = channel.ReadInbound<ByteBuf>()) != null)
        {
            decompressed.AddComponent(msg, true);
        }
        return decompressed;
    }

    protected static void TryDecodeAndCatchBufLeaks(EmbeddedChannel channel, ByteBuf data)
    {
        try
        {
            channel.WriteInbound(data);
        }
        finally
        {
            for (;;)
            {
                ByteBuf inflated = channel.ReadInbound<ByteBuf>();
                if (inflated == null)
                {
                    break;
                }
                inflated.Release();
            }
            channel.Finish();
        }
    }
}
