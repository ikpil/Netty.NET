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

using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the inherited AbstractDecompressorTest tests run for this class (createCompressor and createDecompressor are the
// overrides CreateCompressor and CreateDecompressor). ChannelHandler is IChannelHandler, Decompressor is IDecompressor,
// status() is Status_() and Bzip2Decompressor.builder() is Builder_() (CS0102, see Bzip2Decompressor),
// ByteBufAllocator.DEFAULT is IByteBufAllocator.Default, and try-with-resources is a using statement whose Dispose()
// calls Close(). decompressor::endOfInput is the method group EndOfInput and the lambda keeps the source call;
// assertThrows with a message is Assert.Throws from Xunit.v3.AssertMessages (DecompressionException has no subclasses).
// The char literals of the byte[] initializer are cast to byte, as Java narrows them implicitly.
public class Bzip2DecompressorTest : AbstractDecompressorTest
{
    protected override IChannelHandler CreateCompressor()
    {
        return new Bzip2Encoder();
    }

    protected override IDecompressor.AbstractDecompressorBuilder CreateDecompressor()
    {
        return Bzip2Decompressor.Builder_();
    }

    [Fact]
    public void EndOfInputFailsOnTruncatedStream()
    {
        using (IDecompressor decompressor = CreateDecompressor().Build(IByteBufAllocator.Default))
        {
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            decompressor.AddInput(Unpooled.WrappedBuffer(new byte[] { (byte) 'B', (byte) 'Z', (byte) 'h', (byte) '1' }));

            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            Assert.Throws<DecompressionException>(decompressor.EndOfInput);
        }
    }

    /**
     * Regression test: the unary-coded MTF selector index read in {@code RECEIVE_SELECTORS} was
     * never validated against the number of declared Huffman tables before being used to index
     * into {@link Bzip2MoveToFrontTable} and later the per-table code-limit array, allowing a
     * crafted stream to trigger an {@link ArrayIndexOutOfBoundsException} instead of a clean
     * {@link DecompressionException}.
     */
    [Fact]
    public void TestSelectorIndexOutOfRange()
    {
        using (IDecompressor decompressor = CreateDecompressor().Build(IByteBufAllocator.Default))
        {
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            Assert.Throws<DecompressionException>(() => decompressor.AddInput(
                    Unpooled.WrappedBuffer(Bzip2MalformedStreams.SelectorIndexOutOfRange())),
                    "incorrect selector index");
        }
    }
}
