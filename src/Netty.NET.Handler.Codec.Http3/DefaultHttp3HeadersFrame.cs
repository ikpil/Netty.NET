/*
 * Copyright 2020 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the interfaces are IHttp3HeadersFrame and IHttp3Headers (docs/codec-http2-porting.md, Shared CLR adaptations).
// Objects.equals is object.Equals(a, b) and Objects.hash(headers) is spelled out (31 + hashCode) to keep the Java
// value. getClass() != o.getClass() is GetType() != o.GetType().
public sealed class DefaultHttp3HeadersFrame : IHttp3HeadersFrame
{

    private readonly IHttp3Headers headers;

    public DefaultHttp3HeadersFrame() : this(new DefaultHttp3Headers())
    {
    }

    public DefaultHttp3HeadersFrame(IHttp3Headers headers)
    {
        this.headers = ObjectUtil.CheckNotNull(headers, "headers");
    }

    public IHttp3Headers Headers()
    {
        return headers;
    }

    public override bool Equals(object o)
    {
        if (ReferenceEquals(this, o))
        {
            return true;
        }
        if (o == null || GetType() != o.GetType())
        {
            return false;
        }

        DefaultHttp3HeadersFrame that = (DefaultHttp3HeadersFrame) o;
        return object.Equals(headers, that.headers);
    }

    public override int GetHashCode()
    {
        return unchecked(31 + (headers == null ? 0 : headers.GetHashCode()));
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(headers=" + Headers() + ')';
    }
}
