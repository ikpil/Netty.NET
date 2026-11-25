/*
 * Copyright 2017 The Netty Project
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
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Ssl;

/**
 * {@link ByteToMessageDecoder} which allows to be notified once a full {@code ClientHello} was received.
 */
// CLR: Future<T> is Task<T> (docs/common-task-composition.md): isDone() is IsCompleted, and the FutureListener is a
// continuation on Common's EventExecutorTaskScheduler over ctx's executor, which runs it at once when the lookup
// completes on the event loop and otherwise hands it to the executor, the policy of DefaultPromise.notifyListeners()
// (src/Netty.NET.Common/Concurrent/EventExecutorTaskScheduler.cs). The outbound operations take the CancellationToken
// and return the Task that replace the ChannelPromise, as in codec-base DatagramPacketEncoder; SocketAddress is
// EndPoint.
// List<Object> out is IList<object> @out. catch (Throwable) is catch (Exception), so the catch (Throwable) clauses that
// follow a catch (Exception) have no counterpart, and PlatformDependent.throwException(cause) is throw;
// (src/Netty.NET.Handler/IpFilter/AbstractRemoteAddressFilter.cs). getUnsignedByte is GetByte, the unsigned System.Byte
// (owner decision); the lengths read with getUnsignedShort/getUnsignedMedium are non-negative in both runtimes.
public abstract class SslClientHelloHandler<T> : ByteToMessageDecoder, IChannelOutboundHandler
{

    /**
     * The maximum length of client hello message as defined by
     * <a href="https://www.rfc-editor.org/rfc/rfc5246#section-6.2.1">RFC5246</a>.
     */
    public const int MAX_CLIENT_HELLO_LENGTH = 0xFFFFFF;

