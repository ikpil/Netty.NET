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
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Abstract base class for {@code QUIC} codec builders.
 *
 * @param <B> the type of the {@link QuicCodecBuilder}.
 */
// CLR: QuicCodecBuilder<B extends QuicCodecBuilder<B>> is QuicCodecBuilder<B> where B : QuicCodecBuilder<B>; the final
// setters are non-virtual and return B (Self()). ChannelHandler is IChannelHandler, Executor IExecutor,
// Function<QuicChannel, ? extends QuicSslEngine> Func<IQuicChannel, QuicSslEngine>, and each (long amount, TimeUnit unit)
// pair is a TimeSpan (the Transport convention, docs/common-clr-design.md; unit.toMillis truncates to milliseconds, as
// TimeSpan.Ticks / TicksPerMillisecond does). IllegalArgumentException/IllegalStateException are ArgumentException/
// InvalidOperationException. The boxed Long/Boolean/Integer fields (null: the quiche default) are nullable values.
// Quiche.QUICHE_PROTOCOL_VERSION is QUIC version 1 (System.Net.Quic negotiates it, see Quic.IsVersionSupported) and
// Quiche.QUICHE_MAX_CONN_ID_LEN is Quic.MAX_CONN_ID_LEN. The QuicheConfig of build() is MsQuicConfig, the settings of
// System.Net.Quic (msquic): a setting without a System.Net.Quic counterpart keeps its API and reports
// NotSupportedException from Build() when it holds a value other than quiche's default (owner decision 2026-11-27,
// docs/codec-classes-quic-porting.md, Stage 2, which lists the settings); config.free() has no counterpart (managed
// options). sslContext(context) also keeps the context, whose ALPN protocols a server listener needs before any
// connection. The SSL task executor is accepted and not used: msquic runs TLS on its own threads.
public abstract class QuicCodecBuilder<B> where B : QuicCodecBuilder<B>
{
    private readonly bool server;
    private bool? grease;
    private long? maxIdleTimeout;
    private long? maxRecvUdpPayloadSize;
    private long? maxSendUdpPayloadSize;
    private long? initialMaxData;
    private long? initialMaxStreamDataBidiLocal;
    private long? initialMaxStreamDataBidiRemote;
    private long? initialMaxStreamDataUni;
    private long? initialMaxStreamsBidi;
    private long? initialMaxStreamsUni;
    private long? ackDelayExponent;
    private long? maxAckDelay;
    private bool? disableActiveMigration;
    private bool? enableHystart;
    private bool? discoverPmtu;
    private QuicCongestionControlAlgorithm? congestionControlAlgorithm;
    private int? initialCongestionWindowPackets;
    private int localConnIdLength;
    private Func<IQuicChannel, QuicSslEngine> sslEngineProvider;
    private IFlushStrategy flushStrategy = IFlushStrategy.DEFAULT;
    private int? recvQueueLen;
    private int? sendQueueLen;
    private long? activeConnectionIdLimit;
    private byte[] statelessResetToken;

    private IExecutor sslTaskExecutor;

    // package-private for testing only
    internal int version;

    internal QuicCodecBuilder(bool server)
    {
        Quic.EnsureAvailability();
        this.version = 1;
        this.localConnIdLength = Quic.MAX_CONN_ID_LEN;
        this.server = server;
    }

