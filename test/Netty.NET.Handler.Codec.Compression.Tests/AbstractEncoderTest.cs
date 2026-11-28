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

// CLR: xUnit creates one test class instance per test and runs the inherited theories of an abstract base class for each
// derived test class, as JUnit does. The final @BeforeEach initChannel() runs from IAsyncLifetime.InitializeAsync,
// "Called immediately after the class has been created, before it is used"
// (https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/IAsyncLifetime.cs), so that createChannel() runs
// after every constructor of the derived class as in JUnit; the @AfterEach destroyChannel() runs from DisposeAsync and
// stays virtual for the subclasses that override it. The static @MethodSource factories smallData()/largeData() are the
// static member data SmallData()/LargeData(), whose rows hold the heap and the direct buffer; xUnit member data yields
// object[] rows. DisableDiscoveryEnumeration "skip[s] enumerating data during discovery"
// (https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/Attributes/MemberDataAttributeBase.cs), so the
// buffers are only created when the theory runs, where each row is consumed and released by its test.
// rand.nextInt(100) is rand.Next(100), readOutbound() is ReadOutbound<ByteBuf>(), and the Buffer port's
// addComponent(true, buffer) is AddComponent(buffer, true).
public abstract class AbstractEncoderTest : AbstractCompressionTest, IAsyncLifetime
{

    protected EmbeddedChannel channel;

    /**
     * Decompresses data with some external library.
     */
    protected abstract ByteBuf Decompress(ByteBuf compressed, int originalLength);

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

    public static IEnumerable<object[]> SmallData()
    {
        ByteBuf heap = Unpooled.WrappedBuffer(BYTES_SMALL);
        ByteBuf direct = Unpooled.DirectBuffer(BYTES_SMALL.Length);
        direct.WriteBytes(BYTES_SMALL);
        return new[] { new object[] {heap}, new object[] {direct} };
    }

    public static IEnumerable<object[]> LargeData()
    {
        ByteBuf heap = Unpooled.WrappedBuffer(BYTES_LARGE);
        ByteBuf direct = Unpooled.DirectBuffer(BYTES_LARGE.Length);
        direct.WriteBytes(BYTES_LARGE);
        return new[] { new object[] {heap}, new object[] {direct} };
    }

    [Theory]
    [MemberData(nameof(SmallData), DisableDiscoveryEnumeration = true)]
    public void TestCompressionOfSmallChunkOfData(ByteBuf data)
    {
        TestCompression(data);
    }

    [Theory]
    [MemberData(nameof(LargeData), DisableDiscoveryEnumeration = true)]
    public void TestCompressionOfLargeChunkOfData(ByteBuf data)
    {
        TestCompression(data);
    }

    [Theory]
    [MemberData(nameof(LargeData), DisableDiscoveryEnumeration = true)]
    public void TestCompressionOfBatchedFlowOfData(ByteBuf data)
    {
        TestCompressionOfBatchedFlow(data);
    }

    protected virtual void TestCompression(ByteBuf data)
    {
        int dataLength = data.ReadableBytes;
        Assert.True(channel.WriteOutbound(data.Retain()));
        Assert.True(channel.Finish());
        Assert.Equal(0, data.ReadableBytes);

        ByteBuf decompressed = ReadDecompressed(dataLength);
        Assert.Equal(data.ResetReaderIndex(), decompressed);

        decompressed.Release();
        data.Release();
    }

    // CLR: the loop's in and the later in are one local declared before the loop, because C# reports a nested local of
    // the same name "regardless of the textual order of the variable declarations"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0136); in is @in.
    protected virtual void TestCompressionOfBatchedFlow(ByteBuf data)
    {
        int dataLength = data.ReadableBytes;
        int written = 0, length = rand.Next(100);
        ByteBuf @in;

        while (written + length < dataLength)
        {
            @in = data.RetainedSlice(written, length);
            Assert.True(channel.WriteOutbound(@in));
            Assert.Equal(0, @in.ReadableBytes);
            written += length;
            length = rand.Next(100);
        }

        @in = data.RetainedSlice(written, dataLength - written);
        Assert.True(channel.WriteOutbound(@in));
        Assert.True(channel.Finish());
        Assert.Equal(0, @in.ReadableBytes);

        ByteBuf decompressed = ReadDecompressed(dataLength);
        Assert.Equal(data, decompressed);

        decompressed.Release();
        data.Release();
    }

    protected virtual ByteBuf ReadDecompressed(int dataLength)
    {
        CompositeByteBuf compressed = Unpooled.CompositeBuffer();
        ByteBuf msg;

        while ((msg = channel.ReadOutbound<ByteBuf>()) != null)
        {
            compressed.AddComponent(msg, true);
        }

        return Decompress(compressed, dataLength);
    }
}
