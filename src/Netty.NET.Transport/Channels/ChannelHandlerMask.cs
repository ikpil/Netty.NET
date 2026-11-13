/*
 * Copyright 2019 The Netty Project
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
using System.Runtime.CompilerServices;

namespace Netty.NET.Transport.Channels;

internal static class ChannelHandlerMask
{
    // Using to mask which methods must be called for a ChannelHandler.
    internal const int MaskExceptionCaught = 1;
    internal const int MaskChannelRegistered = 1 << 1, MaskChannelUnregistered = 1 << 2, MaskChannelActive = 1 << 3, MaskChannelInactive = 1 << 4;
    internal const int MaskChannelRead = 1 << 5, MaskChannelReadComplete = 1 << 6, MaskUserEventTriggered = 1 << 7, MaskChannelWritabilityChanged = 1 << 8;
    internal const int MaskBind = 1 << 9, MaskConnect = 1 << 10, MaskDisconnect = 1 << 11, MaskClose = 1 << 12, MaskDeregister = 1 << 13;
    internal const int MaskRead = 1 << 14, MaskWrite = 1 << 15, MaskFlush = 1 << 16;
    internal const int MaskOnlyInbound = MaskChannelRegistered | MaskChannelUnregistered | MaskChannelActive | MaskChannelInactive | MaskChannelRead | MaskChannelReadComplete | MaskUserEventTriggered | MaskChannelWritabilityChanged;
    private const int MaskAllInbound = MaskExceptionCaught | MaskOnlyInbound;
    internal const int MaskOnlyOutbound = MaskBind | MaskConnect | MaskDisconnect | MaskClose | MaskDeregister | MaskRead | MaskWrite | MaskFlush;
    private const int MaskAllOutbound = MaskExceptionCaught | MaskOnlyOutbound;
    // CLR: immutable masks share a weak-key cache. Neither the cached value nor its factory retains a handler instance.
    private static readonly ConditionalWeakTable<Type, CachedMask> Masks = new();
    private sealed class CachedMask(int value) { internal readonly int Value = value; }
    /**
     * Return the {@code executionMask}.
     */
    internal static int Mask(IChannelHandler handler)
    {
        // Try to obtain the mask from the cache first. If this fails calculate it and put it in the cache for fast
        // lookup in the future.
        Type clazz = handler.GetType();
        return Masks.GetOrAdd(clazz, static (handlerType, handler) => new CachedMask(Mask0(handlerType, handler)), handler).Value;
    }
    /**
     * Calculate the {@code executionMask}.
     */
    private static int Mask0(Type handlerType, IChannelHandler handler)
    {
        int mask = MaskExceptionCaught;
        // CLR: a bound interface delegate identifies the actual virtual or explicit implementation, including in NativeAOT.
        // The source Class parameter remains the cache/type discriminator; the representative instance is not invoked.
        if (typeof(IChannelInboundHandler).IsAssignableFrom(handlerType))
        {
            var inbound = (IChannelInboundHandler)handler;
            mask |= MaskAllInbound;
            if (IsSkippable(inbound.ChannelRegistered)) mask &= ~MaskChannelRegistered;
            if (IsSkippable(inbound.ChannelUnregistered)) mask &= ~MaskChannelUnregistered;
            if (IsSkippable(inbound.ChannelActive)) mask &= ~MaskChannelActive;
            if (IsSkippable(inbound.ChannelInactive)) mask &= ~MaskChannelInactive;
            if (IsSkippable(inbound.ChannelRead)) mask &= ~MaskChannelRead;
            if (IsSkippable(inbound.ChannelReadComplete)) mask &= ~MaskChannelReadComplete;
            if (IsSkippable(inbound.ChannelWritabilityChanged)) mask &= ~MaskChannelWritabilityChanged;
            if (IsSkippable(inbound.UserEventTriggered)) mask &= ~MaskUserEventTriggered;
        }
        if (typeof(IChannelOutboundHandler).IsAssignableFrom(handlerType))
        {
            var outbound = (IChannelOutboundHandler)handler;
            mask |= MaskAllOutbound;
            if (IsSkippable(outbound.BindAsync)) mask &= ~MaskBind;
            if (IsSkippable(outbound.ConnectAsync)) mask &= ~MaskConnect;
            if (IsSkippable(outbound.DisconnectAsync)) mask &= ~MaskDisconnect;
            if (IsSkippable(outbound.CloseAsync)) mask &= ~MaskClose;
            if (IsSkippable(outbound.DeregisterAsync)) mask &= ~MaskDeregister;
            if (IsSkippable(outbound.Read)) mask &= ~MaskRead;
            if (IsSkippable(outbound.WriteAsync)) mask &= ~MaskWrite;
            if (IsSkippable(outbound.Flush)) mask &= ~MaskFlush;
        }
        if (IsSkippable(handler.ExceptionCaught)) mask &= ~MaskExceptionCaught;
        return mask;
    }
    private static bool IsSkippable(Delegate method)
    {
        // CLR: handler annotations preserve method metadata; binding selects the actual interface implementation.
        var m = method.Method;
        return m.IsDefined(typeof(SkipAttribute), inherit: false);
    }
    /**
     * Indicates that the annotated event handler method in {@link ChannelHandler} will not be invoked by
     * {@link ChannelPipeline} and so <strong>MUST</strong> only be used when the {@link ChannelHandler}
     * method does nothing except forward to the next {@link ChannelHandler} in the pipeline.
     * <p>
     * Note that this annotation is not {@linkplain Inherited inherited}. If a user overrides a method annotated with
     * {@link Skip}, it will not be skipped anymore. Similarly, the user can override a method not annotated with
     * {@link Skip} and simply pass the event through to the next handler, which reverses the behavior of the
     * supertype.
     * </p>
     */
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class SkipAttribute : Attribute
    {
        // no value
    }
}
