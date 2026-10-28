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
    void ChannelRegistered(ChannelHandlerContext context);
    /**
     * The {@link Channel} of the {@link ChannelHandlerContext} was unregistered from its {@link EventLoop}
     */
    void ChannelUnregistered(ChannelHandlerContext context);
    /**
     * The {@link Channel} of the {@link ChannelHandlerContext} is now active
     */
    void ChannelActive(ChannelHandlerContext context);
    /**
     * The {@link Channel} of the {@link ChannelHandlerContext} was registered is now inactive and reached its
     * end of lifetime.
     */
    void ChannelInactive(ChannelHandlerContext context);
    /**
     * Invoked when the current {@link Channel} has read a message from the peer.
     */
    void ChannelRead(ChannelHandlerContext context, object message);
    /**
     * Invoked when the last message read by the current read operation has been consumed by
     * {@link #channelRead(ChannelHandlerContext, Object)}.  If {@link ChannelOption#AUTO_READ} is off, no further
     * attempt to read an inbound data from the current {@link Channel} will be made until
     * {@link ChannelHandlerContext#read()} is called.
     */
    void ChannelReadComplete(ChannelHandlerContext context);
    /**
     * Gets called if an user event was triggered.
     */
    void UserEventTriggered(ChannelHandlerContext context, object evt);
    /**
     * Gets called once the writable state of a {@link Channel} changed. You can check the state with
     * {@link Channel#isWritable()}.
     */
    void ChannelWritabilityChanged(ChannelHandlerContext context);

}
