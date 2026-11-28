/*
 * Copyright 2013 The Netty Project
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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * Manages streams within a SPDY session.
 */
// CLR: ChannelDuplexHandler is the Transport ChannelDuplexHandler. write(ctx, msg, promise) and close(ctx,
// promise) are WriteAsync/CloseAsync, whose Task replaces the promise. The handler completes, stores and listens
// to its write promises, so each ChannelPromise of the source is a TaskCompletionSource
// (RunContinuationsAsynchronously, as ChunkedWriteHandler's queued writes, docs/handler-porting.md) whose Task the
// handler returns: ctx.write(msg, promise)/ctx.writeAndFlush(msg, promise)/ctx.close(promise) are the private
// Write, WriteAndFlush and Close helpers that complete the promise from the context operation's Task
// (TrySetFromTask, the PromiseNotifier mapping of docs/common-task-composition.md), promise.setFailure is
// SetException, ctx.newPromise() is a new TaskCompletionSource and ctx.newSucceededFuture() is Task.CompletedTask.
// ChannelFuture.addListener is a continuation on Common's EventExecutorTaskScheduler over ctx.Executor, the
// scheduler that handler's IdleStateHandler uses too : the Transport completes its operation Tasks with
// RunContinuationsAsynchronously, so a continuation on TaskScheduler.Default would run later on a thread-pool
// thread, while Java's DefaultPromise notifies a listener at once when the promise completes on its event loop.
// ChannelFutureListener is the operationComplete(Task) of the private ClosingChannelFutureListener, and
// future.isSuccess() is IsCompletedSuccessfully. A queued data frame is written later without the caller's
// CancellationToken (Java has no counterpart). AtomicInteger is an int field updated with Interlocked; volatile
// int is a volatile field. IllegalArgumentException is ArgumentException and numbers in exception texts use the
// invariant culture. The Java byte priority is the CLR byte. The initializer of spdySession reads the fields
// initialSendWindowSize and initialReceiveWindowSize, which hold DEFAULT_WINDOW_SIZE at that point; a C# field
// initializer cannot read instance fields (CS0236, https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236),
// so it passes DEFAULT_WINDOW_SIZE.
public class SpdySessionHandler : ChannelDuplexHandler
{

    private static readonly SpdyProtocolException PROTOCOL_EXCEPTION =
            SpdyProtocolException.NewStatic(null, typeof(SpdySessionHandler), "handleOutboundMessage(...)");
    private static readonly SpdyProtocolException STREAM_CLOSED =
            SpdyProtocolException.NewStatic("Stream closed", typeof(SpdySessionHandler), "removeStream(...)");

    private const int DEFAULT_WINDOW_SIZE = 64 * 1024; // 64 KB default initial window size
    private int initialSendWindowSize    = DEFAULT_WINDOW_SIZE;
    private int initialReceiveWindowSize = DEFAULT_WINDOW_SIZE;
    private volatile int initialSessionReceiveWindowSize = DEFAULT_WINDOW_SIZE;

    private readonly SpdySession spdySession = new SpdySession(DEFAULT_WINDOW_SIZE, DEFAULT_WINDOW_SIZE);
    private int lastGoodStreamId;

    // See https://datatracker.ietf.org/doc/html/rfc7540#section-6.5.2 for why 100 was chosen as a safe default.
    private const int DEFAULT_MAX_CONCURRENT_STREAMS = 100;
    private int remoteConcurrentStreams = int.MaxValue;
    private int localConcurrentStreams  = DEFAULT_MAX_CONCURRENT_STREAMS;

    private int pings;

    private bool sentGoAwayFrame;
    private bool receivedGoAwayFrame;
    private bool sentInitialSettingsFrame;

    private ClosingChannelFutureListener closeSessionFutureListener;

    private readonly bool server;
    private readonly int minorVersion;

    /**
     * Creates a new session handler.
     * <p>
     * Remote-initiated streams are accepted up to {@value #DEFAULT_MAX_CONCURRENT_STREAMS}
     * concurrently (matching the HTTP/2 default, see
     * <a href="https://datatracker.ietf.org/doc/html/rfc7540#section-6.5.2">RFC 7540, Section 6.5.2</a>).
     * Use {@link #SpdySessionHandler(SpdyVersion, boolean, int)} to configure a different limit.
     *
     * @param version the protocol version
     * @param server  {@code true} if and only if this session handler should
     *                handle the server endpoint of the connection.
     *                {@code false} if and only if this session handler should
     *                handle the client endpoint of the connection.
     */
    public SpdySessionHandler(SpdyVersion version, bool server)
        : this(version, server, DEFAULT_MAX_CONCURRENT_STREAMS)
    {
    }

    /**
     * Creates a new session handler.
     *
     * @param version the protocol version
     * @param server  {@code true} if and only if this session handler should
     *                handle the server endpoint of the connection.
     *                {@code false} if and only if this session handler should
     *                handle the client endpoint of the connection.
     * @param maxLocalConcurrentStreams the maximum number of concurrent remote-initiated streams
     *                that will be accepted. This bounds the memory a remote peer can force this
     *                handler to allocate by opening streams. {@code SYN_STREAM} frames received
     *                once this limit is reached are rejected with a {@code RST_STREAM} carrying
     *                {@link SpdyStreamStatus#REFUSED_STREAM}. Must be positive.
     */
    public SpdySessionHandler(SpdyVersion version, bool server, int maxLocalConcurrentStreams)
    {
        this.minorVersion = ObjectUtil.CheckNotNull(version, "version").MinorVersion();
        this.server = server;
        this.localConcurrentStreams = CheckPositive(maxLocalConcurrentStreams, "maxLocalConcurrentStreams");
    }

