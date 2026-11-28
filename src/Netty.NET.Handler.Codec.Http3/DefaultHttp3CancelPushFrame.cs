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

// CLR: the interface is IHttp3CancelPushFrame (docs/codec-http2-porting.md, Shared CLR adaptations). Objects.hash(id) is
// spelled out (31 + Long.hashCode) to keep the Java value, as codec-quic's QuicStreamAddress; the id prints with the
// invariant culture. getClass() != o.getClass() is GetType() != o.GetType().
public sealed class DefaultHttp3CancelPushFrame : IHttp3CancelPushFrame
{
    private readonly long id;

    public DefaultHttp3CancelPushFrame(long id)
    {
        this.id = ObjectUtil.CheckPositiveOrZero(id, "id");
    }

    public long Id()
    {
        return id;
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

        DefaultHttp3CancelPushFrame that = (DefaultHttp3CancelPushFrame) o;
        return id == that.id;
    }

    public override int GetHashCode()
    {
        return unchecked(31 + (int) (id ^ (id >>> 32)));
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "(id=" + Id().ToString(CultureInfo.InvariantCulture) + ')';
    }
}
