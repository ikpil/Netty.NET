/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http.Cookie.CookieUtil;

namespace Netty.NET.Handler.Codec.Http.Cookie;

/**
 * Parent of Client and Server side cookie encoders
 */
// CLR: the String name and value are passed to the CharSequence helpers of CookieUtil as StringCharSequence views (a C#
// string is not an ICharSequence). IllegalArgumentException is ArgumentException; the invalid char is appended as a char,
// as in the Java string concatenation.
public abstract class CookieEncoder
{

    protected readonly bool strict;

    protected CookieEncoder(bool strict)
    {
        this.strict = strict;
    }

    protected void ValidateCookie(string name, string value)
    {
        if (strict)
        {
            int pos;

            if ((pos = FirstInvalidCookieNameOctet(new StringCharSequence(name))) >= 0)
            {
                throw new ArgumentException("Cookie name contains an invalid char: " + name[pos]);
            }

            ICharSequence unwrappedValue = UnwrapValue(new StringCharSequence(value));
            if (unwrappedValue == null)
            {
                throw new ArgumentException("Cookie value wrapping quotes are not balanced: " + value);
            }

            if ((pos = FirstInvalidCookieValueOctet(unwrappedValue)) >= 0)
            {
                throw new ArgumentException("Cookie value contains an invalid char: " +
                                                   unwrappedValue.CharAt(pos));
            }
        }
    }
}
