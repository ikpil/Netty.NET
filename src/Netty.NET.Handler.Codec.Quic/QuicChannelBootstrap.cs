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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Bootstrap that helps to bootstrap {@link QuicChannel}s and connecting these to remote peers.
 */
// CLR: Channel is IChannel; the parent is the QuicEndpointChannel whose pipeline holds the client codec (it takes the
// DatagramChannel's place, see QuicEndpointChannel). Future<QuicChannel> is Task<IQuicChannel>; connect() used
// parent.eventLoop().newPromise(), and both connect overloads are ConnectAsync with a CancellationToken. The option and
// attribute maps are the Dictionary shapes of Quic; option/attr/streamOption/streamAttr take T as Transport's Bootstrap
// does. SocketAddress is EndPoint and new InetSocketAddress(0) the wildcard IPEndPoint with port 0; the casts to
// InetSocketAddress have no counterpart (System.Net.Quic takes an IPEndPoint or a DnsEndPoint as the remote endpoint).
// The QuicChannel is the CLR-only MsQuicChannel of System.Net.Quic (QuicheQuicChannel.forClient). IllegalStateException
// is InvalidOperationException; @Nullable has no counterpart.
public sealed class QuicChannelBootstrap
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(QuicChannelBootstrap));

    private readonly IChannel parent;
    // The order in which ChannelOptions are applied is important they may depend on each other for validation
    // purposes.
    private readonly Dictionary<ChannelOption, object> options = new Dictionary<ChannelOption, object>();
    private readonly Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> attrs =
            new Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)>();
    private readonly Dictionary<ChannelOption, object> streamOptions = new Dictionary<ChannelOption, object>();
    private readonly Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> streamAttrs =
            new Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)>();

    private EndPoint local;
    private EndPoint remote;
    private QuicConnectionAddress localAddress = QuicConnectionAddress.EPHEMERAL;
    private QuicConnectionAddress remoteAddress = QuicConnectionAddress.EPHEMERAL;
    private IChannelHandler handler;
    private IChannelHandler streamHandler;

    /**
     * Creates a new instance which uses the given {@link Channel} to bootstrap the {@link QuicChannel}.
     * This {@link io.netty.channel.ChannelPipeline} of the {@link Channel} needs to have the quic codec in the
     * pipeline.
     *
     * @param parent    the {@link Channel} that is used as the transport layer.
     * @deprecated Use QuicChannel.newBootstrap() instead.
     */
    // CLR: @Deprecated whose @deprecated text names its replacement, so it is not public (owner decision 2026-11-16:
    // such members are not ported); IQuicChannel.NewBootstrap(IChannel), the replacement, creates the bootstrap through
    // this internal constructor, as Java's newBootstrap calls the deprecated one.
    internal QuicChannelBootstrap(IChannel parent)
    {
        Quic.EnsureAvailability();
        this.parent = ObjectUtil.CheckNotNull(parent, "parent");
    }

    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link QuicChannel} instances once they got
     * created. Use a value of {@code null} to remove a previous set {@link ChannelOption}.
     *
     * @param option    the {@link ChannelOption} to apply to the {@link QuicChannel}.
     * @param value     the value of the option.
     * @param <T>       the type of the value.
     * @return          this instance.
     */
    public QuicChannelBootstrap Option<T>(ChannelOption<T> option, T value)
    {
        Quic.UpdateOptions(options, option, value);
        return this;
    }

    /**
     * Allow to specify an initial attribute of the newly created {@link QuicChannel}.  If the {@code value} is
     * {@code null}, the attribute of the specified {@code key} is removed.
     *
     * @param key       the {@link AttributeKey} to apply to the {@link QuicChannel}.
     * @param value     the value of the attribute.
     * @param <T>       the type of the value.
     * @return          this instance.
     */
    public QuicChannelBootstrap Attr<T>(AttributeKey<T> key, T value) where T : class
    {
        Quic.UpdateAttributes(attrs, key, value);
        return this;
    }

    /**
     * Set the {@link ChannelHandler} that is added to the {@link io.netty.channel.ChannelPipeline} of the
     * {@link QuicChannel} once created.
     *
     * @param handler   the {@link ChannelHandler} that is added to the {@link QuicChannel}s
     *                  {@link io.netty.channel.ChannelPipeline}.
     * @return          this instance.
     */
    public QuicChannelBootstrap Handler(IChannelHandler handler)
    {
        this.handler = ObjectUtil.CheckNotNull(handler, "handler");
        return this;
    }

    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link QuicStreamChannel} instances once they got
     * created. Use a value of {@code null} to remove a previous set {@link ChannelOption}.
     *
     * @param option    the {@link ChannelOption} to apply to the {@link QuicStreamChannel}s.
     * @param value     the value of the option.
     * @param <T>       the type of the value.
     * @return          this instance.
     */
    public QuicChannelBootstrap StreamOption<T>(ChannelOption<T> option, T value)
    {
        Quic.UpdateOptions(streamOptions, option, value);
        return this;
    }

    /**
     * Allow to specify an initial attribute of the newly created {@link QuicStreamChannel}. If the {@code value} is
     * {@code null}, the attribute of the specified {@code key} is removed.
     *
     * @param key       the {@link AttributeKey} to apply to the {@link QuicStreamChannel}s.
     * @param value     the value of the attribute.
     * @param <T>       the type of the value.
     * @return          this instance.
     */
    public QuicChannelBootstrap StreamAttr<T>(AttributeKey<T> key, T value) where T : class
    {
        Quic.UpdateAttributes(streamAttrs, key, value);
        return this;
    }

    /**
     * Set the {@link ChannelHandler} that is added to the {@link io.netty.channel.ChannelPipeline} of the
     * {@link QuicStreamChannel} once created.
     *
     * @param streamHandler     the {@link ChannelHandler} that is added to the {@link QuicStreamChannel}s
     *                          {@link io.netty.channel.ChannelPipeline}.
     * @return                  this instance.
     */
    public QuicChannelBootstrap StreamHandler(IChannelHandler streamHandler)
    {
        this.streamHandler = ObjectUtil.CheckNotNull(streamHandler, "streamHandler");
        return this;
    }

    /**
     * Set the local address.
     *
     * @param local    the {@link SocketAddress} of the local peer.
     * @return          this instance.
     */
    public QuicChannelBootstrap LocalAddress(EndPoint local)
    {
        this.local = ObjectUtil.CheckNotNull(local, "local");
        return this;
    }

    /**
     * Set the remote address of the host to talk to.
     *
     * @param remote    the {@link SocketAddress} of the remote peer.
     * @return          this instance.
     */
    public QuicChannelBootstrap RemoteAddress(EndPoint remote)
    {
        this.remote = ObjectUtil.CheckNotNull(remote, "remote");
        return this;
    }

    /**
     * Set the {@link QuicConnectionAddress} to use. If none is specified a random address is generated on your
     * behalf.
     *
     * @param connectionAddress     the {@link QuicConnectionAddress} to use.
     * @return                      this instance.
     * @deprecated                  use {@link #localConnectionAddress(QuicConnectionAddress)}.
     */
    // CLR: @Deprecated whose @deprecated text names its replacement (LocalConnectionAddress), so it is not ported (owner
    // decision 2026-11-16).

    /**
     * Set the {@link QuicConnectionAddress} to use. If none is specified a random address is generated on your
     * behalf.
     *
     * @param localConnectionAddress     the {@link QuicConnectionAddress} to use.
     * @return                      this instance.
     */
    // CLR: System.Net.Quic hides the connection IDs; the address is the channel's LocalAddress (MsQuicChannel).
    public QuicChannelBootstrap LocalConnectionAddress(QuicConnectionAddress localConnectionAddress)
    {
        this.localAddress = ObjectUtil.CheckNotNull(localConnectionAddress, "localConnectionAddress");
        return this;
    }

    /**
     * Set the {@link QuicConnectionAddress} to use. If none is specified a random address is generated on your
     * behalf. Be aware that the provided {@link QuicConnectionAddress} should be generated carefully in a random way
     * that makes it impossible to predict it as otherwise the security of the quic connection might be too weak.
     * Fore more details see
     * <a href="https://datatracker.ietf.org/doc/html/rfc9000#section-7.2">RFC9000 Section 7.2</a>.
     *
     * @param remoteConnectionAddress     the {@link QuicConnectionAddress} to use.
     * @return                            this instance.
     */
    // CLR: System.Net.Quic hides the connection IDs; the address is the channel's RemoteAddress (MsQuicChannel).
    public QuicChannelBootstrap RemoteConnectionAddress(QuicConnectionAddress remoteConnectionAddress)
    {
        this.remoteAddress = ObjectUtil.CheckNotNull(remoteConnectionAddress, "remoteConnectionAddress");
        return this;
    }

    /**
     * Connects a {@link QuicChannel} to the remote peer and notifies the future once done.
     *
     * @return {@link Future} which is notified once the operation completes.
     */
    /**
     * Connects a {@link QuicChannel} to the remote peer and notifies the promise once done.
     *
     * @param promise   the {@link Promise} which is notified once the operations completes.
     * @return          {@link Future} which is notified once the operation completes.

     */
    // CLR: connect() and connect(Promise) are this Task<IQuicChannel>; a canceled token cancels the QuicChannel's connect.
    public Task<IQuicChannel> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (handler == null && streamHandler == null)
        {
            throw new InvalidOperationException("handler and streamHandler not set");
        }

        EndPoint local = this.local;
        if (local == null)
        {
            local = parent.LocalAddress;
        }

        if (local == null)
        {
            local = new IPEndPoint(IPAddress.Any, 0);
        }

        EndPoint remote = this.remote;
        if (remote == null)
        {
            remote = parent.RemoteAddress;
        }

        if (remote == null)
        {
            throw new InvalidOperationException("remote not set");
        }
        if (!MsQuicConfig.IsSupportedPlatform)
        {
            throw new PlatformNotSupportedException("System.Net.Quic is not supported on this platform");
        }

        QuicConnectionAddress localaddress = localAddress;
        QuicConnectionAddress remoteaddress = remoteAddress;
        MsQuicChannel channel = MsQuicChannel.ForClient(parent, local, remote,
                streamHandler, Quic.ToOptionsArray(streamOptions), Quic.ToAttributesArray(streamAttrs));

        Quic.SetupChannel(channel, Quic.ToOptionsArray(options), Quic.ToAttributesArray(attrs), handler, logger);
        TaskCompletionSource<IQuicChannel> promise =
                new TaskCompletionSource<IQuicChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        IEventLoop eventLoop = parent.EventLoop;
        _ = eventLoop.RegisterAsync(channel).ContinueWith(future =>
        {
            Exception cause = future.Exception?.InnerException;
            if (cause != null || future.IsCanceled)
            {
                promise.TrySetException(cause ?? new OperationCanceledException());
            }
            else
            {
                _ = channel.ConnectAsync(remoteaddress, localaddress, cancellationToken).ContinueWith(f =>
                {
                    Exception error = f.Exception?.InnerException;
                    if (error != null)
                    {
                        promise.TrySetException(error);
                    }
                    else if (f.IsCanceled)
                    {
                        promise.TrySetCanceled(cancellationToken);
                    }
                    else
                    {
                        promise.TrySetResult(channel);
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return promise.Task;
    }
}
