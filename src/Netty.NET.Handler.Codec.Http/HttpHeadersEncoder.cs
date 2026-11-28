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

using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http.HttpConstants;
using static Netty.NET.Handler.Codec.Http.HttpObjectEncoder;

namespace Netty.NET.Handler.Codec.Http;

// CLR: ByteBuf.setCharSequence(index, seq, US_ASCII) writes AsciiString.c2b(char) for each char in Java
// (AbstractByteBuf.setCharSequence0 -> ByteBufUtil.writeAscii: a char above 0xFF becomes '?', 0x80-0xFF keep their
// low byte), while Encoding.ASCII would replace every char above 0x7F; the C2b loop keeps the Java bytes, as the
// Buffer port's ByteBufUtil.WriteAscii does for writes at the writer index.
internal static class HttpHeadersEncoder
{
    private const int COLON_AND_SPACE_SHORT = (COLON << 8) | SP;

    internal static void EncoderHeader(ICharSequence name, ICharSequence value, ByteBuf buf)
    {
        int nameLen = name.Length();
        int valueLen = value.Length();
        int entryLen = nameLen + valueLen + 4;
        buf.EnsureWritable(entryLen);
        int offset = buf.WriterIndex;
        WriteAscii(buf, offset, name);
        offset += nameLen;
        ByteBufUtil.SetShortBE(buf, offset, COLON_AND_SPACE_SHORT);
        offset += 2;
        WriteAscii(buf, offset, value);
        offset += valueLen;
        ByteBufUtil.SetShortBE(buf, offset, CRLF_SHORT);
        offset += 2;
        buf.WriterIndex = offset;
    }

    private static void WriteAscii(ByteBuf buf, int offset, ICharSequence value)
    {
        if (value is AsciiString)
        {
            ByteBufUtil.Copy((AsciiString) value, 0, buf, offset, value.Length());
        }
        else
        {
            int length = value.Length();

            for (int i = 0; i < length; i++)
            {
                buf.SetByte(offset + i, AsciiString.C2b(value.CharAt(i)));
            }
        }
    }
}
