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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Base64Codec;

/**
 * Encodes a {@link ByteBuf} into a Base64-encoded {@link ByteBuf}.
 * A typical setup for TCP/IP would be:
 * <pre>
 * {@link ChannelPipeline} pipeline = ...;
 *
 * // Decoders
 * pipeline.addLast("frameDecoder", new {@link DelimiterBasedFrameDecoder}(80, {@link Delimiters#nulDelimiter()}));
 * pipeline.addLast("base64Decoder", new {@link Base64Decoder}());
 *
 * // Encoder
 * pipeline.addLast("base64Encoder", new {@link Base64Encoder}());
 * </pre>
 */
[Sharable]
public class Base64Encoder : MessageToMessageEncoder<ByteBuf>
{
    private readonly bool breakLines;
    private readonly Base64Dialect dialect;

    public Base64Encoder() : this(true)
    {
    }

    public Base64Encoder(bool breakLines) : this(breakLines, Base64Dialect.STANDARD)
    {
    }

    public Base64Encoder(bool breakLines, Base64Dialect dialect) : base(typeof(ByteBuf))
    {
        this.dialect = ObjectUtil.CheckNotNull(dialect, "dialect");
        this.breakLines = breakLines;
    }

    protected override void Encode(IChannelHandlerContext ctx, ByteBuf msg, IList<object> @out)
    {
        @out.Add(Base64.Encode(msg, msg.ReaderIndex, msg.ReadableBytes, breakLines, dialect));
    }
}
