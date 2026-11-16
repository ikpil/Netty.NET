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

namespace Netty.NET.Transport.Channels;
/**
 * {@link ChannelHandler} which adds callbacks for state changes. This allows the user
 * to hook in to state changes easily.
 */
public interface IChannelInboundHandler : IChannelHandler
{
    /**
     * The {@link Channel} of the {@link ChannelHandlerContext} was registered with its {@link EventLoop}
     */
    void ChannelRegistered(IChannelHandlerContext ctx);
    /**
     * The {@link Channel} of the {@link ChannelHandlerContext} was unregistered from its {@link EventLoop}
     */
    void ChannelUnregistered(IChannelHandlerContext ctx);
    /**
     * The {@link Channel} of the {@link ChannelHandlerContext} is now active
     */
    void ChannelActive(IChannelHandlerContext ctx);
    /**
     * The {@link Channel} of the {@link ChannelHandlerContext} was registered is now inactive and reached its
     * end of lifetime.
     */
    void ChannelInactive(IChannelHandlerContext ctx);
    /**
     * Invoked when the current {@link Channel} has read a message from the peer.
     */
    void ChannelRead(IChannelHandlerContext ctx, object msg);
    /**
     * Invoked when the last message read by the current read operation has been consumed by
     * {@link #channelRead(ChannelHandlerContext, Object)}.  If {@link ChannelOption#AUTO_READ} is off, no further
     * attempt to read an inbound data from the current {@link Channel} will be made until
     * {@link ChannelHandlerContext#read()} is called.
     */
    void ChannelReadComplete(IChannelHandlerContext ctx);
    /**
     * Gets called if an user event was triggered.
     */
    void UserEventTriggered(IChannelHandlerContext ctx, object evt);
    /**
     * Gets called once the writable state of a {@link Channel} changed. You can check the state with
     * {@link Channel#isWritable()}.
     */
    void ChannelWritabilityChanged(IChannelHandlerContext ctx);

    /**
     * Gets called if a {@link Throwable} was thrown.
     */
    // CLR: Java re-declares exceptionCaught here only to drop the deprecation of ChannelHandler#exceptionCaught. An
    // explicit re-abstraction keeps the single IChannelHandler slot (no hiding member) and documents the declaration
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-8.0/default-interface-methods#reabstraction).
    abstract void IChannelHandler.ExceptionCaught(IChannelHandlerContext ctx, Exception cause);
}
