/*
 * Copyright 2017 The Netty Project
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

using Netty.NET.Common.Internal;
using static Netty.NET.Common.AsciiString;
using static Netty.NET.Handler.Codec.Http2.IHttp2Headers;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Http2Headers implementation that preserves headers insertion order.
 */
// CLR: DefaultHeaders<CharSequence, CharSequence, Http2Headers> is the codec-base DefaultHeaders<ICharSequence,
// ICharSequence, IHttp2Headers> (docs/codec-base-porting.md, Headers); PseudoHeaderName is the nested type of
// IHttp2Headers, imported with using static (an implementing class does not see the nested types of its interfaces by
// their simple names). The package-private constructor is internal. equals/hashCode are Equals/GetHashCode.
public class InOrderHttp2Headers
        : DefaultHeaders<ICharSequence, ICharSequence, IHttp2Headers>, IHttp2Headers
{

    internal InOrderHttp2Headers() : base(CharSequenceValueConverter.INSTANCE)
    {
    }

    public override bool Equals(object o)
    {
        return o is IHttp2Headers && Equals((IHttp2Headers) o, CASE_SENSITIVE_HASHER);
    }

    public override int GetHashCode()
    {
        return HashCode(CASE_SENSITIVE_HASHER);
    }

    public IHttp2Headers Method(ICharSequence value)
    {
        Set(PseudoHeaderName.METHOD.Value(), value);
        return this;
    }

    public IHttp2Headers Scheme(ICharSequence value)
    {
        Set(PseudoHeaderName.SCHEME.Value(), value);
        return this;
    }

    public IHttp2Headers Authority(ICharSequence value)
    {
        Set(PseudoHeaderName.AUTHORITY.Value(), value);
        return this;
    }

    public IHttp2Headers Path(ICharSequence value)
    {
        Set(PseudoHeaderName.PATH.Value(), value);
        return this;
    }

    public IHttp2Headers Status(ICharSequence value)
    {
        Set(PseudoHeaderName.STATUS.Value(), value);
        return this;
    }

    public ICharSequence Method()
    {
        return Get(PseudoHeaderName.METHOD.Value());
    }

    public ICharSequence Scheme()
    {
        return Get(PseudoHeaderName.SCHEME.Value());
    }

    public ICharSequence Authority()
    {
        return Get(PseudoHeaderName.AUTHORITY.Value());
    }

    public ICharSequence Path()
    {
        return Get(PseudoHeaderName.PATH.Value());
    }

    public ICharSequence Status()
    {
        return Get(PseudoHeaderName.STATUS.Value());
    }

    public bool Contains(ICharSequence name, ICharSequence value, bool caseInsensitive)
    {
        return Contains(name, value, caseInsensitive ? CASE_INSENSITIVE_HASHER : CASE_SENSITIVE_HASHER);
    }
}
