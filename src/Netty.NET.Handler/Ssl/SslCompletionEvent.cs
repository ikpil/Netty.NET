/*
 * Copyright 2017 The Netty Project
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

namespace Netty.NET.Handler.Ssl;

// CLR: Throwable is Exception (docs/codec-base-porting.md). The package-private constructors are internal, so only this
// assembly derives from the class, as only io.netty.handler.ssl can in Java.
public abstract class SslCompletionEvent
{

    private readonly Exception cause;

    internal SslCompletionEvent()
    {
        cause = null;
    }

    internal SslCompletionEvent(Exception cause)
    {
        this.cause = ObjectUtil.CheckNotNull(cause, "cause");
    }

    /**
     * Return {@code true} if the completion was successful
     */
    public bool IsSuccess()
    {
        return cause == null;
    }

    /**
     * Return the {@link Throwable} if {@link #isSuccess()} returns {@code false}
     * and so the completion failed.
     */
    public Exception Cause()
    {
        return cause;
    }

    // CLR: getClass().getSimpleName() is StringUtil.SimpleClassName(this) and the cause is printed with
    // Throwable.toString() (class name, ": ", message), built as CodecException(Exception) builds it
    // (src/Netty.NET.Handler.Codec/CodecException.cs); Exception.ToString() would add the stack trace.
    public override string ToString()
    {
        Exception cause = Cause();
        return cause == null? StringUtil.SimpleClassName(this) + "(SUCCESS)" :
                StringUtil.SimpleClassName(this) +  '(' + ThrowableToString(cause) + ')';
    }

    // CLR: Throwable.toString() of a non-null cause.
    internal static string ThrowableToString(Exception cause) => cause.GetType().FullName + ": " + cause.Message;
}
