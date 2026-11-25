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

namespace Netty.NET.Handler.Codec.Http.Tests.Cookie;

using Netty.NET.Handler.Codec.Http.Cookie;

// CLR: the cookie namespace is imported inside the test namespace, so that ClientCookieEncoder is the cookie type and not
// the deprecated Netty.NET.Handler.Codec.Http.ClientCookieEncoder of an enclosing namespace. Cookie is ICookie;
// assertThrows(IllegalArgumentException.class, executable) is Assert.Throws<ArgumentException>, and the anonymous
// Executable is a lambda. The package-private COOKIE_COMPARATOR is internal (InternalsVisibleTo).
public class ClientCookieEncoderTest
{

    [Fact]
    public void TestEncodingMultipleClientCookies()
    {
        string c1 = "myCookie=myValue";
        string c2 = "myCookie2=myValue2";
        string c3 = "myCookie3=myValue3";
        ICookie cookie1 = new DefaultCookie("myCookie", "myValue");
        cookie1.SetDomain(".adomainsomewhere");
        cookie1.SetMaxAge(50);
        cookie1.SetPath("/apathsomewhere");
        cookie1.SetSecure(true);
        ICookie cookie2 = new DefaultCookie("myCookie2", "myValue2");
        cookie2.SetDomain(".anotherdomainsomewhere");
        cookie2.SetPath("/anotherpathsomewhere");
        cookie2.SetSecure(false);
        ICookie cookie3 = new DefaultCookie("myCookie3", "myValue3");
        string encodedCookie = ClientCookieEncoder.STRICT.Encode(cookie1, cookie2, cookie3);
        // Cookies should be sorted into decreasing order of path length, as per RFC6265.
        // When no path is provided, we assume maximum path length (so cookie3 comes first).
        Assert.Equal(c3 + "; " + c2 + "; " + c1, encodedCookie);
    }

    [Fact]
    public void TestWrappedCookieValue()
    {
        ClientCookieEncoder.STRICT.Encode(new DefaultCookie("myCookie", "\"foo\""));
    }

    [Fact]
    public void TestRejectCookieValueWithSemicolon()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            ClientCookieEncoder.STRICT.Encode(new DefaultCookie("myCookie", "foo;bar"));
        });
    }

    [Fact]
    public void TestComparatorForSamePathLength()
    {
        ICookie cookie = new DefaultCookie("test", "value");
        cookie.SetPath("1");

        ICookie cookie2 = new DefaultCookie("test", "value");
        cookie2.SetPath("2");

        Assert.Equal(0, ClientCookieEncoder.COOKIE_COMPARATOR.Compare(cookie, cookie2));
        Assert.Equal(0, ClientCookieEncoder.COOKIE_COMPARATOR.Compare(cookie2, cookie));
    }
}
