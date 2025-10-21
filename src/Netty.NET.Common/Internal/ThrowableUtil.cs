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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Netty.NET.Common.Internal;

public static class ThrowableUtil
{
    /**
     * Set the {@link StackTraceElement} for the given {@link Exception}, using the {@link Class} and method name.
     */
    public static T unknownStackTrace<T>(Func<string, T> cause, Type clazz,
        [CallerMemberName] string method = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        where T : Exception
    {
        var message = $"location: {clazz.Name}::{method}() in {file}:{line}";
        return cause.Invoke(message);
    }

    public static AggregateException addSuppressed(Exception target, Exception suppressed)
    {
        return new AggregateException([target, suppressed]);
    }

    public static AggregateException addSuppressedAndClear(Exception target, List<Exception> suppressed)
    {
        var ae = addSuppressed(target, suppressed);
        suppressed.Clear();
        return ae;
    }

    public static AggregateException addSuppressed(Exception target, List<Exception> suppressed)
    {
        return new AggregateException([target, ..suppressed]);
    }

    public static ReadOnlyCollection<Exception> getSuppressed(AggregateException source)
    {
        return source.InnerExceptions;
    }
}