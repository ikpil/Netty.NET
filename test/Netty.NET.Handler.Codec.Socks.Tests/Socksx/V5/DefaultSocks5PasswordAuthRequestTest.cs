/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Handler.Codec.Socksx.V5;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V5;

// CLR: assertInstanceOf(C.class, e) is Assert.IsType<C>(e, exactMatch: false); NullPointerException from
// checkNotNull is ArgumentNullException and IllegalArgumentException is ArgumentException.
public class DefaultSocks5PasswordAuthRequestTest
{
    [Fact]
    public void TestConstructorParamsAreNotNull()
    {
        try
        {
            new DefaultSocks5PasswordAuthRequest(null, "");
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentNullException>(e, exactMatch: false);
        }

        try
        {
            new DefaultSocks5PasswordAuthRequest("", null);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentNullException>(e, exactMatch: false);
        }
    }

    [Fact]
    public void TestUsernameOrPasswordIsNotAscii()
    {
        try
        {
            new DefaultSocks5PasswordAuthRequest("παράδειγμα.δοκιμή", "password");
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }

        try
        {
            new DefaultSocks5PasswordAuthRequest("username", "παράδειγμα.δοκιμή");
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }
    }

    [Fact]
    public void TestUsernameOrPasswordLengthIsLessThan255Chars()
    {
        try
        {
            new DefaultSocks5PasswordAuthRequest(
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword",
                    "password");
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }

        try
        {
            new DefaultSocks5PasswordAuthRequest("password",
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword" +
                    "passwordpasswordpasswordpasswordpasswordpasswordpassword");
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }
    }
}
