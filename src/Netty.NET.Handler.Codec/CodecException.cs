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
using Netty.NET.Common.Internal;

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
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/RuntimeException.html), the text of
    // Common's ThrowableUtil.ThrowableToString (Exception.ToString() also appends the stack trace).
    public CodecException(Exception cause) : base(ThrowableUtil.ThrowableToString(cause), cause)
    {
    }
}
