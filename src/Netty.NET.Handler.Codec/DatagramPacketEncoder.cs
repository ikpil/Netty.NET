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
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec;

/**
 * An encoder that encodes the content in {@link AddressedEnvelope} to {@link DatagramPacket} using
 * the specified message encoder. E.g.,
 *
 * <pre><code>
 * {@link ChannelPipeline} pipeline = ...;
 * pipeline.addLast("udpEncoder", new {@link DatagramPacketEncoder}(new {@code ProtobufEncoder}(...));
 * </code></pre>
 *
 * Note: As UDP packets are out-of-order, you should make sure the encoded message size are not greater than
 * the max safe packet size in your particular network path which guarantees no packet fragmentation.
 *
 * @param <M> the type of message to be encoded
 */
// CLR: AddressedEnvelope is IAddressedEnvelope and InetSocketAddress is IPEndPoint, as in DatagramPacket. The source
// matches the erased AddressedEnvelope type; the reified IAddressedEnvelope<M, IPEndPoint> check accepts the envelopes
// whose content type converts to M (IAddressedEnvelope is covariant), and passes others through as unmatched messages.
// write(ctx, msg, promise) and the other outbound operations return the Task that replaces the promise.
public class DatagramPacketEncoder<M> : MessageToMessageEncoder<IAddressedEnvelope<M, IPEndPoint>>
{
    // CLR: C# has no '? super M' wildcard; the encoder's type argument is M, and the covariant IAddressedEnvelope lets an
    // envelope of a subtype of a reference type M reach it.
    private readonly MessageToMessageEncoder<M> encoder;

    /**
     * Create an encoder that encodes the content in {@link AddressedEnvelope} to {@link DatagramPacket} using
     * the specified message encoder.
     *
     * @param encoder the specified message encoder
     */
    public DatagramPacketEncoder(MessageToMessageEncoder<M> encoder)
    {
        this.encoder = CheckNotNull(encoder, "encoder");
    }

    public override bool AcceptOutboundMessage(object msg)
    {
        if (base.AcceptOutboundMessage(msg))
        {
            // CLR: the raw AddressedEnvelope is IAddressedEnvelope<M, EndPoint> (covariant TAddress).
            IAddressedEnvelope<M, EndPoint> envelope = (IAddressedEnvelope<M, EndPoint>) msg;
            return encoder.AcceptOutboundMessage(envelope.Content)
                    && (envelope.Sender is IPEndPoint || envelope.Sender == null)
                    && envelope.Recipient is IPEndPoint;
        }
        return false;
    }

    protected override void Encode(
            IChannelHandlerContext ctx, IAddressedEnvelope<M, IPEndPoint> msg, IList<object> @out)
    {
        Debug.Assert(@out.Count == 0);

        encoder.InvokeEncode(ctx, msg.Content, @out);
        if (@out.Count != 1)
        {
            throw new EncoderException(
                    StringUtil.SimpleClassName(encoder) + " must produce only one message.");
        }
        object content = @out[0];
        if (content is ByteBuf)
        {
            // Replace the ByteBuf with a DatagramPacket.
            @out[0] = new DatagramPacket((ByteBuf) content, msg.Recipient, msg.Sender);
        }
        else
        {
            throw new EncoderException(
                    StringUtil.SimpleClassName(encoder) + " must produce only ByteBuf.");
        }
    }

    public override Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken)
    {
        return encoder.BindAsync(ctx, localAddress, cancellationToken);
    }

    public override Task ConnectAsync(
            IChannelHandlerContext ctx, EndPoint remoteAddress,
            EndPoint localAddress, CancellationToken cancellationToken)
    {
        return encoder.ConnectAsync(ctx, remoteAddress, localAddress, cancellationToken);
    }

    public override Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return encoder.DisconnectAsync(ctx, cancellationToken);
    }

    public override Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return encoder.CloseAsync(ctx, cancellationToken);
    }

    public override Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return encoder.DeregisterAsync(ctx, cancellationToken);
    }

    public override void Read(IChannelHandlerContext ctx)
    {
        encoder.Read(ctx);
    }

    public override void Flush(IChannelHandlerContext ctx)
    {
        encoder.Flush(ctx);
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        encoder.HandlerAdded(ctx);
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        encoder.HandlerRemoved(ctx);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        encoder.ExceptionCaught(ctx, cause);
    }

    public override bool IsSharable => encoder.IsSharable;
}
