/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Common.Internal.Logging;
namespace Netty.NET.Transport.Channels;

[Sharable]
/**
 * A special {@link ChannelInboundHandler} which offers an easy way to initialize a {@link Channel} once it was
 * registered to its {@link EventLoop}.
 *
 * Implementations are most often used in the context of {@link Bootstrap#handler(ChannelHandler)} ,
 * {@link ServerBootstrap#handler(ChannelHandler)} and {@link ServerBootstrap#childHandler(ChannelHandler)} to
 * setup the {@link ChannelPipeline} of a {@link Channel}.
 *
 * <pre>
 *
 * public class MyChannelInitializer extends {@link ChannelInitializer} {
 *     public void initChannel({@link Channel} channel) {
 *         channel.pipeline().addLast("myHandler", new MyHandler());
 *     }
 * }
 *
 * {@link ServerBootstrap} bootstrap = ...;
 * ...
 * bootstrap.childHandler(new MyChannelInitializer());
 * ...
 * </pre>
 * Be aware that this class is marked as {@link Sharable} and so the implementation must be safe to be re-used.
 *
 * @param <C>   A sub-type of {@link Channel}
 */
public abstract class ChannelInitializer<T> : ChannelInboundHandlerAdapter where T : IChannel
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(ChannelInitializer<>));
    // We use a Set as a ChannelInitializer is usually shared between all Channels in a Bootstrap /
    // ServerBootstrap. This way we can reduce the memory usage compared to use Attributes.
    private readonly HashSet<IChannelHandlerContext> _initializing = new();
    /**
     * This method will be called once the {@link Channel} was registered. After the method returns this instance
     * will be removed from the {@link ChannelPipeline} of the {@link Channel}.
     *
     * @param ch            the {@link Channel} which was registered.
     * @throws Exception    is thrown if an error occurs. In that case it will be handled by
     *                      {@link #exceptionCaught(ChannelHandlerContext, Throwable)} which will by default close
     *                      the {@link Channel}.
     */
    protected abstract void InitChannel(T channel);

    /**
     * {@inheritDoc} If override this method ensure you call super!
     */
    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        if (ctx.Channel.IsRegistered)
        {
            // This should always be true with our current DefaultChannelPipeline implementation.
            // The good thing about calling initChannel(...) in handlerAdded(...) is that there will be no ordering
            // surprises if a ChannelInitializer will add another ChannelInitializer. This is as all handlers
            // will be added in the expected order.
            if (Initialize(ctx))
            {
                // We are done with init the Channel, removing the initializer now.
                RemoveState(ctx);
            }
        }
    }

    public sealed override void ChannelRegistered(IChannelHandlerContext ctx)
    {
        // Normally this method will never be called as handlerAdded(...) should call initChannel(...) and remove
        // the handler.
        if (Initialize(ctx))
        {
            // we called initChannel(...) so we need to call now pipeline.fireChannelRegistered() to ensure we not
            // miss an event.
            ctx.Pipeline.FireChannelRegistered();
            // We are done with init the Channel, removing all the state for the Channel now.
            RemoveState(ctx);
        }
        else
        {
            // Called initChannel(...) before which is the expected behavior, so just forward the event.
            ctx.FireChannelRegistered();
        }
    }

    private bool Initialize(IChannelHandlerContext ctx)
    {
        lock (_initializing)
            // Guard against re-entrance.
            if (!_initializing.Add(ctx))
                return false;

        try
        {
            InitChannel((T)ctx.Channel);
        }
        catch (Exception error)
        {
            // Explicitly route the failure into the pipeline. Re-entrance is guarded by
            // the initMap.add(ctx) check above; the finally block below removes the handler.
            ExceptionCaught(ctx, error);
        }
        finally
        {
            if (!ctx.IsRemoved)
                ctx.Pipeline.Remove(this);
        }

        return true;
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        lock (_initializing)
            _initializing.Remove(ctx);
    }

    private void RemoveState(IChannelHandlerContext ctx)
    {
        // The removal may happen in an async fashion if the EventExecutor we use does something funky.
        if (ctx.IsRemoved)
        {
            lock (_initializing)
            {
                _initializing.Remove(ctx);
            }
        }
        else
        {
            // The context is not removed yet which is most likely the case because a custom EventExecutor is used.
            // Let's schedule it on the EventExecutor to give it some more time to be completed in case it is offloaded.
            ctx.Executor.Execute(() => {
                lock (_initializing)
                {
                    _initializing.Remove(ctx);
                }
            });
        }
    }

    /**
     * Handle the {@link Throwable} by logging and closing the {@link Channel}. Sub-classes may override this.
     */
    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (Logger.IsWarnEnabled())
            Logger.Warn("Failed to initialize a channel. Closing: " + ctx.Channel, cause);

        _ = ctx.CloseAsync();
    }
}
