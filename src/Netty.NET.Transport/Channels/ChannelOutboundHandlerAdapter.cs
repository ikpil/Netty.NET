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
 * Skeleton implementation of a {@link ChannelOutboundHandler}. This implementation just forwards each method call via
 * the {@link ChannelHandlerContext}.
 */
public class ChannelOutboundHandlerAdapter : ChannelHandlerAdapter, IChannelOutboundHandler
{
    /**
     * Calls {@link ChannelHandlerContext#bind(SocketAddress, ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    public virtual Task BindAsync(ChannelHandlerContext context, EndPoint localAddress, CancellationToken cancellationToken) => context.BindAsync(localAddress, cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#connect(SocketAddress, SocketAddress, ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    public virtual Task ConnectAsync(ChannelHandlerContext context, EndPoint remoteAddress, EndPoint localAddress, CancellationToken cancellationToken) => context.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#disconnect(ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    public virtual Task DisconnectAsync(ChannelHandlerContext context, CancellationToken cancellationToken) => context.DisconnectAsync(cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#close(ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    public virtual Task CloseAsync(ChannelHandlerContext context, CancellationToken cancellationToken) => context.CloseAsync(cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#deregister(ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    public virtual Task DeregisterAsync(ChannelHandlerContext context, CancellationToken cancellationToken) => context.DeregisterAsync(cancellationToken);
    /**
     * Calls {@link ChannelHandlerContext#write(Object, ChannelPromise)} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    public virtual Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken cancellationToken) => context.WriteAsync(message, cancellationToken);

    /**
     * Calls {@link ChannelHandlerContext#read()} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    public virtual void Read(ChannelHandlerContext context) => context.Read();
    /**
     * Calls {@link ChannelHandlerContext#flush()} to forward
     * to the next {@link ChannelOutboundHandler} in the {@link ChannelPipeline}.
     *
     * Sub-classes may override this method to change behavior.
     */
    public virtual void Flush(ChannelHandlerContext context) => context.Flush();
}
