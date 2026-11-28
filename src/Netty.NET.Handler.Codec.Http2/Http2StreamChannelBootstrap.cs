/*
 * Copyright 2017 The Netty Project
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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;


// CLR: Future<Http2StreamChannel> and Promise<Http2StreamChannel> are Task<IHttp2StreamChannel> and
// TaskCompletionSource<IHttp2StreamChannel>, so open() and open(Promise) take the Async suffix;
// channel.eventLoop().newPromise() is a TaskCompletionSource without asynchronous continuations
// (Http2CodecUtil.NewPromise), promise.setUncancellable() is a completion check, promise.cancel(false)
// TrySetCanceled() and f.isCancelled() IsCanceled. The options and attributes are kept as the Transport's
// Bootstrap keeps them (src/Netty.NET.Transport/Bootstrapping/Bootstrap.cs): an OrderedDictionary under a lock for
// the LinkedHashMap, and a ConcurrentDictionary of typed setters for the attributes (channel.attr(key).set(value)
// needs the generic key); an option is set through IChannelConfig.SetOptions, the untyped setOption. The
// deprecated Http2MultiplexCodec is not ported (its @deprecated text names Http2FrameCodecBuilder and
// Http2MultiplexHandler), so findCtx looks for Http2MultiplexHandler only and the error text names
// Http2MultiplexCodec as a literal. The @Deprecated open0(ChannelHandlerContext, Promise) names its replacements
// open() and open(Promise) and is not ported as a public member; its body is the private Open0 that OpenAsync
// runs. Channel.unsafe().closeForcibly() is AbstractHttp2StreamChannel.Unsafe().CloseForcibly() (the channel's
// internal Http2ChannelUnsafe; Channel.Unsafe is folded into the channel, docs/transport-porting.md). Java assert
// is Debug.Assert.
public sealed class Http2StreamChannelBootstrap
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(Http2StreamChannelBootstrap));
    // CLR: EMPTY_OPTION_ARRAY and EMPTY_ATTRIBUTE_ARRAY only typed the toArray(...) calls; ToArray() needs none.

    // The order in which ChannelOptions are applied is important they may depend on each other for validation
    // purposes.
    private readonly OrderedDictionary<ChannelOption, object> options = new OrderedDictionary<ChannelOption, object>();
    private readonly ConcurrentDictionary<IAttributeKey, Action<IChannel>> attrs =
            new ConcurrentDictionary<IAttributeKey, Action<IChannel>>();
    private readonly IChannel channel;
    private volatile IChannelHandler handler;

    // Cache the ChannelHandlerContext to speed up open(...) operations.
    private volatile IChannelHandlerContext multiplexCtx;

    public Http2StreamChannelBootstrap(IChannel channel)
    {
        this.channel = ObjectUtil.CheckNotNull(channel, "channel");
    }

    /**
     * Allow to specify a {@link ChannelOption} which is used for the {@link Http2StreamChannel} instances once they got
     * created. Use a value of {@code null} to remove a previous set {@link ChannelOption}.
     */
    public Http2StreamChannelBootstrap Option<T>(ChannelOption<T> option, T value)
    {
        ObjectUtil.CheckNotNull(option, "option");

        lock (options)
        {
            if (value == null)
            {
                options.Remove(option);
            }
            else
            {
                options[option] = value;
            }
        }

        return this;
    }

    /**
     * Allow to specify an initial attribute of the newly created {@link Http2StreamChannel}.  If the {@code value} is
     * {@code null}, the attribute of the specified {@code key} is removed.
     */
    public Http2StreamChannelBootstrap Attr<T>(AttributeKey<T> key, T value) where T : class
    {
        ObjectUtil.CheckNotNull(key, "key");

        if (value == null)
        {
            attrs.TryRemove(key, out _);
        }
        else
        {
            attrs[key] = ch => ch.Attr(key).Set(value);
        }

        return this;
    }

    /**
     * the {@link ChannelHandler} to use for serving the requests.
     */
    public Http2StreamChannelBootstrap Handler(IChannelHandler handler)
    {
        this.handler = ObjectUtil.CheckNotNull(handler, "handler");
        return this;
    }

    /**
     * Open a new {@link Http2StreamChannel} to use.
     * @return the {@link Future} that will be notified once the channel was opened successfully or it failed.
     */
    public Task<IHttp2StreamChannel> OpenAsync()
    {
        return OpenAsync(new TaskCompletionSource<IHttp2StreamChannel>());
    }

    /**
     * Open a new {@link Http2StreamChannel} to use and notifies the given {@link Promise}.
     * @return the {@link Future} that will be notified once the channel was opened successfully or it failed.
     */
    public Task<IHttp2StreamChannel> OpenAsync(TaskCompletionSource<IHttp2StreamChannel> promise)
    {
        try
        {
            IChannelHandlerContext ctx = FindCtx();
            IEventExecutor executor = ctx.Executor;
            if (executor.InEventLoop())
            {
                Open0(ctx, promise);
            }
            else
            {
                IChannelHandlerContext finalCtx = ctx;
                executor.Execute(() =>
                {
                    if (channel.IsActive)
                    {
                        Open0(finalCtx, promise);
                    }
                    else
                    {
                        promise.SetException(new ClosedChannelException());
                    }
                });
            }
        }
        catch (Exception cause)
        {
            promise.SetException(cause);
        }

        return promise.Task;
    }

    private IChannelHandlerContext FindCtx()
    {
        // First try to use cached context and if this not work lets try to lookup the context.
        IChannelHandlerContext ctx = multiplexCtx;
        if (ctx != null && !ctx.IsRemoved)
        {
            return ctx;
        }

        IChannelPipeline pipeline = channel.Pipeline;
        ctx = pipeline.Context(typeof(Http2MultiplexHandler));
        if (ctx == null)
        {
            if (channel.IsActive)
            {
                throw new InvalidOperationException("Http2MultiplexCodec" + " or "
                        + StringUtil.SimpleClassName(typeof(Http2MultiplexHandler))
                        + " must be in the ChannelPipeline of Channel " + channel);
            }
            else
            {
                throw new ClosedChannelException();
            }
        }

        multiplexCtx = ctx;
        return ctx;
    }

    private void Open0(IChannelHandlerContext ctx, TaskCompletionSource<IHttp2StreamChannel> promise)
    {
        Debug.Assert(ctx.Executor.InEventLoop());
        if (promise.Task.IsCompleted)
        {
            return;
        }

        IHttp2StreamChannel streamChannel;

        try
        {
            streamChannel = ((Http2MultiplexHandler) ctx.Handler).NewOutboundStream();
        }
        catch (Exception e)
        {
            promise.SetException(e);
            return;
        }

        try
        {
            Init(streamChannel);
        }
        catch (Exception e)
        {
            ((AbstractHttp2StreamChannel) streamChannel).Unsafe().CloseForcibly();
            promise.SetException(e);
            return;
        }

        Task future = ctx.Channel.EventLoop.RegisterAsync(streamChannel);
        Http2CodecUtil.AddListener(ctx, future, f =>
        {
            if (f.IsCompletedSuccessfully)
            {
                promise.SetResult(streamChannel);
            }
            else if (f.IsCanceled)
            {
                promise.TrySetCanceled();
            }
            else
            {
                if (streamChannel.IsRegistered)
                {
                    _ = streamChannel.CloseAsync();
                }
                else
                {
                    ((AbstractHttp2StreamChannel) streamChannel).Unsafe().CloseForcibly();
                }

                promise.SetException(Http2CodecUtil.Cause(f));
            }
        });
    }

    private void Init(IChannel channel)
    {
        IChannelPipeline p = channel.Pipeline;
        IChannelHandler handler = this.handler;
        if (handler != null)
        {
            p.AddLast(handler);
        }

        KeyValuePair<ChannelOption, object>[] optionArray;

        lock (options)
        {
            optionArray = options.ToArray();
        }

        SetChannelOptions(channel, optionArray);
        SetAttributes(channel, attrs.Values.ToArray());
    }

    private static void SetChannelOptions(
            IChannel channel, KeyValuePair<ChannelOption, object>[] options)
    {
        foreach (KeyValuePair<ChannelOption, object> e in options)
        {
            SetChannelOption(channel, e.Key, e.Value);
        }
    }

    private static void SetChannelOption(
            IChannel channel, ChannelOption option, object value)
    {
        try
        {
            if (!channel.Config.SetOptions(new[] { new KeyValuePair<ChannelOption, object>(option, value) }))
            {
                logger.Warn("{} Unknown channel option '{}'", channel, option);
            }
        }
        catch (Exception t)
        {
            logger.Warn("{} Failed to set channel option '{}' with value '{}'", channel, option, value, t);
        }
    }

    private static void SetAttributes(
            IChannel channel, Action<IChannel>[] options)
    {
        foreach (Action<IChannel> e in options)
        {
            e(channel);
        }
    }
}
