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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * The default {@link Socks5InitialRequest}.
 */
// CLR: Socks5AuthMethod... is params Socks5AuthMethod[] and Iterable is IEnumerable; List is IList and
// Collections.unmodifiableList is a ReadOnlyCollection<T> wrapper (see
// Netty.NET.Handler.Codec.Socks.SocksInitRequest). checkNonEmpty(list, "list") throws ArgumentException
// (IllegalArgumentException) before the list is wrapped. AbstractCollection.toString() writes "the collection's
// elements..., enclosed in square brackets ("[]")" separated by ", "
// (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/AbstractCollection.html), while a
// ReadOnlyCollection keeps Object.ToString(), so the list is appended as '[' + AppendJoin(", ", list) + ']'
// ("Concatenates and appends the members of a collection, using the specified separator between each member",
// https://learn.microsoft.com/en-us/dotnet/api/system.text.stringbuilder.appendjoin?view=net-10.0).
// CLR: a Java instance method that is not final can be overridden (JLS 8.4.3.3: "A method can be declared final to
// prevent subclasses from overriding or hiding it", https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html), while
// in C# "By default, methods are non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual), so the accessors of this
// non-final class are virtual (Socks5ClientEncoderTest overrides username()/password() of
// DefaultSocks5PasswordAuthRequest).
public class DefaultSocks5InitialRequest : AbstractSocks5Message, ISocks5InitialRequest
{

    private readonly IList<Socks5AuthMethod> authMethods;

    public DefaultSocks5InitialRequest(params Socks5AuthMethod[] authMethods)
    {
        ObjectUtil.CheckNotNull(authMethods, "authMethods");

        List<Socks5AuthMethod> list = new List<Socks5AuthMethod>(authMethods.Length);

        foreach (Socks5AuthMethod m in authMethods)
        {
            if (m == null)
            {
                break;
            }

            list.Add(m);
        }

        CheckNonEmpty(list, "list");

        this.authMethods = new ReadOnlyCollection<Socks5AuthMethod>(list);
    }

    public DefaultSocks5InitialRequest(IEnumerable<Socks5AuthMethod> authMethods)
    {
        ObjectUtil.CheckNotNull(authMethods, "authSchemes");

        List<Socks5AuthMethod> list = new List<Socks5AuthMethod>();

        foreach (Socks5AuthMethod m in authMethods)
        {
            if (m == null)
            {
                break;
            }

            list.Add(m);
        }

        CheckNonEmpty(list, "list");

        this.authMethods = new ReadOnlyCollection<Socks5AuthMethod>(list);
    }

    public virtual IList<Socks5AuthMethod> AuthMethods()
    {
        return authMethods;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(StringUtil.SimpleClassName(this));

        DecoderResult decoderResult = DecoderResult();
        if (!decoderResult.IsSuccess())
        {
            buf.Append("(decoderResult: ");
            buf.Append(decoderResult);
            buf.Append(", authMethods: ");
        }
        else
        {
            buf.Append("(authMethods: ");
        }

        buf.Append('[').AppendJoin(", ", AuthMethods()).Append(']');
        buf.Append(')');

        return buf.ToString();
    }
}
