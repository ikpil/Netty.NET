/*
 * Copyright 2019 The Netty Project
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

namespace Netty.NET.Handler.Address;

/**
 * {@link ChannelOutboundHandler} implementation which allows to dynamically replace the used
 * {@code remoteAddress} and / or {@code localAddress} when making a connection attempt.
 * <p>
 * This can be useful to for example bind to a specific {@link NetworkInterface} based on
 * the {@code remoteAddress}.
 */
// CLR: SocketAddress is EndPoint and connect(ctx, remote, local, promise) is the Task-returning ConnectAsync; the
// returned Task replaces the promise.
public abstract class DynamicAddressConnectHandler : ChannelOutboundHandlerAdapter
{
    // CLR: promise.setFailure(e) is the returned faulted Task. addListener(...) on the connect future is a synchronous
    // continuation, which runs at once on the calling thread when the connect has already completed, as a listener
    // added to a completed future does
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcontinuationoptions?view=net-10.0).
    public sealed override Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress,
                                             EndPoint localAddress, CancellationToken cancellationToken)
    {
        EndPoint remote;
        EndPoint local;

        try
        {
            remote = RemoteAddress(remoteAddress, localAddress);
            local = LocalAddress(remoteAddress, localAddress);
        }
        catch (Exception e)
        {
            return Task.FromException(e);
        }

        Task future = ctx.ConnectAsync(remote, local, cancellationToken);
        _ = future.ContinueWith(f =>
        {
            if (f.IsCompletedSuccessfully)
            {
                // We only remove this handler from the pipeline once the connect was successful as otherwise
                // the user may try to connect again.
                ctx.Pipeline.Remove(this);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return future;
    }

    /**
     * Returns the local {@link SocketAddress} to use for
     * {@link ChannelHandlerContext#connect(SocketAddress, SocketAddress)} based on the original {@code remoteAddress}
     * and {@code localAddress}.
     * By default, this method returns the given {@code localAddress}.
     */
    protected virtual EndPoint LocalAddress(EndPoint remoteAddress, EndPoint localAddress)
    {
        return localAddress;
    }

    /**
     * Returns the remote {@link SocketAddress} to use for
     * {@link ChannelHandlerContext#connect(SocketAddress, SocketAddress)} based on the original {@code remoteAddress}
     * and {@code localAddress}.
     * By default, this method returns the given {@code remoteAddress}.
     */
    protected virtual EndPoint RemoteAddress(EndPoint remoteAddress, EndPoint localAddress)
    {
        return remoteAddress;
    }
}
