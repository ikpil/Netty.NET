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
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * Decodes {@link ByteBuf}s into SPDY Frames.
 */
// CLR: SpdyFrameDecoderDelegate is ISpdyFrameDecoderDelegate and the field delegate is @delegate (a C# keyword). The
// package-private DEFAULT_MAX_NUM_SETTINGS is an internal constant. The simple enum State is a private C# enum; the
// unreachable default branch (java.lang.Error) throws InvalidOperationException, as codec-base AsciiHeadersEncoder does
// for an unexpected constant. Java methods are virtual unless final. ByteBuf.getByte returns the unsigned CLR byte
// (owner decision); the Java results are kept: the control bit is masked (& 0x80), the priority is shifted and masked
// (>> 5 & 0x07, the same three bits for a signed or unsigned octet), and the flag tests (flags & flag, flags == 0)
// compare bit patterns. The Java byte flags and priority passed to the delegate are CLR bytes (the wire octets).
// Numbers in the frame error text use the invariant culture.
public class SpdyFrameDecoder
{

    protected readonly ISpdyFrameDecoderDelegate @delegate;
    protected readonly int spdyVersion;
    internal const int DEFAULT_MAX_NUM_SETTINGS = 64;
    private readonly int maxChunkSize;
    private readonly int maxNumSettings;

    private int frameType;
    private State state;

    // SPDY common header fields
    private byte flags;
    private int length;
    private int streamId;

    private int numSettings;

    private enum State
    {
        READ_COMMON_HEADER,
        READ_DATA_FRAME,
        READ_SYN_STREAM_FRAME,
        READ_SYN_REPLY_FRAME,
        READ_RST_STREAM_FRAME,
        READ_SETTINGS_FRAME,
        READ_SETTING,
        READ_PING_FRAME,
        READ_GOAWAY_FRAME,
        READ_HEADERS_FRAME,
        READ_WINDOW_UPDATE_FRAME,
        READ_UNKNOWN_FRAME,
        READ_HEADER_BLOCK,
        DISCARD_FRAME,
        FRAME_ERROR
    }

    /**
     * Creates a new instance with the specified {@code version}
     * and the default {@code maxChunkSize (8192)}.
     */
    public SpdyFrameDecoder(SpdyVersion spdyVersion, ISpdyFrameDecoderDelegate @delegate)
        : this(spdyVersion, @delegate, 8192)
    {
    }

    /**
     * Creates a new instance with the specified parameters.
     */
    public SpdyFrameDecoder(SpdyVersion spdyVersion, ISpdyFrameDecoderDelegate @delegate, int maxChunkSize)
        : this(spdyVersion, @delegate, maxChunkSize, DEFAULT_MAX_NUM_SETTINGS)
    {
    }

    /**
     * Creates a new instance with the specified parameters.
     */
    public SpdyFrameDecoder(SpdyVersion spdyVersion, ISpdyFrameDecoderDelegate @delegate,
                            int maxChunkSize, int maxNumSettings)
    {
        this.spdyVersion = ObjectUtil.CheckNotNull(spdyVersion, "spdyVersion").Version();
        this.@delegate = ObjectUtil.CheckNotNull(@delegate, "delegate");
        this.maxChunkSize = ObjectUtil.CheckPositive(maxChunkSize, "maxChunkSize");
        this.maxNumSettings = ObjectUtil.CheckPositive(maxNumSettings, "maxNumSettings");
        state = State.READ_COMMON_HEADER;
    }