    internal QuicCodecBuilder(QuicCodecBuilder<B> builder)
    {
        Quic.EnsureAvailability();
        this.server = builder.server;
        this.grease = builder.grease;
        this.maxIdleTimeout = builder.maxIdleTimeout;
        this.maxRecvUdpPayloadSize = builder.maxRecvUdpPayloadSize;
        this.maxSendUdpPayloadSize = builder.maxSendUdpPayloadSize;
        this.initialMaxData = builder.initialMaxData;
        this.initialMaxStreamDataBidiLocal = builder.initialMaxStreamDataBidiLocal;
        this.initialMaxStreamDataBidiRemote = builder.initialMaxStreamDataBidiRemote;
        this.initialMaxStreamDataUni = builder.initialMaxStreamDataUni;
        this.initialMaxStreamsBidi = builder.initialMaxStreamsBidi;
        this.initialMaxStreamsUni = builder.initialMaxStreamsUni;
        this.ackDelayExponent = builder.ackDelayExponent;
        this.maxAckDelay = builder.maxAckDelay;
        this.disableActiveMigration = builder.disableActiveMigration;
        this.enableHystart = builder.enableHystart;
        this.discoverPmtu = builder.discoverPmtu;
        this.congestionControlAlgorithm = builder.congestionControlAlgorithm;
        this.initialCongestionWindowPackets = builder.initialCongestionWindowPackets;
        this.localConnIdLength = builder.localConnIdLength;
        this.sslEngineProvider = builder.sslEngineProvider;
        this.flushStrategy = builder.flushStrategy;
        this.recvQueueLen = builder.recvQueueLen;
        this.sendQueueLen = builder.sendQueueLen;
        this.activeConnectionIdLimit = builder.activeConnectionIdLimit;
        this.statelessResetToken = builder.statelessResetToken;
        this.sslTaskExecutor = builder.sslTaskExecutor;
        this.version = builder.version;
    }

    /**
     * Returns itself.
     *
     * @return itself.
     */
    protected B Self()
    {
        return (B) (object) this;
    }

    /**
     * Sets the {@link FlushStrategy} that will be used to detect when an automatic flush
     * should happen.
     *
     * @param flushStrategy   the strategy to use.
     * @return                the instance itself.
     */
    public B FlushStrategy(IFlushStrategy flushStrategy)
    {
        this.flushStrategy = ObjectUtil.CheckNotNull(flushStrategy, "flushStrategy");
        return Self();
    }

    /**
     * Sets the congestion control algorithm to use.
     *
     * The default is {@link QuicCongestionControlAlgorithm#CUBIC}.
     *
     * @param congestionControlAlgorithm    the {@link QuicCongestionControlAlgorithm} to use.
     * @return                              the instance itself.
     */
    public B CongestionControlAlgorithm(QuicCongestionControlAlgorithm congestionControlAlgorithm)
    {
        this.congestionControlAlgorithm = congestionControlAlgorithm;
        return Self();
    }

    /**
     * Sets initial congestion window size in terms of packet count.
     *
     * The default value is 10.
     *
     * @param numPackets number of packets for the initial congestion window
     * @return
     */
    public B InitialCongestionWindowPackets(int numPackets)
    {
        this.initialCongestionWindowPackets = numPackets;
        return Self();
    }

    /**
     * Set if <a href="https://tools.ietf.org/html/draft-thomson-quic-bit-grease-00">greasing</a> should be enabled
     * or not.
     *
     * The default value is {@code true}.
     *
     * @param enable    {@code true} if enabled, {@code false} otherwise.
     * @return          the instance itself.
     */
    public B Grease(bool enable)
    {
        grease = enable;
        return Self();
    }

    /**
     * See <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_max_idle_timeout">
     *     set_max_idle_timeout</a>.
     *
     * The default value is infinite, that is, no timeout is used.
     *
     * @param amount    the maximum idle timeout.
     * @param unit      the {@link TimeUnit}.
     * @return          the instance itself.
     */
    // CLR: (amount, unit) is a TimeSpan (see the class comment).
    public B MaxIdleTimeout(TimeSpan amount)
    {
        this.maxIdleTimeout = ToMillis(amount, "amount");
        return Self();
    }

    /**
     * See <a href="https://github.com/cloudflare/quiche/blob/35e38d987c1e53ef2bd5f23b754c50162b5adac8/src/lib.rs#L669">
     *     set_max_send_udp_payload_size</a>.
     *
     * The default and minimum value is 1200.
     *
     * @param size    the maximum payload size that is advertised to the remote peer.
     * @return        the instance itself.
     */
    public B MaxSendUdpPayloadSize(long size)
    {
        this.maxSendUdpPayloadSize = ObjectUtil.CheckPositiveOrZero(size, "value");
        return Self();
    }

