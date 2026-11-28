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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec;

/**
 * A Codec for on-the-fly encoding/decoding of message.
 *
 * This can be thought of as a combination of {@link MessageToMessageDecoder} and {@link MessageToMessageEncoder}.
 *
 * Here is an example of a {@link MessageToMessageCodec} which just decode from {@link Integer} to {@link Long}
 * and encode from {@link Long} to {@link Integer}.
 *
 * <pre>
 *     public class NumberCodec extends
 *             {@link MessageToMessageCodec}&lt;{@link Integer}, {@link Long}&gt; {
 *         {@code @Override}
 *         public {@link Long} decode({@link ChannelHandlerContext} ctx, {@link Integer} msg, List&lt;Object&gt; out)
 *                 throws {@link Exception} {
 *             out.add(msg.longValue());
 *         }
 *
 *         {@code @Override}
 *         public {@link Integer} encode({@link ChannelHandlerContext} ctx, {@link Long} msg, List&lt;Object&gt; out)
 *                 throws {@link Exception} {
 *             out.add(msg.intValue());
 *         }
 *     }
 * </pre>
 *
 * Be aware that you need to call {@link ReferenceCounted#retain()} on messages that are just passed through if they
 * are of type {@link ReferenceCounted}. This is needed as the {@link MessageToMessageCodec} will call
 * {@link ReferenceCounted#release()} on encoded / decoded messages.
 */
public abstract class MessageToMessageCodec<INBOUND_IN, OUTBOUND_IN> : ChannelDuplexHandler
{
    private readonly MessageToMessageDecoder<object> decoder;

    private sealed class Decoder : MessageToMessageDecoder<object>
    {
        private readonly MessageToMessageCodec<INBOUND_IN, OUTBOUND_IN> codec;

        internal Decoder(MessageToMessageCodec<INBOUND_IN, OUTBOUND_IN> codec) : base(typeof(object))
        {
            this.codec = codec;
        }

        public override bool AcceptInboundMessage(object msg)
        {
            return codec.AcceptInboundMessage(msg);
        }

        protected override void Decode(IChannelHandlerContext ctx, object msg, IList<object> @out)
        {
            codec.Decode(ctx, (INBOUND_IN) msg, @out);
        }

        public override bool IsSharable => codec.IsSharable;
    }

    private readonly MessageToMessageEncoder<object> encoder;

    private sealed class Encoder : MessageToMessageEncoder<object>
    {
        private readonly MessageToMessageCodec<INBOUND_IN, OUTBOUND_IN> codec;

        internal Encoder(MessageToMessageCodec<INBOUND_IN, OUTBOUND_IN> codec) : base(typeof(object))
        {
            this.codec = codec;
        }

        public override bool AcceptOutboundMessage(object msg)
        {
            return codec.AcceptOutboundMessage(msg);
        }

        protected override void Encode(IChannelHandlerContext ctx, object msg, IList<object> @out)
        {
            codec.Encode(ctx, (OUTBOUND_IN) msg, @out);
        }

        public override bool IsSharable => codec.IsSharable;
    }

    // CLR: the reified type arguments and Type.IsInstanceOfType replace TypeParameterMatcher.find/get, as in
    // SimpleChannelInboundHandler (https://learn.microsoft.com/dotnet/api/system.type.isinstanceoftype).
    private readonly Type inboundMsgMatcher;
    private readonly Type outboundMsgMatcher;

    /**
     * Create a new instance which will try to detect the types to decode and encode out of the type parameter
     * of the class.
     */
    protected MessageToMessageCodec()
    {
        decoder = new Decoder(this);
        encoder = new Encoder(this);
        inboundMsgMatcher = typeof(INBOUND_IN);
        outboundMsgMatcher = typeof(OUTBOUND_IN);
    }

    /**
     * Create a new instance.
     *
     * @param inboundMessageType    The type of messages to decode
     * @param outboundMessageType   The type of messages to encode
     */
    protected MessageToMessageCodec(
            Type inboundMessageType, Type outboundMessageType)
    {
        decoder = new Decoder(this);
        encoder = new Encoder(this);
        inboundMsgMatcher = MessageToByteEncoder<INBOUND_IN>.CheckMessageType(
                inboundMessageType, typeof(INBOUND_IN), "inboundMessageType");
        outboundMsgMatcher = MessageToByteEncoder<OUTBOUND_IN>.CheckMessageType(
                outboundMessageType, typeof(OUTBOUND_IN), "outboundMessageType");
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        decoder.ChannelRead(ctx, msg);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        decoder.ChannelReadComplete(ctx);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        return encoder.WriteAsync(ctx, msg, cancellationToken);
    }

    /**
     * Returns {@code true} if and only if the specified message can be decoded by this codec.
     *
     * @param msg the message
     */
    public virtual bool AcceptInboundMessage(object msg)
    {
        return inboundMsgMatcher.IsInstanceOfType(msg);
    }

    /**
     * Returns {@code true} if and only if the specified message can be encoded by this codec.
     *
     * @param msg the message
     */
    public virtual bool AcceptOutboundMessage(object msg)
    {
        return outboundMsgMatcher.IsInstanceOfType(msg);
    }

    /**
     * @see MessageToMessageEncoder#encode(ChannelHandlerContext, Object, List)
     */
    protected abstract void Encode(IChannelHandlerContext ctx, OUTBOUND_IN msg, IList<object> @out);

    /**
     * @see MessageToMessageDecoder#decode(ChannelHandlerContext, Object, List)
     */
    protected abstract void Decode(IChannelHandlerContext ctx, INBOUND_IN msg, IList<object> @out);
}
