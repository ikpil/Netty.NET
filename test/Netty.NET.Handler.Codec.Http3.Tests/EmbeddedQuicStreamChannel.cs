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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the package-private final class is internal sealed; it implements codec-quic's IQuicStreamChannel over
// Transport's EmbeddedChannel, as the Java class implements QuicStreamChannel over EmbeddedChannel. The
// QuicStreamChannel API shapes are those of docs/codec-classes-quic-porting.md: shutdown(int, promise),
// shutdownInput(int, promise) and shutdownOutput(int, promise) are ShutdownAsync(int), ShutdownInputAsync(int) and
// ShutdownOutputAsync(int) returning a completed Task (promise.setSuccess()), shutdown(promise)/shutdownOutput(promise)
// are IDuplexChannel's ShutdownAsync()/ShutdownOutputAsync(), the default shutdownInput() that
// writeInboundWithFin/writeInboundFin call is IQuicStreamChannel's default implementation of
// IDuplexChannel.ShutdownInputAsync() (ShutdownInputAsync(0)), reached through the interface, and
// updatePriority(priority, promise) returns a faulted Task (promise.setFailure) with NotSupportedException for
// UnsupportedOperationException. isInputShutdown(), isOutputShutdown() and isShutdown() are IDuplexChannel's
// properties. localAddress()/remoteAddress() override the LocalAddress/RemoteAddress properties (null, as in Java);
// parent(), flush() and read() only narrow the return type (QuicChannel, QuicStreamChannel), so AbstractChannel's
// Parent and EmbeddedChannel's Flush()/Read() are inherited unchanged (IQuicStreamChannel does not re-declare them).
// config() lazily wraps super.config(): AbstractChannel.Config already creates the config lazily through NewConfig(),
// so NewConfig() returns the EmbeddedQuicStreamChannelConfig around EmbeddedChannel's config. AttributeKey<T> needs a
// reference type, so the Long, QuicStreamType and Boolean attributes are AttributeKey<object> holding the boxed value
// (docs/codec-http3-porting.md, Shared CLR adaptations); valueOf(String) is AttributeKey<T>.ValueOf(string). The
// Integer shutdown error codes are int? (null until shut down). The wrapping Unsafe of unsafe() is one override:
// Channel.Unsafe is folded into AbstractChannel (docs/transport-porting.md), so every delegating method of the
// anonymous Unsafe is the inherited member, and its write(msg, promise) is WriteTransportAsync(msg, cancellationToken),
// whose Task is the promise: promise.unvoid().addListener(f -> outputShutdown = 0) is a continuation of that Task
// (ExecuteSynchronously, so it runs when the write completes), and hasFin() is read before the write, which may release
// the frame. DefaultChannelId.newInstance() is DefaultChannelId.NewInstance(); @Nullable (org.jetbrains.annotations)
// has no counterpart.
internal sealed class EmbeddedQuicStreamChannel : EmbeddedChannel, IQuicStreamChannel
{
    private static readonly AttributeKey<object> streamIdKey =
            AttributeKey<object>.ValueOf("embedded_channel_stream_id");
    private static readonly AttributeKey<object> streamTypeKey =
            AttributeKey<object>.ValueOf("embedded_channel_stream_type");
    private static readonly AttributeKey<object> localCreatedKey =
            AttributeKey<object>.ValueOf("embedded_channel_stream_local_created");
    private int? inputShutdown;
    private int? outputShutdown;

    internal EmbeddedQuicStreamChannel(params IChannelHandler[] handlers)
        : this(null, false, QuicStreamType.BIDIRECTIONAL, 0, handlers)
    {
    }

    internal EmbeddedQuicStreamChannel(IQuicChannel parent, bool localCreated, QuicStreamType type,
                                       long id, params IChannelHandler[] handlers)
        : base(parent, DefaultChannelId.NewInstance(), true, false,
                EmbeddedQuicChannel.PrependChannelConsumer(channel =>
                {
                    channel.Attr(streamIdKey).Set(id);
                    channel.Attr(streamTypeKey).Set(type);
                    channel.Attr(localCreatedKey).Set(localCreated);
                }, handlers))
    {
    }

