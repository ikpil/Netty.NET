/*
 * Copyright 2012 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.SocksProxy;

// CLR: Promise<Channel> is TaskCompletionSource<IChannel> (docs/transport-porting.md: TaskCompletionSource producers
// replace ChannelPromise), so promise.setSuccess is SetResult and promise.setFailure SetException; both also throw when
// the promise is already complete, as setSuccess/setFailure throw IllegalStateException.
public sealed class DirectClientHandler : ChannelInboundHandlerAdapter
{

    private readonly TaskCompletionSource<IChannel> promise;

    public DirectClientHandler(TaskCompletionSource<IChannel> promise)
    {
        this.promise = promise;
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        ctx.Pipeline.Remove(this);
        promise.SetResult(ctx.Channel);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception throwable)
    {
        promise.SetException(throwable);
    }
}
