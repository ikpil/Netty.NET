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

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Ssl;

/**
 * Adds <a href="https://en.wikipedia.org/wiki/Transport_Layer_Security">SSL
 * &middot; TLS</a> and StartTLS support to a {@link Channel}.  Please refer
 * to the <strong>"SecureChat"</strong> example in the distribution or the web
 * site for the detailed usage.
 *
 * <h3>Beginning the handshake</h3>
 * <p>
 * Beside using the handshake {@link ChannelFuture} to get notified about the completion of the handshake it's
 * also possible to detect it by implement the
 * {@link ChannelInboundHandler#userEventTriggered(ChannelHandlerContext, Object)}
 * method and check for a {@link SslHandshakeCompletionEvent}.
 *
 * <h3>Handshake</h3>
 * <p>
 * The handshake will be automatically issued for you once the {@link Channel} is active and
 * {@link SSLEngine#getUseClientMode()} returns {@code true}.
 * So no need to bother with it by your self.
 *
 * <h3>Closing the session</h3>
 * <p>
 * To close the SSL session, the {@link #closeOutbound()} method should be
 * called to send the {@code close_notify} message to the remote peer. One
 * exception is when you close the {@link Channel} - {@link SslHandler}
 * intercepts the close request and send the {@code close_notify} message
 * before the channel closure automatically.  Once the SSL session is closed,
 * it is not reusable, and consequently you should create a new
 * {@link SslHandler} with a new {@link SSLEngine} as explained in the
 * following section.
 *
 * <h3>Restarting the session</h3>
 * <p>
 * To restart the SSL session, you must remove the existing closed
 * {@link SslHandler} from the {@link ChannelPipeline}, insert a new
 * {@link SslHandler} with a new {@link SSLEngine} into the pipeline,
 * and start the handshake process as described in the first section.
 *
 * <h3>Implementing StartTLS</h3>
 * <p>
 * <a href="https://en.wikipedia.org/wiki/STARTTLS">StartTLS</a> is the
 * communication pattern that secures the wire in the middle of the plaintext
 * connection.  Please note that it is different from SSL &middot; TLS, that
 * secures the wire from the beginning of the connection.  Typically, StartTLS
 * is composed of three steps:
 * <ol>
 * <li>Client sends a StartTLS request to server.</li>
 * <li>Server sends a StartTLS response to client.</li>
 * <li>Client begins SSL handshake.</li>
 * </ol>
 * If you implement a server, you need to:
 * <ol>
 * <li>create a new {@link SslHandler} instance with {@code startTls} flag set
 *     to {@code true},</li>
 * <li>insert the {@link SslHandler} to the {@link ChannelPipeline}, and</li>
 * <li>write a StartTLS response.</li>
 * </ol>
 * Please note that you must insert {@link SslHandler} <em>before</em> sending
 * the StartTLS response.  Otherwise the client can send begin SSL handshake
 * before {@link SslHandler} is inserted to the {@link ChannelPipeline}, causing
 * data corruption.
 * <p>
 * The client-side implementation is much simpler.
 * <ol>
 * <li>Write a StartTLS request,</li>
 * <li>wait for the StartTLS response,</li>
 * <li>create a new {@link SslHandler} instance with {@code startTls} flag set
 *     to {@code false},</li>
 * <li>insert the {@link SslHandler} to the {@link ChannelPipeline}, and</li>
 * <li>Initiate SSL handshake.</li>
 * </ol>
 *
 * <h3>Known issues</h3>
 * <p>
 * Because of a known issue with the current implementation of the SslEngine that comes
 * with Java it may be possible that you see blocked IO-Threads while a full GC is done.
 * <p>
 * So if you are affected you can workaround this problem by adjust the cache settings
 * like shown below:
 *
 * <pre>
 *     SslContext context = ...;
 *     context.getServerSessionContext().setSessionCacheSize(someSaneSize);
 *     context.getServerSessionContext().setSessionTime(someSameTimeout);
 * </pre>
 * <p>
 * What values to use here depends on the nature of your application and should be set
 * based on monitoring and debugging of it.
 * For more details see
 * <a href="https://github.com/netty/netty/issues/832">#832</a> in our issue tracker.
 */