    internal bool WriteInboundWithFin(params object[] msgs)
    {
        _ = ((IDuplexChannel) this).ShutdownInputAsync();
        bool written = WriteInbound(msgs);
        FireInputShutdownEvents();
        return written;
    }

    internal void WriteInboundFin()
    {
        _ = ((IDuplexChannel) this).ShutdownInputAsync();
        FireInputShutdownEvents();
    }

    private void FireInputShutdownEvents()
    {
        Pipeline.FireUserEventTriggered(ChannelInputShutdownEvent.INSTANCE);
        Pipeline.FireUserEventTriggered(ChannelInputShutdownReadComplete.INSTANCE);
    }

    public QuicStreamPriority Priority()
    {
        return null;
    }

    public Task UpdatePriorityAsync(QuicStreamPriority priority, CancellationToken cancellationToken = default)
    {
        return Task.FromException(new NotSupportedException());
    }

    public override EndPoint LocalAddress => null;

    public override EndPoint RemoteAddress => null;

    // CLR: config() (see the type comment).
    protected override IChannelConfig NewConfig()
    {
        return new EmbeddedQuicStreamChannelConfig(base.NewConfig());
    }

    public bool IsLocalCreated()
    {
        return (bool) Attr(localCreatedKey).Get();
    }

    public QuicStreamType Type()
    {
        return (QuicStreamType) Attr(streamTypeKey).Get();
    }

    public long StreamId()
    {
        return (long) Attr(streamIdKey).Get();
    }

    public bool IsInputShutdown => inputShutdown != null;

    public bool IsOutputShutdown => outputShutdown != null;

    public Task ShutdownAsync(int i)
    {
        if (inputShutdown == null)
        {
            inputShutdown = i;
        }
        if (outputShutdown == null)
        {
            outputShutdown = i;
        }
        return Task.CompletedTask;
    }

    public Task ShutdownInputAsync(int i)
    {
        if (inputShutdown == null)
        {
            inputShutdown = i;
        }
        return Task.CompletedTask;
    }

    public Task ShutdownOutputAsync(int i)
    {
        if (outputShutdown == null)
        {
            outputShutdown = i;
        }
        return Task.CompletedTask;
    }

    public bool IsShutdown => IsInputShutdown && IsOutputShutdown;

    public Task ShutdownAsync()
    {
        return ShutdownAsync(0);
    }

    public Task ShutdownOutputAsync()
    {
        return ShutdownOutputAsync(0);
    }

    internal int? OutputShutdownError()
    {
        return outputShutdown;
    }

    internal int? InputShutdownError()
    {
        return inputShutdown;
    }

