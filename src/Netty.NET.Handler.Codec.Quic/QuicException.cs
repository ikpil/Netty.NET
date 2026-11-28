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

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Exception produced while processing {@code QUIC}.
 */
// CLR: Exception is System.Exception (as codec-base CodecException) and Throwable is Exception.
// Exception(Throwable) has "a detail message of (cause==null ? null : cause.toString())", the text of Common's
// ThrowableUtil.ThrowableToString (Exception.ToString() also appends the stack trace). @Nullable has no
// counterpart. This is not System.Net.Quic.QuicException, which the msquic implementation (stage 2) translates
// into this type.
public class QuicException : Exception
{

    private readonly QuicTransportError error;

    internal QuicException(string message) : base(message)
    {
        this.error = null;
    }

    public QuicException(QuicTransportError error) : base(error.Name())
    {
        this.error = error;
    }

    public QuicException(string message, QuicTransportError error) : base(message)
    {
        this.error = error;
    }

    public QuicException(Exception cause, QuicTransportError error) : base(ThrowableUtil.ThrowableToString(cause), cause)
    {
        this.error = error;
    }

    public QuicException(string message, Exception cause, QuicTransportError error) : base(message, cause)
    {
        this.error = error;
    }

    /**
     * Returns the {@link QuicTransportError} which was the cause of the {@link QuicException}.
     *
     * @return  the {@link QuicTransportError} that caused this {@link QuicException} or {@code null} if
     *          it was caused by something different.
     */
    public QuicTransportError Error()
    {
        return error;
    }
}
