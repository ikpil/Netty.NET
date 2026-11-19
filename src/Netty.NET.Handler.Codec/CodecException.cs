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

namespace Netty.NET.Handler.Codec;

/**
 * An {@link Exception} which is thrown by a codec.
 */
// CLR: RuntimeException is Exception (as ChannelException); serialVersionUID is JVM serialization metadata and has no
// counterpart.
public class CodecException : Exception
{
    /**
     * Creates a new instance.
     */
    public CodecException()
    {
    }

    /**
     * Creates a new instance.
     */
    public CodecException(string message, Exception cause) : base(message, cause)
    {
    }

    /**
     * Creates a new instance.
     */
    public CodecException(string message) : base(message)
    {
    }

    /**
     * Creates a new instance.
     */
    // CLR: RuntimeException(Throwable) has "a detail message of (cause==null ? null : cause.toString())"
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/RuntimeException.html).
    public CodecException(Exception cause) : base(ThrowableToString(cause), cause)
    {
    }

    // CLR: Throwable.toString() is the class name, ": " and getLocalizedMessage(), or just the class name when that is
    // null (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Throwable.html). Exception.ToString()
    // also appends the inner exception and the stack trace
    // (https://learn.microsoft.com/en-us/dotnet/api/system.exception.tostring), so the Java text is built here. When
    // no message was supplied, Exception.Message is a default text, not null
    // (https://learn.microsoft.com/en-us/dotnet/api/system.exception.message), so the ": " part is always present.
    internal static string ThrowableToString(Exception cause) =>
        cause == null ? null : cause.GetType().FullName + ": " + cause.Message;
}
