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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;
/**
 * Skeleton implementation of a {@link ChannelHandler}.
 */
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
public abstract class ChannelHandlerAdapter : IChannelHandler
{
    // Not using volatile because it's used only for a sanity check.
    internal bool added;

    /**
     * Throws {@link IllegalStateException} if {@link ChannelHandlerAdapter#isSharable()} returns {@code true}
     */
    protected void EnsureNotSharable()
    {
        if (IsSharable)
        {
            throw new InvalidOperationException("ChannelHandler " + GetType().FullName + " is not allowed to be shared");
        }
    }

    /**
     * Return {@code true} if the implementation is {@link Sharable} and so can be added
     * to different {@link ChannelPipeline}s.
     */
    public virtual bool IsSharable
    {
        get
        {
            /**
             * Cache the result of {@link Sharable} annotation detection to workaround a condition. We use a
             * {@link ThreadLocal} and {@link WeakHashMap} to eliminate the volatile write/reads. Using different
             * {@link WeakHashMap} instances per {@link Thread} is good enough for us and the number of
             * {@link Thread}s are quite limited anyway.
             *
             * See <a href="https://github.com/netty/netty/issues/2289">#2289</a>.
             */
            Type clazz = GetType();
            ConditionalWeakTable<Type, StrongBox<bool>> cache = InternalThreadLocalMap.Get().HandlerSharableCache();
            if (!cache.TryGetValue(clazz, out StrongBox<bool> sharable))
            {
                sharable = new StrongBox<bool>(clazz.IsDefined(typeof(SharableAttribute), true));
                cache.AddOrUpdate(clazz, sharable);
            }
            return sharable.Value;
        }
    }

    /**
     * Do nothing by default, sub-classes may override this method.
     */
    public virtual void HandlerAdded(IChannelHandlerContext ctx)
    {
        // NOOP
    }

    /**
     * Do nothing by default, sub-classes may override this method.
     */
    public virtual void HandlerRemoved(IChannelHandlerContext ctx)
    {
        // NOOP
    }

    /**
     * Calls {@link ChannelHandlerContext#fireExceptionCaught(Throwable)} to forward
     * to the next {@link ChannelHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     *
     * @deprecated is part of {@link ChannelInboundHandler}
     */
    [ChannelHandlerMask.Skip]
    public virtual void ExceptionCaught(IChannelHandlerContext ctx, Exception cause) => ctx.FireExceptionCaught(cause);
}
