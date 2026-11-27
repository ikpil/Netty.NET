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
using System.Net.Quic;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the IQuicChannel of the msquic implementation, the counterpart of the JNI unit QuicheQuicChannel
// (jvm-only-replaced), over one System.Net.Quic QuicConnection. Its parent is the QuicEndpointChannel that took the
// DatagramChannel's place (docs/codec-classes-quic-porting.md, Stage 2) and it runs on the parent's event loop (the only
// compatible one). It keeps the semantics of QuicheQuicChannel:
//   - a server channel is created by MsQuicServerCodec when msquic asks for the options of a new connection
//     (QuicListenerOptions.ConnectionOptionsCallback, where the handler, options and attributes of the builder are applied
//     and the sslEngineProvider returns its QuicSslEngine) and is registered with channelActive once the listener
//     accepted the connection (handshake done); a client channel is created by QuicChannelBootstrap, registered, and
//     connected through its parent's pipeline (connect(MsQuicChannelAddress), which MsQuicClientCodec turns into
//     QuicConnection.ConnectAsync), with the connect timeout, cancellation, ConnectionPendingException
//     (SocketError.AlreadyInProgress) and AlreadyConnectedException (SocketError.IsConnected) of QuicChannelUnsafe;
//     channelActive and SslHandshakeCompletionEvent.SUCCESS follow the handshake, a failed handshake fires
//     SslHandshakeCompletionEvent with the AuthenticationException (SSLHandshakeException) and fails the connect;
//   - inbound streams are accepted as soon as the connection is established (AcceptInboundStreamAsync also reports the
//     end of the connection) and delivered as channelRead(IQuicStreamChannel) when a read() is pending (all accepted
//     streams per read, then channelReadComplete); the pipeline sets up and registers a stream that reaches its tail
//     with the stream handler, options and attributes (onUnhandledInboundMessage);
//   - CreateStreamAsync opens an outbound stream (OpenOutboundStreamAsync), adds the handler and registers it; it fails
//     with QuicException STREAM_LIMIT_ERROR when the peer allows no stream of the type;
//   - PeerAllowedStreams counts the stream capacity that msquic reports (QuicConnectionOptions.StreamCapacityCallback,
//     minus the streams opened here); QuicStreamLimitChangedEvent fires once the connection is established (the
//     peer's initial limits, which msquic reports before ConnectAsync completes) and on every later increase.
//     System.Net.Quic does not call the callback of a server connection for the client's initial limits (measured with
//     .NET 10.0.11), so a server channel's PeerAllowedStreams stays 0 and its CreateStreamAsync waits for stream
//     capacity (OpenOutboundStreamAsync) instead of failing with STREAM_LIMIT_ERROR;
//   - the end of the connection closes the channel: a peer's CONNECTION_CLOSE (QuicError.ConnectionAborted) fires the
//     QuicConnectionCloseEvent with its application (or transport) error code, an idle timeout (QuicError
//     .ConnectionIdle, ConnectionTimeout) sets IsTimedOut(), and the open streams close with
//     QuicTimeoutClosedChannelException or ClosedChannelException, as doClose() and closeStreams() do;
//   - close() closes the connection with QuicConnection.CloseAsync(error) and disposes it; CloseAsync(applicationClose,
//     error, reason) chooses the error code. System.Net.Quic sends application CONNECTION_CLOSE frames only and no
//     reason phrase, so the reason is released unused and a transport close with an error code reports
//     NotSupportedException (a transport close with error 0 is the plain close).
// System.Net.Quic hides the connection IDs, so LocalAddress/RemoteAddress are QuicConnectionAddress values that identify
// the connection in this process (the ones given to QuicChannelBootstrap, otherwise random ones of Quic.MAX_CONN_ID_LEN
// bytes), not the IDs on the wire; they are null before the connection is established. The datagram extension
// (writes on this channel), statistics (CollectStatsAsync, CollectPathStatsAsync), the peer's transport parameters,
// QLOG and path events have no counterpart and report NotSupportedException where they are used.
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class MsQuicChannel : AbstractChannel, IQuicChannel
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(MsQuicChannel));
    private static readonly ChannelMetadata METADATA = new ChannelMetadata(false, 16);

    private enum ChannelState
    {
        OPEN,
        ACTIVE,
        CLOSED
    }

    private readonly bool server;
    private readonly IChannelHandler streamHandler;
    private readonly KeyValuePair<ChannelOption, object>[] streamOptionsArray;
    private readonly KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] streamAttrsArray;
    private readonly Dictionary<long, MsQuicStreamChannel> streams = new Dictionary<long, MsQuicStreamChannel>();
    private readonly Queue<MsQuicStreamChannel> acceptedStreams = new Queue<MsQuicStreamChannel>();
    private readonly CancellationTokenSource closeCts = new CancellationTokenSource();

    private volatile MsQuicConfig config;
    private volatile QuicConnection connection;
    private volatile MsQuicSslEngine engine;
    private volatile EndPoint local;
    private volatile EndPoint remote;
    private volatile QuicConnectionAddress localConnectionAddress;
    private volatile QuicConnectionAddress remoteConnectionAddress;
    private volatile ChannelState state = ChannelState.OPEN;
    private volatile bool timedOut;
    private volatile bool streamCapacityKnown;
    private long bidiStreamsLeft;
    private long uniStreamsLeft;
    private long streamCapacityChanges;

    // Event loop state.
    private long notifiedStreamCapacityChanges;
    private bool readPending;
    private bool handshakeCompletionNotified;
    private QuicConnectionCloseEvent connectionCloseEvent;
    private TaskCompletionSource connectPromise;
    private CancellationTokenSource connectTimeoutFuture;
    private QuicConnectionAddress connectLocalAddress;
    private QuicConnectionAddress connectRemoteAddress;
    private bool hasCloseData;
    private long closeError;

    private MsQuicChannel(IChannel parent, bool server, EndPoint local, EndPoint remote, IChannelHandler streamHandler,
                          KeyValuePair<ChannelOption, object>[] streamOptionsArray,
                          KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] streamAttrsArray)
        : base(parent, METADATA)
    {
        this.server = server;
        this.local = local;
        this.remote = remote;
        this.streamHandler = streamHandler;
        this.streamOptionsArray = streamOptionsArray;
        this.streamAttrsArray = streamAttrsArray;
    }

    internal static MsQuicChannel ForClient(IChannel parent, EndPoint local, EndPoint remote,
                                            IChannelHandler streamHandler,
                                            KeyValuePair<ChannelOption, object>[] streamOptionsArray,
                                            KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[]
                                                    streamAttrsArray)
    {
        return new MsQuicChannel(parent, false, local, remote, streamHandler, streamOptionsArray, streamAttrsArray);
    }

    internal static MsQuicChannel ForServer(IChannel parent, MsQuicConfig config, IChannelHandler streamHandler,
                                            KeyValuePair<ChannelOption, object>[] streamOptionsArray,
                                            KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[]
                                                    streamAttrsArray)
    {
        MsQuicChannel channel = new MsQuicChannel(parent, true, null, null, streamHandler, streamOptionsArray,
                streamAttrsArray);
        channel.config = config;
        return channel;
    }

    protected override IChannelConfig NewConfig() => new MsQuicChannelConfig(this);

    protected override DefaultChannelPipeline NewChannelPipeline() => new MsQuicChannelPipeline(this);

    // The pipeline that sets up and registers a QuicStreamChannel that reaches its tail (onUnhandledInboundMessage).
    private sealed class MsQuicChannelPipeline : DefaultChannelPipeline
    {
        private readonly MsQuicChannel channel;

        internal MsQuicChannelPipeline(MsQuicChannel channel) : base(channel)
        {
            this.channel = channel;
        }

        protected override void OnUnhandledInboundMessage(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IQuicStreamChannel streamChannel)
            {
                Quic.SetupChannel(streamChannel, channel.streamOptionsArray, channel.streamAttrsArray,
                        channel.streamHandler, logger);
                Task registration = ctx.Channel.EventLoop.RegisterAsync(streamChannel);
                if (!registration.IsCompleted || registration.IsFaulted)
                {
                    _ = registration.ContinueWith(static (t, state) =>
                            logger.Warn("Failed to register {}", state, t.Exception?.InnerException),
                            streamChannel, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted,
                            TaskScheduler.Default);
                }
            }
            else
            {
                base.OnUnhandledInboundMessage(ctx, msg);
            }
        }
    }

    protected override bool IsCompatible(IEventLoop eventLoop)
    {
        return ReferenceEquals(Parent.EventLoop, eventLoop);
    }

    internal bool IsServer => server;

    internal bool IsStreamLocalCreated(long streamId)
    {
        return (streamId & 0x1) == (server ? 1 : 0);
    }

    internal static QuicStreamType StreamType(long streamId)
    {
        return (streamId & 0x2) == 0 ? QuicStreamType.BIDIRECTIONAL : QuicStreamType.UNIDIRECTIONAL;
    }

    public QuicSslEngine SslEngine() => engine;

    public long PeerAllowedStreams(QuicStreamType type)
    {
        switch (type)
        {
            case QuicStreamType.BIDIRECTIONAL:
                return Math.Max(0, Interlocked.Read(ref bidiStreamsLeft));
            case QuicStreamType.UNIDIRECTIONAL:
                return Math.Max(0, Interlocked.Read(ref uniStreamsLeft));
            default:
                return 0;
        }
    }

    public bool IsTimedOut() => timedOut;

    public IQuicTransportParameters PeerTransportParameters()
    {
        throw new NotSupportedException("System.Net.Quic does not report the peer's transport parameters");
    }

    // Override so we never cache as the connection addresses are only known once the connection is established.
    public override EndPoint LocalAddress => LocalAddress0();

    public override EndPoint RemoteAddress => RemoteAddress0();

    protected override EndPoint LocalAddress0() => localConnectionAddress;

    protected override EndPoint RemoteAddress0() => remoteConnectionAddress;

    public EndPoint LocalSocketAddress() => local;

    public EndPoint RemoteSocketAddress() => remote;

    public Task<IQuicConnectionStats> CollectStatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromException<IQuicConnectionStats>(
                new NotSupportedException("System.Net.Quic does not report connection statistics"));
    }

    public Task<IQuicConnectionPathStats> CollectPathStatsAsync(int pathIdx,
                                                                CancellationToken cancellationToken = default)
    {
        return Task.FromException<IQuicConnectionPathStats>(
                new NotSupportedException("System.Net.Quic does not report path statistics"));
    }

    public Task<IQuicStreamChannel> CreateStreamAsync(QuicStreamType type, IChannelHandler handler,
                                                      CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<IQuicStreamChannel> promise =
                new TaskCompletionSource<IQuicStreamChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            ExecuteOnLoop(() => ConnectStream(type, handler, promise));
        }
        catch (Exception e)
        {
            promise.TrySetException(e);
        }
        return promise.Task;
    }

    private void ConnectStream(QuicStreamType type, IChannelHandler handler,
                               TaskCompletionSource<IQuicStreamChannel> promise)
    {
        QuicConnection conn = connection;
        if (conn == null || state == ChannelState.CLOSED)
        {
            promise.TrySetException(new ClosedChannelException());
            return;
        }
        if (streamCapacityKnown && PeerAllowedStreams(type) <= 0)
        {
            promise.TrySetException(new QuicException("STREAM_LIMIT_ERROR: QUICHE_ERR_STREAM_LIMIT",
                    QuicTransportError.STREAM_LIMIT_ERROR));
            return;
        }
        if (type == QuicStreamType.UNIDIRECTIONAL)
        {
            Interlocked.Decrement(ref uniStreamsLeft);
        }
        else
        {
            Interlocked.Decrement(ref bidiStreamsLeft);
        }
        _ = OpenStreamAsync(conn, type, handler, promise);
    }

    private async Task OpenStreamAsync(QuicConnection conn, QuicStreamType type, IChannelHandler handler,
                                       TaskCompletionSource<IQuicStreamChannel> promise)
    {
        QuicStream stream;
        try
        {
            stream = await conn.OpenOutboundStreamAsync(type == QuicStreamType.BIDIRECTIONAL
                    ? System.Net.Quic.QuicStreamType.Bidirectional
                    : System.Net.Quic.QuicStreamType.Unidirectional, closeCts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (!TryExecute(() =>
                {
                    promise.TrySetException(state == ChannelState.CLOSED || IsSilentError(e)
                            ? new ClosedChannelException()
                            : ToQuicException(e));
                    ConnectionError(e);
                }))
            {
                promise.TrySetException(e);
            }
            return;
        }
        if (!TryExecute(() => StreamOpened(stream, handler, promise)))
        {
            promise.TrySetException(new ClosedChannelException());
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void StreamOpened(QuicStream stream, IChannelHandler handler,
                              TaskCompletionSource<IQuicStreamChannel> promise)
    {
        if (state == ChannelState.CLOSED)
        {
            promise.TrySetException(new ClosedChannelException());
            _ = DisposeAsync(stream);
            return;
        }
        MsQuicStreamChannel streamChannel = new MsQuicStreamChannel(this, stream);
        long streamId = streamChannel.StreamId();
        streams[streamId] = streamChannel;
        if (handler != null)
        {
            streamChannel.Pipeline.AddLast(handler);
        }
        Task registration = EventLoop.RegisterAsync(streamChannel);
        if (registration.IsCompleted)
        {
            StreamRegistered(registration, streamChannel, streamId, promise);
        }
        else
        {
            _ = registration.ContinueWith(t => ExecuteOnLoop(() =>
                            StreamRegistered(t, streamChannel, streamId, promise)), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private void StreamRegistered(Task registration, MsQuicStreamChannel streamChannel, long streamId,
                                  TaskCompletionSource<IQuicStreamChannel> promise)
    {
        if (registration.IsCompletedSuccessfully)
        {
            promise.TrySetResult(streamChannel);
        }
        else
        {
            promise.TrySetException(registration.Exception?.InnerException ?? new ClosedChannelException());
            streams.Remove(streamId);
        }
    }

    internal void StreamClosed(MsQuicStreamChannel streamChannel)
    {
        long streamId = streamChannel.StreamId();
        if (streams.TryGetValue(streamId, out MsQuicStreamChannel existing) && ReferenceEquals(existing, streamChannel))
        {
            streams.Remove(streamId);
        }
    }

    private void CloseStreams()
    {
        acceptedStreams.Clear();
        if (streams.Count == 0)
        {
            return;
        }
        ClosedChannelException closedChannelException;
        if (IsTimedOut())
        {
            // Close the streams because of a timeout.
            closedChannelException = new QuicTimeoutClosedChannelException();
        }
        else
        {
            closedChannelException = new ClosedChannelException();
        }
        // Make a copy to ensure we not run into a situation when we change the underlying iterator from
        // another method and so run in an assert error.
        foreach (MsQuicStreamChannel stream in streams.Values.ToArray())
        {
            stream.CloseOnConnectionClose(closedChannelException);
        }
        streams.Clear();
    }

    public Task CloseAsync(bool applicationClose, int error, ByteBuf reason,
                           CancellationToken cancellationToken = default)
    {
        TaskCompletionSource promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            ExecuteOnLoop(() =>
            {
                // System.Net.Quic sends no reason phrase.
                reason.Release();
                if (!applicationClose && error != 0)
                {
                    promise.TrySetException(new NotSupportedException(
                            "System.Net.Quic closes a connection with an application error code only"));
                    return;
                }
                if (!hasCloseData)
                {
                    hasCloseData = true;
                    closeError = error;
                }
                Notify(Pipeline.CloseAsync(cancellationToken), promise);
            });
        }
        catch (Exception e)
        {
            promise.TrySetException(e);
        }
        return promise.Task;
    }

    // ---- connect (client) ----

    // QuicChannelUnsafe.connect: the client connects once, with QuicConnectionAddress arguments.
    protected override Task ConnectTransportAsync(EndPoint remoteAddress, EndPoint localAddress,
                                                  CancellationToken cancellationToken)
    {
        if (server)
        {
            return Task.FromException(new NotSupportedException());
        }
        if (connectPromise != null)
        {
            // ConnectionPendingException
            return Task.FromException(new SocketException((int) SocketError.AlreadyInProgress));
        }
        if (remoteAddress is QuicConnectionAddress remoteId)
        {
            if (connection != null || state != ChannelState.OPEN)
            {
                // If a connection is assigned we know this channel was already connected.
                // AlreadyConnectedException
                return Task.FromException(new SocketException((int) SocketError.IsConnected));
            }

            connectLocalAddress = localAddress as QuicConnectionAddress;
            connectRemoteAddress = remoteId;
            TaskCompletionSource promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            connectPromise = promise;

            // Schedule connect timeout.
            int connectTimeoutMillis = Config.GetConnectTimeoutMillis();
            if (connectTimeoutMillis > 0)
            {
                CancellationTokenSource timeout = new CancellationTokenSource();
                connectTimeoutFuture = timeout;
                EndPoint local = this.local;
                EndPoint remote = this.remote;
                _ = EventLoop.ScheduleAsync(() =>
                {
                    TaskCompletionSource pending = connectPromise;
                    if (pending != null && !pending.Task.IsCompleted
                            && pending.TrySetException(new ConnectTimeoutException(
                            "connection timed out: " + local + " -> " + remote)))
                    {
                        connectPromise = null;
                        _ = Close();
                    }
                }, TimeSpan.FromMilliseconds(connectTimeoutMillis), timeout.Token);
            }

            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration listener = cancellationToken.Register(() => ExecuteOnLoop(() =>
                {
                    if (promise.TrySetCanceled(cancellationToken))
                    {
                        connectTimeoutFuture?.Cancel();
                        connectPromise = null;
                        _ = Close();
                    }
                }));
                _ = promise.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration) state).Dispose(),
                        listener, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
            }

            Task parentConnect;
            try
            {
                parentConnect = Parent.ConnectAsync(new MsQuicChannelAddress(this));
            }
            catch (Exception e)
            {
                parentConnect = Task.FromException(e);
            }
            _ = parentConnect.ContinueWith(f =>
            {
                if (!f.IsCompletedSuccessfully)
                {
                    Exception cause = f.Exception?.InnerException ?? new OperationCanceledException();
                    ExecuteOnLoop(() =>
                    {
                        TaskCompletionSource pending = connectPromise;
                        if (pending != null)
                        {
                            connectPromise = null;
                            pending.TrySetException(cause);
                            // close everything after notify about failure.
                            CloseForcibly();
                        }
                    });
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return promise.Task;
        }

        return Task.FromException(new NotSupportedException());
    }

    // QuicheQuicChannel.connectNow: called by MsQuicClientCodec on the event loop.
    internal void ConnectNow(Func<IQuicChannel, QuicSslEngine> engineProvider, MsQuicConfig config)
    {
        QuicSslEngine quicSslEngine = engineProvider(this);
        if (!(quicSslEngine is MsQuicSslEngine msQuicSslEngine))
        {
            throw new ArgumentException("QuicSslEngine is not of type " + nameof(MsQuicSslEngine));
        }
        if (!msQuicSslEngine.GetUseClientMode())
        {
            throw new ArgumentException("QuicSslEngine is not created in client mode");
        }
        this.config = config;
        engine = msQuicSslEngine;
        QuicClientConnectionOptions options = new QuicClientConnectionOptions
        {
            RemoteEndPoint = remote,
            ClientAuthenticationOptions = msQuicSslEngine.ClientAuthenticationOptions(),
            StreamCapacityCallback = OnStreamCapacity,
        };
        if (local is IPEndPoint localEndPoint)
        {
            options.LocalEndPoint = localEndPoint;
        }
        config.Apply(options);
        _ = ConnectCoreAsync(options);
    }

    private async Task ConnectCoreAsync(QuicClientConnectionOptions options)
    {
        QuicConnection conn = null;
        Exception error = null;
        try
        {
            conn = await QuicConnection.ConnectAsync(options, closeCts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            error = e;
        }
        if (!TryExecute(() =>
            {
                if (conn != null)
                {
                    Connected(conn);
                }
                else
                {
                    ConnectFailed(error);
                }
            }) && conn != null)
        {
            _ = CloseConnectionAsync(conn, 0);
        }
    }

    private void Connected(QuicConnection conn)
    {
        TaskCompletionSource promise = connectPromise;
        if (state == ChannelState.CLOSED || promise == null)
        {
            _ = CloseConnectionAsync(conn, 0);
            return;
        }
        connectPromise = null;
        connectTimeoutFuture?.Cancel();
        connection = conn;
        local = conn.LocalEndPoint;
        remote = conn.RemoteEndPoint;
        engine.HandshakeCompleted(conn);
        QuicConnectionAddress localId = connectLocalAddress;
        QuicConnectionAddress remoteId = connectRemoteAddress;
        localConnectionAddress = localId == null || ReferenceEquals(localId, QuicConnectionAddress.EPHEMERAL)
                ? QuicConnectionAddress.Random() : localId;
        remoteConnectionAddress = remoteId == null || ReferenceEquals(remoteId, QuicConnectionAddress.EPHEMERAL)
                ? QuicConnectionAddress.Random() : remoteId;
        state = ChannelState.ACTIVE;
        SetActive(true);

        bool promiseSet = promise.TrySetResult();
        Pipeline.FireChannelActive();
        NotifyAboutHandshakeCompletionIfNeeded(null);
        NotifyStreamLimitChangedIfNeeded();
        StartAcceptLoop(conn);
        if (!promiseSet)
        {
            _ = Close();
        }
    }

    private void ConnectFailed(Exception error)
    {
        TaskCompletionSource promise = connectPromise;
        if (state == ChannelState.CLOSED || promise == null)
        {
            return;
        }
        connectPromise = null;
        connectTimeoutFuture?.Cancel();
        Exception cause = error is AuthenticationException || error is SocketException ||
                          error is OperationCanceledException
                ? error
                : ToQuicException(error);
        if (error is System.Net.Quic.QuicException quic)
        {
            FireConnectCloseEventIfNeeded(quic);
        }
        if (cause is AuthenticationException)
        {
            NotifyAboutHandshakeCompletionIfNeeded(cause);
        }
        promise.TrySetException(cause);
        _ = Close();
    }

    private void NotifyAboutHandshakeCompletionIfNeeded(Exception cause)
    {
        if (handshakeCompletionNotified)
        {
            return;
        }
        handshakeCompletionNotified = true;
        Pipeline.FireUserEventTriggered(cause == null
                ? SslHandshakeCompletionEvent.SUCCESS
                : new SslHandshakeCompletionEvent(cause));
    }

    // ---- server ----

    // Called by MsQuicServerCodec from QuicListenerOptions.ConnectionOptionsCallback (an msquic thread) before the
    // handshake; the channel is not registered yet.
    internal QuicServerConnectionOptions AttachServerConnection(QuicConnection conn, MsQuicSslEngine sslEngine)
    {
        connection = conn;
        engine = sslEngine;
        local = conn.LocalEndPoint;
        remote = conn.RemoteEndPoint;
        QuicServerConnectionOptions options = new QuicServerConnectionOptions
        {
            ServerAuthenticationOptions = sslEngine.ServerAuthenticationOptions(),
            StreamCapacityCallback = OnStreamCapacity,
        };
        config.Apply(options);
        return options;
    }

    // Called by MsQuicServerCodec once the listener accepted the connection (handshake done), before registration.
    internal void Accepted()
    {
        engine.HandshakeCompleted(connection);
        localConnectionAddress = QuicConnectionAddress.Random();
        remoteConnectionAddress = QuicConnectionAddress.Random();
        state = ChannelState.ACTIVE;
        SetActive(true);
    }

    // Called by MsQuicServerCodec on the event loop once the accepted channel is registered (channelActive fired).
    internal void ServerRegistered()
    {
        NotifyAboutHandshakeCompletionIfNeeded(null);
        NotifyStreamLimitChangedIfNeeded();
        StartAcceptLoop(connection);
    }

    // forceClose(): closes the channel on its event loop (with channelInactive), or closes an unregistered channel
    // without firing any events (closeForcibly()).
    internal void ForceClose()
    {
        if (IsRegistered)
        {
            ExecuteOnLoop(() => _ = Close());
        }
        else
        {
            CloseForcibly();
        }
    }

    // ---- inbound streams and the end of the connection ----

    private void OnStreamCapacity(QuicConnection conn, QuicStreamCapacityChangedArgs args)
    {
        Interlocked.Add(ref bidiStreamsLeft, args.BidirectionalIncrement);
        Interlocked.Add(ref uniStreamsLeft, args.UnidirectionalIncrement);
        if (args.BidirectionalIncrement != 0 || args.UnidirectionalIncrement != 0)
        {
            Interlocked.Increment(ref streamCapacityChanges);
        }
        streamCapacityKnown = true;
        if (state == ChannelState.ACTIVE && IsRegistered)
        {
            TryExecute(NotifyStreamLimitChangedIfNeeded);
        }
    }

    // processReceived(): once the connection is established, a change of the peer's stream limits fires
    // QuicStreamLimitChangedEvent. Java compares the streams left before and after it fetched them (both 0 before the
    // connection is established, so the first event reports the peer's initial limits); msquic reports the initial
    // limits through StreamCapacityCallback before ConnectAsync completes, so the changes reported until now are
    // counted and the event fires once the channel is active (Connected, ServerRegistered) and on every later change.
    private void NotifyStreamLimitChangedIfNeeded()
    {
        long changes = Interlocked.Read(ref streamCapacityChanges);
        if (state != ChannelState.ACTIVE || changes == notifiedStreamCapacityChanges)
        {
            return;
        }
        notifiedStreamCapacityChanges = changes;
        Pipeline.FireUserEventTriggered(QuicStreamLimitChangedEvent.INSTANCE);
    }

    private void StartAcceptLoop(QuicConnection conn)
    {
        MsQuicConfig config = this.config;
        if (conn == null || config == null || !config.AcceptsInboundStreams)
        {
            // System.Net.Quic refuses AcceptInboundStreamAsync when no inbound stream is allowed; the end of the
            // connection is then seen by the stream operations (ConnectionError).
            return;
        }
        _ = AcceptLoopAsync(conn);
    }

    private async Task AcceptLoopAsync(QuicConnection conn)
    {
        CancellationToken token = closeCts.Token;
        try
        {
            for (;;)
            {
                QuicStream stream = await conn.AcceptInboundStreamAsync(token).ConfigureAwait(false);
                if (!TryExecute(() => InboundStreamAccepted(stream)))
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (Exception e)
        {
            TryExecute(() => ConnectionError(e));
        }
    }

    private void InboundStreamAccepted(QuicStream stream)
    {
        if (state == ChannelState.CLOSED)
        {
            _ = DisposeAsync(stream);
            return;
        }
        MsQuicStreamChannel streamChannel = new MsQuicStreamChannel(this, stream);
        streams[streamChannel.StreamId()] = streamChannel;
        acceptedStreams.Enqueue(streamChannel);
        if (readPending)
        {
            DeliverAcceptedStreams();
        }
    }

    // recvStream(): for streams we always process all streams when at least on read was requested.
    private void DeliverAcceptedStreams()
    {
        if (acceptedStreams.Count == 0)
        {
            return;
        }
        while (acceptedStreams.Count != 0 && state != ChannelState.CLOSED)
        {
            readPending = false;
            Pipeline.FireChannelRead(acceptedStreams.Dequeue());
        }
        Pipeline.FireChannelReadComplete();
    }

    protected override void BeginRead()
    {
        if (!IsOpen)
        {
            return;
        }
        DoBeginRead();
    }

    protected override void DoBeginRead()
    {
        readPending = true;
        if (state == ChannelState.ACTIVE)
        {
            DeliverAcceptedStreams();
        }
    }

    // The connection ended (or a stream operation failed because it did): a peer's CONNECTION_CLOSE fires the
    // QuicConnectionCloseEvent, an idle timeout marks the channel timed out, and the channel closes.
    internal void ConnectionError(Exception error)
    {
        if (state == ChannelState.CLOSED || !(error is System.Net.Quic.QuicException quic) || !IsConnectionError(quic))
        {
            return;
        }
        if (quic.QuicError == QuicError.ConnectionIdle || quic.QuicError == QuicError.ConnectionTimeout)
        {
            timedOut = true;
        }
        FireConnectCloseEventIfNeeded(quic);
        _ = Close();
    }

    private void FireConnectCloseEventIfNeeded(System.Net.Quic.QuicException quic)
    {
        if (connectionCloseEvent != null || quic.QuicError != QuicError.ConnectionAborted)
        {
            return;
        }
        connectionCloseEvent = quic.ApplicationErrorCode is long applicationError
                ? new QuicConnectionCloseEvent(true, (int) applicationError, EmptyArrays.EMPTY_BYTES)
                : new QuicConnectionCloseEvent(false, (int) (quic.TransportErrorCode ?? 0), EmptyArrays.EMPTY_BYTES);
        if (IsRegistered)
        {
            Pipeline.FireUserEventTriggered(connectionCloseEvent);
        }
    }

    // ---- channel operations ----

    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    {
        return Task.FromException(new NotSupportedException());
    }

    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        return Task.FromException(new NotSupportedException());
    }

    protected override void DoClose()
    {
        if (state == ChannelState.CLOSED)
        {
            return;
        }
        state = ChannelState.CLOSED;
        connectTimeoutFuture?.Cancel();
        try
        {
            closeCts.Cancel();
        }
        catch (Exception e)
        {
            logger.Debug("Failed to cancel the pending operations of {}", this, e);
        }

        QuicConnection conn = connection;
        long error = hasCloseData ? closeError : 0;
        FailPendingConnectPromise();
        try
        {
            if (conn != null)
            {
                _ = CloseConnectionAsync(conn, error);
            }
        }
        finally
        {
            CloseStreams();
            local = null;
            remote = null;
        }
    }

    private void FailPendingConnectPromise()
    {
        TaskCompletionSource promise = connectPromise;
        if (promise != null)
        {
            connectPromise = null;
            promise.TrySetException(new QuicClosedChannelException(connectionCloseEvent));
        }
    }

    private static async Task CloseConnectionAsync(QuicConnection conn, long error)
    {
        try
        {
            await conn.CloseAsync(error).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.Debug("Failed to close {}", conn, e);
        }
        finally
        {
            try
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.Debug("Failed to dispose {}", conn, e);
            }
        }
    }

    private static async Task DisposeAsync(QuicStream stream)
    {
        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.Debug("Failed to dispose {}", stream, e);
        }
    }

    protected override object FilterOutboundMessage(object msg)
    {
        if (msg is ByteBuf)
        {
            return msg;
        }
        throw new NotSupportedException("Unsupported message type: " + StringUtil.SimpleClassName(msg));
    }

    protected override Task DoWrite(object msg)
    {
        return Task.FromException(new NotSupportedException("Datagram extension is not supported"));
    }

    protected override void HandleWriteError(Exception t)
    {
        OutboundBuffer.Remove(t);
    }

    private bool TryExecute(Action action)
    {
        try
        {
            EventLoop.Execute(action);
            return true;
        }
        catch (RejectedExecutionException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // Not registered.
            return false;
        }
    }

    // ---- System.Net.Quic exceptions ----

    // The QuicError values that end the connection.
    internal static bool IsConnectionError(System.Net.Quic.QuicException quic)
    {
        switch (quic.QuicError)
        {
            case QuicError.ConnectionAborted:
            case QuicError.ConnectionIdle:
            case QuicError.ConnectionTimeout:
            case QuicError.ConnectionRefused:
            case QuicError.VersionNegotiationError:
            case QuicError.TransportError:
                return true;
            default:
                return false;
        }
    }

    private static bool IsSilentError(Exception error)
    {
        return error is ObjectDisposedException || error is OperationCanceledException ||
               error is System.Net.Quic.QuicException quic && quic.QuicError == QuicError.OperationAborted;
    }

    // Quiche.convertToException for System.Net.Quic: a QuicException with the QuicTransportError of the peer's
    // transport error code (APPLICATION_ERROR for an application close, INTERNAL_ERROR otherwise).
    internal static Exception ToQuicException(Exception error)
    {
        if (!(error is System.Net.Quic.QuicException quic))
        {
            return error;
        }
        QuicTransportError transportError = QuicTransportError.INTERNAL_ERROR;
        if (quic.TransportErrorCode is long code)
        {
            try
            {
                transportError = QuicTransportError.ValueOf(code);
            }
            catch (ArgumentException)
            {
                // Unknown code: keep INTERNAL_ERROR.
            }
        }
        else if (quic.ApplicationErrorCode != null)
        {
            transportError = QuicTransportError.APPLICATION_ERROR;
        }
        return new QuicException(quic.Message, quic, transportError);
    }

    // Completes the promise with the outcome of the task.
    internal static void Notify(Task task, TaskCompletionSource promise)
    {
        if (task.IsCompleted)
        {
            Complete(task, promise);
            return;
        }
        _ = task.ContinueWith(static (t, state) => Complete(t, (TaskCompletionSource) state), promise,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static void Complete(Task task, TaskCompletionSource promise)
    {
        if (task.IsCompletedSuccessfully)
        {
            promise.TrySetResult();
        }
        else if (task.IsCanceled)
        {
            promise.TrySetCanceled();
        }
        else
        {
            promise.TrySetException(task.Exception?.InnerException ?? new InvalidOperationException());
        }
    }

    public override string ToString()
    {
        return "()" + base.ToString();
    }
}
