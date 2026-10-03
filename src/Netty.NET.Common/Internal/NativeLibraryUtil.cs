/*
 * Copyright 2016 The Netty Project
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
using System.IO;
using System.Runtime.InteropServices;

namespace Netty.NET.Common.Internal;

/**
 * A Utility to Call the {@link System#load(String)} or {@link System#loadLibrary(String)}.
 * Because the {@link System#load(String)} and {@link System#loadLibrary(String)} are both
 * CallerSensitive, it will load the native library into its caller's {@link ClassLoader}.
 * In OSGi environment, we need this helper to delegate the calling to {@link System#load(String)}
 * and it should be as simple as possible. It will be injected into the native library's
 * ClassLoader when it is undefined. And therefore, when the defined new helper is invoked,
 * the native library would be loaded into the native library's ClassLoader, not the
 * caller's ClassLoader.
 */
// CLR libraries have no JNI/ClassLoader registration. Use the runtime loader;
// each returned handle belongs to the caller and must be freed via NativeLibrary.Free.
public static class NativeLibraryUtil
{
    /**
     * Delegate the calling to {@link System#load(String)} or {@link System#loadLibrary(String)}.
     * @param libName - The native library path or name
     * @param absolute - Whether the native library will be loaded by path or by name
     */
    public static IntPtr LoadLibrary(string libName, bool absolute)
    {
        ArgumentNullException.ThrowIfNull(libName);
        if (absolute)
        {
            if (!Path.IsPathFullyQualified(libName))
                throw new ArgumentException("An absolute native library path is required.", nameof(libName));
            return NativeLibrary.Load(libName);
        }
        return NativeLibrary.Load(libName, typeof(NativeLibraryUtil).Assembly, null);
    }
    // Utility
}
