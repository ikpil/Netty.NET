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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * Encodes a SPDY Frame into a {@link ByteBuf}.
 */
// CLR: ByteBufAllocator is IByteBufAllocator. allocator.ioBuffer(n).order(ByteOrder.BIG_ENDIAN) is IoBuffer(n): the
// Buffer port has no order(ByteOrder) view, and the plain writeShort/writeMedium/writeInt accessors write big-endian
// (docs/buffer-porting.md, Explicit endian replacements for swapped buffers). The Java byte flags and priority are CLR
// bytes; flags are combined with |= on byte values, and (priority & 0xFF) << 13 gives the same short for either sign.
// Set<Integer> is ISet<int>. Java methods are virtual unless final.
public class SpdyFrameEncoder
{

    private readonly int version;

    /**
     * Creates a new instance with the specified {@code spdyVersion}.
     */
    public SpdyFrameEncoder(SpdyVersion spdyVersion)
    {
        version = ObjectUtil.CheckNotNull(spdyVersion, "spdyVersion").Version();
    }

    protected virtual void WriteControlFrameHeader(ByteBuf buffer, int type, byte flags, int length)
    {
        buffer.WriteShort(version | 0x8000);
        buffer.WriteShort(type);
        buffer.WriteByte(flags);
        buffer.WriteMedium(length);
    }

    public virtual ByteBuf EncodeDataFrame(IByteBufAllocator allocator, int streamId, bool last, ByteBuf data)
    {
        byte flags = last ? SPDY_DATA_FLAG_FIN : (byte) 0;
        int length = data.ReadableBytes;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        frame.WriteInt(streamId & 0x7FFFFFFF);
        frame.WriteByte(flags);
        frame.WriteMedium(length);
        frame.WriteBytes(data, data.ReaderIndex, length);
        return frame;
    }

    public virtual ByteBuf EncodeSynStreamFrame(IByteBufAllocator allocator,  int streamId, int associatedToStreamId,
            byte priority, bool last, bool unidirectional, ByteBuf headerBlock)
    {
        int headerBlockLength = headerBlock.ReadableBytes;
        byte flags = last ? SPDY_FLAG_FIN : (byte) 0;

        if (unidirectional)
        {
            flags |= SPDY_FLAG_UNIDIRECTIONAL;
        }

        int length = 10 + headerBlockLength;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        WriteControlFrameHeader(frame, SPDY_SYN_STREAM_FRAME, flags, length);
        frame.WriteInt(streamId);
        frame.WriteInt(associatedToStreamId);
        frame.WriteShort((priority & 0xFF) << 13);
        frame.WriteBytes(headerBlock, headerBlock.ReaderIndex, headerBlockLength);
        return frame;
    }

    public virtual ByteBuf EncodeSynReplyFrame(IByteBufAllocator allocator, int streamId, bool last,
                                               ByteBuf headerBlock)
    {
        int headerBlockLength = headerBlock.ReadableBytes;
        byte flags = last ? SPDY_FLAG_FIN : (byte) 0;
        int length = 4 + headerBlockLength;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        WriteControlFrameHeader(frame, SPDY_SYN_REPLY_FRAME, flags, length);
        frame.WriteInt(streamId);
        frame.WriteBytes(headerBlock, headerBlock.ReaderIndex, headerBlockLength);
        return frame;
    }

    public virtual ByteBuf EncodeRstStreamFrame(IByteBufAllocator allocator, int streamId, int statusCode)
    {
        byte flags = 0;
        int length = 8;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        WriteControlFrameHeader(frame, SPDY_RST_STREAM_FRAME, flags, length);
        frame.WriteInt(streamId);
        frame.WriteInt(statusCode);
        return frame;
    }

    public virtual ByteBuf EncodeSettingsFrame(IByteBufAllocator allocator, ISpdySettingsFrame spdySettingsFrame)
    {
        ISet<int> ids = spdySettingsFrame.Ids();
        int numSettings = ids.Count;

        byte flags = spdySettingsFrame.ClearPreviouslyPersistedSettings() ?
                SPDY_SETTINGS_CLEAR : (byte) 0;
        int length = 4 + 8 * numSettings;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        WriteControlFrameHeader(frame, SPDY_SETTINGS_FRAME, flags, length);
        frame.WriteInt(numSettings);

        foreach (int id in ids)
        {
            flags = 0;

            if (spdySettingsFrame.IsPersistValue(id))
            {
                flags |= SPDY_SETTINGS_PERSIST_VALUE;
            }

            if (spdySettingsFrame.IsPersisted(id))
            {
                flags |= SPDY_SETTINGS_PERSISTED;
            }

            frame.WriteByte(flags);
            frame.WriteMedium(id);
            frame.WriteInt(spdySettingsFrame.GetValue(id));
        }

        return frame;
    }

    public virtual ByteBuf EncodePingFrame(IByteBufAllocator allocator, int id)
    {
        byte flags = 0;
        int length = 4;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        WriteControlFrameHeader(frame, SPDY_PING_FRAME, flags, length);
        frame.WriteInt(id);
        return frame;
    }

    public virtual ByteBuf EncodeGoAwayFrame(IByteBufAllocator allocator, int lastGoodStreamId, int statusCode)
    {
        byte flags = 0;
        int length = 8;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        WriteControlFrameHeader(frame, SPDY_GOAWAY_FRAME, flags, length);
        frame.WriteInt(lastGoodStreamId);
        frame.WriteInt(statusCode);
        return frame;
    }

    public virtual ByteBuf EncodeHeadersFrame(IByteBufAllocator allocator, int streamId, bool last, ByteBuf headerBlock)
    {
        int headerBlockLength = headerBlock.ReadableBytes;
        byte flags = last ? SPDY_FLAG_FIN : (byte) 0;
        int length = 4 + headerBlockLength;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        WriteControlFrameHeader(frame, SPDY_HEADERS_FRAME, flags, length);
        frame.WriteInt(streamId);
        frame.WriteBytes(headerBlock, headerBlock.ReaderIndex, headerBlockLength);
        return frame;
    }

    public virtual ByteBuf EncodeWindowUpdateFrame(IByteBufAllocator allocator, int streamId, int deltaWindowSize)
    {
        byte flags = 0;
        int length = 8;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        WriteControlFrameHeader(frame, SPDY_WINDOW_UPDATE_FRAME, flags, length);
        frame.WriteInt(streamId);
        frame.WriteInt(deltaWindowSize);
        return frame;
    }

    public virtual ByteBuf EncodeUnknownFrame(IByteBufAllocator allocator, int frameType, byte flags, ByteBuf data)
    {
        int length = data.ReadableBytes;
        ByteBuf frame = allocator.IoBuffer(SPDY_HEADER_SIZE + length);
        WriteControlFrameHeader(frame, frameType, flags, length);

        if (length > 0)
        {
            frame.WriteBytes(data, data.ReaderIndex, length);
        }

        return frame;
    }
}
