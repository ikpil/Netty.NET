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

using System.Collections.Generic;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Stomp;

// CLR: CharSequence is ICharSequence and the codec-base DefaultHeaders<K, V, T> and HeadersUtils keep their Java
// members (docs/codec-base-porting.md, Headers); the static imports AsciiString.CASE_SENSITIVE_HASHER and
// CASE_INSENSITIVE_HASHER are the Common AsciiString IEqualityComparer<ICharSequence> fields. contains(name, value)
// overrides the virtual DefaultHeaders.Contains(K, V), and copy() narrows the virtual DefaultHeaders.Copy() with a
// covariant override return (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override).
public class DefaultStompHeaders
        : DefaultHeaders<ICharSequence, ICharSequence, IStompHeaders>, IStompHeaders
{
    public DefaultStompHeaders() : base(AsciiString.CASE_SENSITIVE_HASHER, CharSequenceValueConverter.INSTANCE)
    {
    }

    public string GetAsString(ICharSequence name)
    {
        return HeadersUtils.GetAsString(this, name);
    }

    public IList<string> GetAllAsString(ICharSequence name)
    {
        return HeadersUtils.GetAllAsString(this, name);
    }

    public IIterator<IEntry<string, string>> IteratorAsString()
    {
        return HeadersUtils.IteratorAsString(this);
    }

    public override bool Contains(ICharSequence name, ICharSequence value)
    {
        return Contains(name, value, false);
    }

    public bool Contains(ICharSequence name, ICharSequence value, bool ignoreCase)
    {
        return Contains(name, value,
                ignoreCase ? AsciiString.CASE_INSENSITIVE_HASHER : AsciiString.CASE_SENSITIVE_HASHER);
    }

    public override DefaultStompHeaders Copy()
    {
        DefaultStompHeaders copyHeaders = new DefaultStompHeaders();
        copyHeaders.AddImpl(this);
        return copyHeaders;
    }
}
