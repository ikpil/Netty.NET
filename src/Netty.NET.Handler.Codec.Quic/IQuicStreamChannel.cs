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
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * A QUIC stream.
 */
// CLR: DuplexChannel is IDuplexChannel; a returned Task replaces ChannelFuture and the ChannelPromise overloads fold into
// the Task-returning members (docs/transport-porting.md, Decisions). The shutdown members that take an error code keep
// the shape of the IDuplexChannel members they extend (no CancellationToken); updatePriority takes one, as the
// IChannelOutboundInvoker operations do. The default bind/connect/disconnect/close/deregister/write/writeAndFlush that
// delegate to the pipeline are default implementations of the IChannelOutboundInvoker members, and the default
// shutdownInput()/shutdownInput(ChannelPromise), which shut the input down with error 0, the default implementation of
// IDuplexChannel.ShutdownInputAsync() (a class's own implementation takes precedence, as a Java class method does over a
// default method); newPromise()-style defaults and the shutdownOutput()/shutdown() defaults that only pass newPromise()
// have no Task counterpart. localAddress(), remoteAddress(), parent(), read(), flush() and config() are re-declared in
// Java only to narrow their return types (QuicStreamAddress, QuicChannel, QuicStreamChannel, QuicStreamChannelConfig);
// they are inherited from IChannel unchanged, as a C# interface cannot narrow an inherited member's return type without
// hiding it (the ISocketChannel rule), so callers cast Parent to IQuicChannel, Config to IQuicStreamChannelConfig and
// LocalAddress/RemoteAddress to QuicStreamAddress. @Nullable has no counterpart (nullable reference types are not
// enabled in this port).
public interface IQuicStreamChannel : IDuplexChannel
{

    /**
     * Should be added to a {@link ChannelFuture} when the output should be cleanly shutdown via a {@code FIN}. No more
     * writes will be allowed after this point.
     */
    // CLR: a ChannelFutureListener constant reads f.channel(); a Task has no channel, and the ChannelFutureListener
    // constants have no counterpart (docs/transport-porting.md, JVM-only table), so a caller continues the write Task
    // with channel.ShutdownOutputAsync(), as ChannelFutureListener.CLOSE is a continuation that closes the channel.

    Task IChannelOutboundInvoker.BindAsync(EndPoint socketAddress, CancellationToken cancellationToken)
    {
        return Pipeline.BindAsync(socketAddress, cancellationToken);
    }

    Task IChannelOutboundInvoker.ConnectAsync(EndPoint remoteAddress, EndPoint localAddress,
                                              CancellationToken cancellationToken)
    {
        return Pipeline.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    Task IChannelOutboundInvoker.DisconnectAsync(CancellationToken cancellationToken)
    {
        return Pipeline.DisconnectAsync(cancellationToken);
    }

    Task IChannelOutboundInvoker.CloseAsync(CancellationToken cancellationToken)
    {
        return Pipeline.CloseAsync(cancellationToken);
    }

    Task IChannelOutboundInvoker.DeregisterAsync(CancellationToken cancellationToken)
    {
        return Pipeline.DeregisterAsync(cancellationToken);
    }

    Task IChannelOutboundInvoker.WriteAsync(object msg, CancellationToken cancellationToken)
    {
        return Pipeline.WriteAsync(msg, cancellationToken);
    }

    Task IChannelOutboundInvoker.WriteAndFlushAsync(object msg, CancellationToken cancellationToken)
    {
        return Pipeline.WriteAndFlushAsync(msg, cancellationToken);
    }

    Task IDuplexChannel.ShutdownInputAsync()
    {
        return ShutdownInputAsync(0);
    }

    /**
     * Shortcut for calling {@link #shutdownInput(int)} and {@link #shutdownInput(int)}.
     *
     * @param error the error to send.
     * @return the future that is notified on completion.
     */
    /**
     * Shortcut for calling {@link #shutdownInput(int, ChannelPromise)} and {@link #shutdownInput(int, ChannelPromise)}.
     *
     * @param error the error to send.
     * @param promise will be notified on completion.
     * @return the future that is notified on completion.
     */
    Task ShutdownAsync(int error);

    /**
     * Shutdown the input of the stream with the given error code. This means a {@code STOP_SENDING} frame will
     * be send to the remote peer and all data received will be discarded.
     *
     * @param error the error to send as part of the {@code STOP_SENDING} frame.
     * @return the future that is notified on completion.
     */
    /**
     * Shutdown the input of the stream with the given error code. This means a {@code STOP_SENDING} frame will
     * be send to the remote peer and all data received will be discarded.
     *
     * @param error the error to send as part of the {@code STOP_SENDING} frame.
     * @param promise will be notified on completion.
     * @return the future that is notified on completion.
     */
    Task ShutdownInputAsync(int error);

    /**
     * Shutdown the output of the stream with the given error code. This means a {@code RESET_STREAM} frame will
     * be send to the remote peer and all data that is not sent yet will be discarded.
     *
     * <strong>Important:</strong>If you want to shutdown the output without sending a {@code RESET_STREAM} frame you
     * should use {@link #shutdownOutput()} which will shutdown the output by sending a {@code FIN} and so signal
     * a clean shutdown.
     *
     * @param error the error to send as part of the {@code RESET_STREAM} frame.
     * @return the future that is notified on completion.
     */
    /**
     * Shutdown the output of the stream with the given error code. This means a {@code RESET_STREAM} frame will
     * be send to the remote peer and all data that is not sent yet will be discarded.
     *
     * <strong>Important:</strong>If you want to shutdown the output without sending a {@code RESET_STREAM} frame you
     * should use {@link #shutdownOutput(ChannelPromise)} which will shutdown the output by sending a {@code FIN}
     * and so signal a clean shutdown.
     *
     * @param error the error to send as part of the {@code RESET_STREAM} frame.
     * @param promise will be notified on completion.
     * @return the future that is notified on completion.
     */
    Task ShutdownOutputAsync(int error);

    /**
     * Returns {@code true} if the stream was created locally.
     *
     * @return {@code true} if created locally, {@code false} otherwise.
     */
    bool IsLocalCreated();

    /**
     * Returns the {@link QuicStreamType} of the stream.
     *
     * @return {@link QuicStreamType} of this stream.
     */
    QuicStreamType Type();

    /**
     * The id of the stream.
     *
     * @return the stream id of this {@link QuicStreamChannel}.
     */
    long StreamId();

    /**
     * The {@link QuicStreamPriority} if explicit set for the stream via {@link #updatePriority(QuicStreamPriority)} or
     * {@link #updatePriority(QuicStreamPriority, ChannelPromise)}. Otherwise {@code null}.
     *
     * @return the priority if any was set.
     */
    QuicStreamPriority Priority();

    /**
     * Update the priority of the stream. A stream's priority determines the order in which stream data is sent
     * on the wire (streams with lower priority are sent first).
     *
     * @param priority  the priority.
     * @return          future that is notified once the operation completes.
     */
    /**
     * Update the priority of the stream. A stream's priority determines the order in which stream data is sent
     * on the wire (streams with lower priority are sent first).
     *
     * @param priority  the priority.
     * @param promise   notified once operations completes.
     * @return          future that is notified once the operation completes.
     */
    Task UpdatePriorityAsync(QuicStreamPriority priority, CancellationToken cancellationToken = default);
}
