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

using System;
using System.Net.Quic;
using System.Numerics;
using System.Runtime.Versioning;
using System.Threading;

namespace Netty.NET.Handler.Codec.Quic;

// Unsupported settings accept their documented defaults. An unset receive window keeps the platform default;
// explicit windows round up to a power of two, capped at 2^30. Zero credit is unsupported.
// An unset or zero idle timeout disables it. Inbound stream limits are capped at 65,535 per stream type.
internal sealed class MsQuicConfig
{
    private const long MAX_WINDOW = 1L << 30;
    private const long MAX_STREAMS = ushort.MaxValue;

    private readonly long? maxIdleTimeout;
    private readonly long? initialMaxData;
    private readonly long? initialMaxStreamDataBidiLocal;
    private readonly long? initialMaxStreamDataBidiRemote;
    private readonly long? initialMaxStreamDataUni;
    private readonly long? initialMaxStreamsBidi;
    private readonly long? initialMaxStreamsUni;

    internal MsQuicConfig(int version, bool? grease, long? maxIdleTimeout, long? maxSendUdpPayloadSize,
                          long? maxRecvUdpPayloadSize, long? initialMaxData, long? initialMaxStreamDataBidiLocal,
                          long? initialMaxStreamDataBidiRemote, long? initialMaxStreamDataUni,
                          long? initialMaxStreamsBidi, long? initialMaxStreamsUni, long? ackDelayExponent,
                          long? maxAckDelay, bool? disableActiveMigration, bool? enableHystart, bool? discoverPmtu,
                          QuicCongestionControlAlgorithm? congestionControlAlgorithm,
                          int? initialCongestionWindowPackets, int? recvQueueLen, int? sendQueueLen,
                          long? activeConnectionIdLimit, byte[] statelessResetToken)
    {
        // quiche settings without a System.Net.Quic counterpart: only quiche's default is accepted.
        Unsupported(version != 1, "version(" + version + "): System.Net.Quic negotiates QUIC version 1 itself");
        Unsupported(grease == false, "grease(false)");
        Unsupported(maxSendUdpPayloadSize != null && maxSendUdpPayloadSize != 1200,
                "maxSendUdpPayloadSize(" + maxSendUdpPayloadSize + ")");
        Unsupported(maxRecvUdpPayloadSize != null && maxRecvUdpPayloadSize != 65527,
                "maxRecvUdpPayloadSize(" + maxRecvUdpPayloadSize + ")");
        Unsupported(ackDelayExponent != null && ackDelayExponent != 3, "ackDelayExponent(" + ackDelayExponent + ")");
        Unsupported(maxAckDelay != null && maxAckDelay != 25, "maxAckDelay(" + maxAckDelay + " ms)");
        // msquic allows the peer to migrate (QUIC_SETTINGS.MigrationEnabled is TRUE by default and System.Net.Quic does
        // not change it), so it sends no disable_active_migration transport parameter: that is activeMigration(true),
        // also quiche's default. activeMigration(false) needs that transport parameter, which System.Net.Quic cannot
        // set.
        Unsupported(disableActiveMigration == true, "activeMigration(false)");
        Unsupported(enableHystart == false, "hystart(false)");
        Unsupported(discoverPmtu == true, "discoverPmtu(true)");
        Unsupported(congestionControlAlgorithm != null &&
                    congestionControlAlgorithm != QuicCongestionControlAlgorithm.CUBIC,
                "congestionControlAlgorithm(" + congestionControlAlgorithm + ")");
        Unsupported(initialCongestionWindowPackets != null && initialCongestionWindowPackets != 10,
                "initialCongestionWindowPackets(" + initialCongestionWindowPackets + ")");
        Unsupported(recvQueueLen != null || sendQueueLen != null,
                "datagram(" + recvQueueLen + ", " + sendQueueLen + "): the QUIC datagram extension");
        Unsupported(activeConnectionIdLimit != null && activeConnectionIdLimit != 2,
                "activeConnectionIdLimit(" + activeConnectionIdLimit + ")");
        Unsupported(statelessResetToken != null, "statelessResetToken(byte[])");
        Unsupported(initialMaxStreamsBidi > MAX_STREAMS,
                "initialMaxStreamsBidirectional(" + initialMaxStreamsBidi + "): msquic allows at most 65535");
        Unsupported(initialMaxStreamsUni > MAX_STREAMS,
                "initialMaxStreamsUnidirectional(" + initialMaxStreamsUni + "): msquic allows at most 65535");
        CheckWindow(initialMaxData, "initialMaxData");
        CheckWindow(initialMaxStreamDataBidiLocal, "initialMaxStreamDataBidirectionalLocal");
        CheckWindow(initialMaxStreamDataBidiRemote, "initialMaxStreamDataBidirectionalRemote");
        CheckWindow(initialMaxStreamDataUni, "initialMaxStreamDataUnidirectional");

        this.maxIdleTimeout = maxIdleTimeout;
        this.initialMaxData = initialMaxData;
        this.initialMaxStreamDataBidiLocal = initialMaxStreamDataBidiLocal;
        this.initialMaxStreamDataBidiRemote = initialMaxStreamDataBidiRemote;
        this.initialMaxStreamDataUni = initialMaxStreamDataUni;
        this.initialMaxStreamsBidi = initialMaxStreamsBidi;
        this.initialMaxStreamsUni = initialMaxStreamsUni;
    }

