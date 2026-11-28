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

using System.Globalization;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * ChannelId implementation which is used by our {@link Http2StreamChannel} implementation.
 */
// CLR: serialVersionUID is JVM serialization metadata and has no counterpart (docs/codec-http-porting.md). The
// package-private class is internal. The sequence number is appended with the invariant culture, the String.valueOf
// text of a Java int (Http2CodecUtil.ToJavaString).
internal sealed class Http2StreamChannelId : IChannelId
{
    private readonly int id;
    private readonly IChannelId parentId;

    internal Http2StreamChannelId(IChannelId parentId, int id)
    {
        this.parentId = parentId;
        this.id = id;
    }

    public string AsShortText()
    {
        return parentId.AsShortText() + '/' + id.ToString(CultureInfo.InvariantCulture);
    }

    public string AsLongText()
    {
        return parentId.AsLongText() + '/' + id.ToString(CultureInfo.InvariantCulture);
    }

    public int CompareTo(IChannelId o)
    {
        if (o is Http2StreamChannelId)
        {
            Http2StreamChannelId otherId = (Http2StreamChannelId) o;
            int res = parentId.CompareTo(otherId.parentId);
            if (res == 0)
            {
                return id - otherId.id;
            }
            else
            {
                return res;
            }
        }

        return parentId.CompareTo(o);
    }

    public override int GetHashCode()
    {
        return id * 31 + parentId.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        if (!(obj is Http2StreamChannelId))
        {
            return false;
        }

        Http2StreamChannelId otherId = (Http2StreamChannelId) obj;
        return id == otherId.id && parentId.Equals(otherId.parentId);
    }

    public int GetSequenceId()
    {
        return id;
    }

    public override string ToString()
    {
        return AsShortText();
    }
}
