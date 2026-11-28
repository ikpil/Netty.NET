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

namespace Netty.NET.Handler.Codec.Http.Cookie;

// CLR: the final class with a private constructor is a static class (as HttpConstants); the String constants are const
// strings.
public static class CookieHeaderNames
{
    public const string PATH = "Path";

    public const string EXPIRES = "Expires";

    public const string MAX_AGE = "Max-Age";

    public const string DOMAIN = "Domain";

    public const string SECURE = "Secure";

    public const string HTTPONLY = "HTTPOnly";

    public const string SAMESITE = "SameSite";

    public const string PARTITIONED = "Partitioned";

    /**
     * Possible values for the SameSite attribute.
     * See <a href="https://tools.ietf.org/html/draft-ietf-httpbis-rfc6265bis-05">changes to RFC6265bis</a>
     */
    // CLR: a Java enum with a method is a sealed class with static readonly instances and a private constructor,
    // as codec-base Base64Dialect : a C# enum cannot declare the static of(String) method. name() is Name() and
    // ToString() returns the constant name as Enum.toString() does. The implicit values()/valueOf(String) members
    // are not added (no caller).
    public sealed class SameSite
    {
        public static readonly SameSite Lax = new SameSite(nameof(Lax));
        public static readonly SameSite Strict = new SameSite(nameof(Strict));
        public static readonly SameSite None = new SameSite(nameof(None));

        private static readonly SameSite[] VALUES = { Lax, Strict, None };

        // CLR: the constant name that java.lang.Enum keeps for name() and toString().
        private readonly string name;

        private SameSite(string name)
        {
            this.name = name;
        }

        public string Name()
        {
            return name;
        }

        public override string ToString()
        {
            return name;
        }

        /**
         * Return the enum value corresponding to the passed in same-site-flag, using a case insensitive comparison.
         *
         * @param name value for the SameSite Attribute
         * @return enum value for the provided name or null
         */
        // CLR: String.equalsIgnoreCase is string.Equals with StringComparison.OrdinalIgnoreCase ("ordinal (binary)
        // sort rules and ignoring the case",
        // https://learn.microsoft.com/en-us/dotnet/api/system.stringcomparison?view=net-10.0; Java compares code
        // points "ignoring case" without the locale,
        // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html).
        internal static SameSite Of(string name)
        {
            if (name != null)
            {
                foreach (SameSite each in VALUES)
                {
                    if (string.Equals(each.Name(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return each;
                    }
                }
            }

            return null;
        }
    }

    // CLR: the private constructor and its comment; a static class has no constructor.
    // Unused.
}
