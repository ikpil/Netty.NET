/*
 * Copyright 2013 The Netty Project
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
using System.Threading;
namespace Netty.NET.Transport.Channels;
/**
 * Skeleton implementation of a {@link ChannelHandler}.
 */
public abstract class ChannelHandlerAdapter : IChannelHandler
{
    private int _added;
    /**
     * Return {@code true} if the implementation is {@link Sharable} and so can be added
     * to different {@link ChannelPipeline}s.
     */
    public virtual bool IsSharable => GetType().IsDefined(typeof(SharableAttribute), true);
    internal void CheckMultiplicity()
    {
        if (!IsSharable && Interlocked.Exchange(ref _added, 1) != 0)
            throw new ChannelPipelineException("A non-sharable handler cannot be added more than once.");
    }
    /**
     * Throws {@link IllegalStateException} if {@link ChannelHandlerAdapter#isSharable()} returns {@code true}
     */
    protected void EnsureNotSharable()
    {
        if (IsSharable)
            throw new InvalidOperationException("This handler cannot be sharable.");
    }
    /**
     * Do nothing by default, sub-classes may override this method.
     */
    public virtual void HandlerAdded(ChannelHandlerContext context) { }
    public virtual void HandlerRemoved(ChannelHandlerContext context) { }
    /**
     * Calls {@link ChannelHandlerContext#fireExceptionCaught(Throwable)} to forward
     * to the next {@link ChannelHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     *
     * @deprecated is part of {@link ChannelInboundHandler}
     */
    public virtual void ExceptionCaught(ChannelHandlerContext context, Exception cause) => context.FireExceptionCaught(cause);
}
