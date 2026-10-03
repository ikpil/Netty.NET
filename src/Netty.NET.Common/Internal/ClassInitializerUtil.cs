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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security;

namespace Netty.NET.Common.Internal;

/// <summary>Explicitly initializes runtime types before native bootstrap work.</summary>
/// <remarks>Types carry their own assembly/load-context identity. Initialization
/// failures propagate; only type-loading and security failures are best effort.</remarks>
public static class ClassInitializerUtil
{
    /// <summary>Runs each exact type's static initializer in the supplied order.</summary>
    [RequiresUnreferencedCode("Type initializers must be preserved when trimming.")]
    public static void TryInitialize(params Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        foreach (Type type in types)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type.ContainsGenericParameters)
                throw new ArgumentException("A closed runtime type is required for initialization.", nameof(types));
            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            catch (TypeLoadException) { }
            catch (SecurityException) { }
        }
    }
}
