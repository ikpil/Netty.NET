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

using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the interfaces are IHttp3PushPromiseFrame and IHttp3Headers. Objects.equals is object.Equals(a, b) and
// Objects.hash(id, headers) is spelled out (31 * (31 + Long.hashCode(id)) + hashCode) to keep the Java value; the
// id prints with the invariant culture. getClass() != o.getClass() is GetType() != o.GetType().
public sealed class DefaultHttp3PushPromiseFrame : IHttp3PushPromiseFrame
{

    private readonly long id;
    private readonly IHttp3Headers headers;

    public DefaultHttp3PushPromiseFrame(long id) : this(id, new DefaultHttp3Headers())
    {
    }

    public DefaultHttp3PushPromiseFrame(long id, IHttp3Headers headers)
    {
        this.id = ObjectUtil.CheckPositiveOrZero(id, "id");
        this.headers = ObjectUtil.CheckNotNull(headers, "headers");
    }

    public long Id()
    {
        return id;
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

        DefaultHttp3PushPromiseFrame that = (DefaultHttp3PushPromiseFrame) o;
        return id == that.id &&
                object.Equals(headers, that.headers);
    }

    public override int GetHashCode()
    {
        return unchecked(31 * (31 + (int) (id ^ (id >>> 32))) + (headers == null ? 0 : headers.GetHashCode()));
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(id=" + Id().ToString(CultureInfo.InvariantCulture) + ", headers=" +
               Headers() + ')';
    }
}
