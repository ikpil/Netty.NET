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
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Contains utility methods that help to bootstrap server / clients with HTTP3 support.
 */
// CLR: QuicChannel, QuicStreamChannel and QuicStreamType are codec-quic's IQuicChannel, IQuicStreamChannel and the C#
// enum QuicStreamType (docs/codec-classes-quic-porting.md, API shapes); Channel and ChannelHandler are IChannel and
// IChannelHandler. newRequestStream returns the Task<IQuicStreamChannel> of CreateStreamAsync (Future<QuicStreamChannel>)
// and takes the Async suffix (docs/transport-porting.md). IllegalStateException is InvalidOperationException; clone() of
// the protocol array is Array.Clone. The anonymous Http3RequestStreamInitializer of requestStreamInitializer is the
// private nested class HandlerRequestStreamInitializer. The @Nullable (org.jetbrains.annotations) annotations have no
// counterpart (the projects do not enable the nullable annotation context).
// newQuicServerCodecBuilder()/newQuicClientCodecBuilder() return the codec-quic builders, which configure the msquic
// listener and connection options (docs/codec-classes-quic-porting.md, Stage 2); configure(builder) keeps the Java
// self-typed bound (T extends QuicCodecBuilder<T>) as a generic constraint.
public sealed class Http3
{

    private Http3()
    {
    }

    private static readonly string[] H3_PROTOS = new string[] {
            "h3-29",
            "h3-30",
            "h3-31",
            "h3-32",
            "h3"
    };

    private static readonly AttributeKey<IQuicStreamChannel> HTTP3_CONTROL_STREAM_KEY =
            AttributeKey<IQuicStreamChannel>.ValueOf(typeof(Http3), "HTTP3ControlStream");

    private static readonly AttributeKey<QpackAttributes> QPACK_ATTRIBUTES_KEY =
            AttributeKey<QpackAttributes>.ValueOf(typeof(Http3), "QpackAttributes");

    /**
     * Returns the local initiated control stream for the HTTP/3 connection.
     * @param channel   the channel for the HTTP/3 connection.
     * @return          the control stream.
     */
    public static IQuicStreamChannel GetLocalControlStream(IChannel channel)
    {
        return channel.Attr(HTTP3_CONTROL_STREAM_KEY).Get();
    }

    /**
     * Returns the value of the <a
     * href="https://quicwg.org/base-drafts/draft-ietf-quic-http.html#name-max_push_id">max push ID</a> received for
     * this connection.
     *
     * @return Received <a
     * href="https://quicwg.org/base-drafts/draft-ietf-quic-http.html#name-max_push_id">max push ID</a> for this
     * connection.
     */
    internal static long MaxPushIdReceived(IQuicChannel channel)
    {
        Http3ConnectionHandler connectionHandler = Http3CodecUtils.GetConnectionHandlerOrClose(channel);
        if (connectionHandler == null)
        {
            throw new InvalidOperationException("Connection handler not found.");
        }

        return connectionHandler.localControlStreamHandler.MaxPushIdReceived();
    }

    internal static void SetLocalControlStream(IChannel channel, IQuicStreamChannel controlStreamChannel)
    {
        channel.Attr(HTTP3_CONTROL_STREAM_KEY).Set(controlStreamChannel);
    }

    internal static QpackAttributes GetQpackAttributes(IChannel channel)
    {
        return channel.Attr(QPACK_ATTRIBUTES_KEY).Get();
    }

    internal static void SetQpackAttributes(IChannel channel, QpackAttributes attributes)
    {
        channel.Attr(QPACK_ATTRIBUTES_KEY).Set(attributes);
    }

