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

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Exception thrown if an error happens during QPACK processing.
 */
// CLR: the checked Exception is the CLR Exception; Throwable is Exception and Class<?> is Type. super(message, cause,
// enableSuppression, writableStackTrace): a CLR exception has no suppression switch (Common's
// ThrowableUtil.AddSuppressed keeps one list per exception), so enableSuppression has no counterpart. With
// writableStackTrace false Java records no stack trace; a CLR exception records its trace when it is thrown
// (https://learn.microsoft.com/en-us/dotnet/api/system.exception.stacktrace?view=net-10.0), so StackTrace keeps the
// first non-null trace (the "Unknown Source" frame that ThrowableUtil.UnknownStackTrace stores for newStatic) and
// later throws of the shared instance do not add frames, as codec-http2's StacklessHttp2Exception does. newStatic keeps
// ThrowableUtil.UnknownStackTrace (Common). The @Nullable cause has no counterpart (no nullable annotation context).
public sealed class QpackException : Exception
{
    private readonly bool writableStackTrace;
    private string frozenStackTrace;

    private QpackException(string message, Exception cause, bool enableSuppression,
                           bool writableStackTrace) : base(message, cause)
    {
        this.writableStackTrace = writableStackTrace;
    }

    internal static QpackException NewStatic(Type clazz, string method, string message)
    {
        return ThrowableUtil.UnknownStackTrace(new QpackException(message, null, false, false), clazz, method);
    }

    // CLR: see the type comment (writableStackTrace).
    public override string StackTrace => writableStackTrace ? base.StackTrace : frozenStackTrace ??= base.StackTrace;
}
