/*
 * Copyright 2014 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec;

public sealed class AsciiHeadersEncoder
{

    /**
     * The separator characters to insert between a header name and a header value.
     */
    public enum SeparatorType
    {
        /**
         * {@code ':'}
         */
        COLON,
        /**
         * {@code ': '}
         */
        COLON_SPACE,
    }

    /**
     * The newline characters to insert between header entries.
     */
    public enum NewlineType
    {
        /**
         * {@code '\n'}
         */
        LF,
        /**
         * {@code '\r\n'}
         */
        CRLF
    }

    private readonly ByteBuf buf;
    private readonly SeparatorType separatorType;
    private readonly NewlineType newlineType;

    public AsciiHeadersEncoder(ByteBuf buf) : this(buf, SeparatorType.COLON_SPACE, NewlineType.CRLF)
    {
    }

    // CLR: the enum parameters are value types and cannot be null, so their checkNotNull calls have no counterpart.
    public AsciiHeadersEncoder(ByteBuf buf, SeparatorType separatorType, NewlineType newlineType)
    {
        this.buf = ObjectUtil.CheckNotNull(buf, "buf");
        this.separatorType = separatorType;
        this.newlineType = newlineType;
    }

    // CLR: Map.Entry is IEntry (see IHeaders.cs).
    public void Encode(IEntry<ICharSequence, ICharSequence> entry)
    {
        ICharSequence name = entry.GetKey();
        ICharSequence value = entry.GetValue();
        ByteBuf buf = this.buf;
        int nameLen = name.Length();
        int valueLen = value.Length();
        int entryLen = nameLen + valueLen + 4;
        int offset = buf.WriterIndex;
        buf.EnsureWritable(entryLen);
        WriteAscii(buf, offset, name);
        offset += nameLen;

        // CLR: java.lang.Error for an unexpected enum constant is InvalidOperationException; a C# enum variable can
        // hold an undefined value, so the default branch is reachable.
        switch (separatorType)
        {
            case SeparatorType.COLON:
                buf.SetByte(offset ++, ':');
                break;
            case SeparatorType.COLON_SPACE:
                buf.SetByte(offset ++, ':');
                buf.SetByte(offset ++, ' ');
                break;
            default:
                throw new InvalidOperationException("Unexpected separator type: " + separatorType);
        }

        WriteAscii(buf, offset, value);
        offset += valueLen;

        switch (newlineType)
        {
            case NewlineType.LF:
                buf.SetByte(offset ++, '\n');
                break;
            case NewlineType.CRLF:
                buf.SetByte(offset ++, '\r');
                buf.SetByte(offset ++, '\n');
                break;
            default:
                throw new InvalidOperationException("Unexpected newline type: " + newlineType);
        }

        buf.WriterIndex = offset;
    }

    // CLR: CharsetUtil.US_ASCII is Encoding.ASCII, which also replaces a character above U+007F with '?'; it
    // replaces each UTF-16 code unit of an unmappable surrogate pair, where Java's encoder writes one '?' per pair
    // (docs/common-ascii-memory.md). setCharSequence takes the sequence's characters as a span.
    private static void WriteAscii(ByteBuf buf, int offset, ICharSequence value)
    {
        if (value is AsciiString)
        {
            ByteBufUtil.Copy((AsciiString) value, 0, buf, offset, value.Length());
        }
        else
        {
            buf.SetCharSequence(offset, value.ToString(), Encoding.ASCII);
        }
    }
}
