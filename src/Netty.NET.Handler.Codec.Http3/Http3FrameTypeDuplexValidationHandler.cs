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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http3.Http3FrameValidationUtils;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private class is internal; ChannelOutboundHandler is the Transport's IChannelOutboundHandler, whose
// operations return a Task instead of completing a ChannelPromise argument (docs/codec-base-porting.md, Outbound
// operations). The final write(ctx, Object, ChannelPromise) is WriteAsync(ctx, msg, cancellationToken) (not virtual),
// which creates the promise (Http3CodecUtils.NewPromise()), runs the Java body and returns the promise's Task; the
// package-private overridable write(ctx, T, ChannelPromise) and writeFrameDiscarded(...) are internal virtual members
// that keep the TaskCompletionSource parameter, and ctx.write(msg, promise) is Http3CodecUtils.Write. The other
// operations pass their CancellationToken to the context and return its Task. SocketAddress is EndPoint. throws
// Exception has no counterpart.
internal class Http3FrameTypeDuplexValidationHandler<T> : Http3FrameTypeInboundValidationHandler<T>, IChannelOutboundHandler
    where T : class, IHttp3Frame
{

    internal Http3FrameTypeDuplexValidationHandler(Type frameType) : base(frameType)
    {
    }

    public Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
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

    public virtual void Flush(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    public virtual Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken)
    {
        return ctx.BindAsync(localAddress, cancellationToken);
    }

    public virtual Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress,
                                     CancellationToken cancellationToken)
    {
        return ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public virtual Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DisconnectAsync(cancellationToken);
    }

    public virtual Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.CloseAsync(cancellationToken);
    }

    public virtual Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DeregisterAsync(cancellationToken);
    }

    public virtual void Read(IChannelHandlerContext ctx)
    {
        ctx.Read();
    }
}
