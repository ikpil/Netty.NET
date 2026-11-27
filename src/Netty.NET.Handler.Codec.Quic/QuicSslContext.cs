/*
 * Copyright 2021 The Netty Project
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

using System.Security.Cryptography.X509Certificates;
using Netty.NET.Buffer;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Special {@link SslContext} that can be used for {@code QUIC}.
 */
// CLR: SslContext is the handler port's SslContext on SslStream (owner decision 2026-11-24, docs/handler-ssl-design.md),
// which does not port newEngine(alloc[, peerHost, peerPort]) or sessionContext() (decision 2: no SSLEngine facade). A
// QUIC context needs both: System.Net.Quic takes the TLS configuration per connection (QuicClientConnectionOptions/
// QuicServerConnectionOptions carry an SslClientAuthenticationOptions/SslServerAuthenticationOptions), so newEngine is
// where a QuicSslContext builds them, in a QuicSslEngine that later also holds the negotiated facts (see QuicSslEngine).
// The Java overrides of newEngine/sessionContext are therefore new abstract members here, returning QuicSslEngine and
// IQuicSslSessionContext as the Java overrides narrow them. The ALPN list of a server listener
// (QuicListenerOptions.ApplicationProtocols, needed before any connection) is ApplicationProtocolNegotiator().Protocols()
// of the context, and isClient() selects the options type. The abstract SslContext members stay abstract for the
// implementation (stage 2): isClient(), cipherSuites(), applicationProtocolNegotiator() and the protected
// newHandler(alloc, startTls) and newHandler(alloc, peerHost, peerPort, startTls), which a QUIC context implements by
// throwing NotSupportedException, as QuicheQuicSslContext throws UnsupportedOperationException (QUIC has no SslHandler);
// a subclass in another assembly overrides those protected internal members as protected. toX509Certificates0 calls the
// protected internal SslContext.ToX509Certificates(Stream): InputStream is System.IO.Stream, X509Certificate
// X509Certificate2 and CertificateException CryptographicException (see SslContext); it is internal (package-private
// in Java), for QuicSslContextBuilder (stage 2).
public abstract class QuicSslContext : SslContext
{

    public abstract QuicSslEngine NewEngine(IByteBufAllocator alloc);

    public abstract QuicSslEngine NewEngine(IByteBufAllocator alloc, string peerHost, int peerPort);

    public abstract IQuicSslSessionContext SessionContext();

    internal static X509Certificate2[] ToX509Certificates0(System.IO.Stream stream)
    {
        return SslContext.ToX509Certificates(stream);
    }
}
