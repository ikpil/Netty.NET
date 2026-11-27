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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Allows to bootstrap outgoing {@link QuicStreamChannel}s.
 */
// CLR: Future<QuicStreamChannel> is Task<IQuicStreamChannel>; create() used parent.eventLoop().newPromise(), and both
// create overloads are CreateAsync with a CancellationToken, as IQuicChannel.CreateStreamAsync. The option and attribute
// maps are the Dictionary shapes of Quic (Map<ChannelOption<?>, Object> and the attribute entries with their typed
// setters, see Quic); option(option, value) and attr(key, value) take T as Transport's Bootstrap does (AttributeKey<T>
// needs a reference type T). checkNotNull(type) has no counterpart for the QuicStreamType enum (a value type cannot be
// null). IllegalStateException is InvalidOperationException. The package-private constructor is internal; @Nullable has
// no counterpart.
public sealed class QuicStreamChannelBootstrap
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(QuicStreamChannelBootstrap));

    private readonly IQuicChannel parent;
    private readonly Dictionary<ChannelOption, object> options = new Dictionary<ChannelOption, object>();
    private readonly Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> attrs =
            new Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)>();
    private IChannelHandler handler;
    private QuicStreamType type = QuicStreamType.BIDIRECTIONAL;

    /**
     * Creates a new instance which uses the given {@link QuicChannel} to bootstrap {@link QuicStreamChannel}s.
     *
     * @param parent    the {@link QuicChannel} that is used.

     */
    internal QuicStreamChannelBootstrap(IQuicChannel parent)
    {
        this.parent = ObjectUtil.CheckNotNull(parent, "parent");
    }

    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link QuicStreamChannel} instances once they got
     * created. Use a value of {@code null} to remove a previous set {@link ChannelOption}.
     *
     * @param option    the {@link ChannelOption} to apply to the {@link QuicStreamChannel}.
     * @param value     the value of the option.
     * @param <T>       the type of the value.
     * @return          this instance.
     */
    public QuicStreamChannelBootstrap Option<T>(ChannelOption<T> option, T value)
    {
        Quic.UpdateOptions(options, option, value);
        return this;
    }

    /**
     * Allow to specify an initial attribute of the newly created {@link QuicStreamChannel}. If the {@code value} is
     * {@code null}, the attribute of the specified {@code key} is removed.
     *
     * @param key       the {@link AttributeKey} to apply to the {@link QuicChannel}.
     * @param value     the value of the attribute.
     * @param <T>       the type of the value.
     * @return          this instance.
     */
    public QuicStreamChannelBootstrap Attr<T>(AttributeKey<T> key, T value) where T : class
    {
        Quic.UpdateAttributes(attrs, key, value);
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
    public QuicStreamChannelBootstrap Handler(IChannelHandler streamHandler)
    {
        this.handler = ObjectUtil.CheckNotNull(streamHandler, "streamHandler");
        return this;
    }

    /**
     * Set the {@link QuicStreamType} to use for the {@link QuicStreamChannel}, default is
     * {@link QuicStreamType#BIDIRECTIONAL}.
     *
     * @param type     the {@link QuicStreamType} of the  {@link QuicStreamChannel}.
     * @return         this instance.
     */
    public QuicStreamChannelBootstrap Type(QuicStreamType type)
    {
        this.type = type;
        return this;
    }

    /**
     * Creates a new {@link QuicStreamChannel} and notifies the {@link Future}.
     *
     * @return  the {@link Future} that is notified once the operation completes.
     */
    /**
     * Creates a new {@link QuicStreamChannel} and notifies the {@link Future}.
     *
     * @param promise   the {@link Promise} that is notified once the operation completes.
     * @return          the {@link Future} that is notified once the operation completes.
     */
    public Task<IQuicStreamChannel> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (handler == null)
        {
            throw new InvalidOperationException("streamHandler not set");
        }

        return parent.CreateStreamAsync(type, new QuicStreamChannelBootstrapHandler(handler,
                Quic.ToOptionsArray(options), Quic.ToAttributesArray(attrs)), cancellationToken);
    }

    private sealed class QuicStreamChannelBootstrapHandler : ChannelInitializer<IQuicStreamChannel>
    {
        private readonly IChannelHandler streamHandler;
        private readonly KeyValuePair<ChannelOption, object>[] streamOptions;
        private readonly KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[] streamAttrs;

        internal QuicStreamChannelBootstrapHandler(IChannelHandler streamHandler,
                                                   KeyValuePair<ChannelOption, object>[] streamOptions,
                                                   KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)>[]
                                                           streamAttrs)
        {
            this.streamHandler = streamHandler;
            this.streamOptions = streamOptions;
            this.streamAttrs = streamAttrs;
        }
        protected override void InitChannel(IQuicStreamChannel ch)
        {
            Quic.SetupChannel(ch, streamOptions, streamAttrs, streamHandler, logger);
        }
    }
}
