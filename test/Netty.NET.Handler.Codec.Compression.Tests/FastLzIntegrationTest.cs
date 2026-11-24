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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the inherited tests of AbstractIntegrationTest run for this class and for its two public nested classes, which
// xUnit discovers as test classes as JUnit does for static member classes; createEncoder()/createDecoder() implement
// the abstract members. rand.nextBoolean() is rand.Next(2) == 0: Next(2) returns "A 32-bit signed integer that is
// greater than or equal to 0, and less than maxValue" (https://learn.microsoft.com/en-us/dotnet/api/system.random.next?view=net-10.0),
// so each value is equally likely. SplittableRandom is not ported (see AbstractIntegrationTest): new
// SplittableRandom(rand.nextLong()) is a Random seeded from the shared random and rng.nextInt(100) is rng.Next(100); the
// batch sizes only need to be random. The in of the first loop and the later in are one local @in declared before the
// loop, because a local declared after the loop would be in scope in the loop's nested block too
// (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0136), as AbstractEncoderTest does. readOutbound()/
// readInbound() are ReadOutbound<ByteBuf>()/ReadInbound<ByteBuf>() and the Buffer port's addComponent(true, buffer) is
// AddComponent(buffer, true).
public class FastLzIntegrationTest : AbstractIntegrationTest
{

    public class TestWithChecksum : AbstractIntegrationTest
    {

        protected override EmbeddedChannel CreateEncoder()
        {
            return new EmbeddedChannel(new FastLzFrameEncoder(true));
        }

        protected override EmbeddedChannel CreateDecoder()
        {
            return new EmbeddedChannel(new FastLzFrameDecoder(true));
        }
    }

    public class TestRandomChecksum : AbstractIntegrationTest
    {

        protected override EmbeddedChannel CreateEncoder()
        {
            return new EmbeddedChannel(new FastLzFrameEncoder(rand.Next(2) == 0));
        }

        protected override EmbeddedChannel CreateDecoder()
        {
            return new EmbeddedChannel(new FastLzFrameDecoder(rand.Next(2) == 0));
        }
    }

    protected override EmbeddedChannel CreateEncoder()
    {
        return new EmbeddedChannel(new FastLzFrameEncoder(rand.Next(2) == 0));
    }

    protected override EmbeddedChannel CreateDecoder()
    {
        return new EmbeddedChannel(new FastLzFrameDecoder(rand.Next(2) == 0));
    }

    // test batched flow of data
    protected override void TestIdentity(byte[] data, bool heapBuffer)
    {
        InitChannels();
        ByteBuf original = heapBuffer? Unpooled.WrappedBuffer(data) :
                Unpooled.DirectBuffer(data.Length).WriteBytes(data);
        CompositeByteBuf compressed = Unpooled.CompositeBuffer();
        CompositeByteBuf decompressed = Unpooled.CompositeBuffer();

        try
        {
            Random rng = new Random(rand.Next());
            int written = 0, length = rng.Next(100);
            ByteBuf @in;
            while (written + length < data.Length)
            {
                @in = Unpooled.WrappedBuffer(data, written, length);
                encoder.WriteOutbound(@in);
                written += length;
                length = rng.Next(100);
            }
            @in = Unpooled.WrappedBuffer(data, written, data.Length - written);
            encoder.WriteOutbound(@in);
            encoder.Finish();

            ByteBuf msg;
            while ((msg = encoder.ReadOutbound<ByteBuf>()) != null)
            {
                compressed.AddComponent(msg, true);
            }

            byte[] compressedArray = new byte[compressed.ReadableBytes];
            compressed.ReadBytes(compressedArray);
            written = 0;
            length = rng.Next(100);
            while (written + length < compressedArray.Length)
            {
                @in = Unpooled.WrappedBuffer(compressedArray, written, length);
                decoder.WriteInbound(@in);
                written += length;
                length = rng.Next(100);
            }
            @in = Unpooled.WrappedBuffer(compressedArray, written, compressedArray.Length - written);
            decoder.WriteInbound(@in);

            Assert.False(compressed.IsReadable());
            while ((msg = decoder.ReadInbound<ByteBuf>()) != null)
            {
                decompressed.AddComponent(msg, true);
            }
            Assert.Equal(original, decompressed);
        }
        finally
        {
            compressed.Release();
            decompressed.Release();
            original.Release();
            CloseChannels();
        }
    }
}
