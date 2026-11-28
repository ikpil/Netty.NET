/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: The pseudo-header setters narrow the return type to EmptyHttp2Headers; C# 9 covariant returns cover class
// overrides only, so the public methods return EmptyHttp2Headers and the IHttp2Headers members are implemented
// explicitly with them (as codec-http's message classes, docs/codec-http-porting.md, Shared CLR adaptations).
// UnsupportedOperationException is NotSupportedException.
public sealed class EmptyHttp2Headers
        : EmptyHeaders<ICharSequence, ICharSequence, IHttp2Headers>, IHttp2Headers
{
    public static readonly EmptyHttp2Headers INSTANCE = new EmptyHttp2Headers();

    private EmptyHttp2Headers()
    {
    }

    public EmptyHttp2Headers Method(ICharSequence method)
    {
        throw new NotSupportedException();
    }

    public EmptyHttp2Headers Scheme(ICharSequence status)
    {
        throw new NotSupportedException();
    }

    public EmptyHttp2Headers Authority(ICharSequence authority)
    {
        throw new NotSupportedException();
    }

    public EmptyHttp2Headers Path(ICharSequence path)
    {
        throw new NotSupportedException();
    }

    public EmptyHttp2Headers Status(ICharSequence status)
    {
        throw new NotSupportedException();
    }

    IHttp2Headers IHttp2Headers.Method(ICharSequence value)
    {
        return Method(value);
    }

    IHttp2Headers IHttp2Headers.Scheme(ICharSequence value)
    {
        return Scheme(value);
    }

    IHttp2Headers IHttp2Headers.Authority(ICharSequence value)
    {
        return Authority(value);
    }

    IHttp2Headers IHttp2Headers.Path(ICharSequence value)
    {
        return Path(value);
    }

    IHttp2Headers IHttp2Headers.Status(ICharSequence value)
    {
        return Status(value);
    }

    public ICharSequence Method()
    {
        return Get(IHttp2Headers.PseudoHeaderName.METHOD.Value());
    }

    public ICharSequence Scheme()
    {
        return Get(IHttp2Headers.PseudoHeaderName.SCHEME.Value());
    }

    public ICharSequence Authority()
    {
        return Get(IHttp2Headers.PseudoHeaderName.AUTHORITY.Value());
    }

    public ICharSequence Path()
    {
        return Get(IHttp2Headers.PseudoHeaderName.PATH.Value());
    }

    public ICharSequence Status()
    {
        return Get(IHttp2Headers.PseudoHeaderName.STATUS.Value());
    }

    public bool Contains(ICharSequence name, ICharSequence value, bool caseInsensitive)
    {
        return false;
    }
}