    public virtual void Decode(ByteBuf buffer)
    {
        bool last;
        int statusCode;

        while (true)
        {
            switch(state)
            {
                case State.READ_COMMON_HEADER:
                    if (buffer.ReadableBytes < SPDY_HEADER_SIZE)
                    {
                        return;
                    }

                    int frameOffset  = buffer.ReaderIndex;
                    int flagsOffset  = frameOffset + SPDY_HEADER_FLAGS_OFFSET;
                    int lengthOffset = frameOffset + SPDY_HEADER_LENGTH_OFFSET;
                    buffer.SkipBytes(SPDY_HEADER_SIZE);

                    bool control = (buffer.GetByte(frameOffset) & 0x80) != 0;

                    int version;
                    if (control)
                    {
                        // Decode control frame common header
                        version = GetUnsignedShort(buffer, frameOffset) & 0x7FFF;
                        frameType = GetUnsignedShort(buffer, frameOffset + SPDY_HEADER_TYPE_OFFSET);
                        streamId = 0; // Default to session Stream-ID
                    }
                    else
                    {
                        // Decode data frame common header
                        version = spdyVersion; // Default to expected version
                        frameType = SPDY_DATA_FRAME;
                        streamId = GetUnsignedInt(buffer, frameOffset);
                    }

                    flags  = buffer.GetByte(flagsOffset);
                    length = GetUnsignedMedium(buffer, lengthOffset);

                    // Check version first then validity
                    if (version != spdyVersion)
                    {
                        state = State.FRAME_ERROR;
                        @delegate.ReadFrameError("Invalid SPDY Version");
                    }
                    else if (!IsValidFrameHeader(streamId, frameType, flags, length))
                    {
                        state = State.FRAME_ERROR;
                        @delegate.ReadFrameError("Invalid Frame Error");
                    }
                    else if (IsValidUnknownFrameHeader(streamId, frameType, flags, length))
                    {
                        state = State.READ_UNKNOWN_FRAME;
                    }
                    else
                    {
                        state = GetNextState(frameType, length);
                    }
                    break;

                case State.READ_DATA_FRAME:
                    if (length == 0)
                    {
                        state = State.READ_COMMON_HEADER;
                        @delegate.ReadDataFrame(streamId, HasFlag(flags, SPDY_DATA_FLAG_FIN), Unpooled.Buffer(0));
                        break;
                    }

                    // Generate data frames that do not exceed maxChunkSize
                    int dataLength = Math.Min(maxChunkSize, length);

                    // Wait until entire frame is readable
                    if (buffer.ReadableBytes < dataLength)
                    {
                        return;
                    }

                    ByteBuf data = buffer.ReadRetainedSlice(dataLength);
                    length -= dataLength;

                    if (length == 0)
                    {
                        state = State.READ_COMMON_HEADER;
                    }

                    last = length == 0 && HasFlag(flags, SPDY_DATA_FLAG_FIN);

                    @delegate.ReadDataFrame(streamId, last, data);
                    break;

                case State.READ_SYN_STREAM_FRAME:
                    if (buffer.ReadableBytes < 10)
                    {
                        return;
                    }

                    int offset = buffer.ReaderIndex;
                    streamId = GetUnsignedInt(buffer, offset);
                    int associatedToStreamId = GetUnsignedInt(buffer, offset + 4);
                    byte priority = (byte) (buffer.GetByte(offset + 8) >> 5 & 0x07);
                    last = HasFlag(flags, SPDY_FLAG_FIN);
                    bool unidirectional = HasFlag(flags, SPDY_FLAG_UNIDIRECTIONAL);
                    buffer.SkipBytes(10);
                    length -= 10;

                    if (streamId == 0)
                    {
                        state = State.FRAME_ERROR;
                        @delegate.ReadFrameError("Invalid SYN_STREAM Frame");
                    }
                    else
                    {
                        state = State.READ_HEADER_BLOCK;
                        @delegate.ReadSynStreamFrame(streamId, associatedToStreamId, priority, last, unidirectional);
                    }
                    break;

                case State.READ_SYN_REPLY_FRAME:
                    if (buffer.ReadableBytes < 4)
                    {
                        return;
                    }

                    streamId = GetUnsignedInt(buffer, buffer.ReaderIndex);
                    last = HasFlag(flags, SPDY_FLAG_FIN);

                    buffer.SkipBytes(4);
                    length -= 4;

                    if (streamId == 0)
                    {
                        state = State.FRAME_ERROR;
                        @delegate.ReadFrameError("Invalid SYN_REPLY Frame");
                    }
                    else
                    {
                        state = State.READ_HEADER_BLOCK;
                        @delegate.ReadSynReplyFrame(streamId, last);
                    }
                    break;

                case State.READ_RST_STREAM_FRAME:
                    if (buffer.ReadableBytes < 8)
                    {
                        return;
                    }

                    streamId = GetUnsignedInt(buffer, buffer.ReaderIndex);
                    statusCode = GetSignedInt(buffer, buffer.ReaderIndex + 4);
                    buffer.SkipBytes(8);

                    if (streamId == 0 || statusCode == 0)
                    {
                        state = State.FRAME_ERROR;
                        @delegate.ReadFrameError("Invalid RST_STREAM Frame");
                    }
                    else
                    {
                        state = State.READ_COMMON_HEADER;
                        @delegate.ReadRstStreamFrame(streamId, statusCode);
                    }
                    break;

                case State.READ_SETTINGS_FRAME:
                    if (buffer.ReadableBytes < 4)
                    {
                        return;
                    }

                    bool clear = HasFlag(flags, SPDY_SETTINGS_CLEAR);

                    numSettings = GetUnsignedInt(buffer, buffer.ReaderIndex);
                    buffer.SkipBytes(4);
                    length -= 4;

                    // Validate frame length against number of entries. Each ID/Value entry is 8 bytes.
                    if ((length & 0x07) != 0 || length >> 3 != numSettings)
                    {
                        state = State.FRAME_ERROR;
                        @delegate.ReadFrameError("Invalid SETTINGS Frame");
                    }
                    else if (numSettings > maxNumSettings)
                    {
                        state = State.FRAME_ERROR;
                        @delegate.ReadFrameError("Invalid SETTINGS Frame (allowed number of settings exceeded: "
                                + numSettings.ToString(CultureInfo.InvariantCulture) + " > "
                                + maxNumSettings.ToString(CultureInfo.InvariantCulture) + ')');
                    }
                    else
                    {
                        state = State.READ_SETTING;
                        @delegate.ReadSettingsFrame(clear);
                    }
                    break;

                case State.READ_SETTING:
                    if (numSettings == 0)
                    {
                        state = State.READ_COMMON_HEADER;
                        @delegate.ReadSettingsEnd();
                        break;
                    }

                    if (buffer.ReadableBytes < 8)
                    {
                        return;
                    }

                    byte settingsFlags = buffer.GetByte(buffer.ReaderIndex);
                    int id = GetUnsignedMedium(buffer, buffer.ReaderIndex + 1);
                    int value = GetSignedInt(buffer, buffer.ReaderIndex + 4);
                    bool persistValue = HasFlag(settingsFlags, SPDY_SETTINGS_PERSIST_VALUE);
                    bool persisted = HasFlag(settingsFlags, SPDY_SETTINGS_PERSISTED);
                    buffer.SkipBytes(8);

                    --numSettings;

                    @delegate.ReadSetting(id, value, persistValue, persisted);
                    break;

                case State.READ_PING_FRAME:
                    if (buffer.ReadableBytes < 4)
                    {
                        return;
                    }

                    int pingId = GetSignedInt(buffer, buffer.ReaderIndex);
                    buffer.SkipBytes(4);

                    state = State.READ_COMMON_HEADER;
                    @delegate.ReadPingFrame(pingId);
                    break;

                case State.READ_GOAWAY_FRAME:
                    if (buffer.ReadableBytes < 8)
                    {
                        return;
                    }

                    int lastGoodStreamId = GetUnsignedInt(buffer, buffer.ReaderIndex);
                    statusCode = GetSignedInt(buffer, buffer.ReaderIndex + 4);
                    buffer.SkipBytes(8);

                    state = State.READ_COMMON_HEADER;
                    @delegate.ReadGoAwayFrame(lastGoodStreamId, statusCode);
                    break;

                case State.READ_HEADERS_FRAME:
                    if (buffer.ReadableBytes < 4)
                    {
                        return;
                    }

                    streamId = GetUnsignedInt(buffer, buffer.ReaderIndex);
                    last = HasFlag(flags, SPDY_FLAG_FIN);

                    buffer.SkipBytes(4);
                    length -= 4;

                    if (streamId == 0)
                    {
                        state = State.FRAME_ERROR;
                        @delegate.ReadFrameError("Invalid HEADERS Frame");
                    }
                    else
                    {
                        state = State.READ_HEADER_BLOCK;
                        @delegate.ReadHeadersFrame(streamId, last);
                    }
                    break;

                case State.READ_WINDOW_UPDATE_FRAME:
                    if (buffer.ReadableBytes < 8)
                    {
                        return;
                    }

                    streamId = GetUnsignedInt(buffer, buffer.ReaderIndex);
                    int deltaWindowSize = GetUnsignedInt(buffer, buffer.ReaderIndex + 4);
                    buffer.SkipBytes(8);

                    if (deltaWindowSize == 0)
                    {
                        state = State.FRAME_ERROR;
                        @delegate.ReadFrameError("Invalid WINDOW_UPDATE Frame");
                    }
                    else
                    {
                        state = State.READ_COMMON_HEADER;
                        @delegate.ReadWindowUpdateFrame(streamId, deltaWindowSize);
                    }
                    break;

                case State.READ_UNKNOWN_FRAME:
                    if (DecodeUnknownFrame(frameType, flags, length, buffer))
                    {
                        state = State.READ_COMMON_HEADER;
                        break;
                    }
                    return;

                case State.READ_HEADER_BLOCK:
                    if (length == 0)
                    {
                        state = State.READ_COMMON_HEADER;
                        @delegate.ReadHeaderBlockEnd();
                        break;
                    }

                    if (!buffer.IsReadable())
                    {
                        return;
                    }

                    int compressedBytes = Math.Min(buffer.ReadableBytes, length);
                    ByteBuf headerBlock = buffer.ReadRetainedSlice(compressedBytes);
                    length -= compressedBytes;

                    @delegate.ReadHeaderBlock(headerBlock);
                    break;

                case State.DISCARD_FRAME:
                    int numBytes = Math.Min(buffer.ReadableBytes, length);
                    buffer.SkipBytes(numBytes);
                    length -= numBytes;
                    if (length == 0)
                    {
                        state = State.READ_COMMON_HEADER;
                        break;
                    }
                    return;

                case State.FRAME_ERROR:
                    buffer.SkipBytes(buffer.ReadableBytes);
                    return;

                default:
                    throw new InvalidOperationException("Unexpected state: " + state);
            }
        }
    }

