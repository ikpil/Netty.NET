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

using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * The default {@link Socks5InitialResponse}.
 */
// CLR: StringUtil.simpleClassName is StringUtil.SimpleClassName.
// CLR: a Java instance method that is not final can be overridden (JLS 8.4.3.3: "A method can be declared final to
// prevent subclasses from overriding or hiding it", https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html), while
// in C# "By default, methods are non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual), so the accessors of this
// non-final class are virtual (Socks5ClientEncoderTest overrides username()/password() of
// DefaultSocks5PasswordAuthRequest).
public class DefaultSocks5InitialResponse : AbstractSocks5Message, ISocks5InitialResponse
{

    private readonly Socks5AuthMethod authMethod;

    public DefaultSocks5InitialResponse(Socks5AuthMethod authMethod)
    {
        this.authMethod = ObjectUtil.CheckNotNull(authMethod, "authMethod");
    }

    public virtual Socks5AuthMethod AuthMethod()
    {
        return authMethod;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(StringUtil.SimpleClassName(this));

        DecoderResult decoderResult = DecoderResult();
        if (!decoderResult.IsSuccess())
        {
            buf.Append("(decoderResult: ");
            buf.Append(decoderResult);
            buf.Append(", authMethod: ");
        }
        else
        {
            buf.Append("(authMethod: ");
        }
        buf.Append(AuthMethod());
        buf.Append(')');

        return buf.ToString();
    }
}
