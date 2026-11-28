/*
 * Copyright 2021 The Netty Project
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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * {@link DeflateOptions} holds {@link #compressionLevel()},
 * {@link #memLevel()} and {@link #windowBits()} for Deflate compression.
 */
// CLR: The package-private constructor and DEFAULT are internal (StandardCompressionOptions uses them), and the
// accessor methods stay methods. windowBits and memLevel keep their Java ranges and values: ZlibCodecFactory hands
// any value other than 15/8 to JZlibEncoder, which is not ported. The BCL streams behind JdkZlibEncoder use their
// own window and memory settings, ZLibNative.Deflate_DefaultWindowBits (15) and Deflate_DefaultMemLevel (8)
// (https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/Common/src/System/IO/Compression/ZLibNative.cs),
// which are the values ZlibCodecFactory routes to JdkZlibEncoder; ZLibCompressionOptions on.NET 10 sets only the
// compression level and strategy
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibcompressionoptions.compressionlevel?view=net-10.0).
public class DeflateOptions : ICompressionOptions
{

    private readonly int compressionLevel;
    private readonly int windowBits;
    private readonly int memLevel;

    /**
     * @see StandardCompressionOptions#deflate()
     */
    internal static readonly DeflateOptions DEFAULT = new DeflateOptions(
            6, 15, 8
    );

    /**
     * @see StandardCompressionOptions#deflate(int, int, int)
     */
    internal DeflateOptions(int compressionLevel, int windowBits, int memLevel)
    {
        this.compressionLevel = ObjectUtil.CheckInRange(compressionLevel, 0, 9, "compressionLevel");
        this.windowBits = ObjectUtil.CheckInRange(windowBits, 9, 15, "windowBits");
        this.memLevel = ObjectUtil.CheckInRange(memLevel, 1, 9, "memLevel");
    }

    public int CompressionLevel()
    {
        return compressionLevel;
    }

    public int WindowBits()
    {
        return windowBits;
    }

    public int MemLevel()
    {
        return memLevel;
    }
}
