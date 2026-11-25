/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.Codec.Spdy;

// CLR: Throwable is Exception; serialVersionUID is JVM serialization metadata and has no counterpart. The cause-only
// constructor keeps Java's detail message, cause.toString() (class name, ": ", message), through Common's
// ThrowableUtil.ThrowableToString (docs/codec-base-porting.md, Exceptions). Class<?> is Type and assert is
// Debug.Assert. The private shared constructor calls super(message, null, false, true): the CLR Exception has no
// suppression or writable-stack-trace switches, and a null message gives the CLR default message where Java's
// getMessage() returns null (only PROTOCOL_EXCEPTION of SpdySessionHandler has one, and no code reads it).
public class SpdyProtocolException : Exception
{

    /**
     * Creates a new instance.
     */
    public SpdyProtocolException() { }

    /**
     * Creates a new instance.
     */
    public SpdyProtocolException(string message, Exception cause) : base(message, cause)
    {
    }

    /**
     * Creates a new instance.
     */
    public SpdyProtocolException(string message) : base(message)
    {
    }

    /**
     * Creates a new instance.
     */
    public SpdyProtocolException(Exception cause)
        : base(ThrowableUtil.ThrowableToString(cause), cause)
    {
    }

    internal static SpdyProtocolException NewStatic(string message, Type clazz, string method)
    {
        SpdyProtocolException exception = new StacklessSpdyProtocolException(message, true);
        return ThrowableUtil.UnknownStackTrace(exception, clazz, method);
    }

    private SpdyProtocolException(string message, bool shared) : base(message)
    {
        Debug.Assert(shared);
    }

    private sealed class StacklessSpdyProtocolException : SpdyProtocolException
    {

        internal StacklessSpdyProtocolException(string message, bool shared) : base(message, shared)
        {
        }

        // Override fillInStackTrace() so we not populate the backtrace via a native call and so leak the
        // Classloader.
        // CLR: there is no fillInStackTrace(). The first non-null trace (the origin frame stored by
        // ThrowableUtil.UnknownStackTrace) is frozen, so later throws or awaits do not add frames, as Common's
        // GlobalEventExecutor.StacklessUnsupportedOperationException does.
        private string _stackTrace;

        public override string StackTrace => _stackTrace ??= base.StackTrace;
    }
}
