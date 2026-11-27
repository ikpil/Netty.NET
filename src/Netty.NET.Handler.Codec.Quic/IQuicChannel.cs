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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * A QUIC {@link Channel}.
 */
// CLR: Channel is IChannel; a returned Task replaces ChannelFuture/Future and the ChannelPromise/Promise overloads fold
// into the Task-returning members with a CancellationToken, as IChannelOutboundInvoker does (docs/transport-porting.md,
// Decisions). The default bind/connect/disconnect/close/deregister/write/writeAndFlush that delegate to the pipeline are
// default implementations of the IChannelOutboundInvoker members (a class's own implementation, such as
// AbstractChannel's, takes precedence, as a Java class method does over a default method); newPromise(),
// newProgressivePromise(), newSucceededFuture(), newFailedFuture(Throwable) and voidPromise() have no Task counterpart
// (docs/transport-porting.md: "newPromise()-style defaults have no Task counterpart"). read(), flush(), config(),
// localAddress() and remoteAddress() are re-declared in Java only to narrow their return types (QuicChannel,
// QuicChannelConfig, QuicConnectionAddress); they are inherited from IChannel unchanged, as a C# interface cannot narrow
// an inherited member's return type without hiding it (the ISocketChannel rule), so callers cast Config to
// IQuicChannelConfig and LocalAddress/RemoteAddress to QuicConnectionAddress. SSLEngine is QuicSslEngine (see that
// class). SocketAddress is EndPoint, ChannelHandler IChannelHandler; @Nullable has no counterpart (nullable reference
// types are not enabled in this port). The static newBootstrap(Channel) returns a QuicChannelBootstrap, which belongs to
// the msquic-backed implementation (stage 2, docs/codec-classes-quic-porting.md), so it was added with that class.
public interface IQuicChannel : IChannel
{

    Task IChannelOutboundInvoker.BindAsync(EndPoint localAddress, CancellationToken cancellationToken) =>
        Pipeline.BindAsync(localAddress, cancellationToken);

    Task IChannelOutboundInvoker.ConnectAsync(EndPoint remoteAddress, EndPoint localAddress,
                                              CancellationToken cancellationToken) =>
        Pipeline.ConnectAsync(remoteAddress, localAddress, cancellationToken);

    Task IChannelOutboundInvoker.DisconnectAsync(CancellationToken cancellationToken) =>
        Pipeline.DisconnectAsync(cancellationToken);

    Task IChannelOutboundInvoker.CloseAsync(CancellationToken cancellationToken) =>
        Pipeline.CloseAsync(cancellationToken);

    Task IChannelOutboundInvoker.DeregisterAsync(CancellationToken cancellationToken) =>
        Pipeline.DeregisterAsync(cancellationToken);

    Task IChannelOutboundInvoker.WriteAsync(object msg, CancellationToken cancellationToken) =>
        Pipeline.WriteAsync(msg, cancellationToken);

    Task IChannelOutboundInvoker.WriteAndFlushAsync(object msg, CancellationToken cancellationToken) =>
        Pipeline.WriteAndFlushAsync(msg, cancellationToken);

    /**
     * Returns the configuration of this channel.
     */
    // CLR: config() is IChannel.Config (see the type comment); its value is the channel's IQuicChannelConfig.

    /**
     * Returns the used {@link SSLEngine} or {@code null} if none is used (yet).
     *
     * @return the engine.
     */
    QuicSslEngine SslEngine();

    /**
     * Returns the number of streams that can be created before stream creation will fail
     * with {@link QuicTransportError#STREAM_LIMIT_ERROR} error.
     *
     * @param type the stream type.
     * @return the number of streams left.
     */
    long PeerAllowedStreams(QuicStreamType type);

    /**
     * Returns {@code true} if the connection was closed because of idle timeout.
     *
     * @return {@code true} if the connection was closed because of idle timeout, {@code false}.
     */
    bool IsTimedOut();

    /**
     * Returns the {@link QuicTransportParameters} of the peer once received, or {@code null} if not known yet.
     *
     * @return peerTransportParams.
     */
    IQuicTransportParameters PeerTransportParameters();

    /**
     * Returns the local {@link QuicConnectionAddress}. This address might change over the life-time of the
     * channel.
     *
     * @return  local   the local {@link QuicConnectionAddress} or {@code null} if none is assigned yet,
     *                  or assigned anymore.
     */
    // CLR: localAddress() is IChannel.LocalAddress (see the type comment); its value is a QuicConnectionAddress or null.

    /**
     * Returns the remote {@link QuicConnectionAddress}. This address might change over the life-time of the
     * channel.
     *
     * @return  remote   the remote {@link QuicConnectionAddress} or {@code null} if none is assigned yet,
     *                   or assigned anymore.
     */
    // CLR: remoteAddress() is IChannel.RemoteAddress (see the type comment); its value is a QuicConnectionAddress or
    // null.

    /**
     * Returns the local {@link SocketAddress} of the underlying transport that received the data.
     * This address might change over the life-time of the channel.
     *
     * @return  local   the local {@link SocketAddress} of the underlying transport or {@code null} if none is assigned
     *                  yet, or assigned anymore.
     */
    EndPoint LocalSocketAddress();

    /**
     * Returns the remote {@link SocketAddress} of the underlying transport to which the data is sent.
     * This address might change over the life-time of the channel.
     *
     * @return  local   the remote {@link SocketAddress} of the underlying transport or {@code null} if none is assigned
     *                  yet, or assigned anymore.
     */
    EndPoint RemoteSocketAddress();

