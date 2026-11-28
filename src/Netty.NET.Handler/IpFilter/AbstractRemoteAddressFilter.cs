/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.IpFilter;

/**
 * This class provides the functionality to either accept or reject new {@link Channel}s
 * based on their IP address.
 * <p>
 * You should inherit from this class if you would like to implement your own IP-based filter. Basically you have to
 * implement {@link #accept(ChannelHandlerContext, SocketAddress)} to decided whether you want to accept or reject
 * a connection from the remote address.
 * <p>
 * Furthermore overriding {@link #channelRejected(ChannelHandlerContext, SocketAddress)} gives you the
 * flexibility to respond to rejected (denied) connections. If you do not want to send a response, just have it return
 * null.  Take a look at {@link RuleBasedIpFilter} for details.
 */
// CLR: T extends SocketAddress is T : EndPoint; ChannelFuture is Task (docs/transport-porting.md).
public abstract class AbstractRemoteAddressFilter<T> : ChannelInboundHandlerAdapter where T : EndPoint
{
    public override void ChannelRegistered(IChannelHandlerContext ctx)
    {
        HandleNewChannel(ctx);
        ctx.FireChannelRegistered();
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        if (!HandleNewChannel(ctx))
        {
            throw new InvalidOperationException("cannot determine to accept or reject a channel: " + ctx.Channel);
        }
        else
        {
            ctx.FireChannelActive();
        }
    }

    // CLR: the unchecked cast (T) ctx.channel().remoteAddress() is erased in Java, and a subclass with a narrower T
    // checks the type in the bridge method of its accept(...) override, inside the try below, so a mismatch closes the
    // channel. The C# cast is checked where it is written ("At run time, an explicit conversion might not succeed and a
    // cast expression might throw an exception",
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/type-testing-and-cast), so it is
    // made inside the try for the same outcome. catch (Throwable) is catch (Exception), and
    // PlatformDependent.throwException(cause), which rethrows the cause without declaring it, is throw;, after which
    // the unreachable "return true" of the source is not repeated.
    private bool HandleNewChannel(IChannelHandlerContext ctx)
    {
        EndPoint address = ctx.Channel.RemoteAddress;

        // If the remote address is not available yet, defer the decision.
        if (address == null)
        {
            return false;
        }

        // Evaluate the decision before removing this handler from the pipeline, so that if accept(...) throws,
        // the channel is closed instead of silently continuing through the rest of the pipeline unfiltered.
        T remoteAddress;
        bool accepted;

        try
        {
            remoteAddress = (T) address;
            accepted = Accept(ctx, remoteAddress);
        }
        catch (Exception)
        {
            _ = ctx.CloseAsync();
            throw;
        }

        // No need to keep this handler in the pipeline anymore because the decision has been made.
        // Also, this will prevent the subsequent events from being handled by this handler.
        ctx.Pipeline.Remove(this);

        if (accepted)
        {
            ChannelAccepted(ctx, remoteAddress);
        }
        else
        {
            Task rejectedFuture = ChannelRejected(ctx, remoteAddress);
            if (rejectedFuture != null)
            {
                // CLR: ChannelFutureListener.CLOSE closes future.channel(); a Task has no channel, and the futures a
                // rejection handler returns come from ctx (as ctx.writeAndFlush(...)), so ctx.Channel is closed by a
                // synchronous continuation, as MessageAggregator does in codec-base.
                _ = rejectedFuture.ContinueWith(_ => ctx.Channel.CloseAsync(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else
            {
                _ = ctx.CloseAsync();
            }
        }

        return true;
    }

    /**
     * This method is called immediately after a {@link io.netty.channel.Channel} gets registered.
     *
     * @return Return true if connections from this IP address and port should be accepted. False otherwise.
     */
    protected abstract bool Accept(IChannelHandlerContext ctx, T remoteAddress);

    /**
     * This method is called if {@code remoteAddress} gets accepted by
     * {@link #accept(ChannelHandlerContext, SocketAddress)}.  You should override it if you would like to handle
     * (e.g. respond to) accepted addresses.
     */
    protected virtual void ChannelAccepted(IChannelHandlerContext ctx, T remoteAddress)
    {
    }

    /**
     * This method is called if {@code remoteAddress} gets rejected by
     * {@link #accept(ChannelHandlerContext, SocketAddress)}.  You should override it if you would like to handle
     * (e.g. respond to) rejected addresses.
     *
     * @return A {@link ChannelFuture} if you perform I/O operations, so that
     *         the {@link Channel} can be closed once it completes. Null otherwise.
     */
    protected virtual Task ChannelRejected(IChannelHandlerContext ctx, T remoteAddress)
    {
        return null;
    }
}
