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

using System;
using System.Buffers;
using System.Net;
using System.Net.Quic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the IQuicStreamChannel of the msquic implementation, the counterpart of the JNI unit QuicheQuicStreamChannel
// (jvm-only-replaced), over one System.Net.Quic QuicStream. It derives from the Transport's AbstractChannel and keeps the
// semantics of QuicheQuicStreamChannel:
//   - it is open and active from its creation and fires channelRegistered/channelActive when the parent's event loop
//     registers it (only that event loop is compatible); a locally created unidirectional stream has its input shut
//     down from the start;
//   - reads honor read()/AUTO_READ: each read() starts a read batch of QuicStream.ReadAsync calls into buffers of the
//     RecvByteBufAllocator handle, which continues while the handle's continueReading() allows it and data is already
//     buffered (a read that does not complete synchronously ends the batch with channelReadComplete and is delivered by
//     the next batch; a result that arrives while no read() is pending is kept until the next one). A FIN (ReadAsync
//     returns 0, or ReadsClosed completed with the last bytes) shuts the input down and fires
//     ChannelInputShutdownEvent and ChannelInputShutdownReadComplete with half-closure (the default), or closes the
//     stream; READ_FRAMES delivers IQuicStreamFrames whose FIN flag carries it. A RESET_STREAM from the peer is a
//     QuicStreamResetException with the peer's application error code (QuicException StreamAborted);
//   - writes go through the ChannelOutboundBuffer (so writability follows the write buffer water marks, where quiche's
//     stream capacity decides it in Java) and are QuicStream.WriteAsync calls, one message at a time (AbstractChannel's
//     per-message drain); an IQuicStreamFrame with FIN completes the writes (FIN), after which writes fail with
//     ChannelOutputShutdownException("Fin was sent already"); a STOP_SENDING from the peer fails the flushed writes
//     with ChannelOutputShutdownException("STOP_SENDING frame received") without closing the stream; a write on a
//     unidirectional stream that the peer created fails with NotSupportedException;
//   - ShutdownOutputAsync() writes IQuicStreamFrame.EMPTY_FIN, ShutdownOutputAsync(error) and ShutdownInputAsync(error)
//     are QuicStream.Abort(Write/Read, error) (RESET_STREAM/STOP_SENDING), and the stream closes itself once its FIN was
//     sent and the peer's FIN received, as closeIfDone() does;
//   - close() sends the FIN if none was sent and disposes the QuicStream, which also stops a read side that is still
//     open (STOP_SENDING with DefaultStreamErrorCode 0, as System.Net.Quic's DisposeAsync does; quiche keeps the unread
//     data instead).
// Stream priorities have no System.Net.Quic counterpart: UpdatePriorityAsync reports NotSupportedException and
// Priority() stays null. Events and errors complete on the parent's event loop.
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class MsQuicStreamChannel : AbstractChannel, IQuicStreamChannel
{
    private static readonly IInternalLogger LOGGER = InternalLoggerFactory.GetInstance(typeof(MsQuicStreamChannel));
    private static readonly ChannelMetadata METADATA = new ChannelMetadata(false, 16);

    private readonly MsQuicChannel parent;
    private readonly QuicStream stream;
    private readonly QuicStreamAddress address;
    private readonly QuicStreamType type;
    private readonly bool localCreated;

    // Read state, owned by the event loop.
    private bool readable;
    private bool readPending;
    private bool readInFlight;
    private bool inRecv;
    private bool batchOpen;
    private bool readCompleteNeeded;
    private bool finReceived;
    private bool hasStashedRead;
    private ByteBuf stashedBuffer;
    private int stashedBytes;
    private Exception stashedError;

    // Write state, owned by the event loop.
    private bool finSent;
    private bool closeAfterFin;
    private bool stopSendingReceived;

    private volatile bool inputShutdown;
    private volatile bool outputShutdown;

    internal MsQuicStreamChannel(MsQuicChannel parent, QuicStream stream) : base(parent, METADATA)
    {
        this.parent = parent;
        this.stream = stream;
        long streamId = stream.Id;
        address = new QuicStreamAddress(streamId);
        type = MsQuicChannel.StreamType(streamId);
        localCreated = parent.IsStreamLocalCreated(streamId);
        // Local created unidirectional streams have the input shutdown by spec. There will never be any data for
        // these to be read.
        if (type == QuicStreamType.UNIDIRECTIONAL && localCreated)
        {
            inputShutdown = true;
        }
        readable = !inputShutdown;
        SetActive(true);
    }

    protected override IChannelConfig NewConfig() => new MsQuicStreamChannelConfig(this);

    protected override bool IsCompatible(IEventLoop eventLoop) => ReferenceEquals(eventLoop, parent.EventLoop);

    protected override EndPoint LocalAddress0() => address;

    protected override EndPoint RemoteAddress0() => address;

    public bool IsLocalCreated() => localCreated;

    public QuicStreamType Type() => type;

    public long StreamId() => address.StreamId();

    public QuicStreamPriority Priority() => null;

    public Task UpdatePriorityAsync(QuicStreamPriority priority, CancellationToken cancellationToken = default)
    {
        return Task.FromException(new NotSupportedException("Stream priorities are not supported by System.Net.Quic"));
    }

    public bool IsInputShutdown => inputShutdown;

    public bool IsOutputShutdown => outputShutdown;

    public bool IsShutdown => outputShutdown && inputShutdown;

    public Task ShutdownOutputAsync()
    {
        return RunOnLoop(() =>
        {
            outputShutdown = true;
            Task write = WriteTransportAsync(IQuicStreamFrame.EMPTY_FIN, CancellationToken.None);
            FlushTransport();
            return write;
        });
    }

    public Task ShutdownInputAsync(int error)
    {
        return RunOnLoop(() =>
        {
            inputShutdown = true;
            readable = false;
            Abort(QuicAbortDirection.Read, error);
            CloseIfDone();
            return Task.CompletedTask;
        });
    }

    public Task ShutdownOutputAsync(int error)
    {
        return RunOnLoop(() =>
        {
            Abort(QuicAbortDirection.Write, error);
            outputShutdown = true;
            CloseIfDone();
            return Task.CompletedTask;
        });
    }

    public Task ShutdownAsync()
    {
        return RunOnLoop(() =>
        {
            inputShutdown = true;
            outputShutdown = true;
            readable = false;
            _ = WriteTransportAsync(IQuicStreamFrame.EMPTY_FIN, CancellationToken.None);
            FlushTransport();
            Abort(QuicAbortDirection.Read, 0);
            CloseIfDone();
            return Task.CompletedTask;
        });
    }

    public Task ShutdownAsync(int error)
    {
        return RunOnLoop(() =>
        {
            inputShutdown = true;
            outputShutdown = true;
            readable = false;
            Abort(QuicAbortDirection.Both, error);
            CloseIfDone();
            return Task.CompletedTask;
        });
    }

    // quiche_conn_stream_shutdown: only the directions the stream has.
    private void Abort(QuicAbortDirection direction, long error)
    {
        if (!stream.CanRead)
        {
            direction &= ~QuicAbortDirection.Read;
        }
        if (!stream.CanWrite)
        {
            direction &= ~QuicAbortDirection.Write;
        }
        if (direction != 0)
        {
            stream.Abort(direction, error);
        }
    }

    private void CloseIfDone()
    {
        if (finSent && (finReceived || type == QuicStreamType.UNIDIRECTIONAL && localCreated))
        {
            _ = Close();
        }
    }

    private void SendFinIfNeeded()
    {
        if (!finSent)
        {
            finSent = true;
            if (stream.CanWrite)
            {
                stream.CompleteWrites();
            }
        }
    }

    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    {
        return Task.FromException(new NotSupportedException());
    }

    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        return Task.FromException(new NotSupportedException());
    }

    protected override void DoClose()
    {
        try
        {
            // Close the channel and fail the queued messages in all cases.
            SendFinIfNeeded();
        }
        catch (Exception)
        {
            // Just ignore
        }
        if (type == QuicStreamType.UNIDIRECTIONAL && localCreated || finReceived)
        {
            inputShutdown = true;
            outputShutdown = true;
        }
        readable = false;
        ReleaseStashedRead();
        parent.StreamClosed(this);
        _ = DisposeStreamAsync();
    }

    private async Task DisposeStreamAsync()
    {
        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LOGGER.Debug("Failed to dispose the QuicStream of {}", this, e);
        }
    }

    // Closes the stream because its connection closed; the flushed writes fail with the cause (closeStreams()).
    internal void CloseOnConnectionClose(ClosedChannelException cause)
    {
        if (IsOpen)
        {
            CloseOnWriteError(cause);
        }
    }

    protected override void DoBeginRead()
    {
        readPending = true;
        if (hasStashedRead)
        {
            ByteBuf buffer = stashedBuffer;
            int bytes = stashedBytes;
            Exception error = stashedError;
            hasStashedRead = false;
            stashedBuffer = null;
            stashedError = null;
            OpenBatch();
            if (OnReadResult(buffer, bytes, error))
            {
                Recv(true);
            }
            return;
        }
        if (readable && !readInFlight)
        {
            Recv(false);
        }
    }

    private void OpenBatch()
    {
        if (!batchOpen)
        {
            batchOpen = true;
            readCompleteNeeded = false;
            RecvBufAllocHandle().Reset(Config);
        }
    }

    // Read was complete and something was read, so we we need to reset the readPending flags, the allocHandle
    // and call fireChannelReadComplete(). The user may schedule another read now.
    private void EndBatch(bool force)
    {
        if (!batchOpen)
        {
            return;
        }
        batchOpen = false;
        if (readCompleteNeeded || force)
        {
            readCompleteNeeded = false;
            RecvBufAllocHandle().ReadComplete();
            Pipeline.FireChannelReadComplete();
        }
    }

    // QuicStreamChannelUnsafe.recv(): reads while a read() is pending (or the current batch continues) and data is
    // buffered; see the class comment.
    private void Recv(bool continueBatch)
    {
        if (inRecv)
        {
            // As the use may call read() we need to guard against reentrancy here as otherwise it could
            // be possible that we re-enter this method while still processing it.
            return;
        }
        inRecv = true;
        try
        {
            while (continueBatch || (IsOpen && readPending && readable && !readInFlight))
            {
                continueBatch = false;
                if (!IsOpen || !readable || readInFlight)
                {
                    EndBatch(false);
                    break;
                }
                OpenBatch();
                for (;;)
                {
                    IRecvByteBufAllocator.IHandle allocHandle = RecvBufAllocHandle();
                    ByteBuf byteBuf = allocHandle.Allocate(Config.GetAllocator());
                    allocHandle.AttemptedBytesRead(byteBuf.WritableBytes);
                    ValueTask<int> read;
                    try
                    {
                        read = stream.ReadAsync(byteBuf.AsMemory(byteBuf.WriterIndex, byteBuf.WritableBytes));
                    }
                    catch (Exception e)
                    {
                        if (!OnReadResult(byteBuf, -1, e))
                        {
                            break;
                        }
                        continue;
                    }
                    if (!read.IsCompleted)
                    {
                        readInFlight = true;
                        _ = CompleteReadAsync(read, byteBuf);
                        EndBatch(false);
                        return;
                    }
                    int bytes = -1;
                    Exception error = null;
                    try
                    {
                        bytes = read.GetAwaiter().GetResult();
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                    if (!OnReadResult(byteBuf, bytes, error))
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            // About to leave the method lets reset so we can enter it again.
            inRecv = false;
        }
    }

    private async Task CompleteReadAsync(ValueTask<int> read, ByteBuf byteBuf)
    {
        int bytes = -1;
        Exception error = null;
        try
        {
            bytes = await read.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            error = e;
        }
        try
        {
            EventLoop.Execute(() =>
            {
                readInFlight = false;
                if (!IsOpen)
                {
                    byteBuf.Release();
                    return;
                }
                if (!readPending)
                {
                    // No read() is pending: keep the result until the next one.
                    hasStashedRead = true;
                    stashedBuffer = byteBuf;
                    stashedBytes = bytes;
                    stashedError = error;
                    return;
                }
                OpenBatch();
                if (OnReadResult(byteBuf, bytes, error))
                {
                    Recv(true);
                }
            });
        }
        catch (RejectedExecutionException)
        {
            byteBuf.Release();
        }
    }

    private void ReleaseStashedRead()
    {
        if (hasStashedRead)
        {
            hasStashedRead = false;
            stashedBuffer?.Release();
            stashedBuffer = null;
            stashedError = null;
        }
    }

    // One completed QuicStream.ReadAsync of the current batch: the body of the read loop of recv(). Returns true when
    // the batch continues (continueReading()); otherwise the batch has ended.
    private bool OnReadResult(ByteBuf byteBuf, int bytes, Exception error)
    {
        IChannelPipeline pipeline = Pipeline;
        MsQuicStreamChannelConfig config = (MsQuicStreamChannelConfig) Config;
        IRecvByteBufAllocator.IHandle allocHandle = RecvBufAllocHandle();
        bool readFrames = config.IsReadFrames();
        if (error != null)
        {
            byteBuf.Release();
            readable = false;
            if (!IsOpen || inputShutdown || IsSilentReadError(error))
            {
                parent.ConnectionError(error);
                // The stream or its connection was closed (the parent closes the stream) or its input was shut down
                // locally: nothing to report.
                EndBatch(false);
                return false;
            }
            HandleReadException(pipeline, null, ReadException(error), readFrames);
            return false;
        }

        // It's possible that the stream was marked as finish while we iterated over the readable streams
        // or while we did have auto read disabled. If so we need to ensure we not try to read from it as it
        // would produce an error.
        bool fin = bytes == 0 || stream.ReadsClosed.IsCompletedSuccessfully;
        if (bytes > 0)
        {
            byteBuf.WriterIndex += bytes;
        }
        if (fin)
        {
            // If we received a FIN we also should mark the channel as non-readable as
            // there is nothing left to read really.
            readable = false;
            finReceived = true;
            inputShutdown = true;
        }
        bool continueReading = false;
        try
        {
            allocHandle.LastBytesRead(byteBuf.ReadableBytes);
            if (allocHandle.LastBytesRead() <= 0)
            {
                byteBuf.Release();
                if (finReceived && readFrames)
                {
                    // If we read QuicStreamFrames we should fire an frame through the pipeline
                    // with an empty buffer but the fin flag set to true.
                    byteBuf = Unpooled.EmptyBuffer;
                }
                else
                {
                    byteBuf = null;
                }
            }
            if (byteBuf != null)
            {
                // We did read one message.
                allocHandle.IncMessagesRead(1);
                readCompleteNeeded = true;

                // It's important that we reset this to false before we call fireChannelRead(...)
                // as the user may request another read() from channelRead(...) callback.
                readPending = false;

                ByteBuf message = byteBuf;
                byteBuf = null;
                if (readFrames)
                {
                    pipeline.FireChannelRead(new DefaultQuicStreamFrame(message, finReceived));
                }
                else
                {
                    pipeline.FireChannelRead(message);
                }
                continueReading = allocHandle.ContinueReading();
            }
            if (finReceived)
            {
                EndBatch(false);
                readable = false;
                CloseOnRead(pipeline, readFrames);
                return false;
            }
        }
        catch (Exception cause)
        {
            readable = false;
            HandleReadException(pipeline, byteBuf, cause, readFrames);
            return false;
        }
        if (continueReading && IsOpen && readable)
        {
            return true;
        }
        EndBatch(false);
        return false;
    }

    // Errors of a read that the stream does not report itself: the connection closed (MsQuicChannel closes its
    // streams), the stream was disposed or its read side aborted locally.
    private static bool IsSilentReadError(Exception error)
    {
        if (error is ObjectDisposedException || error is OperationCanceledException)
        {
            return true;
        }
        return error is System.Net.Quic.QuicException quic &&
               (quic.QuicError == QuicError.OperationAborted || MsQuicChannel.IsConnectionError(quic));
    }

    // Quiche.convertToException for a stream read: a RESET_STREAM is QuicStreamResetException with the error code.
    private static Exception ReadException(Exception error)
    {
        if (error is System.Net.Quic.QuicException quic && quic.QuicError == QuicError.StreamAborted)
        {
            return new QuicStreamResetException("QUICHE_ERR_STREAM_RESET", quic.ApplicationErrorCode ?? -1);
        }
        return MsQuicChannel.ToQuicException(error);
    }

    private void CloseOnRead(IChannelPipeline pipeline, bool readFrames)
    {
        if (readFrames && finReceived && finSent)
        {
            _ = Close();
        }
        else if (((MsQuicStreamChannelConfig) Config).IsAllowHalfClosure())
        {
            if (finReceived)
            {
                // If we receive a fin there will be no more data to read so we need to fire both events
                // to be consistent with other transports.
                pipeline.FireUserEventTriggered(ChannelInputShutdownEvent.INSTANCE);
                pipeline.FireUserEventTriggered(ChannelInputShutdownReadComplete.INSTANCE);
                if (finSent)
                {
                    // This was an unidirectional stream which means as soon as we received FIN and sent a FIN
                    // we need close the connection.
                    _ = Close();
                }
            }
        }
        else
        {
            // This was an unidirectional stream which means as soon as we received FIN we need
            // close the connection.
            _ = Close();
        }
    }

    private void HandleReadException(IChannelPipeline pipeline, ByteBuf byteBuf, Exception cause, bool readFrames)
    {
        if (byteBuf != null)
        {
            if (byteBuf.IsReadable())
            {
                pipeline.FireChannelRead(byteBuf);
            }
            else
            {
                byteBuf.Release();
            }
        }

        EndBatch(true);
        pipeline.FireExceptionCaught(cause);
        if (finReceived)
        {
            CloseOnRead(pipeline, readFrames);
        }
    }

    // QuicStreamChannelUnsafe.write(msg, promise): "Check first if the Channel is in a state in which it will accept
    // writes, if not fail everything with the right exception": a closed stream fails the write with
    // ClosedChannelException (AbstractChannel) and a stream whose FIN was sent with ChannelOutputShutdownException("Fin
    // was sent already"), both before the message is filtered, so an unsupported message written after
    // shutdownOutput() reports the output shutdown, as in Java.
    protected override Task WriteTransportAsync(object msg, CancellationToken cancellationToken)
    {
        if (IsOpen && finSent)
        {
            ReferenceCountUtil.Release(msg);
            return Task.FromException(new ChannelOutputShutdownException("Fin was sent already"));
        }
        return base.WriteTransportAsync(msg, cancellationToken);
    }

    protected override object FilterOutboundMessage(object msg)
    {
        if (msg is ByteBuf || msg is IQuicStreamFrame)
        {
            return msg;
        }
        throw new NotSupportedException("unsupported message type: " + StringUtil.SimpleClassName(msg));
    }

    // write0(msg): called on the event loop by AbstractChannel's per-message drain of the ChannelOutboundBuffer.
    protected override Task DoWrite(object msg)
    {
        try
        {
            if (type == QuicStreamType.UNIDIRECTIONAL && !localCreated)
            {
                throw new NotSupportedException(
                        "Writes on non-local created streams that are unidirectional are not supported");
            }
            if (finSent)
            {
                throw new ChannelOutputShutdownException("Fin was sent already");
            }
            if (stopSendingReceived)
            {
                throw new ChannelOutputShutdownException("STOP_SENDING frame received");
            }

            bool fin;
            ByteBuf buffer;
            if (msg is ByteBuf byteBuf)
            {
                fin = false;
                buffer = byteBuf;
            }
            else
            {
                IQuicStreamFrame frame = (IQuicStreamFrame) msg;
                fin = frame.HasFin();
                buffer = frame.Content;
            }

            bool readableBuffer = buffer.IsReadable();
            if (!fin && !readableBuffer)
            {
                return Task.CompletedTask;
            }
            if (fin)
            {
                finSent = true;
                outputShutdown = true;
                closeAfterFin = true;
            }
            if (!readableBuffer)
            {
                stream.CompleteWrites();
                return Task.CompletedTask;
            }
            return WriteAsync(buffer.ReadableSequence, fin);
        }
        catch (Exception e)
        {
            return Task.FromException(e);
        }
    }

    private async Task WriteAsync(ReadOnlySequence<byte> data, bool fin)
    {
        if (data.IsSingleSegment)
        {
            await stream.WriteAsync(data.First, fin).ConfigureAwait(false);
            return;
        }
        ReadOnlySequence<byte>.Enumerator segments = data.GetEnumerator();
        bool hasSegment = segments.MoveNext();
        while (hasSegment)
        {
            ReadOnlyMemory<byte> segment = segments.Current;
            hasSegment = segments.MoveNext();
            await stream.WriteAsync(segment, fin && !hasSegment).ConfigureAwait(false);
        }
    }

    protected override void DoWriteComplete()
    {
        // Let's check if we should close the channel now.
        // If it's a unidirectional channel we can close it as there will be no fin that we can read
        // from the remote peer. If its an bidirectional channel we should only close the channel if we
        // also received the fin from the remote peer.
        if (closeAfterFin && finSent && (type == QuicStreamType.UNIDIRECTIONAL || finReceived))
        {
            closeAfterFin = false;
            // close the channel now
            _ = Close();
        }
    }

    protected override void HandleWriteError(Exception t)
    {
        if (t is System.Net.Quic.QuicException quic && quic.QuicError == QuicError.StreamAborted)
        {
            // Once its signaled that the stream is stopped we can just fail everything.
            // That said we should not close the channel yet as there might be some data that is
            // not read yet by the user.
            stopSendingReceived = true;
            ChannelOutputShutdownException cause = new ChannelOutputShutdownException("STOP_SENDING frame received");
            while (OutboundBuffer.Remove(cause))
            {
            }
            return;
        }
        parent.ConnectionError(t);
        Exception failure = t;
        if (t is System.Net.Quic.QuicException || t is ObjectDisposedException)
        {
            failure = IsSilentReadError(t) ? new ClosedChannelException() : MsQuicChannel.ToQuicException(t);
        }
        OutboundBuffer.Remove(failure);
    }

    // Runs the operation on the event loop and returns its Task.
    private Task RunOnLoop(Func<Task> operation)
    {
        IEventLoop loop = EventLoop;
        if (loop.InEventLoop())
        {
            try
            {
                return operation();
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }
        TaskCompletionSource promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            loop.Execute(() =>
            {
                Task task;
                try
                {
                    task = operation();
                }
                catch (Exception e)
                {
                    promise.TrySetException(e);
                    return;
                }
                MsQuicChannel.Notify(task, promise);
            });
        }
        catch (Exception e)
        {
            return Task.FromException(e);
        }
        return promise.Task;
    }

    public override string ToString()
    {
        return "[id: 0x" + Id.AsShortText() + ", " + address + "]";
    }
}
