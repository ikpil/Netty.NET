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

using System.Collections.Generic;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.String;

/**
 * Apply a line separator to the requested {@link String} and encode it into a {@link ByteBuf}.
 * A typical setup for a text-based line protocol in a TCP/IP socket would be:
 * <pre>
 * {@link ChannelPipeline} pipeline = ...;
 *
 * // Decoders
 * pipeline.addLast("frameDecoder", new {@link LineBasedFrameDecoder}(80));
 * pipeline.addLast("stringDecoder", new {@link StringDecoder}(CharsetUtil.UTF_8));
 *
 * // Encoder
 * pipeline.addLast("lineEncoder", new {@link LineEncoder}(LineSeparator.UNIX, CharsetUtil.UTF_8));
 * </pre>
 * and then you can use a {@link String} instead of a {@link ByteBuf}
 * as a message:
 * <pre>
 * void channelRead({@link ChannelHandlerContext} ctx, {@link String} msg) {
 *     ch.write("Did you say '" + msg + "'?");
 * }
 * </pre>
 */
[Sharable]
public class LineEncoder : MessageToMessageEncoder<IEnumerable<char>>
{
    private readonly Encoding charset;
    private readonly byte[] lineSeparator;

    /**
     * Creates a new instance with the current system line separator and UTF-8 charset encoding.
     */
    public LineEncoder() : this(LineSeparator.DEFAULT, Encoding.UTF8)
    {
    }

    /**
     * Creates a new instance with the specified line separator and UTF-8 charset encoding.
     */
    public LineEncoder(LineSeparator lineSeparator) : this(lineSeparator, Encoding.UTF8)
    {
    }

    /**
     * Creates a new instance with the specified character set.
     */
    public LineEncoder(Encoding charset) : this(LineSeparator.DEFAULT, charset)
    {
    }

    /**
     * Creates a new instance with the specified line separator and character set.
     */
    // CLR: value().getBytes(charset) is charset.GetBytes(value()).
    public LineEncoder(LineSeparator lineSeparator, Encoding charset) : base(typeof(IEnumerable<char>))
    {
        this.charset = ObjectUtil.CheckNotNull(charset, "charset");
        this.lineSeparator = charset.GetBytes(ObjectUtil.CheckNotNull(lineSeparator, "lineSeparator").Value());
    }

    protected override void Encode(IChannelHandlerContext ctx, IEnumerable<char> msg, IList<object> @out)
    {
        ByteBuf buffer = ByteBufUtil.EncodeString(ctx.Alloc(), Wrap(msg), charset, lineSeparator.Length);
        buffer.WriteBytes(lineSeparator);
        @out.Add(buffer);
    }

    // CLR: CharBuffer.wrap(msg) is a read-only buffer with the content of the character sequence
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/nio/CharBuffer.html#wrap(java.lang.CharSequence));
    // ByteBufUtil.EncodeString takes a ReadOnlySpan<char>, so a string is used as is and any other sequence is
    // collected into a string.
    private static string Wrap(IEnumerable<char> msg)
    {
        return msg as string ?? string.Concat(msg);
    }
}
