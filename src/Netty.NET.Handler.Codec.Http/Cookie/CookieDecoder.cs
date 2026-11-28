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

using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using static Netty.NET.Handler.Codec.Http.Cookie.CookieUtil;

namespace Netty.NET.Handler.Codec.Http.Cookie;

/**
 * Parent of Client and Server side cookie decoders
 */
// CLR: CharBuffer.wrap(header, valueBegin, valueEnd), a read-only CharSequence view of the region, is the Common
// StringCharSequence(header, offset, count) view, which keeps the string and the range without copying
// (src/Netty.NET.Common/StringCharSequence.cs). getClass() is GetType(); "a field initializer cannot reference the
// non-static field, method, or property" (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236), so the
// constructor initializes the logger field before the strict flag, as the Java field order does. String.substring(begin,
// end) is Substring(begin, end - begin).
public abstract class CookieDecoder
{

    private readonly IInternalLogger logger;

    private readonly bool strict;

    protected CookieDecoder(bool strict)
    {
        logger = InternalLoggerFactory.GetInstance(GetType());
        this.strict = strict;
    }

    protected DefaultCookie InitCookie(string header, int nameBegin, int nameEnd, int valueBegin, int valueEnd)
    {
        if (nameBegin == -1 || nameBegin == nameEnd)
        {
            logger.Debug("Skipping cookie with null name");
            return null;
        }

        if (valueBegin == -1)
        {
            logger.Debug("Skipping cookie with null value");
            return null;
        }

        ICharSequence wrappedValue = new StringCharSequence(header, valueBegin, valueEnd - valueBegin);
        ICharSequence unwrappedValue = UnwrapValue(wrappedValue);
        if (unwrappedValue == null)
        {
            logger.Debug("Skipping cookie because starting quotes are not properly balanced in '{}'",
                    wrappedValue);
            return null;
        }

        string name = header.Substring(nameBegin, nameEnd - nameBegin);

        int invalidOctetPos;
        if (strict && (invalidOctetPos = FirstInvalidCookieNameOctet(new StringCharSequence(name))) >= 0)
        {
            if (logger.IsDebugEnabled())
            {
                logger.Debug("Skipping cookie because name '{}' contains invalid char '{}'",
                        name, name[invalidOctetPos]);
            }

            return null;
        }

        bool wrap = unwrappedValue.Length() != valueEnd - valueBegin;

        if (strict && (invalidOctetPos = FirstInvalidCookieValueOctet(unwrappedValue)) >= 0)
        {
            if (logger.IsDebugEnabled())
            {
                logger.Debug("Skipping cookie because value '{}' contains invalid char '{}'",
                        unwrappedValue, unwrappedValue.CharAt(invalidOctetPos));
            }

            return null;
        }

        DefaultCookie cookie = new DefaultCookie(name, unwrappedValue.ToString());
        cookie.SetWrap(wrap);
        return cookie;
    }
}