    /**
     * See <a href="https://github.com/cloudflare/quiche/blob/35e38d987c1e53ef2bd5f23b754c50162b5adac8/src/lib.rs#L662">
     *     set_max_recv_udp_payload_size</a>.
     *
     * The default value is 65527.
     *
     * @param size    the maximum payload size that is advertised to the remote peer.
     * @return        the instance itself.
     */
    public B MaxRecvUdpPayloadSize(long size)
    {
        this.maxRecvUdpPayloadSize = ObjectUtil.CheckPositiveOrZero(size, "value");
        return Self();
    }

    /**
     * See <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_initial_max_data">
     *     set_initial_max_data</a>.
     *
     * The default value is 0.
     *
     * @param value   the initial maximum data limit.
     * @return        the instance itself.
     */
    public B InitialMaxData(long value)
    {
        this.initialMaxData = ObjectUtil.CheckPositiveOrZero(value, "value");
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_initial_max_stream_data_bidi_local">
     *     set_initial_max_stream_data_bidi_local</a>.
     *
     * The default value is 0.
     *
     * @param value   the initial maximum data limit for local bidirectional streams.
     * @return        the instance itself.
     */
    public B InitialMaxStreamDataBidirectionalLocal(long value)
    {
        this.initialMaxStreamDataBidiLocal = ObjectUtil.CheckPositiveOrZero(value, "value");
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_initial_max_stream_data_bidi_remote">
     *     set_initial_max_stream_data_bidi_remote</a>.
     *
     * The default value is 0.
     *
     * @param value   the initial maximum data limit for remote bidirectional streams.
     * @return        the instance itself.
     */
    public B InitialMaxStreamDataBidirectionalRemote(long value)
    {
        this.initialMaxStreamDataBidiRemote = ObjectUtil.CheckPositiveOrZero(value, "value");
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_initial_max_stream_data_uni">
     *     set_initial_max_stream_data_uni</a>.
     *
     * The default value is 0.
     *
     * @param value   the initial maximum data limit for unidirectional streams.
     * @return        the instance itself.
     */
    public B InitialMaxStreamDataUnidirectional(long value)
    {
        this.initialMaxStreamDataUni = ObjectUtil.CheckPositiveOrZero(value, "value");
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_initial_max_streams_bidi">
     *     set_initial_max_streams_bidi</a>.
     *
     * The default value is 0.
     *
     * @param value   the initial maximum stream limit for bidirectional streams.
     * @return        the instance itself.
     */
    public B InitialMaxStreamsBidirectional(long value)
    {
        this.initialMaxStreamsBidi = ObjectUtil.CheckPositiveOrZero(value, "value");
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_initial_max_streams_uni">
     *     set_initial_max_streams_uni</a>.
     *
     * The default value is 0.
     *
     * @param value   the initial maximum stream limit for unidirectional streams.
     * @return        the instance itself.
     */
    public B InitialMaxStreamsUnidirectional(long value)
    {
        this.initialMaxStreamsUni = ObjectUtil.CheckPositiveOrZero(value, "value");
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_ack_delay_exponent">
     *     set_ack_delay_exponent</a>.
     *
     * The default value is 3.
     *
     * @param value   the delay exponent used for ACKs.
     * @return        the instance itself.
     */
    public B AckDelayExponent(long value)
    {
        this.ackDelayExponent = ObjectUtil.CheckPositiveOrZero(value, "value");
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_max_ack_delay">
     *     set_max_ack_delay</a>.
     *
     * The default value is 25 milliseconds.
     *
     * @param amount    the max ack delay.
     * @param unit      the {@link TimeUnit}.
     * @return          the instance itself.
     */
    // CLR: (amount, unit) is a TimeSpan (see the class comment).
    public B MaxAckDelay(TimeSpan amount)
    {
        this.maxAckDelay = ToMillis(amount, "amount");
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.set_disable_active_migration">
     *     set_disable_active_migration</a>.
     *
     * The default value is {@code true}.
     *
     * @param enable  {@code true} if migration should be enabled, {@code false} otherwise.
     * @return        the instance itself.
     */
    public B ActiveMigration(bool enable)
    {
        this.disableActiveMigration = !enable;
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/0.6.0/quiche/struct.Config.html#method.enable_hystart">
     *     enable_hystart</a>.
     *
     * The default value is {@code true}.
     *
     * @param enable  {@code true} if Hystart should be enabled.
     * @return        the instance itself.
     */
    public B Hystart(bool enable)
    {
        this.enableHystart = enable;
        return Self();
    }

    /**
     * See
     * <a href="https://docs.rs/quiche/latest/quiche/struct.Config.html#method.discover_pmtu">
     *     discover_pmtu</a>.
     *
     * Configures whether to do path MTU discovery.
     *
     * The default value is {@code false}.
     *
     * @param enable  {@code true} if path MTU discovery should be enabled.
     * @return        the instance itself.
     */
    public B DiscoverPmtu(bool enable)
    {
        this.discoverPmtu = enable;
        return Self();
    }

    /**
     * Sets the local connection id length that is used.
     *
     * The default is 20, which is also the maximum that is supported.
     *
     * @param value   the length of local generated connections ids.
     * @return        the instance itself.
     */
    public B LocalConnectionIdLength(int value)
    {
        this.localConnIdLength = ObjectUtil.CheckInRange(value, 0, Quic.MAX_CONN_ID_LEN, "value");
        return Self();
    }

    /**
     * Allows to configure the {@code QUIC version} that should be used.
     *
     * The default value is the latest supported version by the underlying library.
     *
     * @param version the {@code QUIC version} to use.
     * @return        the instance itself.
     */
    public B Version(int version)
    {
        this.version = version;
        return Self();
    }

    /**
     * If configured this will enable <a href="https://tools.ietf.org/html/draft-ietf-quic-datagram-01">
     *     Datagram support.</a>
     * @param recvQueueLen  the RECV queue length.
     * @param sendQueueLen  the SEND queue length.
     * @return              the instance itself.
     */
    public B Datagram(int recvQueueLen, int sendQueueLen)
    {
        ObjectUtil.CheckPositive(recvQueueLen, "recvQueueLen");
        ObjectUtil.CheckPositive(sendQueueLen, "sendQueueLen");

        this.recvQueueLen = recvQueueLen;
        this.sendQueueLen = sendQueueLen;
        return Self();
    }

    /**
     * The {@link QuicSslContext} that will be used to create {@link QuicSslEngine}s for {@link QuicChannel}s.
     *
     * If you need a more flexible way to provide {@link QuicSslEngine}s use {@link #sslEngineProvider(Function)}.
     *
     * @param sslContext    the context.
     * @return              the instance itself.
     */
    public B SslContext(QuicSslContext sslContext)
    {
        if (server != sslContext.IsServer())
        {
            throw new ArgumentException("QuicSslContext.isServer() " + (sslContext.IsServer() ? "true" : "false")
                    + " isn't supported by this builder");
        }
        return SslEngineProvider(q => sslContext.NewEngine(q.Alloc()));
    }

    /**
     * The {@link Function} that will return the {@link QuicSslEngine} that should be used for the
     * {@link QuicChannel}.
     *
     * @param sslEngineProvider    the provider.
     * @return                      the instance itself.
     */
    public B SslEngineProvider(Func<IQuicChannel, QuicSslEngine> sslEngineProvider)
    {
        this.sslEngineProvider = sslEngineProvider;
        return Self();
    }

    /**
     * Allow to configure a {@link Executor} that will be used to run expensive SSL operations.
     *
     * @param sslTaskExecutor       the {@link Executor} that will be used to offload expensive SSL operations.
     * @return                      the instance itself.
     */
    public B SslTaskExecutor(IExecutor sslTaskExecutor)
    {
        this.sslTaskExecutor = sslTaskExecutor;
        return Self();
    }

    /**
     * Allows to configure the {@code active connect id limit} that should be used.
     *
     * @param limit     the limit to use.
     * @return          the instance itself.
     */
    public B ActiveConnectionIdLimit(long limit)
    {
        ObjectUtil.CheckPositive(limit, "limit");
        activeConnectionIdLimit = limit;
        return Self();
    }

    /**
     * Allows to configure the {@code active connect id limit} that should be used.
     *
     * @param token     the token to use.
     * @return          the instance itself.
     */
    public B StatelessResetToken(byte[] token)
    {
        if (token.Length != 16)
        {
            throw new ArgumentException("token must be 16 bytes but was " + token.Length);
        }

        this.statelessResetToken = (byte[]) token.Clone();
        return Self();
    }

    private MsQuicConfig CreateConfig()
    {
        return new MsQuicConfig(version, grease,
                maxIdleTimeout, maxSendUdpPayloadSize, maxRecvUdpPayloadSize, initialMaxData,
                initialMaxStreamDataBidiLocal, initialMaxStreamDataBidiRemote,
                initialMaxStreamDataUni, initialMaxStreamsBidi, initialMaxStreamsUni,
                ackDelayExponent, maxAckDelay, disableActiveMigration, enableHystart, discoverPmtu,
                congestionControlAlgorithm, initialCongestionWindowPackets, recvQueueLen, sendQueueLen,
                activeConnectionIdLimit, statelessResetToken);
    }

    /**
     * Validate the configuration before building the codec.
     */
    protected virtual void Validate()
    {
        if (sslEngineProvider == null)
        {
            throw new InvalidOperationException("sslEngineProvider can't be null");
        }
    }

    /**
     * Builds the QUIC codec that should be added to the {@link io.netty.channel.ChannelPipeline} of the underlying
     * {@link io.netty.channel.Channel} which is used as transport for QUIC.
     *
     * @return the {@link ChannelHandler} which acts as QUIC codec.
     */
    // CLR: the underlying Channel is a QuicEndpointChannel (see that class). config.free() on failure has no
    // counterpart: MsQuicConfig holds managed settings only.
    public IChannelHandler Build()
    {
        Validate();
        MsQuicConfig config = CreateConfig();
        return Build(config, sslEngineProvider, sslTaskExecutor, localConnIdLength, flushStrategy);
    }

    /**
     * Clone the builder
     *
     * @return the new instance that is a clone if this instance.
     */
    public abstract B Clone();

    /**
     * Builds the QUIC codec.
     *
     * @param config                the {@link QuicheConfig} that should be used.
     * @param sslContextProvider    the context provider
     * @param sslTaskExecutor       the {@link Executor} to use.
     * @param localConnIdLength     the local connection id length.
     * @param flushStrategy         the {@link FlushStrategy}  that should be used.
     * @return                      the {@link ChannelHandler} which acts as codec.
     */
    // CLR: QuicheConfig is MsQuicConfig (see the class comment); package-private abstract is internal abstract.
    internal abstract IChannelHandler Build(MsQuicConfig config,
                                            Func<IQuicChannel, QuicSslEngine> sslContextProvider,
                                            IExecutor sslTaskExecutor,
                                            int localConnIdLength, IFlushStrategy flushStrategy);

    // CLR-only helper: TimeUnit.toMillis(checkPositiveOrZero(amount, "amount")) of a TimeSpan.
    private static long ToMillis(TimeSpan amount, string name)
    {
        if (amount < TimeSpan.Zero)
        {
            throw new ArgumentException(name + " : " + amount + " (expected: >= 0)");
        }
        return amount.Ticks / TimeSpan.TicksPerMillisecond;
    }
}
