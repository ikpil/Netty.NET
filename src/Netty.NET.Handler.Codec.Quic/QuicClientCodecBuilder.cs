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
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * {@link QuicCodecBuilder} that configures and builds a {@link ChannelHandler} that should be added to the
 * {@link io.netty.channel.ChannelPipeline} of a {@code QUIC} client.
 */
// CLR: the built codec is the handler of a QuicEndpointChannel and connects the QuicChannels that
// QuicChannel.NewBootstrap(endpoint) creates with QuicConnection.ConnectAsync (MsQuicClientCodec, the counterpart of
// QuicheQuicClientCodec). The private copy constructor takes QuicCodecBuilder<QuicClientCodecBuilder>, as in Java.
public sealed class QuicClientCodecBuilder : QuicCodecBuilder<QuicClientCodecBuilder>
{

    /**
     * Creates a new instance.
     */
    public QuicClientCodecBuilder() : base(false)
    {
    }

    private QuicClientCodecBuilder(QuicCodecBuilder<QuicClientCodecBuilder> builder) : base(builder)
    {
    }

    public override QuicClientCodecBuilder Clone()
    {
        return new QuicClientCodecBuilder(this);
    }

    internal override IChannelHandler Build(MsQuicConfig config,
                                            Func<IQuicChannel, QuicSslEngine> sslEngineProvider,
                                            IExecutor sslTaskExecutor,
                                            int localConnIdLength, IFlushStrategy flushStrategy)
    {
        if (!MsQuicConfig.IsSupportedPlatform)
        {
            throw new PlatformNotSupportedException("System.Net.Quic is not supported on this platform");
        }

        return new MsQuicClientCodec(config, sslEngineProvider, sslTaskExecutor, localConnIdLength, flushStrategy);
    }
}
