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

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Used when the remote peer supports the
 * <a href="https://tools.ietf.org/html/draft-ietf-quic-datagram-01">QUIC DATAGRAM extension.</a>
 */
// CLR: the package-private constructor is internal; the length prints with the invariant culture. System.Net.Quic has no
// datagram extension (RFC 9221; docs/quic-design.md, No counterpart in System.Net.Quic): the event keeps its Java API
// (owner decision 2026-11-27) and the msquic implementation (stage 2) never fires it.
public sealed class QuicDatagramExtensionEvent : IQuicExtensionEvent
{

    private readonly int maxLength;

    internal QuicDatagramExtensionEvent(int maxLength)
    {
        this.maxLength = ObjectUtil.CheckPositiveOrZero(maxLength, "maxLength");
    }

    /**
     * The maximum datagram payload length the peer will accept. If you try to write bigger datagrams the write will
     * fail.
     *
     * @return the max length.
     */
    public int MaxLength()
    {
        return maxLength;
    }

    public override string ToString()
    {
        return "QuicDatagramExtensionEvent{" +
                "maxLength=" + maxLength.ToString(CultureInfo.InvariantCulture) +
                '}';
    }
}