    public virtual void SetSessionReceiveWindowSize(int sessionReceiveWindowSize)
    {
        CheckPositiveOrZero(sessionReceiveWindowSize, "sessionReceiveWindowSize");

        // This will not send a window update frame immediately.
        // If this value increases the allowed receive window size,
        // a WINDOW_UPDATE frame will be sent when only half of the
        // session window size remains during data frame processing.
        // If this value decreases the allowed receive window size,
        // the window will be reduced as data frames are processed.
        initialSessionReceiveWindowSize = sessionReceiveWindowSize;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        if (ctx.Channel.IsActive)
        {
            SendInitialSettingsFrame(ctx);
        }
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        SendInitialSettingsFrame(ctx);
        base.ChannelActive(ctx);
    }

    private void SendInitialSettingsFrame(IChannelHandlerContext ctx)
    {
        if (sentInitialSettingsFrame)
        {
            return;
        }

        sentInitialSettingsFrame = true;
        // Advertise the accept limit to the remote peer. This is advisory only -- the limit is
        // enforced locally by acceptStream(...) regardless of whether the peer honors it.
        ISpdySettingsFrame settingsFrame = new DefaultSpdySettingsFrame();
        settingsFrame.SetValue(ISpdySettingsFrame.SETTINGS_MAX_CONCURRENT_STREAMS, localConcurrentStreams);
        _ = ctx.WriteAndFlushAsync(settingsFrame);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is ISpdyDataFrame)
        {

            /*
             * SPDY Data frame processing requirements:
             *
             * If an endpoint receives a data frame for a Stream-ID which is not open
             * and the endpoint has not sent a GOAWAY frame, it must issue a stream error
             * with the error code INVALID_STREAM for the Stream-ID.
             *
             * If an endpoint which created the stream receives a data frame before receiving
             * a SYN_REPLY on that stream, it is a protocol error, and the recipient must
             * issue a stream error with the getStatus code PROTOCOL_ERROR for the Stream-ID.
             *
             * If an endpoint receives multiple data frames for invalid Stream-IDs,
             * it may close the session.
             *
             * If an endpoint refuses a stream it must ignore any data frames for that stream.
             *
             * If an endpoint receives a data frame after the stream is half-closed from the
             * sender, it must send a RST_STREAM frame with the getStatus STREAM_ALREADY_CLOSED.
             *
             * If an endpoint receives a data frame after the stream is closed, it must send
             * a RST_STREAM frame with the getStatus PROTOCOL_ERROR.
             */
            ISpdyDataFrame spdyDataFrame = (ISpdyDataFrame) msg;
            int streamId = spdyDataFrame.StreamId();

            int deltaWindowSize = -1 * spdyDataFrame.Content.ReadableBytes;
            int newSessionWindowSize =
                spdySession.UpdateReceiveWindowSize(SPDY_SESSION_STREAM_ID, deltaWindowSize);

            // Check if session window size is reduced beyond allowable lower bound
            if (newSessionWindowSize < 0)
            {
                IssueSessionError(ctx, SpdySessionStatus.PROTOCOL_ERROR);
                return;
            }

            // Send a WINDOW_UPDATE frame if less than half the session window size remains
            if (newSessionWindowSize <= initialSessionReceiveWindowSize / 2)
            {
                int sessionDeltaWindowSize = initialSessionReceiveWindowSize - newSessionWindowSize;
                spdySession.UpdateReceiveWindowSize(SPDY_SESSION_STREAM_ID, sessionDeltaWindowSize);
                ISpdyWindowUpdateFrame spdyWindowUpdateFrame =
                    new DefaultSpdyWindowUpdateFrame(SPDY_SESSION_STREAM_ID, sessionDeltaWindowSize);
                _ = ctx.WriteAndFlushAsync(spdyWindowUpdateFrame);
            }

            // Check if we received a data frame for a Stream-ID which is not open

            if (!spdySession.IsActiveStream(streamId))
            {
                spdyDataFrame.Release();

                if (streamId <= lastGoodStreamId)
                {
                    IssueStreamError(ctx, streamId, SpdyStreamStatus.PROTOCOL_ERROR);
                }
                else if (!sentGoAwayFrame)
                {
                    IssueStreamError(ctx, streamId, SpdyStreamStatus.INVALID_STREAM);
                }

                return;
            }

            // Check if we received a data frame for a stream which is half-closed

            if (spdySession.IsRemoteSideClosed(streamId))
            {
                spdyDataFrame.Release();
                IssueStreamError(ctx, streamId, SpdyStreamStatus.STREAM_ALREADY_CLOSED);
                return;
            }

            // Check if we received a data frame before receiving a SYN_REPLY
            if (!IsRemoteInitiatedId(streamId) && !spdySession.HasReceivedReply(streamId))
            {
                spdyDataFrame.Release();
                IssueStreamError(ctx, streamId, SpdyStreamStatus.PROTOCOL_ERROR);
                return;
            }

            /*
             * SPDY Data frame flow control processing requirements:
             *
             * Recipient should not send a WINDOW_UPDATE frame as it consumes the last data frame.
             */

            // Update receive window size
            int newWindowSize = spdySession.UpdateReceiveWindowSize(streamId, deltaWindowSize);

            // Window size can become negative if we sent a SETTINGS frame that reduces the
            // size of the transfer window after the peer has written data frames.
            // The value is bounded by the length that SETTINGS frame decrease the window.
            // This difference is stored for the session when writing the SETTINGS frame
            // and is cleared once we send a WINDOW_UPDATE frame.
            if (newWindowSize < spdySession.GetReceiveWindowSizeLowerBound(streamId))
            {
                spdyDataFrame.Release();
                IssueStreamError(ctx, streamId, SpdyStreamStatus.FLOW_CONTROL_ERROR);
                return;
            }

            // Window size became negative due to sender writing frame before receiving SETTINGS
            // Send data frames upstream in initialReceiveWindowSize chunks
            if (newWindowSize < 0)
            {
                while (spdyDataFrame.Content.ReadableBytes > initialReceiveWindowSize)
                {
                    ISpdyDataFrame partialDataFrame = new DefaultSpdyDataFrame(
                            streamId, spdyDataFrame.Content.ReadRetainedSlice(initialReceiveWindowSize));
                    _ = ctx.WriteAndFlushAsync(partialDataFrame);
                }
            }

            // Send a WINDOW_UPDATE frame if less than half the stream window size remains
            if (newWindowSize <= initialReceiveWindowSize / 2 && !spdyDataFrame.IsLast())
            {
                int streamDeltaWindowSize = initialReceiveWindowSize - newWindowSize;
                spdySession.UpdateReceiveWindowSize(streamId, streamDeltaWindowSize);
                ISpdyWindowUpdateFrame spdyWindowUpdateFrame =
                        new DefaultSpdyWindowUpdateFrame(streamId, streamDeltaWindowSize);
                _ = ctx.WriteAndFlushAsync(spdyWindowUpdateFrame);
            }

            // Close the remote side of the stream if this is the last frame
            if (spdyDataFrame.IsLast())
            {
                HalfCloseStream(ctx, streamId, true, Task.CompletedTask);
            }

        }
        else if (msg is ISpdySynStreamFrame)
        {

            /*
             * SPDY SYN_STREAM frame processing requirements:
             *
             * If an endpoint receives a SYN_STREAM with a Stream-ID that is less than
             * any previously received SYN_STREAM, it must issue a session error with
             * the getStatus PROTOCOL_ERROR.
             *
             * If an endpoint receives multiple SYN_STREAM frames with the same active
             * Stream-ID, it must issue a stream error with the getStatus code PROTOCOL_ERROR.
             *
             * The recipient can reject a stream by sending a stream error with the
             * getStatus code REFUSED_STREAM.
             */

            ISpdySynStreamFrame spdySynStreamFrame = (ISpdySynStreamFrame) msg;
            int streamId = spdySynStreamFrame.StreamId();

            // Check if we received a valid SYN_STREAM frame
            if (spdySynStreamFrame.IsInvalid() ||
                !IsRemoteInitiatedId(streamId) ||
                spdySession.IsActiveStream(streamId))
            {
                IssueStreamError(ctx, streamId, SpdyStreamStatus.PROTOCOL_ERROR);
                return;
            }

            // Stream-IDs must be monotonically increasing
            if (streamId <= lastGoodStreamId)
            {
                IssueSessionError(ctx, SpdySessionStatus.PROTOCOL_ERROR);
                return;
            }

            // Try to accept the stream
            byte priority = spdySynStreamFrame.Priority();
            bool remoteSideClosed = spdySynStreamFrame.IsLast();
            bool localSideClosed = spdySynStreamFrame.IsUnidirectional();
            if (!AcceptStream(streamId, priority, remoteSideClosed, localSideClosed))
            {
                IssueStreamError(ctx, streamId, SpdyStreamStatus.REFUSED_STREAM);
                return;
            }

        }
        else if (msg is ISpdySynReplyFrame)
        {

            /*
             * SPDY SYN_REPLY frame processing requirements:
             *
             * If an endpoint receives multiple SYN_REPLY frames for the same active Stream-ID
             * it must issue a stream error with the getStatus code STREAM_IN_USE.
             */

            ISpdySynReplyFrame spdySynReplyFrame = (ISpdySynReplyFrame) msg;
            int streamId = spdySynReplyFrame.StreamId();

            // Check if we received a valid SYN_REPLY frame
            if (spdySynReplyFrame.IsInvalid() ||
                IsRemoteInitiatedId(streamId) ||
                spdySession.IsRemoteSideClosed(streamId))
            {
                IssueStreamError(ctx, streamId, SpdyStreamStatus.INVALID_STREAM);
                return;
            }

            // Check if we have received multiple frames for the same Stream-ID
            if (spdySession.HasReceivedReply(streamId))
            {
                IssueStreamError(ctx, streamId, SpdyStreamStatus.STREAM_IN_USE);
                return;
            }

            spdySession.ReceivedReply(streamId);

            // Close the remote side of the stream if this is the last frame
            if (spdySynReplyFrame.IsLast())
            {
                HalfCloseStream(ctx, streamId, true, Task.CompletedTask);
            }

        }
        else if (msg is ISpdyRstStreamFrame)
        {

            /*
             * SPDY RST_STREAM frame processing requirements:
             *
             * After receiving a RST_STREAM on a stream, the receiver must not send
             * additional frames on that stream.
             *
             * An endpoint must not send a RST_STREAM in response to a RST_STREAM.
             */

            ISpdyRstStreamFrame spdyRstStreamFrame = (ISpdyRstStreamFrame) msg;
            RemoveStream(ctx, spdyRstStreamFrame.StreamId(), Task.CompletedTask);

        }
        else if (msg is ISpdySettingsFrame)
        {

            ISpdySettingsFrame spdySettingsFrame = (ISpdySettingsFrame) msg;

            int settingsMinorVersion = spdySettingsFrame.GetValue(ISpdySettingsFrame.SETTINGS_MINOR_VERSION);
            if (settingsMinorVersion >= 0 && settingsMinorVersion != minorVersion)
            {
                // Settings frame had the wrong minor version
                IssueSessionError(ctx, SpdySessionStatus.PROTOCOL_ERROR);
                return;
            }

            int newConcurrentStreams =
                spdySettingsFrame.GetValue(ISpdySettingsFrame.SETTINGS_MAX_CONCURRENT_STREAMS);
            if (newConcurrentStreams >= 0)
            {
                remoteConcurrentStreams = newConcurrentStreams;
            }

            // Persistence flag are inconsistent with the use of SETTINGS to communicate
            // the initial window size. Remove flags from the sender requesting that the
            // value be persisted. Remove values that the sender indicates are persisted.
            if (spdySettingsFrame.IsPersisted(ISpdySettingsFrame.SETTINGS_INITIAL_WINDOW_SIZE))
            {
                spdySettingsFrame.RemoveValue(ISpdySettingsFrame.SETTINGS_INITIAL_WINDOW_SIZE);
            }

            spdySettingsFrame.SetPersistValue(ISpdySettingsFrame.SETTINGS_INITIAL_WINDOW_SIZE, false);

            int newInitialWindowSize =
                spdySettingsFrame.GetValue(ISpdySettingsFrame.SETTINGS_INITIAL_WINDOW_SIZE);
            if (newInitialWindowSize >= 0)
            {
                UpdateInitialSendWindowSize(newInitialWindowSize);
            }

        }
        else if (msg is ISpdyPingFrame)
        {

            /*
             * SPDY PING frame processing requirements:
             *
             * Receivers of a PING frame should send an identical frame to the sender
             * as soon as possible.
             *
             * Receivers of a PING frame must ignore frames that it did not initiate
             */

            ISpdyPingFrame spdyPingFrame = (ISpdyPingFrame) msg;

            if (IsRemoteInitiatedId(spdyPingFrame.Id()))
            {
                _ = ctx.WriteAndFlushAsync(spdyPingFrame);
                return;
            }

            // Note: only checks that there are outstanding pings since uniqueness is not enforced
            if (Volatile.Read(ref pings) == 0)
            {
                return;
            }

            Interlocked.Decrement(ref pings);

        }
        else if (msg is ISpdyGoAwayFrame)
        {

            receivedGoAwayFrame = true;

        }
        else if (msg is ISpdyHeadersFrame)
        {

            ISpdyHeadersFrame spdyHeadersFrame = (ISpdyHeadersFrame) msg;
            int streamId = spdyHeadersFrame.StreamId();

            // Check if we received a valid HEADERS frame
            if (spdyHeadersFrame.IsInvalid())
            {
                IssueStreamError(ctx, streamId, SpdyStreamStatus.PROTOCOL_ERROR);
                return;
            }

            if (spdySession.IsRemoteSideClosed(streamId))
            {
                IssueStreamError(ctx, streamId, SpdyStreamStatus.INVALID_STREAM);
                return;
            }

            // Close the remote side of the stream if this is the last frame
            if (spdyHeadersFrame.IsLast())
            {
                HalfCloseStream(ctx, streamId, true, Task.CompletedTask);
            }

        }
        else if (msg is ISpdyWindowUpdateFrame)
        {

            /*
             * SPDY WINDOW_UPDATE frame processing requirements:
             *
             * Receivers of a WINDOW_UPDATE that cause the window size to exceed 2^31
             * must send a RST_STREAM with the getStatus code FLOW_CONTROL_ERROR.
             *
             * Sender should ignore all WINDOW_UPDATE frames associated with a stream
             * after sending the last frame for the stream.
             */

            ISpdyWindowUpdateFrame spdyWindowUpdateFrame = (ISpdyWindowUpdateFrame) msg;
            int streamId = spdyWindowUpdateFrame.StreamId();
            int deltaWindowSize = spdyWindowUpdateFrame.DeltaWindowSize();

            // Ignore frames for half-closed streams
            if (streamId != SPDY_SESSION_STREAM_ID && spdySession.IsLocalSideClosed(streamId))
            {
                return;
            }

            // Check for numerical overflow
            if (spdySession.GetSendWindowSize(streamId) > int.MaxValue - deltaWindowSize)
            {
                if (streamId == SPDY_SESSION_STREAM_ID)
                {
                    IssueSessionError(ctx, SpdySessionStatus.PROTOCOL_ERROR);
                }
                else
                {
                    IssueStreamError(ctx, streamId, SpdyStreamStatus.FLOW_CONTROL_ERROR);
                }

                return;
            }

            UpdateSendWindowSize(ctx, streamId, deltaWindowSize);
        }

