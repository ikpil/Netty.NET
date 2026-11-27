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

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the QuicSslContext of the msquic implementation (stage 2, docs/codec-classes-quic-porting.md), the
// counterpart of the JNI unit QuicheQuicSslContext (jvm-only-replaced), which QuicSslContextBuilder.Build() creates. The
// BoringSSL SSL_CTX and its callbacks become the SslServerAuthenticationOptions/SslClientAuthenticationOptions that
// NewEngine builds per connection, with the JCA-to-BCL mapping of the handler port (docs/handler-ssl-design.md): the
// TrustManagerFactory is an X509ChainPolicy (the options' CertificateChainPolicy, cloned per engine because the policy is
// mutable), a TrustManager is a RemoteCertificateValidationCallback, the KeyManagerFactory is an
// SslStreamCertificateContext (ServerCertificateContext on a server; on a client its TargetCertificate is the client
// certificate, as QUIC's client options take ClientCertificates) and a KeyManager is a ServerCertificateSelectionCallback
// or LocalCertificateSelectionCallback. The verify mode of the server (boringSSLVerifyModeForServer: NONE, PEER,
// PEER|FAIL_IF_NO_PEER_CERT) is ClientCertificateRequired with the decision in VerifyRemoteCertificate, as JdkSslContext
// decides it. The ALPN list of the context is the options' ApplicationProtocols and the negotiator's Protocols(), which
// the listener also needs (QuicListenerOptions.ApplicationProtocols). The SNI mapping selects the context of a server
// connection from SslClientHelloInfo.ServerName in QuicListenerOptions.ConnectionOptionsCallback (MsQuicServerCodec).
// msquic owns the TLS groups, the signature algorithms, the session cache and tickets, 0-RTT and key logging; the
// builder reports them as unsupported (QuicSslContextBuilder) and the BoringSSL context options are skipped with the
// debug log Java writes for an option it does not know.
internal sealed class MsQuicSslContext : QuicSslContext
{
    private static readonly IInternalLogger LOGGER = InternalLoggerFactory.GetInstance(typeof(MsQuicSslContext));

    // The default of the client builder (QuicheQuicSslContext.defaultEndpointVerificationAlgorithm, which hides the
    // inherited field with the same value).
    internal new static readonly string defaultEndpointVerificationAlgorithm =
            SslContext.defaultEndpointVerificationAlgorithm;

    internal readonly ClientAuth clientAuth;
    private readonly bool server;
    private readonly string endpointIdentificationAlgorithm;
    private readonly X509ChainPolicy trustManagerFactory;
    private readonly RemoteCertificateValidationCallback trustManager;
    private readonly SslStreamCertificateContext keyManagerFactory;
    private readonly ServerCertificateSelectionCallback serverKeyManager;
    private readonly LocalCertificateSelectionCallback clientKeyManager;
    private readonly Func<string, QuicSslContext> mapping;
    private readonly IApplicationProtocolNegotiator apn;
    private readonly long sessionCacheSize;
    private readonly long sessionTimeout;
    private readonly MsQuicSslSessionContext sessionCtx = new MsQuicSslSessionContext();

    internal MsQuicSslContext(bool server, long sessionTimeout, long sessionCacheSize, ClientAuth clientAuth,
                              X509ChainPolicy trustManagerFactory, RemoteCertificateValidationCallback trustManager,
                              SslStreamCertificateContext keyManagerFactory,
                              ServerCertificateSelectionCallback serverKeyManager,
                              LocalCertificateSelectionCallback clientKeyManager,
                              Func<string, QuicSslContext> mapping, string[] applicationProtocols,
                              string endpointIdentificationAlgorithm,
                              KeyValuePair<SslContextOption, object>[] ctxOptions)
    {
        Codec.Quic.Quic.EnsureAvailability();
        this.server = server;
        this.endpointIdentificationAlgorithm = endpointIdentificationAlgorithm;
        this.clientAuth = server ? clientAuth : ClientAuth.NONE;
        this.trustManagerFactory = trustManagerFactory;
        this.trustManager = trustManager;
        if (server && keyManagerFactory == null && serverKeyManager == null)
        {
            throw new ArgumentException("No KeyManagerFactory");
        }
        this.keyManagerFactory = keyManagerFactory;
        this.serverKeyManager = serverKeyManager;
        this.clientKeyManager = clientKeyManager;
        this.mapping = mapping;
        if (ctxOptions != null)
        {
            foreach (KeyValuePair<SslContextOption, object> ctxOpt in ctxOptions)
            {
                LOGGER.Debug("Skipping unsupported " + nameof(SslContextOption) + ": " + ctxOpt.Key);
            }
        }
        apn = new ProtocolNegotiator(applicationProtocols);
        this.sessionCacheSize = sessionCacheSize;
        this.sessionTimeout = sessionTimeout;
    }

    // The SNI mapping of a server context, or null.
    internal Func<string, QuicSslContext> Mapping => mapping;

    internal long SessionCacheSize() => sessionCacheSize;

    internal long SessionTimeout() => sessionTimeout;

    public override bool IsClient()
    {
        return !server;
    }

    public override IList<string> CipherSuites()
    {
        return new List<string> { "TLS_AES_128_GCM_SHA256", "TLS_AES_256_GCM_SHA384" };
    }

    public override IApplicationProtocolNegotiator ApplicationProtocolNegotiator()
    {
        return apn;
    }

    public override QuicSslEngine NewEngine(IByteBufAllocator alloc)
    {
        return NewEngine0(null, -1);
    }

