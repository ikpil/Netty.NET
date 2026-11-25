/*
 * Copyright 2018 The Netty Project
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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Utility class which allows easy writing of HTTP2 frames via {@link EmbeddedChannel#writeInbound(Object...)}.
 */
// CLR: the package-private final class is an internal sealed class with internal methods. ctx.newPromise() is a new
// TaskCompletionSource (the TaskCompletionSource that stands for a ChannelPromise, docs/codec-http-porting.md, Shared
// CLR adaptations) and future.syncUninterruptibly() is GetAwaiter().GetResult(), which waits and throws the failure
// ("The task completed in a Faulted state",
// https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.taskawaiter.getresult?view=net-10.0,
// as recorded in docs/codec-http-porting.md, Core test notes). The write methods take the Async suffix of the C#
// frame writer.
internal sealed class Http2FrameInboundWriter
{

    private readonly IChannelHandlerContext ctx;
    private readonly IHttp2FrameWriter writer;

    internal Http2FrameInboundWriter(EmbeddedChannel channel) : this(channel, new DefaultHttp2FrameWriter())
    {
    }

    internal Http2FrameInboundWriter(EmbeddedChannel channel, IHttp2FrameWriter writer)
    {
        ctx = new WriteInboundChannelHandlerContext(channel);
        this.writer = writer;
    }

