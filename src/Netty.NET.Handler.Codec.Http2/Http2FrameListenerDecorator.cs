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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Provides a decorator around a {@link Http2FrameListener} and delegates all method calls
 */
// CLR: the overridable Java methods are virtual and the protected final field is protected internal readonly: Java's
// protected also grants package access, which Http2EmptyDataFrameConnectionDecoder and Http2MaxRstFrameDecoder use to
// unwrap the decorated listener.
// checkNotNull is ObjectUtil.CheckNotNull (ArgumentNullException, docs/codec-base-porting.md).
public class Http2FrameListenerDecorator : IHttp2FrameListener
{
    protected internal readonly IHttp2FrameListener listener;

    public Http2FrameListenerDecorator(IHttp2FrameListener listener)
    {
        this.listener = CheckNotNull(listener, "listener");
    }

    public virtual int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding, bool endOfStream)
    {
        return listener.OnDataRead(ctx, streamId, data, padding, endOfStream);
    }

    public virtual void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding,
            bool endStream)
    {
        listener.OnHeadersRead(ctx, streamId, headers, padding, endStream);
    }

    public virtual void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
            int streamDependency,
            short weight, bool exclusive, int padding, bool endStream)
    {
        listener.OnHeadersRead(ctx, streamId, headers, streamDependency, weight, exclusive, padding, endStream);
    }

    public virtual void OnPriorityRead(IChannelHandlerContext ctx, int streamId, int streamDependency, short weight,
            bool exclusive)
    {
        listener.OnPriorityRead(ctx, streamId, streamDependency, weight, exclusive);
    }

    public virtual void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
    {
        listener.OnRstStreamRead(ctx, streamId, errorCode);
    }

    public virtual void OnSettingsAckRead(IChannelHandlerContext ctx)
    {
        listener.OnSettingsAckRead(ctx);
    }

    public virtual void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
    {
        listener.OnSettingsRead(ctx, settings);
    }

    public virtual void OnPingRead(IChannelHandlerContext ctx, long data)
    {
        listener.OnPingRead(ctx, data);
    }

    public virtual void OnPingAckRead(IChannelHandlerContext ctx, long data)
    {
        listener.OnPingAckRead(ctx, data);
    }

    public virtual void OnPushPromiseRead(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
            IHttp2Headers headers,
            int padding)
    {
        listener.OnPushPromiseRead(ctx, streamId, promisedStreamId, headers, padding);
    }

    public virtual void OnGoAwayRead(IChannelHandlerContext ctx, int lastStreamId, long errorCode, ByteBuf debugData)
    {
        listener.OnGoAwayRead(ctx, lastStreamId, errorCode, debugData);
    }

    public virtual void OnWindowUpdateRead(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement)
    {
        listener.OnWindowUpdateRead(ctx, streamId, windowSizeIncrement);
    }

    public virtual void OnUnknownFrame(IChannelHandlerContext ctx, byte frameType, int streamId, Http2Flags flags,
            ByteBuf payload)
    {
        listener.OnUnknownFrame(ctx, frameType, streamId, flags, payload);
    }
}
