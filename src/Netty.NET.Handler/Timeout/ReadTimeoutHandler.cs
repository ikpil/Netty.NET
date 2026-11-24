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
using System.Diagnostics;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Timeout;

/**
 * Raises a {@link ReadTimeoutException} when no data was read within a certain
 * period of time.
 *
 * <pre>
 * // The connection is closed when there is no inbound traffic
 * // for 30 seconds.
 *
 * public class MyChannelInitializer extends {@link ChannelInitializer}&lt;{@link Channel}&gt; {
 *     public void initChannel({@link Channel} channel) {
 *         channel.pipeline().addLast("readTimeoutHandler", new {@link ReadTimeoutHandler}(30));
 *         channel.pipeline().addLast("myHandler", new MyHandler());
 *     }
 * }
 *
 * // Handler should handle the {@link ReadTimeoutException}.
 * public class MyHandler extends {@link ChannelDuplexHandler} {
 *     {@code @Override}
 *     public void exceptionCaught({@link ChannelHandlerContext} ctx, {@link Throwable} cause)
 *             throws {@link Exception} {
 *         if (cause instanceof {@link ReadTimeoutException}) {
 *             // do something
 *         } else {
 *             super.exceptionCaught(ctx, cause);
 *         }
 *     }
 * }
 *
 * {@link ServerBootstrap} bootstrap = ...;
 * ...
 * bootstrap.childHandler(new MyChannelInitializer());
 * ...
 * </pre>
 * @see WriteTimeoutHandler
 * @see IdleStateHandler
 */
public class ReadTimeoutHandler : IdleStateHandler
{
    private bool closed;

    /**
     * Creates a new instance.
     *
     * @param timeoutSeconds
     *        read timeout in seconds
     */
    public ReadTimeoutHandler(int timeoutSeconds) : this(TimeSpan.FromSeconds(timeoutSeconds))
    {
    }

    /**
     * Creates a new instance.
     *
     * @param timeout
     *        read timeout
     * @param unit
     *        the {@link TimeUnit} of {@code timeout}
     */
    // CLR: a TimeSpan replaces the (long timeout, TimeUnit unit) pair (docs/common-clr-design.md, "Monotonic time and
    // duration conversion").
    public ReadTimeoutHandler(TimeSpan timeout) : base(timeout, TimeSpan.Zero, TimeSpan.Zero)
    {
    }

    // CLR: the Java assert is Debug.Assert.
    protected sealed override void ChannelIdle(IChannelHandlerContext ctx, IdleStateEvent evt)
    {
        Debug.Assert(evt.State() == IdleState.READER_IDLE);
        ReadTimedOut(ctx);
    }

    /**
     * Is called when a read timeout was detected.
     */
    protected virtual void ReadTimedOut(IChannelHandlerContext ctx)
    {
        if (!closed)
        {
            ctx.FireExceptionCaught(ReadTimeoutException.INSTANCE);
            _ = ctx.CloseAsync();
            closed = true;
        }
    }
}
