/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec;

/**
 * A decoder that decodes the content of the received {@link DatagramPacket} using
 * the specified {@link ByteBuf} decoder. E.g.,
 *
 * <pre><code>
 * {@link ChannelPipeline} pipeline = ...;
 * pipeline.addLast("udpDecoder", new {@link DatagramPacketDecoder}(new {@code ProtobufDecoder}(...));
 * </code></pre>
 */
public class DatagramPacketDecoder : MessageToMessageDecoder<DatagramPacket>
{
    private readonly MessageToMessageDecoder<ByteBuf> decoder;

    /**
     * Create a {@link DatagramPacket} decoder using the specified {@link ByteBuf} decoder.
     *
     * @param decoder the specified {@link ByteBuf} decoder
     */
    public DatagramPacketDecoder(MessageToMessageDecoder<ByteBuf> decoder) : base(typeof(DatagramPacket))
    {
        this.decoder = CheckNotNull(decoder, "decoder");
    }

    public override bool AcceptInboundMessage(object msg)
    {
        if (msg is DatagramPacket)
        {
            return decoder.AcceptInboundMessage(((DatagramPacket) msg).Content);
        }

        return false;
    }

    protected override void Decode(IChannelHandlerContext ctx, DatagramPacket msg, IList<object> @out)
    {
        decoder.InvokeDecode(ctx, msg.Content, @out);
    }

    public override void ChannelRegistered(IChannelHandlerContext ctx)
    {
        decoder.ChannelRegistered(ctx);
    }

    public override void ChannelUnregistered(IChannelHandlerContext ctx)
    {
        decoder.ChannelUnregistered(ctx);
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        decoder.ChannelActive(ctx);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        decoder.ChannelInactive(ctx);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        decoder.ChannelReadComplete(ctx);
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        decoder.UserEventTriggered(ctx, evt);
    }

    public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
    {
        decoder.ChannelWritabilityChanged(ctx);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        decoder.ExceptionCaught(ctx, cause);
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        decoder.HandlerAdded(ctx);
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        decoder.HandlerRemoved(ctx);
    }

    public override bool IsSharable => decoder.IsSharable;
}
