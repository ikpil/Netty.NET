/*
 * Copyright 2012 The Netty Project
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
using System.Diagnostics;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;
/**
 * A {@link RuntimeException} which is thrown when an I/O operation fails.
 */
// CLR: serialVersionUID is JVM serialization metadata and has no counterpart.
public class ChannelException : Exception
{
    /**
     * Creates a new exception.
     */
    public ChannelException()
    {
    }

    /**
     * Creates a new exception.
     */
    public ChannelException(string message, Exception cause) : base(message, cause)
    {
    }

    /**
     * Creates a new exception.
     */
    public ChannelException(string message) : base(message)
    {
    }

    /**
     * Creates a new exception.
     */
    // CLR: RuntimeException(Throwable) has "a detail message of (cause==null ? null : cause.toString())"
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/RuntimeException.html), the Java text that
    // ThrowableUtil.ThrowableToString builds (owner decision 2026-11-24).
    public ChannelException(Exception cause) : base(ThrowableUtil.ThrowableToString(cause), cause)
    {
    }

    protected ChannelException(string message, Exception cause, bool shared) : base(message, cause)
    {
        // CLR: Exception has no suppression/writableStackTrace constructor flags; the stack trace is recorded only when
        // an instance is thrown (https://learn.microsoft.com/dotnet/api/system.exception.stacktrace).
        Debug.Assert(shared);
    }

    internal static ChannelException NewStatic(string message, Type clazz, string method)
    {
        ChannelException exception = new StacklessChannelException(message, null, true);
        return ThrowableUtil.UnknownStackTrace(exception, clazz, method);
    }

    private sealed class StacklessChannelException : ChannelException
    {
        internal StacklessChannelException(string message, Exception cause, bool shared) : base(message, cause, shared)
        {
        }
    }
}
