/*
 * Copyright 2024 The Netty Project
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

using System.Net;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Statistics about a path of the {@code QUIC} connection.
 * If unknown by the implementation it might return {@code -1} values
 * for the various methods.
 */
// CLR: InetSocketAddress is IPEndPoint. System.Net.Quic reports no path statistics; the interface keeps its Java
// API and the msquic implementation (stage 2) reports collectPathStats as unsupported.
public interface IQuicConnectionPathStats
{
    /**
     * @return The local address used by this path.
     */
    IPEndPoint LocalAddress();

    /**
     * @return The peer address seen by this path.
     */
    IPEndPoint PeerAddress();

    /**
     * @return The validation state of the path.
     */
    long ValidationState();

    /**
     * @return Whether this path is active.
     */
    bool Active();

    /**
     * @return The number of QUIC packets received on this path.
     */
    long Recv();

    /**
     * @return The number of QUIC packets sent on this path.
     */
    long Sent();

    /**
     * @return The number of QUIC packets that were lost on this path.
     */
    long Lost();

    /**
     * @return The number of sent QUIC packets with retransmitted data on this path.
     */
    long Retrans();

    /**
     * @return The estimated round-trip time of the path (in nanoseconds).
     */
    long Rtt();

    /**
     * @return The size of the path's congestion window in bytes.
     */
    long Cwnd();

    /**
     * @return The number of sent bytes on this path.
     */
    long SentBytes();

    /**
     * @return The number of received bytes on this path.
     */
    long RecvBytes();

    /**
     * @return The number of bytes lost on this path.
     */
    long LostBytes();

    /**
     * @return The number of stream bytes retransmitted on this path.
     */
    long StreamRetransBytes();

    /**
     * @return The current PMTU for the path.
     */
    long Pmtu();

    /**
     * @return The most recent data delivery rate estimate in bytes/s.
     */
    long DeliveryRate();
}
