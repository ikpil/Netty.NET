/*
 * Copyright 2018 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.IO;
using System.Net;
using System.Reflection;

namespace Netty.NET.Common.Internal;

/**
 * A utility class that provides various common operations and constants
 * related to loading resources
 */
public static class ResourcesUtil
{
    /**
     * Returns a {@link FileInfo} named {@code fileName} associated with {@link Class} {@code resourceClass} .
     *
     * @param resourceClass The associated class
     * @param fileName The file name
     * @return The file named {@code fileName} associated with {@link Class} {@code resourceClass} .
     */
    public static FileInfo getFile(Type resourceClass, string fileName)
    {
        Assembly assembly = resourceClass.Assembly;

        string assemblyLocation = assembly.Location;
        string assemblyDir = Path.GetDirectoryName(assemblyLocation);

        if (assemblyDir == null)
            throw new InvalidOperationException("Unable to determine assembly directory.");

        string filePath = Path.Combine(assemblyDir, fileName);

        filePath = WebUtility.UrlDecode(filePath);
        return new FileInfo(filePath);
    }
}