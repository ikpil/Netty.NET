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
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * The default {@link Socks5PasswordAuthRequest}.
 */
// CLR: IllegalArgumentException is ArgumentException; StringUtil.simpleClassName is StringUtil.SimpleClassName.
// CLR: a Java instance method that is not final can be overridden (JLS 8.4.3.3: "A method can be declared final to
// prevent subclasses from overriding or hiding it", https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html), while
// in C# "By default, methods are non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual), so the accessors of this
// non-final class are virtual (Socks5ClientEncoderTest overrides username()/password() of
// DefaultSocks5PasswordAuthRequest).
public class DefaultSocks5PasswordAuthRequest : AbstractSocks5Message, ISocks5PasswordAuthRequest
{

    private readonly string username;
    private readonly string password;

    public DefaultSocks5PasswordAuthRequest(string username, string password)
    {
        ObjectUtil.CheckNotNull(username, "username");
        ObjectUtil.CheckNotNull(password, "password");

        if (username.Length > 255)
        {
            throw new ArgumentException("username: **** (expected: less than 256 chars)");
        }
        if (password.Length > 255)
        {
            throw new ArgumentException("password: **** (expected: less than 256 chars)");
        }

        this.username = username;
        this.password = password;
    }

    public virtual string Username()
    {
        return username;
    }

    public virtual string Password()
    {
        return password;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(StringUtil.SimpleClassName(this));

        DecoderResult decoderResult = DecoderResult();
        if (!decoderResult.IsSuccess())
        {
            buf.Append("(decoderResult: ");
            buf.Append(decoderResult);
            buf.Append(", username: ");
        }
        else
        {
            buf.Append("(username: ");
        }

        buf.Append(Username());
        buf.Append(", password: ****)");

        return buf.ToString();
    }
}
