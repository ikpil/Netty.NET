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
using System.Diagnostics;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Dns;

namespace Netty.NET.Resolver.Dns;

/**
 * A metadata carrier exception, to propagate {@link DnsResponseCode} information as an enrichment
 * within the {@link UnknownHostException} cause.
 */
// CLR: RuntimeException is Exception (docs/codec-base-porting.md); UnknownHostException is Common's
// UnknownHostException (owner decision 2026-11-25, docs/resolver-dns-porting.md). serialVersionUID has no counterpart.
// super(message, null, false, true) disables suppression ("calls to addSuppressed ... will have no effect",
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Throwable.html); a CLR exception has no
// suppressed list and Common's ThrowableUtil.AddSuppressed keeps one per exception without such a switch, so it is the
// base(message) constructor. The fillInStackTrace() override has no counterpart: the JDK records the stack at
// construction (same page), a CLR exception only when it is thrown
// (https://learn.microsoft.com/en-us/dotnet/api/system.exception.stacktrace?view=net-10.0); its comment is recorded in
// the manifest. newStatic(...) keeps ThrowableUtil.UnknownStackTrace (Common), which sets the single "Unknown Source"
// frame through ExceptionDispatchInfo.SetRemoteStackTrace.
public sealed class DnsErrorCauseException : Exception
{

    private readonly DnsResponseCode code;

    private DnsErrorCauseException(string message, DnsResponseCode code, bool shared) : base(message)
    {
        this.code = code;
        Debug.Assert(shared);
    }

    /**
     * Returns the DNS error-code that caused the {@link UnknownHostException}.
     *
     * @return the DNS error-code that caused the {@link UnknownHostException}.
     */
    public DnsResponseCode GetCode()
    {
        return code;
    }

    internal static DnsErrorCauseException NewStatic(string message, DnsResponseCode code, Type clazz, string method)
    {
        DnsErrorCauseException exception = new DnsErrorCauseException(message, code, true);
        return ThrowableUtil.UnknownStackTrace(exception, clazz, method);
    }
}
