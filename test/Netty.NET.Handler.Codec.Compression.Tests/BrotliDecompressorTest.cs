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

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: Decompressor is IDecompressor, status() is Status_() and builder() is Builder_() (see BrotliDecompressor);
// ChannelHandler is IChannelHandler and ByteBufAllocator.DEFAULT is IByteBufAllocator.Default. try-with-resources is a
// using statement whose Dispose() calls Close() (see IDecompressor); the decompressor is closed before the finally block
// runs, as in Java, because the using statement sits inside the try. assertThrows is Assert.Throws, which requires the
// exact type, and DecompressionException is what EndOfInput throws.
public class BrotliDecompressorTest : AbstractDecompressorTest
{
    protected override IChannelHandler CreateCompressor()
    {
        return new BrotliEncoder();
    }

    protected override IDecompressor.AbstractDecompressorBuilder CreateDecompressor()
    {
        return BrotliDecompressor.Builder_();
    }

    [Fact]
    public void ReportsOutputBeforeMoreInput()
    {
        ByteBuf[] data = LargeData();

        try
        {
            using (IDecompressor decompressor = CreateDecompressor().Build(IByteBufAllocator.Default))
            {
                Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
                decompressor.AddInput(data[0]);
                data[0] = null;

                Assert.Equal(IDecompressor.Status.NEED_OUTPUT, decompressor.Status_());
                decompressor.TakeOutput().Release();
            }
        }
        finally
        {
            foreach (ByteBuf buffer in data)
            {
                if (buffer != null)
                {
                    buffer.Release();
                }
            }
        }
    }

    [Fact]
    public void EndOfInputRejectsTruncatedStream()
    {
        ByteBuf[] data = SmallData();
        ByteBuf truncated = data[0].ReadRetainedSlice(data[0].ReadableBytes - 1);

        try
        {
            using (IDecompressor decompressor = CreateDecompressor().Build(IByteBufAllocator.Default))
            {
                Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
                decompressor.AddInput(truncated);
                truncated = null;

                while (decompressor.Status_() == IDecompressor.Status.NEED_OUTPUT)
                {
                    decompressor.TakeOutput().Release();
                }

                Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
                Assert.Throws<DecompressionException>(decompressor.EndOfInput);
            }
        }
        finally
        {
            if (truncated != null)
            {
                truncated.Release();
            }

            foreach (ByteBuf buffer in data)
            {
                buffer.Release();
            }
        }
    }
}
