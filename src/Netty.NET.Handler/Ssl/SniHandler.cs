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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Ssl;

/**
 * <p>Enables <a href="https://tools.ietf.org/html/rfc3546#section-3.1">SNI
 * (Server Name Indication)</a> extension for server side SSL. For clients
 * support SNI, the server could have multiple host name bound on a single IP.
 * The client will send host name in the handshake data so server could decide
 * which certificate to choose for the host name.</p>
 */
// CLR: io.netty.util.Mapping is Func<TIn, TOut> and AsyncMapping is IAsyncMapping<TInput, TOutput>, whose MapAsync
// returns a provider-owned Task instead of completing a caller Promise; DomainNameMapping keeps its Map method,
// used as the Func. Func is covariant in its result, so Mapping<? super String, ? extends SslContext> is
// Func<string, SslContext>; the input of IAsyncMapping is contravariant but Task<T> is invariant, so
// AsyncMapping<? super String, ? extends SslContext> is IAsyncMapping<string, SslContext>, and DomainNameMapping<?
// extends SslContext> is DomainNameMapping<SslContext> (a class type parameter has no variance).
// Future<SslContext> is Task<SslContext>, as in AbstractSniHandler. The unchecked casts of the source have no
// counterpart.
public class SniHandler : AbstractSniHandler<SslContext>
{
    private static readonly Selection EMPTY_SELECTION = new Selection(null, null);

    protected readonly IAsyncMapping<string, SslContext> mapping;

    private volatile Selection selection = EMPTY_SELECTION;

    /**
     * Creates a SNI detection handler with configured {@link SslContext}
     * maintained by {@link Mapping}
     *
     * @param mapping the mapping of domain name to {@link SslContext}
     */
    public SniHandler(Func<string, SslContext> mapping)
        : this(new AsyncMappingAdapter(mapping))
    {
    }

    /**
     * Creates a SNI detection handler with configured {@link SslContext}
     * maintained by {@link Mapping}
     *
     * @param mapping the mapping of domain name to {@link SslContext}
     * @param maxClientHelloLength the maximum length of the client hello message
     * @param handshakeTimeoutMillis the handshake timeout in milliseconds
     */
    public SniHandler(Func<string, SslContext> mapping,
                      int maxClientHelloLength, long handshakeTimeoutMillis)
        : this(new AsyncMappingAdapter(mapping), maxClientHelloLength, handshakeTimeoutMillis)
    {
    }

    /**
     * Creates a SNI detection handler with configured {@link SslContext}
     * maintained by {@link DomainNameMapping}
     *
     * @param mapping the mapping of domain name to {@link SslContext}
     */
    // CLR: the Mapping view of DomainNameMapping is its Map method group (null stays null, as the cast does).
    public SniHandler(DomainNameMapping<SslContext> mapping)
        : this(mapping == null ? null : (Func<string, SslContext>) mapping.Map)
    {
    }

    /**
     * Creates a SNI detection handler with configured {@link SslContext}
     * maintained by {@link AsyncMapping}
     *
     * @param mapping the mapping of domain name to {@link SslContext}
     */
    public SniHandler(IAsyncMapping<string, SslContext> mapping)
        : this(mapping, DEFAULT_MAX_CLIENT_HELLO_LENGTH, DEFAULT_HANDSHAKE_TIMEOUT_MILLIS)
    {
    }

    /**
     * Creates a SNI detection handler with configured {@link SslContext}
     * maintained by {@link AsyncMapping}
     *
     * @param mapping the mapping of domain name to {@link SslContext}
     * @param maxClientHelloLength the maximum length of the client hello message
     * @param handshakeTimeoutMillis the handshake timeout in milliseconds
     */
    public SniHandler(IAsyncMapping<string, SslContext> mapping,
                      int maxClientHelloLength, long handshakeTimeoutMillis)
        : base(maxClientHelloLength, handshakeTimeoutMillis)
    {
        this.mapping = ObjectUtil.CheckNotNull(mapping, "mapping");
    }

    /**
     * Creates a SNI detection handler with configured {@link SslContext}
     * maintained by {@link Mapping}
     *
     * @param mapping the mapping of domain name to {@link SslContext}
     * @param handshakeTimeoutMillis the handshake timeout in milliseconds
     */
    public SniHandler(Func<string, SslContext> mapping, long handshakeTimeoutMillis)
        : this(new AsyncMappingAdapter(mapping), handshakeTimeoutMillis)
    {
    }

    /**
     * Creates a SNI detection handler with configured {@link SslContext}
     * maintained by {@link AsyncMapping}
     *
     * @param mapping the mapping of domain name to {@link SslContext}
     * @param handshakeTimeoutMillis the handshake timeout in milliseconds
     */
    public SniHandler(IAsyncMapping<string, SslContext> mapping, long handshakeTimeoutMillis)
        : this(mapping, DEFAULT_MAX_CLIENT_HELLO_LENGTH, handshakeTimeoutMillis)
    {
    }

