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

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Statistics about the {@code QUIC} connection. If unknown by the implementation it might return {@code -1} values
 * for the various methods.
 */
// CLR: System.Net.Quic reports no connection statistics (docs/quic-design.md, No counterpart in System.Net.Quic); the
// interface keeps its Java API (owner decision 2026-11-27) and the msquic implementation (stage 2) reports
// collectStats as unsupported.
public interface IQuicConnectionStats
{
    /**
     * @return The number of QUIC packets received on the connection.
     */
    long Recv();

    /**
     * @return The number of QUIC packets sent on this connection.
     */
    long Sent();

    /**
     * @return The number of QUIC packets that were lost.
     */
    long Lost();

    /**
     * @return The number of sent QUIC packets with retransmitted data.
     */
    long Retrans();

    /**
     * @return The number of sent bytes.
     */
    long SentBytes();

    /**
     * @return The number of received bytes.
     */
    long RecvBytes();

    /**
     * @return  The number of bytes lost.
     */
    long LostBytes();

    /**
     * @return  The number of stream bytes retransmitted.
     */
    long StreamRetransBytes();

    /**
     * @return   The number of known paths for the connection.
     */
    long PathsCount();
}
