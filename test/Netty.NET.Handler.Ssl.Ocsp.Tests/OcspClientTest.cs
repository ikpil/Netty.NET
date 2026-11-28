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
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.PkiTesting;
using Netty.NET.Resolver.Dns;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using HttpVersion = Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Ssl.Ocsp.Tests;

// CLR: the Bouncy Castle builders are the CLR-only builders of OcspTestBuilders.cs, and the module's Bouncy Castle
// types are the replacements of OcspAsn1.cs (BasicOCSPResp is BasicOcspResp, CertificateID CertificateId, and an
// X509CertificateHolder is the X509Certificate2 itself). OCSPException is CryptographicException; assertThrows is
// Assert.Throws, assertThatThrownBy(...).hasMessageContaining is Assert.Throws with Assert.Contains on the message, and
// assertDoesNotThrow is a direct call (an exception fails the test). @ParameterizedTest @ValueSource(strings) is
// [Theory] [InlineData]. Promise<BasicOCSPResp> is a TaskCompletionSource<BasicOcspResp>; promise.get() awaits its Task
// and promise.await() waits for it to complete. The package-private OcspClient members are internal, and the protected
// createDefaultResolver is reached through OcspServerCertificateValidator.InvokeCreateDefaultResolver.
public class OcspClientTest : AbstractOcspTest
{
    // CLR: HttpsURLConnection.getServerCertificates() is the chain an SslStream to the host builds for the server
    // certificate (the remote certificate followed by its issuers), captured in its validation callback, which accepts
    // the certificate when SslStream found no error, as the JDK's default trust does; disconnect() disposes the stream
    // and the TcpClient. The test needs the internet, as the Java test does.
    [Theory(Timeout = 60000)]
    [InlineData("https://apple.com")]
    public async Task SimpleOcspQueryTest(string urlString)
    {
        Uri url = new Uri(urlString);
        using TcpClient httpsConnection = new TcpClient();
        await httpsConnection.ConnectAsync(url.Host, url.Port, TestContext.Current.CancellationToken);
        X509Certificate2[] certs = null;
        using SslStream stream = new SslStream(httpsConnection.GetStream(), false,
                (sender, certificate, chain, errors) =>
                {
                    certs = new X509Certificate2[chain.ChainElements.Count];

                    for (int i = 0; i < certs.Length; i++)
                    {
                        certs[i] = X509CertificateLoader.LoadCertificate(chain.ChainElements[i].Certificate.RawData);
                    }

                    return errors == SslPolicyErrors.None;
                });
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = url.Host },
                TestContext.Current.CancellationToken);

        // Pull server certificates for validation
        X509Certificate2 serverCert = certs[0];
        X509Certificate2 certIssuer = certs[1];

        IoTransport transport = CreateDefaultTransport();
        TaskCompletionSource<BasicOcspResp> promise = new TaskCompletionSource<BasicOcspResp>();
        OcspClient.Query(serverCert, certIssuer, false,
                transport, OcspServerCertificateValidator.InvokeCreateDefaultResolver(transport), promise);
        BasicOcspResp basicOCSPResp = await promise.Task;

        // 'null' means certificate is valid
        Assert.Null(basicOCSPResp.GetResponses()[0].GetCertStatus());
    }

    [Fact]
    public void ValidateSignatureWithIncludedChainSucceeds()
    {
        X509Bundle rootIssuer = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=SomeRootCA")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();

        X509Bundle intermediateIssuer = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=SomeIntermediateCA")
                .SetIsCertificateAuthority(true)
                .BuildIssuedBy(rootIssuer);

        X509Bundle ocspResponder = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=SomeOCSPResponder")
                .AddExtendedKeyUsageOcspSigning()
                .BuildIssuedBy(intermediateIssuer);

        // Create actual OCSP response with the responder's certificate
        X509Certificate2 responderHolder = ocspResponder.GetCertificate();
        X509Certificate2 intermediateHolder = intermediateIssuer.GetCertificate();

        // Create a minimal BasicOCSPResp that contains the certificate chain
        BasicOcspResp resp = CreateBasicOcspResponse(
                ocspResponder,
                new X509Certificate2[] { responderHolder, intermediateHolder }
        );

        OcspClient.ValidateSignature(resp, intermediateIssuer.GetCertificate());
    }

    [Fact]
    public void ValidateSignatureWithInvalidChainThrows()
    {
        // Build an unrelated responder chain so nothing is signed by the provided issuer (using RSA)
        X509Bundle issuerBundle = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=Issuer")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();

        // Different CA
        X509Bundle otherRoot = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=SomeRootCA")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();

        X509Bundle otherIntermediate = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=SomeIntermediateCA")
                .SetIsCertificateAuthority(true)
                .BuildIssuedBy(otherRoot);

        X509Bundle otherResponder = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=SomeResponder")
                .BuildIssuedBy(otherIntermediate);

        X509Certificate2 responderHolder = otherResponder.GetCertificate();
        X509Certificate2 intermediateHolder = otherIntermediate.GetCertificate();

        // Create actual OCSP response with untrusted chain
        BasicOcspResp resp = CreateBasicOcspResponse(
                otherResponder,
                new X509Certificate2[] { responderHolder, intermediateHolder }
        );

        Assert.Throws<CryptographicException>(() =>
                OcspClient.ValidateSignature(resp, issuerBundle.GetCertificate())
        );
    }

    [Fact]
    public void ValidateDelegateResponderWithoutEkuMustThrow()
    {
        X509Bundle caRoot = new CertificateBuilder()
            .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
            .Subject("CN=TrustedRootCA")
            .SetIsCertificateAuthority(true)
            .BuildSelfSigned();

        X509Bundle badActorCert = new CertificateBuilder()
            .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
            .Subject("CN=BadActorServer")
            .BuildIssuedBy(caRoot);

        X509Certificate2 badActorHolder = badActorCert.GetCertificate();

        BasicOcspResp forgedResponse = CreateBasicOcspResponse(
            badActorCert,
            new X509Certificate2[] { badActorHolder }
        );

        CryptographicException e = Assert.Throws<CryptographicException>(
            () => OcspClient.ValidateSignature(forgedResponse, caRoot.GetCertificate()));
        Assert.Contains("OCSP Responder is not authorized to sign OCSP responses", e.Message);
    }

    [Fact]
    public void ValidateDirectResponderWithoutEkuMustNotThrow()
    {
        X509Bundle caRoot = new CertificateBuilder()
            .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
            .Subject("CN=TrustedRootCA")
            .SetIsCertificateAuthority(true)
            .BuildSelfSigned();

        BasicOcspResp response = CreateBasicOcspResponse(
            caRoot,
            new X509Certificate2[] { caRoot.GetCertificate() }
        );

        OcspClient.ValidateSignature(response, caRoot.GetCertificate());
    }

    // CLR: assertThat(...).isNotEqualTo compares the certificates with X509Certificate.equals, which compares their
    // encodings; here the DER encodings are compared.
    [Fact]
    public void ValidateSignatureWithReissuedIssuerCertificateSucceeds()
    {
        X509Bundle caRoot = new CertificateBuilder()
            .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
            .Subject("CN=TrustedRootCA")
            .SetIsCertificateAuthority(true)
            .BuildSelfSigned();

        X509Bundle reissuedCaRoot = new CertificateBuilder()
            .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
            .Subject("CN=TrustedRootCA")
            .SetIsCertificateAuthority(true)
            .KeyPair(caRoot.GetKeyPair())
            .BuildIssuedBy(caRoot);

        Assert.NotEqual(caRoot.GetCertificate().RawData, reissuedCaRoot.GetCertificate().RawData);

        X509Certificate2 reissuedHolder = reissuedCaRoot.GetCertificate();
        BasicOcspResp resp = CreateBasicOcspResponse(reissuedCaRoot, new X509Certificate2[] { reissuedHolder });

        OcspClient.ValidateSignature(resp, caRoot.GetCertificate());
    }

    [Fact]
    public void ValidateSignatureWithIndirectlyIssuedResponderThrows()
    {
        X509Bundle caRoot = new CertificateBuilder()
            .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
            .Subject("CN=TrustedRootCA")
            .SetIsCertificateAuthority(true)
            .BuildSelfSigned();

        // Chains up to caRoot, but did not issue the certificate in question.
        X509Bundle intermediateCa = new CertificateBuilder()
            .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
            .Subject("CN=SomeIntermediateCA")
            .SetIsCertificateAuthority(true)
            .BuildIssuedBy(caRoot);

        X509Bundle indirectResponder = new CertificateBuilder()
            .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
            .Subject("CN=IndirectOCSPResponder")
            .AddExtendedKeyUsageOcspSigning()
            .BuildIssuedBy(intermediateCa);

        X509Certificate2 responderHolder = indirectResponder.GetCertificate();
        X509Certificate2 intermediateHolder = intermediateCa.GetCertificate();

        BasicOcspResp forgedResponse = CreateBasicOcspResponse(indirectResponder,
            new X509Certificate2[] { responderHolder, intermediateHolder });

        Assert.Throws<CryptographicException>(
            () => OcspClient.ValidateSignature(forgedResponse, caRoot.GetCertificate()));
    }

    // CLR: the anonymous ChannelOutboundHandlerAdapter is the nested ForgedResponseHandler, whose ConnectAsync returns a
    // completed Task (promise.setSuccess()) and then fires channelActive and the forged response on the channel's event
    // loop. The GeneralName/AccessDescription/AuthorityInformationAccess encoding is OcspTestBuilders'
    // AuthorityInformationAccess.OcspUri. resolver.close() is Dispose() and group.shutdownGracefully() discards the Task.
    [Theory]
    [InlineData("unrelated-certificate")]
    [InlineData("non-basic-response-type")]
    [InlineData("null-response-bytes")]
    public async Task TestCertIdBypass(string scenario)
    {
        X509Bundle caRoot = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=TrustedRootCA")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();

        byte[] aia = AuthorityInformationAccess.OcspUri("http://localhost/");
        X509Bundle targetCert = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=TargetServer")
                .AddExtensionOctetString("1.3.6.1.5.5.7.1.1", false, aia)
                .BuildIssuedBy(caRoot);

        byte[] forgedResponseEncoded = ForgedResponse(scenario, caRoot);
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            IoTransport transport = IoTransport.Create(group.Next(), () =>
            {
                NioSocketChannel channel = new NioSocketChannel();
                channel.Pipeline.AddFirst(new ForgedResponseHandler(forgedResponseEncoded));
                return channel;
            }, () => new NioDatagramChannel());

            DnsNameResolver resolver = OcspServerCertificateValidator.InvokeCreateDefaultResolver(transport);
            TaskCompletionSource<BasicOcspResp> promise = new TaskCompletionSource<BasicOcspResp>();
            OcspClient.Query(
                    targetCert.GetCertificate(), caRoot.GetCertificate(), false,
                    transport, resolver, promise);

            await ((Task) promise.Task).ContinueWith(static _ =>
            {
            }, TestContext.Current.CancellationToken,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            Assert.False(promise.Task.IsCompletedSuccessfully);
            resolver.Dispose();
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private sealed class ForgedResponseHandler : ChannelOutboundHandlerAdapter
    {
        private readonly byte[] forgedResponseEncoded;

        internal ForgedResponseHandler(byte[] forgedResponseEncoded)
        {
            this.forgedResponseEncoded = forgedResponseEncoded;
        }

        public override Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress,
                                          EndPoint localAddress, CancellationToken cancellationToken)
        {
            ctx.Executor.Execute(() =>
            {
                ctx.Pipeline.FireChannelActive();
                DefaultFullHttpResponse httpResponse = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1,
                        HttpResponseStatus.OK, Unpooled.WrappedBuffer(forgedResponseEncoded));
                httpResponse.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "application/ocsp-response");
                httpResponse.Headers().Set(HttpHeaderNames.CONTENT_LENGTH,
                        httpResponse.Content.ReadableBytes);

                ctx.Pipeline.FireChannelRead(httpResponse);
            });
            return Task.CompletedTask;
        }
    }

    private static byte[] ForgedResponse(string scenario, X509Bundle caRoot)
    {
        if ("non-basic-response-type".Equals(scenario))
        {
            return OcspRespBuilder.Encode(OcspResponseStatus.Successful, "1.2.3.4.5", new byte[] { 0x30, 0x00 });
        }
        if ("null-response-bytes".Equals(scenario))
        {
            return OcspRespBuilder.Encode(OcspResponseStatus.Unauthorized, null, null);
        }

        X509Certificate2 caHolder = caRoot.GetCertificate();
        BasicOcspResp forgedBasicResp = CreateBasicOcspResponse(caRoot, new X509Certificate2[] { caHolder });
        return OcspRespBuilder.Build(OcspResponseStatus.Successful, forgedBasicResp);
    }

    // CLR: new CertificateID(digestCalculatorProvider.get(HASH_SHA1), holder, serial) is CertificateId.Create with SHA-1,
    // RespID(holder.getSubject()) the subject name, and JcaContentSignerBuilder("SHA256withRSA").build(private key) a
    // ContentSigner over the bundle's RSA key.
    private static BasicOcspResp CreateBasicOcspResponse(X509Bundle responderBundle,
                                                         X509Certificate2[] certChain)
    {
        X509Bundle dummyCert = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.rsa2048)
                .Subject("CN=DummyCert")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();

        // Create certificate ID for OCSP response
        CertificateId certId = CertificateId.Create(
                OcspObjectIdentifiers.IdSha1,
                dummyCert.GetCertificate(),
                CertificateId.SerialNumberOf(dummyCert.GetCertificate())
        );

        // Create response builder with responder ID based on certificate
        X509Certificate2 responderHolder = responderBundle.GetCertificate();
        RespId respID = new RespId(responderHolder.SubjectName);

        BasicOcspRespBuilder respBuilder = new BasicOcspRespBuilder(respID);

        // Add response for the certificate (status: good)
        respBuilder.AddResponse(certId, CertificateStatus.GOOD);

        // Build and sign the response with the responder's private key
        ContentSigner signer = new ContentSigner("SHA256withRSA",
                (AsymmetricAlgorithm) responderBundle.GetKeyPair());

        return respBuilder.Build(signer, certChain, DateTimeOffset.UtcNow);
    }
}
