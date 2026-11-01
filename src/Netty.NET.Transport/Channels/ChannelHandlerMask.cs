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
    internal const int ExceptionCaught = 1;
    internal const int ChannelRegistered = 1 << 1, ChannelUnregistered = 1 << 2, ChannelActive = 1 << 3, ChannelInactive = 1 << 4;
    internal const int ChannelRead = 1 << 5, ChannelReadComplete = 1 << 6, UserEventTriggered = 1 << 7, ChannelWritabilityChanged = 1 << 8;
    internal const int Bind = 1 << 9, Connect = 1 << 10, Disconnect = 1 << 11, Close = 1 << 12, Deregister = 1 << 13;
    internal const int Read = 1 << 14, Write = 1 << 15, Flush = 1 << 16;
    internal const int OnlyInbound = ChannelRegistered | ChannelUnregistered | ChannelActive | ChannelInactive | ChannelRead | ChannelReadComplete | UserEventTriggered | ChannelWritabilityChanged;
    internal const int OnlyOutbound = Bind | Connect | Disconnect | Close | Deregister | Read | Write | Flush;
    // CLR: immutable masks can share a thread-safe weak-key cache. The value does not retain its Type key.
    private static readonly ConditionalWeakTable<Type, CachedMask> Masks = new();
    private sealed class CachedMask(int value) { internal readonly int Value = value; }
    /**
     * Return the {@code executionMask}.
     */
    internal static int Mask(Type type)
    {
        // Try to obtain the mask from the cache first. If this fails calculate it and put it in the cache for fast
        // lookup in the future.
        return Masks.GetValue(type, static key => new CachedMask(Calculate(key))).Value;
    }
    /**
     * Calculate the {@code executionMask}.
     */
    private static int Calculate(Type type) => InterfaceMask(type, typeof(IChannelHandler)) |
        InterfaceMask(type, typeof(IChannelInboundHandler)) | InterfaceMask(type, typeof(IChannelOutboundHandler));
    private static int InterfaceMask(Type type, Type contract)
    {
        if (!contract.IsAssignableFrom(type))
            return 0;
        var map = type.GetInterfaceMap(contract);
        int mask = 0;
        for (int i = 0; i < map.InterfaceMethods.Length; i++)
        {
            // CLR interface maps include explicit implementations; inspecting only public method names would miss them.
            if (map.TargetMethods[i].IsDefined(typeof(SkipAttribute), inherit: false))
                continue;
            mask |= map.InterfaceMethods[i].Name switch
            {
                nameof(IChannelHandler.ExceptionCaught) => ExceptionCaught,
                nameof(IChannelInboundHandler.ChannelRegistered) => ChannelRegistered,
                nameof(IChannelInboundHandler.ChannelUnregistered) => ChannelUnregistered,
                nameof(IChannelInboundHandler.ChannelActive) => ChannelActive,
                nameof(IChannelInboundHandler.ChannelInactive) => ChannelInactive,
                nameof(IChannelInboundHandler.ChannelRead) => ChannelRead,
                nameof(IChannelInboundHandler.ChannelReadComplete) => ChannelReadComplete,
                nameof(IChannelInboundHandler.UserEventTriggered) => UserEventTriggered,
                nameof(IChannelInboundHandler.ChannelWritabilityChanged) => ChannelWritabilityChanged,
                nameof(IChannelOutboundHandler.BindAsync) => Bind,
                nameof(IChannelOutboundHandler.ConnectAsync) => Connect,
                nameof(IChannelOutboundHandler.DisconnectAsync) => Disconnect,
                nameof(IChannelOutboundHandler.CloseAsync) => Close,
                nameof(IChannelOutboundHandler.DeregisterAsync) => Deregister,
                nameof(IChannelOutboundHandler.Read) => Read,
                nameof(IChannelOutboundHandler.WriteAsync) => Write,
                nameof(IChannelOutboundHandler.Flush) => Flush,
                _ => 0
            };
        }
        return mask;
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
