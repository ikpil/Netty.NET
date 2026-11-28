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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http3.Http3FrameValidationUtils;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private class is internal. Class<T> is System.Type (see Http3FrameValidationUtils); T keeps the bound
// IHttp3Frame and adds the class constraint of the validate methods. The final write(ctx, Object, ChannelPromise) is the
// sealed override WriteAsync(ctx, msg, cancellationToken), which creates the promise (Http3CodecUtils.NewPromise()),
// runs the Java body and returns the promise's Task (docs/codec-http2-porting.md, Shared CLR adaptations); the
// package-private overridable write(ctx, T, ChannelPromise) and writeFrameDiscarded(...) are internal virtual members
// that keep the TaskCompletionSource parameter, and ctx.write(msg, promise) is Http3CodecUtils.Write.
internal class Http3FrameTypeOutboundValidationHandler<T> : ChannelOutboundHandlerAdapter where T : class, IHttp3Frame
{

    private readonly Type frameType;

    internal Http3FrameTypeOutboundValidationHandler(Type frameType)
    {
        this.frameType = ObjectUtil.CheckNotNull(frameType, "frameType");
    }

    public sealed override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = Http3CodecUtils.NewPromise();
        T frame = ValidateFrameWritten<T>(frameType, msg);
        if (frame != null)
        {
            Write(ctx, frame, promise);
        }
        else
        {
            WriteFrameDiscarded(msg, promise);
        }

        return promise.Task;
    }

    internal virtual void Write(IChannelHandlerContext ctx, T msg, TaskCompletionSource promise)
    {
        Http3CodecUtils.Write(ctx, msg, promise);
    }

    internal virtual void WriteFrameDiscarded(object discardedFrame, TaskCompletionSource promise)
    {
        FrameTypeUnexpected(promise, discardedFrame);
    }
}
