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
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

/// <summary>
/// The channel a QUIC codec runs on: a QUIC endpoint (RFC 9000: "an entity that can participate in a QUIC connection by
/// generating, receiving, and processing QUIC packets") whose UDP socket belongs to System.Net.Quic (msquic).
/// </summary>
/// <remarks>
/// <para>In Netty the codec built by <see cref="QuicServerCodecBuilder"/> or <see cref="QuicClientCodecBuilder"/> is the
/// handler of a <c>NioDatagramChannel</c> and runs QUIC in user space on its datagrams. msquic owns the UDP socket, so this
/// CLR-only channel takes the datagram channel's place (owner decision 2026-11-27, listener-based model,
/// docs/quic-design.md):</para>
/// <code>
/// // Server: binding starts a QuicListener; each connection is a child IQuicChannel.
/// IChannel server = await new Bootstrap().Group(group).Channel&lt;QuicEndpointChannel&gt;()
///         .Handler(new QuicServerCodecBuilder().SslContext(context).Handler(...).StreamHandler(...).Build())
///         .BindAsync(new IPEndPoint(IPAddress.Loopback, 9999));
/// // Client: binding keeps the local address; QuicChannel bootstraps connect through the endpoint.
/// IChannel endpoint = await new Bootstrap().Group(group).Channel&lt;QuicEndpointChannel&gt;()
///         .Handler(new QuicClientCodecBuilder().SslContext(context).Build())
///         .BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
/// IQuicChannel quicChannel = await IQuicChannel.NewBootstrap(endpoint).StreamHandler(...)
///         .RemoteAddress(new IPEndPoint(IPAddress.Loopback, 9999)).ConnectAsync();
/// </code>
/// <para>The channel needs the codec in its pipeline when it binds. A server endpoint's <c>LocalAddress</c> is the
/// listener's endpoint; its read() (AUTO_READ by default) accepts connections, which pass its pipeline as
/// <c>channelRead</c> on their way to the codec. Closing the endpoint stops the listener and closes its QUIC channels.
/// The endpoint has no remote address and carries no messages of its own: writes fail with
/// <see cref="NotSupportedException"/>, and connect(...) accepts only the QUIC channels' own connect requests.</para>
/// </remarks>
// CLR-only (no Java counterpart): the name follows RFC 9000's "endpoint" because the channel is neither a datagram
// channel nor a server socket: it is the UDP endpoint of msquic, shared by the client and server codecs. It derives from
// the Transport's AbstractChannel like the other channels of this port and needs no I/O handle, so any event loop of the
// Transport runs it (the QUIC channels run on the endpoint's event loop, as Java's run on the datagram channel's).
public sealed class QuicEndpointChannel : AbstractChannel
{
    private static readonly ChannelMetadata METADATA = new ChannelMetadata(false, 16);

    private volatile EndPoint localAddress;

    // An event loop that shuts down closes the channels registered with its I/O handler (NioIoHandler.PrepareToDestroy),
    // so in Java the datagram channel under the QUIC codec closes and the codec closes its QUIC channels. This channel
    // has no I/O handle for the handler to close; like LocalServerChannel on a loop without an I/O handler, it closes
    // itself from a shutdown hook of its event loop (SingleThreadEventExecutor.AddShutdownHook). Without it, a test that
    // shuts its group down without closing the endpoint (Http3FrameToHttpObjectCodecTest.MultipleFramesInFin, as in
    // Java) left the QUIC streams open and their in-flight receive buffers unreleased (measured 2026-11-27 with the
    // leak presence detector, testsuite-common).
    private readonly Action shutdownHook;

    /// <summary>Creates a new endpoint channel; QUIC must be available (<see cref="Quic.EnsureAvailability"/>).</summary>
    public QuicEndpointChannel() : base(null, METADATA)
    {
        Quic.EnsureAvailability();
        shutdownHook = () => _ = Close();
    }

    protected override void DoRegister()
    {
        if (EventLoop is SingleThreadEventExecutor executor)
        {
            executor.AddShutdownHook(shutdownHook);
        }
    }

    protected override void DoDeregister()
    {
        if (EventLoop is SingleThreadEventExecutor executor)
        {
            executor.RemoveShutdownHook(shutdownHook);
        }
    }

    protected override EndPoint LocalAddress0()
    {
        return localAddress;
    }

    protected override EndPoint RemoteAddress0()
    {
        return null;
    }

    // The QUIC codec of the pipeline (the builders' Build() result), or null.
    private MsQuicCodec Codec()
    {
        foreach (KeyValuePair<string, IChannelHandler> entry in Pipeline)
        {
            if (entry.Value is MsQuicCodec codec)
            {
                return codec;
            }
        }

        return null;
    }

    protected override async Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    {
        if (!MsQuicConfig.IsSupportedPlatform)
        {
            throw new PlatformNotSupportedException("System.Net.Quic is not supported on this platform");
        }

        MsQuicCodec codec = Codec();
        if (codec == null)
        {
            throw new InvalidOperationException("No QUIC codec (QuicServerCodecBuilder or QuicClientCodecBuilder " +
                                                "Build()) in the pipeline of " + this);
        }

        localAddress = await codec.BindAsync(this, address, cancellationToken).ConfigureAwait(false);
        SetActive(true);
    }

    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        return Task.FromException(new NotSupportedException(
                "A QUIC endpoint is not connected; connect QUIC channels with IQuicChannel.NewBootstrap(endpoint)"));
    }

    protected override void DoBeginRead()
    {
        if (MsQuicConfig.IsSupportedPlatform)
        {
            Codec()?.BeginRead(this);
        }
    }

    protected override void DoClose()
    {
        if (MsQuicConfig.IsSupportedPlatform)
        {
            Codec()?.EndpointClosed(this);
        }
    }

    protected override object FilterOutboundMessage(object msg)
    {
        throw new NotSupportedException("unsupported message type: " + StringUtil.SimpleClassName(msg) +
                                        " (a QUIC endpoint carries no messages)");
    }

    protected override void HandleWriteError(Exception t)
    {
        OutboundBuffer.Remove(t);
    }
}
