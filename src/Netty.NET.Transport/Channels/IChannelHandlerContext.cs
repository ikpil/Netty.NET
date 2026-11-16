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

using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels;

// CLR: the fireXxx(), read() and flush() members that Java re-declares here only to narrow their return type to
// ChannelHandlerContext are inherited from IChannelInboundInvoker and IChannelOutboundInvoker unchanged, as a C#
// interface cannot narrow an inherited member's return type without hiding it. channel(), executor(), name(),
// handler(), isRemoved() and pipeline() keep the property form whose name equals the Java method (owner decision
// 2026-11-16).
/**
 * Enables a {@link ChannelHandler} to interact with its {@link ChannelPipeline}
 * and other handlers. Among other things a handler can notify the next {@link ChannelHandler} in the
 * {@link ChannelPipeline} as well as modify the {@link ChannelPipeline} it belongs to dynamically.
 *
 * <h3>Notify</h3>
 *
 * You can notify the closest handler in the same {@link ChannelPipeline} by calling one of the various methods
 * provided here.
 *
 * Please refer to {@link ChannelPipeline} to understand how an event flows.
 *
 * <h3>Modifying a pipeline</h3>
 *
 * You can get the {@link ChannelPipeline} your handler belongs to by calling
 * {@link #pipeline()}.  A non-trivial application could insert, remove, or
 * replace handlers in the pipeline dynamically at runtime.
 *
 * <h3>Retrieving for later use</h3>
 *
 * You can keep the {@link ChannelHandlerContext} for later use, such as
 * triggering an event outside the handler methods, even from a different thread.
 * <pre>
 * public class MyHandler extends {@link ChannelDuplexHandler} {
 *
 *     <b>private {@link ChannelHandlerContext} ctx;</b>
 *
 *     public void beforeAdd({@link ChannelHandlerContext} ctx) {
 *         <b>this.ctx = ctx;</b>
 *     }
 *
 *     public void login(String username, password) {
 *         ctx.write(new LoginMessage(username, password));
 *     }
 *     ...
 * }
 * </pre>
 *
 * <h3>Storing stateful information</h3>
 *
 * {@link #attr(AttributeKey)} allow you to
 * store and access stateful information that is related with a {@link ChannelHandler} / {@link Channel} and its
 * context. Please refer to {@link ChannelHandler} to learn various recommended
 * ways to manage stateful information.
 *
 * <h3>A handler can have more than one {@link ChannelHandlerContext}</h3>
 *
 * Please note that a {@link ChannelHandler} instance can be added to more than
 * one {@link ChannelPipeline}.  It means a single {@link ChannelHandler}
 * instance can have more than one {@link ChannelHandlerContext} and therefore
 * the single instance can be invoked with different
 * {@link ChannelHandlerContext}s if it is added to one or more {@link ChannelPipeline}s more than once.
 * Also note that a {@link ChannelHandler} that is supposed to be added to multiple {@link ChannelPipeline}s should
 * be marked as {@link io.netty.channel.ChannelHandler.Sharable}.
 *
 * <h3>Additional resources worth reading</h3>
 * <p>
 * Please refer to the {@link ChannelHandler}, and
 * {@link ChannelPipeline} to find out more about inbound and outbound operations,
 * what fundamental differences they have, how they flow in a  pipeline,  and how to handle
 * the operation in your application.
 */
public interface IChannelHandlerContext : IAttributeMap, IChannelInboundInvoker, IChannelOutboundInvoker
{
    /**
     * Return the {@link Channel} which is bound to the {@link ChannelHandlerContext}.
     */
    IChannel Channel { get; }

    /**
     * Returns the {@link EventExecutor} which is used to execute an arbitrary task.
     */
    IEventExecutor Executor { get; }

    /**
     * The unique name of the {@link ChannelHandlerContext}.The name was used when then {@link ChannelHandler}
     * was added to the {@link ChannelPipeline}. This name can also be used to access the registered
     * {@link ChannelHandler} from the {@link ChannelPipeline}.
     */
    string Name { get; }

    /**
     * The {@link ChannelHandler} that is bound this {@link ChannelHandlerContext}.
     */
    IChannelHandler Handler { get; }

    /**
     * Return {@code true} if the {@link ChannelHandler} which belongs to this context was removed
     * from the {@link ChannelPipeline}. Note that this method is only meant to be called from with in the
     * {@link EventLoop}.
     */
    bool IsRemoved { get; }

    /**
     * Return the assigned {@link ChannelPipeline}
     */
    IChannelPipeline Pipeline { get; }

    /**
     * Return the assigned {@link ByteBufAllocator} which will be used to allocate {@link ByteBuf}s.
     */
    IByteBufAllocator Alloc();

    /**
     * @deprecated Use {@link Channel#attr(AttributeKey)}
     */
    // CLR: re-declared as in Java; no [Obsolete] is added, as the inherited IAttributeMap member stays supported.
    new IAttribute<T> Attr<T>(AttributeKey<T> key) where T : class;

    /**
     * @deprecated Use {@link Channel#hasAttr(AttributeKey)}
     */
    new bool HasAttr<T>(AttributeKey<T> key) where T : class;
}
