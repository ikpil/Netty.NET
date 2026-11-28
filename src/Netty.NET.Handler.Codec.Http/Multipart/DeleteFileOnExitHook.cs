/*
 * Copyright 2020 The Netty Project
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
using System.Collections.Concurrent;
using System.IO;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * DeleteFileOnExitHook.
 */
// CLR: ConcurrentHashMap.newKeySet() is a ConcurrentDictionary<string, bool> used as a set (add is TryAdd, remove
// is TryRemove, contains is ContainsKey), as handler's UniqueIpFilter does
// (src/Netty.NET.Handler/IpFilter/UniqueIpFilter.cs). Runtime.addShutdownHook(Thread), whose hooks start "When the
// virtual machine begins its shutdown sequence"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Runtime.html), is a handler of
// AppDomain.ProcessExit, which "Occurs when the default application domain's parent process exits" and "can
// perform termination activities, such as closing files"
// (https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.processexit?view=net-10.0), registered in the
// static constructor (the static initializer), as pkitesting's X509Bundle does for File.deleteOnExit
// (src/Netty.NET.PkiTesting/X509Bundle.cs). new File(filename).delete() ignores a failure, as Java ignores its
// false result; File.Delete does nothing for a missing file and throws IOException/UnauthorizedAccessException
// otherwise (https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.delete?view=net-10.0).
internal static class DeleteFileOnExitHook
{
    private static readonly ConcurrentDictionary<string, bool> FILES = new ConcurrentDictionary<string, bool>();

    static DeleteFileOnExitHook()
    {
        // DeleteOnExitHook must be the last shutdown hook to be invoked.
        // Application shutdown hooks may add the first file to the
        // delete on exit list and cause the DeleteOnExitHook to be
        // registered during shutdown in progress.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RunHook();
    }

    /**
     * Remove from the pool to reduce space footprint.
     *
     * @param file tmp file path
     */
    public static void Remove(string file)
    {
        FILES.TryRemove(file, out _);
    }

    /**
     * Add to the hook and clean up when the program exits.
     *
     * @param file tmp file path
     */
    public static void Add(string file)
    {
        FILES.TryAdd(file, true);
    }

    /**
     * Check in the hook files.
     *
     * @param file target file
     * @return true or false
     */
    public static bool CheckFileExist(string file)
    {
        return FILES.ContainsKey(file);
    }

    /**
     * Clean up all the files.
     */
    internal static void RunHook()
    {
        foreach (string filename in FILES.Keys)
        {
            try
            {
                new FileInfo(filename).Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
