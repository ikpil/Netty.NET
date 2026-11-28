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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Ssl;

/**
 * <p>Enables <a href="https://tools.ietf.org/html/rfc3546#section-3.1">SNI
 * (Server Name Indication)</a> extension for server side SSL. For clients
 * support SNI, the server could have multiple host name bound on a single IP.
 * The client will send host name in the handshake data so server could decide
 * which certificate to choose for the host name.</p>
 */
// CLR: Future<T> is Task<T> (see SslClientHelloHandler). ScheduledFuture<?> of the timeout is the
// CancellationTokenSource whose token was passed to ctx.Executor.ScheduleAsync (cancel(false) is Cancel()), as
// IdleStateHandler keeps its timeouts (src/Netty.NET.Handler/Timeout/IdleStateHandler.cs); the scheduled Task is not
// observed, as nothing observes the Java future. The anonymous Runnable is a lambda. in.toString(offset, length,
// US_ASCII) takes ByteBufUtil.decodeString's US-ASCII fast path, new String(array, 0, offset, len), whose characters
// are "(char)(((hibyte & 0xff) << 8) | (b & 0xff))"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), so every octet is decoded as
// Latin-1 ("pinned ByteBufUtil.decodeString's deprecated ASCII fast path maps all octets as Latin-1. Encoding.Latin1
// provides that mapping", docs/buffer-porting.md); Encoding.ASCII would give '?' for octets above 127.
// toLowerCase(Locale.US) is ToLowerInvariant(), which maps the Latin-1 letters the same way (Locale.US has no special
// casing). getUnsignedByte is GetByte, the unsigned System.Byte (owner decision).
public abstract class AbstractSniHandler<T> : SslClientHelloHandler<T>
{

