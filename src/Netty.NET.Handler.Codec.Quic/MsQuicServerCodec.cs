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
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the server codec that QuicServerCodecBuilder.Build() returns, the counterpart of the JNI unit
// QuicheQuicServerCodec (jvm-only-replaced). Binding its QuicEndpointChannel starts a QuicListener on the address
// (QuicListener.ListenAsync); the endpoint's LocalAddress is the listener's LocalEndPoint (the port the system chose for
// port 0). For each new connection msquic asks for its options (QuicListenerOptions.ConnectionOptionsCallback, on an
// msquic thread): the codec creates the server QuicChannel there, applies the builder's handler, options and attributes
// (Quic.setupChannel), asks the sslEngineProvider for its QuicSslEngine (which must be a server engine of this
// implementation, else IllegalArgumentException as in Java), selects the SNI context of the client's server name when the
// context has an SNI mapping, and returns the QuicServerConnectionOptions of the builder's settings with the engine's
// SslServerAuthenticationOptions. The endpoint's read() accepts the next connection (QuicListener.AcceptConnectionAsync,
// which completes after the handshake), and the accepted QuicChannel goes through the endpoint's pipeline as
// channelRead, where this codec registers it with the endpoint's event loop (channelActive, then
// SslHandshakeCompletionEvent.SUCCESS), as QuicheQuicServerCodec registers the channels it creates. A connection whose
// handshake fails never becomes a QuicChannel (the listener drops it); it is logged.
//
// The listener needs its ALPN list before any connection (QuicListenerOptions.ApplicationProtocols, at least one):
// binding creates the QuicChannel of the first connection and its QuicSslEngine in advance, and the listener uses that
// engine's ALPN protocols (with SslContext(QuicSslContext) they are the context's). The connection ID generator, the
// reset token generator and token handlers other than NoQuicTokenHandler/InsecureQuicTokenHandler are reported as
// unsupported by QuicServerCodecBuilder: msquic generates connection IDs and reset tokens and validates addresses with
// its own retry tokens.
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class MsQuicServerCodec : MsQuicCodec
{
    private static readonly IInternalLogger LOGGER = InternalLoggerFactory.GetInstance(typeof(MsQuicServerCodec));

    private readonly Func<IQuicChannel, QuicSslEngine> sslEngineProvider;
    private readonly IChannelHandler handler;
    private readonly KeyValuePair<ChannelOption, object>[] optionsArray;
    private readonly KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] attrsArray;
    private readonly IChannelHandler streamHandler;
    private readonly KeyValuePair<ChannelOption, object>[] streamOptionsArray;
    private readonly KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] streamAttrsArray;
    private readonly ConditionalWeakTable<QuicConnection, MsQuicChannel> connecting =
            new ConditionalWeakTable<QuicConnection, MsQuicChannel>();

    private volatile QuicListener listener;
    private (MsQuicChannel Channel, MsQuicSslEngine Engine)? nextChannel;
    private bool acceptInFlight;
    private bool readPending;

    internal MsQuicServerCodec(MsQuicConfig config, int localConnIdLength, IFlushStrategy flushStrategy,
                               Func<IQuicChannel, QuicSslEngine> sslEngineProvider, IChannelHandler handler,
                               KeyValuePair<ChannelOption, object>[] optionsArray,
                               KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] attrsArray,
                               IChannelHandler streamHandler,
                               KeyValuePair<ChannelOption, object>[] streamOptionsArray,
                               KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] streamAttrsArray)
        : base(config, localConnIdLength, flushStrategy)
    {
        this.sslEngineProvider = sslEngineProvider;
        this.handler = handler;
        this.optionsArray = optionsArray;
        this.attrsArray = attrsArray;
        this.streamHandler = streamHandler;
        this.streamOptionsArray = streamOptionsArray;
        this.streamAttrsArray = streamAttrsArray;
    }

    internal override async Task<EndPoint> BindAsync(QuicEndpointChannel endpoint, EndPoint localAddress,
                                                     CancellationToken cancellationToken)
    {
        if (!(localAddress is IPEndPoint listenEndPoint))
        {
            throw new ArgumentException("A QUIC server binds an IPEndPoint: " + localAddress);
        }

        (MsQuicChannel Channel, MsQuicSslEngine Engine) first = NewChannel(endpoint);
        List<SslApplicationProtocol> protocols = first.Engine.ServerAuthenticationOptions().ApplicationProtocols;

        lock (this)
        {
            nextChannel = first;
        }

        QuicListenerOptions options = new QuicListenerOptions
        {
            ListenEndPoint = listenEndPoint,
            ApplicationProtocols = protocols == null
                    ? new List<SslApplicationProtocol>()
                    : new List<SslApplicationProtocol>(protocols),
            ConnectionOptionsCallback = (connection, clientHello, _) =>
                    ValueTask.FromResult(NewConnectionOptions(endpoint, connection, clientHello)),
        };
        QuicListener quicListener = await QuicListener.ListenAsync(options, cancellationToken).ConfigureAwait(false);
        listener = quicListener;
        return quicListener.LocalEndPoint;
    }

    // Creates a server QuicChannel with its QuicSslEngine (quicPacketRead of QuicheQuicServerCodec).
    private (MsQuicChannel Channel, MsQuicSslEngine Engine) NewChannel(QuicEndpointChannel endpoint)
    {
        MsQuicChannel channel = MsQuicChannel.ForServer(endpoint, Config, streamHandler, streamOptionsArray,
                streamAttrsArray);
        Quic.SetupChannel(channel, optionsArray, attrsArray, handler, LOGGER);
        QuicSslEngine engine = sslEngineProvider(channel);
        if (!(engine is MsQuicSslEngine quicSslEngine))
        {
            channel.ForceClose();
            throw new ArgumentException("QuicSslEngine is not of type " + nameof(MsQuicSslEngine));
        }

        if (engine.GetUseClientMode())
        {
            channel.ForceClose();
            throw new ArgumentException("QuicSslEngine is not created in server mode");
        }

        return (channel, quicSslEngine);
    }

    // QuicListenerOptions.ConnectionOptionsCallback (an msquic thread).
    private QuicServerConnectionOptions NewConnectionOptions(QuicEndpointChannel endpoint, QuicConnection connection,
                                                             SslClientHelloInfo clientHello)
    {
        (MsQuicChannel Channel, MsQuicSslEngine Engine) next;

        lock (this)
        {
            next = nextChannel ?? NewChannel(endpoint);
            nextChannel = null;
        }

        MsQuicSslEngine engine = next.Engine;
        Func<string, QuicSslContext> mapping = engine.Context.Mapping;
        if (mapping != null && !string.IsNullOrEmpty(clientHello.ServerName) &&
            mapping(clientHello.ServerName) is MsQuicSslContext selected && !ReferenceEquals(selected, engine.Context))
        {
            // The SNI callback selects the QuicSslContext of the server name.
            engine = (MsQuicSslEngine) selected.NewEngine(next.Channel.Alloc());
        }

        QuicServerConnectionOptions options = next.Channel.AttachServerConnection(connection, engine);
        connecting.AddOrUpdate(connection, next.Channel);
        return options;
    }

    internal override void BeginRead(QuicEndpointChannel endpoint)
    {
        readPending = true;

        if (listener != null && !acceptInFlight)
        {
            acceptInFlight = true;
            _ = AcceptAsync(endpoint, listener);
        }
    }

    private async Task AcceptAsync(QuicEndpointChannel endpoint, QuicListener quicListener)
    {
        QuicConnection connection = null;
        Exception error = null;

        try
        {
            connection = await quicListener.AcceptConnectionAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            error = e;
        }

        try
        {
            endpoint.EventLoop.Execute(() => Accepted(endpoint, connection, error));
        }
        catch (Exception)
        {
            if (connection != null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void Accepted(QuicEndpointChannel endpoint, QuicConnection connection, Exception error)
    {
        acceptInFlight = false;

        if (connection == null)
        {
            if (error is ObjectDisposedException || !endpoint.IsOpen || listener == null)
            {
                return;
            }

            // A connection whose handshake failed: the listener drops it.
            LOGGER.Debug("A QUIC connection failed before it was accepted on {}", endpoint, error);
        }
        else if (!connecting.TryGetValue(connection, out MsQuicChannel channel))
        {
            _ = connection.DisposeAsync();
        }
        else
        {
            connecting.Remove(connection);
            channel.Accepted();
            readPending = false;
            endpoint.Pipeline.FireChannelRead(channel);
            endpoint.Pipeline.FireChannelReadComplete();
        }

        if (readPending && endpoint.IsOpen)
        {
            BeginRead(endpoint);
        }
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is MsQuicChannel channel && ReferenceEquals(channel.Parent, ctx.Channel))
        {
            AddChannel(channel);
            Task registration = ctx.Channel.EventLoop.RegisterAsync(channel);
            if (registration.IsCompleted)
            {
                Registered(registration, channel);
            }
            else
            {
                _ = registration.ContinueWith(t => channel.EventLoop.Execute(() => Registered(t, channel)),
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            return;
        }

        ctx.FireChannelRead(msg);
    }

    private static void Registered(Task registration, MsQuicChannel channel)
    {
        if (registration.IsCompletedSuccessfully)
        {
            channel.ServerRegistered();
        }
        else
        {
            LOGGER.Warn("Failed to register {}", channel, registration.Exception?.InnerException);
            channel.ForceClose();
        }
    }

    internal override void EndpointClosed(QuicEndpointChannel endpoint)
    {
        QuicListener quicListener = listener;
        listener = null;

        lock (this)
        {
            nextChannel?.Channel.ForceClose();
            nextChannel = null;
        }

        if (quicListener != null)
        {
            _ = DisposeListenerAsync(quicListener);
        }

        base.EndpointClosed(endpoint);
    }

    private static async Task DisposeListenerAsync(QuicListener quicListener)
    {
        try
        {
            await quicListener.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LOGGER.Debug("Failed to dispose {}", quicListener, e);
        }
    }

    protected override Task ConnectQuicChannel(MsQuicChannel channel, EndPoint remoteAddress, EndPoint localAddress)
    {
        return Task.FromException(new NotSupportedException());
    }
}
