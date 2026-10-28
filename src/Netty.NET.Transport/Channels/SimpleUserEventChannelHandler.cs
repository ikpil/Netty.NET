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

using Netty.NET.Common;

namespace Netty.NET.Transport.Channels;

/// <summary>Consumes events of T and optionally releases their reference-counted ownership.</summary>
/**
 * {@link ChannelInboundHandlerAdapter} which allows to conveniently only handle a specific type of user events.
 *
 * For example, here is an implementation which only handle {@link String} user events.
 *
 * <pre>
 *     public class StringEventHandler extends
 *             {@link SimpleUserEventChannelHandler}&lt;{@link String}&gt; {
 *
 *         {@code @Override}
 *         protected void eventReceived({@link ChannelHandlerContext} ctx, {@link String} evt)
 *                 throws {@link Exception} {
 *             System.out.println(evt);
 *         }
 *     }
 * </pre>
 *
 * Be aware that depending of the constructor parameters it will release all handled events by passing them to
 * {@link ReferenceCountUtil#release(Object)}. In this case you may need to use
 * {@link ReferenceCountUtil#retain(Object)} if you pass the object to the next handler in the {@link ChannelPipeline}.
 */
public abstract class SimpleUserEventChannelHandler<T> : ChannelInboundHandlerAdapter
{
    private readonly bool _autoRelease;
    /**
     * Create a new instance which will try to detect the types to match out of the type parameter of the class.
     *
     * @param autoRelease   {@code true} if handled events should be released automatically by passing them to
     *                      {@link ReferenceCountUtil#release(Object)}.
     */
    /**
     * see {@link #SimpleUserEventChannelHandler(Class, boolean)} with {@code true} as boolean value.
     */
    /**
     * Create a new instance
     *
     * @param eventType      The type of events to match
     * @param autoRelease    {@code true} if handled events should be released automatically by passing them to
     *                       {@link ReferenceCountUtil#release(Object)}.
     */
    protected SimpleUserEventChannelHandler(bool autoRelease = true) { _autoRelease = autoRelease; }
    /**
     * Returns {@code true} if the given user event should be handled. If {@code false} it will be passed to the next
     * {@link ChannelInboundHandler} in the {@link ChannelPipeline}.
     */
    protected virtual bool AcceptEvent(object evt) => evt is T;
    public sealed override void UserEventTriggered(ChannelHandlerContext context, object evt)
    {
        bool release = true;
        try
        {
            if (AcceptEvent(evt))
                EventReceived(context, (T)evt);
            else
            { release = false; context.FireUserEventTriggered(evt); }
        }
        finally { if (_autoRelease && release) ReferenceCountUtil.Release(evt); }
    }
    /**
     * Is called for each user event triggered of type {@link I}.
     *
     * @param ctx the {@link ChannelHandlerContext} which this {@link SimpleUserEventChannelHandler} belongs to
     * @param evt the user event to handle
     *
     * @throws Exception is thrown if an error occurred
     */
    protected abstract void EventReceived(ChannelHandlerContext context, T evt);
}
