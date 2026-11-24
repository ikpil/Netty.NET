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

// CLR: owner decision 2026-11-23: Brotli runs on the BCL System.IO.Compression.BrotliEncoder/BrotliDecoder structs, which
// are part of the shared framework since .NET Core 2.1
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder?view=net-10.0,
// https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlidecoder?view=net-10.0), instead of brotli4j
// (JNI). This class is the availability probe of brotli4j: the Java static initializer looks up
// com.aayushatharva.brotli4j.Brotli4jLoader on the class path and asks it for the native library's unavailability cause.
// There is no class path to probe and no native library to load here, so the static initializer, its logger and the
// CNFE/cause fields are not ported: isAvailable() is always true, ensureAvailability() never throws and cause() is always
// null. The Javadoc below still describes the brotli4j probe. Throwable is Exception (docs/codec-base-porting.md).
public sealed class Brotli
{

    /**
     *
     * @return true when brotli4j is in the classpath
     * and native library is available on this platform and could be loaded
     */
    // CLR: always true, because the BCL Brotli encoder and decoder are always available (see the class comment).
    public static bool IsAvailable()
    {
        return true;
    }

    /**
     * Throws when brotli support is missing from the classpath or is unavailable on this platform
     * @throws Throwable a ClassNotFoundException if brotli4j is missing
     * or a UnsatisfiedLinkError if brotli4j native lib can't be loaded
     */
    // CLR: never throws, because the BCL Brotli encoder and decoder are always available (see the class comment).
    public static void EnsureAvailability()
    {
    }

    /**
     * Returns {@link Throwable} of unavailability cause
     */
    // CLR: always null, because the BCL Brotli encoder and decoder are always available (see the class comment).
    public static Exception Cause()
    {
        return null;
    }

    private Brotli()
    {
    }
}
