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
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/**
 * Cheap {@link ClosedChannelException} that does not fill in the stacktrace.
 */
// CLR: serialVersionUID is JVM serialization metadata and has no counterpart.
[UnstableApi]
public sealed class StacklessClosedChannelException : ClosedChannelException
{
    private StacklessClosedChannelException()
    {
    }

    private StacklessClosedChannelException(Exception cause) : base(cause)
    {
    }

    // Suppress a warning since this method doesn't need synchronization
    // CLR: Exception has no fillInStackTrace to override. The CLR records a stack trace only when an exception is
    // thrown, so an instance that only fails a Task carries none; NewInstance stores the origin as a remote trace.

    /**
     * Creates a new {@link StacklessClosedChannelException} which has the origin of the given {@link Class} and method.
     */
    public static StacklessClosedChannelException NewInstance(Type clazz, string method)
    {
        return ThrowableUtil.UnknownStackTrace(new StacklessClosedChannelException(), clazz, method);
    }

    // CLR: replaces newInstance(clazz, method).initCause(cause); InnerException can only be set at construction.
    public static StacklessClosedChannelException NewInstance(Type clazz, string method, Exception cause)
    {
        return ThrowableUtil.UnknownStackTrace(new StacklessClosedChannelException(cause), clazz, method);
    }
}
