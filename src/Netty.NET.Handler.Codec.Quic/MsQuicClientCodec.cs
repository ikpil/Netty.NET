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
using System.Net;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the client codec that QuicClientCodecBuilder.Build() returns, the counterpart of the JNI unit
// QuicheQuicClientCodec (jvm-only-replaced). Binding its QuicEndpointChannel only records the local address, which a
// QuicChannelBootstrap uses as QuicClientConnectionOptions.LocalEndPoint (System.Net.Quic binds one UDP socket per
// client connection). connectQuicChannel validates the QuicSslEngine of the sslEngineProvider and starts
// QuicConnection.ConnectAsync for the channel (MsQuicChannel.ConnectNow); as in Java, the parent's connect completes at
// once and the QuicChannel's connect completes with the handshake. The SSL task executor is not used: msquic runs the
// TLS handshake on its own threads.
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class MsQuicClientCodec : MsQuicCodec
{
    private readonly Func<IQuicChannel, QuicSslEngine> sslEngineProvider;

    internal MsQuicClientCodec(MsQuicConfig config, Func<IQuicChannel, QuicSslEngine> sslEngineProvider,
                               IExecutor sslTaskExecutor, int localConnIdLength, IFlushStrategy flushStrategy)
        : base(config, localConnIdLength, flushStrategy)
    {
        this.sslEngineProvider = sslEngineProvider;
    }

    protected override Task ConnectQuicChannel(MsQuicChannel channel, EndPoint remoteAddress, EndPoint localAddress)
    {
        try
        {
            channel.ConnectNow(sslEngineProvider, Config);
        }
        catch (Exception cause)
        {
            // Only fail the original promise. Cleanup will be done as part of the listener attached to it.
            return Task.FromException(cause);
        }

        AddChannel(channel);
        return Task.CompletedTask;
    }
}