// CLR: the TLS engine is System.Net.Security.SslStream (owner decision 2026-11-24, docs/handler-ssl-design.md): "An
// SslStream instance transmits data using a stream that you supply when creating the SslStream"
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0). The supplied stream is the
// private nested MediationStream, a CLR-only helper of this class (no Java counterpart, so it has no manifest entry of
// its own): decode(...) still cuts the input at SslUtils.getEncryptedPacketLength and hands one whole TLS record per
// unwrap to it, and SslStream's pending read of it completes inline on the event loop, so the handshake, the
// certificate callbacks and the reads run inside the call, as SSLEngine.unwrap does. The structure, fields, state flags,
// futures, timeouts and events of the Java class are kept; only the SSLEngine calls are replaced, by the private
// Engine* methods at the end of this class, which return an EngineResult with SSLEngineResult's Status/HandshakeStatus
// so that the wrap/unwrap loops keep their Java shape. Owner decision 2026-11-24 (no SSLEngine facade): the
// constructors take SslClientAuthenticationOptions/SslServerAuthenticationOptions (owned by the handler) instead of an
// SSLEngine, engine() is not public (the internal Engine() serves the ported tests), and the Executor
// (delegatedTaskExecutor) constructors and the ResumptionController constructor are not ported. SslEngineType's
// TCNATIVE/CONSCRYPT branches serve the library-not-ported OpenSSL/Conscrypt engines; its JDK branch (heap buffers,
// MERGE_CUMULATOR, one record per unwrap) is the behaviour kept here. ChannelFuture/ChannelPromise/Future/Promise are
// Task/TaskCompletionSource (docs/transport-porting.md, docs/handler-porting.md "Task promises"); listeners that touch
// this handler run on Common's EventExecutorTaskScheduler over ctx.Executor (at once on the event loop, else through
// Execute: DefaultPromise.notifyListeners), and ScheduledFuture is the CancellationTokenSource of
// ctx.Executor.ScheduleAsync, as AbstractSniHandler does. SSLException/SSLHandshakeException are AuthenticationException
// (ssl design decision 3); (long, TimeUnit) pairs are TimeSpan (docs/common-clr-design.md, "Monotonic time and duration
// conversion"); Java's checked "throws" clauses have no counterpart.
public class SslHandler : ByteToMessageDecoder, IChannelOutboundHandler
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(SslHandler));
    // CLR: Pattern.compile(..., CASE_INSENSITIVE) matched with matches() is a Regex of the pattern wrapped in
    // \A(?:...)\z whose letters are written as [xX] classes (CASE_INSENSITIVE alone matches US-ASCII only) and whose
    // "." excludes the Java line terminators (\n, \r, \u0085, \u2028, \u2029); the .NET "." only excludes \n
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Pattern.html,
    // https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-classes-in-regular-expressions), as
    // CipherSuiteConverter maps it.
    private static readonly Regex IGNORABLE_ERROR_MESSAGE = CompileJavaPattern(
            "^.*(?:connection.*(?:reset|closed|abort|broken)|broken.*pipe).*$");
    private const int STATE_SENT_FIRST_MESSAGE = 1;
    private const int STATE_FLUSHED_BEFORE_HANDSHAKE = 1 << 1;
    private const int STATE_READ_DURING_HANDSHAKE = 1 << 2;
    private const int STATE_HANDSHAKE_STARTED = 1 << 3;
    /**
     * Set by wrap*() methods when something is produced.
     * {@link #channelReadComplete(ChannelHandlerContext)} will check this flag, clear it, and call ctx.flush().
     */
    private const int STATE_NEEDS_FLUSH = 1 << 4;
    private const int STATE_OUTBOUND_CLOSED = 1 << 5;
    private const int STATE_CLOSE_NOTIFY = 1 << 6;
    private const int STATE_PROCESS_TASK = 1 << 7;
    /**
     * This flag is used to determine if we need to call {@link ChannelHandlerContext#read()} to consume more data
     * when {@link ChannelConfig#isAutoRead()} is {@code false}.
     */
    private const int STATE_FIRE_CHANNEL_READ = 1 << 8;
    private const int STATE_UNWRAP_REENTRY = 1 << 9;

    /**
     * <a href="https://tools.ietf.org/html/rfc5246#section-6.2">2^14</a> which is the maximum sized plaintext chunk
     * allowed by the TLS RFC.
     */
    private const int MAX_PLAINTEXT_LENGTH = 16 * 1024;

    // CLR: SSLSession.getPacketBufferSize(): the largest TLS record, a 5-byte header and a TLSCiphertext fragment whose
    // "length MUST NOT exceed 2^14 + 2048" (https://www.rfc-editor.org/rfc/rfc5246.txt, section 6.2.3). SslStream has no
    // session buffer sizes.
    private const int PACKET_BUFFER_SIZE = SslUtils.SSL_RECORD_HEADER_LENGTH + MAX_PLAINTEXT_LENGTH + 2048;

    // CLR: the SslEngineType enum chooses between the OpenSSL, Conscrypt and JDK SSLEngines. The first two are
    // library-not-ported (netty-tcnative, Conscrypt) and SslStream is the only engine, so the values of the JDK
    // constant are fields of this class: heap buffers, MERGE_CUMULATOR, and jdkCompatibilityMode true (decode(...)
    // always takes decodeJdkCompatible(...)); its unwrap/allocateWrapBuffer/calculateRequiredOutBufSpace are EngineUnwrap,
    // AllocateOutNetBuf and CalculateRequiredOutBufSpace.

    // BEGIN Platform-dependent flags

    /**
     * {@code true} if and only if {@link SSLEngine} expects a direct buffer and so if a heap buffer
     * is given will make an extra memory copy.
     */
    // CLR: SslStream reads and writes managed memory, as the JDK engine works on heap buffers (false).
    private static readonly bool wantsDirectBuffer = false;

    // END Platform-dependent flags

    /**
     * When using JDK {@link SSLEngine}, we use {@link #MERGE_CUMULATOR} because it works only with
     * one {@link ByteBuffer}.
     *
     * When using {@link OpenSslEngine}, we can use {@link #COMPOSITE_CUMULATOR} because it has
     * {@link OpenSslEngine#unwrap(ByteBuffer[], ByteBuffer[])} which works with multiple {@link ByteBuffer}s
     * and which does not need to do extra memory copies.
     */
    private static readonly ICumulator cumulator = MERGE_CUMULATOR;

    private volatile IChannelHandlerContext ctx;
    // CLR: the SSLEngine is the SslStream (see the class comment).
    private readonly SslStream engine;

    private readonly bool startTls;

    private readonly SslTasksRunner sslTaskRunnerForUnwrap;
    private readonly SslTasksRunner sslTaskRunner;

    private SslHandlerCoalescingBufferQueue pendingUnencryptedWrites;
    private TaskCompletionSource<IChannel> handshakePromise = NewChannelPromise();
    private readonly TaskCompletionSource<IChannel> sslClosePromise = NewChannelPromise();

    private int packetLength;
    // CLR: the Java short holds ten flags; an int keeps the bit operations free of casts.
    private int state;

    // CLR: C# has no volatile long; the volatile fields are read and written through Volatile.Read/Write
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile?view=net-10.0).
    private long handshakeTimeoutMillis = 10000;
    private long closeNotifyFlushTimeoutMillis = 3000;
    private long closeNotifyReadTimeoutMillis;
    internal volatile int wrapDataSize = MAX_PLAINTEXT_LENGTH;

    /**
     * Creates a new instance which runs all delegated tasks directly on the {@link EventExecutor}.
     *
     * @param engine  the {@link SSLEngine} this handler will use
     */
    // CLR: the SSLEngine argument is the client authentication options of the SslStream that this handler creates
    // (owner decision 2026-11-24); SslStream runs its certificate callbacks and cryptography inline, so there is no
    // delegated task.
    public SslHandler(SslClientAuthenticationOptions options) : this(options, false)
    {
    }

    /**
     * Creates a new instance which runs all delegated tasks directly on the {@link EventExecutor}.
     *
     * @param engine    the {@link SSLEngine} this handler will use
     * @param startTls  {@code true} if the first write request shouldn't be
     *                  encrypted by the {@link SSLEngine}
     */
    public SslHandler(SslClientAuthenticationOptions options, bool startTls)
        : this(ObjectUtil.CheckNotNull(options, "options"), null, startTls)
    {
    }

    // CLR: the server-mode counterparts of the two constructors above (SSLEngine.getUseClientMode() false).
    public SslHandler(SslServerAuthenticationOptions options) : this(options, false)
    {
    }

    public SslHandler(SslServerAuthenticationOptions options, bool startTls)
        : this(null, ObjectUtil.CheckNotNull(options, "options"), startTls)
    {
    }

    private SslHandler(SslClientAuthenticationOptions clientOptions, SslServerAuthenticationOptions serverOptions,
                       bool startTls)
    {
        this.clientOptions = clientOptions;
        this.serverOptions = serverOptions;
        mediation = new MediationStream(this);
        engine = new SslStream(mediation, false);
        this.startTls = startTls;
        SetCumulator(cumulator);
        sslTaskRunnerForUnwrap = new SslTasksRunner(this, true);
        sslTaskRunner = new SslTasksRunner(this, false);
        onEngineProgress = OnEngineProgress;
    }

    public long GetHandshakeTimeoutMillis()
    {
        return Volatile.Read(ref handshakeTimeoutMillis);
    }

    // CLR: the TimeSpan carries the unit, so the TimeUnit parameter and its checkNotNull have no counterpart.
    public void SetHandshakeTimeout(TimeSpan handshakeTimeout)
    {
        SetHandshakeTimeoutMillis(ToMillis(handshakeTimeout));
    }

    public void SetHandshakeTimeoutMillis(long handshakeTimeoutMillis)
    {
        Volatile.Write(ref this.handshakeTimeoutMillis,
                ObjectUtil.CheckPositiveOrZero(handshakeTimeoutMillis, "handshakeTimeoutMillis"));
    }

    /**
     * Sets the number of bytes to pass to each {@link SSLEngine#wrap(ByteBuffer[], int, int, ByteBuffer)} call.
     * <p>
     * This value will partition data which is passed to write
     * {@link #write(ChannelHandlerContext, Object, ChannelPromise)}. The partitioning will work as follows:
     * <ul>
     * <li>If {@code wrapDataSize <= 0} then we will write each data chunk as is.</li>
     * <li>If {@code wrapDataSize > data size} then we will attempt to aggregate multiple data chunks together.</li>
     * <li>If {@code wrapDataSize > data size}  Else if {@code wrapDataSize <= data size} then we will divide the data
     * into chunks of {@code wrapDataSize} when writing.</li>
     * </ul>
     * <p>
     * If the {@link SSLEngine} doesn't support a gather wrap operation (e.g. {@link SslProvider#OPENSSL}) then
     * aggregating data before wrapping can help reduce the ratio between TLS overhead vs data payload which will lead
     * to better goodput. Writing fixed chunks of data can also help target the underlying transport's (e.g. TCP)
     * frame size. Under lossy/congested network conditions this may help the peer get full TLS packets earlier and
     * be able to do work sooner, as opposed to waiting for the all the pieces of the TLS packet to arrive.
     * @param wrapDataSize the number of bytes which will be passed to each
     *      {@link SSLEngine#wrap(ByteBuffer[], int, int, ByteBuffer)} call.
     */
    [UnstableApi]
    public void SetWrapDataSize(int wrapDataSize)
    {
        this.wrapDataSize = wrapDataSize;
    }

    /**
     * Gets the timeout for flushing the close_notify that was triggered by closing the
     * {@link Channel}. If the close_notify was not flushed in the given timeout the {@link Channel} will be closed
     * forcibly.
     */
    public long GetCloseNotifyFlushTimeoutMillis()
    {
        return Volatile.Read(ref closeNotifyFlushTimeoutMillis);
    }

    /**
     * Sets the timeout for flushing the close_notify that was triggered by closing the
     * {@link Channel}. If the close_notify was not flushed in the given timeout the {@link Channel} will be closed
     * forcibly.
     */
    public void SetCloseNotifyFlushTimeout(TimeSpan closeNotifyFlushTimeout)
    {
        SetCloseNotifyFlushTimeoutMillis(ToMillis(closeNotifyFlushTimeout));
    }

    /**
     * See {@link #setCloseNotifyFlushTimeout(long, TimeUnit)}.
     */
    public void SetCloseNotifyFlushTimeoutMillis(long closeNotifyFlushTimeoutMillis)
    {
        Volatile.Write(ref this.closeNotifyFlushTimeoutMillis, ObjectUtil.CheckPositiveOrZero(closeNotifyFlushTimeoutMillis,
                "closeNotifyFlushTimeoutMillis"));
    }

    /**
     * Gets the timeout (in ms) for receiving the response for the close_notify that was triggered by closing the
     * {@link Channel}. This timeout starts after the close_notify message was successfully written to the
     * remote peer. Use {@code 0} to directly close the {@link Channel} and not wait for the response.
     */
    public long GetCloseNotifyReadTimeoutMillis()
    {
        return Volatile.Read(ref closeNotifyReadTimeoutMillis);
    }

    /**
     * Sets the timeout  for receiving the response for the close_notify that was triggered by closing the
     * {@link Channel}. This timeout starts after the close_notify message was successfully written to the
     * remote peer. Use {@code 0} to directly close the {@link Channel} and not wait for the response.
     */
    public void SetCloseNotifyReadTimeout(TimeSpan closeNotifyReadTimeout)
    {
        SetCloseNotifyReadTimeoutMillis(ToMillis(closeNotifyReadTimeout));
    }

    /**
     * See {@link #setCloseNotifyReadTimeout(long, TimeUnit)}.
     */
    public void SetCloseNotifyReadTimeoutMillis(long closeNotifyReadTimeoutMillis)
    {
        Volatile.Write(ref this.closeNotifyReadTimeoutMillis, ObjectUtil.CheckPositiveOrZero(closeNotifyReadTimeoutMillis,
                "closeNotifyReadTimeoutMillis"));
    }

    // CLR: engine() is not public (owner decision 2026-11-24): SslStream is the engine, and the session facts of
    // SSLSession are its SslProtocol, NegotiatedCipherSuite, RemoteCertificate and LocalCertificate. The internal
    // accessor serves the ported tests through InternalsVisibleTo; a public accessor waits for an owner naming decision.
    internal SslStream Engine()
    {
        return engine;
    }

    // CLR-only: the authentication options this handler's SslStream uses, which hold what Java reads from the engine's
    // configuration (engine().getEnabledProtocols(), getNeedClientAuth(), ...); exactly one of them is non-null. Internal
    // for the ported tests (DelegatingSslContextTest, SslContextBuilderTest), like Engine().
    internal SslClientAuthenticationOptions ClientOptions()
    {
        return clientOptions;
    }

    internal SslServerAuthenticationOptions ServerOptions()
    {
        return serverOptions;
    }

    /**
     * Returns the name of the current application-level protocol.
     *
     * @return the protocol name or {@code null} if application-level protocol has not been negotiated
     */
    // CLR: the ApplicationProtocolAccessor of the SSLEngine wrappers is SslStream.NegotiatedApplicationProtocol ("The
    // negotiated application protocol in TLS handshake",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0), read when the first
    // handshake completed and decoded as UTF-8, the encoding of the SslApplicationProtocol(string) constructor
    // (https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslApplicationProtocol.cs).
    // An empty protocol (no ALPN) is null, as in Java.
    public string ApplicationProtocol()
    {
        return negotiatedApplicationProtocol;
    }

    /**
     * Returns a {@link Future} that will get notified once the current TLS handshake completes.
     *
     * @return the {@link Future} for the initial TLS handshake if {@link #renegotiate()} was not invoked.
     *         The {@link Future} for the most recent {@linkplain #renegotiate() TLS renegotiation} otherwise.
     */
    public Task<IChannel> HandshakeFuture()
    {
        return handshakePromise.Task;
    }

    /**
     * Sends an SSL {@code close_notify} message to the specified channel and
     * destroys the underlying {@link SSLEngine}. This will <strong>not</strong> close the underlying
     * {@link Channel}. If you want to also close the {@link Channel} use {@link Channel#close()} or
     * {@link ChannelHandlerContext#close()}
     */
    // CLR: closeOutbound() and closeOutbound(ChannelPromise) fold into one Task-returning member, as the Transport
    // folds the ChannelPromise overloads (docs/transport-porting.md); the Task replaces ctx.newPromise().
    public Task CloseOutbound()
    {
        IChannelHandlerContext ctx = this.ctx;
        TaskCompletionSource promise = NewUserPromise();
        if (ctx.Executor.InEventLoop())
        {
            CloseOutbound0(promise);
        }
        else
        {
            ctx.Executor.Execute(() => CloseOutbound0(promise));
        }
        return promise.Task;
    }

    private void CloseOutbound0(TaskCompletionSource promise)
    {
        SetState(STATE_OUTBOUND_CLOSED);
        EngineCloseOutbound();
        try
        {
            Flush(ctx, promise);
        }
        catch (Exception e)
        {
            if (!promise.TrySetException(e))
            {
                logger.Warn("{} flush() raised a masked exception.", ctx.Channel, e);
            }
        }
    }

    /**
     * Return the {@link Future} that will get notified if the inbound of the {@link SSLEngine} is closed.
     *
     * This method will return the same {@link Future} all the time.
     *
     * @see SSLEngine
     */
    public Task<IChannel> SslCloseFuture()
    {
        return sslClosePromise.Task;
    }

    protected override void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        try
        {
            if (pendingUnencryptedWrites != null && !pendingUnencryptedWrites.IsEmpty)
            {
                // Check if queue is not empty first because create a new ChannelException is expensive
                pendingUnencryptedWrites.ReleaseAndFailAll(
                  new ChannelException("Pending write on removal of SslHandler"));
            }
            pendingUnencryptedWrites = null;

            AuthenticationException cause = null;

            // If the handshake or SSLEngine closure is not done yet we should fail corresponding promise and
            // notify the rest of the
            // pipeline.
            if (!handshakePromise.Task.IsCompleted)
            {
                cause = new AuthenticationException("SslHandler removed before handshake completed");
                if (handshakePromise.TrySetException(cause))
                {
                    ctx.FireUserEventTriggered(new SslHandshakeCompletionEvent(cause));
                }
            }
            if (!sslClosePromise.Task.IsCompleted)
            {
                if (cause == null)
                {
                    cause = new AuthenticationException("SslHandler removed before SSLEngine was closed");
                }
                NotifyClosePromise(cause);
            }
        }
        finally
        {
            // CLR: ReferenceCountUtil.release(engine) releases a reference-counted OpenSSL engine; the SslStream is
            // disposed here (with the mediation stream, so its pending operations end).
            EngineDispose();
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

    public virtual Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DeregisterAsync(cancellationToken);
    }

    public virtual Task DisconnectAsync(IChannelHandlerContext ctx,
                                        CancellationToken cancellationToken)
    {
        return CloseOutboundAndChannel(ctx, cancellationToken, true);
    }

    public virtual Task CloseAsync(IChannelHandlerContext ctx,
                                   CancellationToken cancellationToken)
    {
        return CloseOutboundAndChannel(ctx, cancellationToken, false);
    }

    public virtual void Read(IChannelHandlerContext ctx)
    {
        if (!handshakePromise.Task.IsCompleted)
        {
            SetState(STATE_READ_DURING_HANDSHAKE);
        }

        ctx.Read();
    }

    private static InvalidOperationException NewPendingWritesNullException()
    {
        return new InvalidOperationException("pendingUnencryptedWrites is null, handlerRemoved0 called?");
    }

    // CLR: the ChannelPromise is the returned Task: a TaskCompletionSource queued with the buffer, whose continuations
    // run asynchronously as the Transport's write Tasks do; the CancellationToken cancels the queued promise, as
    // ChannelPromise.cancel(...) does (docs/handler-porting.md, "Task promises").
    public virtual Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (!(msg is ByteBuf))
        {
            UnsupportedMessageTypeException exception = new UnsupportedMessageTypeException(msg, typeof(ByteBuf));
            ReferenceCountUtil.SafeRelease(msg);
            return Task.FromException(exception);
        }
        else if (pendingUnencryptedWrites == null)
        {
            ReferenceCountUtil.SafeRelease(msg);
            return Task.FromException(NewPendingWritesNullException());
        }
        else
        {
            TaskCompletionSource promise = NewUserPromise();
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
                        static (s, token) => ((TaskCompletionSource) s).TrySetCanceled(token), promise);
                _ = promise.Task.ContinueWith(static (_, r) => ((CancellationTokenRegistration) r).Unregister(),
                        registration, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
            }
            pendingUnencryptedWrites.Add((ByteBuf) msg, promise);
            return promise.Task;
        }
    }

    public virtual void Flush(IChannelHandlerContext ctx)
    {
        // Do not encrypt the first write request if this handler is
        // created with startTLS flag turned on.
        if (startTls && !IsStateSet(STATE_SENT_FIRST_MESSAGE))
        {
            SetState(STATE_SENT_FIRST_MESSAGE);
            _ = pendingUnencryptedWrites.WriteAndRemoveAllAsync(ctx);
            ForceFlush(ctx);
            // Explicit start handshake processing once we send the first message. This will also ensure
            // we will schedule the timeout if needed.
            StartHandshakeProcessing(true);
            return;
        }

        if (IsStateSet(STATE_PROCESS_TASK))
        {
            return;
        }

        try
        {
            WrapAndFlush(ctx);
        }
        catch (Exception cause)
        {
            SetHandshakeFailure(ctx, cause);
            // CLR: PlatformDependent.throwException(cause) rethrows the cause unchanged, which is throw;.
            throw;
        }
    }

    private void WrapAndFlush(IChannelHandlerContext ctx)
    {
        if (pendingUnencryptedWrites.IsEmpty)
        {
            // It's important to NOT use a voidPromise here as the user
            // may want to add a ChannelFutureListener to the ChannelPromise later.
            //
            // See https://github.com/netty/netty/issues/3364
            pendingUnencryptedWrites.Add(Unpooled.EmptyBuffer, NewPromise());
        }
        if (!handshakePromise.Task.IsCompleted)
        {
            SetState(STATE_FLUSHED_BEFORE_HANDSHAKE);
        }
        try
        {
            Wrap(ctx, false);
        }
        finally
        {
            // We may have written some parts of data before an exception was thrown so ensure we always flush.
            // See https://github.com/netty/netty/issues/3900#issuecomment-172481830
            ForceFlush(ctx);
        }
    }

    // This method will not call setHandshakeFailure(...) !
    private void Wrap(IChannelHandlerContext ctx, bool inUnwrap)
    {
        ByteBuf @out = null;
        IByteBufAllocator alloc = ctx.Alloc();
        try
        {
            int wrapDataSize = this.wrapDataSize;
            // Only continue to loop if the handler was not removed in the meantime.
            // See https://github.com/netty/netty/issues/5860
            while (!ctx.IsRemoved && !pendingUnencryptedWrites.IsEmpty)
            {
                TaskCompletionSource promise = NewPromise();
                ByteBuf buf = wrapDataSize > 0 ?
                        pendingUnencryptedWrites.Remove(alloc, wrapDataSize, promise) :
                        pendingUnencryptedWrites.RemoveFirst(promise);
                if (buf == null)
                {
                    break;
                }

                EngineResult result;

                try
                {
                    if (buf.ReadableBytes > MAX_PLAINTEXT_LENGTH)
                    {
                        // If we pulled a buffer larger than the supported packet size, we can slice it up and
                        // iteratively, encrypting multiple packets into a single larger buffer. This substantially
                        // saves on allocations for large responses. Here we estimate how large of a buffer we need.
                        // If we overestimate a bit, that's fine. If we underestimate, we'll simply re-enqueue the
                        // remaining buffer and get it on the next outer loop.
                        int readableBytes = buf.ReadableBytes;
                        int numPackets = readableBytes / MAX_PLAINTEXT_LENGTH;
                        if (readableBytes % MAX_PLAINTEXT_LENGTH != 0)
                        {
                            numPackets += 1;
                        }

                        if (@out == null)
                        {
                            @out = AllocateOutNetBuf(ctx, readableBytes, numPackets);
                        }
                        result = WrapMultiple(alloc, engine, buf, @out);
                    }
                    else
                    {
                        if (@out == null)
                        {
                            @out = AllocateOutNetBuf(ctx, buf.ReadableBytes, 1);
                        }
                        result = Wrap(alloc, engine, buf, @out);
                    }
                }
                catch (Exception e)
                {
                    // Either wrapMultiple(...), wrap(...) or allocateOutNetBuf(...) did throw.
                    // In this case we need to release the buffer that we removed from pendingUnencryptedWrites
                    // before failing the promise and rethrowing it. Failing to do so would result in a buffer leak.
                    // See https://github.com/netty/netty/issues/14644
                    //
                    // We don't need to release out here as this is done in a finally block already.
                    buf.Release();
                    promise.TrySetException(e);
                    throw;
                }

                if (buf.IsReadable())
                {
                    pendingUnencryptedWrites.AddFirst(buf, promise);
                    // When we add the buffer/promise pair back we need to be sure we don't complete the promise
                    // later. We only complete the promise if the buffer is completely consumed.
                    promise = null;
                }
                else
                {
                    buf.Release();
                }

                // We need to write any data before we invoke any methods which may trigger re-entry, otherwise
                // writes may occur out of order and TLS sequencing may be off (e.g. SSLV3_ALERT_BAD_RECORD_MAC).
                if (@out.IsReadable())
                {
                    ByteBuf b = @out;
                    @out = null;
                    if (promise != null)
                    {
                        Cascade(ctx.WriteAsync(b), promise);
                    }
                    else
                    {
                        _ = ctx.WriteAsync(b);
                    }
                }
                else if (promise != null)
                {
                    Cascade(ctx.WriteAsync(Unpooled.EmptyBuffer), promise);
                }
                // else out is not readable we can re-use it and so save an extra allocation

                if (result.GetStatus() == Status.CLOSED)
                {
                    // First check if there is any write left that needs to be failed, if there is none we don't need
                    // to create a new exception or obtain an existing one.
                    if (!pendingUnencryptedWrites.IsEmpty)
                    {
                        // Make a best effort to preserve any exception that way previously encountered from the
                        // handshake or the transport, else fallback to a general error.
                        Exception exception = CauseOf(handshakePromise.Task);
                        if (exception == null)
                        {
                            exception = CauseOf(sslClosePromise.Task);
                            if (exception == null)
                            {
                                exception = new SslClosedEngineException("SSLEngine closed already");
                            }
                        }
                        pendingUnencryptedWrites.ReleaseAndFailAll(exception);
                    }

                    return;
                }
                else
                {
                    switch (result.GetHandshakeStatus())
                    {
                        case HandshakeStatus.NEED_TASK:
                            if (!RunDelegatedTasks(inUnwrap))
                            {
                                // We scheduled a task on the delegatingTaskExecutor, so stop processing as we will
                                // resume once the task completes.
                                goto outer_break;
                            }
                            break;
                        case HandshakeStatus.FINISHED:
                        case HandshakeStatus.NOT_HANDSHAKING: // work around for android bug that skips the FINISHED state.
                            SetHandshakeSuccess();
                            break;
                        case HandshakeStatus.NEED_WRAP:
                            // If we are expected to wrap again and we produced some data we need to ensure there
                            // is something in the queue to process as otherwise we will not try again before there
                            // was more added. Failing to do so may fail to produce an alert that can be
                            // consumed by the remote peer.
                            if (result.BytesProduced() > 0 && pendingUnencryptedWrites.IsEmpty)
                            {
                                pendingUnencryptedWrites.Add(Unpooled.EmptyBuffer);
                            }
                            break;
                        case HandshakeStatus.NEED_UNWRAP:
                            // The underlying engine is starving so we need to feed it with more data.
                            // See https://github.com/netty/netty/pull/5039
                            ReadIfNeeded(ctx);
                            return;
                        default:
                            throw new InvalidOperationException(
                                    "Unknown handshake status: " + result.GetHandshakeStatus());
                    }
                }
            }
            outer_break: ;
        }
        finally
        {
            if (@out != null)
            {
                @out.Release();
            }
            if (inUnwrap)
            {
                SetState(STATE_NEEDS_FLUSH);
            }
        }
    }

    /**
     * This method will not call
     * {@link #setHandshakeFailure(ChannelHandlerContext, Throwable, boolean, boolean, boolean)} or
     * {@link #setHandshakeFailure(ChannelHandlerContext, Throwable)}.
     * @return {@code true} if this method ends on {@link SSLEngineResult.HandshakeStatus#NOT_HANDSHAKING}.
     */
    private bool WrapNonAppData(IChannelHandlerContext ctx, bool inUnwrap)
    {
        ByteBuf @out = null;
        IByteBufAllocator alloc = ctx.Alloc();
        try
        {
            // Only continue to loop if the handler was not removed in the meantime.
            // See https://github.com/netty/netty/issues/5860
            while (!ctx.IsRemoved)
            {
                if (@out == null)
                {
                    // As this is called for the handshake we have no real idea how big the buffer needs to be.
                    // That said 2048 should give us enough room to include everything like ALPN / NPN data.
                    // If this is not enough we will increase the buffer in wrap(...).
                    @out = AllocateOutNetBuf(ctx, 2048, 1);
                }
                EngineResult result = Wrap(alloc, engine, Unpooled.EmptyBuffer, @out);
                if (result.BytesProduced() > 0)
                {
                    AddListener(ctx.WriteAsync(@out), future =>
                    {
                        Exception cause = CauseOf(future);
                        if (cause != null)
                        {
                            SetHandshakeFailureTransportFailure(ctx, cause);
                        }
                    });
                    if (inUnwrap)
                    {
                        SetState(STATE_NEEDS_FLUSH);
                    }
                    @out = null;
                }

                HandshakeStatus status = result.GetHandshakeStatus();
                switch (status)
                {
                    case HandshakeStatus.FINISHED:
                        // We may be here because we read data and discovered the remote peer initiated a renegotiation
                        // and this write is to complete the new handshake. The user may have previously done a
                        // writeAndFlush which wasn't able to wrap data due to needing the pending handshake, so we
                        // attempt to wrap application data here if any is pending.
                        if (SetHandshakeSuccess() && inUnwrap && !pendingUnencryptedWrites.IsEmpty)
                        {
                            Wrap(ctx, true);
                        }
                        return false;
                    case HandshakeStatus.NEED_TASK:
                        if (!RunDelegatedTasks(inUnwrap))
                        {
                            // We scheduled a task on the delegatingTaskExecutor, so stop processing as we will
                            // resume once the task completes.
                            goto outer_break;
                        }
                        break;
                    case HandshakeStatus.NEED_UNWRAP:
                        if (inUnwrap || UnwrapNonAppData(ctx) <= 0)
                        {
                            // If we asked for a wrap, the engine requested an unwrap, and we are in unwrap there is
                            // no use in trying to call wrap again because we have already attempted (or will after we
                            // return) to feed more data to the engine.
                            return false;
                        }
                        break;
                    case HandshakeStatus.NEED_WRAP:
                        break;
                    case HandshakeStatus.NOT_HANDSHAKING:
                        if (SetHandshakeSuccess() && inUnwrap && !pendingUnencryptedWrites.IsEmpty)
                        {
                            Wrap(ctx, true);
                        }
                        // Workaround for TLS False Start problem reported at:
                        // https://github.com/netty/netty/issues/1108#issuecomment-14266970
                        if (!inUnwrap)
                        {
                            UnwrapNonAppData(ctx);
                        }
                        return true;
                    default:
                        throw new InvalidOperationException("Unknown handshake status: " + result.GetHandshakeStatus());
                }

                // Check if did not produce any bytes and if so break out of the loop, but only if we did not process
                // a task as last action. It's fine to not produce any data as part of executing a task.
                if (result.BytesProduced() == 0 && status != HandshakeStatus.NEED_TASK)
                {
                    break;
                }
            }
            outer_break: ;
        }  finally
        {
            if (@out != null)
            {
                @out.Release();
            }
        }
        return false;
    }

    private EngineResult WrapMultiple(IByteBufAllocator alloc, SslStream engine, ByteBuf @in, ByteBuf @out)
    {
        EngineResult result = default;
        bool wrapped = false;

        do
        {
            int nextSliceSize = Math.Min(MAX_PLAINTEXT_LENGTH, @in.ReadableBytes);
            // This call over-estimates, because we are slicing and not every nioBuffer will be part of
            // every slice. We could improve the estimate by having an nioBufferCount(offset, length).
            // CLR: SslStream needs no NIO buffer count; the estimate is the record size of CalculateRequiredOutBufSpace.
            int nextOutSize = CalculateRequiredOutBufSpace(nextSliceSize, 1);

            if (!@out.IsWritable(nextOutSize))
            {
                // CLR: result is a struct, so the null check of the source is the wrapped flag.
                if (wrapped)
                {
                    // We underestimated the space needed to encrypt the entire in buf. Break out, and
                    // upstream will re-enqueue the buffer for later.
                    break;
                }
                // This shouldn't happen, as the out buf was properly sized for at least packetLength
                // prior to calling wrap.
                @out.EnsureWritable(nextOutSize);
            }

            ByteBuf wrapBuf = @in.ReadSlice(nextSliceSize);
            result = Wrap(alloc, engine, wrapBuf, @out);
            wrapped = true;

            if (result.GetStatus() == Status.CLOSED)
            {
                // If the engine gets closed, we can exit out early. Otherwise, we'll do a full handling of
                // possible results once finished.
                break;
            }

            if (wrapBuf.IsReadable())
            {
                // There may be some left-over, in which case we can just pick it up next loop, so reset the original
                // reader index so its included again in the next slice.
                @in.ReaderIndex = @in.ReaderIndex - wrapBuf.ReadableBytes;
            }
        } while (@in.ReadableBytes > 0);

        return result;
    }

    // CLR: SSLEngine.wrap(ByteBuffer[], ByteBuffer) is EngineWrap, which hands the readable bytes to SslStream and
    // appends the records it produces to out (WriteBytes moves the writer index, so the source's
    // out.writerIndex(...) update has no counterpart). The NIO ByteBuffer selection (singleBuffer, nioBuffers(), the
    // direct-buffer copy) has no counterpart: SslStream takes ReadOnlyMemory.
    private EngineResult Wrap(IByteBufAllocator alloc, SslStream engine, ByteBuf @in, ByteBuf @out)
    {
        for (;;)
        {
            EngineResult result = EngineWrap(@in, @out);
            @in.SkipBytes(result.BytesConsumed());

            if (result.GetStatus() == Status.BUFFER_OVERFLOW)
            {
                @out.EnsureWritable(PACKET_BUFFER_SIZE);
            }
            else
            {
                return result;
            }
        }
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        bool handshakeFailed = CauseOf(handshakePromise.Task) != null;

        // Channel closed, we will generate 'ClosedChannelException' now.
        StacklessClosedChannelException exception = StacklessClosedChannelException.NewInstance(
                typeof(SslHandler), "channelInactive(ChannelHandlerContext)");

        // Add a suppressed exception if the handshake was not completed yet.
        if (IsStateSet(STATE_HANDSHAKE_STARTED) && !handshakePromise.Task.IsCompleted)
        {
            ThrowableUtil.AddSuppressed(exception, StacklessSSLHandshakeException.NewInstance(
                    "Connection closed while SSL/TLS handshake was in progress",
                    typeof(SslHandler), "channelInactive"));
        }

        // Make sure to release SSLEngine,
        // and notify the handshake future if the connection has been closed during handshake.
        SetHandshakeFailure(ctx, exception, !IsStateSet(STATE_OUTBOUND_CLOSED), IsStateSet(STATE_HANDSHAKE_STARTED),
                false);

        // Ensure we always notify the sslClosePromise as well
        NotifyClosePromise(exception);

        try
        {
            base.ChannelInactive(ctx);
        }
        catch (DecoderException e)
        {
            if (!handshakeFailed || !(e.InnerException is AuthenticationException))
            {
                // We only rethrow the exception if the handshake did not fail before channelInactive(...) was called
                // as otherwise this may produce duplicated failures as super.channelInactive(...) will also call
                // channelRead(...).
                //
                // See https://github.com/netty/netty/issues/10119
                throw;
            }
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (IgnoreException(cause))
        {
            // It is safe to ignore the 'connection reset by peer' or
            // 'broken pipe' error after sending close_notify.
            if (logger.IsDebugEnabled())
            {
                logger.Debug(
                        "{} Swallowing a harmless 'connection reset by peer / broken pipe' error that occurred " +
                        "while writing close_notify in response to the peer's close_notify", ctx.Channel, cause);
            }

            // Close the connection explicitly just in case the transport
            // did not close the connection automatically.
            if (ctx.Channel.IsActive)
            {
                _ = ctx.CloseAsync();
            }
        }
        else
        {
            ctx.FireExceptionCaught(cause);
        }
    }

    /**
     * Checks if the given {@link Throwable} can be ignore and just "swallowed"
     *
     * When an ssl connection is closed a close_notify message is sent.
     * After that the peer also sends close_notify however, it's not mandatory to receive
     * the close_notify. The party who sent the initial close_notify can close the connection immediately
     * then the peer will get connection reset error.
     *
     */
    // CLR: SSLException is AuthenticationException, and the Transport reports socket errors as SocketException, which is
    // not an IOException (src/Netty.NET.Transport/Channels/Nio/AbstractNioByteChannel.cs closes on both), so both count
    // as IOException. The StackTraceElement walk that looks for a JDK SocketChannel/DatagramChannel/SctpChannel read
    // frame (with class loading) has no CLR counterpart; a SocketException carries its cause as SocketErrorCode instead,
    // and ConnectionReset ("The connection was reset by the remote peer.") and ConnectionAborted ("The connection was
    // aborted by .NET or the underlying socket provider.") are the reset/abort cases
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror?view=net-10.0).
    private bool IgnoreException(Exception t)
    {
        if (!(t is AuthenticationException) && (t is IOException || t is SocketException) &&
                sslClosePromise.Task.IsCompleted)
        {
            string message = t.Message;

            // first try to match connection reset / broke peer based on the regex. This is the fastest way
            // but may fail on different jdk impls or OS's
            if (message != null && IGNORABLE_ERROR_MESSAGE.IsMatch(message))
            {
                return true;
            }

            if (t is SocketException socketException &&
                    (socketException.SocketErrorCode == SocketError.ConnectionReset ||
                     socketException.SocketErrorCode == SocketError.ConnectionAborted))
            {
                return true;
            }
        }

        return false;
    }

    /**
     * Returns {@code true} if the given {@link ByteBuf} is encrypted. Be aware that this method
     * will not increase the readerIndex of the given {@link ByteBuf}.
     *
     * @param   buffer
     *                  The {@link ByteBuf} to read from. Be aware that it must have at least 5 bytes to read,
     *                  otherwise it will throw an {@link IllegalArgumentException}.
     * @return encrypted
     *                  {@code true} if the {@link ByteBuf} is encrypted, {@code false} otherwise.
     * @param probeSSLv2
     *                  {@code true} if the input {@code buffer} might be SSLv2. If {@code true} is used this
     *                  methods might produce false-positives in some cases so it's strongly suggested to
     *                  use {@code false}.
     * @throws IllegalArgumentException
     *                  Is thrown if the given {@link ByteBuf} has not at least 5 bytes to read.
     */
    public static bool IsEncrypted(ByteBuf buffer, bool probeSSLv2)
    {
        if (buffer.ReadableBytes < SslUtils.SSL_RECORD_HEADER_LENGTH)
        {
            throw new ArgumentException(
                    "buffer must have at least " + SslUtils.SSL_RECORD_HEADER_LENGTH + " readable bytes");
        }
        return SslUtils.GetEncryptedPacketLength(buffer, buffer.ReaderIndex, probeSSLv2) != SslUtils.NOT_ENCRYPTED;
    }

    private void DecodeJdkCompatible(IChannelHandlerContext ctx, ByteBuf @in)
    {
        int packetLength = this.packetLength;
        // If we calculated the length of the current SSL record before, use that information.
        if (packetLength > 0)
        {
            if (@in.ReadableBytes < packetLength)
            {
                return;
            }
        }
        else
        {
            // Get the packet length and wait until we get a packets worth of data to unwrap.
            int readableBytes = @in.ReadableBytes;
            if (readableBytes < SslUtils.SSL_RECORD_HEADER_LENGTH)
            {
                return;
            }
            packetLength = SslUtils.GetEncryptedPacketLength(@in, @in.ReaderIndex, true);
            if (packetLength == SslUtils.NOT_ENCRYPTED)
            {
                // Not an SSL/TLS packet
                NotSslRecordException e = new NotSslRecordException(
                        "not an SSL/TLS record: " + ByteBufUtil.HexDump(@in));
                @in.SkipBytes(@in.ReadableBytes);

                // First fail the handshake promise as we may need to have access to the SSLEngine which may
                // be released because the user will remove the SslHandler in an exceptionCaught(...) implementation.
                SetHandshakeFailure(ctx, e);

                throw e;
            }
            if (packetLength == SslUtils.NOT_ENOUGH_DATA)
            {
                return;
            }
            Debug.Assert(packetLength > 0);
            if (packetLength > readableBytes)
            {
                // wait until the whole packet can be read
                this.packetLength = packetLength;
                return;
            }
        }

        // Reset the state of this class so we can get the length of the next packet. We assume the entire packet will
        // be consumed by the SSLEngine.
        this.packetLength = 0;
        try
        {
            int bytesConsumed = Unwrap(ctx, @in, packetLength);
            if (bytesConsumed != packetLength && !EngineIsInboundDone())
            {
                // The JDK equivalent of getEncryptedPacketLength has some optimizations and can behave slightly
                // differently to ours, but this should always be a sign of bad input data.
                throw new NotSslRecordException();
            }
        }
        catch (Exception cause)
        {
            HandleUnwrapThrowable(ctx, cause);
        }
    }

    private void HandleUnwrapThrowable(IChannelHandlerContext ctx, Exception cause)
    {
        try
        {
            // We should attempt to notify the handshake failure before writing any pending data. If we are in unwrap
            // and failed during the handshake process, and we attempt to wrap, then promises will fail, and if
            // listeners immediately close the Channel then we may end up firing the handshake event after the Channel
            // has been closed.
            if (handshakePromise.TrySetException(cause))
            {
                ctx.FireUserEventTriggered(new SslHandshakeCompletionEvent(cause));
            }

            // Let's check if the handler was removed in the meantime and so pendingUnencryptedWrites is null.
            if (pendingUnencryptedWrites != null)
            {
                // We need to flush one time as there may be an alert that we should send to the remote peer because
                // of the SSLException reported here.
                WrapAndFlush(ctx);
            }
        }
        catch (AuthenticationException ex)
        {
            logger.Debug("SSLException during trying to call SSLEngine.wrap(...)" +
                    " because of an previous SSLException, ignoring...", ex);
        }
        finally
        {
            // ensure we always flush and close the channel.
            SetHandshakeFailure(ctx, cause, true, false, true);
        }
        // CLR: ExceptionDispatchInfo.Throw rethrows the original exception unchanged as PlatformDependent.throwException
        // does (https://learn.microsoft.com/dotnet/api/system.runtime.exceptionservices.exceptiondispatchinfo.throw), as
        // Transport ChannelHandlerMask does.
        ExceptionDispatchInfo.Throw(cause);
    }

    // CLR: jdkCompatibilityMode is always true (see the SslEngineType comment), so decodeNonJdkCompatible(...), which
    // only serves OpenSSL engines outside that mode, has no counterpart.
    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (IsStateSet(STATE_PROCESS_TASK))
        {
            return;
        }
        DecodeJdkCompatible(ctx, @in);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ChannelReadComplete0(ctx);
    }

    private void ChannelReadComplete0(IChannelHandlerContext ctx)
    {
        // Discard bytes of the cumulation buffer if needed.
        DiscardSomeReadBytes();

        FlushIfNeeded(ctx);
        ReadIfNeeded(ctx);

        ClearState(STATE_FIRE_CHANNEL_READ);
        ctx.FireChannelReadComplete();
    }

    private void ReadIfNeeded(IChannelHandlerContext ctx)
    {
        // If handshake is not finished yet, we need more data.
        if (!ctx.Channel.Config.IsAutoRead() &&
                (!IsStateSet(STATE_FIRE_CHANNEL_READ) || !handshakePromise.Task.IsCompleted))
        {
            // No auto-read used and no message passed through the ChannelPipeline or the handshake was not complete
            // yet, which means we need to trigger the read to ensure we not encounter any stalls.
            ctx.Read();
        }
    }

    private void FlushIfNeeded(IChannelHandlerContext ctx)
    {
        if (IsStateSet(STATE_NEEDS_FLUSH))
        {
            ForceFlush(ctx);
        }
    }

    /**
     * Calls {@link SSLEngine#unwrap(ByteBuffer, ByteBuffer)} with an empty buffer to handle handshakes, etc.
     */
    private int UnwrapNonAppData(IChannelHandlerContext ctx)
    {
        return Unwrap(ctx, Unpooled.EmptyBuffer, 0);
    }

    /**
     * Unwraps inbound SSL records.
     */
    private int Unwrap(IChannelHandlerContext ctx, ByteBuf packet, int length)
    {
        int originalLength = length;
        bool wrapLater = false;
        bool notifyClosure = false;
        bool executedRead = false;
        ByteBuf decodeOut = Allocate(ctx, length);
        try
        {
            // Only continue to loop if the handler was not removed in the meantime.
            // See https://github.com/netty/netty/issues/5860
            do
            {
                EngineResult result = EngineUnwrap(packet, length, decodeOut);
                Status status = result.GetStatus();
                HandshakeStatus handshakeStatus = result.GetHandshakeStatus();
                int produced = result.BytesProduced();
                int consumed = result.BytesConsumed();

                // Skip bytes now in case unwrap is called in a re-entry scenario. For example LocalChannel.read()
                // may entry this method in a re-entry fashion and if the peer is writing into a shared buffer we may
                // unwrap the same data multiple times.
                packet.SkipBytes(consumed);
                length -= consumed;

                // The expected sequence of events is:
                // 1. Notify of handshake success
                // 2. fireChannelRead for unwrapped data
                if (handshakeStatus == HandshakeStatus.FINISHED || handshakeStatus == HandshakeStatus.NOT_HANDSHAKING)
                {
                    wrapLater |= (decodeOut.IsReadable() ?
                            SetHandshakeSuccessUnwrapMarkReentry() : SetHandshakeSuccess()) ||
                            handshakeStatus == HandshakeStatus.FINISHED ||
                            // We need to check if pendingUnecryptedWrites is null as the SslHandler
                            // might have been removed in the meantime.
                            (pendingUnencryptedWrites != null  && !pendingUnencryptedWrites.IsEmpty);
                }

                // Dispatch decoded data after we have notified of handshake success. If this method has been invoked
                // in a re-entry fashion we execute a task on the executor queue to process after the stack unwinds
                // to preserve order of events.
                if (decodeOut.IsReadable())
                {
                    SetState(STATE_FIRE_CHANNEL_READ);
                    if (IsStateSet(STATE_UNWRAP_REENTRY))
                    {
                        executedRead = true;
                        ExecuteChannelRead(ctx, decodeOut);
                    }
                    else
                    {
                        ctx.FireChannelRead(decodeOut);
                    }
                    decodeOut = null;
                }

                if (status == Status.CLOSED)
                {
                    notifyClosure = true; // notify about the CLOSED state of the SSLEngine. See #137
                }
                else if (status == Status.BUFFER_OVERFLOW)
                {
                    if (decodeOut != null)
                    {
                        decodeOut.Release();
                    }
                    // CLR: SSLSession.getApplicationBufferSize() is the largest plaintext of a record; EngineUnwrap
                    // appends with WriteBytes and never reports BUFFER_OVERFLOW, so this branch only keeps the
                    // structure.
                    int applicationBufferSize = MAX_PLAINTEXT_LENGTH;
                    // Allocate a new buffer which can hold all the rest data and loop again.
                    // It may happen that applicationBufferSize < produced while there is still more to unwrap, in this
                    // case we will just allocate a new buffer with the capacity of applicationBufferSize and call
                    // unwrap again.
                    decodeOut = Allocate(ctx, applicationBufferSize < produced ?
                            applicationBufferSize : applicationBufferSize - produced);
                    continue;
                }

                if (handshakeStatus == HandshakeStatus.NEED_TASK)
                {
                    bool pending = RunDelegatedTasks(true);
                    if (!pending)
                    {
                        // We scheduled a task on the delegatingTaskExecutor, so stop processing as we will
                        // resume once the task completes.
                        //
                        // We break out of the loop only and do NOT return here as we still may need to notify
                        // about the closure of the SSLEngine.
                        wrapLater = false;
                        break;
                    }
                }
                else if (handshakeStatus == HandshakeStatus.NEED_WRAP)
                {
                    // If the wrap operation transitions the status to NOT_HANDSHAKING and there is no more data to
                    // unwrap then the next call to unwrap will not produce any data. We can avoid the potentially
                    // costly unwrap operation and break out of the loop.
                    if (WrapNonAppData(ctx, true) && length == 0)
                    {
                        break;
                    }
                }

                if (status == Status.BUFFER_UNDERFLOW ||
                        // If we processed NEED_TASK we should try again even we did not consume or produce anything.
                        handshakeStatus != HandshakeStatus.NEED_TASK && (consumed == 0 && produced == 0 ||
                                (length == 0 && handshakeStatus == HandshakeStatus.NOT_HANDSHAKING)))
                {
                    if (handshakeStatus == HandshakeStatus.NEED_UNWRAP)
                    {
                        // The underlying engine is starving so we need to feed it with more data.
                        // See https://github.com/netty/netty/pull/5039
                        ReadIfNeeded(ctx);
                    }

                    break;
                }
                else if (decodeOut == null)
                {
                    decodeOut = Allocate(ctx, length);
                }
            } while (!ctx.IsRemoved);

            if (IsStateSet(STATE_FLUSHED_BEFORE_HANDSHAKE) && handshakePromise.Task.IsCompleted)
            {
                // We need to call wrap(...) in case there was a flush done before the handshake completed to ensure
                // we do not stale.
                //
                // See https://github.com/netty/netty/pull/2437
                ClearState(STATE_FLUSHED_BEFORE_HANDSHAKE);
                wrapLater = true;
            }

            if (wrapLater)
            {
                Wrap(ctx, true);
            }
        }
        finally
        {
            if (decodeOut != null)
            {
                decodeOut.Release();
            }

            if (notifyClosure)
            {
                if (executedRead)
                {
                    ExecuteNotifyClosePromise(ctx);
                }
                else
                {
                    NotifyClosePromise(null);
                }
            }
        }
        return originalLength - length;
    }

    private bool SetHandshakeSuccessUnwrapMarkReentry()
    {
        // setHandshakeSuccess calls out to external methods which may trigger re-entry. We need to preserve ordering of
        // fireChannelRead for decodeOut relative to re-entry data.
        bool setReentryState = !IsStateSet(STATE_UNWRAP_REENTRY);
        if (setReentryState)
        {
            SetState(STATE_UNWRAP_REENTRY);
        }
        try
        {
            return SetHandshakeSuccess();
        }
        finally
        {
            // It is unlikely this specific method will be re-entry because handshake completion is infrequent, but just
            // in case we only clear the state if we set it in the first place.
            if (setReentryState)
            {
                ClearState(STATE_UNWRAP_REENTRY);
            }
        }
    }

    private void ExecuteNotifyClosePromise(IChannelHandlerContext ctx)
    {
        try
        {
            ctx.Executor.Execute(() => NotifyClosePromise(null));
        }
        catch (RejectedExecutionException e)
        {
            NotifyClosePromise(e);
        }
    }

    private void ExecuteChannelRead(IChannelHandlerContext ctx, ByteBuf decodedOut)
    {
        try
        {
            ctx.Executor.Execute(() => ctx.FireChannelRead(decodedOut));
        }
        catch (RejectedExecutionException)
        {
            decodedOut.Release();
            throw;
        }
    }

    // CLR: SSLEngine.getDelegatedTask() has no SslStream counterpart: SslStream runs its certificate callbacks and
    // cryptography inline. NEED_TASK means that SslStream went on asynchronously (it neither waits for input nor has
    // output), so the "task" is SslStream's own continuation: as with an offloaded delegated task, processing stops
    // here and the SslTasksRunner resumes on the event loop once SslStream makes progress.
    private bool RunDelegatedTasks(bool inUnwrap)
    {
        ExecuteDelegatedTask(inUnwrap);
        return false;
    }

    private SslTasksRunner GetTaskRunner(bool inUnwrap)
    {
        return inUnwrap ? sslTaskRunnerForUnwrap : sslTaskRunner;
    }

    private void ExecuteDelegatedTask(bool inUnwrap)
    {
        ExecuteDelegatedTask(GetTaskRunner(inUnwrap));
    }

    // CLR: delegatedTaskExecutor.execute(task) is the registration of the runner that OnEngineProgress hands SslStream's
    // next progress to; SslStream may already have progressed between the status check and the registration, so the
    // status is checked again and the runner runs at once if it changed.
    private void ExecuteDelegatedTask(SslTasksRunner task)
    {
        SetState(STATE_PROCESS_TASK);
        try
        {
            Volatile.Write(ref waitingTaskRunner, task);
            if (EngineGetHandshakeStatus() != HandshakeStatus.NEED_TASK &&
                    Interlocked.CompareExchange(ref waitingTaskRunner, null, task) == task)
            {
                task.Run();
            }
        }
        catch (RejectedExecutionException)
        {
            ClearState(STATE_PROCESS_TASK);
            throw;
        }
    }

    /**
     * {@link Runnable} that will be scheduled on the {@code delegatedTaskExecutor} and will take care
     * of resume work on the {@link EventExecutor} once the task was executed.
     */
    // CLR: the "task" is SslStream's asynchronous continuation (see RunDelegatedTasks); Run() is called by
    // OnEngineProgress when it made progress. The fields are the outer handler's (a Java inner class reads them
    // directly). runCompleteTask only serves the library-not-ported AsyncRunnable (OpenSSL) tasks.
    private sealed class SslTasksRunner
    {
        private readonly SslHandler handler;
        private readonly bool inUnwrap;
        private readonly Action resumeOnEventExecutor;

        internal SslTasksRunner(SslHandler handler, bool inUnwrap)
        {
            this.handler = handler;
            this.inUnwrap = inUnwrap;
            resumeOnEventExecutor = ResumeOnEventExecutor;
        }

        // Handle errors which happened during task processing.
        private void TaskError(Exception e)
        {
            IChannelHandlerContext ctx = handler.ctx;
            if (inUnwrap)
            {
                // As the error happened while the task was scheduled as part of unwrap(...) we also need to ensure
                // we fire it through the pipeline as inbound error to be consistent with what we do in decode(...).
                //
                // This will also ensure we fail the handshake future and flush all produced data.
                try
                {
                    handler.HandleUnwrapThrowable(ctx, e);
                }
                catch (Exception cause)
                {
                    SafeExceptionCaught(cause);
                }
            }
            else
            {
                handler.SetHandshakeFailure(ctx, e);
                handler.ForceFlush(ctx);
            }
        }

        // Try to call exceptionCaught(...)
        private void SafeExceptionCaught(Exception cause)
        {
            IChannelHandlerContext ctx = handler.ctx;
            try
            {
                handler.ExceptionCaught(ctx, WrapIfNeeded(cause));
            }
            catch (Exception error)
            {
                ctx.FireExceptionCaught(error);
            }
        }

        private Exception WrapIfNeeded(Exception cause)
        {
            if (!inUnwrap)
            {
                // If we are not in unwrap(...) we can just rethrow without wrapping at all.
                return cause;
            }
            // As the exception would have been triggered by an inbound operation we will need to wrap it in a
            // DecoderException to mimic what a decoder would do when decode(...) throws.
            return cause is DecoderException ? cause : new DecoderException(cause);
        }

        private void TryDecodeAgain()
        {
            IChannelHandlerContext ctx = handler.ctx;
            try
            {
                handler.ChannelRead(ctx, Unpooled.EmptyBuffer);
            }
            catch (Exception cause)
            {
                SafeExceptionCaught(cause);
            }
            finally
            {
                // As we called channelRead(...) we also need to call channelReadComplete(...) which
                // will ensure we either call ctx.fireChannelReadComplete() or will trigger a ctx.read() if
                // more data is needed.
                handler.ChannelReadComplete0(ctx);
            }
        }

        /**
         * Executed after the wrapped {@code task} was executed via {@code delegatedTaskExecutor} to resume work
         * on the {@link EventExecutor}.
         */
        private void ResumeOnEventExecutor()
        {
            IChannelHandlerContext ctx = handler.ctx;
            Debug.Assert(ctx.Executor.InEventLoop());
            handler.ClearState(STATE_PROCESS_TASK);
            // CLR: a resume that OnEngineProgress scheduled without a waiting runner may be scheduled again from here.
            Interlocked.Exchange(ref handler.resumeScheduled, 0);
            try
            {
                HandshakeStatus status = handler.EngineGetHandshakeStatus();
                switch (status)
                {
                    // There is another task that needs to be executed and offloaded to the delegatingTaskExecutor as
                    // a result of this. Let's reschedule....
                    case HandshakeStatus.NEED_TASK:
                        handler.ExecuteDelegatedTask(this);

                        break;

                    // The handshake finished, lets notify about the completion of it and resume processing.
                    case HandshakeStatus.FINISHED:
                    // Not handshaking anymore, lets notify about the completion if not done yet and resume processing.
                    case HandshakeStatus.NOT_HANDSHAKING:
                        handler.SetHandshakeSuccess(); // NOT_HANDSHAKING -> workaround for android skipping FINISHED state.
                        try
                        {
                            // Lets call wrap to ensure we produce the alert if there is any pending and also to
                            // ensure we flush any queued data..
                            handler.Wrap(ctx, inUnwrap);
                        }
                        catch (Exception e)
                        {
                            TaskError(e);
                            return;
                        }
                        if (inUnwrap)
                        {
                            // If we were in the unwrap call when the task was processed we should also try to unwrap
                            // non app data first as there may not anything left in the inbound buffer to process.
                            handler.UnwrapNonAppData(ctx);
                        }

                        // Flush now as we may have written some data as part of the wrap call.
                        handler.ForceFlush(ctx);

                        TryDecodeAgain();
                        break;

                    // We need more data so lets try to unwrap first and then call decode again which will feed us
                    // with buffered data (if there is any).
                    case HandshakeStatus.NEED_UNWRAP:
                        try
                        {
                            handler.UnwrapNonAppData(ctx);
                        }
                        catch (AuthenticationException e)
                        {
                            handler.HandleUnwrapThrowable(ctx, e);
                            return;
                        }
                        TryDecodeAgain();
                        break;

                    // To make progress we need to call SSLEngine.wrap(...) which may produce more output data
                    // that will be written to the Channel.
                    case HandshakeStatus.NEED_WRAP:
                        try
                        {
                            if (!handler.WrapNonAppData(ctx, false) && inUnwrap)
                            {
                                // The handshake finished in wrapNonAppData(...), we need to try call
                                // unwrapNonAppData(...) as we may have some alert that we should read.
                                //
                                // This mimics what we would do when we are calling this method while in unwrap(...).
                                handler.UnwrapNonAppData(ctx);
                            }

                            // Flush now as we may have written some data as part of the wrap call.
                            handler.ForceFlush(ctx);
                        }
                        catch (Exception e)
                        {
                            TaskError(e);
                            return;
                        }

                        // Now try to feed in more data that we have buffered.
                        TryDecodeAgain();
                        break;

                    default:
                        // Should never reach here as we handle all cases.
                        // CLR: AssertionError is an Error; InvalidOperationException reaches the catch below as the
                        // Error reaches catch (Throwable).
                        throw new InvalidOperationException();
                }
            }
            catch (Exception cause)
            {
                SafeExceptionCaught(cause);
            }
        }

        internal void RunComplete()
        {
            IEventExecutor executor = handler.ctx.Executor;
            if (executor.IsShuttingDown())
            {
                // The executor is already shutting down, just return.
                return;
            }
            // Jump back on the EventExecutor. We do this even if we are already on the EventLoop to guard against
            // reentrancy issues. Failing to do so could lead to the situation of tryDecode(...) be called and so
            // channelRead(...) while still in the decode loop. In this case channelRead(...) might release the input
            // buffer if its empty which would then result in an IllegalReferenceCountException when we try to continue
            // decoding.
            //
            // See https://github.com/netty/netty-tcnative/issues/680
            executor.Execute(resumeOnEventExecutor);
        }

        // CLR: there is no delegated task to take from the engine and run (SslStream has already run it); what remains
        // of run() is runComplete() and its exception handling.
        internal void Run()
        {
            try
            {
                RunComplete();
            }
            catch (Exception cause)
            {
                HandleException(cause);
            }
        }

        private void HandleException(Exception cause)
        {
            IChannelHandlerContext ctx = handler.ctx;
            IEventExecutor executor = ctx.Executor;
            if (executor.InEventLoop())
            {
                handler.ClearState(STATE_PROCESS_TASK);
                SafeExceptionCaught(cause);
            }
            else
            {
                try
                {
                    executor.Execute(() =>
                    {
                        handler.ClearState(STATE_PROCESS_TASK);
                        SafeExceptionCaught(cause);
                    });
                }
                catch (RejectedExecutionException)
                {
                    handler.ClearState(STATE_PROCESS_TASK);
                    // the context itself will handle the rejected exception when try to schedule the operation so
                    // ignore the RejectedExecutionException
                    ctx.FireExceptionCaught(cause);
                }
            }
        }
    }

    /**
     * Notify all the handshake futures about the successfully handshake
     * @return {@code true} if {@link #handshakePromise} was set successfully and a {@link SslHandshakeCompletionEvent}
     * was fired. {@code false} otherwise.
     */
    // CLR: the SSLSession facts are the SslStream's. ResumptionController (trust-manager glue of the JDK engines) is
    // not ported, so the resumed-session validation has no counterpart. SSLEngine reports FINISHED/NOT_HANDSHAKING only
    // after a handshake; the SslStream status is derived from the handshake Task, so success also requires that Task to
    // have completed successfully (an engine closed before or after a failed handshake also reports NOT_HANDSHAKING).
    private bool SetHandshakeSuccess()
    {
        // Our control flow may invoke this method multiple times for a single FINISHED event. For example
        // wrapNonAppData may drain pendingUnencryptedWrites in wrap which transitions to handshake from FINISHED to
        // NOT_HANDSHAKING which invokes setHandshakeSuccess, and then wrapNonAppData also directly invokes this method.
        bool notified = false;
        if (!handshakePromise.Task.IsCompleted && EngineHandshakeSucceeded())
        {
            // CLR: the application protocol and the application-data read of a completed handshake are set up first.
            EngineEnsureReading();
            notified = handshakePromise.TrySetResult(ctx.Channel);
        }
        if (notified)
        {
            if (logger.IsDebugEnabled())
            {
                logger.Debug(
                        "{} HANDSHAKEN: protocol:{} cipher suite:{}",
                        ctx.Channel,
                        engine.SslProtocol,
                        engine.NegotiatedCipherSuite);
            }
            ctx.FireUserEventTriggered(SslHandshakeCompletionEvent.SUCCESS);
        }
        if (IsStateSet(STATE_READ_DURING_HANDSHAKE))
        {
            ClearState(STATE_READ_DURING_HANDSHAKE);
            if (!ctx.Channel.Config.IsAutoRead())
            {
                ctx.Read();
            }
        }
        return notified;
    }

    /**
     * Notify all the handshake futures about the failure during the handshake.
     */
    private void SetHandshakeFailure(IChannelHandlerContext ctx, Exception cause)
    {
        SetHandshakeFailure(ctx, cause, true, true, false);
    }

    /**
     * Notify all the handshake futures about the failure during the handshake.
     */
    private void SetHandshakeFailure(IChannelHandlerContext ctx, Exception cause, bool closeInbound,
                                     bool notify, bool alwaysFlushAndClose)
    {
        try
        {
            // Release all resources such as internal buffers that SSLEngine is managing.
            SetState(STATE_OUTBOUND_CLOSED);
            EngineCloseOutbound();

            if (closeInbound)
            {
                // CLR: SslStream has no closeInbound(); the inbound side is marked done, which never throws, so the
                // debug log of SSLEngine.closeInbound()'s SSLException (no close_notify received) has no counterpart.
                EngineCloseInbound();
            }
            if (handshakePromise.TrySetException(cause) || alwaysFlushAndClose)
            {
                SslUtils.HandleHandshakeFailure(ctx, cause, notify);
            }
        }
        finally
        {
            // Ensure we remove and fail all pending writes in all cases and so release memory quickly.
            ReleaseAndFailAll(ctx, cause);
        }
    }

    private void SetHandshakeFailureTransportFailure(IChannelHandlerContext ctx, Exception cause)
    {
        // If TLS control frames fail to write we are in an unknown state and may become out of
        // sync with our peer. We give up and close the channel. This will also take care of
        // cleaning up any outstanding state (e.g. handshake promise, queued unencrypted data).
        try
        {
            AuthenticationException transportFailure = new AuthenticationException("failure when writing TLS control frames", cause);
            ReleaseAndFailAll(ctx, transportFailure);
            if (handshakePromise.TrySetException(transportFailure))
            {
                ctx.FireUserEventTriggered(new SslHandshakeCompletionEvent(transportFailure));
            }
        }
        finally
        {
            _ = ctx.CloseAsync();
        }
    }

    // CLR: the ResumptionController removal of an invalid session has no counterpart (see setHandshakeSuccess()).
    private void ReleaseAndFailAll(IChannelHandlerContext ctx, Exception cause)
    {
        if (pendingUnencryptedWrites != null)
        {
            pendingUnencryptedWrites.ReleaseAndFailAll(cause);
        }
    }

    private void NotifyClosePromise(Exception cause)
    {
        if (cause == null)
        {
            if (sslClosePromise.TrySetResult(ctx.Channel))
            {
                ctx.FireUserEventTriggered(SslCloseCompletionEvent.SUCCESS);
            }
        }
        else
        {
            if (sslClosePromise.TrySetException(cause))
            {
                ctx.FireUserEventTriggered(new SslCloseCompletionEvent(cause));
            }
        }
    }

    // CLR: the ChannelPromise is the returned Task. A channel that is not active returns the Task of the next
    // handler's disconnect/close; otherwise a TaskCompletionSource stands for the promise and is completed with
    // TrySet* only, which is what PromiseNotifier.cascade(false, ctx.newPromise(), promise) provides
    // (docs/common-task-composition.md), so safeClose(...) completes it directly. A Task cannot be cancelled by its
    // consumer, so the cascade's cancellation propagation has no counterpart.
    private Task CloseOutboundAndChannel(
            IChannelHandlerContext ctx, CancellationToken cancellationToken, bool disconnect)
    {
        SetState(STATE_OUTBOUND_CLOSED);
        EngineCloseOutbound();

        if (!ctx.Channel.IsActive)
        {
            if (disconnect)
            {
                return ctx.DisconnectAsync(cancellationToken);
            }
            else
            {
                return ctx.CloseAsync(cancellationToken);
            }
        }

        TaskCompletionSource promise = NewUserPromise();
        TaskCompletionSource closeNotifyPromise = NewPromise();
        try
        {
            Flush(ctx, closeNotifyPromise);
        }
        finally
        {
            if (!IsStateSet(STATE_CLOSE_NOTIFY))
            {
                SetState(STATE_CLOSE_NOTIFY);
                // It's important that we do not pass the original ChannelPromise to safeClose(...) as when flush(....)
                // throws an Exception it will be propagated to the AbstractChannelHandlerContext which will try
                // to fail the promise because of this. This will then fail as it was already completed by
                // safeClose(...). We create a new ChannelPromise and try to notify the original ChannelPromise
                // once it is complete. If we fail to do so we just ignore it as in this case it was failed already
                // because of a propagated Exception.
                //
                // See https://github.com/netty/netty/issues/5931
                SafeClose(ctx, closeNotifyPromise.Task, promise);
            }
            else
            {
                // / We already handling the close_notify so just attach the promise to the sslClosePromise.
                AddListener(sslClosePromise.Task, f => promise.TrySetResult());
            }
        }
        return promise.Task;
    }

    private void Flush(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        if (pendingUnencryptedWrites != null)
        {
            pendingUnencryptedWrites.Add(Unpooled.EmptyBuffer, promise);
        }
        else
        {
            promise.TrySetException(NewPendingWritesNullException());
        }
        Flush(ctx);
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
        // CLR: the scheduler of the listeners that DefaultPromise would notify on ctx.executor().
        listenerScheduler = new EventExecutorTaskScheduler(() => ctx.Executor);
        IChannel channel = ctx.Channel;
        pendingUnencryptedWrites = new PendingWritesQueue(this, channel, 16, wantsDirectBuffer);

        // CLR: getOption(TCP_FASTOPEN_CONNECT) is the try pattern; Channel.Unsafe is folded into AbstractChannel, whose
        // OutboundBuffer is unsafe().outboundBuffer() (as IdleStateHandler reads it). setOpensslEngineSocketFd(...) only
        // serves the library-not-ported OpenSSL engine.
        bool fastOpen = channel.Config.TryGetOption(ChannelOption.TCP_FASTOPEN_CONNECT, out bool fastOpenValue) &&
                fastOpenValue;
        bool active = channel.IsActive;
        if (active || fastOpen)
        {
            // Explicitly flush the handshake only if the channel is already active.
            // With TCP Fast Open, we write to the outbound buffer before the TCP connect is established.
            // The buffer will then be flushed as part of establishing the connection, saving us a round-trip.
            StartHandshakeProcessing(active);
            // If we weren't able to include client_hello in the TCP SYN (e.g. no token, disabled at the OS) we have to
            // flush pending data in the outbound buffer later in channelActive().
            ChannelOutboundBuffer outboundBuffer;
            if (fastOpen && ((outboundBuffer = (channel as AbstractChannel)?.OutboundBuffer) == null ||
                    outboundBuffer.TotalPendingWriteBytes > 0))
            {
                SetState(STATE_NEEDS_FLUSH);
            }
        }
    }

    // CLR: the anonymous SslHandlerCoalescingBufferQueue subclass of handlerAdded(...).
    private sealed class PendingWritesQueue : SslHandlerCoalescingBufferQueue
    {
        private readonly SslHandler handler;

        internal PendingWritesQueue(SslHandler handler, IChannel channel, int initSize, bool wantsDirectBuffer)
            : base(channel, initSize, wantsDirectBuffer)
        {
            this.handler = handler;
        }

        protected override int WrapDataSize()
        {
            return handler.wrapDataSize;
        }
    }

    private void StartHandshakeProcessing(bool flushAtEnd)
    {
        if (!IsStateSet(STATE_HANDSHAKE_STARTED))
        {
            SetState(STATE_HANDSHAKE_STARTED);
            if (EngineGetUseClientMode())
            {
                // Begin the initial handshake.
                // channelActive() event has been fired already, which means this.channelActive() will
                // not be invoked. We have to initialize here instead.
                Handshake(flushAtEnd);
            }
            ApplyHandshakeTimeout();
        }
        else if (IsStateSet(STATE_NEEDS_FLUSH))
        {
            ForceFlush(ctx);
        }
    }

    /**
     * Performs TLS renegotiation.
     */
    public Task<IChannel> Renegotiate()
    {
        IChannelHandlerContext ctx = this.ctx;
        if (ctx == null)
        {
            throw new InvalidOperationException();
        }

        return Renegotiate(NewChannelPromise());
    }

    /**
     * Performs TLS renegotiation.
     */
    // CLR: the Promise<Channel> is a TaskCompletionSource<IChannel> producer, as the Transport takes producers
    // (PendingWriteQueue.Add); Future<Channel> is its Task. SslStream renegotiates only on the server
    // (SslStream.NegotiateClientCertificateAsync: "Negotiates the client certificate on the authenticated connection",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.negotiateclientcertificateasync?view=net-10.0);
    // in client mode beginHandshake() fails (see EngineBeginHandshake).
    public Task<IChannel> Renegotiate(TaskCompletionSource<IChannel> promise)
    {
        ObjectUtil.CheckNotNull(promise, "promise");

        IChannelHandlerContext ctx = this.ctx;
        if (ctx == null)
        {
            throw new InvalidOperationException();
        }

        IEventExecutor executor = ctx.Executor;
        if (!executor.InEventLoop())
        {
            executor.Execute(() => RenegotiateOnEventLoop(promise));
            return promise.Task;
        }

        RenegotiateOnEventLoop(promise);
        return promise.Task;
    }

    private void RenegotiateOnEventLoop(TaskCompletionSource<IChannel> newHandshakePromise)
    {
        TaskCompletionSource<IChannel> oldHandshakePromise = handshakePromise;
        if (!oldHandshakePromise.Task.IsCompleted)
        {
            // There's no need to handshake because handshake is in progress already.
            // Merge the new promise into the old one.
            _ = oldHandshakePromise.Task.ContinueWith(static (f, p) => ((TaskCompletionSource<IChannel>) p).TrySetFromTask(f),
                    newHandshakePromise, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
        else
        {
            handshakePromise = newHandshakePromise;
            Handshake(true);
            ApplyHandshakeTimeout();
        }
    }

    /**
     * Performs TLS (re)negotiation.
     * @param flushAtEnd Set to {@code true} if the outbound buffer should be flushed (written to the network) at the
     *                  end. Set to {@code false} if the handshake will be flushed later, e.g. as part of TCP Fast Open
     *                  connect.
     */
    private void Handshake(bool flushAtEnd)
    {
        if (EngineGetHandshakeStatus() != HandshakeStatus.NOT_HANDSHAKING)
        {
            // Not all SSLEngine implementations support calling beginHandshake multiple times while a handshake
            // is in progress. See https://github.com/netty/netty/issues/4718.
            return;
        }
        if (handshakePromise.Task.IsCompleted)
        {
            // If the handshake is done already lets just return directly as there is no need to trigger it again.
            // This can happen if the handshake(...) was triggered before we called channelActive(...) by a
            // flush() that was triggered by a ChannelFutureListener that was added to the ChannelFuture returned
            // from the connect(...) method. In this case we will see the flush() happen before we had a chance to
            // call fireChannelActive() on the pipeline.
            return;
        }

        // Begin handshake.
        IChannelHandlerContext ctx = this.ctx;
        try
        {
            EngineBeginHandshake();
            WrapNonAppData(ctx, false);
        }
        catch (Exception e)
        {
            SetHandshakeFailure(ctx, e);
        }
        finally
        {
            if (flushAtEnd)
            {
                ForceFlush(ctx);
            }
        }
    }

    private void ApplyHandshakeTimeout()
    {
        TaskCompletionSource<IChannel> localHandshakePromise = this.handshakePromise;

        // Set timeout if necessary.
        long handshakeTimeoutMillis = Volatile.Read(ref this.handshakeTimeoutMillis);
        if (handshakeTimeoutMillis <= 0 || localHandshakePromise.Task.IsCompleted)
        {
            return;
        }

        IChannelHandlerContext ctx = this.ctx;
        CancellationTokenSource timeoutFuture = new CancellationTokenSource();
        _ = ctx.Executor.ScheduleAsync(() =>
        {
            if (localHandshakePromise.Task.IsCompleted)
            {
                return;
            }
            AuthenticationException exception =
                    new SslHandshakeTimeoutException("handshake timed out after " + handshakeTimeoutMillis + "ms");
            try
            {
                if (localHandshakePromise.TrySetException(exception))
                {
                    SslUtils.HandleHandshakeFailure(ctx, exception, true);
                }
            }
            finally
            {
                ReleaseAndFailAll(ctx, exception);
            }
        }, TimeSpan.FromMilliseconds(handshakeTimeoutMillis), timeoutFuture.Token);

        // Cancel the handshake timeout when handshake is finished.
        AddListener(localHandshakePromise.Task, f => timeoutFuture.Cancel());
    }

    private void ForceFlush(IChannelHandlerContext ctx)
    {
        ClearState(STATE_NEEDS_FLUSH);
        ctx.Flush();
    }

    /**
     * Issues an initial TLS handshake once connected when used in client-mode
     */
    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        if (!startTls)
        {
            StartHandshakeProcessing(true);
        }
        ctx.FireChannelActive();
    }

    private void SafeClose(
            IChannelHandlerContext ctx, Task flushFuture,
            TaskCompletionSource promise)
    {
        if (!ctx.Channel.IsActive)
        {
            AddCloseListener(ctx.CloseAsync(), promise);
            return;
        }

        CancellationTokenSource timeoutFuture;
        if (!flushFuture.IsCompleted)
        {
            long closeNotifyTimeout = Volatile.Read(ref closeNotifyFlushTimeoutMillis);
            if (closeNotifyTimeout > 0)
            {
                // Force-close the connection if close_notify is not fully sent in time.
                timeoutFuture = new CancellationTokenSource();
                _ = ctx.Executor.ScheduleAsync(() =>
                {
                    // May be done in the meantime as cancel(...) is only best effort.
                    if (!flushFuture.IsCompleted)
                    {
                        logger.Warn("{} Last write attempt timed out; force-closing the connection.",
                                ctx.Channel);
                        AddCloseListener(ctx.CloseAsync(), promise);
                    }
                }, TimeSpan.FromMilliseconds(closeNotifyTimeout), timeoutFuture.Token);
            }
            else
            {
                timeoutFuture = null;
            }
        }
        else
        {
            timeoutFuture = null;
        }

        // Close the connection if close_notify is sent in time.
        AddListener(flushFuture, f =>
        {
            if (timeoutFuture != null)
            {
                timeoutFuture.Cancel();
            }
            long closeNotifyReadTimeout = Volatile.Read(ref closeNotifyReadTimeoutMillis);
            if (closeNotifyReadTimeout <= 0)
            {
                // Trigger the close in all cases to make sure the promise is notified
                // See https://github.com/netty/netty/issues/2358
                AddCloseListener(ctx.CloseAsync(), promise);
            }
            else
            {
                CancellationTokenSource closeNotifyReadTimeoutFuture;

                if (!sslClosePromise.Task.IsCompleted)
                {
                    closeNotifyReadTimeoutFuture = new CancellationTokenSource();
                    _ = ctx.Executor.ScheduleAsync(() =>
                    {
                        if (!sslClosePromise.Task.IsCompleted)
                        {
                            logger.Debug(
                                    "{} did not receive close_notify in {}ms; force-closing the connection.",
                                    ctx.Channel, closeNotifyReadTimeout);

                            // Do the close now...
                            AddCloseListener(ctx.CloseAsync(), promise);
                        }
                    }, TimeSpan.FromMilliseconds(closeNotifyReadTimeout), closeNotifyReadTimeoutFuture.Token);
                }
                else
                {
                    closeNotifyReadTimeoutFuture = null;
                }

                // Do the close once the we received the close_notify.
                AddListener(sslClosePromise.Task, future =>
                {
                    if (closeNotifyReadTimeoutFuture != null)
                    {
                        closeNotifyReadTimeoutFuture.Cancel();
                    }
                    AddCloseListener(ctx.CloseAsync(), promise);
                });
            }
        });
    }

    private void AddCloseListener(Task future, TaskCompletionSource promise)
    {
        // We notify the promise in the ChannelPromiseNotifier as there is a "race" where the close(...) call
        // by the timeoutFuture and the close call in the flushFuture listener will be called. Because of
        // this we need to use trySuccess() and tryFailure(...) as otherwise we can cause an
        // IllegalStateException.
        // Also we not want to log if the notification happens as this is expected in some cases.
        // See https://github.com/netty/netty/issues/5598
        Cascade(future, promise);
    }

    /**
     * Always prefer a direct buffer when it's pooled, so that we reduce the number of memory copies
     * in {@link OpenSslEngine}.
     */
    private ByteBuf Allocate(IChannelHandlerContext ctx, int capacity)
    {
        IByteBufAllocator alloc = ctx.Alloc();
        if (wantsDirectBuffer)
        {
            return alloc.DirectBuffer(capacity);
        }
        else
        {
            return alloc.Buffer(capacity);
        }
    }

    /**
     * Allocates an outbound network buffer for {@link SSLEngine#wrap(ByteBuffer, ByteBuffer)} which can encrypt
     * the specified amount of pending bytes.
     */
    private ByteBuf AllocateOutNetBuf(IChannelHandlerContext ctx, int pendingBytes, int numComponents)
    {
        // For JDK we don't have a good source for the max wrap overhead. We need at least one packet buffer
        // size, but may be able to fit more in based on the total requested.
        // CLR: SslStream has no wrap-overhead query either; the packet buffer size is PACKET_BUFFER_SIZE. The JDK branch
        // ignores numComponents, so the callers pass the packet count without the NIO buffer count of the source.
        return ctx.Alloc().HeapBuffer(Math.Max(pendingBytes, PACKET_BUFFER_SIZE));
    }

    private static int CalculateRequiredOutBufSpace(int pendingBytes, int numComponents)
    {
        // As for the JDK SSLEngine we always need to operate on buffer space required by the SSLEngine
        // (normally ~16KB). This is required even if the amount of data to encrypt is very small. Use heap
        // buffers to reduce the native memory usage.
        //
        // Beside this the JDK SSLEngine also (as of today) will do an extra heap to direct buffer copy
        // if a direct buffer is used as its internals operate on byte[].
        return PACKET_BUFFER_SIZE;
    }

    private bool IsStateSet(int bit)
    {
        return (state & bit) == bit;
    }

    private void SetState(int bit)
    {
        state |= bit;
    }

    private void ClearState(int bit)
    {
        state &= ~bit;
    }

    // CLR: LazyChannelPromise (a DefaultPromise on ctx.executor() with a dead-lock check for sync()) is a
    // TaskCompletionSource<IChannel> whose continuations run asynchronously, as the Transport's operation Tasks do; a
    // Task has no executor and no blocking-wait check, and the listeners of this handler are scheduled on
    // listenerScheduler.
    private static TaskCompletionSource<IChannel> NewChannelPromise()
    {
        return new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // ---- CLR-only helpers -----------------------------------------------------------------------------------------

    private EventExecutorTaskScheduler listenerScheduler;

    // CLR: ctx.newPromise() for the promises of this handler (aggregate write promises, the close_notify flush): their
    // continuations (the queued writes' producers) run at once, as DefaultPromise notifies on the event loop.
    private static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource();
    }

    // CLR: a promise whose Task is returned to the caller; its continuations run asynchronously, as the Transport's
    // operation Tasks do.
    private static TaskCompletionSource NewUserPromise()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // CLR: future.addListener(listener) on this handler's state: a continuation on listenerScheduler (Common's
    // EventExecutorTaskScheduler over ctx.Executor), which runs it at once on the event loop and otherwise hands it to
    // the executor (DefaultPromise.notifyListeners policy).
    private void AddListener(Task future, Action<Task> listener)
    {
        _ = future.ContinueWith(listener, CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
    }

    // CLR: ctx.write(msg, promise) / PromiseNotifier.cascade(future, promise): the outcome of the Task is copied into
    // the producer with TrySetFromTask (docs/common-task-composition.md). The continuation runs on listenerScheduler
    // (Common's EventExecutorTaskScheduler over ctx.Executor): the Transport completes its operation Tasks with
    // RunContinuationsAsynchronously, which queues the continuation to its scheduler from the completing thread, and
    // this scheduler runs it at once on the event loop, as DefaultPromise notifies a ChannelPromise there;
    // TaskScheduler.Default would complete the producers on a thread-pool thread.
    private void Cascade(Task future, TaskCompletionSource promise)
    {
        if (future.IsCompleted)
        {
            promise.TrySetFromTask(future);
        }
        else
        {
            _ = future.ContinueWith(static (f, p) => ((TaskCompletionSource) p).TrySetFromTask(f), promise,
                    CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
        }
    }

    // CLR: Future.cause(): null on success or while pending; a cancelled Task's cause is a TaskCanceledException, as
    // AbstractSniHandler maps CancellationException.
    private static Exception CauseOf(Task future)
    {
        if (future.IsFaulted)
        {
            return future.Exception.InnerException;
        }
        return future.IsCanceled ? new TaskCanceledException(future) : null;
    }

    // CLR: TimeUnit.toMillis truncates toward zero, as the integer division of the 100 ns ticks does.
    private static long ToMillis(TimeSpan duration)
    {
        return duration.Ticks / TimeSpan.TicksPerMillisecond;
    }

    private static Regex CompileJavaPattern(string pattern)
    {
        StringBuilder regex = new StringBuilder(@"\A(?:");
        foreach (char c in pattern)
        {
            if (c == '.')
            {
                regex.Append(@"[^\n\r\u0085\u2028\u2029]");
            }
            else if (c >= 'a' && c <= 'z')
            {
                regex.Append('[').Append(c).Append(char.ToUpperInvariant(c)).Append(']');
            }
            else
            {
                regex.Append(c);
            }
        }
        return new Regex(regex.Append(@")\z").ToString(), RegexOptions.CultureInvariant);
    }

    // ---- CLR: the SSLEngine calls on SslStream ---------------------------------------------------------------------
    //
    // SSLEngine.wrap/unwrap are EngineWrap/EngineUnwrap, beginHandshake() EngineBeginHandshake, getHandshakeStatus()
    // EngineGetHandshakeStatus, closeOutbound()/closeInbound()/isInboundDone() EngineCloseOutbound/EngineCloseInbound/
    // EngineIsInboundDone and getUseClientMode() EngineGetUseClientMode. The handshake is
    // AuthenticateAsClientAsync/AuthenticateAsServerAsync(options) ("If the authentication fails, you receive a
    // AuthenticationException, and this SslStream is no longer useable",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.authenticateasclientasync?view=net-10.0),
    // started where Java calls beginHandshake() or, as SSLEngine.wrap()/unwrap() "will implicitly call this method if
    // handshaking has not already begun" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLEngine.html),
    // by the first EngineWrap/EngineUnwrap. After the handshake one SslStream.ReadAsync is kept outstanding; a pushed
    // record completes it inline with the decrypted bytes or with 0, "When there is no more data to be read"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.readasync?view=net-10.0), which is the
    // peer's close_notify (SSLEngineResult.Status.CLOSED). Application data goes through SslStream.WriteAsync, which may
    // not be called "until you have successfully authenticated" and not while another write is in progress
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.writeasync?view=net-10.0), so a wrap
    // during a handshake consumes nothing, as an SSLEngine does; closeOutbound() is SslStream.ShutdownAsync ("Shuts down
    // this SslStream", https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.shutdownasync?view=net-10.0),
    // which writes close_notify. The status of a result is derived from SslStream's state: output waiting in the
    // mediation stream or an unreported failure is NEED_WRAP, a handshake that waits for a record NEED_UNWRAP, a
    // handshake that went on asynchronously NEED_TASK, and a handshake Task that completed successfully is reported
    // FINISHED once. Failures are thrown once by the call that observes them, as SSLException from wrap/unwrap;
    // SslStream's IOExceptions of a read or write are wrapped in AuthenticationException (SSLException), since the
    // mediation stream itself never fails.
    //
    // SslStream continues inline only where the completing thread may run continuations: "if the current thread has a
    // non-null SynchronizationContext or a non-default scheduler, then we better not run arbitrary continuations here"
    // (AwaitTaskContinuation.IsValidLocationForInlining,
    // https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskContinuation.cs).
    // RunEngine therefore clears a SynchronizationContext for the call
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.synchronizationcontext.setsynchronizationcontext?view=net-10.0)
    // and, inside a Task of another scheduler (e.g. a listener on EventExecutorTaskScheduler), runs the call in a Task
    // on the default scheduler with Task.RunSynchronously, "on the TaskScheduler provided"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.runsynchronously?view=net-10.0).

    private enum Status
    {
        BUFFER_UNDERFLOW,
        BUFFER_OVERFLOW,
        OK,
        CLOSED,
    }

    private enum HandshakeStatus
    {
        NOT_HANDSHAKING,
        FINISHED,
        NEED_TASK,
        NEED_WRAP,
        NEED_UNWRAP,
    }

    // The result of EngineWrap/EngineUnwrap, as SSLEngineResult.
    private readonly struct EngineResult
    {
        private readonly Status status;
        private readonly HandshakeStatus handshakeStatus;
        private readonly int bytesConsumed;
        private readonly int bytesProduced;

        internal EngineResult(Status status, HandshakeStatus handshakeStatus, int bytesConsumed, int bytesProduced)
        {
            this.status = status;
            this.handshakeStatus = handshakeStatus;
            this.bytesConsumed = bytesConsumed;
            this.bytesProduced = bytesProduced;
        }

        internal Status GetStatus() => status;

        internal HandshakeStatus GetHandshakeStatus() => handshakeStatus;

        internal int BytesConsumed() => bytesConsumed;

        internal int BytesProduced() => bytesProduced;
    }

    private readonly SslClientAuthenticationOptions clientOptions;
    private readonly SslServerAuthenticationOptions serverOptions;
    private readonly MediationStream mediation;
    private readonly Action onEngineProgress;
    private Task handshakeOperation;
    private bool handshakeFinishedReported;
    private bool initialHandshakeDone;
    private string negotiatedApplicationProtocol;
    private byte[] readBuffer;
    private ValueTask<int> readOperation;
    private bool readOutstanding;
    private CancellationTokenSource readCancellation;
    private ValueTask writeOperation;
    private bool writeOutstanding;
    private ByteBuf writeRetained;
    private Task shutdownOperation;
    private bool engineInboundDone;
    private bool engineOutboundDone;
    private bool engineClosed;
    private bool engineDisposed;
    private bool engineFailureReported;
    private Exception engineFailure;
    private volatile int engineThread;
    private SslTasksRunner waitingTaskRunner;
    private int resumeScheduled;

    private TResult RunEngine<TState, TResult>(TState state, Func<TState, TResult> call)
    {
        int previousThread = engineThread;
        engineThread = Environment.CurrentManagedThreadId;
        SynchronizationContext context = SynchronizationContext.Current;
        bool resetContext = context != null && context.GetType() != typeof(SynchronizationContext);
        if (resetContext)
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
        try
        {
            if (TaskScheduler.Current == TaskScheduler.Default)
            {
                return call(state);
            }
            Task<TResult> inline = new Task<TResult>(static s =>
            {
                (Func<TState, TResult> c, TState st) = ((Func<TState, TResult>, TState)) s;
                return c(st);
            }, (call, state));
            inline.RunSynchronously(TaskScheduler.Default);
            return inline.GetAwaiter().GetResult();
        }
        finally
        {
            if (resetContext)
            {
                SynchronizationContext.SetSynchronizationContext(context);
            }
            engineThread = previousThread;
        }
    }

    // SSLEngine.getUseClientMode().
    private bool EngineGetUseClientMode()
    {
        return clientOptions != null;
    }

    // SSLEngine.beginHandshake(): the first handshake, or a renegotiation of the server.
    private void EngineBeginHandshake()
    {
        if (engineClosed)
        {
            return;
        }
        if (handshakeOperation != null && !EngineHandshakeSucceeded())
        {
            return;
        }
        if (handshakeOperation != null && clientOptions != null)
        {
            throw new NotSupportedException(
                    "SslStream cannot start a renegotiation in client mode (only NegotiateClientCertificateAsync of a " +
                    "server is available)");
        }
        bool renegotiation = handshakeOperation != null;
        handshakeFinishedReported = false;
        try
        {
            if (renegotiation)
            {
                // NegotiateClientCertificateAsync reads, and SslStream allows one read at a time.
                // Platform limit: application data that the peer sends before it answers the renegotiation fails it on
                // TLS 1.2 (SslStream throws "Received data during renegotiation.") and is not delivered on TLS 1.3
                // (measured), as SslStream's RenegotiateAsync rejects it only below TLS 1.3, where "it is not possible
                // to distinguish between late Handshake and Application Data"
                // (https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslStream.IO.cs).
                if (engine.RemoteCertificate != null)
                {
                    // The client certificate is known already (also from a resumed session): there is nothing to
                    // negotiate, and NegotiateClientCertificateAsync would throw InvalidOperationException ("Remote
                    // certificate is already available.", measured), so the renegotiation completes at once.
                    handshakeOperation = Task.CompletedTask;
                }
                else
                {
                    EngineCancelRead();
                    handshakeOperation = RunEngine(this, static h => h.EngineNegotiateClientCertificate());
                }
            }
            else
            {
                handshakeOperation = RunEngine(this, static h => h.clientOptions != null ?
                        h.engine.AuthenticateAsClientAsync(h.clientOptions) :
                        h.engine.AuthenticateAsServerAsync(h.serverOptions));
            }
        }
        catch (Exception e)
        {
            // A usage exception is thrown at once; it is the handshake failure (reported by this throw).
            handshakeOperation = Task.FromException(e);
            engineFailureReported = true;
            engineClosed = true;
            throw;
        }
        _ = handshakeOperation.ContinueWith(static (t, s) =>
        {
            _ = t.Exception;
            ((SslHandler) s).OnEngineProgress();
        }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // NegotiateClientCertificateAsync is SupportedOSPlatform freebsd, linux and windows.
    private Task EngineNegotiateClientCertificate()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            return engine.NegotiateClientCertificateAsync();
        }
        throw new PlatformNotSupportedException(
                "SslStream.NegotiateClientCertificateAsync is not supported on this platform");
    }

    private bool EngineHandshakeSucceeded()
    {
        return handshakeOperation != null && handshakeOperation.IsCompletedSuccessfully;
    }

    // SSLEngine.getHandshakeStatus().
    private HandshakeStatus EngineGetHandshakeStatus()
    {
        if (handshakeOperation == null || engineDisposed)
        {
            return HandshakeStatus.NOT_HANDSHAKING;
        }
        if (mediation.HasOutput() || EngineCheckFailure() != null)
        {
            return HandshakeStatus.NEED_WRAP;
        }
        // An application write that waits for a renegotiation: the wrap loops must wait for input, not spin.
        if ((!handshakeOperation.IsCompleted || EngineWritePending()) && !engineClosed)
        {
            return mediation.IsReadPending() ? HandshakeStatus.NEED_UNWRAP : HandshakeStatus.NEED_TASK;
        }
        return HandshakeStatus.NOT_HANDSHAKING;
    }

    // The handshake status of a wrap/unwrap result: FINISHED once when the handshake Task completed successfully.
    private HandshakeStatus EngineResultHandshakeStatus()
    {
        if (!handshakeFinishedReported && EngineHandshakeSucceeded() && !mediation.HasOutput() && !engineClosed)
        {
            handshakeFinishedReported = true;
            return HandshakeStatus.FINISHED;
        }
        return EngineGetHandshakeStatus();
    }

    // SSLEngine.closeOutbound(): close_notify through SslStream.ShutdownAsync once the handshake succeeded.
    private void EngineCloseOutbound()
    {
        if (engineOutboundDone)
        {
            return;
        }
        engineOutboundDone = true;
        if (!initialHandshakeDone || engineClosed)
        {
            return;
        }
        try
        {
            shutdownOperation = RunEngine(this, static h => h.engine.ShutdownAsync());
            _ = shutdownOperation.ContinueWith(static (t, s) =>
            {
                _ = t.Exception;
                ((SslHandler) s).OnEngineProgress();
            }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        catch (Exception e)
        {
            logger.Debug("SslStream.ShutdownAsync() raised an exception.", e);
        }
    }

    // SSLEngine.closeInbound().
    private void EngineCloseInbound()
    {
        engineInboundDone = true;
    }

    // SSLEngine.isInboundDone().
    private bool EngineIsInboundDone()
    {
        return engineInboundDone || engineClosed;
    }

    // SSLEngine.unwrap(ByteBuffer, ByteBuffer): hands len bytes of in (one whole record, from its reader index) to the
    // pending read of SslStream and appends the decrypted bytes to out.
    private EngineResult EngineUnwrap(ByteBuf @in, int len, ByteBuf @out)
    {
        if (engineClosed || engineInboundDone)
        {
            return new EngineResult(Status.CLOSED, EngineGetHandshakeStatus(), 0, 0);
        }
        if (handshakeOperation == null)
        {
            EngineBeginHandshake();
        }
        EngineEnsureReading();
        int produced = EngineHarvestReads(@out);
        // A record shorter than the 5-byte record header (an SSLv2-style length from getEncryptedPacketLength) is not a
        // complete packet for the JDK engine ("SSLv3/TLS length field is in bytes 3/4"; bytesInCompletePacket returns -1
        // when "packet.remaining() < 5" and readRecord answers "new SSLEngineResult(Status.BUFFER_UNDERFLOW, hsStatus,
        // 0, 0)", https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/sun/security/ssl/SSLEngineInputRecord.java,
        // https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/sun/security/ssl/SSLEngineImpl.java),
        // and SslStream would wait for the rest of a header that decode(...) never hands over, so nothing is consumed
        // (decodeJdkCompatible then reports NotSslRecordException, as SslHandlerTest.testIncorrectLength expects).
        if (len > 0 && len < SslUtils.SSL_RECORD_HEADER_LENGTH)
        {
            return new EngineResult(Status.BUFFER_UNDERFLOW, EngineGetHandshakeStatus(), 0, produced);
        }
        if (len > 0)
        {
            RunEngine((this, @in, len), static s =>
            {
                s.Item1.mediation.Push(s.Item2, s.Item2.ReaderIndex, s.Item3);
                return true;
            });
        }
        EngineThrowIfFailed();
        EngineEnsureReading();
        produced += EngineHarvestReads(@out);
        EngineThrowIfFailed();
        return new EngineResult(engineInboundDone ? Status.CLOSED : Status.OK, EngineResultHandshakeStatus(), len,
                produced);
    }

    // SSLEngine.wrap(ByteBuffer[], ByteBuffer): the records SslStream wrote (handshake, alerts, close_notify) come first,
    // then the application data of in once the handshake succeeded. The consumed bytes are skipped by the caller.
    private EngineResult EngineWrap(ByteBuf @in, ByteBuf @out)
    {
        if (handshakeOperation == null && !engineClosed)
        {
            EngineBeginHandshake();
        }
        EngineEnsureReading();
        EngineThrowIfFailed();
        int produced = mediation.DrainOutput(@out);
        if (engineClosed || engineOutboundDone)
        {
            return new EngineResult(Status.CLOSED, EngineResultHandshakeStatus(), 0, produced);
        }
        int consumed = 0;
        // During a renegotiation of this server SslStream holds its write side (WriteAsync throws
        // NotSupportedException, measured), so application data waits as during an SSLEngine handshake.
        if (EngineHandshakeSucceeded() && !EngineWritePending() && @in.IsReadable())
        {
            consumed = @in.ReadableBytes;
            EngineWriteApplicationData(@in, consumed);
            EngineThrowIfFailed();
            produced += mediation.DrainOutput(@out);
        }
        return new EngineResult(Status.OK, EngineResultHandshakeStatus(), consumed, produced);
    }

    private void EngineWriteApplicationData(ByteBuf @in, int length)
    {
        ReadOnlySequence<byte> sequence = @in.AsReadOnlySequence(@in.ReaderIndex, length);
        bool borrowed = sequence.IsSingleSegment;
        ReadOnlyMemory<byte> data = borrowed ? sequence.First : sequence.ToArray();
        ValueTask operation;
        try
        {
            operation = RunEngine((this, data), static s => s.Item1.engine.WriteAsync(s.Item2));
        }
        catch (Exception e)
        {
            engineFailure = ToSSLException(e);
            return;
        }
        if (operation.IsCompleted)
        {
            try
            {
                operation.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                engineFailure = ToSSLException(e);
            }
            return;
        }
        // SslStream waits for a renegotiation before it encrypts: the memory of in must stay valid until then.
        writeOperation = operation;
        writeOutstanding = true;
        if (borrowed)
        {
            writeRetained = @in.Retain();
        }
        operation.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(onEngineProgress);
    }

    private bool EngineWritePending()
    {
        if (writeOutstanding && writeOperation.IsCompleted)
        {
            writeOutstanding = false;
            try
            {
                writeOperation.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                engineFailure ??= ToSSLException(e);
            }
            finally
            {
                writeOperation = default;
                if (writeRetained != null)
                {
                    writeRetained.Release();
                    writeRetained = null;
                }
            }
        }
        return writeOutstanding;
    }

    // Starts the outstanding application-data read once the (re)handshake succeeded.
    private void EngineEnsureReading()
    {
        if (readOutstanding || engineInboundDone || engineClosed || !EngineHandshakeSucceeded())
        {
            return;
        }
        if (!initialHandshakeDone)
        {
            initialHandshakeDone = true;
            SslApplicationProtocol protocol = engine.NegotiatedApplicationProtocol;
            negotiatedApplicationProtocol = protocol.Protocol.IsEmpty ? null : Encoding.UTF8.GetString(protocol.Protocol.Span);
        }
        EngineStartRead();
    }

    private void EngineStartRead()
    {
        readBuffer ??= new byte[MAX_PLAINTEXT_LENGTH];
        if (!EngineGetUseClientMode())
        {
            readCancellation ??= new CancellationTokenSource();
        }
        try
        {
            readOperation = RunEngine(this, static h => h.engine.ReadAsync(h.readBuffer,
                    h.readCancellation?.Token ?? CancellationToken.None));
        }
        catch (Exception e)
        {
            engineFailure ??= ToSSLException(e);
            return;
        }
        readOutstanding = true;
        if (!readOperation.IsCompleted)
        {
            readOperation.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(onEngineProgress);
        }
    }

    // Appends the bytes of the completed reads to out and starts the next read; a read of 0 is the peer's close_notify.
    private int EngineHarvestReads(ByteBuf @out)
    {
        int produced = 0;
        while (readOutstanding && readOperation.IsCompleted)
        {
            readOutstanding = false;
            int n;
            try
            {
                n = readOperation.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                engineFailure ??= ToSSLException(e);
                return produced;
            }
            finally
            {
                readOperation = default;
            }
            if (n == 0)
            {
                engineInboundDone = true;
                EngineAcknowledgeCloseNotify();
                return produced;
            }
            @out.WriteBytes(readBuffer.AsSpan(0, n));
            produced += n;
            EngineStartRead();
        }
        return produced;
    }

    // The JDK SSLEngine answers a received close_notify by itself below TLS 1.3 (TransportContext.passiveInboundClose:
    // "For TLS 1.2 and prior version, it is required to respond with a close_notify alert of its own", unless its
    // outbound is closed already,
    // https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/sun/security/ssl/TransportContext.java;
    // RFC 5246 7.2.1: "The other party MUST respond with a close_notify alert of its own",
    // https://www.rfc-editor.org/rfc/rfc5246.txt), and SslHandler relies on it (unwrap's NEED_WRAP after CLOSED, tested by
    // CloseNotifyTest and ParameterizedSslHandlerTest.testCloseNotify). SslStream does not answer (it writes 0 bytes,
    // measured), so the engine closes its outbound side here, which writes the close_notify that the following wrap
    // drains. TLS 1.3 only closes the inbound side, as the JDK engine does.
    private void EngineAcknowledgeCloseNotify()
    {
        if (engineOutboundDone || engine.SslProtocol == System.Security.Authentication.SslProtocols.Tls13)
        {
            return;
        }
        EngineCloseOutbound();
    }

    // Ends the outstanding read before a renegotiation (cancelling its pending read of the mediation stream).
    private void EngineCancelRead()
    {
        if (!readOutstanding || readOperation.IsCompleted || readCancellation == null)
        {
            return;
        }
        CancellationTokenSource cancellation = readCancellation;
        readCancellation = null;
        RunEngine(cancellation, static c =>
        {
            c.Cancel();
            return true;
        });
        if (readOperation.IsCompleted)
        {
            readOutstanding = false;
            try
            {
                readOperation.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // The read ended without data.
            }
            catch (Exception e)
            {
                engineFailure ??= ToSSLException(e);
            }
            finally
            {
                readOperation = default;
            }
        }
        cancellation.Dispose();
    }

    private Exception EngineCheckFailure()
    {
        if (engineFailureReported || engineDisposed)
        {
            return null;
        }
        if (engineFailure != null)
        {
            return engineFailure;
        }
        if (handshakeOperation != null && (handshakeOperation.IsFaulted || handshakeOperation.IsCanceled))
        {
            // Any exception of the handshake (an AuthenticationException, or what a validation or selection callback
            // threw, which SslStream passes on unchanged) becomes the SSLHandshakeException.
            engineFailure = handshakeOperation.IsFaulted ? ToSSLException(handshakeOperation.Exception.InnerException) :
                    new AuthenticationException("SslStream authentication was cancelled");
            return engineFailure;
        }
        return null;
    }

    private void EngineThrowIfFailed()
    {
        Exception failure = EngineCheckFailure();
        if (failure != null)
        {
            engineFailureReported = true;
            engineClosed = true;
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    // As SSLHandshakeException.initCause(e) / SSLException(cause): a non-SSL exception becomes the inner exception.
    private static Exception ToSSLException(Exception e)
    {
        return e is AuthenticationException ? e : new AuthenticationException(e.Message, e);
    }

    // ReferenceCountUtil.release(engine): SslStream.Dispose() also disposes the mediation stream, which ends the pending
    // read of SslStream.
    private void EngineDispose()
    {
        if (engineDisposed)
        {
            return;
        }
        engineDisposed = true;
        engineClosed = true;
        try
        {
            RunEngine(this, static h =>
            {
                h.engine.Dispose();
                return true;
            });
        }
        catch (Exception e)
        {
            logger.Debug("SslStream.Dispose() raised an exception.", e);
        }
        EngineObserveCompleted();
        readCancellation?.Dispose();
        readCancellation = null;
    }

    // Consumes the results of completed operations after disposal, so that no failure stays unobserved.
    private void EngineObserveCompleted()
    {
        if (readOutstanding && readOperation.IsCompleted)
        {
            readOutstanding = false;
            try
            {
                readOperation.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // The disposed SslStream ends its read with an exception.
            }
            readOperation = default;
        }
        EngineWritePending();
    }

    // Called by the mediation stream and the SslStream operations whenever SslStream makes progress. Inside an engine
    // call the caller inspects the state itself; otherwise SslStream went on asynchronously, and the waiting
    // SslTasksRunner (NEED_TASK) or a new resume picks up the progress on the event loop.
    private void OnEngineProgress()
    {
        if (engineThread == Environment.CurrentManagedThreadId)
        {
            return;
        }
        if (engineDisposed)
        {
            EngineObserveCompleted();
            return;
        }
        SslTasksRunner runner = Interlocked.Exchange(ref waitingTaskRunner, null);
        if (runner == null)
        {
            if (ctx == null || handshakeOperation == null || Interlocked.Exchange(ref resumeScheduled, 1) != 0)
            {
                return;
            }
            runner = sslTaskRunnerForUnwrap;
        }
        runner.Run();
    }

    // The stream that SslStream reads and writes: one whole TLS record per Push, completing a pending read inline
    // (ManualResetValueTaskSourceCore without RunContinuationsAsynchronously invokes the continuation directly when no
    // context was captured,
    // https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Sources/ManualResetValueTaskSourceCore.cs;
    // "Continuations may run asynchronously if this is false, but they'll never run synchronously if this is true",
    // https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.sources.manualresetvaluetasksourcecore-1.runcontinuationsasynchronously?view=net-10.0),
    // and collecting the records SslStream writes until DrainOutput. A zero-byte read waits for data without consuming
    // it. Inside Netty.NET.Handler.* the simple name Stream is the Netty.NET.Handler.Stream namespace, hence
    // System.IO.Stream.
    private sealed class MediationStream : System.IO.Stream, IValueTaskSource<int>
    {
        private const int INITIAL_OUTPUT_CAPACITY = 4096;
        private const int RETAINED_OUTPUT_CAPACITY = 64 * 1024;

        private readonly SslHandler handler;
        private readonly object gate = new object();
        private byte[] input = Array.Empty<byte>();
        private int inputOffset;
        private int inputCount;
        private byte[] output = Array.Empty<byte>();
        private int outputCount;
        private ManualResetValueTaskSourceCore<int> readSource;
        private Memory<byte> pendingReadBuffer;
        private bool readPending;
        private CancellationTokenRegistration readRegistration;
        private bool disposed;

        internal MediationStream(SslHandler handler)
        {
            this.handler = handler;
        }

        internal bool IsReadPending()
        {
            lock (gate)
            {
                return readPending;
            }
        }

        internal bool HasOutput()
        {
            lock (gate)
            {
                return outputCount > 0;
            }
        }

        internal void Push(ByteBuf buffer, int index, int length)
        {
            bool complete = false;
            int n = 0;
            CancellationTokenRegistration registration = default;
            lock (gate)
            {
                if (readPending)
                {
                    n = Math.Min(length, pendingReadBuffer.Length);
                    buffer.GetBytes(index, pendingReadBuffer.Span.Slice(0, n));
                    index += n;
                    length -= n;
                    readPending = false;
                    pendingReadBuffer = default;
                    registration = readRegistration;
                    readRegistration = default;
                    complete = true;
                }
                if (length > 0)
                {
                    if (inputOffset + inputCount + length > input.Length)
                    {
                        byte[] grown = inputCount + length > input.Length ?
                                new byte[Math.Max(input.Length * 2, inputCount + length)] : input;
                        System.Buffer.BlockCopy(input, inputOffset, grown, 0, inputCount);
                        input = grown;
                        inputOffset = 0;
                    }
                    buffer.GetBytes(index, input.AsSpan(inputOffset + inputCount, length));
                    inputCount += length;
                }
            }
            if (complete)
            {
                registration.Unregister();
                readSource.SetResult(n);
            }
        }

        internal int DrainOutput(ByteBuf @out)
        {
            lock (gate)
            {
                int n = outputCount;
                if (n == 0)
                {
                    return 0;
                }
                @out.WriteBytes(output.AsSpan(0, n));
                outputCount = 0;
                if (output.Length > RETAINED_OUTPUT_CAPACITY)
                {
                    output = Array.Empty<byte>();
                }
                return n;
            }
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            short version;
            lock (gate)
            {
                if (disposed)
                {
                    return ValueTask.FromException<int>(new ObjectDisposedException(nameof(MediationStream)));
                }
                if (inputCount > 0)
                {
                    int n = Math.Min(buffer.Length, inputCount);
                    input.AsSpan(inputOffset, n).CopyTo(buffer.Span);
                    inputOffset += n;
                    inputCount -= n;
                    if (inputCount == 0)
                    {
                        inputOffset = 0;
                    }
                    return new ValueTask<int>(n);
                }
                readSource.Reset();
                version = readSource.Version;
                pendingReadBuffer = buffer;
                readPending = true;
                if (cancellationToken.CanBeCanceled)
                {
                    readRegistration = cancellationToken.UnsafeRegister(
                            static (s, token) => ((MediationStream) s).CancelPendingRead(token), this);
                }
            }
            handler.OnEngineProgress();
            return new ValueTask<int>(this, version);
        }

        private void CancelPendingRead(CancellationToken token)
        {
            lock (gate)
            {
                if (!readPending)
                {
                    return;
                }
                readPending = false;
                pendingReadBuffer = default;
                readRegistration = default;
            }
            readSource.SetException(new OperationCanceledException(token));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        // SslHandler drives SslStream through its asynchronous members only, which read with ReadAsync.
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (gate)
            {
                if (outputCount + buffer.Length > output.Length)
                {
                    byte[] grown = new byte[Math.Max(Math.Max(output.Length * 2, INITIAL_OUTPUT_CAPACITY),
                            outputCount + buffer.Length)];
                    System.Buffer.BlockCopy(output, 0, grown, 0, outputCount);
                    output = grown;
                }
                buffer.CopyTo(output.AsSpan(outputCount));
                outputCount += buffer.Length;
            }
            handler.OnEngineProgress();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(buffer.AsSpan(offset, count));
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override bool CanRead => true;

        public override bool CanWrite => true;

        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            bool complete = false;
            CancellationTokenRegistration registration = default;
            lock (gate)
            {
                if (!disposed)
                {
                    disposed = true;
                    if (readPending)
                    {
                        readPending = false;
                        pendingReadBuffer = default;
                        registration = readRegistration;
                        readRegistration = default;
                        complete = true;
                    }
                }
            }
            if (complete)
            {
                registration.Unregister();
                readSource.SetException(new ObjectDisposedException(nameof(MediationStream)));
            }
            base.Dispose(disposing);
        }

        public int GetResult(short token)
        {
            return readSource.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return readSource.GetStatus(token);
        }

        public void OnCompleted(Action<object> continuation, object state, short token,
                                ValueTaskSourceOnCompletedFlags flags)
        {
            readSource.OnCompleted(continuation, state, token, flags);
        }
    }
}
