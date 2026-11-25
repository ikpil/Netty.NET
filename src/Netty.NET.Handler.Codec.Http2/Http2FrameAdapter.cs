/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Convenience class that provides no-op implementations for all methods of {@link Http2FrameListener}.
 */
// CLR: the overridable Java methods are virtual. The throws Http2Exception clauses have no C# counterpart.
public class Http2FrameAdapter : IHttp2FrameListener
{

    public virtual int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding,
            bool endOfStream)
    {
        return data.ReadableBytes + padding;
    }

    public virtual void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
            int padding, bool endStream)
    {
    }

    public virtual void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
            int streamDependency, short weight, bool exclusive, int padding, bool endStream)
    {
    }

    public virtual void OnPriorityRead(IChannelHandlerContext ctx, int streamId, int streamDependency,
            short weight, bool exclusive)
    {
    }

    public virtual void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
    {
    }

    public virtual void OnSettingsAckRead(IChannelHandlerContext ctx)
    {
    }

    public virtual void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
    {
    }

    public virtual void OnPingRead(IChannelHandlerContext ctx, long data)
    {
    }

    public virtual void OnPingAckRead(IChannelHandlerContext ctx, long data)
    {
    }

    public virtual void OnPushPromiseRead(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
            IHttp2Headers headers, int padding)
    {
    }

    public virtual void OnGoAwayRead(IChannelHandlerContext ctx, int lastStreamId, long errorCode,
            ByteBuf debugData)
    {
    }

    public virtual void OnWindowUpdateRead(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement)
    {
    }

    public virtual void OnUnknownFrame(IChannelHandlerContext ctx, byte frameType, int streamId, Http2Flags flags,
            ByteBuf payload)
    {
    }
}