    /**
     * @return the selected hostname
     */
    public string Hostname()
    {
        return selection.hostname;
    }

    /**
     * @return the selected {@link SslContext}
     */
    public SslContext SslContext()
    {
        return selection.context;
    }

    /**
     * The default implementation will simply call {@link AsyncMapping#map(Object, Promise)} but
     * users can override this method to implement custom behavior.
     *
     * @see AsyncMapping#map(Object, Promise)
     */
    // CLR: map(hostname, ctx.executor().newPromise()) is MapAsync(hostname): the mapping owns the returned Task, and
    // AbstractSniHandler continues on the channel's executor.
    protected override Task<SslContext> Lookup(IChannelHandlerContext ctx, string hostname)
    {
        return mapping.MapAsync(hostname);
    }

    // CLR: future.isSuccess() is IsCompletedSuccessfully and getNow() is Result. future.cause() is the faulted Task's
    // exception or, for a cancelled Task, a TaskCanceledException (as AbstractSniHandler reads it). java.lang.Error has no
    // CLR counterpart (every exception derives from Exception), so the rethrow of an Error is not ported and every cause
    // is wrapped in the DecoderException. catch (Throwable) with PlatformDependent.throwException is catch (Exception)
    // and throw;.
    protected sealed override void OnLookupComplete(IChannelHandlerContext ctx,
                                                    string hostname, Task<SslContext> future)
    {
        if (!future.IsCompletedSuccessfully)
        {
            Exception cause = future.IsFaulted
                    ? (future.Exception.InnerExceptions.Count == 1 ? future.Exception.InnerException : future.Exception)
                    : new TaskCanceledException(future);
            throw new DecoderException("failed to get the SslContext for " + hostname, cause);
        }

        SslContext sslContext = future.Result;
        selection = new Selection(sslContext, hostname);

        try
        {
            ReplaceHandler(ctx, hostname, sslContext);
        }
        catch (Exception)
        {
            selection = EMPTY_SELECTION;
            throw;
        }
    }

    /**
     * The default implementation of this method will simply replace {@code this} {@link SniHandler}
     * instance with a {@link SslHandler}. Users may override this method to implement custom behavior.
     *
     * Please be aware that this method may get called after a client has already disconnected and
     * custom implementations must take it into consideration when overriding this method.
     *
     * It's also possible for the hostname argument to be {@code null}.
     */
    // CLR: ReferenceCountUtil.safeRelease(sslHandler.engine()) releases the engine of a handler that did not reach
    // the pipeline; the engine is the SslStream the handler created (SslHandler.engine() is internal, owner
    // decision 2026-11-24), whose Dispose "Releases the unmanaged resources used by the SslStream"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0).
    protected virtual void ReplaceHandler(IChannelHandlerContext ctx, string hostname, SslContext sslContext)
    {
        SslHandler sslHandler = null;

        try
        {
            sslHandler = NewSslHandler(sslContext, ctx.Alloc());
            ctx.Pipeline.Replace(this, typeof(SslHandler).FullName, sslHandler);
            sslHandler = null;
        }
        finally
        {
            // Since the SslHandler was not inserted into the pipeline the ownership of the SSLEngine was not
            // transferred to the SslHandler.
            // See https://github.com/netty/netty/issues/5678
            if (sslHandler != null)
            {
                sslHandler.Engine().Dispose();
            }
        }
    }

    /**
     * Returns a new {@link SslHandler} using the given {@link SslContext} and {@link ByteBufAllocator}.
     * Users may override this method to implement custom behavior.
     */
    protected virtual SslHandler NewSslHandler(SslContext context, IByteBufAllocator allocator)
    {
        SslHandler sslHandler = context.NewHandler(allocator);
        sslHandler.SetHandshakeTimeoutMillis(handshakeTimeoutMillis);
        return sslHandler;
    }

    // CLR: promise.setFailure(cause)/setSuccess(context) are Task.FromException/Task.FromResult ("Task.FromResult/
    // Task.FromException are sufficient for synchronous adapters", docs/common-native-async-mapping.md); catch (Throwable)
    // is catch (Exception). The cancellation token is not used, as the synchronous mapping cannot be cancelled.
    private sealed class AsyncMappingAdapter : IAsyncMapping<string, SslContext>
    {
        private readonly Func<string, SslContext> mapping;

        internal AsyncMappingAdapter(Func<string, SslContext> mapping)
        {
            this.mapping = ObjectUtil.CheckNotNull(mapping, "mapping");
        }

        public Task<SslContext> MapAsync(string input, CancellationToken cancellationToken = default)
        {
            SslContext context;

            try
            {
                context = mapping(input);
            }
            catch (Exception cause)
            {
                return Task.FromException<SslContext>(cause);
            }

            return Task.FromResult(context);
        }
    }

    private sealed class Selection
    {
        internal readonly SslContext context;
        internal readonly string hostname;

        internal Selection(SslContext context, string hostname)
        {
            this.context = context;
            this.hostname = hostname;
        }
    }
}
