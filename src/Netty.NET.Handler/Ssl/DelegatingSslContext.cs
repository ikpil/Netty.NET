/*
 * Copyright 2016 The Netty Project
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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * Adapter class which allows to wrap another {@link SslContext} and init {@link SSLEngine} instances.
 */
// CLR: the SSLEngine is not ported (owner decision 2026-11-24, docs/handler-ssl-design.md: no SSLEngine facade), so the
// newEngine overrides, sessionContext() and the abstract initEngine(SSLEngine) hook are not ported (initEngine's Javadoc
// is recorded); SslContext has no sessionCacheSize()/sessionTimeout() to forward and no Executor overloads of newHandler
// (see SslContext). initHandler(SslHandler) stays the hook that sees every handler; Java's final overrides are sealed
// overrides.
public abstract class DelegatingSslContext : SslContext
{

    private readonly SslContext ctx;

    protected DelegatingSslContext(SslContext ctx)
    {
        this.ctx = ObjectUtil.CheckNotNull(ctx, "ctx");
    }

    public sealed override bool IsClient()
    {
        return ctx.IsClient();
    }

    public sealed override IList<string> CipherSuites()
    {
        return ctx.CipherSuites();
    }

    public sealed override IApplicationProtocolNegotiator ApplicationProtocolNegotiator()
    {
        return ctx.ApplicationProtocolNegotiator();
    }

    protected internal sealed override SslHandler NewHandler(IByteBufAllocator alloc, bool startTls)
    {
        SslHandler handler = ctx.NewHandler(alloc, startTls);
        InitHandler(handler);
        return handler;
    }

    protected internal sealed override SslHandler NewHandler(IByteBufAllocator alloc, string peerHost, int peerPort,
                                                             bool startTls)
    {
        SslHandler handler = ctx.NewHandler(alloc, peerHost, peerPort, startTls);
        InitHandler(handler);
        return handler;
    }

    /**
     * Init the {@link SslHandler}. This will by default call {@link #initEngine(SSLEngine)}, sub-classes may override
     * this.
     */
    // CLR: initEngine(handler.engine()) has no counterpart (the engine is not ported, see the class comment), so the
    // default does nothing.
    protected virtual void InitHandler(SslHandler handler)
    {
    }
}