    public override QuicSslEngine NewEngine(IByteBufAllocator alloc, string peerHost, int peerPort)
    {
        return NewEngine0(peerHost, peerPort);
    }

    public override IQuicSslSessionContext SessionContext()
    {
        return sessionCtx;
    }

    protected override SslHandler NewHandler(IByteBufAllocator alloc, bool startTls)
    {
        throw new NotSupportedException();
    }

    protected override SslHandler NewHandler(IByteBufAllocator alloc, string peerHost, int peerPort, bool startTls)
    {
        throw new NotSupportedException();
    }

    // The ALPN protocols as SslApplicationProtocol values; null (no ALPN) for an empty list.
    internal List<SslApplicationProtocol> ApplicationProtocols()
    {
        IList<string> names = apn.Protocols();
        if (names.Count == 0)
        {
            return null;
        }
        List<SslApplicationProtocol> protocols = new List<SslApplicationProtocol>(names.Count);
        foreach (string name in names)
        {
            protocols.Add(new SslApplicationProtocol(name));
        }
        return protocols;
    }

    private MsQuicSslEngine NewEngine0(string peerHost, int peerPort)
    {
        MsQuicSslEngine engine = null;
        RemoteCertificateValidationCallback verify = (sender, certificate, chain, errors) =>
        {
            engine.RecordPeerChain(certificate, chain);
            return VerifyRemoteCertificate(sender, certificate, chain, errors);
        };
        if (server)
        {
            SslServerAuthenticationOptions options = new SslServerAuthenticationOptions
            {
                ApplicationProtocols = ApplicationProtocols(),
                CertificateChainPolicy = trustManagerFactory?.Clone(),
                ClientCertificateRequired = clientAuth != ClientAuth.NONE,
                RemoteCertificateValidationCallback = verify,
                ServerCertificateContext = keyManagerFactory,
                ServerCertificateSelectionCallback = keyManagerFactory == null ? serverKeyManager : null,
            };
            engine = new MsQuicSslEngine(this, peerHost, peerPort, null, options);
        }
        else
        {
            SslClientAuthenticationOptions options = new SslClientAuthenticationOptions
            {
                TargetHost = peerHost,
                ApplicationProtocols = ApplicationProtocols(),
                CertificateChainPolicy = trustManagerFactory?.Clone(),
                RemoteCertificateValidationCallback = verify,
                LocalCertificateSelectionCallback = clientKeyManager,
            };
            if (keyManagerFactory != null)
            {
                options.ClientCertificates = new X509CertificateCollection { keyManagerFactory.TargetCertificate };
            }
            engine = new MsQuicSslEngine(this, peerHost, peerPort, options, null);
        }
        return engine;
    }

    // The decision of the trust managers (BoringSSLCertificateVerifyCallback), as JdkSslContext decides it: a server
    // without client authentication accepts any client; a missing peer certificate fails a client and a server that
    // requires one (SSL_VERIFY_FAIL_IF_NO_PEER_CERT); without endpoint identification (a null or empty algorithm) the
    // host name check is not part of the decision; a TrustManager callback decides with SslStream's verdict, otherwise
    // the certificate is trusted when the platform found no error with the system trust or the CertificateChainPolicy.
    private bool VerifyRemoteCertificate(object sender, X509Certificate certificate, X509Chain chain,
                                         SslPolicyErrors sslPolicyErrors)
    {
        if (server && clientAuth == ClientAuth.NONE)
        {
            return true;
        }
        if (certificate == null)
        {
            return server && clientAuth != ClientAuth.REQUIRE;
        }
        if (string.IsNullOrEmpty(endpointIdentificationAlgorithm))
        {
            sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
        }
        if (trustManager != null)
        {
            return trustManager(sender, certificate, chain, sslPolicyErrors);
        }
        return sslPolicyErrors == SslPolicyErrors.None;
    }

    // The package-private helpers of QuicheQuicSslContext that QuicSslContextBuilder calls; they reach the protected
    // internal factories of SslContext (docs/handler-ssl-design.md).
    internal static X509Certificate2[] ToX509Certificates0(FileInfo file)
    {
        return SslContext.ToX509Certificates(file);
    }

    internal static AsymmetricAlgorithm ToPrivateKey0(FileInfo keyFile, string keyPassword)
    {
        return SslContext.ToPrivateKey(keyFile, keyPassword);
    }

    internal static X509ChainPolicy BuildTrustManagerFactory0(X509Certificate2[] certCollection)
    {
        return SslContext.BuildTrustManagerFactory(certCollection, null, null);
    }

    internal static SslStreamCertificateContext BuildKeyManagerFactory0(X509Certificate2[] certChain,
                                                                     AsymmetricAlgorithm key, string keyPassword)
    {
        return SslContext.BuildKeyManagerFactory(certChain, null, key, keyPassword, null, null);
    }

    // QuicheQuicApplicationProtocolNegotiator: the ALPN protocols of the builder (none for null).
    private sealed class ProtocolNegotiator : IApplicationProtocolNegotiator
    {
        private readonly IList<string> protocols;

        internal ProtocolNegotiator(string[] protocols)
        {
            this.protocols = protocols == null ? new List<string>() : new List<string>(protocols);
        }

        public IList<string> Protocols()
        {
            return protocols;
        }
    }

    // QuicheQuicSslSessionContext: msquic issues and rotates session tickets itself, so ticket keys are unsupported.
    private sealed class MsQuicSslSessionContext : IQuicSslSessionContext
    {
        public void SetTicketKeys(params SslSessionTicketKey[] keys)
        {
            throw new NotSupportedException("System.Net.Quic manages session ticket keys inside msquic");
        }
    }
}
