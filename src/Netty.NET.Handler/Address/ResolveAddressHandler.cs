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

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Resolver;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Address;

/**
 * {@link ChannelOutboundHandlerAdapter} which will resolve the {@link SocketAddress} that is passed to
 * {@link #connect(ChannelHandlerContext, SocketAddress, SocketAddress, ChannelPromise)} if it is not already resolved
 * and the {@link AddressResolver} supports the type of {@link SocketAddress}.
 */
// CLR: the field and constructor parameter AddressResolverGroup<? extends SocketAddress> accept a group of any
// SocketAddress subtype. C# has no wildcard, a constructor cannot declare its own type parameter, and a generic class
// is invariant: "Variant type parameters are restricted to generic interface and generic delegate types"
// (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance), so
// AddressResolverGroup<IPEndPoint> (DefaultAddressResolverGroup) does not convert to AddressResolverGroup<EndPoint>.
// The wildcard is therefore the class type parameter T (bounded by EndPoint, the CLR SocketAddress); Bootstrap's
// resolver(AddressResolverGroup<?>) is the generic method Resolver<T> for the same reason.
[Sharable]
public class ResolveAddressHandler<T> : ChannelOutboundHandlerAdapter where T : EndPoint
{
    private readonly AddressResolverGroup<T> resolverGroup;

    public ResolveAddressHandler(AddressResolverGroup<T> resolverGroup)
    {
        this.resolverGroup = ObjectUtil.CheckNotNull(resolverGroup, "resolverGroup");
    }

    // CLR: the promise is the returned Task. On the resolution path a TaskCompletionSource stands for it: the
    // FutureListener is a synchronous continuation of the resolve Task (run at once when the Task is already complete,
    // https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcontinuationoptions?view=net-10.0),
    // future.cause() != null is a faulted or cancelled Task, whose outcome promise.setFailure(cause) copies through
    // TrySetFromTask, and ctx.connect(future.getNow(), localAddress, promise) copies the outcome of the connect Task
    // into the TaskCompletionSource (docs/common-task-composition.md).
    public override Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress,
                                      EndPoint localAddress, CancellationToken cancellationToken)
    {
        IAddressResolver<T> resolver = resolverGroup.GetResolver(ctx.Executor);
        if (resolver.IsSupported(remoteAddress) && !resolver.IsResolved(remoteAddress))
        {
            TaskCompletionSource promise = new TaskCompletionSource();
            _ = resolver.ResolveAsync(remoteAddress).ContinueWith(future =>
            {
                if (!future.IsCompletedSuccessfully)
                {
                    promise.TrySetFromTask(future);
                }
                else
                {
                    _ = ctx.ConnectAsync(future.Result, localAddress, cancellationToken).ContinueWith(
                            static (connect, state) => ((TaskCompletionSource) state).TrySetFromTask(connect), promise,
                            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }

                ctx.Pipeline.Remove(this);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return promise.Task;
        }
        else
        {
            Task future = ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
            ctx.Pipeline.Remove(this);
            return future;
        }
    }
}
