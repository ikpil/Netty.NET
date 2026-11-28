/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Smtp;

/**
 * Default {@link SmtpResponse} implementation.
 */
// CLR: The List equals/hashCode/toString used by the source go through
// SmtpUtils.ListEquals/ListHashCode/ListToString. IllegalArgumentException is ArgumentException. Numbers in text
// use the invariant culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
[UnstableApi]
public sealed class DefaultSmtpResponse : ISmtpResponse
{

    private readonly int code;
    private readonly IList<ICharSequence> details;

    /**
     * Creates a new instance with the given smtp code and no details.
     */
    public DefaultSmtpResponse(int code) : this(code, (IList<ICharSequence>) null)
    {
    }

    /**
     * Creates a new instance with the given smtp code and details.
     */
    public DefaultSmtpResponse(int code, params ICharSequence[] details) : this(code, SmtpUtils.ToUnmodifiableList(details))
    {
    }

    internal DefaultSmtpResponse(int code, IList<ICharSequence> details)
    {
        if (code < 100 || code > 599)
        {
            throw new ArgumentException("code must be 100 <= code <= 599");
        }

        this.code = code;

        if (details == null)
        {
            this.details = ReadOnlyCollection<ICharSequence>.Empty;
        }
        else
        {
            this.details = new ReadOnlyCollection<ICharSequence>(details);
        }
    }

    public int Code()
    {
        return code;
    }

    public IList<ICharSequence> Details()
    {
        return details;
    }

    public override int GetHashCode()
    {
        return code * 31 + SmtpUtils.ListHashCode(details);
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultSmtpResponse))
        {
            return false;
        }

        if (o == this)
        {
            return true;
        }

        DefaultSmtpResponse other = (DefaultSmtpResponse) o;

        return Code() == other.Code() &&
                SmtpUtils.ListEquals(Details(), other.Details());
    }

    public override string ToString()
    {
        return "DefaultSmtpResponse{" +
                "code=" + code.ToString(CultureInfo.InvariantCulture) +
                ", details=" + SmtpUtils.ListToString(details) +
                '}';
    }
}
