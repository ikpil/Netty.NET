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

using System;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * {@link BrotliOptions} holds {@link Encoder.Parameters} for
 * Brotli compression.
 */
// CLR: brotli4j's Encoder.Parameters has no counterpart (owner decision 2026-11-23: brotli4j is not ported), so the
// options hold the three values that Netty puts into it: the quality, the window and the mode. The BCL
// System.IO.Compression.BrotliEncoder that BrotliEncoder uses is created from the quality ("0 is the minimum (no
// compression), 11 is the maximum") and the window ("The minimum value is 10, and the maximum value is 24")
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder.-ctor?view=net-10.0); it has no
// mode parameter, so the mode is kept but not used (see BrotliMode). The public parameters() accessor only returns the
// brotli4j type and is not ported; the encoder reads the internal fields. The package-private constructor
// BrotliOptions(Encoder.Parameters) is the internal constructor BrotliOptions(int quality, int window, BrotliMode mode),
// which StandardCompressionOptions calls with the values it validated, as it calls setQuality/setWindow/setMode in Java.
// BrotliMode is a C# enum and cannot be null, so checkNotNull(parameters, "Parameters") has no counterpart. The
// IllegalStateException of an unavailable Brotli is InvalidOperationException (docs/codec-base-porting.md); it is never
// thrown, because Brotli.IsAvailable() is always true.
public sealed class BrotliOptions : ICompressionOptions
{

    internal readonly int quality;
    internal readonly int window;
    internal readonly BrotliMode mode;

    /**
     * @see StandardCompressionOptions#brotli()
     */
    // CLR: new Encoder.Parameters().setQuality(4).setMode(Encoder.Mode.TEXT) leaves lgwin at -1, which brotli4j does not
    // pass to the native encoder (encoder_jni.cc sets BROTLI_PARAM_LGWIN only when lgwin >= 0,
    // https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/natives/src/main/cpp/encoder_jni.cc), so the encoder
    // uses BROTLI_DEFAULT_WINDOW, 22 (https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/brotli/include/brotli/encode.h).
    // The BCL constructor takes the window explicitly, so the default options hold 22.
    internal static readonly BrotliOptions DEFAULT = new BrotliOptions(4, 22, BrotliMode.TEXT);

    internal BrotliOptions(int quality, int window, BrotliMode mode)
    {
        if (!Brotli.IsAvailable())
        {
            throw new InvalidOperationException("Brotli is not available", Brotli.Cause());
        }

        this.quality = quality;
        this.window = window;
        this.mode = mode;
    }
}