    internal void WriteInboundData(int streamId, ByteBuf data, int padding, bool endStream)
    {
        writer.WriteDataAsync(ctx, streamId, data, padding, endStream, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundHeaders(int streamId, IHttp2Headers headers,
                         int padding, bool endStream)
    {
        writer.WriteHeadersAsync(ctx, streamId, headers, padding, endStream, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundHeaders(int streamId, IHttp2Headers headers,
                               int streamDependency, short weight, bool exclusive, int padding, bool endStream)
    {
        writer.WriteHeadersAsync(ctx, streamId, headers, streamDependency,
                weight, exclusive, padding, endStream, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundPriority(int streamId, int streamDependency,
                                short weight, bool exclusive)
    {
        writer.WritePriorityAsync(ctx, streamId, streamDependency, weight,
                exclusive, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundRstStream(int streamId, long errorCode)
    {
        writer.WriteRstStreamAsync(ctx, streamId, errorCode, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundSettings(Http2Settings settings)
    {
        writer.WriteSettingsAsync(ctx, settings, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundSettingsAck()
    {
        writer.WriteSettingsAckAsync(ctx, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundPing(bool ack, long data)
    {
        writer.WritePingAsync(ctx, ack, data, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WritePushPromise(int streamId, int promisedStreamId,
                                   IHttp2Headers headers, int padding)
    {
           writer.WritePushPromiseAsync(ctx, streamId, promisedStreamId,
                   headers, padding, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundGoAway(int lastStreamId, long errorCode, ByteBuf debugData)
    {
        writer.WriteGoAwayAsync(ctx, lastStreamId, errorCode, debugData, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundWindowUpdate(int streamId, int windowSizeIncrement)
    {
        writer.WriteWindowUpdateAsync(ctx, streamId, windowSizeIncrement, NewPromise()).GetAwaiter().GetResult();
    }

    internal void WriteInboundFrame(byte frameType, int streamId,
                             Http2Flags flags, ByteBuf payload)
    {
        writer.WriteFrameAsync(ctx, frameType, streamId, flags, payload, NewPromise()).GetAwaiter().GetResult();
    }

    // CLR: ctx.newPromise() of the context below, which returns channel.newPromise().
    private static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource();
    }

    // CLR: the C# IChannelHandlerContext has properties for channel()/executor()/name()/handler()/isRemoved()/
    // pipeline() and folds the ChannelPromise overloads into its Task-returning members (docs/transport-porting.md:
    // "The ChannelPromise overloads fold into the Task-returning members"); newPromise(), newProgressivePromise(),
    // newSucceededFuture(), newFailedFuture(Throwable) and voidPromise() are not members of the C# context, so the
    // promise overloads and those factories have no counterpart here. writeAndFlush(msg, promise) is
    // WriteAndFlushAsync(msg, CancellationToken), which completes a new TaskCompletionSource the same way; write(msg)
    // calls it as write(msg, newPromise()) does. channel.alloc() is the IChannel default member Alloc().
    private sealed class WriteInboundChannelHandlerContext : ChannelOutboundHandlerAdapter,
            IChannelHandlerContext
    {
        private readonly EmbeddedChannel channel;

        internal WriteInboundChannelHandlerContext(EmbeddedChannel channel)
        {
            this.channel = channel;
        }

        public IChannel Channel => channel;

        public IEventExecutor Executor => channel.EventLoop;

        public string Name => "WriteInbound";

        public IChannelHandler Handler => this;

        public bool IsRemoved => false;

        public IChannelInboundInvoker FireChannelRegistered()
        {
            channel.Pipeline.FireChannelRegistered();
            return this;
        }

        public IChannelInboundInvoker FireChannelUnregistered()
        {
            channel.Pipeline.FireChannelUnregistered();
            return this;
        }

        public IChannelInboundInvoker FireChannelActive()
        {
            channel.Pipeline.FireChannelActive();
            return this;
        }

        public IChannelInboundInvoker FireChannelInactive()
        {
            channel.Pipeline.FireChannelInactive();
            return this;
        }

        public IChannelInboundInvoker FireExceptionCaught(Exception cause)
        {
            channel.Pipeline.FireExceptionCaught(cause);
            return this;
        }

        public IChannelInboundInvoker FireUserEventTriggered(object evt)
        {
            channel.Pipeline.FireUserEventTriggered(evt);
            return this;
        }

        public IChannelInboundInvoker FireChannelRead(object msg)
        {
            channel.Pipeline.FireChannelRead(msg);
            return this;
        }

        public IChannelInboundInvoker FireChannelReadComplete()
        {
            channel.Pipeline.FireChannelReadComplete();
            return this;
        }

        public IChannelInboundInvoker FireChannelWritabilityChanged()
        {
            channel.Pipeline.FireChannelWritabilityChanged();
            return this;
        }

        public IChannelOutboundInvoker Read()
        {
            channel.Read();
            return this;
        }

        public IChannelOutboundInvoker Flush()
        {
            channel.Pipeline.FireChannelReadComplete();
            return this;
        }

        public IChannelPipeline Pipeline => channel.Pipeline;

        public IByteBufAllocator Alloc()
        {
            return ((IChannel) channel).Alloc();
        }

        public IAttribute<T> Attr<T>(AttributeKey<T> key) where T : class
        {
            return channel.Attr(key);
        }

        public bool HasAttr<T>(AttributeKey<T> key) where T : class
        {
            return channel.HasAttr(key);
        }

        public Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default)
        {
            return channel.BindAsync(localAddress, cancellationToken);
        }

        public Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null,
                CancellationToken cancellationToken = default)
        {
            return channel.ConnectAsync(remoteAddress, localAddress, cancellationToken);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return channel.DisconnectAsync(cancellationToken);
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            return channel.CloseAsync(cancellationToken);
        }

        public Task DeregisterAsync(CancellationToken cancellationToken = default)
        {
            return channel.DeregisterAsync(cancellationToken);
        }

        public Task WriteAsync(object msg, CancellationToken cancellationToken = default)
        {
            return WriteAndFlushAsync(msg, cancellationToken);
        }

        public Task WriteAndFlushAsync(object msg, CancellationToken cancellationToken = default)
        {
            TaskCompletionSource promise = NewPromise();
            try
            {
                channel.WriteInbound(msg);
                channel.RunPendingTasks();
                promise.SetResult();
            }
            catch (Exception cause)
            {
                promise.SetException(cause);
            }
            return promise.Task;
        }
    }
}
