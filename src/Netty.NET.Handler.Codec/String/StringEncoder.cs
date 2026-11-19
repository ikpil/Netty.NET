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
 * Encodes the requested {@link String} into a {@link ByteBuf}.
 * A typical setup for a text-based line protocol in a TCP/IP socket would be:
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
// CLR: a CharSequence is "a readable sequence of char values"
// (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/CharSequence.html). System.String implements
// IEnumerable<char> but not ICharSequence (https://learn.microsoft.com/dotnet/api/system.string), so the message type is
// IEnumerable<char>, the interface shared by string and the ICharSequence types (AsciiString, StringCharSequence,
// AppendableCharSequence). java.nio.charset.Charset is System.Text.Encoding, as in ByteBuf.ToString(Encoding charset).
[Sharable]
public class StringEncoder : MessageToMessageEncoder<IEnumerable<char>>
{
    private readonly Encoding charset;

    /**
     * Creates a new instance with the current system character set.
     */
    // CLR: Charset.defaultCharset() is UTF-8 unless changed in an implementation specific manner
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/nio/charset/Charset.html#defaultCharset());
    // Encoding.Default always returns a UTF8Encoding on .NET Core and later
    // (https://learn.microsoft.com/dotnet/api/system.text.encoding.default).
    public StringEncoder() : this(Encoding.Default)
    {
    }

    /**
     * Creates a new instance with the specified character set.
     */
    public StringEncoder(Encoding charset) : base(typeof(IEnumerable<char>))
    {
        this.charset = ObjectUtil.CheckNotNull(charset, "charset");
    }

    protected override void Encode(IChannelHandlerContext ctx, IEnumerable<char> msg, IList<object> @out)
    {
        // CLR: msg.length() is the length of the wrapped characters (see Wrap).
        string chars = Wrap(msg);
        if (chars.Length == 0)
        {
            return;
        }

        @out.Add(ByteBufUtil.EncodeString(ctx.Alloc(), chars, charset));
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
