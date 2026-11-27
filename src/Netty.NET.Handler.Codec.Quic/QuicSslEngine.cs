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

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * An {@link SSLEngine} that can be used for QUIC.
 */
// CLR: javax.net.ssl.SSLEngine has no counterpart. The handler port has no SSLEngine-shaped facade: the engine of a TLS
// connection is the SslClientAuthenticationOptions/SslServerAuthenticationOptions a context builds for it, and the
// negotiated facts are read from the TLS stream (owner decision 2026-11-24, docs/handler-ssl-design.md, decision 2).
// QUIC carries TLS 1.3 inside the transport: System.Net.Quic takes the same options per connection
// (QuicClientConnectionOptions.ClientAuthenticationOptions, QuicServerConnectionOptions.ServerAuthenticationOptions) and
// reports the negotiated facts on the QuicConnection (NegotiatedApplicationProtocol, RemoteCertificate,
// NegotiatedCipherSuite, SslProtocol). So the engine of one QUIC connection is an abstract class holding both halves:
//   - the options its QuicSslContext built for it (newEngine), exactly one of ClientAuthenticationOptions() and
//     ServerAuthenticationOptions() being non-null; GetUseClientMode() tells which, and GetPeerHost()/GetPeerPort()
//     are the peer passed to newEngine(alloc, peerHost, peerPort) (null and -1 otherwise), as SSLEngine's accessors;
//   - the facts of SSLEngine.getApplicationProtocol() and of getSession().getPeerCertificates(), getCipherSuite() and
//     getProtocol(), which the msquic implementation (stage 2) records from the QuicConnection once the handshake
//     completed: GetApplicationProtocol() (null before the handshake, "" without ALPN, decoded as UTF-8 as SslHandler
//     decodes SslApplicationProtocol), GetPeerCertificates() (the peer's certificate followed by its chain, as the
//     validation callback sees it; null while unknown, where SSLSession throws SSLPeerUnverifiedException),
//     GetCipherSuite() (TlsCipherSuite, whose names are the IANA names Java reports; TLS_NULL_WITH_NULL_NULL before
//     the handshake, as SSLSession's "SSL_NULL_WITH_NULL_NULL") and GetProtocol() (SslProtocols; None before the
//     handshake, where Java reports "NONE").
// The other SSLEngine members (wrap/unwrap, delegated tasks, handshake status, the enabled protocol/cipher suite and
// client-authentication setters, session creation) served quiche's BoringSSL engine and have no counterpart: msquic runs
// the handshake itself, and the options are configured before the connection starts.
public abstract class QuicSslEngine
{

    // CLR-only: SSLEngine's protected constructor.
    protected QuicSslEngine()
    {
    }

    // CLR-only: SSLEngine.getUseClientMode(); true when ClientAuthenticationOptions() is the configuration.
    public abstract bool GetUseClientMode();

    // CLR-only: SSLEngine.getPeerHost(); null when the context created the engine without peer information.
    public abstract string GetPeerHost();

    // CLR-only: SSLEngine.getPeerPort(); -1 when the context created the engine without peer information.
    public abstract int GetPeerPort();

    // CLR-only: the client configuration of this connection (TargetHost from the peer host, ALPN protocols, client
    // certificates, remote certificate validation), handed to QuicClientConnectionOptions.ClientAuthenticationOptions;
    // null for a server engine.
    public abstract SslClientAuthenticationOptions ClientAuthenticationOptions();

    // CLR-only: the server configuration of this connection (server certificate or its selection callback, ALPN
    // protocols, client certificate requirement and validation), handed to
    // QuicServerConnectionOptions.ServerAuthenticationOptions; null for a client engine.
    public abstract SslServerAuthenticationOptions ServerAuthenticationOptions();

    // CLR-only: SSLEngine.getApplicationProtocol(), the negotiated ALPN protocol.
    public abstract string GetApplicationProtocol();

    // CLR-only: getSession().getPeerCertificates().
    public abstract X509Certificate2[] GetPeerCertificates();

    // CLR-only: getSession().getCipherSuite().
    public abstract TlsCipherSuite GetCipherSuite();

    // CLR-only: getSession().getProtocol().
    public abstract SslProtocols GetProtocol();
}
