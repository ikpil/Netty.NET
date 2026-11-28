/*
 * Copyright 2024 The Netty Project
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
 * Provides a way to specify the Brotli compression mode.
 */
// CLR: the package-private adapt() converts the constant to brotli4j's Encoder.Mode and is not ported: brotli4j is
// not ported and the BCL System.IO.Compression.BrotliEncoder takes only a quality and a window
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder.-ctor?view=net-10.0). Without
// it the Java enum has no fields or methods, so it is a simple C# enum. BrotliOptions keeps the mode, but the BCL
// encoder does not take it: its streaming encoder never sets the native BROTLI_PARAM_MODE ("Tune encoder for
// specific input"), which therefore stays at BROTLI_DEFAULT_MODE, BROTLI_MODE_GENERIC
// (https://raw.githubusercontent.com/dotnet/dotnet/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/runtime/src/libraries/System.IO.Compression.Brotli/src/System/IO/Compression/enc/BrotliEncoder.cs,
// https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/brotli/include/brotli/encode.h). The mode is a
// compression hint, so TEXT and FONT give a valid Brotli stream that may differ in size from brotli4j's.
public enum BrotliMode
{

    /**
     * The compressor does not make any assumptions about the input data's properties,
     * making it suitable for a wide range of data types.
     * default mode.
     */
    GENERIC,

    /**
     * Optimized for UTF-8 formatted text input.
     */
    TEXT,

    /**
     * Designed specifically for font data compression, as used in WOFF 2.0.
     */
    FONT,
}
