/*
 * Copyright 2020 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.HAProxy;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.HAProxy;

// CLR: write(ctx, msg, promise) is WriteAsync, which returns the Task of ctx.WriteAsync in place of completing the
// caller's promise. future.addListener is a continuation of that Task on the context's event loop
// (EventExecutorTaskScheduler, the timing of DefaultPromise.notifyListeners()). HAProxyHandler.this is this, and
// ctx.close() discards its Task as Java discards the future.
public class HAProxyHandler : ChannelOutboundHandlerAdapter
{

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        ctx.Pipeline.AddBefore(ctx.Name, null, HAProxyMessageEncoder.INSTANCE);
        base.HandlerAdded(ctx);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        Task future = ctx.WriteAsync(msg, cancellationToken);

        if (msg is HAProxyMessage)
        {
            _ = future.ContinueWith(future1 =>
            {
                if (future1.IsCompletedSuccessfully)
                {
                    ctx.Pipeline.Remove(HAProxyMessageEncoder.INSTANCE);
                    ctx.Pipeline.Remove(this);
                }
                else
                {
                    _ = ctx.CloseAsync();
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
        }

        return future;
    }
}
