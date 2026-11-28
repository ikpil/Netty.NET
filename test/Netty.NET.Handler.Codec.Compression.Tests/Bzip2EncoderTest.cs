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

using System.IO;
using ICSharpCode.SharpZipLib.BZip2;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the inherited AbstractEncoderTest tests run for this class (createChannel and decompress are the overrides
// CreateChannel and Decompress). commons-compress BZip2CompressorInputStream is SharpZipLib's BZip2InputStream(Stream),
// the independent bzip2 implementation of the C# tests (owner decision 2026-11-23); it reads the block size from the
// stream header ('h' and a digit 1..9) as commons-compress does
// (https://raw.githubusercontent.com/icsharpcode/SharpZipLib/v1.4.2/src/ICSharpCode.SharpZipLib/BZip2/BZip2InputStream.cs).
// Its Read(byte[], int, int) returns "zero if the end of the stream is reached" where InputStream.read returns -1, which
// the loop's read > 0 test covers in both languages, and ReadByte() returns "byte read or -1 on end of stream", so
// assertEquals(-1, bzip2Is.read()) is Assert.Equal(-1, bzip2Is.ReadByte()). try-with-resources is two using
// statements; BZip2InputStream disposes the ByteBufInputStream it owns (IsStreamOwner defaults to true) and the Buffer
// port's ByteBufInputStream releases its buffer only on the first Dispose (src/Netty.NET.Buffer/ByteBufInputStream.cs),
// as Java closes both streams.
public class Bzip2EncoderTest : AbstractEncoderTest
{

    protected override EmbeddedChannel CreateChannel()
    {
        return new EmbeddedChannel(new Bzip2Encoder(MIN_BLOCK_SIZE));
    }

    protected override ByteBuf Decompress(ByteBuf compressed, int originalLength)
    {

        byte[] decompressed = new byte[originalLength];

        using (Stream @is = new ByteBufInputStream(compressed, true))
        using (BZip2InputStream bzip2Is = new BZip2InputStream(@is))
        {

            int remaining = originalLength;

            while (remaining > 0)
            {
                int read = bzip2Is.Read(decompressed, originalLength - remaining, remaining);
                if (read > 0)
                {
                    remaining -= read;
                }
                else
                {
                    break;
                }
            }

            Assert.Equal(-1, bzip2Is.ReadByte());
        }

        return Unpooled.WrappedBuffer(decompressed);
    }
}