    // CLR: the write(msg, promise) of the wrapping Unsafe of unsafe() (see the type comment).
    protected override Task WriteTransportAsync(object msg, CancellationToken cancellationToken)
    {
        bool fin = msg is IQuicStreamFrame && ((IQuicStreamFrame) msg).HasFin();
        Task promise = base.WriteTransportAsync(msg, cancellationToken);
        if (fin)
        {
            // Mimic the API.
            _ = promise.ContinueWith(f => outputShutdown = 0, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return promise;
    }

    // CLR: the QuicStreamChannelConfig of config(). Every setter returns this as the IChannelConfig of the C# interface
    // (it does not narrow inherited return types, see IQuicStreamChannelConfig), setAllowHalfClosure returns it as
    // IDuplexChannelConfig; getOption(option) is TryGetOption(option, out value), and the CLR-only WriteProgress
    // property is delegated too. getMaxMessagesPerRead/setMaxMessagesPerRead are not @Deprecated here (they are on
    // ChannelConfig, [Obsolete] on IChannelConfig), so their delegating calls compile with a CS0612 suppression, as
    // javac compiles the Java calls with a deprecation warning.
    private sealed class EmbeddedQuicStreamChannelConfig : IQuicStreamChannelConfig
    {
        private readonly IChannelConfig config;
        private bool allowHalfClosure;

        internal EmbeddedQuicStreamChannelConfig(IChannelConfig config)
        {
            this.config = config;
        }

        public IQuicStreamChannelConfig SetReadFrames(bool readFrames)
        {
            return this;
        }

        public bool IsReadFrames()
        {
            return false;
        }

        public IDuplexChannelConfig SetAllowHalfClosure(bool allowHalfClosure)
        {
            this.allowHalfClosure = allowHalfClosure;
            return this;
        }

#pragma warning disable CS0612
        public IChannelConfig SetMaxMessagesPerRead(int maxMessagesPerRead)
        {
            config.SetMaxMessagesPerRead(maxMessagesPerRead);
            return this;
        }
#pragma warning restore CS0612

        public IChannelConfig SetWriteSpinCount(int writeSpinCount)
        {
            config.SetWriteSpinCount(writeSpinCount);
            return this;
        }

        public IChannelConfig SetAllocator(IByteBufAllocator allocator)
        {
            config.SetAllocator(allocator);
            return this;
        }

        public IChannelConfig SetRecvByteBufAllocator(IRecvByteBufAllocator allocator)
        {
            config.SetRecvByteBufAllocator(allocator);
            return this;
        }

        public IChannelConfig SetAutoRead(bool autoRead)
        {
            config.SetAutoRead(autoRead);
            return this;
        }

        public IChannelConfig SetAutoClose(bool autoClose)
        {
            config.SetAutoClose(autoClose);
            return this;
        }

        public IChannelConfig SetMessageSizeEstimator(IMessageSizeEstimator estimator)
        {
            config.SetMessageSizeEstimator(estimator);
            return this;
        }

        public IChannelConfig SetWriteBufferWaterMark(WriteBufferWaterMark writeBufferWaterMark)
        {
            config.SetWriteBufferWaterMark(writeBufferWaterMark);
            return this;
        }

        public IChannelConfig SetConnectTimeoutMillis(int connectTimeoutMillis)
        {
            config.SetConnectTimeoutMillis(connectTimeoutMillis);
            return this;
        }

        public IChannelConfig SetWriteBufferHighWaterMark(int writeBufferHighWaterMark)
        {
            config.SetWriteBufferHighWaterMark(writeBufferHighWaterMark);
            return this;
        }

        public IChannelConfig SetWriteBufferLowWaterMark(int writeBufferLowWaterMark)
        {
            config.SetWriteBufferLowWaterMark(writeBufferLowWaterMark);
            return this;
        }

        public bool IsAllowHalfClosure()
        {
            return allowHalfClosure;
        }

        public IProgress<ChannelWriteProgress> WriteProgress
        {
            get => config.WriteProgress;
            set => config.WriteProgress = value;
        }

        public IReadOnlyDictionary<ChannelOption, object> GetOptions()
        {
            return config.GetOptions();
        }

        public bool SetOptions(IEnumerable<KeyValuePair<ChannelOption, object>> options)
        {
            return config.SetOptions(options);
        }

        public bool TryGetOption<T>(ChannelOption<T> option, out T value)
        {
            return config.TryGetOption(option, out value);
        }

        public bool SetOption<T>(ChannelOption<T> option, T value)
        {
            return config.SetOption(option, value);
        }

        public int GetConnectTimeoutMillis()
        {
            return config.GetConnectTimeoutMillis();
        }

#pragma warning disable CS0612
        public int GetMaxMessagesPerRead()
        {
            return config.GetMaxMessagesPerRead();
        }
#pragma warning restore CS0612

        public int GetWriteSpinCount()
        {
            return config.GetWriteSpinCount();
        }

        public IByteBufAllocator GetAllocator()
        {
            return config.GetAllocator();
        }

        public IRecvByteBufAllocator GetRecvByteBufAllocator()
        {
            return config.GetRecvByteBufAllocator();
        }

        public bool IsAutoRead()
        {
            return config.IsAutoRead();
        }

        public bool IsAutoClose()
        {
            return config.IsAutoClose();
        }

        public int GetWriteBufferHighWaterMark()
        {
            return config.GetWriteBufferHighWaterMark();
        }

        public int GetWriteBufferLowWaterMark()
        {
            return config.GetWriteBufferLowWaterMark();
        }

        public IMessageSizeEstimator GetMessageSizeEstimator()
        {
            return config.GetMessageSizeEstimator();
        }

        public WriteBufferWaterMark GetWriteBufferWaterMark()
        {
            return config.GetWriteBufferWaterMark();
        }
    }
}