    // The platforms of System.Net.Quic's [SupportedOSPlatform] attributes, as a guard of the platform analyzer (CA1416)
    // for the public types that reach the msquic implementation (Quic.IsAvailable() also requires Windows or Linux).
    [SupportedOSPlatformGuard("windows")]
    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("macos")]
    internal static bool IsSupportedPlatform =>
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    private static void Unsupported(bool unsupported, string setting)
    {
        if (unsupported)
        {
            throw new NotSupportedException(setting + " is not supported by System.Net.Quic (msquic)");
        }
    }

    private static void CheckWindow(long? window, string setting)
    {
        Unsupported(window == 0, setting + "(0): msquic needs a non-zero receive window");
        Unsupported(window > MAX_WINDOW, setting + "(" + window + "): msquic allows at most 2^30");
    }

    private static int Window(long window)
    {
        return (int) BitOperations.RoundUpToPowerOf2((ulong) window);
    }

    // True when at least one inbound stream type is allowed; System.Net.Quic refuses AcceptInboundStreamAsync otherwise.
    internal bool AcceptsInboundStreams => (initialMaxStreamsBidi ?? 0) > 0 || (initialMaxStreamsUni ?? 0) > 0;

    // The settings as QuicConnectionOptions (shared by QuicClientConnectionOptions and QuicServerConnectionOptions).
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    internal void Apply(QuicConnectionOptions options)
    {
        long idle = maxIdleTimeout ?? 0;
        options.IdleTimeout = idle == 0 ? System.Threading.Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(idle);
        options.MaxInboundBidirectionalStreams = (int) (initialMaxStreamsBidi ?? 0);
        options.MaxInboundUnidirectionalStreams = (int) (initialMaxStreamsUni ?? 0);
        QuicReceiveWindowSizes windows = new QuicReceiveWindowSizes();

        if (initialMaxData != null)
        {
            windows.Connection = Window(initialMaxData.Value);
        }

        if (initialMaxStreamDataBidiLocal != null)
        {
            windows.LocallyInitiatedBidirectionalStream = Window(initialMaxStreamDataBidiLocal.Value);
        }

        if (initialMaxStreamDataBidiRemote != null)
        {
            windows.RemotelyInitiatedBidirectionalStream = Window(initialMaxStreamDataBidiRemote.Value);
        }

        if (initialMaxStreamDataUni != null)
        {
            windows.UnidirectionalStream = Window(initialMaxStreamDataUni.Value);
        }

        options.InitialReceiveWindowSizes = windows;
        options.DefaultStreamErrorCode = 0;
        options.DefaultCloseErrorCode = 0;
    }
}
