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
 * Standard Compression Options for {@link BrotliOptions},
 * {@link GzipOptions} and {@link DeflateOptions}
 */
// CLR: the final class is sealed. Not ported: the @Deprecated brotli(Encoder.Parameters), whose @deprecated text names
// brotli(int, int, BrotliMode) (owner decision 2026-11-16), and zstd()/zstd(int, int, int), which return the
// library-not-ported ZstdOptions: zstd-jni has no BCL counterpart on .NET 10 (owner decision 2026-11-23,
// docs/codec-compression-porting.md). Their Javadoc is recorded in docs/codec-compression-porting-manifest.json.
public sealed class StandardCompressionOptions
{
    private StandardCompressionOptions()
    {
        // Prevent outside initialization
    }

    /**
     * Default implementation of {@link BrotliOptions} with {@link Encoder.Parameters#setQuality(int)} set to 4
     * and {@link Encoder.Parameters#setMode(Encoder.Mode)} set to {@link Encoder.Mode#TEXT}
     */
    public static BrotliOptions Brotli()
    {
        return BrotliOptions.DEFAULT;
    }

    /**
     * Create a new {@link BrotliOptions}
     *
     * @param quality Specifies the compression level.
     * @param window  Specifies the size of the sliding window when compressing.
     * @param mode    optimizes the compression algorithm based on the type of input data.
     * @throws NullPointerException If {@link BrotliMode} is {@code null}
     */
    // CLR: BrotliMode is a C# enum (a value type), so it cannot be null and checkNotNull(mode, "mode") has no
    // counterpart. The brotli4j Encoder.Parameters built from quality/window/mode.adapt() is the internal
    // BrotliOptions(quality, window, mode) constructor; the BCL BrotliEncoder takes quality and window only, so the
    // mode is kept but not applied (BrotliMode, docs/codec-compression-porting.md).
    public static BrotliOptions Brotli(int quality, int window, BrotliMode mode)
    {
        ObjectUtil.CheckInRange(quality, 0, 11, "quality");
        ObjectUtil.CheckInRange(window, 10, 24, "window");
        return new BrotliOptions(quality, window, mode);
    }

    /**
     * Create a new {@link SnappyOptions}
     */
    public static SnappyOptions Snappy()
    {
        return new SnappyOptions();
    }

    /**
     * Default implementation of {@link GzipOptions} with
     * {@code compressionLevel()} set to 6, {@code windowBits()} set to 15 and {@code memLevel()} set to 8.
     */
    public static GzipOptions Gzip()
    {
        return GzipOptions.DEFAULT;
    }

    /**
     * Create a new {@link GzipOptions} Instance
     *
     * @param compressionLevel {@code 1} yields the fastest compression and {@code 9} yields the
     *                         best compression.  {@code 0} means no compression.  The default
     *                         compression level is {@code 6}.
     * @param windowBits       The base two logarithm of the size of the history buffer.  The
     *                         value should be in the range {@code 9} to {@code 15} inclusive.
     *                         Larger values result in better compression at the expense of
     *                         memory usage.  The default value is {@code 15}.
     * @param memLevel         How much memory should be allocated for the internal compression
     *                         state.  {@code 1} uses minimum memory and {@code 9} uses maximum
     *                         memory.  Larger values result in better and faster compression
     *                         at the expense of memory usage.  The default value is {@code 8}
     */
    public static GzipOptions Gzip(int compressionLevel, int windowBits, int memLevel)
    {
        return new GzipOptions(compressionLevel, windowBits, memLevel);
    }

    /**
     * Default implementation of {@link DeflateOptions} with
     * {@code compressionLevel} set to 6, {@code windowBits} set to 15 and {@code memLevel} set to 8.
     */
    public static DeflateOptions Deflate()
    {
        return DeflateOptions.DEFAULT;
    }

    /**
     * Create a new {@link DeflateOptions} Instance
     *
     * @param compressionLevel {@code 1} yields the fastest compression and {@code 9} yields the
     *                         best compression.  {@code 0} means no compression.  The default
     *                         compression level is {@code 6}.
     * @param windowBits       The base two logarithm of the size of the history buffer.  The
     *                         value should be in the range {@code 9} to {@code 15} inclusive.
     *                         Larger values result in better compression at the expense of
     *                         memory usage.  The default value is {@code 15}.
     * @param memLevel         How much memory should be allocated for the internal compression
     *                         state.  {@code 1} uses minimum memory and {@code 9} uses maximum
     *                         memory.  Larger values result in better and faster compression
     *                         at the expense of memory usage.  The default value is {@code 8}
     */
    public static DeflateOptions Deflate(int compressionLevel, int windowBits, int memLevel)
    {
        return new DeflateOptions(compressionLevel, windowBits, memLevel);
    }
}
