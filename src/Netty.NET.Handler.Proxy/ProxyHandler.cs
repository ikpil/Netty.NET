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
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Proxy;

/**
 * A common abstraction for protocols that establish blind forwarding proxy tunnels.
 */
// CLR: SocketAddress is EndPoint, an unresolved InetSocketAddress is DnsEndPoint and a resolved one IPEndPoint
// (docs/handler-porting.md, Addresses). The ChannelPromise of connect/write is the Task that ConnectAsync/WriteAsync
// return (docs/codec-base-porting.md, "Outbound operations"): a write queued before the handshake finishes returns the
// consumer Task of PendingWriteQueue.Add. Future<Channel> connectFuture() is Task<IChannel>; LazyChannelPromise (a
// DefaultPromise on ctx.executor() that throws IllegalStateException while the handler is not added) is a
// TaskCompletionSource<IChannel> whose continuations run asynchronously, as SslHandler maps its LazyChannelPromise: a
// Task has no executor. The write listener is a continuation on Common's EventExecutorTaskScheduler over ctx.Executor
// (at once on the event loop, otherwise through Execute: DefaultPromise.notifyListeners), and the connect timeout's
// Future<?> is the CancellationTokenSource of ctx.Executor.ScheduleAsync (cancel(false) is Cancel()), as
// WriteTimeoutHandler does. Final Java methods are sealed overrides or non-virtual; Java's checked "throws" clauses
// have no counterpart.
public abstract class ProxyHandler : ChannelDuplexHandler
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(ProxyHandler));

    /**
     * The default connect timeout: 10 seconds.
     */
    private const long DEFAULT_CONNECT_TIMEOUT_MILLIS = 10000;

    /**
     * A string that signifies 'no authentication' or 'anonymous'.
     */
    internal const string AUTH_NONE = "none";

    private readonly EndPoint proxyAddress;
    private volatile EndPoint destinationAddress;
    // CLR: C# has no volatile long; the field is read and written through Volatile.Read/Write
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile?view=net-10.0), as in SslHandler.
    private long connectTimeoutMillis = DEFAULT_CONNECT_TIMEOUT_MILLIS;

    private volatile IChannelHandlerContext ctx;
    private PendingWriteQueue pendingWrites;
    private bool finished;
    private bool suppressChannelReadComplete;
    private bool flushedPrematurely;
    private readonly TaskCompletionSource<IChannel> connectPromise = NewChannelPromise();
    private CancellationTokenSource connectTimeoutFuture;
    // CLR: the listener reads this handler, so it is assigned in the constructor (a C# field initializer cannot refer to
    // the instance); future.isSuccess() is IsCompletedSuccessfully.
    private readonly Action<Task> writeListener;

    // CLR: the TaskScheduler of writeListener (see the class comment).
    private readonly EventExecutorTaskScheduler listenerScheduler;

    protected ProxyHandler(EndPoint proxyAddress)
    {
        this.proxyAddress = ObjectUtil.CheckNotNull(proxyAddress, "proxyAddress");
        writeListener = future =>
        {
            if (!future.IsCompletedSuccessfully)
            {
                SetConnectFailure(Cause(future));
            }
        };
        listenerScheduler = new EventExecutorTaskScheduler(() => ctx.Executor);
    }

    /**
     * Returns the name of the proxy protocol in use.
     */
    public abstract string Protocol();

    /**
     * Returns the name of the authentication scheme in use.
     */
    public abstract string AuthScheme();

    /**
     * Returns the address of the proxy server.
     */
    // CLR: Java infers T from the assignment target; a C# caller names it, and the cast is checked (see
    // ProxyConnectionEvent).
    public T ProxyAddress<T>() where T : EndPoint
    {
        return (T) proxyAddress;
    }

    /**
     * Returns the address of the destination to connect to via the proxy server.
     */
    public T DestinationAddress<T>() where T : EndPoint
    {
        return (T) destinationAddress;
    }

    /**
     * Returns {@code true} if and only if the connection to the destination has been established successfully.
     */
    public bool IsConnected()
    {
        return connectPromise.Task.IsCompletedSuccessfully;
    }

    /**
     * Returns a {@link Future} that is notified when the connection to the destination has been established
     * or the connection attempt has failed.
     */
    public Task<IChannel> ConnectFuture()
    {
        return connectPromise.Task;
    }

    /**
     * Returns the connect timeout in millis.  If the connection attempt to the destination does not finish within
     * the timeout, the connection attempt will be failed.
     */
    public long ConnectTimeoutMillis()
    {
        return Volatile.Read(ref connectTimeoutMillis);
    }

    /**
     * Sets the connect timeout in millis.  If the connection attempt to the destination does not finish within
     * the timeout, the connection attempt will be failed.
     */
    public void SetConnectTimeoutMillis(long connectTimeoutMillis)
    {
        if (connectTimeoutMillis <= 0)
        {
            connectTimeoutMillis = 0;
        }

        Volatile.Write(ref this.connectTimeoutMillis, connectTimeoutMillis);
    }

    public sealed override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
        AddCodec(ctx);

        if (ctx.Channel.IsActive)
        {
            // channelActive() event has been fired already, which means this.channelActive() will
            // not be invoked. We have to initialize here instead.
            SendInitialMessage(ctx);
        }
        else
        {
            // channelActive() event has not been fired yet.  this.channelOpen() will be invoked
            // and initialization will occur there.
        }
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        // The handler can be removed before the connection to the destination was established, for example because
        // the connection to the proxy server failed. The channel never became active in that case, so
        // channelInactive(...) is not called. Fail what is still pending, as otherwise the queued writes and
        // connectFuture() would never be completed and the queued messages would leak.
        // If the handshake already finished (the handler may be removed from a ProxyConnectionEvent listener while
        // setConnectSuccess() runs), setConnectSuccess() or setConnectFailure() completes everything.
        if (!finished)
        {
            Exception cause = new ProxyConnectException(
                    ExceptionMessage("handler removed before the connection was established"));
            FailPendingWrites(cause);
            connectPromise.TrySetException(cause);
            CancelConnectTimeoutFuture();
        }
    }

    /**
     * Adds the codec handlers required to communicate with the proxy server.
     */
    protected abstract void AddCodec(IChannelHandlerContext ctx);

    /**
     * Removes the encoders added in {@link #addCodec(ChannelHandlerContext)}.
     */
    protected abstract void RemoveEncoder(IChannelHandlerContext ctx);

    /**
     * Removes the decoders added in {@link #addCodec(ChannelHandlerContext)}.
     */
    protected abstract void RemoveDecoder(IChannelHandlerContext ctx);

    // CLR: promise.setFailure(new ConnectionPendingException()) is a Task faulted with
    // SocketException(SocketError.AlreadyInProgress), which stands for ConnectionPendingException in the Transport
    // (src/Netty.NET.Transport/Channels/Local/LocalChannel.cs, Nio/AbstractNioChannel.cs).
    public sealed override Task ConnectAsync(
            IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress,
            CancellationToken cancellationToken)
    {
        if (destinationAddress != null)
        {
            return Task.FromException(new SocketException((int) SocketError.AlreadyInProgress));
        }

        destinationAddress = remoteAddress;
        return ctx.ConnectAsync(proxyAddress, localAddress, cancellationToken);
    }

    public sealed override void ChannelActive(IChannelHandlerContext ctx)
    {
        SendInitialMessage(ctx);
        ctx.FireChannelActive();
    }

    /**
     * Sends the initial message to be sent to the proxy server. This method also starts a timeout task which marks
     * the {@link #connectPromise} as failure if the connection attempt does not success within the timeout.
     */
    // CLR: schedule(Runnable, connectTimeoutMillis, MILLISECONDS) is ScheduleAsync with TimeSpan.FromMilliseconds, as
    // SslHandler schedules its handshake timeout.
    private void SendInitialMessage(IChannelHandlerContext ctx)
    {
        long connectTimeoutMillis = Volatile.Read(ref this.connectTimeoutMillis);
        if (connectTimeoutMillis > 0)
        {
            connectTimeoutFuture = new CancellationTokenSource();
            _ = ctx.Executor.ScheduleAsync(() =>
            {
                if (!connectPromise.Task.IsCompleted)
                {
                    SetConnectFailure(new ProxyConnectException(ExceptionMessage("timeout")));
                }
            }, TimeSpan.FromMilliseconds(connectTimeoutMillis), connectTimeoutFuture.Token);
        }

        object initialMessage = NewInitialMessage(ctx);
        if (initialMessage != null)
        {
            SendToProxyServer(initialMessage);
        }

        ReadIfNeeded(ctx);
    }

    /**
     * Returns a new message that is sent at first time when the connection to the proxy server has been established.
     *
     * @return the initial message, or {@code null} if the proxy server is expected to send the first message instead
     */
    protected abstract object NewInitialMessage(IChannelHandlerContext ctx);

    /**
     * Sends the specified message to the proxy server.  Use this method to send a response to the proxy server in
     * {@link #handleResponse(ChannelHandlerContext, Object)}.
     */
    protected void SendToProxyServer(object msg)
    {
        _ = ctx.WriteAndFlushAsync(msg).ContinueWith(writeListener, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, listenerScheduler);
    }

    public sealed override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (finished)
        {
            ctx.FireChannelInactive();
        }
        else
        {
            // Disconnected before connected to the destination.
            SetConnectFailure(new ProxyConnectException(ExceptionMessage("disconnected")));
        }
    }

    public sealed override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (finished)
        {
            ctx.FireExceptionCaught(cause);
        }
        else
        {
            // Exception was raised before the connection attempt is finished.
            SetConnectFailure(cause);
        }
    }

    // CLR: catch (Throwable) is catch (Exception), which also catches the InvalidOperationException that stands for
    // java.lang.Error (Socks5ProxyHandler).
    public sealed override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (finished)
        {
            // Received a message after the connection has been established; pass through.
            suppressChannelReadComplete = false;
            ctx.FireChannelRead(msg);
        }
        else
        {
            suppressChannelReadComplete = true;
            Exception cause = null;
            try
            {
                bool done = HandleResponse(ctx, msg);
                if (done)
                {
                    SetConnectSuccess();
                }
            }
            catch (Exception t)
            {
                cause = t;
            }
            finally
            {
                ReferenceCountUtil.Release(msg);
                if (cause != null)
                {
                    SetConnectFailure(cause);
                }
            }
        }
    }

    /**
     * Handles the message received from the proxy server.
     *
     * @return {@code true} if the connection to the destination has been established,
     *         {@code false} if the connection to the destination has not been established and more messages are
     *         expected from the proxy server
     */
    protected abstract bool HandleResponse(IChannelHandlerContext ctx, object response);

    // CLR: Java's boolean &= evaluates its right operand, as C#'s bool &= does, so both codecs are always removed.
    private void SetConnectSuccess()
    {
        finished = true;
        CancelConnectTimeoutFuture();

        if (!connectPromise.Task.IsCompleted)
        {
            bool removedCodec = true;

            removedCodec &= SafeRemoveEncoder();

            ctx.FireUserEventTriggered(
                    new ProxyConnectionEvent(Protocol(), AuthScheme(), proxyAddress, destinationAddress));

            removedCodec &= SafeRemoveDecoder();

            if (removedCodec)
            {
                WritePendingWrites();

                if (flushedPrematurely)
                {
                    ctx.Flush();
                }
                connectPromise.TrySetResult(ctx.Channel);
            }
            else
            {
                // We are at inconsistent state because we failed to remove all codec handlers.
                Exception cause = new ProxyConnectException(
                        "failed to remove all codec handlers added by the proxy handler; bug?");
                FailPendingWritesAndClose(cause);
            }
        }
    }

    private bool SafeRemoveDecoder()
    {
        try
        {
            RemoveDecoder(ctx);
            return true;
        }
        catch (Exception e)
        {
            logger.Warn("Failed to remove proxy decoders:", e);
        }

        return false;
    }

    private bool SafeRemoveEncoder()
    {
        try
        {
            RemoveEncoder(ctx);
            return true;
        }
        catch (Exception e)
        {
            logger.Warn("Failed to remove proxy encoders:", e);
        }

        return false;
    }

    // CLR: cause.toString() is Common's ThrowableUtil.ThrowableToString (the class name, ": " and the message), because
    // Exception.ToString() also appends the inner exception and the stack trace (owner decision 2026-11-24).
    private void SetConnectFailure(Exception cause)
    {
        finished = true;
        CancelConnectTimeoutFuture();

        if (!connectPromise.Task.IsCompleted)
        {

            if (!(cause is ProxyConnectException))
            {
                cause = new ProxyConnectException(
                        ExceptionMessage(ThrowableUtil.ThrowableToString(cause)), cause);
            }

            SafeRemoveDecoder();
            SafeRemoveEncoder();
            FailPendingWritesAndClose(cause);
        }
    }

    private void FailPendingWritesAndClose(Exception cause)
    {
        FailPendingWrites(cause);
        connectPromise.TrySetException(cause);
        ctx.FireExceptionCaught(cause);
        _ = ctx.CloseAsync();
    }

    private void CancelConnectTimeoutFuture()
    {
        if (connectTimeoutFuture != null)
        {
            connectTimeoutFuture.Cancel();
            connectTimeoutFuture = null;
        }
    }

    /**
     * Decorates the specified exception message with the common information such as the current protocol,
     * authentication scheme, proxy address, and destination address.
     */
    // CLR: StringBuilder.append(Object) appends "null" for a null reference, while the C# Append(object) appends
    // nothing (https://learn.microsoft.com/en-us/dotnet/api/system.text.stringbuilder.append?view=net-10.0), so a
    // destination that is not set yet is written as "null" explicitly. An address prints as EndPoint.ToString().
    protected string ExceptionMessage(string msg)
    {
        if (msg == null)
        {
            msg = "";
        }

        StringBuilder buf = new StringBuilder(128 + msg.Length)
            .Append(Protocol())
            .Append(", ")
            .Append(AuthScheme())
            .Append(", ")
            .Append(proxyAddress)
            .Append(" => ")
            .Append((object) destinationAddress ?? "null");
        if (msg.Length != 0)
        {
            buf.Append(", ").Append(msg);
        }

        return buf.ToString();
    }

    public sealed override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        if (suppressChannelReadComplete)
        {
            suppressChannelReadComplete = false;

            ReadIfNeeded(ctx);
        }
        else
        {
            ctx.FireChannelReadComplete();
        }
    }

    public sealed override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (finished)
        {
            WritePendingWrites();
            return ctx.WriteAsync(msg, cancellationToken);
        }
        else
        {
            return AddPendingWrite(ctx, msg, cancellationToken);
        }
    }

    public sealed override void Flush(IChannelHandlerContext ctx)
    {
        if (finished)
        {
            WritePendingWrites();
            ctx.Flush();
        }
        else
        {
            flushedPrematurely = true;
        }
    }

    private static void ReadIfNeeded(IChannelHandlerContext ctx)
    {
        if (!ctx.Channel.Config.IsAutoRead())
        {
            ctx.Read();
        }
    }

    // CLR: removeAndWriteAll() returns the aggregate write Task, which the Java caller ignores as well.
    private void WritePendingWrites()
    {
        if (pendingWrites != null)
        {
            _ = pendingWrites.RemoveAndWriteAllAsync();
            pendingWrites = null;
        }
    }

    private void FailPendingWrites(Exception cause)
    {
        if (pendingWrites != null)
        {
            pendingWrites.RemoveAndFailAll(cause);
            pendingWrites = null;
        }
    }

    // CLR: pendingWrites.add(msg, promise) is Add(msg, cancellationToken), whose consumer Task is the write's result.
    private Task AddPendingWrite(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        PendingWriteQueue pendingWrites = this.pendingWrites;
        if (pendingWrites == null)
        {
            this.pendingWrites = pendingWrites = new PendingWriteQueue(ctx);
        }
        return pendingWrites.Add(msg, cancellationToken);
    }

    // ---- CLR-only helpers -----------------------------------------------------------------------------------------

    // CLR: LazyChannelPromise (see the class comment).
    private static TaskCompletionSource<IChannel> NewChannelPromise()
    {
        return new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // CLR: future.cause(): the exception of a faulted Task (the first inner exception of its AggregateException) and a
    // TaskCanceledException for a canceled Task (Java's CancellationException), as in codec-http2's Http2CodecUtil.
    private static Exception Cause(Task future)
    {
        if (future.IsFaulted)
        {
            return future.Exception.InnerException;
        }
        return new TaskCanceledException(future);
    }

    // CLR: the subclasses' "InetSocketAddress raddr = destinationAddress()" casts the destination: a resolved address is
    // an IPEndPoint, an unresolved one a DnsEndPoint, and any other address fails the cast. Java throws
    // ClassCastException there, which is InvalidCastException; a null destination stays null, as in Java.
    internal static EndPoint CastToInetSocketAddress(EndPoint address)
    {
        if (address == null || address is IPEndPoint || address is DnsEndPoint)
        {
            return address;
        }
        throw new InvalidCastException(string.Format(CultureInfo.InvariantCulture,
                "Unable to cast object of type '{0}' to type 'InetSocketAddress'.", address.GetType().FullName));
    }

    // CLR: InetAddress.getHostAddress(), the text the SOCKS handlers put in their requests. An Inet4Address prints the
    // dotted quad, as IPAddress.ToString() does; an Inet6Address prints the eight 16-bit groups as Integer.toHexString,
    // separated by ':' and not compressed, followed by '%' and the scope id when one was set, while IPAddress.ToString()
    // compresses with "::". The JDK text is built as handler's IpSubnetFilterRule.GetHostAddress builds it
    // (src/Netty.NET.Handler/IpFilter/IpSubnetFilterRule.cs, with the OpenJDK and Microsoft Learn sources); a scope id
    // of 0 counts as none, because IPAddress has no "scope set" flag.
    internal static string GetHostAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }
        byte[] src = address.GetAddressBytes();
        StringBuilder sb = new StringBuilder(39);
        for (int i = 0; i < 16 / 2; i++)
        {
            sb.Append((((src[i << 1] << 8) & 0xff00) | (src[(i << 1) + 1] & 0xff)).ToString("x", CultureInfo.InvariantCulture));
            if (i < 16 / 2 - 1)
            {
                sb.Append(':');
            }
        }
        if (address.ScopeId != 0)
        {
            sb.Append('%').Append(address.ScopeId.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
