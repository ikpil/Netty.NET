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
using System.Linq;
using System.Net;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the QUIC codec that QuicCodecBuilder.Build() returns, the counterpart of the JNI unit QuicheQuicCodec
// (jvm-only-replaced). In Java the codec is the handler of a DatagramChannel and runs QUIC in user space on its
// datagrams; msquic owns the UDP socket, so the codec is the handler of a QuicEndpointChannel and holds what System.Net.Quic
// needs: the connection settings (MsQuicConfig), the sslEngineProvider and, for a server, the handler, options and
// attributes of the accepted QuicChannels. It is not sharable (one codec per endpoint, as Java's), tracks the QuicChannels
// of its endpoint and closes them when the endpoint closes or the codec is removed (handlerRemoved: forceClose()), and
// turns the connect(MsQuicChannelAddress) of a client QuicChannel into a connection (connectQuicChannel). The datagram
// machinery of QuicheQuicCodec (connection ID routing, flush strategy, segmented packets, token handling) belongs to
// msquic: a non-default local connection ID length or FlushStrategy reports NotSupportedException (MsQuicConfig).
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal abstract class MsQuicCodec : ChannelDuplexHandler
{
    private readonly HashSet<MsQuicChannel> channels = new HashSet<MsQuicChannel>();
    private IChannelHandlerContext context;

    protected MsQuicCodec(MsQuicConfig config, int localConnIdLength, IFlushStrategy flushStrategy)
    {
        if (localConnIdLength != Quic.MAX_CONN_ID_LEN)
        {
            throw new NotSupportedException("localConnectionIdLength(" + localConnIdLength +
                                            ") is not supported by System.Net.Quic (msquic)");
        }
        if (!ReferenceEquals(flushStrategy, IFlushStrategy.DEFAULT))
        {
            throw new NotSupportedException("flushStrategy(FlushStrategy) is not supported by System.Net.Quic " +
                                            "(msquic sends the datagrams itself)");
        }
        Config = config;
    }

    internal MsQuicConfig Config { get; }

    public sealed override bool IsSharable => false;

    // The endpoint channel the codec is added to.
    protected QuicEndpointChannel Endpoint => (QuicEndpointChannel) context?.Channel;

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        if (!(ctx.Channel is QuicEndpointChannel))
        {
            throw new InvalidOperationException(
                    "The QUIC codec runs on a " + nameof(QuicEndpointChannel) + " (System.Net.Quic owns the UDP socket), " +
                    "not on " + ctx.Channel.GetType().Name);
        }
        context = ctx;
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        if (ctx.Channel is QuicEndpointChannel endpoint)
        {
            EndpointClosed(endpoint);
        }
        context = null;
    }

    // Binds the endpoint: a server starts its QuicListener, a client only keeps the address for its connections.
    internal virtual Task<EndPoint> BindAsync(QuicEndpointChannel endpoint, EndPoint localAddress,
                                              CancellationToken cancellationToken)
    {
        return Task.FromResult(localAddress);
    }

    // The endpoint's read(): a server accepts the next connection.
    internal virtual void BeginRead(QuicEndpointChannel endpoint)
    {
    }

    // The endpoint closed: its QuicChannels close.
    internal virtual void EndpointClosed(QuicEndpointChannel endpoint)
    {
        CloseChannels();
    }

    public override Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress,
                                      CancellationToken cancellationToken)
    {
        if (remoteAddress is MsQuicChannelAddress addr)
        {
            return ConnectQuicChannel(addr.Channel, remoteAddress, localAddress);
        }
        return ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    // Connects the given QuicChannel; a server codec fails it (NotSupportedException).
    protected abstract Task ConnectQuicChannel(MsQuicChannel channel, EndPoint remoteAddress, EndPoint localAddress);

    // addChannel/removeChannel: on the endpoint's event loop.
    protected void AddChannel(MsQuicChannel channel)
    {
        if (channels.Add(channel))
        {
            _ = channel.CloseFuture.ContinueWith(_ =>
            {
                try
                {
                    channel.EventLoop.Execute(() => channels.Remove(channel));
                }
                catch (Exception)
                {
                    // The event loop is gone.
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private void CloseChannels()
    {
        foreach (MsQuicChannel ch in channels.ToArray())
        {
            ch.ForceClose();
        }
        channels.Clear();
    }
}
