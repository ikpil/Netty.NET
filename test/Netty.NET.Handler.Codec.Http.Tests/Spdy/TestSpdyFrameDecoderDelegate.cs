/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Handler.Codec.Spdy;

namespace Netty.NET.Handler.Codec.Http.Tests.Spdy;

// CLR: The field delegate is @delegate (a C# keyword). ArrayDeque<ByteBuf> is Queue<ByteBuf> and poll() is
// TryDequeue.
internal sealed class TestSpdyFrameDecoderDelegate : ISpdyFrameDecoderDelegate
{
    private readonly ISpdyFrameDecoderDelegate @delegate;

    private readonly Queue<ByteBuf> buffers = new Queue<ByteBuf>();

    internal TestSpdyFrameDecoderDelegate(ISpdyFrameDecoderDelegate @delegate)
    {
        this.@delegate = @delegate;
    }

    public void ReadDataFrame(int streamId, bool last, ByteBuf data)
    {
        @delegate.ReadDataFrame(streamId, last, data);
        buffers.Enqueue(data);
    }

    public void ReadSynStreamFrame(int streamId, int associatedToStreamId,
                                   byte priority, bool last, bool unidirectional)
    {
        @delegate.ReadSynStreamFrame(streamId, associatedToStreamId, priority, last, unidirectional);
    }

    public void ReadSynReplyFrame(int streamId, bool last)
    {
        @delegate.ReadSynReplyFrame(streamId, last);
    }

    public void ReadRstStreamFrame(int streamId, int statusCode)
    {
        @delegate.ReadRstStreamFrame(streamId, statusCode);
    }

    public void ReadSettingsFrame(bool clearPersisted)
    {
        @delegate.ReadSettingsFrame(clearPersisted);
    }

    public void ReadSetting(int id, int value, bool persistValue, bool persisted)
    {
        @delegate.ReadSetting(id, value, persistValue, persisted);
    }

    public void ReadSettingsEnd()
    {
        @delegate.ReadSettingsEnd();
    }

    public void ReadPingFrame(int id)
    {
        @delegate.ReadPingFrame(id);
    }

    public void ReadGoAwayFrame(int lastGoodStreamId, int statusCode)
    {
        @delegate.ReadGoAwayFrame(lastGoodStreamId, statusCode);
    }

    public void ReadHeadersFrame(int streamId, bool last)
    {
        @delegate.ReadHeadersFrame(streamId, last);
    }

    public void ReadWindowUpdateFrame(int streamId, int deltaWindowSize)
    {
        @delegate.ReadWindowUpdateFrame(streamId, deltaWindowSize);
    }

    public void ReadHeaderBlock(ByteBuf headerBlock)
    {
        @delegate.ReadHeaderBlock(headerBlock);
        buffers.Enqueue(headerBlock);
    }

    public void ReadHeaderBlockEnd()
    {
        @delegate.ReadHeaderBlockEnd();
    }

    public void ReadFrameError(string message)
    {
        @delegate.ReadFrameError(message);
    }

    public void ReadUnknownFrame(int frameType, byte flags, ByteBuf payload)
    {
        @delegate.ReadUnknownFrame(frameType, flags, payload);
        buffers.Enqueue(payload);
    }

    internal void ReleaseAll()
    {
        for (;;)
        {
            if (!buffers.TryDequeue(out ByteBuf buf))
            {
                return;
            }

            buf.Release();
        }
    }
}
