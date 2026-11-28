/*
 * Copyright 2022 The Netty Project
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
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Resolver.Dns;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Ssl.Ocsp;

/**
 * {@link OcspServerCertificateValidator} validates incoming server's certificate
 * using OCSP. Once TLS handshake is completed, {@link SslHandshakeCompletionEvent#SUCCESS} is fired, validator
 * will perform certificate validation using OCSP over HTTP/1.1 with the server's certificate issuer OCSP responder.
 */
// CLR: ctx.pipeline().get(SslHandler.class).engine().getSession().getPeerCertificates() is the internal
// SslHandler.PeerCertificates() (the remote certificate followed by its issuers; SslHandler.engine() is not public,
// docs/handler-ssl-design.md, and InternalsVisibleTo opens the accessor to this assembly), and X509Certificate is
// X509Certificate2. Promise<BasicOCSPResp> from ctx.executor().newPromise() is a TaskCompletionSource<BasicOcspResp>
// whose listener runs on Common's EventExecutorTaskScheduler over ctx.Executor, so it runs on the event loop as the
// DefaultPromise listener does. BasicOCSPResp, SingleResp and RevokedStatus are the CLR-only replacements of
// OcspAsn1.cs; java.util.Date is DateTimeOffset, System.currentTimeMillis() is DateTimeOffset.UtcNow, and before/after
// are the < and > operators. OCSPException is CryptographicException and IllegalStateException
// InvalidOperationException. AttributeKey<Boolean> is AttributeKey<object> holding a boxed bool, as resolver-dns's
// DNS_PIPELINE_ATTRIBUTE (AttributeKey<T> requires a reference type). ChannelPromise is the returned Task of each
// outbound operation (docs/transport-porting.md). The checked "throws Exception" clauses have no counterpart.
public class OcspServerCertificateValidator : ByteToMessageDecoder, IChannelOutboundHandler
{
    /**
     * An attribute used to mark all channels created by the {@link OcspServerCertificateValidator}.
     */
    public static readonly AttributeKey<object> OCSP_PIPELINE_ATTRIBUTE =
            AttributeKey<object>.NewInstance("io.netty.handler.ssl.ocsp.pipeline");

    /**
     * Tolerate some clock skew in the OCSP validity time. Default to 15 minutes, which is the same as the JDK.
     */
    private static readonly long CLOCK_SKEW_TOLERANCE_MILLIS = GetClockSkewTolerance();

    // CLR: TimeUnit.MINUTES.toSeconds(15), TimeUnit.DAYS.toSeconds(2) and TimeUnit.SECONDS.toMillis are TimeSpan
    // conversions (docs/common-clr-design.md, "Monotonic time and duration conversion").
    private static long GetClockSkewTolerance()
    {
        long defaultToleranceSeconds = (long) TimeSpan.FromMinutes(15).TotalSeconds;
        long maxToleranceSeconds = (long) TimeSpan.FromDays(2).TotalSeconds;
        long configuredToleranceSeconds = SystemPropertyUtil.GetLong("io.netty.handler.ssl.ocsp.clockSkew",
            SystemPropertyUtil.GetLong("com.sun.security.ocsp.clockSkew", defaultToleranceSeconds));
        if (configuredToleranceSeconds < 0 || configuredToleranceSeconds > maxToleranceSeconds)
        {
            // Ignore negative and extremely large values.
            configuredToleranceSeconds = defaultToleranceSeconds;
        }

        return (long) TimeSpan.FromSeconds(configuredToleranceSeconds).TotalMilliseconds;
    }

    private readonly bool closeAndThrowIfNotValid;
    private readonly bool validateNonce;
    private readonly IoTransport ioTransport;
    private readonly DnsNameResolver dnsNameResolver;
    private bool ocspQueryInProgress;
    private bool readPending;
    private bool discardInbound;

    /**
     * Create a new {@link OcspServerCertificateValidator} instance without nonce validation
     * on OCSP response, using default {@link IoTransport#DEFAULT} instance,
     * default {@link DnsNameResolver} implementation and with {@link #closeAndThrowIfNotValid}
     * set to {@code true}
     */
    public OcspServerCertificateValidator() : this(false)
    {
    }

    /**
     * Create a new {@link OcspServerCertificateValidator} instance with
     * default {@link IoTransport#DEFAULT} instance and default {@link DnsNameResolver} implementation
     * and {@link #closeAndThrowIfNotValid} set to {@code true}.
     *
     * @param validateNonce Set to {@code true} if we should force nonce validation on
     *                      OCSP response else set to {@code false}
     */
    public OcspServerCertificateValidator(bool validateNonce) : this(validateNonce, IoTransport.DEFAULT)
    {
    }

