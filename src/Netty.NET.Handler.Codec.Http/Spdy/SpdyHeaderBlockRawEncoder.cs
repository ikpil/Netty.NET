/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;

namespace Netty.NET.Handler.Codec.Spdy;

// CLR: IllegalArgumentException is ArgumentException. Encode is public as in Java (see SpdyHeaderBlockEncoder);
// end() stays internal.
public class SpdyHeaderBlockRawEncoder : SpdyHeaderBlockEncoder
{

    private readonly int version;

    public SpdyHeaderBlockRawEncoder(SpdyVersion version)
    {
        this.version = ObjectUtil.CheckNotNull(version, "version").Version();
    }

    private static void SetLengthField(ByteBuf buffer, int writerIndex, int length)
    {
        buffer.SetInt(writerIndex, length);
    }

    private static void WriteLengthField(ByteBuf buffer, int length)
    {
        buffer.WriteInt(length);
    }

    public override ByteBuf Encode(IByteBufAllocator alloc, ISpdyHeadersFrame frame)
    {
        ISet<ICharSequence> names = frame.Headers().Names();
        int numHeaders = names.Count;
        if (numHeaders == 0)
        {
            return Unpooled.EmptyBuffer;
        }
        if (numHeaders > SPDY_MAX_NV_LENGTH)
        {
            throw new ArgumentException(
                    "header block contains too many headers");
        }

        ByteBuf headerBlock = alloc.HeapBuffer();
        WriteLengthField(headerBlock, numHeaders);

        foreach (ICharSequence name in names)
        {
            WriteLengthField(headerBlock, name.Length());
            ByteBufUtil.WriteAscii(headerBlock, name.ToString());
            int savedIndex = headerBlock.WriterIndex;
            int valueLength = 0;
            WriteLengthField(headerBlock, valueLength);

            foreach (ICharSequence value in frame.Headers().GetAll(name))
            {
                int length = value.Length();
                if (length > 0)
                {
                    ByteBufUtil.WriteAscii(headerBlock, value.ToString());
                    headerBlock.WriteByte(0);
                    valueLength += length + 1;
                }
            }

            if (valueLength != 0)
            {
                valueLength --;
            }

            if (valueLength > SPDY_MAX_NV_LENGTH)
            {
                throw new ArgumentException(
                        "header exceeds allowable length: " + name);
            }

            if (valueLength > 0)
            {
                SetLengthField(headerBlock, savedIndex, valueLength);
                headerBlock.WriterIndex = headerBlock.WriterIndex - 1;
            }
        }

        return headerBlock;
    }

    internal override void End()
    {
    }
}