    // Package-private for testing purposes.
    internal static string ExtractSniHostname(ByteBuf @in)
    {
        // See https://tools.ietf.org/html/rfc5246#section-7.4.1.2
        //
        // Decode the ssl client hello packet.
        //
        // struct {
        //    ProtocolVersion client_version;
        //    Random random;
        //    SessionID session_id;
        //    CipherSuite cipher_suites<2..2^16-2>;
        //    CompressionMethod compression_methods<1..2^8-1>;
        //    select (extensions_present) {
        //        case false:
        //            struct {};
        //        case true:
        //            Extension extensions<0..2^16-1>;
        //    };
        // } ClientHello;
        //

        // We have to skip bytes until SessionID (which sum to 34 bytes in this case).
        int offset = @in.ReaderIndex;
        int endOffset = @in.WriterIndex;
        offset += 34;

        if (endOffset - offset >= 6)
        {
            int sessionIdLength = @in.GetByte(offset);
            offset += sessionIdLength + 1;

            if (endOffset - offset >= 2)
            {
                int cipherSuitesLength = @in.GetUnsignedShort(offset);
                offset += cipherSuitesLength + 2;

                if (endOffset - offset >= 1)
                {
                    int compressionMethodLength = @in.GetByte(offset);
                    offset += compressionMethodLength + 1;

                    if (endOffset - offset >= 2)
                    {
                        int extensionsLength = @in.GetUnsignedShort(offset);
                        offset += 2;
                        int extensionsLimit = offset + extensionsLength;

                        // Extensions should never exceed the record boundary.
                        if (extensionsLimit <= endOffset)
                        {
                            while (extensionsLimit - offset >= 4)
                            {
                                int extensionType = @in.GetUnsignedShort(offset);
                                offset += 2;

                                int extensionLength = @in.GetUnsignedShort(offset);
                                offset += 2;

                                if (extensionsLimit - offset < extensionLength)
                                {
                                    break;
                                }

                                // SNI
                                // See https://tools.ietf.org/html/rfc6066#page-6
                                if (extensionType == 0)
                                {
                                    offset += 2;
                                    if (extensionsLimit - offset < 3)
                                    {
                                        break;
                                    }

                                    int serverNameType = @in.GetByte(offset);
                                    offset++;

                                    if (serverNameType == 0)
                                    {
                                        int serverNameLength = @in.GetUnsignedShort(offset);
                                        offset += 2;

                                        if (extensionsLimit - offset < serverNameLength)
                                        {
                                            break;
                                        }

                                        string hostname = @in.ToString(
                                                offset, serverNameLength, Encoding.Latin1);
                                        return hostname.ToLowerInvariant();
                                    }
                                    else
                                    {
                                        // invalid enum value
                                        break;
                                    }
                                }

                                offset += extensionLength;
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    // CLR: TimeUnit.SECONDS.toMillis(10).
    internal const long DEFAULT_HANDSHAKE_TIMEOUT_MILLIS = 10 * 1000L;
    protected readonly long handshakeTimeoutMillis;
    private CancellationTokenSource timeoutFuture;
    private string hostname;

    /**
     * @param handshakeTimeoutMillis    the handshake timeout in milliseconds
     */
    protected AbstractSniHandler(long handshakeTimeoutMillis)
        : this(DEFAULT_MAX_CLIENT_HELLO_LENGTH, handshakeTimeoutMillis)
    {
    }

    /**
     * @param maxClientHelloLength     the maximum length of the client hello message.
     * @param handshakeTimeoutMillis    the handshake timeout in milliseconds
     */
    protected AbstractSniHandler(int maxClientHelloLength, long handshakeTimeoutMillis) : base(maxClientHelloLength)
    {
        this.handshakeTimeoutMillis = ObjectUtil.CheckPositiveOrZero(handshakeTimeoutMillis, "handshakeTimeoutMillis");
    }

    public AbstractSniHandler() : this(DEFAULT_MAX_CLIENT_HELLO_LENGTH, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        if (ctx.Channel.IsActive)
        {
            CheckStartTimeout(ctx);
        }
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        ctx.FireChannelActive();
        CheckStartTimeout(ctx);
    }

    private void CheckStartTimeout(IChannelHandlerContext ctx)
    {
        if (handshakeTimeoutMillis <= 0 || timeoutFuture != null)
        {
            return;
        }

        timeoutFuture = new CancellationTokenSource();
        _ = ctx.Executor.ScheduleAsync(() =>
        {
            if (ctx.Channel.IsActive)
            {
                SslHandshakeTimeoutException exception = new SslHandshakeTimeoutException(
                    "handshake timed out after " + handshakeTimeoutMillis + "ms");
                ctx.FireUserEventTriggered(new SniCompletionEvent(exception));
                _ = ctx.CloseAsync();
            }
        }, TimeSpan.FromMilliseconds(handshakeTimeoutMillis), timeoutFuture.Token);
    }

    protected override Task<T> Lookup(IChannelHandlerContext ctx, ByteBuf clientHello)
    {
        hostname = clientHello == null ? null : ExtractSniHostname(clientHello);

        return Lookup(ctx, hostname);
    }

    protected override void OnLookupComplete(IChannelHandlerContext ctx, Task<T> future)
    {
        if (timeoutFuture != null)
        {
            timeoutFuture.Cancel();
        }

        try
        {
            OnLookupComplete(ctx, hostname, future);
        }
        finally
        {
            FireSniCompletionEvent(ctx, hostname, future);
        }
    }

    /**
     * Kicks off a lookup for the given SNI value and returns a {@link Future} which in turn will
     * notify the {@link #onLookupComplete(ChannelHandlerContext, String, Future)} on completion.
     *
     * @see #onLookupComplete(ChannelHandlerContext, String, Future)
     */
    protected abstract Task<T> Lookup(IChannelHandlerContext ctx, string hostname);

    /**
     * Called upon completion of the {@link #lookup(ChannelHandlerContext, String)} {@link Future}.
     *
     * @see #lookup(ChannelHandlerContext, String)
     */
    protected abstract void OnLookupComplete(IChannelHandlerContext ctx,
                                             string hostname, Task<T> future);

    private static void FireSniCompletionEvent(IChannelHandlerContext ctx, string hostname, Task future)
    {
        Exception cause = Cause(future);
        if (cause == null)
        {
            ctx.FireUserEventTriggered(new SniCompletionEvent(hostname));
        }
        else
        {
            ctx.FireUserEventTriggered(new SniCompletionEvent(hostname, cause));
        }
    }

    // CLR: Future.cause() of a completed Task: the exception of a faulted Task (the single inner exception, as
    // Transport AbstractBootstrap.Cause reads it), and for a cancelled Task, whose Java cause() is a
    // CancellationException, a TaskCanceledException "with a reference to the Task that has been canceled"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcanceledexception.-ctor?view=net-10.0).
    private static Exception Cause(Task future)
    {
        if (future.IsFaulted)
        {
            return future.Exception.InnerExceptions.Count == 1 ? future.Exception.InnerException : future.Exception;
        }

        return future.IsCanceled ? new TaskCanceledException(future) : null;
    }
}
