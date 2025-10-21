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
using System.Reflection;
using System.Security;

namespace Netty.NET.Common.Internal;

/**
 * Utility which ensures that classes are loaded by the {@link ClassLoader}.
 */
public static class ClassInitializerUtil
{
    /**
     * Preload the given classes and so ensure the {@link ClassLoader} has these loaded after this method call.
     *
     * @param loadingClass      the {@link Class} that wants to load the classes.
     * @param classes           the classes to load.
     */
    public static void tryLoadClasses(Type loadingType, params Type[] classes)
    {
        if (loadingType == null)
            throw new ArgumentNullException(nameof(loadingType));

        Assembly assembly = loadingType.Assembly;
        foreach (Type type in classes)
        {
            tryLoadType(assembly, type.FullName);
        }
    }

    private static void tryLoadType(Assembly assembly, string typeName)
    {
        try
        {
            // Load the type and ensure it is initialized
            Type type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type != null)
            {
                // Accessing Type.TypeInitializer ensures the type is initialized (similar to Class.forName with initialize=true)
                _ = type.TypeInitializer;
            }
        }
        catch (TypeLoadException)
        {
            // Ignore type loading failures
        }
        catch (SecurityException)
        {
            // Ignore security-related failures
        }
    }
}