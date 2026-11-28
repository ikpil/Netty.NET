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

using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Compresses a {@link ByteBuf} using the deflate algorithm.
 */
// CLR: ChannelFuture and ChannelPromise are Task : close() and close(ChannelPromise) fold into the one
// Task-returning CloseAsync(CancellationToken cancellationToken = default), as EmbeddedChannel and Bzip2Encoder
// do; the promise is the returned Task, the token is passed on to the channel operation as MessageToByteEncoder's
// write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), and a call without a token is close(). Both
// Javadocs precede it. It is an overload next to the inherited handler method CloseAsync(IChannelHandlerContext,
// CancellationToken), as close(ChannelPromise) and close(ChannelHandlerContext, ChannelPromise) are in Java.
public abstract class ZlibEncoder : MessageToByteEncoder<ByteBuf>
{

    protected ZlibEncoder() : base(typeof(ByteBuf), false)
    {
    }

    /**
     * Returns {@code true} if and only if the end of the compressed stream
     * has been reached.
     */
    public abstract bool IsClosed();

    /**
     * Close this {@link ZlibEncoder} and so finish the encoding.
     *
     * The returned {@link ChannelFuture} will be notified once the
     * operation completes.
     */
    /**
     * Close this {@link ZlibEncoder} and so finish the encoding.
     * The given {@link ChannelFuture} will be notified once the operation
     * completes and will also be returned.
     */
    public abstract Task CloseAsync(CancellationToken cancellationToken = default);

}