    private static bool HasFlag(byte flags, byte flag)
    {
        return (flags & flag) != 0;
    }

    private static State GetNextState(int type, int length)
    {
        switch (type)
        {
            case SPDY_DATA_FRAME:
                return State.READ_DATA_FRAME;

            case SPDY_SYN_STREAM_FRAME:
                return State.READ_SYN_STREAM_FRAME;

            case SPDY_SYN_REPLY_FRAME:
                return State.READ_SYN_REPLY_FRAME;

            case SPDY_RST_STREAM_FRAME:
                return State.READ_RST_STREAM_FRAME;

            case SPDY_SETTINGS_FRAME:
                return State.READ_SETTINGS_FRAME;

            case SPDY_PING_FRAME:
                return State.READ_PING_FRAME;

            case SPDY_GOAWAY_FRAME:
                return State.READ_GOAWAY_FRAME;

            case SPDY_HEADERS_FRAME:
                return State.READ_HEADERS_FRAME;

            case SPDY_WINDOW_UPDATE_FRAME:
                return State.READ_WINDOW_UPDATE_FRAME;

            default:

                if (length != 0)
                {
                    return State.DISCARD_FRAME;
                }
                else
                {
                    return State.READ_COMMON_HEADER;
                }
        }
    }

    /**
     * Decode the unknown frame, returns true if parsed something, otherwise false.
     */
    protected virtual bool DecodeUnknownFrame(int frameType, byte flags, int length, ByteBuf buffer)
    {
        if (length == 0)
        {
            @delegate.ReadUnknownFrame(frameType, flags, Unpooled.EmptyBuffer);
            return true;
        }
        if (buffer.ReadableBytes < length)
        {
            return false;
        }
        ByteBuf data = buffer.ReadRetainedSlice(length);
        @delegate.ReadUnknownFrame(frameType, flags, data);
        return true;
    }

    /**
     * Check whether the unknown frame is valid, if not, the frame will be discarded,
     * otherwise, the frame will be passed to {@link #decodeUnknownFrame(int, byte, int, ByteBuf)}.
     * */
    protected virtual bool IsValidUnknownFrameHeader(int streamId, int type, byte flags, int length)
    {
        return false;
    }

    private static bool IsValidFrameHeader(int streamId, int type, byte flags, int length)
    {
        switch (type)
        {
            case SPDY_DATA_FRAME:
                return streamId != 0;

            case SPDY_SYN_STREAM_FRAME:
                return length >= 10;

            case SPDY_SYN_REPLY_FRAME:
                return length >= 4;

            case SPDY_RST_STREAM_FRAME:
                return flags == 0 && length == 8;

            case SPDY_SETTINGS_FRAME:
                return length >= 4;

            case SPDY_PING_FRAME:
                return length == 4;

            case SPDY_GOAWAY_FRAME:
                return length == 8;

            case SPDY_HEADERS_FRAME:
                return length >= 4;

            case SPDY_WINDOW_UPDATE_FRAME:
                return length == 8;

            default:
                return true;
        }
    }
}
