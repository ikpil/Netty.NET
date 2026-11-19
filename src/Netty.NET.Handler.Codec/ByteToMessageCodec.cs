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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec;

/**
 * A Codec for on-the-fly encoding/decoding of bytes to messages and vise-versa.
 *
 * This can be thought of as a combination of {@link ByteToMessageDecoder} and {@link MessageToByteEncoder}.
 *
 * Be aware that sub-classes of {@link ByteToMessageCodec} <strong>MUST NOT</strong>
 * annotated with {@link @Sharable}.
 */
public abstract class ByteToMessageCodec<I> : ChannelDuplexHandler
{
    // CLR: the reified type argument and Type.IsInstanceOfType replace TypeParameterMatcher.find/get, as in
    // SimpleChannelInboundHandler (https://learn.microsoft.com/dotnet/api/system.type.isinstanceoftype).
    private readonly Type outboundMsgMatcher;
    private readonly MessageToByteEncoder<I> encoder;

    private readonly ByteToMessageDecoder decoder;

    // CLR: the anonymous decoder is a named nested class that forwards to the codec.
    private sealed class Decoder : ByteToMessageDecoder
    {
        private readonly ByteToMessageCodec<I> codec;

        internal Decoder(ByteToMessageCodec<I> codec)
        {
            this.codec = codec;
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            codec.Decode(ctx, @in, @out);
        }

        protected override void DecodeLast(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            codec.DecodeLast(ctx, @in, @out);
        }
    }

    /**
     * see {@link #ByteToMessageCodec(boolean)} with {@code true} as boolean parameter.
     */
    protected ByteToMessageCodec() : this(true)
    {
    }

    /**
     * see {@link #ByteToMessageCodec(Class, boolean)} with {@code true} as boolean value.
     */
    protected ByteToMessageCodec(Type outboundMessageType) : this(outboundMessageType, true)
    {
    }

    /**
     * Create a new instance which will try to detect the types to match out of the type parameter of the class.
     *
     * @param preferDirect          {@code true} if a direct {@link ByteBuf} should be tried to be used as target for
     *                              the encoded messages. If {@code false} is used it will allocate a heap
     *                              {@link ByteBuf}, which is backed by an byte array.
     */
    protected ByteToMessageCodec(bool preferDirect)
    {
        EnsureNotSharable();
        decoder = new Decoder(this);
        outboundMsgMatcher = typeof(I);
        encoder = new Encoder(this, preferDirect);
    }

    /**
     * Create a new instance
     *
     * @param outboundMessageType   The type of messages to match
     * @param preferDirect          {@code true} if a direct {@link ByteBuf} should be tried to be used as target for
     *                              the encoded messages. If {@code false} is used it will allocate a heap
     *                              {@link ByteBuf}, which is backed by an byte array.
     */
    protected ByteToMessageCodec(Type outboundMessageType, bool preferDirect)
    {
        EnsureNotSharable();
        decoder = new Decoder(this);
        outboundMsgMatcher = MessageToByteEncoder<I>.CheckMessageType(outboundMessageType, typeof(I),
                "outboundMessageType");
        encoder = new Encoder(this, preferDirect, outboundMessageType);
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

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        decoder.ChannelRead(ctx, msg);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        return encoder.WriteAsync(ctx, msg, cancellationToken);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        decoder.ChannelReadComplete(ctx);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        decoder.ChannelInactive(ctx);
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        try
        {
            decoder.HandlerAdded(ctx);
        }
        finally
        {
            encoder.HandlerAdded(ctx);
        }
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        try
        {
            decoder.HandlerRemoved(ctx);
        }
        finally
        {
            encoder.HandlerRemoved(ctx);
        }
    }

    /**
     * @see MessageToByteEncoder#encode(ChannelHandlerContext, Object, ByteBuf)
     */
    protected abstract void Encode(IChannelHandlerContext ctx, I msg, ByteBuf @out);

    /**
     * @see ByteToMessageDecoder#decode(ChannelHandlerContext, ByteBuf, List)
     */
    protected abstract void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out);

    /**
     * @see ByteToMessageDecoder#decodeLast(ChannelHandlerContext, ByteBuf, List)
     */
    protected virtual void DecodeLast(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (@in.IsReadable())
        {
            // Only call decode() if there is something left in the buffer to decode.
            // See https://github.com/netty/netty/issues/4386
            Decode(ctx, @in, @out);
        }
    }

    // CLR: the inner class reaches the codec through an explicit reference (Java's ByteToMessageCodec.this).
    private sealed class Encoder : MessageToByteEncoder<I>
    {
        private readonly ByteToMessageCodec<I> codec;

        internal Encoder(ByteToMessageCodec<I> codec, bool preferDirect) : base(preferDirect)
        {
            this.codec = codec;
        }

        internal Encoder(ByteToMessageCodec<I> codec, bool preferDirect, Type outboundMessageType)
            : base(outboundMessageType, preferDirect)
        {
            this.codec = codec;
        }

        public override bool AcceptOutboundMessage(object msg)
        {
            return codec.AcceptOutboundMessage(msg);
        }

        protected override void Encode(IChannelHandlerContext ctx, I msg, ByteBuf @out)
        {
            codec.Encode(ctx, msg, @out);
        }
    }
}
