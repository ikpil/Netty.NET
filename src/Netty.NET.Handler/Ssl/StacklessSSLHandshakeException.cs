/*
 * Copyright 2023 The Netty Project
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
using System.Security.Authentication;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * A {@link SSLHandshakeException} that does not fill in the stack trace.
 */
// CLR: SSLHandshakeException is AuthenticationException, as in SslHandshakeTimeoutException (ssl design decision 3,
// docs/handler-ssl-design.md). serialVersionUID is JVM serialization metadata and has no counterpart.
internal sealed class StacklessSSLHandshakeException : AuthenticationException
{

    private StacklessSSLHandshakeException(string reason) : base(reason)
    {
    }

    // This is a performance optimization to not fill in the
    // stack trace as this is a stackless exception.
    // CLR: Exception has no fillInStackTrace to override. The CLR records a stack trace only when an exception is
    // thrown, so an instance that only fails a Task or is fired through the pipeline carries none, and NewInstance stores
    // the origin as a remote trace, as Transport StacklessClosedChannelException does
    // (src/Netty.NET.Transport/Channels/StacklessClosedChannelException.cs, docs/transport-porting.md).

    /**
     * Creates a new {@link StacklessSSLHandshakeException} which has the origin of the given {@link Class} and method.
     */
    internal static StacklessSSLHandshakeException NewInstance(string reason, Type clazz, string method)
    {
        return ThrowableUtil.UnknownStackTrace(new StacklessSSLHandshakeException(reason), clazz, method);
    }
}
