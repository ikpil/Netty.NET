/*
 * Copyright 2026 The Netty Project
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
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the char literals of the byte[] initializers are cast to byte, because only int constants in range convert
// implicitly (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
public class FastLzFrameDecoderTest
{

    [Fact]
    public void TestCompressedBlockWithEmptyPayload()
    {
        AssertDecompressionException(new byte[] {
                (byte) 'F', (byte) 'L', (byte) 'Z',
                0x01,
                0x00, 0x00,
                0x00, 0x01
        });
    }

    [Fact]
    public void TestCompressedBlockWithTruncatedMatch()
    {
        AssertDecompressionException(new byte[] {
                (byte) 'F', (byte) 'L', (byte) 'Z',
                0x01,
                0x00, 0x03,
                0x00, 0x04,
                0x00, (byte) 'A', 0x20
        });
    }

    private static void AssertDecompressionException(byte[] input)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new FastLzFrameDecoder());
        try
        {
            Assert.Throws<DecompressionException>(() => channel.WriteInbound(Unpooled.WrappedBuffer(input)));
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }
}
