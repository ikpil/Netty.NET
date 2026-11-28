/*
 * Copyright 2021 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: The QuicChannel API shapes are those of docs/codec-classes-quic-porting.md: createStream(type, handler,
// promise) is CreateStreamAsync returning a completed Task<IQuicStreamChannel> (promise.setSuccess),
// close(applicationClose, error, reason, promise) is CloseAsync(bool, int, ByteBuf, CancellationToken), and
// collectStats/collectPathStats return a faulted Task (promise.setFailure) with NotSupportedException for
// UnsupportedOperationException. localAddress()/remoteAddress() override the LocalAddress/RemoteAddress properties
// (null, as in Java); flush() and read() only narrow the return type to QuicChannel, so EmbeddedChannel's
// Flush()/Read() are inherited unchanged (IQuicChannel does not re-declare them). config() lazily wraps
// super.config(): AbstractChannel.Config already creates the config lazily through NewConfig(), so NewConfig()
// returns the EmbeddedQuicChannelConfig around EmbeddedChannel's config (created once, as the Java field).
// AttributeKey<T> needs a reference type, so the AtomicLong stream id generator is a StrongBox<long> updated with
// Interlocked.Add (getAndAdd(2) is the new value minus 2). EnumMap<QuicStreamType, Long> is a
// Dictionary<QuicStreamType, long>, AtomicBoolean an int with Interlocked.CompareExchange,
// ConcurrentLinkedQueue<Integer> a ConcurrentQueue<int>, and unmodifiableCollection the queue as an
// IReadOnlyCollection<int> (a read-only view). The listener promise.addListener(__ -> reason.release()) is a
// continuation of the close Task (ExecuteSynchronously, so it runs when the close completes). Consumer<Channel> is
// Action<IChannel>, and the anonymous ChannelInboundHandlerAdapter of prependChannelConsumer the private nested
// class ChannelConsumerHandler; System.arraycopy is Array.Copy. SSLEngine is QuicSslEngine and SocketAddress
// EndPoint. @Nullable (org.jetbrains.annotations) has no counterpart.
internal sealed class EmbeddedQuicChannel : EmbeddedChannel, IQuicChannel
{
    private static readonly AttributeKey<StrongBox<long>> streamIdGeneratorKey =
            AttributeKey<StrongBox<long>>.ValueOf("embedded_channel_stream_id_generator");
    private readonly Dictionary<QuicStreamType, long> peerAllowedStreams = new Dictionary<QuicStreamType, long>();
    private int closed;
    private readonly ConcurrentQueue<int> closeErrorCodes = new ConcurrentQueue<int>();

    /**
     * TWO bits reserved for Variable-Length Integer Encoding
     * <a href="https://datatracker.ietf.org/doc/html/rfc9000?#name-variable-length-integer-enc">rfc9000</a>
     * TWO LSB used for distinguish Client/Server initiated stream & Bi/unidirection
     * these are not reserved but part of it
     * <a href="https://datatracker.ietf.org/doc/html/rfc9000?#name-stream-types-and-identifier">rfc9000</a>
     * so we can max stream per stream type (bi/uni) = (2^62-1)/2
     */
    private const long MAX_PEER_STREAMS_PER_STREAM_TYPE = ((1L << 62) - 1) / 2;

    internal EmbeddedQuicChannel(bool server) : this(server, new IChannelHandler[0])
    {
    }

    internal EmbeddedQuicChannel(bool server, params IChannelHandler[] handlers)
        : base(PrependChannelConsumer(
                channel => channel.Attr(streamIdGeneratorKey).Set(new StrongBox<long>(server ? 1 : 0)), handlers))
    {
    }

    internal static IChannelHandler[] PrependChannelConsumer(Action<IChannel> channelConsumer,
                                                            params IChannelHandler[] handlers)
    {
        IChannelHandler[] toReturn = new IChannelHandler[handlers.Length + 1];
        toReturn[0] = new ChannelConsumerHandler(channelConsumer);
        Array.Copy(handlers, 0, toReturn, 1, handlers.Length);
        return toReturn;
    }

    private sealed class ChannelConsumerHandler : ChannelInboundHandlerAdapter
    {
        private readonly Action<IChannel> channelConsumer;

        internal ChannelConsumerHandler(Action<IChannel> channelConsumer)
        {
            this.channelConsumer = channelConsumer;
        }

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            channelConsumer(ctx.Channel);
            base.HandlerAdded(ctx);
        }
    }

    public override EndPoint LocalAddress => null;

    public override EndPoint RemoteAddress => null;

    public EndPoint LocalSocketAddress()
    {
        return null;
    }

    public EndPoint RemoteSocketAddress()
    {
        return null;
    }

    public bool IsTimedOut()
    {
        return false;
    }

    public QuicSslEngine SslEngine()
    {
        return null;
    }

    // CLR: config() (see the type comment).
    protected override IChannelConfig NewConfig()
    {
        return new EmbeddedQuicChannelConfig(base.NewConfig());
    }

    public long PeerAllowedStreams(QuicStreamType type)
    {
        return peerAllowedStreams.TryGetValue(type, out long value) ? value : MAX_PEER_STREAMS_PER_STREAM_TYPE;
    }

    public void PeerAllowedStreams(QuicStreamType type, long peerAllowedStreams)
    {
        if (peerAllowedStreams > MAX_PEER_STREAMS_PER_STREAM_TYPE)
        {
            peerAllowedStreams = MAX_PEER_STREAMS_PER_STREAM_TYPE;
        }

        this.peerAllowedStreams[type] = peerAllowedStreams;
    }

    public Task<IQuicStreamChannel> CreateStreamAsync(QuicStreamType type, IChannelHandler handler,
                                                      CancellationToken cancellationToken = default)
    {
        StrongBox<long> streamIdGenerator = Attr(streamIdGeneratorKey).Get();
        return Task.FromResult<IQuicStreamChannel>(new EmbeddedQuicStreamChannel(this, true, type,
                Interlocked.Add(ref streamIdGenerator.Value, 2) - 2, handler));
    }

    public Task CloseAsync(bool applicationClose, int error, ByteBuf reason,
                           CancellationToken cancellationToken = default)
    {
        closeErrorCodes.Enqueue(error);
        bool first = Interlocked.CompareExchange(ref closed, 1, 0) == 0;
        if (!first)
        {
            reason.Release();
        }

        Task future = CloseAsync(cancellationToken);

        if (first)
        {
            _ = future.ContinueWith(_ => reason.Release(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        return future;
    }

    public Task<IQuicConnectionStats> CollectStatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromException<IQuicConnectionStats>(
                new NotSupportedException("Collect stats not supported for embedded channel."));
    }

    public Task<IQuicConnectionPathStats> CollectPathStatsAsync(int i, CancellationToken cancellationToken = default)
    {
        return Task.FromException<IQuicConnectionPathStats>(
                new NotSupportedException("Collect path stats not supported for embedded channel."));
    }

    public EmbeddedQuicStreamChannel LocalControlStream()
    {
        return (EmbeddedQuicStreamChannel) Http3.GetLocalControlStream(this);
    }

    public IQuicTransportParameters PeerTransportParameters()
    {
        return null;
    }

    internal IReadOnlyCollection<int> CloseErrorCodes()
    {
        return closeErrorCodes;
    }

    // CLR: the delegating QuicChannelConfig of config(). Every setter returns this as the IChannelConfig of the C#
    // interface (it does not narrow inherited return types, see IQuicChannelConfig); getOption(option) is
    // TryGetOption(option, out value), and the CLR-only WriteProgress property is delegated too. The @Deprecated
    // getMaxMessagesPerRead/setMaxMessagesPerRead are [Obsolete], as on IChannelConfig.
    private sealed class EmbeddedQuicChannelConfig : IQuicChannelConfig
    {
        private readonly IChannelConfig @delegate;

        internal EmbeddedQuicChannelConfig(IChannelConfig @delegate)
        {
            this.@delegate = @delegate;
        }

        public IProgress<ChannelWriteProgress> WriteProgress
        {
            get => @delegate.WriteProgress;
            set => @delegate.WriteProgress = value;
        }

        public IReadOnlyDictionary<ChannelOption, object> GetOptions()
        {
            return @delegate.GetOptions();
        }

        public bool SetOptions(IEnumerable<KeyValuePair<ChannelOption, object>> map)
        {
            return @delegate.SetOptions(map);
        }

        public bool TryGetOption<T>(ChannelOption<T> channelOption, out T value)
        {
            return @delegate.TryGetOption(channelOption, out value);
        }

        public bool SetOption<T>(ChannelOption<T> channelOption, T t)
        {
            return @delegate.SetOption(channelOption, t);
        }

        public int GetConnectTimeoutMillis()
        {
            return @delegate.GetConnectTimeoutMillis();
        }

        public IChannelConfig SetConnectTimeoutMillis(int i)
        {
            @delegate.SetConnectTimeoutMillis(i);
            return this;
        }

        [Obsolete]
        public int GetMaxMessagesPerRead()
        {
            return @delegate.GetMaxMessagesPerRead();
        }

        [Obsolete]
        public IChannelConfig SetMaxMessagesPerRead(int i)
        {
            @delegate.SetMaxMessagesPerRead(i);
            return this;
        }

        public int GetWriteSpinCount()
        {
            return @delegate.GetWriteSpinCount();
        }

        public IChannelConfig SetWriteSpinCount(int i)
        {
            @delegate.SetWriteSpinCount(i);
            return this;
        }

        public IByteBufAllocator GetAllocator()
        {
            return @delegate.GetAllocator();
        }

        public IChannelConfig SetAllocator(IByteBufAllocator byteBufAllocator)
        {
            @delegate.SetAllocator(byteBufAllocator);
            return this;
        }

        public IRecvByteBufAllocator GetRecvByteBufAllocator()
        {
            return @delegate.GetRecvByteBufAllocator();
        }

        public IChannelConfig SetRecvByteBufAllocator(IRecvByteBufAllocator recvByteBufAllocator)
        {
            @delegate.SetRecvByteBufAllocator(recvByteBufAllocator);
            return this;
        }

        public bool IsAutoRead()
        {
            return @delegate.IsAutoRead();
        }

        public IChannelConfig SetAutoRead(bool b)
        {
            @delegate.SetAutoRead(b);
            return this;
        }

        public bool IsAutoClose()
        {
            return @delegate.IsAutoClose();
        }

        public IChannelConfig SetAutoClose(bool b)
        {
            @delegate.SetAutoClose(b);
            return this;
        }

        public int GetWriteBufferHighWaterMark()
        {
            return @delegate.GetWriteBufferHighWaterMark();
        }

        public IChannelConfig SetWriteBufferHighWaterMark(int i)
        {
            @delegate.SetWriteBufferHighWaterMark(i);
            return this;
        }

        public int GetWriteBufferLowWaterMark()
        {
            return @delegate.GetWriteBufferLowWaterMark();
        }

        public IChannelConfig SetWriteBufferLowWaterMark(int i)
        {
            @delegate.SetWriteBufferLowWaterMark(i);
            return this;
        }

        public IMessageSizeEstimator GetMessageSizeEstimator()
        {
            return @delegate.GetMessageSizeEstimator();
        }

        public IChannelConfig SetMessageSizeEstimator(IMessageSizeEstimator messageSizeEstimator)
        {
            @delegate.SetMessageSizeEstimator(messageSizeEstimator);
            return this;
        }

        public WriteBufferWaterMark GetWriteBufferWaterMark()
        {
            return @delegate.GetWriteBufferWaterMark();
        }

        public IChannelConfig SetWriteBufferWaterMark(WriteBufferWaterMark writeBufferWaterMark)
        {
            @delegate.SetWriteBufferWaterMark(writeBufferWaterMark);
            return this;
        }
    }
}