    /**
     * Create a new {@link OcspServerCertificateValidator} instance
     *
     * @param validateNonce Set to {@code true} if we should force nonce validation on
     *                      OCSP response else set to {@code false}
     * @param ioTransport   {@link IoTransport} to use
     */
    public OcspServerCertificateValidator(bool validateNonce, IoTransport ioTransport)
        : this(validateNonce, ioTransport, CreateDefaultResolver(ioTransport))
    {
    }

    /**
     * Create a new {@link IoTransport} instance with {@link #closeAndThrowIfNotValid} set to {@code true}
     *
     * @param validateNonce   Set to {@code true} if we should force nonce validation on
     *                        OCSP response else set to {@code false}
     * @param ioTransport     {@link IoTransport} to use
     * @param dnsNameResolver {@link DnsNameResolver} implementation to use
     */
    public OcspServerCertificateValidator(bool validateNonce, IoTransport ioTransport,
                                          DnsNameResolver dnsNameResolver)
        : this(true, validateNonce, ioTransport, dnsNameResolver)
    {
    }

    /**
     * Create a new {@link IoTransport} instance
     *
     * @param closeAndThrowIfNotValid If set to {@code true} then we will close the channel and throw an exception
     *                                when certificate is not {@link OcspResponse.Status#VALID}.
     *                                If set to {@code false} then we will simply pass the {@link OcspValidationEvent}
     *                                to the next handler in pipeline and let it decide what to do.
     * @param validateNonce           Set to {@code true} if we should force nonce validation on
     *                                OCSP response else set to {@code false}
     * @param ioTransport             {@link IoTransport} to use
     * @param dnsNameResolver         {@link DnsNameResolver} implementation to use
     */
    public OcspServerCertificateValidator(bool closeAndThrowIfNotValid, bool validateNonce,
                                          IoTransport ioTransport, DnsNameResolver dnsNameResolver)
    {
        this.closeAndThrowIfNotValid = closeAndThrowIfNotValid;
        this.validateNonce = validateNonce;
        this.ioTransport = ObjectUtil.CheckNotNull(ioTransport, "IoTransport");
        this.dnsNameResolver = ObjectUtil.CheckNotNull(dnsNameResolver, "DnsNameResolver");
    }

    // CLR: ioTransport.datagramChannel()/socketChannel() are the Func factories DnsNameResolverBuilder takes.
    protected static DnsNameResolver CreateDefaultResolver(IoTransport ioTransport)
    {
        return new DnsNameResolverBuilder()
                .EventLoop(ioTransport.EventLoop())
                .DatagramChannelFactory(ioTransport.DatagramChannel())
                .SocketChannelFactory(ioTransport.SocketChannel())
                .Build();
    }