    /**
     * Creates a stream that is using this {@link QuicChannel} and notifies the {@link Future} once done.
     * The {@link ChannelHandler} (if not {@code null}) is added to the {@link io.netty.channel.ChannelPipeline} of the
     * {@link QuicStreamChannel} automatically.
     *
     * @param type      the {@link QuicStreamType} of the {@link QuicStreamChannel}.
     * @param handler   the {@link ChannelHandler} that will be added to the {@link QuicStreamChannel}s
     *                  {@link io.netty.channel.ChannelPipeline} during the stream creation.
     * @return          the {@link Future} that will be notified once the operation completes.
     */
    /**
     * Creates a stream that is using this {@link QuicChannel} and notifies the {@link Promise} once done.
     * The {@link ChannelHandler} (if not {@code null}) is added to the {@link io.netty.channel.ChannelPipeline} of the
     * {@link QuicStreamChannel} automatically.
     *
     * @param type      the {@link QuicStreamType} of the {@link QuicStreamChannel}.
     * @param handler   the {@link ChannelHandler} that will be added to the {@link QuicStreamChannel}s
     *                  {@link io.netty.channel.ChannelPipeline} during the stream creation.
     * @param promise   the {@link ChannelPromise} that will be notified once the operation completes.
     * @return          the {@link Future} that will be notified once the operation completes.
     */
    // CLR: createStream(type, handler) used eventLoop().newPromise(); both overloads are this Task<IQuicStreamChannel>.
    Task<IQuicStreamChannel> CreateStreamAsync(QuicStreamType type, IChannelHandler handler,
                                               CancellationToken cancellationToken = default);

    /**
     * Returns a new {@link QuicStreamChannelBootstrap} which makes it easy to bootstrap new {@link QuicStreamChannel}s
     * with custom options and attributes. For simpler use-cases you may want to consider using
     * {@link #createStream(QuicStreamType, ChannelHandler)} or
     * {@link #createStream(QuicStreamType, ChannelHandler, Promise)} directly.
     *
     * @return {@link QuicStreamChannelBootstrap} that can be used to bootstrap a {@link QuicStreamChannel}.
     */
    QuicStreamChannelBootstrap NewStreamBootstrap()
    {
        return new QuicStreamChannelBootstrap(this);
    }

    /**
     * Close the {@link QuicChannel}
     *
     * @param applicationClose  {@code true} if an application close should be used,
     *                          {@code false} if a normal close should be used.
     * @param error             the application error number, or {@code 0} if no special error should be signaled.
     * @param reason            the reason for the closure (which may be an empty {@link ByteBuf}.
     * @return                  the future that is notified.
     */
    /**
     * Close the {@link QuicChannel}
     *
     * @param applicationClose  {@code true} if an application close should be used,
     *                          {@code false} if a normal close should be used.
     * @param error             the application error number, or {@code 0} if no special error should be signaled.
     * @param reason            the reason for the closure (which may be an empty {@link ByteBuf}.
     * @param promise           the {@link ChannelPromise} that will be notified.
     * @return                  the future that is notified.
     */
    // CLR: close(applicationClose, error, reason) used newPromise(); both overloads are this Task.
    Task CloseAsync(bool applicationClose, int error, ByteBuf reason, CancellationToken cancellationToken = default);

    /**
     * Collects statistics about the connection and notifies the {@link Future} once done.
     *
     * @return the {@link Future} that is notified once the stats were collected.
     */
    /**
     * Collects statistics about the connection and notifies the {@link Promise} once done.
     *
     * @param   promise the {@link ChannelPromise} that is notified once the stats were collected.
     * @return          the {@link Future} that is notified once the stats were collected.
     */
    // CLR: collectStats() used eventLoop().newPromise(); both overloads are this Task<IQuicConnectionStats>.
    Task<IQuicConnectionStats> CollectStatsAsync(CancellationToken cancellationToken = default);

    /**
     * Collects statistics about the path of the connection and notifies the {@link Future} once done.
     *
     * @return the {@link Future} that is notified once the stats were collected.
     */
    /**
     * Collects statistics about the path of the connection and notifies the {@link Promise} once done.
     *
     * @param   promise the {@link ChannelPromise} that is notified once the stats were collected.
     * @return          the {@link Future} that is notified once the stats were collected.
     */
    // CLR: collectPathStats(pathIdx) used eventLoop().newPromise(); both overloads are this
    // Task<IQuicConnectionPathStats>.
    Task<IQuicConnectionPathStats> CollectPathStatsAsync(int pathIdx, CancellationToken cancellationToken = default);

    /**
     * Creates a new {@link QuicChannelBootstrap} that can be used to create and connect new {@link QuicChannel}s to
     * endpoints using the given {@link Channel} as transport layer.
     *
     * @param channel   the {@link Channel} that is used as transport layer.
     * @return          {@link QuicChannelBootstrap} that can be used to bootstrap a client side {@link QuicChannel}.
     */
    // CLR: a static interface method (stage 2, see the type comment); the transport layer is the QuicEndpointChannel
    // whose pipeline holds the client codec (QuicClientCodecBuilder.Build()).
    static QuicChannelBootstrap NewBootstrap(IChannel channel)
    {
        return new QuicChannelBootstrap(channel);
    }
}
