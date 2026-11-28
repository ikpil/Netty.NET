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
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * {@link QuicCodecBuilder} that configures and builds a {@link ChannelHandler} that should be added to the
 * {@link io.netty.channel.ChannelPipeline} of a {@code QUIC} server.
 */
// CLR: the option and attribute maps are the Dictionary shapes of Quic (Map<ChannelOption<?>, Object> and the
// attribute entries with their typed setters, see Quic); option/attr/streamOption/streamAttr take T as Transport's
// Bootstrap does (AttributeKey<T> needs a reference type T). The built codec is the handler of a
// QuicEndpointChannel and creates the QuicChannels of a QuicListener (MsQuicServerCodec, the counterpart of
// QuicheQuicServerCodec). msquic generates the connection IDs and stateless reset tokens and validates client
// addresses with its own retry tokens, so a connectionIdAddressGenerator or resetTokenGenerator, and a token
// handler other than NoQuicTokenHandler and InsecureQuicTokenHandler (which only ask for tokens without their own
// validation), report NotSupportedException from Build(); the defaults that build(...) chooses for them are kept.
// @Nullable has no counterpart.
public sealed class QuicServerCodecBuilder : QuicCodecBuilder<QuicServerCodecBuilder>
{
    // The order in which ChannelOptions are applied is important they may depend on each other for validation
    // purposes.
    private readonly Dictionary<ChannelOption, object> options = new Dictionary<ChannelOption, object>();
    private readonly Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> attrs =
            new Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)>();
    private readonly Dictionary<ChannelOption, object> streamOptions = new Dictionary<ChannelOption, object>();
    private readonly Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)> streamAttrs =
            new Dictionary<IAttributeKey, (object Value, Action<IChannel> Apply)>();
    private IChannelHandler handler;
    private IChannelHandler streamHandler;
    private IQuicConnectionIdGenerator connectionIdAddressGenerator;
    private IQuicTokenHandler tokenHandler;
    private IQuicResetTokenGenerator resetTokenGenerator;

    /**
     * Creates a new instance.
     */
    public QuicServerCodecBuilder() : base(true)
    {
    }

    private QuicServerCodecBuilder(QuicServerCodecBuilder builder) : base(builder)
    {
        foreach (KeyValuePair<ChannelOption, object> e in builder.options)
        {
            options[e.Key] = e.Value;
        }

        foreach (KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)> e in builder.attrs)
        {
            attrs[e.Key] = e.Value;
        }

        foreach (KeyValuePair<ChannelOption, object> e in builder.streamOptions)
        {
            streamOptions[e.Key] = e.Value;
        }

        foreach (KeyValuePair<IAttributeKey, (object Value, Action<IChannel> Apply)> e in builder.streamAttrs)
        {
            streamAttrs[e.Key] = e.Value;
        }

        handler = builder.handler;
        streamHandler = builder.streamHandler;
        connectionIdAddressGenerator = builder.connectionIdAddressGenerator;
        tokenHandler = builder.tokenHandler;
        resetTokenGenerator = builder.resetTokenGenerator;
    }

    public override QuicServerCodecBuilder Clone()
    {
        return new QuicServerCodecBuilder(this);
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
    public QuicServerCodecBuilder Option<T>(ChannelOption<T> option, T value)
    {
        Quic.UpdateOptions(options, option, value);

        return Self();
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
    public QuicServerCodecBuilder Attr<T>(AttributeKey<T> key, T value) where T : class
    {
        Quic.UpdateAttributes(attrs, key, value);

        return Self();
    }

    /**
     * Set the {@link ChannelHandler} that is added to the {@link io.netty.channel.ChannelPipeline} of the
     * {@link QuicChannel} once created.
     *
     * @param handler   the {@link ChannelHandler} that is added to the {@link QuicChannel}s
     *                  {@link io.netty.channel.ChannelPipeline}.
     * @return          this instance.
     */
    public QuicServerCodecBuilder Handler(IChannelHandler handler)
    {
        this.handler = ObjectUtil.CheckNotNull(handler, "handler");
        return Self();
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
    public QuicServerCodecBuilder StreamOption<T>(ChannelOption<T> option, T value)
    {
        Quic.UpdateOptions(streamOptions, option, value);

        return Self();
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
    public QuicServerCodecBuilder StreamAttr<T>(AttributeKey<T> key, T value) where T : class
    {
        Quic.UpdateAttributes(streamAttrs, key, value);

        return Self();
    }

    /**
     * Set the {@link ChannelHandler} that is added to the {@link io.netty.channel.ChannelPipeline} of the
     * {@link QuicStreamChannel} once created.
     *
     * @param streamHandler     the {@link ChannelHandler} that is added to the {@link QuicStreamChannel}s
     *                          {@link io.netty.channel.ChannelPipeline}.
     * @return                  this instance.
     */
    public QuicServerCodecBuilder StreamHandler(IChannelHandler streamHandler)
    {
        this.streamHandler = ObjectUtil.CheckNotNull(streamHandler, "streamHandler");
        return Self();
    }

    /**
     * Sets the {@link QuicConnectionIdGenerator} to use.
     *
     * @param connectionIdAddressGenerator  the {@link QuicConnectionIdGenerator} to use.
     * @return                              this instance.
     */
    public QuicServerCodecBuilder ConnectionIdAddressGenerator(
            IQuicConnectionIdGenerator connectionIdAddressGenerator)
    {
        this.connectionIdAddressGenerator = connectionIdAddressGenerator;
        return this;
    }

    /**
     * Set the {@link QuicTokenHandler} that is used to generate and validate tokens or
     * {@code null} if no tokens should be used at all.
     *
     * @param tokenHandler  the {@link QuicTokenHandler} to use.
     * @return              this instance.
     */
    public QuicServerCodecBuilder TokenHandler(IQuicTokenHandler tokenHandler)
    {
        this.tokenHandler = tokenHandler;
        return Self();
    }

    /**
     * Set the {@link QuicResetTokenGenerator} that is used to generate stateless reset tokens or
     * {@code null} if the default should be used.
     *
     * @param resetTokenGenerator  the {@link QuicResetTokenGenerator} to use.
     * @return                     this instance.
     */
    public QuicServerCodecBuilder ResetTokenGenerator(IQuicResetTokenGenerator resetTokenGenerator)
    {
        this.resetTokenGenerator = resetTokenGenerator;
        return Self();
    }

    protected override void Validate()
    {
        base.Validate();
        if (handler == null && streamHandler == null)
        {
            throw new InvalidOperationException("handler and streamHandler not set");
        }
    }

    internal override IChannelHandler Build(MsQuicConfig config,
                                            Func<IQuicChannel, QuicSslEngine> sslEngineProvider,
                                            IExecutor sslTaskExecutor,
                                            int localConnIdLength, IFlushStrategy flushStrategy)
    {
        Validate();
        // CLR: the handlers that msquic replaces (see the class comment).
        if (this.tokenHandler != null && !(this.tokenHandler is NoQuicTokenHandler) &&
            !(this.tokenHandler is InsecureQuicTokenHandler))
        {
            throw new NotSupportedException("tokenHandler(" + this.tokenHandler.GetType().Name +
                                            ") is not supported by System.Net.Quic (msquic validates addresses itself)");
        }
        if (connectionIdAddressGenerator != null)
        {
            throw new NotSupportedException("connectionIdAddressGenerator(...) is not supported by System.Net.Quic " +
                                            "(msquic generates the connection IDs)");
        }
        if (this.resetTokenGenerator != null)
        {
            throw new NotSupportedException("resetTokenGenerator(...) is not supported by System.Net.Quic " +
                                            "(msquic generates the stateless reset tokens)");
        }

        IQuicTokenHandler tokenHandler = this.tokenHandler;
        if (tokenHandler == null)
        {
            tokenHandler = NoQuicTokenHandler.INSTANCE;
        }

        IQuicConnectionIdGenerator generator = connectionIdAddressGenerator;
        if (generator == null)
        {
            generator = IQuicConnectionIdGenerator.SignGenerator();
        }

        IQuicResetTokenGenerator resetTokenGenerator = this.resetTokenGenerator;
        if (resetTokenGenerator == null)
        {
            resetTokenGenerator = IQuicResetTokenGenerator.SignGenerator();
        }

        IChannelHandler handler = this.handler;
        IChannelHandler streamHandler = this.streamHandler;
        if (!MsQuicConfig.IsSupportedPlatform)
        {
            throw new PlatformNotSupportedException("System.Net.Quic is not supported on this platform");
        }

        return new MsQuicServerCodec(config, localConnIdLength, flushStrategy, sslEngineProvider, handler,
                Quic.ToOptionsArray(options), Quic.ToAttributesArray(attrs),
                streamHandler, Quic.ToOptionsArray(streamOptions), Quic.ToAttributesArray(streamAttrs));
    }
}
