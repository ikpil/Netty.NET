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

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * Callback interface for {@link SpdyFrameDecoder}.
 */
// CLR: the Java interface takes the I prefix (owner rule). The default method readUnknownFrame is a default interface
// member (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface), called through the
// interface as Java calls it. The Java byte priority and flags are CLR bytes (the wire octets).
public interface ISpdyFrameDecoderDelegate
{

    /**
     * Called when a DATA frame is received.
     */
    void ReadDataFrame(int streamId, bool last, ByteBuf data);

    /**
     * Called when a SYN_STREAM frame is received.
     * The Name/Value Header Block is not included. See readHeaderBlock().
     */
    void ReadSynStreamFrame(
            int streamId, int associatedToStreamId, byte priority, bool last, bool unidirectional);

    /**
     * Called when a SYN_REPLY frame is received.
     * The Name/Value Header Block is not included. See readHeaderBlock().
     */
    void ReadSynReplyFrame(int streamId, bool last);

    /**
     * Called when a RST_STREAM frame is received.
     */
    void ReadRstStreamFrame(int streamId, int statusCode);

    /**
     * Called when a SETTINGS frame is received.
     * Settings are not included. See readSetting().
     */
    void ReadSettingsFrame(bool clearPersisted);

    /**
     * Called when an individual setting within a SETTINGS frame is received.
     */
    void ReadSetting(int id, int value, bool persistValue, bool persisted);

    /**
     * Called when the entire SETTINGS frame has been received.
     */
    void ReadSettingsEnd();

    /**
     * Called when a PING frame is received.
     */
    void ReadPingFrame(int id);

    /**
     * Called when a GOAWAY frame is received.
     */
    void ReadGoAwayFrame(int lastGoodStreamId, int statusCode);

    /**
     * Called when a HEADERS frame is received.
     * The Name/Value Header Block is not included. See readHeaderBlock().
     */
    void ReadHeadersFrame(int streamId, bool last);

    /**
     * Called when a WINDOW_UPDATE frame is received.
     */
    void ReadWindowUpdateFrame(int streamId, int deltaWindowSize);

    /**
     * Called when the header block within a SYN_STREAM, SYN_REPLY, or HEADERS frame is received.
     */
    void ReadHeaderBlock(ByteBuf headerBlock);

    /**
     * Called when an entire header block has been received.
     */
    void ReadHeaderBlockEnd();

    /**
     * Called when an unrecoverable session error has occurred.
     */
    void ReadFrameError(string message);

    /**
     * Called when an unknown frame is received.
     *
     * @param frameType the frame type from the spdy header.
     * @param flags the flags in the frame header.
     * @param payload the payload of the frame.
     */
    void ReadUnknownFrame(int frameType, byte flags, ByteBuf payload)
    {
        payload.Release();
    }
}
