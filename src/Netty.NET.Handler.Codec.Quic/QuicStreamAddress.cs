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
using System.Net;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * A {@link SocketAddress} for QUIC stream.
 */
// CLR: SocketAddress is EndPoint (as Transport's LocalAddress). Objects.hash(streamId) is spelled out
// (31 + Long.hashCode) to keep the Java value; the id prints with the invariant culture.
public sealed class QuicStreamAddress : EndPoint
{

    private readonly long streamId;

    public QuicStreamAddress(long streamId)
    {
        this.streamId = streamId;
    }

    /**
     * Return the id of the stream.
     *
     * @return the id.
     */
    public long StreamId()
    {
        return streamId;
    }

    public override bool Equals(object o)
    {
        if (!(o is QuicStreamAddress))
        {
            return false;
        }
        QuicStreamAddress that = (QuicStreamAddress) o;
        return streamId == that.streamId;
    }

    public override int GetHashCode()
    {
        return unchecked(31 + (int) (streamId ^ (streamId >>> 32)));
    }

    public override string ToString()
    {
        return "QuicStreamAddress{" +
                "streamId=" + streamId.ToString(CultureInfo.InvariantCulture) +
                '}';
    }
}
