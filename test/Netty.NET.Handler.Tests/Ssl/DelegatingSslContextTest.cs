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

using System.Net.Security;
using Netty.NET.Buffer;
using Netty.NET.Handler.Ssl;
using AuthSslProtocols = System.Security.Authentication.SslProtocols;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the SSLEngine is the SslServerAuthenticationOptions a context builds for each SslHandler (owner decision
// 2026-11-24, docs/handler-ssl-design.md: no SSLEngine facade), so DelegatingSslContext's initEngine(SSLEngine) hook is
// initHandler(SslHandler) (src/Netty.NET.Handler/Ssl/DelegatingSslContext.cs), and engine.setEnabledProtocols/
// getEnabledProtocols are the options' EnabledSslProtocols, read through the internal SslHandler.ServerOptions()
// (engine() is internal and returns the SslStream, which does not expose them). SslProtocols.TLS_v1_1 is @Deprecated
// with a replacement and not ported (the "TLSv1.1" text, src/Netty.NET.Handler/Ssl/JdkSslContext.cs ToSslProtocols); the
// enabled protocols are the System.Security.Authentication.SslProtocols value Tls11, which .NET marks obsolete
// (SYSLIB0039), as JdkSslContext maps "TLSv1.1". new JdkSslContext(SSLContext.getDefault(), false, ClientAuth.NONE) is
// the internal JdkSslContext constructor of a server context without key or trust material (the public SSLContext
// constructors are not ported, see JdkSslContext.cs), with the default negotiator of
// JdkDefaultApplicationProtocolNegotiator.INSTANCE (JdkSslContext.ToNegotiator(null, true)). The anonymous
// DelegatingSslContext is the nested ProtocolsDelegatingSslContext. UnpooledByteBufAllocator.DEFAULT is Default.
// testInitEngineOnNewEngine is not ported: SslContext.newEngine(...) has no counterpart (owner decision 2), and the same
// initialization is asserted through newHandler(...) by testInitEngineOnNewSslHandler; it is recorded as
// CLR-inapplicable in docs/handler-porting-manifest.json.
public class DelegatingSslContextTest
{
#pragma warning disable SYSLIB0039 // TLSv1.1 is the protocol the source enables; .NET marks the value obsolete.
    private static readonly AuthSslProtocols EXPECTED_PROTOCOLS = AuthSslProtocols.Tls11;
#pragma warning restore SYSLIB0039

    [Fact]
    public void TestInitEngineOnNewSslHandler()
    {
        SslContext delegating = NewDelegatingSslContext();

        SslHandler handler = delegating.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.Equal(EXPECTED_PROTOCOLS, ServerOptions(handler).EnabledSslProtocols);

        handler = delegating.NewHandler(UnpooledByteBufAllocator.Default, "localhost", 9090);
        Assert.Equal(EXPECTED_PROTOCOLS, ServerOptions(handler).EnabledSslProtocols);
    }

    private static SslContext NewDelegatingSslContext()
    {
        return new ProtocolsDelegatingSslContext(new JdkSslContext(null, null, null, null, null, false, null,
                IdentityCipherSuiteFilter.INSTANCE, JdkSslContext.ToNegotiator(null, true), ClientAuth.NONE, null, false,
                null, null));
    }

    private sealed class ProtocolsDelegatingSslContext : DelegatingSslContext
    {
        internal ProtocolsDelegatingSslContext(SslContext ctx) : base(ctx)
        {
        }

        protected override void InitHandler(SslHandler handler)
        {
            ServerOptions(handler).EnabledSslProtocols = EXPECTED_PROTOCOLS;
        }
    }

    // CLR: see the class comment.
    private static SslServerAuthenticationOptions ServerOptions(SslHandler handler)
    {
        return handler.ServerOptions();
    }
}