    // CLR-only: the tests call the protected static createDefaultResolver through package access (static import); a test
    // assembly cannot reach a protected member, so this internal forwarder serves them through InternalsVisibleTo, as
    // handler-proxy's HttpProxyHandler.InvokeNewInitialMessage does.
    internal static DnsNameResolver InvokeCreateDefaultResolver(IoTransport ioTransport)
    {
        return CreateDefaultResolver(ioTransport);
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        // Just buffer until the handler is removed which will happen once we did finish the OCSP processing.
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (discardInbound)
        {
            ReferenceCountUtil.Release(msg);
            return;
        }

        base.ChannelRead(ctx, msg);
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is SslHandshakeCompletionEvent)
        {
            SslHandshakeCompletionEvent sslHandshakeCompletionEvent = (SslHandshakeCompletionEvent) evt;

            // If TLS handshake was successful then only we will perform OCSP certificate validation.
            // If not, then just forward the event to next handler in pipeline and remove ourselves from pipeline.
            if (sslHandshakeCompletionEvent.IsSuccess())
            {
                X509Certificate2[] certificates = ctx.Pipeline.Get<SslHandler>()
                        .PeerCertificates();

                Debug.Assert(certificates.Length >= 2, "There must an end-entity certificate and issuer certificate");

                TaskCompletionSource<BasicOcspResp> ocspRespPromise = new TaskCompletionSource<BasicOcspResp>();
                OcspClient.Query(certificates[0], certificates[1],
                        validateNonce, ioTransport, dnsNameResolver, ocspRespPromise);
                ocspQueryInProgress = true;
                _ = ocspRespPromise.Task.ContinueWith(future =>
                {
                    ocspQueryInProgress = false;
                    // Assume revoked as default.
                    OcspResponse.Status status = OcspResponse.Status.REVOKED;

                    try
                    {
                        // If Future is success then we have successfully received OCSP response
                        // from OCSP responder. We will validate it now and process.
                        if (future.IsCompletedSuccessfully)
                        {
                            SingleResp response = future.Result.GetResponses()[0];

                            DateTimeOffset thisUpdate = response.GetThisUpdate();
                            DateTimeOffset? nextUpdate = response.GetNextUpdate();
                            DateTimeOffset now = DateTimeOffset.UtcNow;
                            DateTimeOffset nowLower = now - TimeSpan.FromMilliseconds(CLOCK_SKEW_TOLERANCE_MILLIS);
                            DateTimeOffset nowUpper = now + TimeSpan.FromMilliseconds(CLOCK_SKEW_TOLERANCE_MILLIS);
                            // CLR: thisUpdate == null has no counterpart: thisUpdate is a required GeneralizedTime
                            // of the SingleResponse, which SingleResp always reads as a DateTimeOffset.
                            if (nowUpper < thisUpdate ||
                                nowLower > (nextUpdate == null ? thisUpdate : nextUpdate.Value))
                            {
                                ctx.FireExceptionCaught(new InvalidOperationException("OCSP Response is out-of-date"));

                                if (closeAndThrowIfNotValid)
                                {
                                    _ = ctx.CloseAsync();
                                }

                                return;
                            }

                            if (response.GetCertStatus() == null)
                            {
                                // 'null' means certificate is valid
                                status = OcspResponse.Status.VALID;
                            }
                            else if (response.GetCertStatus() is RevokedStatus)
                            {
                                status = OcspResponse.Status.REVOKED;
                            }
                            else
                            {
                                status = OcspResponse.Status.UNKNOWN;
                            }

                            ctx.FireUserEventTriggered(new OcspValidationEvent(
                                new OcspResponse(status, thisUpdate, nextUpdate)));

                            // If Certificate is not VALID and 'closeAndThrowIfNotValid' is set
                            // to 'true' then close the channel and throw an exception.
                            if (status != OcspResponse.Status.VALID && closeAndThrowIfNotValid)
                            {
                                // Certificate is not valid. Throw
                                ctx.FireExceptionCaught(new CryptographicException(
                                    "Certificate not valid. Status: " + status));
                                _ = ctx.CloseAsync();
                            }
                        }
                        else
                        {
                            ctx.FireExceptionCaught(Cause(future));

                            if (closeAndThrowIfNotValid)
                            {
                                _ = ctx.CloseAsync();
                            }
                        }
                    }
                    catch (Exception th)
                    {
                        ctx.FireExceptionCaught(th);

                        if (closeAndThrowIfNotValid)
                        {
                            _ = ctx.CloseAsync();
                        }
                    }
                    finally
                    {
                        if (status != OcspResponse.Status.VALID && closeAndThrowIfNotValid)
                        {
                            // The peer can't be trusted, so drop all bytes on the floor and stay in the pipeline
                            // until the channel is closed. This way neither the bytes we buffered nor anything that
                            // arrives later (for example data still buffered in the SslHandler) is handed to the
                            // following handlers.
                            discardInbound = true;
                            InternalBuffer().Clear();
                            ctx.FireUserEventTriggered(evt);
                        }
                        else
                        {
                            ctx.FireUserEventTriggered(evt);
                            // Lets remove ourselves from the pipeline because we are done processing validation.
                            // This will also forward all the buffered bytes to the next handler.
                            ctx.Pipeline.Remove(this);
                        }

                        if (readPending)
                        {
                            readPending = false;
                            ctx.Read();
                        }
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        new EventExecutorTaskScheduler(() => ctx.Executor));
            }
            else
            {
                ctx.FireUserEventTriggered(evt);
            }
        }
        else
        {
            ctx.FireUserEventTriggered(evt);
        }
    }

    // CLR-only helper: future.cause() of the completed Task (see OcspClient).
    private static Exception Cause(Task future)
    {
        if (future.IsFaulted)
        {
            return future.Exception.InnerExceptions.Count == 1 ? future.Exception.InnerException : future.Exception;
        }

        return future.IsCanceled ? new TaskCanceledException(future) : null;
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        _ = ctx.CloseAsync();
    }

    public virtual Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress, CancellationToken cancellationToken)
    {
        return ctx.BindAsync(localAddress, cancellationToken);
    }

    public virtual Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress,
                        EndPoint localAddress, CancellationToken cancellationToken)
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

    public virtual void Read(IChannelHandlerContext ctx)
    {
        // Let's stop reading until we are done with the processing of the OCSP query.
        if (ocspQueryInProgress)
        {
            readPending = true;
        }
        else
        {
            readPending = false;
            ctx.Read();
        }
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
