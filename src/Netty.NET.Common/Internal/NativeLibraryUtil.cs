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
using System.Runtime.InteropServices;

namespace Netty.NET.Common.Internal;

/**
 * A Utility to Call the {@link System#load(string)} or {@link System#loadLibrary(string)}.
 * Because the {@link System#load(string)} and {@link System#loadLibrary(string)} are both
 * CallerSensitive, it will load the native library into its caller's {@link ClassLoader}.
 * In OSGi environment, we need this helper to delegate the calling to {@link System#load(string)}
 * and it should be as simple as possible. It will be injected into the native library's
 * ClassLoader when it is undefined. And therefore, when the defined new helper is invoked,
 * the native library would be loaded into the native library's ClassLoader, not the
 * caller's ClassLoader.
 */
public static class NativeLibraryUtil 
{
    /**
     * Delegate the calling to {@link System#load(string)} or {@link System#loadLibrary(string)}.
     * @param libName - The native library path or name
     * @param absolute - Whether the native library will be loaded by path or by name
     */
    /// <summary>
    /// Load a native library by absolute path or by name (searching via platform defaults).
    /// Returns the native handle, or throws if loading failed.
    /// </summary>
    public static IntPtr loadLibrary(string libName, bool absolute)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return LoadLibrary_Windows(libName, absolute);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return LoadLibrary_Unix(libName, absolute, isMac: false);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return LoadLibrary_Unix(libName, absolute, isMac: true);
        }
        else
        {
            throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
        }
    }

    #region Windows
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    private static IntPtr LoadLibrary_Windows(string libName, bool absolute)
    {
        IntPtr handle;

        if (absolute)
        {
            handle = LoadLibrary(libName);
        }
        else
        {
            // Let Windows search for the DLL by name (will append .dll if omitted)
            handle = LoadLibrary(libName);
        }

        if (handle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new DllNotFoundException($"Failed to load native library '{libName}' on Windows. Win32 error code: {err}");
        }

        return handle;
    }
    #endregion

    #region Unix (Linux / macOS)
    private const int RTLD_NOW = 2; // for dlopen's flags

    // Linux: usually in libdl.so.2. Let the loader resolve via "libdl.so.2" first.
    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr dlopen_linux(string fileName, int flags);
    [DllImport("libdl.so.2", EntryPoint = "dlerror")]
    private static extern IntPtr dlerror_linux();

    // macOS: dlopen is in libSystem; using "libSystem.B.dylib"
    [DllImport("libSystem.B.dylib", EntryPoint = "dlopen")]
    private static extern IntPtr dlopen_mac(string fileName, int flags);
    [DllImport("libSystem.B.dylib", EntryPoint = "dlerror")]
    private static extern IntPtr dlerror_mac();

    private static IntPtr LoadLibrary_Unix(string libName, bool absolute, bool isMac)
    {
        IntPtr handle = IntPtr.Zero;

        string[] candidates;
        if (absolute)
        {
            candidates = new[] { libName };
        }
        else
        {
            if (isMac)
            {
                // On macOS: try given name, then with lib prefix and .dylib
                candidates = new[]
                {
                    libName,
                    $"lib{libName}.dylib",
                    $"{libName}.dylib"
                };
            }
            else
            {
                // On Linux: try given name, then lib{name}.so, then with version wildcard is harder so keep simple
                candidates = new[]
                {
                    libName,
                    $"lib{libName}.so",
                    $"{libName}.so"
                };
            }
        }

        foreach (var candidate in candidates)
        {
            if (isMac)
            {
                handle = dlopen_mac(candidate, RTLD_NOW);
                if (handle != IntPtr.Zero) break;
                // clear previous error by calling dlerror once
                _ = dlerror_mac();
            }
            else
            {
                handle = dlopen_linux(candidate, RTLD_NOW);
                if (handle != IntPtr.Zero) break;
                _ = dlerror_linux();
            }
        }

        if (handle == IntPtr.Zero)
        {
            string errMsg;
            if (isMac)
            {
                var errPtr = dlerror_mac();
                errMsg = errPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errPtr) : "Unknown error";
            }
            else
            {
                var errPtr = dlerror_linux();
                errMsg = errPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errPtr) : "Unknown error";
            }

            throw new DllNotFoundException($"Failed to load native library '{libName}' on {(isMac ? "macOS" : "Linux")}. Tried: {string.Join(", ", candidates)}. dlerror: {errMsg}");
        }

        return handle;
    }
    #endregion
}
