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

using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the inherited tests of AbstractDecompressorTest run for this class as JUnit runs them; createCompressor() and
// createDecompressor() implement its abstract members. ChannelHandler is IChannelHandler, Decompressor is IDecompressor
// and builder() is Builder_() (see FastLzFrameDecompressor). C# has no throws clauses.
public class FastLzDecompressorTest : AbstractDecompressorTest
{

    public FastLzDecompressorTest()
    {
    }

    protected override IChannelHandler CreateCompressor()
    {
        return new FastLzFrameEncoder();
    }

    protected override IDecompressor.AbstractDecompressorBuilder CreateDecompressor()
    {
        return FastLzFrameDecompressor.Builder_();
    }
}
