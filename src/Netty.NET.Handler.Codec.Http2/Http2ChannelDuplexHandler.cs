/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A {@link ChannelDuplexHandler} providing additional functionality for HTTP/2. Specifically it allows to:
 * <ul>
 *     <li>Create new outbound streams using {@link #newStream()}.</li>
 *     <li>Iterate over all active streams using {@link #forEachActiveStream(Http2FrameStreamVisitor)}.</li>
 * </ul>
 *
 * <p>The {@link Http2FrameCodec} is required to be part of the {@link ChannelPipeline} before this handler is added,
 * or else an {@link IllegalStateException} will be thrown.
 */
// CLR: the final handlerAdded/handlerRemoved are sealed overrides of the Transport's ChannelHandlerAdapter members;
// IllegalStateException is InvalidOperationException and IllegalArgumentException ArgumentException
// (docs/codec-base-porting.md, Exceptions); pipeline().context(Class) is Context(Type) (docs/transport-porting.md,
// Interfaces added); Class.getSimpleName() and StringUtil.simpleClassName are StringUtil.SimpleClassName(Type). The
// checked "throws Exception"/"throws Http2Exception" clauses have no counterpart.
public abstract class Http2ChannelDuplexHandler : ChannelDuplexHandler
{

    private volatile Http2FrameCodec frameCodec;

    public sealed override void HandlerAdded(IChannelHandlerContext ctx)
    {
        frameCodec = RequireHttp2FrameCodec(ctx);
        HandlerAdded0(ctx);
    }

    protected virtual void HandlerAdded0(IChannelHandlerContext ctx)
    {
        // NOOP
    }

    public sealed override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        try
        {
            HandlerRemoved0(ctx);
        }
        finally
        {
            frameCodec = null;
        }
    }

    protected virtual void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        // NOOP
    }

    /**
     * Creates a new {@link Http2FrameStream} object.
     *
     * <p>This method is <em>thread-safe</em>.
     */
    public IHttp2FrameStream NewStream()
    {
        Http2FrameCodec codec = frameCodec;
        if (codec == null)
        {
            throw new InvalidOperationException(StringUtil.SimpleClassName(typeof(Http2FrameCodec)) + " not found." +
                    " Has the handler been added to a pipeline?");
        }
        return codec.NewStream();
    }

    /**
     * Allows to iterate over all currently active streams.
     *
     * <p>This method may only be called from the eventloop thread.
     */
    protected void ForEachActiveStream(IHttp2FrameStreamVisitor streamVisitor)
    {
        frameCodec.ForEachActiveStream(streamVisitor);
    }

    private static Http2FrameCodec RequireHttp2FrameCodec(IChannelHandlerContext ctx)
    {
        IChannelHandlerContext frameCodecCtx = ctx.Pipeline.Context(typeof(Http2FrameCodec));
        if (frameCodecCtx == null)
        {
            throw new ArgumentException(StringUtil.SimpleClassName(typeof(Http2FrameCodec))
                                               + " was not found in the channel pipeline.");
        }
        return (Http2FrameCodec) frameCodecCtx.Handler;
    }
}
