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

using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the QuicSslEngine of the msquic implementation (stage 2, docs/codec-classes-quic-porting.md), the counterpart
// of the JNI unit QuicheQuicSslEngine (jvm-only-replaced). It holds the SslClientAuthenticationOptions or
// SslServerAuthenticationOptions that MsQuicSslContext built for one connection, which MsQuicChannel hands to
// QuicClientConnectionOptions.ClientAuthenticationOptions or returns as QuicServerConnectionOptions
// .ServerAuthenticationOptions, and, once QuicConnection.ConnectAsync/QuicListener.AcceptConnectionAsync completed, the
// negotiated facts of the QuicConnection (NegotiatedApplicationProtocol, RemoteCertificate, NegotiatedCipherSuite,
// SslProtocol). The peer's chain is the one the remote certificate validation callback received, as SslHandler reports
// it. The facts are written once on the channel's event loop and read from any thread, hence the volatile fields.
internal sealed class MsQuicSslEngine : QuicSslEngine
{
    private readonly bool clientMode;
    private readonly string peerHost;
    private readonly int peerPort;
    private readonly SslClientAuthenticationOptions clientOptions;
    private readonly SslServerAuthenticationOptions serverOptions;

    private volatile string applicationProtocol;
    private volatile X509Certificate2[] peerCertificates;
    private volatile X509Certificate2[] validatedChain;
    private TlsCipherSuite cipherSuite;
    private SslProtocols protocol;

    internal MsQuicSslEngine(MsQuicSslContext context, string peerHost, int peerPort,
                             SslClientAuthenticationOptions clientOptions,
                             SslServerAuthenticationOptions serverOptions)
    {
        Context = context;
        this.peerHost = peerHost;
        this.peerPort = peerPort;
        this.clientOptions = clientOptions;
        this.serverOptions = serverOptions;
        clientMode = clientOptions != null;
    }

    // The context that created this engine (the SNI mapping of a server context selects another one).
    internal MsQuicSslContext Context { get; }

    public override bool GetUseClientMode()
    {
        return clientMode;
    }

    public override string GetPeerHost()
    {
        return peerHost;
    }

    public override int GetPeerPort()
    {
        return peerPort;
    }

    public override SslClientAuthenticationOptions ClientAuthenticationOptions()
    {
        return clientOptions;
    }

    public override SslServerAuthenticationOptions ServerAuthenticationOptions()
    {
        return serverOptions;
    }

    public override string GetApplicationProtocol()
    {
        return applicationProtocol;
    }

    public override X509Certificate2[] GetPeerCertificates()
    {
        X509Certificate2[] certificates = peerCertificates;
        return certificates == null ? null : (X509Certificate2[]) certificates.Clone();
    }

    public override TlsCipherSuite GetCipherSuite()
    {
        lock (this)
        {
            return cipherSuite;
        }
    }

    public override SslProtocols GetProtocol()
    {
        lock (this)
        {
            return protocol;
        }
    }

    // Called by the remote certificate validation callback of the options: the peer's certificate followed by the chain
    // the platform built for it.
    internal void RecordPeerChain(X509Certificate certificate, X509Chain chain)
    {
        if (certificate == null)
        {
            validatedChain = null;
            return;
        }

        X509Certificate2 leaf = certificate as X509Certificate2 ??
                X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        int count = chain == null ? 0 : chain.ChainElements.Count;
        X509Certificate2[] certificates = new X509Certificate2[count == 0 ? 1 : count];
        certificates[0] = leaf;

        for (int i = 1; i < count; i++)
        {
            certificates[i] = chain.ChainElements[i].Certificate;
        }

        validatedChain = certificates;
    }

    // Records the facts negotiated by the handshake of the connection.
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    internal void HandshakeCompleted(QuicConnection connection)
    {
        SslApplicationProtocol alpn = connection.NegotiatedApplicationProtocol;
        applicationProtocol = alpn.Protocol.IsEmpty ? string.Empty : Encoding.UTF8.GetString(alpn.Protocol.Span);
        X509Certificate2[] chain = validatedChain;
        if (chain == null && connection.RemoteCertificate != null)
        {
            X509Certificate remote = connection.RemoteCertificate;
            chain = new[]
            {
                remote as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(remote.GetRawCertData())
            };
        }

        peerCertificates = chain;

        lock (this)
        {
            cipherSuite = connection.NegotiatedCipherSuite;
            protocol = connection.SslProtocol;
        }
    }
}
