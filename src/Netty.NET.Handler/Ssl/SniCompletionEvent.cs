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

/**
 * Event that is fired once we did a selection of a {@link SslContext} based on the {@code SNI hostname},
 * which may be because it was successful or there was an error.
 */
public sealed class SniCompletionEvent : SslCompletionEvent
{
    private readonly string hostname;

    public SniCompletionEvent(string hostname)
    {
        this.hostname = hostname;
    }

    public SniCompletionEvent(string hostname, Exception cause) : base(cause)
    {
        this.hostname = hostname;
    }

    public SniCompletionEvent(Exception cause) : this(null, cause)
    {
    }

    /**
     * Returns the SNI hostname send by the client if we were able to parse it, {@code null} otherwise.
     */
    public string Hostname()
    {
        return hostname;
    }

    // CLR: getClass().getSimpleName() is StringUtil.SimpleClassName(this); string concatenation of a null hostname gives
    // "" where Java gives "null", so the null case is written out; the cause is Throwable.toString() (Common's
    // ThrowableUtil.ThrowableToString).
    public override string ToString()
    {
        Exception cause = Cause();
        return cause == null ? StringUtil.SimpleClassName(this) + "(SUCCESS='"  + (hostname ?? "null") + "'\")":
                StringUtil.SimpleClassName(this) +  '(' + ThrowableUtil.ThrowableToString(cause) + ')';
    }
}
