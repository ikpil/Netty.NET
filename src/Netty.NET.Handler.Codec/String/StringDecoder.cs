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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.String;

/**
 * Decodes a received {@link ByteBuf} into a {@link String}.  Please
 * note that this decoder must be used with a proper {@link ByteToMessageDecoder}
 * such as {@link DelimiterBasedFrameDecoder} or {@link LineBasedFrameDecoder}
 * if you are using a stream-based transport such as TCP/IP.  A typical setup for a
 * text-based line protocol in a TCP/IP socket would be:
 * <pre>
 * {@link ChannelPipeline} pipeline = ...;
 *
 * // Decoders
 * pipeline.addLast("frameDecoder", new {@link LineBasedFrameDecoder}(80));
 * pipeline.addLast("stringDecoder", new {@link StringDecoder}(CharsetUtil.UTF_8));
 *
 * // Encoder
 * pipeline.addLast("stringEncoder", new {@link StringEncoder}(CharsetUtil.UTF_8));
 * </pre>
 * and then you can use a {@link String} instead of a {@link ByteBuf}
 * as a message:
 * <pre>
 * void channelRead({@link ChannelHandlerContext} ctx, {@link String} msg) {
 *     ch.write("Did you say '" + msg + "'?\n");
 * }
 * </pre>
 */
// CLR: java.nio.charset.Charset is System.Text.Encoding, as in ByteBuf.ToString(Encoding charset).
[Sharable]
public class StringDecoder : MessageToMessageDecoder<ByteBuf>
{
    // TODO Use CharsetDecoder instead.
    private readonly Encoding charset;

    /**
     * Creates a new instance with the current system character set.
     */
    // CLR: Charset.defaultCharset() is UTF-8 unless changed in an implementation specific manner
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/nio/charset/Charset.html#defaultCharset());
    // Encoding.Default always returns a UTF8Encoding on .NET Core and later
    // (https://learn.microsoft.com/dotnet/api/system.text.encoding.default).
    public StringDecoder() : this(Encoding.Default)
    {
    }

    /**
     * Creates a new instance with the specified character set.
     */
    public StringDecoder(Encoding charset) : base(typeof(ByteBuf))
    {
        this.charset = ObjectUtil.CheckNotNull(charset, "charset");
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf msg, IList<object> @out)
    {
        @out.Add(msg.ToString(charset));
    }
}