        ctx.FireChannelRead(msg);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        foreach (int streamId in spdySession.ActiveStreams().Keys)
        {
            RemoveStream(ctx, streamId, Task.CompletedTask);
        }

        ctx.FireChannelInactive();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (cause is SpdyProtocolException)
        {
            IssueSessionError(ctx, SpdySessionStatus.PROTOCOL_ERROR);
        }

        ctx.FireExceptionCaught(cause);
    }

    public override Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = NewPromise();
        SendGoAwayFrame(ctx, promise);
        return promise.Task;
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (msg is ISpdyDataFrame ||
            msg is ISpdySynStreamFrame ||
            msg is ISpdySynReplyFrame ||
            msg is ISpdyRstStreamFrame ||
            msg is ISpdySettingsFrame ||
            msg is ISpdyPingFrame ||
            msg is ISpdyGoAwayFrame ||
            msg is ISpdyHeadersFrame ||
            msg is ISpdyWindowUpdateFrame)
        {

            TaskCompletionSource promise = NewPromise();
            HandleOutboundMessage(ctx, msg, promise, cancellationToken);
            return promise.Task;
        }
        else
        {
            return ctx.WriteAsync(msg, cancellationToken);
        }
    }

    private void HandleOutboundMessage(IChannelHandlerContext ctx, object msg, TaskCompletionSource promise,
                                       CancellationToken cancellationToken)
    {
        if (msg is ISpdyDataFrame)
        {

            ISpdyDataFrame spdyDataFrame = (ISpdyDataFrame) msg;
            int streamId = spdyDataFrame.StreamId();

            // Frames must not be sent on half-closed streams
            if (spdySession.IsLocalSideClosed(streamId))
            {
                spdyDataFrame.Release();
                promise.SetException(PROTOCOL_EXCEPTION);
                return;
            }

            /*
             * SPDY Data frame flow control processing requirements:
             *
             * Sender must not send a data frame with data length greater
             * than the transfer window size.
             *
             * After sending each data frame, the sender decrements its
             * transfer window size by the amount of data transmitted.
             *
             * When the window size becomes less than or equal to 0, the
             * sender must pause transmitting data frames.
             */

            int dataLength = spdyDataFrame.Content.ReadableBytes;
            int sendWindowSize = spdySession.GetSendWindowSize(streamId);
            int sessionSendWindowSize = spdySession.GetSendWindowSize(SPDY_SESSION_STREAM_ID);
            sendWindowSize = Math.Min(sendWindowSize, sessionSendWindowSize);

            if (sendWindowSize <= 0)
            {
                // Stream is stalled -- enqueue Data frame and return
                spdySession.PutPendingWrite(streamId, new SpdySession.PendingWrite(spdyDataFrame, promise));
                return;
            }
            else if (sendWindowSize < dataLength)
            {
                // Stream is not stalled but we cannot send the entire frame
                spdySession.UpdateSendWindowSize(streamId, -1 * sendWindowSize);
                spdySession.UpdateSendWindowSize(SPDY_SESSION_STREAM_ID, -1 * sendWindowSize);

                // Create a partial data frame whose length is the current window size
                ISpdyDataFrame partialDataFrame = new DefaultSpdyDataFrame(
                        streamId, spdyDataFrame.Content.ReadRetainedSlice(sendWindowSize));

                // Enqueue the remaining data (will be the first frame queued)
                spdySession.PutPendingWrite(streamId, new SpdySession.PendingWrite(spdyDataFrame, promise));

                // The transfer window size is pre-decremented when sending a data frame downstream.
                // Close the session on write failures that leave the transfer window in a corrupt state.
                IChannelHandlerContext context = ctx;
                AddListener(ctx, ctx.WriteAsync(partialDataFrame, cancellationToken), future =>
                {
                    if (!future.IsCompletedSuccessfully)
                    {
                        IssueSessionError(context, SpdySessionStatus.INTERNAL_ERROR);
                    }
                });
                return;
            }
            else
            {
                // Window size is large enough to send entire data frame
                spdySession.UpdateSendWindowSize(streamId, -1 * dataLength);
                spdySession.UpdateSendWindowSize(SPDY_SESSION_STREAM_ID, -1 * dataLength);

                // The transfer window size is pre-decremented when sending a data frame downstream.
                // Close the session on write failures that leave the transfer window in a corrupt state.
                IChannelHandlerContext context = ctx;
                AddListener(ctx, promise.Task, future =>
                {
                    if (!future.IsCompletedSuccessfully)
                    {
                        IssueSessionError(context, SpdySessionStatus.INTERNAL_ERROR);
                    }
                });
            }

            // Close the local side of the stream if this is the last frame
            if (spdyDataFrame.IsLast())
            {
                HalfCloseStream(ctx, streamId, false, promise.Task);
            }

        }
        else if (msg is ISpdySynStreamFrame)
        {

            ISpdySynStreamFrame spdySynStreamFrame = (ISpdySynStreamFrame) msg;
            int streamId = spdySynStreamFrame.StreamId();

            if (IsRemoteInitiatedId(streamId))
            {
                promise.SetException(PROTOCOL_EXCEPTION);
                return;
            }

            byte priority = spdySynStreamFrame.Priority();
            bool remoteSideClosed = spdySynStreamFrame.IsUnidirectional();
            bool localSideClosed = spdySynStreamFrame.IsLast();
            if (!AcceptStream(streamId, priority, remoteSideClosed, localSideClosed))
            {
                promise.SetException(PROTOCOL_EXCEPTION);
                return;
            }

        }
        else if (msg is ISpdySynReplyFrame)
        {

            ISpdySynReplyFrame spdySynReplyFrame = (ISpdySynReplyFrame) msg;
            int streamId = spdySynReplyFrame.StreamId();

            // Frames must not be sent on half-closed streams
            if (!IsRemoteInitiatedId(streamId) || spdySession.IsLocalSideClosed(streamId))
            {
                promise.SetException(PROTOCOL_EXCEPTION);
                return;
            }

            // Close the local side of the stream if this is the last frame
            if (spdySynReplyFrame.IsLast())
            {
                HalfCloseStream(ctx, streamId, false, promise.Task);
            }

        }
        else if (msg is ISpdyRstStreamFrame)
        {

            ISpdyRstStreamFrame spdyRstStreamFrame = (ISpdyRstStreamFrame) msg;
            RemoveStream(ctx, spdyRstStreamFrame.StreamId(), promise.Task);

        }
        else if (msg is ISpdySettingsFrame)
        {

            ISpdySettingsFrame spdySettingsFrame = (ISpdySettingsFrame) msg;

            int settingsMinorVersion = spdySettingsFrame.GetValue(ISpdySettingsFrame.SETTINGS_MINOR_VERSION);
            if (settingsMinorVersion >= 0 && settingsMinorVersion != minorVersion)
            {
                // Settings frame had the wrong minor version
                promise.SetException(PROTOCOL_EXCEPTION);
                return;
            }

            int newConcurrentStreams =
                    spdySettingsFrame.GetValue(ISpdySettingsFrame.SETTINGS_MAX_CONCURRENT_STREAMS);
            if (newConcurrentStreams >= 0)
            {
                localConcurrentStreams = newConcurrentStreams;
            }

            // Persistence flag are inconsistent with the use of SETTINGS to communicate
            // the initial window size. Remove flags from the sender requesting that the
            // value be persisted. Remove values that the sender indicates are persisted.
            if (spdySettingsFrame.IsPersisted(ISpdySettingsFrame.SETTINGS_INITIAL_WINDOW_SIZE))
            {
                spdySettingsFrame.RemoveValue(ISpdySettingsFrame.SETTINGS_INITIAL_WINDOW_SIZE);
            }

            spdySettingsFrame.SetPersistValue(ISpdySettingsFrame.SETTINGS_INITIAL_WINDOW_SIZE, false);

            int newInitialWindowSize =
                    spdySettingsFrame.GetValue(ISpdySettingsFrame.SETTINGS_INITIAL_WINDOW_SIZE);
            if (newInitialWindowSize >= 0)
            {
                UpdateInitialReceiveWindowSize(newInitialWindowSize);
            }

        }
        else if (msg is ISpdyPingFrame)
        {

            ISpdyPingFrame spdyPingFrame = (ISpdyPingFrame) msg;
            if (IsRemoteInitiatedId(spdyPingFrame.Id()))
            {
                ctx.FireExceptionCaught(new ArgumentException(
                            "invalid PING ID: " + spdyPingFrame.Id().ToString(CultureInfo.InvariantCulture)));
                return;
            }

            Interlocked.Increment(ref pings);

        }
        else if (msg is ISpdyGoAwayFrame)
        {

            // Why is this being sent? Intercept it and fail the write.
            // Should have sent a CLOSE ChannelStateEvent
            promise.SetException(PROTOCOL_EXCEPTION);
            return;

        }
        else if (msg is ISpdyHeadersFrame)
        {

            ISpdyHeadersFrame spdyHeadersFrame = (ISpdyHeadersFrame) msg;
            int streamId = spdyHeadersFrame.StreamId();

            // Frames must not be sent on half-closed streams
            if (spdySession.IsLocalSideClosed(streamId))
            {
                promise.SetException(PROTOCOL_EXCEPTION);
                return;
            }

            // Close the local side of the stream if this is the last frame
            if (spdyHeadersFrame.IsLast())
            {
                HalfCloseStream(ctx, streamId, false, promise.Task);
            }

        }
        else if (msg is ISpdyWindowUpdateFrame)
        {

            // Why is this being sent? Intercept it and fail the write.
            promise.SetException(PROTOCOL_EXCEPTION);
            return;
        }

        Write(ctx, msg, promise, cancellationToken);
    }

    /*
     * SPDY Session Error Handling:
     *
     * When a session error occurs, the endpoint encountering the error must first
     * send a GOAWAY frame with the Stream-ID of the most recently received stream
     * from the remote endpoint, and the error code for why the session is terminating.
     *
     * After sending the GOAWAY frame, the endpoint must close the TCP connection.
     */
    private void IssueSessionError(
            IChannelHandlerContext ctx, SpdySessionStatus status)
    {

        AddListener(ctx, SendGoAwayFrame(ctx, status), new ClosingChannelFutureListener(ctx, NewPromise()));
    }

    /*
     * SPDY Stream Error Handling:
     *
     * Upon a stream error, the endpoint must send a RST_STREAM frame which contains
     * the Stream-ID for the stream where the error occurred and the error getStatus which
     * caused the error.
     *
     * After sending the RST_STREAM, the stream is closed to the sending endpoint.
     *
     * Note: this is only called by the worker thread
     */
    private void IssueStreamError(IChannelHandlerContext ctx, int streamId, SpdyStreamStatus status)
    {
        bool fireChannelRead = !spdySession.IsRemoteSideClosed(streamId);
        TaskCompletionSource promise = NewPromise();
        RemoveStream(ctx, streamId, promise.Task);

        ISpdyRstStreamFrame spdyRstStreamFrame = new DefaultSpdyRstStreamFrame(streamId, status);
        WriteAndFlush(ctx, spdyRstStreamFrame, promise);

        if (fireChannelRead)
        {
            ctx.FireChannelRead(spdyRstStreamFrame);
        }
    }

    /*
     * Helper functions
     */

    private bool IsRemoteInitiatedId(int id)
    {
        bool serverId = IsServerId(id);
        return server && !serverId || !server && serverId;
    }

    // need to synchronize to prevent new streams from being created while updating active streams
    private void UpdateInitialSendWindowSize(int newInitialWindowSize)
    {
        int deltaWindowSize = newInitialWindowSize - initialSendWindowSize;
        initialSendWindowSize = newInitialWindowSize;
        spdySession.UpdateAllSendWindowSizes(deltaWindowSize);
    }

    // need to synchronize to prevent new streams from being created while updating active streams
    private void UpdateInitialReceiveWindowSize(int newInitialWindowSize)
    {
        int deltaWindowSize = newInitialWindowSize - initialReceiveWindowSize;
        initialReceiveWindowSize = newInitialWindowSize;
        spdySession.UpdateAllReceiveWindowSizes(deltaWindowSize);
    }

    // need to synchronize accesses to sentGoAwayFrame, lastGoodStreamId, and initial window sizes
    private bool AcceptStream(
            int streamId, byte priority, bool remoteSideClosed, bool localSideClosed)
    {
        // Cannot initiate any new streams after receiving or sending GOAWAY
        if (receivedGoAwayFrame || sentGoAwayFrame)
        {
            return false;
        }

        bool remote = IsRemoteInitiatedId(streamId);
        int maxConcurrentStreams = remote ? localConcurrentStreams : remoteConcurrentStreams;
        if (spdySession.NumActiveStreams(remote) >= maxConcurrentStreams)
        {
            return false;
        }

        spdySession.AcceptStream(
                streamId, priority, remoteSideClosed, localSideClosed,
                initialSendWindowSize, initialReceiveWindowSize, remote);

        if (remote)
        {
            lastGoodStreamId = streamId;
        }

        return true;
    }

    private void HalfCloseStream(IChannelHandlerContext ctx, int streamId, bool remote, Task future)
    {
        if (remote)
        {
            spdySession.CloseRemoteSide(streamId, IsRemoteInitiatedId(streamId));
        }
        else
        {
            spdySession.CloseLocalSide(streamId, IsRemoteInitiatedId(streamId));
        }

        if (closeSessionFutureListener != null && spdySession.NoActiveStreams())
        {
            AddListener(ctx, future, closeSessionFutureListener);
        }
    }

    private void RemoveStream(IChannelHandlerContext ctx, int streamId, Task future)
    {
        spdySession.RemoveStream(streamId, STREAM_CLOSED, IsRemoteInitiatedId(streamId));

        if (closeSessionFutureListener != null && spdySession.NoActiveStreams())
        {
            AddListener(ctx, future, closeSessionFutureListener);
        }
    }

    private void UpdateSendWindowSize(IChannelHandlerContext ctx, int streamId, int deltaWindowSize)
    {
        spdySession.UpdateSendWindowSize(streamId, deltaWindowSize);

        while (true)
        {
            // Check if we have unblocked a stalled stream
            SpdySession.PendingWrite pendingWrite = spdySession.GetPendingWrite(streamId);
            if (pendingWrite == null)
            {
                return;
            }

            ISpdyDataFrame spdyDataFrame = pendingWrite.spdyDataFrame;
            int dataFrameSize = spdyDataFrame.Content.ReadableBytes;
            int writeStreamId = spdyDataFrame.StreamId();
            int sendWindowSize = spdySession.GetSendWindowSize(writeStreamId);
            int sessionSendWindowSize = spdySession.GetSendWindowSize(SPDY_SESSION_STREAM_ID);
            sendWindowSize = Math.Min(sendWindowSize, sessionSendWindowSize);

            if (sendWindowSize <= 0)
            {
                return;
            }
            else if (sendWindowSize < dataFrameSize)
            {
                // We can send a partial frame
                spdySession.UpdateSendWindowSize(writeStreamId, -1 * sendWindowSize);
                spdySession.UpdateSendWindowSize(SPDY_SESSION_STREAM_ID, -1 * sendWindowSize);

                // Create a partial data frame whose length is the current window size
                ISpdyDataFrame partialDataFrame = new DefaultSpdyDataFrame(
                        writeStreamId, spdyDataFrame.Content.ReadRetainedSlice(sendWindowSize));

                // The transfer window size is pre-decremented when sending a data frame downstream.
                AddListener(ctx, ctx.WriteAndFlushAsync(partialDataFrame), future =>
                {
                    if (!future.IsCompletedSuccessfully)
                    {
                        IssueSessionError(ctx, SpdySessionStatus.INTERNAL_ERROR);
                    }
                });
            }
            else
            {
                // Window size is large enough to send entire data frame
                spdySession.RemovePendingWrite(writeStreamId);
                spdySession.UpdateSendWindowSize(writeStreamId, -1 * dataFrameSize);
                spdySession.UpdateSendWindowSize(SPDY_SESSION_STREAM_ID, -1 * dataFrameSize);

                // Close the local side of the stream if this is the last frame
                if (spdyDataFrame.IsLast())
                {
                    HalfCloseStream(ctx, writeStreamId, false, pendingWrite.promise.Task);
                }

                // The transfer window size is pre-decremented when sending a data frame downstream.
                // Close the session on write failures that leave the transfer window in a corrupt state.
                AddListener(ctx, WriteAndFlush(ctx, spdyDataFrame, pendingWrite.promise), future =>
                {
                    if (!future.IsCompletedSuccessfully)
                    {
                        IssueSessionError(ctx, SpdySessionStatus.INTERNAL_ERROR);
                    }
                });
            }
        }
    }

    private void SendGoAwayFrame(IChannelHandlerContext ctx, TaskCompletionSource future)
    {
        // Avoid NotYetConnectedException
        if (!ctx.Channel.IsActive)
        {
            Close(ctx, future);
            return;
        }

        Task f = SendGoAwayFrame(ctx, SpdySessionStatus.OK);

        if (spdySession.NoActiveStreams())
        {
            AddListener(ctx, f, new ClosingChannelFutureListener(ctx, future));
        }
        else
        {
            closeSessionFutureListener = new ClosingChannelFutureListener(ctx, future);
        }
        // FIXME: Close the connection forcibly after timeout.
    }

    private Task SendGoAwayFrame(
            IChannelHandlerContext ctx, SpdySessionStatus status)
    {
        if (!sentGoAwayFrame)
        {
            sentGoAwayFrame = true;
            ISpdyGoAwayFrame spdyGoAwayFrame = new DefaultSpdyGoAwayFrame(lastGoodStreamId, status);
            return ctx.WriteAndFlushAsync(spdyGoAwayFrame);
        }
        else
        {
            return Task.CompletedTask;
        }
    }

    // CLR: ctx.newPromise(): a promise the handler completes (see the class comment).
    private static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // CLR: ctx.write(msg, promise): the promise completes with the write's Task.
    private static void Write(IChannelHandlerContext ctx, object msg, TaskCompletionSource promise,
                              CancellationToken cancellationToken)
    {
        Notify(ctx, ctx.WriteAsync(msg, cancellationToken), promise);
    }

    // CLR: ctx.writeAndFlush(msg, promise), which returns the promise.
    private static Task WriteAndFlush(IChannelHandlerContext ctx, object msg, TaskCompletionSource promise)
    {
        Notify(ctx, ctx.WriteAndFlushAsync(msg), promise);
        return promise.Task;
    }

    private static void Close(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        Notify(ctx, ctx.CloseAsync(), promise);
    }

    // CLR: completes the promise from the operation's Task (TrySetFromTask) on the listener scheduler.
    private static void Notify(IChannelHandlerContext ctx, Task future, TaskCompletionSource promise)
    {
        _ = future.ContinueWith(static (t, state) => ((TaskCompletionSource) state).TrySetFromTask(t), promise,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
    }

    private static void AddListener(IChannelHandlerContext ctx, Task future, Action<Task> listener)
    {
        _ = future.ContinueWith(listener, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
    }

    private static void AddListener(IChannelHandlerContext ctx, Task future, ClosingChannelFutureListener listener)
    {
        AddListener(ctx, future, listener.OperationComplete);
    }

    private sealed class ClosingChannelFutureListener
    {
        private readonly IChannelHandlerContext ctx;
        private readonly TaskCompletionSource promise;

        internal ClosingChannelFutureListener(IChannelHandlerContext ctx, TaskCompletionSource promise)
        {
            this.ctx = ctx;
            this.promise = promise;
        }

        public void OperationComplete(Task sentGoAwayFuture)
        {
            Close(ctx, promise);
        }
    }
}
