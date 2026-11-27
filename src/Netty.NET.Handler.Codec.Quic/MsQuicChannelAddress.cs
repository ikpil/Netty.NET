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

using System.Net;

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the counterpart of the JNI unit QuicheQuicChannelAddress (jvm-only-replaced). A client MsQuicChannel connects
// by calling connect(...) on its parent, the QuicEndpointChannel, with this address, as QuicheQuicChannel does on its
// DatagramChannel; the client codec in the parent's pipeline recognizes it and starts QuicConnection.ConnectAsync for
// the channel. Every handler of the parent's pipeline sees the connect, so one can fail it (QuicChannelConnectTest
// testConnectFailsInParentPipeline).
internal sealed class MsQuicChannelAddress : EndPoint
{
    internal MsQuicChannelAddress(MsQuicChannel channel)
    {
        Channel = channel;
    }

    internal MsQuicChannel Channel { get; }
}
