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
 * An exception related to violate the HTTP3 spec.
 */
// CLR: the checked Exception is the CLR Exception (C# has no checked exceptions, so the throws clauses of the package
// have no counterpart); Throwable is Exception. The @Nullable (org.jetbrains.annotations) parameter annotations have no
// counterpart: the project does not enable the nullable annotation context. A null message gives the CLR default
// message where Java's getMessage() returns null ("If no message was supplied ... the system supplies a default
// message", https://learn.microsoft.com/en-us/dotnet/api/system.exception.message?view=net-10.0).
public sealed class Http3Exception : Exception
{
    private readonly Http3ErrorCode errorCode;

    /**
     * Create a new instance.
     *
     * @param errorCode the {@link Http3ErrorCode} that caused this exception.
     * @param message   the message to include.
     */
    public Http3Exception(Http3ErrorCode errorCode, string message) : base(message)
    {
        this.errorCode = ObjectUtil.CheckNotNull(errorCode, "errorCode");
    }

    /**
     * Create a new instance.
     *
     * @param errorCode the {@link Http3ErrorCode} that caused this exception.
     * @param message   the message to include.
     * @param cause     the {@link Throwable} to wrap.
     */
    public Http3Exception(Http3ErrorCode errorCode, string message, Exception cause) : base(message, cause)
    {
        this.errorCode = ObjectUtil.CheckNotNull(errorCode, "errorCode");
    }

    /**
     * Returns the related {@link Http3ErrorCode}.
     *
     * @return the {@link Http3ErrorCode}.
     */
    public Http3ErrorCode ErrorCode()
    {
        return errorCode;
    }
}