    /**
     * Returns a new HTTP/3 request-stream that will use the given {@link ChannelHandler}
     * to dispatch {@link Http3RequestStreamFrame}s too. The needed HTTP/3 codecs are automatically added to the
     * pipeline as well.
     *
     * If you need more control you can also use the {@link Http3RequestStreamInitializer} directly.
     *
     * @param channel   the {@link QuicChannel} for which we create the request-stream.
     * @param handler   the {@link ChannelHandler} to add.
     * @return          the {@link Future} that will be notified once the request-stream was opened.
     */
    public static Task<IQuicStreamChannel> NewRequestStreamAsync(IQuicChannel channel, IChannelHandler handler)
    {
        return channel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL, RequestStreamInitializer(handler));
    }

    /**
     * Returns a new HTTP/3 request-stream bootstrap that will use the given {@link ChannelHandler}
     * to dispatch {@link Http3RequestStreamFrame}s too. The needed HTTP/3 codecs are automatically added to the
     * pipeline as well.
     *
     * If you need more control you can also use the {@link Http3RequestStreamInitializer} directly.
     *
     * @param channel   the {@link QuicChannel} for which we create the request-stream.
     * @param handler   the {@link ChannelHandler} to add.
     * @return          the {@link QuicStreamChannelBootstrap} that should be used.
     */
    public static QuicStreamChannelBootstrap NewRequestStreamBootstrap(IQuicChannel channel, IChannelHandler handler)
    {
        return channel.NewStreamBootstrap().Handler(RequestStreamInitializer(handler))
                .Type(QuicStreamType.BIDIRECTIONAL);
    }

    /**
     * Returns the supported protocols for H3.
     *
     * @return the supported protocols.
     */
    public static string[] SupportedApplicationProtocols()
    {
        return (string[]) H3_PROTOS.Clone();
    }

    /**
     * <a href="https://tools.ietf.org/html/draft-ietf-quic-http-32#section-6.2">
     *     Minimum number max unidirectional streams</a>.
     */
    // control-stream, qpack decoder stream, qpack encoder stream
    public const int MIN_INITIAL_MAX_STREAMS_UNIDIRECTIONAL = 3;

    /**
     * <a href="https://tools.ietf.org/html/draft-ietf-quic-http-32#section-6.2">
     *     Minimum max data for unidirectional streams</a>.
     */
    public const int MIN_INITIAL_MAX_STREAM_DATA_UNIDIRECTIONAL = 1024;

    /**
     * Returns a new {@link QuicServerCodecBuilder} that has preconfigured for HTTP3.
     *
     * @return a pre-configured builder for HTTP3.
     */
    public static QuicServerCodecBuilder NewQuicServerCodecBuilder()
    {
        return Configure(new QuicServerCodecBuilder());
    }

    /**
     * Returns a new {@link QuicClientCodecBuilder} that has preconfigured for HTTP3.
     *
     * @return a pre-configured builder for HTTP3.
     */
    public static QuicClientCodecBuilder NewQuicClientCodecBuilder()
    {
        return Configure(new QuicClientCodecBuilder());
    }

    private static T Configure<T>(T builder) where T : QuicCodecBuilder<T>
    {
        return builder.InitialMaxStreamsUnidirectional(MIN_INITIAL_MAX_STREAMS_UNIDIRECTIONAL)
                .InitialMaxStreamDataUnidirectional(MIN_INITIAL_MAX_STREAM_DATA_UNIDIRECTIONAL);
    }

    private static Http3RequestStreamInitializer RequestStreamInitializer(IChannelHandler handler)
    {
        if (handler is Http3RequestStreamInitializer)
        {
            return (Http3RequestStreamInitializer) handler;
        }

        return new HandlerRequestStreamInitializer(handler);
    }

    // CLR: the anonymous Http3RequestStreamInitializer of requestStreamInitializer(handler).
    private sealed class HandlerRequestStreamInitializer : Http3RequestStreamInitializer
    {
        private readonly IChannelHandler handler;

        internal HandlerRequestStreamInitializer(IChannelHandler handler)
        {
            this.handler = handler;
        }

        protected override void InitRequestStream(IQuicStreamChannel ch)
        {
            ch.Pipeline.AddLast(handler);
        }
    }
}
