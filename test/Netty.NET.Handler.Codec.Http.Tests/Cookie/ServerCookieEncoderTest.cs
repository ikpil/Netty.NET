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

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http.Tests.Cookie;

using Netty.NET.Handler.Codec.Http.Cookie;
using SameSite = Netty.NET.Handler.Codec.Http.Cookie.CookieHeaderNames.SameSite;

// CLR: the cookie namespace is imported inside the test namespace, so that ClientCookieEncoder is the cookie type (see
// ClientCookieEncoderTest). Cookie is ICookie and List<String>/Set<Character> are List<string>/HashSet<char>.
// Pattern.compile(result).matcher(s).find() and group(1) are Regex.Match(s, result).Success and Groups[1].Value (the
// pattern uses no syntax whose meaning differs). java.util.Date is DateTimeOffset (codec-base DateFormatter, whose
// parseHttpDate takes the text as a StringCharSequence) and System.currentTimeMillis() is
// DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(). "throws ParseException" has no counterpart. assertThat(s).contains(x) is
// Assert.Contains(x, s), and toLowerCase() of the message is ToLowerInvariant() (the message is ASCII). Java's catch of
// IllegalArgumentException is catch (ArgumentException).
public class ServerCookieEncoderTest
{

    [Fact]
    public void TestEncodingSingleCookieV0()
    {

        int maxAge = 50;

        string result = "myCookie=myValue; Max-Age=50; Expires=(.+?); Path=/apathsomewhere;" +
                " Domain=.adomainsomewhere; Secure; SameSite=Lax; Partitioned";
        DefaultCookie cookie = new DefaultCookie("myCookie", "myValue");
        cookie.SetDomain(".adomainsomewhere");
        cookie.SetMaxAge(maxAge);
        cookie.SetPath("/apathsomewhere");
        cookie.SetSecure(true);
        cookie.SetSameSite(SameSite.Lax);
        cookie.SetPartitioned(true);

        string encodedCookie = ServerCookieEncoder.STRICT.Encode(cookie);

        Match matcher = Regex.Match(encodedCookie, result);
        Assert.True(matcher.Success);
        DateTimeOffset expiresDate = DateFormatter.ParseHttpDate(new StringCharSequence(matcher.Groups[1].Value)).Value;
        long diff = (expiresDate.ToUnixTimeMilliseconds() - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000;
        // 2 secs should be fine
        Assert.True(Math.Abs(diff - maxAge) <= 2);
    }

    [Fact]
    public void TestEncodingWithNoCookies()
    {
        string encodedCookie1 = ClientCookieEncoder.STRICT.Encode();
        IList<string> encodedCookie2 = ServerCookieEncoder.STRICT.Encode();
        Assert.Null(encodedCookie1);
        Assert.NotNull(encodedCookie2);
        Assert.True(encodedCookie2.Count == 0);
    }

    [Fact]
    public void TestEncodingMultipleCookiesStrict()
    {
        List<string> result = new List<string>();
        result.Add("cookie2=value2");
        result.Add("cookie1=value3");
        ICookie cookie1 = new DefaultCookie("cookie1", "value1");
        ICookie cookie2 = new DefaultCookie("cookie2", "value2");
        ICookie cookie3 = new DefaultCookie("cookie1", "value3");
        IList<string> encodedCookies = ServerCookieEncoder.STRICT.Encode(cookie1, cookie2, cookie3);
        Assert.Equal(result, encodedCookies);
    }

    [Fact]
    public void IllegalCharInCookieNameMakesStrictEncoderThrowsException()
    {
        ISet<char> illegalChars = new HashSet<char>();
        // CTLs
        for (int i = 0x00; i <= 0x1F; i++)
        {
            illegalChars.Add((char) i);
        }
        illegalChars.Add((char) 0x7F);
        // separators
        foreach (char c in new char[] { '(', ')', '<', '>', '@', ',', ';', ':', '\\', '"', '/', '[', ']',
                '?', '=', '{', '}', ' ', '\t' })
        {
            illegalChars.Add(c);
        }

        int exceptions = 0;

        foreach (char c in illegalChars)
        {
            try
            {
                ServerCookieEncoder.STRICT.Encode(new DefaultCookie("foo" + c + "bar", "value"));
            }
            catch (ArgumentException)
            {
                exceptions++;
            }
        }

        Assert.Equal(illegalChars.Count, exceptions);
    }

    [Fact]
    public void IllegalCharInCookieValueMakesStrictEncoderThrowsException()
    {
        ISet<char> illegalChars = new HashSet<char>();
        // CTLs
        for (int i = 0x00; i <= 0x1F; i++)
        {
            illegalChars.Add((char) i);
        }
        illegalChars.Add((char) 0x7F);
        // whitespace, DQUOTE, comma, semicolon, and backslash
        foreach (char c in new char[] { ' ', '"', ',', ';', '\\' })
        {
            illegalChars.Add(c);
        }

        int exceptions = 0;

        foreach (char c in illegalChars)
        {
            try
            {
                ServerCookieEncoder.STRICT.Encode(new DefaultCookie("name", "value" + c));
            }
            catch (ArgumentException)
            {
                exceptions++;
            }
        }

        Assert.Equal(illegalChars.Count, exceptions);
    }

    [Fact]
    public void IllegalCharInWrappedValueAppearsInException()
    {
        try
        {
            ServerCookieEncoder.STRICT.Encode(new DefaultCookie("name", "\"value,\""));
        }
        catch (ArgumentException e)
        {
            Assert.Contains("cookie value contains an invalid char: ,", e.Message.ToLowerInvariant());
        }
    }

    [Fact]
    public void TestEncodingMultipleCookiesLax()
    {
        List<string> result = new List<string>();
        result.Add("cookie1=value1");
        result.Add("cookie2=value2");
        result.Add("cookie1=value3");
        ICookie cookie1 = new DefaultCookie("cookie1", "value1");
        ICookie cookie2 = new DefaultCookie("cookie2", "value2");
        ICookie cookie3 = new DefaultCookie("cookie1", "value3");
        IList<string> encodedCookies = ServerCookieEncoder.LAX.Encode(cookie1, cookie2, cookie3);
        Assert.Equal(result, encodedCookies);
    }
}
