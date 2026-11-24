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

namespace Netty.NET.Handler.Codec.Compression;

/**
 * {@link GzipOptions} holds {@link #compressionLevel()},
 * {@link #memLevel()} and {@link #windowBits()} for Gzip compression.
 * This class is an extension of {@link DeflateOptions}
 */
// CLR: the final class is sealed; the package-private constructor and DEFAULT are internal (StandardCompressionOptions
// uses them). DEFAULT hides DeflateOptions.DEFAULT, as the Java static field does, so it carries the new modifier
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier). windowBits and memLevel
// keep their Java meaning; see DeflateOptions for the window and memory settings of the BCL streams.
public sealed class GzipOptions : DeflateOptions
{

    /**
     * @see StandardCompressionOptions#gzip()
     */
    internal static new readonly GzipOptions DEFAULT = new GzipOptions(
            6, 15, 8
    );

    /**
     * @see StandardCompressionOptions#gzip(int, int, int)
     */
    internal GzipOptions(int compressionLevel, int windowBits, int memLevel) : base(compressionLevel, windowBits, memLevel)
    {
    }
}