    // Let's use a default limit of 64kb which should be big enough for almost everything in practice but still
    // small enough to not allocate to much memory.
    internal const int DEFAULT_MAX_CLIENT_HELLO_LENGTH = 64 * 1024;

    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(SslClientHelloHandler<T>));

    private readonly int maxClientHelloLength;
    private bool handshakeFailed;
    private bool suppressRead;
    private bool readPending;
    private ByteBuf handshakeBuffer;
    private int aggregatedBytes;
    private int handshakeLength = -1;

    public SslClientHelloHandler() : this(DEFAULT_MAX_CLIENT_HELLO_LENGTH)
    {
    }

    protected SslClientHelloHandler(int maxClientHelloLength)
    {
        // 16MB is the maximum as per RFC:
        // See https://www.rfc-editor.org/rfc/rfc5246#section-6.2.1
        this.maxClientHelloLength =
                ObjectUtil.CheckInRange(maxClientHelloLength, 0, MAX_CLIENT_HELLO_LENGTH, "maxClientHelloLength");
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (!suppressRead && !handshakeFailed)
        {
            try
            {
                int readerIndex = @in.ReaderIndex + aggregatedBytes;
                int readableBytes = @in.ReadableBytes - aggregatedBytes;

                // Check if we have enough data to determine the record type and length.
                while (readableBytes >= SslUtils.SSL_RECORD_HEADER_LENGTH)
                {
                    int contentType = @in.GetByte(readerIndex);
                    switch (contentType)
                    {
                        case SslUtils.SSL_CONTENT_TYPE_CHANGE_CIPHER_SPEC:
                        // fall-through
                        case SslUtils.SSL_CONTENT_TYPE_ALERT:
                            int len = SslUtils.GetEncryptedPacketLength(@in, readerIndex, true);

                            // Not an SSL/TLS packet
                            if (len == SslUtils.NOT_ENCRYPTED)
                            {
                                handshakeFailed = true;
                                NotSslRecordException e = new NotSslRecordException(
                                        "not an SSL/TLS record: " + ByteBufUtil.HexDump(@in));
                                @in.SkipBytes(@in.ReadableBytes);
                                ctx.FireUserEventTriggered(new SniCompletionEvent(e));
                                SslUtils.HandleHandshakeFailure(ctx, e, true);
                                throw e;
                            }
                            if (len == SslUtils.NOT_ENOUGH_DATA)
                            {
                                // Not enough data
                                return;
                            }
                            // No ClientHello
                            Select(ctx, null);
                            return;
                        case SslUtils.SSL_CONTENT_TYPE_HANDSHAKE:
                            int majorVersion = @in.GetByte(readerIndex + 1);
                            // SSLv3 or TLS
                            if (majorVersion == 3)
                            {
                                int packetLength = @in.GetUnsignedShort(readerIndex + 3) +
                                        SslUtils.SSL_RECORD_HEADER_LENGTH;

                                if (readableBytes < packetLength)
                                {
                                    // client hello incomplete; try again to decode once more data is ready.
                                    return;
                                }

                                if (packetLength == SslUtils.SSL_RECORD_HEADER_LENGTH)
                                {
                                    // Zero-length handshake records are forbidden by
                                    // https://www.rfc-editor.org/rfc/rfc8446#section-5.1 and we can't select
                                    // the default context for them: some TLS implementations skip the empty
                                    // record and still process a ClientHello that follows it, which would allow
                                    // to bypass the SNI based selection. We also can't just skip them as this
                                    // would allow to buffer an unbounded amount of data. Fail closed instead.
                                    handshakeFailed = true;
                                    NotSslRecordException e = new NotSslRecordException(
                                            "zero-length handshake record: " + ByteBufUtil.HexDump(@in));
                                    @in.SkipBytes(@in.ReadableBytes);
                                    ctx.FireUserEventTriggered(new SniCompletionEvent(e));
                                    SslUtils.HandleHandshakeFailure(ctx, e, true);
                                    throw e;
                                }

                                int endOffset = readerIndex + packetLength;

                                // Let's check if we already parsed the handshake length or not.
                                if (handshakeLength == -1)
                                {
                                    if (handshakeBuffer == null &&
                                            readerIndex + SslUtils.SSL_RECORD_HEADER_LENGTH + 4 <= endOffset)
                                    {
                                        int handshakeType = @in.GetByte(readerIndex +
                                                SslUtils.SSL_RECORD_HEADER_LENGTH);

                                        // Check if this is a clientHello(1)
                                        // See https://tools.ietf.org/html/rfc5246#section-7.4
                                        if (handshakeType != 1)
                                        {
                                            Select(ctx, null);
                                            return;
                                        }

                                        // Read the length of the handshake as it may arrive in fragments
                                        // See https://tools.ietf.org/html/rfc5246#section-7.4
                                        handshakeLength = @in.GetUnsignedMedium(readerIndex +
                                                SslUtils.SSL_RECORD_HEADER_LENGTH + 1);

                                        if (handshakeLength > maxClientHelloLength && maxClientHelloLength != 0)
                                        {
                                            TooLongFrameException e = new TooLongFrameException(
                                                    "ClientHello length exceeds " + maxClientHelloLength +
                                                            ": " + handshakeLength);
                                            @in.SkipBytes(@in.ReadableBytes);
                                            ctx.FireUserEventTriggered(new SniCompletionEvent(e));
                                            SslUtils.HandleHandshakeFailure(ctx, e, true);
                                            throw e;
                                        }

                                        if (handshakeLength + 4 + SslUtils.SSL_RECORD_HEADER_LENGTH <= packetLength)
                                        {
                                            // We have everything we need in one packet.
                                            // Skip the record header and handshake header (this sums up as 4 bytes)
                                            readerIndex += SslUtils.SSL_RECORD_HEADER_LENGTH + 4;
                                            int clientHelloLength = handshakeLength;
                                            handshakeLength = -1;
                                            Select(ctx, @in.RetainedSlice(readerIndex, clientHelloLength));
                                            return;
                                        }
                                    }
                                }

                                if (handshakeBuffer == null)
                                {
                                    handshakeBuffer = ctx.Alloc().Buffer();
                                }

                                // Combine the encapsulated data in one buffer but not include the SSL_RECORD_HEADER
                                handshakeBuffer.WriteBytes(@in, readerIndex + SslUtils.SSL_RECORD_HEADER_LENGTH,
                                        packetLength - SslUtils.SSL_RECORD_HEADER_LENGTH);
                                readerIndex += packetLength;
                                readableBytes -= packetLength;
                                aggregatedBytes += packetLength;
                                if (handshakeLength == -1)
                                {
                                    if (handshakeBuffer.ReadableBytes < 4)
                                    {
                                        continue;
                                    }

                                    int handshakeType = handshakeBuffer.GetByte(0);
                                    handshakeLength = handshakeBuffer.GetUnsignedMedium(1);

                                    // Check if this is a clientHello(1)
                                    // See https://tools.ietf.org/html/rfc5246#section-7.4
                                    if (handshakeType != 1)
                                    {
                                        Select(ctx, null);
                                        return;
                                    }

                                    if (handshakeLength > maxClientHelloLength && maxClientHelloLength != 0)
                                    {
                                        TooLongFrameException e = new TooLongFrameException(
                                                "ClientHello length exceeds " + maxClientHelloLength +
                                                        ": " + handshakeLength);
                                        @in.SkipBytes(@in.ReadableBytes);
                                        ctx.FireUserEventTriggered(new SniCompletionEvent(e));
                                        SslUtils.HandleHandshakeFailure(ctx, e, true);
                                        throw e;
                                    }
                                }

                                if (handshakeBuffer.ReadableBytes >= handshakeLength + 4)
                                {
                                    ByteBuf clientHello = handshakeBuffer.SetIndex(4, handshakeLength + 4).Slice();
                                    handshakeBuffer = null;
                                    handshakeLength = -1;

                                    Select(ctx, clientHello);
                                    return;
                                }
                                break;
                            }
                            // fall-through
                            goto default;
                        default:
                            // not tls, ssl or application data
                            Select(ctx, null);
                            return;
                    }
                }
            }
            catch (NotSslRecordException)
            {
                // Just rethrow as in this case we also closed the channel and this is consistent with SslHandler.
                throw;
            }
            catch (TooLongFrameException)
            {
                // Just rethrow as in this case we also closed the channel
                throw;
            }
            catch (Exception e)
            {
                // unexpected encoding, ignore sni and use default
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("Unexpected client hello packet: " + ByteBufUtil.HexDump(@in), e);
                }
                Select(ctx, null);
            }
        }
    }

    private void ReleaseHandshakeBuffer()
    {
        ReleaseIfNotNull(handshakeBuffer);
        handshakeBuffer = null;
        handshakeLength = -1;
    }

    private static void ReleaseIfNotNull(ByteBuf buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
        }
    }

    private void Select(IChannelHandlerContext ctx, ByteBuf clientHello)
    {
        Task<T> future;
        try
        {
            future = Lookup(ctx, clientHello);
            if (future.IsCompleted)
            {
                try
                {
                    OnLookupComplete(ctx, future);
                }
                catch (DecoderException err)
                {
                    ctx.FireExceptionCaught(err);
                }
                catch (Exception cause)
                {
                    ctx.FireExceptionCaught(new DecoderException(cause));
                }
            }
            else
            {
                suppressRead = true;
                ByteBuf finalClientHello = clientHello;
                _ = future.ContinueWith(future1 =>
                {
                    ReleaseIfNotNull(finalClientHello);
                    try
                    {
                        suppressRead = false;
                        try
                        {
                            OnLookupComplete(ctx, future1);
                        }
                        catch (DecoderException err)
                        {
                            ctx.FireExceptionCaught(err);
                        }
                        catch (Exception cause)
                        {
                            ctx.FireExceptionCaught(new DecoderException(cause));
                        }
                    }
                    finally
                    {
                        if (readPending)
                        {
                            readPending = false;
                            ctx.Read();
                        }
                    }
                }, CancellationToken.None, TaskContinuationOptions.None,
                        new EventExecutorTaskScheduler(() => ctx.Executor));

                // Ownership was transferred to the FutureListener.
                clientHello = null;
            }
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            ReleaseIfNotNull(clientHello);
        }
    }

    protected override void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        ReleaseHandshakeBuffer();

        base.HandlerRemoved0(ctx);
    }

    /**
     * Kicks off a lookup for the given {@code ClientHello} and returns a {@link Future} which in turn will
     * notify the {@link #onLookupComplete(ChannelHandlerContext, Future)} on completion.
     *
     * See https://tools.ietf.org/html/rfc5246#section-7.4.1.2
     *
     * <pre>
     * struct {
     *    ProtocolVersion client_version;
     *    Random random;
     *    SessionID session_id;
     *    CipherSuite cipher_suites<2..2^16-2>;
     *    CompressionMethod compression_methods<1..2^8-1>;
     *    select (extensions_present) {
     *        case false:
     *            struct {};
     *        case true:
     *            Extension extensions<0..2^16-1>;
     *    };
     * } ClientHello;
     * </pre>
     *
     * @see #onLookupComplete(ChannelHandlerContext, Future)
     */
    protected abstract Task<T> Lookup(IChannelHandlerContext ctx, ByteBuf clientHello);

    /**
     * Called upon completion of the {@link #lookup(ChannelHandlerContext, ByteBuf)} {@link Future}.
     *
     * @see #lookup(ChannelHandlerContext, ByteBuf)
     */
    protected abstract void OnLookupComplete(IChannelHandlerContext ctx, Task<T> future);

    public virtual void Read(IChannelHandlerContext ctx)
    {
        if (suppressRead)
        {
            readPending = true;
        }
        else
        {
            ctx.Read();
        }
    }

    public virtual Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress,
            CancellationToken cancellationToken)
    {
        return ctx.BindAsync(localAddress, cancellationToken);
    }

    public virtual Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress,
                        CancellationToken cancellationToken)
    {
        return ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public virtual Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DisconnectAsync(cancellationToken);
    }

    public virtual Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.CloseAsync(cancellationToken);
    }

    public virtual Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DeregisterAsync(cancellationToken);
    }

    public virtual Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        return ctx.WriteAsync(msg, cancellationToken);
    }

    public virtual void Flush(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }
}
