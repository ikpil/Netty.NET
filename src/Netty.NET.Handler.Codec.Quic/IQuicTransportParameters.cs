/*
 * Copyright 2023 The Netty Project
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
 * Transport parameters for QUIC.
 */
// CLR: System.Net.Quic does not report the peer's transport parameters (QuicConnection exposes the negotiated TLS facts
// and the endpoints only); the interface keeps its Java API (owner decision 2026-11-27) and the msquic implementation
// (stage 2) decides what peerTransportParameters() returns.
public interface IQuicTransportParameters
{

    /**
     * The maximum idle timeout.
     * @return timeout.
     */
    long MaxIdleTimeout();

    /**
     * The maximum UDP payload size.
     *
     * @return maximum payload size.
     */
    long MaxUdpPayloadSize();

    /**
     * The initial flow control maximum data for the connection.
     *
     * @return flowcontrol.
     */
    long InitialMaxData();

    /**
     * The initial flow control maximum data for local bidirectional streams.
     *
     * @return flowcontrol.
     */
    long InitialMaxStreamDataBidiLocal();

    /**
     * The initial flow control maximum data for remote bidirectional streams.
     *
     * @return flowcontrol.
     */
    long InitialMaxStreamDataBidiRemote();

    /**
     * The initial flow control maximum data for unidirectional streams.
     *
     * @return flowcontrol.
     */
    long InitialMaxStreamDataUni();

    /**
     * The initial maximum bidirectional streams.
     *
     * @return streams.
     */
    long InitialMaxStreamsBidi();

    /**
     * The initial maximum unidirectional streams.
     *
     * @return streams.
     */
    long InitialMaxStreamsUni();

    /**
     * The ACK delay exponent
     *
     * @return exponent.
     */
    long AckDelayExponent();

    /**
     * The max ACK delay.
     *
     * @return delay.
     */
    long MaxAckDelay();

    /**
     * Whether active migration is disabled.
     *
     * @return disabled.
     */
    bool DisableActiveMigration();

    /**
     * The active connection ID limit.
     *
     * @return limit.
     */
    long ActiveConnIdLimit();

    /**
     * DATAGRAM frame extension parameter, if any.
     *
     * @return param.
     */
    long MaxDatagramFrameSize();
}
