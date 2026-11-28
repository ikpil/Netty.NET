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
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

// CLR: the package-private abstract class is internal. Promise<AddressedEnvelope<DnsResponse, InetSocketAddress>> is a
// TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> (handler-porting.md, "Promise, ChannelPromise,
// Future": TaskCompletionSource/Task); AddressedEnvelope<DnsResponse, InetSocketAddress> is the covariant
// IAddressedEnvelope<IDnsResponse, IPEndPoint> of codec-dns, and the name server of a query is resolved, so
// InetSocketAddress is IPEndPoint (docs/handler-porting.md, Addresses). The listeners of the promise, the write and the
// TCP connect are continuations on Common's EventExecutorTaskScheduler over channel.EventLoop, which runs them at once
// on the event loop and otherwise through the executor, the DefaultPromise.notifyListeners() timing
// (src/Netty.NET.Common/Concurrent/EventExecutorTaskScheduler.cs); a listener that throws faults its continuation Task,
// where DefaultPromise logs "An exception was thrown by ...operationComplete()"
// (https://github.com/netty/netty/blob/64cc10f38ea5f5bd7eae48507817c66680d0afdc/common/src/main/java/io/netty/util/concurrent/DefaultPromise.java).
// isDone() is Task.IsCompleted, tryFailure/trySuccess are TrySetException/TrySetResult and future.cause() is
// DnsNameResolver.Cause (a canceled Task gives a TaskCanceledException, an OperationCanceledException like
// CancellationException). ChannelFuture/ChannelPromise are the Task of the channel operation and channel.newPromise() +
// write(query, promise) is the Task that WriteAsync/WriteAndFlushAsync return (docs/transport-porting.md);
// channel.newFailedFuture(e) is Task.FromException(e). The ScheduledFuture of channel.eventLoop().schedule(...) is the
// CancellationTokenSource of EventLoop.ScheduleAsync and cancel(false) is Cancel(), as handler's WriteTimeoutHandler and
// SslHandler map it (docs/handler-porting.md); the volatile Future<?> is a volatile field. The anonymous Runnables and
// listeners are lambdas over the Action/continuation delegates these APIs take; the anonymous
// AbstractDnsOptPseudoRrRecord and ChannelInboundHandlerAdapter are the nested OptPseudoRrRecord and TcpResponseHandler.
// IllegalStateException is InvalidOperationException, Throwable is Exception, assert is Debug.Assert and
// getClass().getSimpleName() is GetType().Name. Numbers in messages use the invariant culture.
internal abstract class DnsQueryContext
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DnsQueryContext));
    private static readonly long ID_REUSE_ON_TIMEOUT_DELAY_MILLIS;

    // CLR: the static initializer is the static constructor (it runs after the static field initializers).
    static DnsQueryContext()
    {
        ID_REUSE_ON_TIMEOUT_DELAY_MILLIS =
                SystemPropertyUtil.GetLong("io.netty.resolver.dns.idReuseOnTimeoutDelayMillis", 10000);
        logger.Debug("-Dio.netty.resolver.dns.idReuseOnTimeoutDelayMillis: {}", ID_REUSE_ON_TIMEOUT_DELAY_MILLIS);
    }

    private static readonly TcpDnsQueryEncoder TCP_ENCODER = new TcpDnsQueryEncoder();

    private readonly IChannel channel;
    private readonly IPEndPoint nameServerAddr;
    private readonly DnsQueryContextManager queryContextManager;
    private readonly IDnsQueryLifecycleObserver queryLifecycleObserver;
    private readonly TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> promise;

    private readonly IDnsQuestion question;
    private readonly IDnsRecord[] additionals;
    private readonly IDnsRecord optResource;

    private readonly bool recursionDesired;

    private readonly Bootstrap socketBootstrap;

    private readonly bool retryWithTcpOnTimeout;
    private readonly long queryTimeoutMillis;

    private volatile CancellationTokenSource timeoutFuture;

    private int id = int.MinValue;

    // CLR: the scheduler of the listeners (see the class comment).
    private readonly EventExecutorTaskScheduler listenerScheduler;

    internal DnsQueryContext(IChannel channel,
                    IPEndPoint nameServerAddr,
                    DnsQueryContextManager queryContextManager,
                    IDnsQueryLifecycleObserver queryLifecycleObserver,
                    int maxPayLoadSize,
                    bool recursionDesired,
                    long queryTimeoutMillis,
                    IDnsQuestion question,
                    IDnsRecord[] additionals,
                    TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> promise,
                    Bootstrap socketBootstrap,
                    bool retryWithTcpOnTimeout)
    {
        this.channel = ObjectUtil.CheckNotNull(channel, "channel");
        this.queryContextManager = ObjectUtil.CheckNotNull(queryContextManager, "queryContextManager");
        this.queryLifecycleObserver = ObjectUtil.CheckNotNull(queryLifecycleObserver, "queryLifecycleObserver");
        this.nameServerAddr = ObjectUtil.CheckNotNull(nameServerAddr, "nameServerAddr");
        this.question = ObjectUtil.CheckNotNull(question, "question");
        this.additionals = ObjectUtil.CheckNotNull(additionals, "additionals");
        this.promise = ObjectUtil.CheckNotNull(promise, "promise");
        this.recursionDesired = recursionDesired;
        this.queryTimeoutMillis = queryTimeoutMillis;
        this.socketBootstrap = socketBootstrap;
        this.retryWithTcpOnTimeout = retryWithTcpOnTimeout;
        listenerScheduler = new EventExecutorTaskScheduler(() => this.channel.EventLoop);

        if (maxPayLoadSize > 0 &&
                // Only add the extra OPT record if there is not already one. This is required as only one is allowed
                // as per RFC:
                //  - https://datatracker.ietf.org/doc/html/rfc6891#section-6.1.1
                !HasOptRecord(additionals))
        {
            optResource = new OptPseudoRrRecord(maxPayLoadSize);
        }
        else
        {
            optResource = null;
        }
    }

    // CLR: the anonymous AbstractDnsOptPseudoRrRecord(maxPayLoadSize, 0, 0) subclass.
    private sealed class OptPseudoRrRecord : AbstractDnsOptPseudoRrRecord
    {
        internal OptPseudoRrRecord(int maxPayLoadSize) : base(maxPayLoadSize, 0, 0)
        {
        }
        // We may want to remove this in the future and let the user just specify the opt record in the query.
    }

    private static bool HasOptRecord(IDnsRecord[] additionals)
    {
        if (additionals != null && additionals.Length > 0)
        {
            foreach (IDnsRecord additional in additionals)
            {
                if (additional.Type() == DnsRecordType.OPT)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /**
     * Returns {@code true} if the query was completed already.
     *
     * @return {@code true} if done.
     */
    internal bool IsDone()
    {
        return promise.Task.IsCompleted;
    }

    /**
     * Returns the {@link DnsQuestion} that will be written as part of the {@link DnsQuery}.
     *
     * @return the question.
     */
    internal IDnsQuestion Question()
    {
        return question;
    }

    /**
     * Creates and returns a new {@link DnsQuery}.
     *
     * @param id                the transaction id to use.
     * @param nameServerAddr    the nameserver to which the query will be send.
     * @return                  the new query.
     */
    protected abstract IDnsQuery NewQuery(int id, IPEndPoint nameServerAddr);

    /**
     * Returns the protocol that is used for the query.
     *
     * @return  the protocol.
     */
    protected abstract string Protocol();

    /**
     * Write the query and return the {@link ChannelFuture} that is completed once the write completes.
     *
     * @param flush                 {@code true} if {@link Channel#flush()} should be called as well.
     */
    internal void WriteQuery(bool flush)
    {
        Debug.Assert(id == int.MinValue, GetType().Name +
                ".writeQuery(...) can only be executed once.");

        if ((id = queryContextManager.Add(nameServerAddr, this)) == -1)
        {
            // We did exhaust the id space, fail the query
            InvalidOperationException e = new InvalidOperationException("query ID space exhausted: " + Question());
            FinishFailure("failed to send a query via " + Protocol(), e, false);
            queryLifecycleObserver.QueryWritten(nameServerAddr, Task.FromException(e));
            return;
        }

        // Ensure we remove the id from the QueryContextManager once the query completes.
        _ = promise.Task.ContinueWith(future =>
        {
            // Cancel the timeout task.
            CancellationTokenSource timeoutFuture = this.timeoutFuture;
            if (timeoutFuture != null)
            {
                this.timeoutFuture = null;
                timeoutFuture.Cancel();
            }

            Exception cause = DnsNameResolver.Cause(future);
            if (cause is DnsNameResolverTimeoutException || cause is OperationCanceledException)
            {
                // This query was failed due a timeout or cancellation. Let's delay the removal of the id to reduce
                // the risk of reusing the same id again while the remote nameserver might send the response after
                // the timeout.
                _ = channel.EventLoop.ScheduleAsync(() => RemoveFromContextManager(nameServerAddr),
                        TimeSpan.FromMilliseconds(ID_REUSE_ON_TIMEOUT_DELAY_MILLIS));
            }
            else
            {
                // Remove the id from the manager as soon as the query completes. This may be because of success,
                // failure or cancellation
                RemoveFromContextManager(nameServerAddr);
            }
        }, CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
        IDnsQuestion question = Question();
        IDnsQuery query = NewQuery(id, nameServerAddr);

        query.SetRecursionDesired(recursionDesired);

        query.AddRecord(DnsSection.QUESTION, question);

        foreach (IDnsRecord record in additionals)
        {
            query.AddRecord(DnsSection.ADDITIONAL, record);
        }

        if (optResource != null)
        {
            query.AddRecord(DnsSection.ADDITIONAL, optResource);
        }

        if (logger.IsDebugEnabled())
        {
            logger.Debug("{} WRITE: {}, [{}: {}], {}",
                    channel, Protocol(), id, nameServerAddr, question);
        }

        Task f = SendQuery(query, flush);
        queryLifecycleObserver.QueryWritten(nameServerAddr, f);
    }

    private void RemoveFromContextManager(IPEndPoint nameServerAddr)
    {
        DnsQueryContext self = queryContextManager.Remove(nameServerAddr, id);

        Debug.Assert(self == this, "Removed DnsQueryContext is not the correct instance");
    }

    // CLR: channel.newPromise() passed to write(...) is the Task that the write returns (see the class comment).
    private Task SendQuery(IDnsQuery query, bool flush)
    {
        return WriteQuery(query, flush);
    }

    private Task WriteQuery(IDnsQuery query,
                            bool flush)
    {
        Task writeFuture = flush ? channel.WriteAndFlushAsync(query) :
                channel.WriteAsync(query);
        if (writeFuture.IsCompleted)
        {
            OnQueryWriteCompletion(queryTimeoutMillis, writeFuture);
        }
        else
        {
            _ = writeFuture.ContinueWith(future =>
                    OnQueryWriteCompletion(queryTimeoutMillis, future),
                    CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
        }

        return writeFuture;
    }

    private void OnQueryWriteCompletion(long queryTimeoutMillis,
                                        Task writeFuture)
    {
        if (!writeFuture.IsCompletedSuccessfully)
        {
            FinishFailure("failed to send a query '" + id.ToString(CultureInfo.InvariantCulture) + "' via " + Protocol(),
                    DnsNameResolver.Cause(writeFuture), false);
            return;
        }

        // Schedule a query timeout task if necessary.
        if (queryTimeoutMillis > 0)
        {
            CancellationTokenSource scheduled = new CancellationTokenSource();
            _ = channel.EventLoop.ScheduleAsync(() =>
            {
                if (promise.Task.IsCompleted)
                {
                    // Received a response before the query times out.
                    return;
                }

                FinishFailure("query '" + id.ToString(CultureInfo.InvariantCulture) + "' via " + Protocol() +
                        " timed out after " + queryTimeoutMillis.ToString(CultureInfo.InvariantCulture) +
                        " milliseconds", null, true);
            }, TimeSpan.FromMilliseconds(queryTimeoutMillis), scheduled.Token);
            timeoutFuture = scheduled;
        }
    }

    /**
     * Notifies the original {@link Promise} that the response for the query was received.
     * This method takes ownership of passed {@link AddressedEnvelope}.
     */
    internal void FinishSuccess(IAddressedEnvelope<IDnsResponse, IPEndPoint> envelope, bool truncated)
    {
        // Check if the response was not truncated or if a fallback to TCP is possible.
        if (!truncated || !RetryWithTcp(envelope))
        {
            IDnsResponse res = envelope.Content;
            if (res.Count(DnsSection.QUESTION) != 1)
            {
                logger.Warn("{} Received a DNS response with invalid number of questions. Expected: 1, found: {}",
                        channel, envelope);
            }
            else if (!Question().Equals(res.RecordAt<IDnsRecord>(DnsSection.QUESTION)))
            {
                logger.Warn("{} Received a mismatching DNS response. Expected: [{}], found: {}",
                        channel, Question(), envelope);
            }
            else if (TrySuccess(envelope))
            {
                return; // Ownership transferred, don't release
            }

            envelope.Release();
        }
    }

    private bool TrySuccess(IAddressedEnvelope<IDnsResponse, IPEndPoint> envelope)
    {
        return promise.TrySetResult(envelope);
    }

    /**
     * Notifies the original {@link Promise} that the query completes because of an failure.
     */
    internal bool FinishFailure(string message, Exception cause, bool timeout)
    {
        if (promise.Task.IsCompleted)
        {
            return false;
        }

        IDnsQuestion question = Question();

        StringBuilder buf = new StringBuilder(message.Length + 128);
        buf.Append('[')
           .Append(id.ToString(CultureInfo.InvariantCulture))
           .Append(": ")
           .Append(nameServerAddr)
           .Append("] ")
           .Append(question)
           .Append(' ')
           .Append(message)
           .Append(" (no stack trace available)");

        DnsNameResolverException e;

        if (timeout)
        {
            // This was caused by a timeout so use DnsNameResolverTimeoutException to allow the user to
            // handle it special (like retry the query).
            e = new DnsNameResolverTimeoutException(nameServerAddr, question, buf.ToString());
            if (retryWithTcpOnTimeout && RetryWithTcp(e))
            {
                // We did successfully retry with TCP.
                return false;
            }
        }
        else
        {
            e = new DnsNameResolverException(nameServerAddr, question, buf.ToString(), cause);
        }

        return promise.TrySetException(e);
    }

    /**
     * Retry the original query with TCP if possible.
     *
     * @param originalResult    the result of the original {@link DnsQueryContext}.
     * @return                  {@code true} if retry via TCP is supported and so the ownership of
     *                          {@code originalResult} was transferred, {@code false} otherwise.
     */
    private bool RetryWithTcp(object originalResult)
    {
        if (socketBootstrap == null)
        {
            return false;
        }

        _ = socketBootstrap.ConnectAsync(nameServerAddr).ContinueWith(future =>
        {
            if (!future.IsCompletedSuccessfully)
            {
                // CLR: a faulted Task<IChannel> carries no channel (docs/transport-porting.md: RegisterAsync/BindAsync/
                // ConnectAsync return a faulted Task<IChannel>, which has no channel accessor), so future.channel() is
                // logged as null.
                logger.Debug("{} Unable to fallback to TCP [{}: {}]",
                        null, id, nameServerAddr, DnsNameResolver.Cause(future));
                // TCP fallback failed, just use the truncated response or error.
                FinishOriginal(originalResult, future);
                return;
            }

            IChannel tcpCh = future.Result;
            TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> promise =
                    new TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
            TcpDnsQueryContext tcpCtx = new TcpDnsQueryContext(tcpCh,
                    (IPEndPoint) tcpCh.RemoteAddress, queryContextManager, queryLifecycleObserver, 0,
                    recursionDesired, queryTimeoutMillis, Question(), additionals, promise);
            tcpCh.Pipeline.AddLast(TCP_ENCODER);
            tcpCh.Pipeline.AddLast(new TcpDnsResponseDecoder());
            tcpCh.Pipeline.AddLast(new TcpResponseHandler(this, tcpCtx));

            _ = promise.Task.ContinueWith(future1 =>
            {
                if (future1.IsCompletedSuccessfully)
                {
                    FinishSuccess(future1.Result, false);
                    // Release the original result.
                    ReferenceCountUtil.Release(originalResult);
                }
                else
                {
                    // TCP fallback failed, just use the truncated response or error.
                    FinishOriginal(originalResult, future1);
                }

                _ = tcpCh.CloseAsync();
            }, CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
            tcpCtx.WriteQuery(true);
        }, CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
        return true;
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of retryWithTcp(...); it reads the enclosing context's fields.
    private sealed class TcpResponseHandler : ChannelInboundHandlerAdapter
    {
        private readonly DnsQueryContext context;
        private readonly TcpDnsQueryContext tcpCtx;

        internal TcpResponseHandler(DnsQueryContext context, TcpDnsQueryContext tcpCtx)
        {
            this.context = context;
            this.tcpCtx = tcpCtx;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            IChannel tcpCh = ctx.Channel;
            IDnsResponse response = (IDnsResponse) msg;
            int queryId = response.Id();

            if (logger.IsDebugEnabled())
            {
                logger.Debug("{} RECEIVED: TCP [{}: {}], {}", tcpCh, queryId,
                        tcpCh.RemoteAddress, response);
            }

            DnsQueryContext foundCtx = context.queryContextManager.Get(context.nameServerAddr, queryId);
            if (foundCtx != null && foundCtx.IsDone())
            {
                logger.Debug("{} Received a DNS response for a query that was timed out or cancelled " +
                        ": TCP [{}: {}]", tcpCh, queryId, context.nameServerAddr);
                response.Release();
            }
            else if (foundCtx == tcpCtx)
            {
                tcpCtx.FinishSuccess(new AddressedEnvelopeAdapter(
                        (IPEndPoint) ctx.Channel.RemoteAddress,
                        (IPEndPoint) ctx.Channel.LocalAddress,
                        response), false);
            }
            else
            {
                response.Release();
                tcpCtx.FinishFailure("Received TCP DNS response with unexpected ID", null, false);

                if (logger.IsDebugEnabled())
                {
                    logger.Debug("{} Received a DNS response with an unexpected ID: TCP [{}: {}]",
                            tcpCh, queryId, tcpCh.RemoteAddress);
                }
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (tcpCtx.FinishFailure(
                    "TCP fallback error", cause, false) && logger.IsDebugEnabled())
            {
                logger.Debug("{} Error during processing response: TCP [{}: {}]",
                        ctx.Channel, context.id,
                        ctx.Channel.RemoteAddress, cause);
            }
        }
    }

    private void FinishOriginal(object originalResult, Task future)
    {
        if (originalResult is Exception)
        {
            Exception error = (Exception) originalResult;
            ThrowableUtil.AddSuppressed(error, DnsNameResolver.Cause(future));
            promise.TrySetException(error);
        }
        else
        {
            FinishSuccess((IAddressedEnvelope<IDnsResponse, IPEndPoint>) originalResult, false);
        }
    }

    // CLR: AddressedEnvelope's content()/sender()/recipient() are the Content/Sender/Recipient members of
    // IAddressedEnvelope and refCnt() is RefCnt (docs/transport-porting.md); retain/touch return IReferenceCounted.
    // AddressedEnvelope<?, SocketAddress> is IAddressedEnvelope<object, EndPoint>, which the covariant
    // IAddressedEnvelope<out T, out TAddress> matches for every reference content type, as codec-dns DatagramDnsResponse
    // tests it. equals(...) keeps the source's final response.equals(obj); int arithmetic wraps as in Java.
    private sealed class AddressedEnvelopeAdapter : IAddressedEnvelope<IDnsResponse, IPEndPoint>
    {
        private readonly IPEndPoint sender;
        private readonly IPEndPoint recipient;
        private readonly IDnsResponse response;

        internal AddressedEnvelopeAdapter(IPEndPoint sender, IPEndPoint recipient, IDnsResponse response)
        {
            this.sender = sender;
            this.recipient = recipient;
            this.response = response;
        }

        public IDnsResponse Content => response;

        public IPEndPoint Sender => sender;

        public IPEndPoint Recipient => recipient;

        public IReferenceCounted Retain()
        {
            response.Retain();
            return this;
        }

        public IReferenceCounted Retain(int increment)
        {
            response.Retain(increment);
            return this;
        }

        public IReferenceCounted Touch()
        {
            response.Touch();
            return this;
        }

        public IReferenceCounted Touch(object hint)
        {
            response.Touch(hint);
            return this;
        }

        public int RefCnt => response.RefCnt;

        public bool Release()
        {
            return response.Release();
        }

        public bool Release(int decrement)
        {
            return response.Release(decrement);
        }

        public override bool Equals(object obj)
        {
            if (this == obj)
            {
                return true;
            }

            if (!(obj is IAddressedEnvelope<object, EndPoint>))
            {
                return false;
            }

            IAddressedEnvelope<object, EndPoint> that = (IAddressedEnvelope<object, EndPoint>) obj;

            if (Sender == null)
            {
                if (that.Sender != null)
                {
                    return false;
                }
            }
            else if (!Sender.Equals(that.Sender))
            {
                return false;
            }

            if (Recipient == null)
            {
                if (that.Recipient != null)
                {
                    return false;
                }
            }
            else if (!Recipient.Equals(that.Recipient))
            {
                return false;
            }

            return response.Equals(obj);
        }

        public override int GetHashCode()
        {
            int hashCode = response.GetHashCode();

            if (Sender != null)
            {
                hashCode = hashCode * 31 + Sender.GetHashCode();
            }

            if (Recipient != null)
            {
                hashCode = hashCode * 31 + Recipient.GetHashCode();
            }

            return hashCode;
        }
    }
}
