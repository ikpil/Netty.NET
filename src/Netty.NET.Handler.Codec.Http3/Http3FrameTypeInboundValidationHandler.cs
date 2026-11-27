/*
 * Copyright 2021 The Netty Project
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
using static Netty.NET.Handler.Codec.Http3.Http3FrameValidationUtils;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private class is internal. Class<T> is System.Type (see Http3FrameValidationUtils); T keeps the bound
// IHttp3Frame and adds the class constraint of the validate methods. The final channelRead(ctx, Object) is a sealed
// override and the package-private overridable channelRead(ctx, T) and readFrameDiscarded(...) are internal virtual
// overloads, which the package's handlers override; the T overload is the better match for a T argument, as in Java.
// throws Exception has no counterpart.
internal class Http3FrameTypeInboundValidationHandler<T> : ChannelInboundHandlerAdapter where T : class, IHttp3Frame
{

    protected readonly Type frameType;

    internal Http3FrameTypeInboundValidationHandler(Type frameType)
    {
        this.frameType = ObjectUtil.CheckNotNull(frameType, "frameType");
    }

    public sealed override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        T frame = ValidateFrameRead<T>(frameType, msg);
        if (frame != null)
        {
            ChannelRead(ctx, frame);
        }
        else
        {
            ReadFrameDiscarded(ctx, msg);
        }
    }

    internal virtual void ChannelRead(IChannelHandlerContext ctx, T frame)
    {
        ctx.FireChannelRead(frame);
    }

    internal virtual void ReadFrameDiscarded(IChannelHandlerContext ctx, object discardedFrame)
    {
        FrameTypeUnexpected(ctx, discardedFrame);
    }
}
