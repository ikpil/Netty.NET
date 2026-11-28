/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http;

/**
 * The class of HTTP status.
 */
// CLR: a Java enum with a constructor, fields, methods and a constant-specific class body is a class with static
// readonly instances, as codec-base Base64Dialect and handler LogLevel (docs/codec-base-porting.md,
// docs/handler-porting.md): "You can't define a method inside the definition of an enumeration type"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). The UNKNOWN constant's
// class body (an anonymous subclass in Java) is the private nested UnknownHttpStatusClass that overrides the virtual
// contains(int). ToString() returns the constant name as Enum.toString() does; the implicit values()/valueOf(String)
// members are not added (no caller). The package-private defaultReasonPhrase() is internal.
public class HttpStatusClass
{
    /**
     * The informational class (1xx)
     */
    public static readonly HttpStatusClass INFORMATIONAL = new HttpStatusClass(nameof(INFORMATIONAL), 100, 200, "Informational");
    /**
     * The success class (2xx)
     */
    public static readonly HttpStatusClass SUCCESS = new HttpStatusClass(nameof(SUCCESS), 200, 300, "Success");
    /**
     * The redirection class (3xx)
     */
    public static readonly HttpStatusClass REDIRECTION = new HttpStatusClass(nameof(REDIRECTION), 300, 400, "Redirection");
    /**
     * The client error class (4xx)
     */
    public static readonly HttpStatusClass CLIENT_ERROR = new HttpStatusClass(nameof(CLIENT_ERROR), 400, 500, "Client Error");
    /**
     * The server error class (5xx)
     */
    public static readonly HttpStatusClass SERVER_ERROR = new HttpStatusClass(nameof(SERVER_ERROR), 500, 600, "Server Error");
    /**
     * The unknown class
     */
    public static readonly HttpStatusClass UNKNOWN = new UnknownHttpStatusClass();

    private sealed class UnknownHttpStatusClass : HttpStatusClass
    {
        internal UnknownHttpStatusClass() : base(nameof(UNKNOWN), 0, 0, "Unknown Status")
        {
        }

        public override bool Contains(int code)
        {
            return code < 100 || code >= 600;
        }
    }

    private static readonly HttpStatusClass[] statusArray = new HttpStatusClass[6];
    static HttpStatusClass()
    {
        statusArray[1] = INFORMATIONAL;
        statusArray[2] = SUCCESS;
        statusArray[3] = REDIRECTION;
        statusArray[4] = CLIENT_ERROR;
        statusArray[5] = SERVER_ERROR;
    }

    /**
     * Returns the class of the specified HTTP status code.
     */
    public static HttpStatusClass ValueOf(int code)
    {
        if (UNKNOWN.Contains(code))
        {
            return UNKNOWN;
        }

        return statusArray[Fast_div100(code)];
    }

    /**
     * @param dividend Must >= 0
     * @return dividend/100
     */
    private static int Fast_div100(int dividend)
    {
        return (int) ((dividend * 1374389535L) >> 37);
    }

    /**
     * Returns the class of the specified HTTP status code.
     * @param code Just the numeric portion of the http status code.
     */
    public static HttpStatusClass ValueOf(ICharSequence code)
    {
        if (code != null && code.Length() == 3)
        {
            char c0 = code.CharAt(0);
            return IsDigit(c0) && IsDigit(code.CharAt(1)) && IsDigit(code.CharAt(2)) ? ValueOf(Digit(c0) * 100)
                                                                                     : UNKNOWN;
        }

        return UNKNOWN;
    }

    private static int Digit(char c)
    {
        return c - '0';
    }

    private static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    private readonly int min;
    private readonly int max;
    private readonly AsciiString defaultReasonPhrase;
    // CLR: the constant name that java.lang.Enum keeps for name() and toString().
    private readonly string name;

    private HttpStatusClass(string name, int min, int max, string defaultReasonPhrase)
    {
        this.name = name;
        this.min = min;
        this.max = max;
        this.defaultReasonPhrase = AsciiString.Cached(defaultReasonPhrase);
    }

    /**
     * Returns {@code true} if and only if the specified HTTP status code falls into this class.
     */
    public virtual bool Contains(int code)
    {
        return code >= min && code < max;
    }

    /**
     * Returns the default reason phrase of this HTTP status class.
     */
    internal AsciiString DefaultReasonPhrase()
    {
        return defaultReasonPhrase;
    }

    public override string ToString()
    {
        return name;
    }
}
