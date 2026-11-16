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

using System.Net;
using System.Threading;
using System.Threading.Tasks;
namespace Netty.NET.Transport.Channels;
/**
 * {@link ChannelHandler} implementation which represents a combination out of a {@link ChannelInboundHandler} and
 * the {@link ChannelOutboundHandler}.
 *
 * It is a good starting point if your {@link ChannelHandler} implementation needs to intercept operations and also
 * state updates.
 */
public class ChannelDuplexHandler : ChannelInboundHandlerAdapter, IChannelOutboundHandler
{
    /**
     * Calls {@link ChannelHandlerContext#bind(SocketAddress, ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    [ChannelHandlerMask.Skip]
    public virtual Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken) => ctx.BindAsync(localAddress, cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#connect(SocketAddress, SocketAddress, ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    [ChannelHandlerMask.Skip]
    public virtual Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken) => ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#disconnect(ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    [ChannelHandlerMask.Skip]
    public virtual Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken) => ctx.DisconnectAsync(cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#close(ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    [ChannelHandlerMask.Skip]
    public virtual Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken) => ctx.CloseAsync(cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#deregister(ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    [ChannelHandlerMask.Skip]
    public virtual Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken) => ctx.DeregisterAsync(cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#write(Object, ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    [ChannelHandlerMask.Skip]
    public virtual Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken) => ctx.WriteAsync(msg, cancellationToken);

    /**
     * Calls {@link ChannelHandlerContext#read()} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    [ChannelHandlerMask.Skip]
    public virtual void Read(IChannelHandlerContext ctx) => ctx.Read();
    /**
     * Calls {@link ChannelHandlerContext#flush()} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    [ChannelHandlerMask.Skip]
    public virtual void Flush(IChannelHandlerContext ctx) => ctx.Flush();
}
