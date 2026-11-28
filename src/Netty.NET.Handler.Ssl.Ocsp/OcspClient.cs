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
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Resolver.Dns;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using HttpVersion = Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Ssl.Ocsp;

// CLR: X509Certificate is X509Certificate2, and the Bouncy Castle OCSP types are the CLR-only replacements of
// OcspAsn1.cs: CertificateID is CertificateId, OCSPReqBuilder OcspReqBuilder, OCSPResp OcspResp, BasicOCSPResp
// BasicOcspResp, Extension OcspExtension, and the DigestCalculatorProvider is the hash algorithm the CertificateId
// computes with (HASH_SHA1 is OcspObjectIdentifiers.IdSha1). OCSPException, OperatorCreationException and the JCA
// CertificateException are CryptographicException (docs/pkitesting-porting.md maps CertificateException so);
// IllegalStateException is InvalidOperationException and IllegalArgumentException ArgumentException
// (docs/codec-base-porting.md). Promise<T> is TaskCompletionSource<T> (created without
// RunContinuationsAsynchronously, as Http2CodecUtil.NewPromise()), and each listener is a continuation on Common's
// EventExecutorTaskScheduler over the event loop of the IoTransport, the executor of the Java promises and of the
// DnsNameResolver, which notifies at once on that event loop (docs/handler-porting.md, "Task promises"). eventLoop.execute
// takes a lambda for the anonymous Runnable. SecureRandom is RandomNumberGenerator. java.net.URL is Uri; InetAddress is
// IPAddress, and dnsNameResolver.resolve(host) is ResolveAsync(host). The JDK's CertPathBuilder ("PKIX") is X509Chain.
internal sealed class OcspClient
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(OcspClient));

    private static readonly RandomNumberGenerator SECURE_RANDOM = RandomNumberGenerator.Create();
    private static readonly int OCSP_RESPONSE_MAX_SIZE = SystemPropertyUtil.GetInt(
            "io.netty.ocsp.responseSize", 1024 * 10);
    public const string OID_OCSP_SIGNING = "1.3.6.1.5.5.7.3.9";

    static OcspClient()
    {
        logger.Debug("-Dio.netty.ocsp.responseSize: {} bytes", OCSP_RESPONSE_MAX_SIZE);
    }

    /**
     * Query the certificate status using OCSP
     *
     * @param x509Certificate       Client {@link X509Certificate} to validate
     * @param issuer                {@link X509Certificate} issuer of client certificate
     * @param validateResponseNonce Set to {@code true} to enable OCSP response validation
     * @param ioTransport           {@link IoTransport} to use
     * @param responsePromise      {@link Promise} of {@link BasicOCSPResp}
     */
    // CLR: the nonce extension carries the 16 nonce octets as its extnValue, as Bouncy Castle's
    // new Extension(id_pkix_ocsp_nonce, false, new DEROctetString(nonce)) does; derNonce is those octets, which
    // validateNonce compares with the extnValue of the response's nonce extension.
    internal static void Query(X509Certificate2 x509Certificate,
                                        X509Certificate2 issuer, bool validateResponseNonce,
                                        IoTransport ioTransport, DnsNameResolver dnsNameResolver,
                                        TaskCompletionSource<BasicOcspResp> responsePromise)
    {
        IEventLoop eventLoop = ioTransport.EventLoop();
        eventLoop.Execute(() =>
        {
            try
            {
                CertificateId certificateID = CertificateId.Create(OcspObjectIdentifiers.IdSha1, issuer,
                        CertificateId.SerialNumberOf(x509Certificate));

                // Initialize OCSP Request Builder and add CertificateID into it.
                OcspReqBuilder builder = new OcspReqBuilder();
                builder.AddRequest(certificateID);

                // Generate 16-bytes (octets) of nonce and add it into OCSP Request builder.
                // Because as per RFC-8954#2.1:
                //
                //   OCSP responders MUST accept lengths of at least
                //   16 octets and MAY choose to ignore the Nonce extension for requests
                //   where the length of the nonce is less than 16 octets.
                byte[] nonce = new byte[16];
                SECURE_RANDOM.GetBytes(nonce);
                byte[] derNonce = nonce;
                builder.SetRequestExtensions(new[]
                {
                    new OcspExtension(OcspObjectIdentifiers.IdPkixOcspNonce, false, derNonce)
                });

                // Get OCSP URL from Certificate and query it.
                Uri uri = new Uri(ParseOcspUrlFromCertificate(x509Certificate));

                // Find port
                // CLR: Uri.Port is the explicit port, or the scheme's default port when there is none (getPort() is -1
                // then, and getDefaultPort() supplies it).
                int port = uri.Port;

                // Configure path
                string path = UrlPath(uri);
                if (path.Length == 0)
                {
                    path = "/";
                }
                else
                {
                    if (uri.Query.Length != 0)
                    {
                        path = path + '?' + uri.Query.Substring(1);
                    }
                }

                Task<OcspResp> ocspResponsePromise = Query(eventLoop,
                        Unpooled.WrappedBuffer(builder.Build()),
                        uri.Host, port, path, ioTransport, dnsNameResolver);

                // Validate OCSP response
                _ = ocspResponsePromise.ContinueWith(future =>
                {
                    // If Future was successful then we have received OCSP response
                    // We will now validate it.
                    if (future.IsCompletedSuccessfully)
                    {
                        object responseObject;

                        try
                        {
                            responseObject = future.Result.GetResponseObject();
                        }
                        catch (CryptographicException)
                        {
                            // CLR: as in Java, future.cause() of the successful future is null, so the failure is set
                            // with a null cause: DefaultPromise.setFailure(null) throws NullPointerException, which
                            // DefaultPromise logs as a failing listener, and responsePromise stays incomplete.
                            // SetException(null) throws ArgumentNullException, which faults only this discarded
                            // continuation.
                            responsePromise.SetException(Cause(future));
                            return;
                        }

                        if (responseObject is BasicOcspResp basicOcspResp)
                        {
                            ValidateResponse(x509Certificate, responsePromise,
                                    basicOcspResp, derNonce, issuer, validateResponseNonce);
                        }
                        else
                        {
                            // CLR: Class.toString() is "class " and the fully qualified name; the undecoded response
                            // is a byte array here (System.Byte[]), where Bouncy Castle returns a DEROctetString.
                            responsePromise.TrySetException(new CryptographicException("Unsupported OCSP response type: "
                                    + (responseObject == null ? "null" : "class " + responseObject.GetType().FullName)));
                        }
                    }
                    else
                    {
                        responsePromise.TrySetException(Cause(future));
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        new EventExecutorTaskScheduler(() => eventLoop));
            }
            catch (Exception ex)
            {
                responsePromise.TrySetException(ex);
            }
        });
    }

    /**
     * Query the OCSP responder for certificate status using HTTP/1.1
     *
     * @param eventLoop   {@link EventLoop} for HTTP request execution
     * @param ocspRequest {@link ByteBuf} containing OCSP request data
     * @param host        OCSP responder hostname
     * @param port        OCSP responder port
     * @param path        OCSP responder path
     * @param ioTransport {@link IoTransport} to use
     * @return Returns {@link Promise} containing {@link OCSPResp}
     */
    // CLR: the returned Promise is the Task of the TaskCompletionSource that eventLoop.newPromise() creates;
    // ChannelFuture is the Task<IChannel> of ConnectAsync, whose result is the channel(). Boolean.TRUE is a boxed true,
    // as OCSP_PIPELINE_ATTRIBUTE is an AttributeKey<object> (see OcspServerCertificateValidator).
    private static Task<OcspResp> Query(IEventLoop eventLoop, ByteBuf ocspRequest,
                                           string host, int port, string path,
                                           IoTransport ioTransport, DnsNameResolver dnsNameResolver)
    {
        TaskCompletionSource<OcspResp> responsePromise = new TaskCompletionSource<OcspResp>();

        try
        {
            Bootstrap bootstrap = new Bootstrap()
                    .Group(ioTransport.EventLoop())
                    .Option(ChannelOption.TCP_NODELAY, true)
                    .ChannelFactory(ioTransport.SocketChannel())
                    .Attr(OcspServerCertificateValidator.OCSP_PIPELINE_ATTRIBUTE, (object) true)
                    .Handler(new Initializer(responsePromise, 10 * 1000));
            _ = dnsNameResolver.ResolveAsync(host).ContinueWith(future =>
            {

                // If Future was successful then we have successfully resolved OCSP server address.
                // If not, mark 'responsePromise' as failure.
                if (future.IsCompletedSuccessfully)
                {
                    // Get the resolved InetAddress
                    IPAddress hostAddress = future.Result;
                    Task<IChannel> channelFuture = bootstrap.ConnectAsync(hostAddress, port);
                    _ = channelFuture.ContinueWith(f =>
                    {
                        // If Future was successful then connection to OCSP responder was successful.
                        // We will send a OCSP request now
                        if (f.IsCompletedSuccessfully)
                        {
                            IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                                    path, ocspRequest);
                            request.Headers().Add(HttpHeaderNames.HOST, host);
                            request.Headers().Add(HttpHeaderNames.USER_AGENT, "Netty OCSP Client");
                            request.Headers().Add(HttpHeaderNames.CONTENT_TYPE, OcspHttpHandler.OCSP_REQUEST_TYPE);
                            request.Headers().Add(HttpHeaderNames.ACCEPT_ENCODING, OcspHttpHandler.OCSP_RESPONSE_TYPE);
                            request.Headers().Add(HttpHeaderNames.CONTENT_LENGTH, ocspRequest.ReadableBytes);

                            // Send the OCSP HTTP Request
                            _ = f.Result.WriteAndFlushAsync(request);
                        }
                        else
                        {
                            responsePromise.TrySetException(new InvalidOperationException(
                                    "Connection to OCSP Responder Failed", Cause(f)));
                        }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                            new EventExecutorTaskScheduler(() => eventLoop));
                }
                else
                {
                    responsePromise.TrySetException(Cause(future));
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => eventLoop));
        }
        catch (Exception ex)
        {
            responsePromise.TrySetException(ex);
        }

        return responsePromise.Task;
    }

    // CLR: the DigestCalculatorProvider parameter has no counterpart; matchesIssuer computes the issuer hashes with the
    // CertID's own hash algorithm (see CertificateId).
    private static void ValidateResponse(
            X509Certificate2 x509Certificate,
            TaskCompletionSource<BasicOcspResp> responsePromise, BasicOcspResp basicResponse,
            byte[] derNonce, X509Certificate2 issuer, bool validateNonce)
    {
        try
        {
            // Validate number of responses. We only requested for 1 certificate
            // so number of responses must be 1. If not, we will throw an error.
            int responses = basicResponse.GetResponses().Length;
            if (responses != 1)
            {
                responsePromise.TrySetException(
                        new ArgumentException("Expected number of responses was 1 but got: " + responses));
                return;
            }

            CertificateId respCertId = basicResponse.GetResponses()[0].GetCertID();
            if (!respCertId.MatchesIssuer(issuer)
                    || !respCertId.SerialNumber.Equals(CertificateId.SerialNumberOf(x509Certificate)))
            {
                responsePromise.TrySetException(
                        new CryptographicException("OCSP response CertID does not match queried certificate"));
                return;
            }

            if (validateNonce)
            {
                ValidateNonce(basicResponse, derNonce);
            }

            ValidateSignature(basicResponse, issuer);
            responsePromise.TrySetResult(basicResponse);
        }
        catch (Exception ex)
        {
            responsePromise.TrySetException(ex);
        }
    }

    /**
     * Validate OCSP response nonce
     */
    private static void ValidateNonce(BasicOcspResp basicResponse, byte[] encodedNonce)
    {
        OcspExtension nonceExt = basicResponse.GetExtension(OcspObjectIdentifiers.IdPkixOcspNonce);
        if (nonceExt != null)
        {
            byte[] responseNonceString = nonceExt.ExtnValue;
            if (!responseNonceString.AsSpan().SequenceEqual(encodedNonce))
            {
                throw new CryptographicException("Nonce does not match");
            }
        }
        else
        {
            throw new ArgumentException("Nonce is not present");
        }
    }

    /**
     * Validate OCSP response signature
     */
    // CLR: Java's one try block maps OperatorCreationException ("Error validating OCSP-Signature") and
    // CertificateException ("Error while processing certificates for OCSP signature validation") to OCSPException.
    // Both and the OCSPExceptions thrown inside the block are CryptographicException here, so the conversions are caught
    // where they happen: loading the certificates of the response (JcaX509CertificateConverter.getCertificate is
    // X509CertificateLoader.LoadCertificate) and building the verifier (IsSignatureValid). ClassCastException and
    // IllegalArgumentException of getExtendedKeyUsage() are the CryptographicException the BCL throws for a malformed
    // extension, and a missing extension is null, as getExtendedKeyUsage() returns it.
    internal static void ValidateSignature(BasicOcspResp resp, X509Certificate2 issuerCertificate)
    {
        byte[][] certs = resp.GetCerts();

        // If responder certificate is included, validate the chain
        if (certs != null && certs.Length > 0)
        {
            X509Certificate2[] certificates = new X509Certificate2[certs.Length];

            try
            {
                for (int i = 0; i < certs.Length; i++)
                {
                    certificates[i] = X509CertificateLoader.LoadCertificate(certs[i]);
                }
            }
            catch (CryptographicException e)
            {
                throw new CryptographicException(
                        "Error while processing certificates for OCSP signature validation", e);
            }

            // Use the first included certificate to verify the OCSP response signature.
            X509Certificate2 responderCertificate = certificates[0];

            if (!IsIssuingCa(responderCertificate, issuerCertificate))
            {
                // Original certificate issuer has delegated OCSP signing.
                // Responder cert must be authorized to sign OCSP responses.
                List<string> extendedKeyUsage;

                try
                {
                    extendedKeyUsage = GetExtendedKeyUsage(responderCertificate);
                }
                catch (CryptographicException e)
                {
                    throw new CryptographicException(
                            "Responder has invalid or malformed ExtendedKeyUsage extension", e);
                }

                if (extendedKeyUsage == null ||
                    !extendedKeyUsage.Contains(OID_OCSP_SIGNING))
                {
                    throw new CryptographicException("OCSP Responder is not authorized to sign OCSP responses");
                }

                // Build chain from responder certificate to issuer using CertPathBuilder
                ValidateCertificateChain(responderCertificate, certificates, issuerCertificate);
            }

            // Verify OCSP response signature using responder cert
            if (!IsSignatureValid(resp, certificates[0]))
            {
                throw new CryptographicException("OCSP response signature is not valid");
            }
        }
        else
        {
            // Validate signature using issuer certificate
            if (!IsSignatureValid(resp, issuerCertificate))
            {
                throw new CryptographicException("OCSP response signature is not valid");
            }
        }
    }

    // CLR-only helper: providerBuilder.build(certificate) and resp.isSignatureValid(verifier); an algorithm or key the
    // BCL cannot verify with (NotSupportedException of BasicOcspResp.IsSignatureValid) is the OperatorCreationException
    // that validateSignature reports as "Error validating OCSP-Signature".
    private static bool IsSignatureValid(BasicOcspResp resp, X509Certificate2 certificate)
    {
        try
        {
            return resp.IsSignatureValid(certificate);
        }
        catch (NotSupportedException e)
        {
            throw new CryptographicException("Error validating OCSP-Signature", e);
        }
    }

    // CLR-only helper: X509Certificate.getExtendedKeyUsage(), the KeyPurposeId OIDs of the extended key usage extension
    // (2.5.29.37), or null without one.
    private static List<string> GetExtendedKeyUsage(X509Certificate2 certificate)
    {
        X509Extension extension = certificate.Extensions["2.5.29.37"];
        if (extension == null)
        {
            return null;
        }

        X509EnhancedKeyUsageExtension eku = new X509EnhancedKeyUsageExtension(extension, extension.Critical);
        List<string> oids = new List<string>();

        foreach (Oid oid in eku.EnhancedKeyUsages)
        {
            oids.Add(oid.Value);
        }

        return oids;
    }

    /**
     * <a href="https://datatracker.ietf.org/doc/html/rfc6960#section-4.2.2.2">RFC 6960 4.2.2.2</a>:
     * <blockquote>Is the certificate of the CA that issued the certificate in question</blockquote>
     * The name and the key are compared instead of the encoding, so that a CA certificate that was re-issued or
     * cross-signed with the same name and key still matches.
     */
    // CLR: X500Principal.equals compares the canonical RFC 2253 forms, which ignore case
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/security/auth/x500/X500Principal.html#equals(java.lang.Object));
    // here the names are equal when their DER encodings are, or else when their decoded forms are equal ignoring case.
    // PublicKey.equals compares the encoded keys: the SubjectPublicKeyInfo DER of both certificates.
    private static bool IsIssuingCa(X509Certificate2 responderCertificate, X509Certificate2 issuerCertificate)
    {
        return SameName(responderCertificate.SubjectName, issuerCertificate.SubjectName)
            && responderCertificate.PublicKey.ExportSubjectPublicKeyInfo().AsSpan()
                    .SequenceEqual(issuerCertificate.PublicKey.ExportSubjectPublicKeyInfo());
    }

    private static bool SameName(X500DistinguishedName first, X500DistinguishedName second)
    {
        return first.RawData.AsSpan().SequenceEqual(second.RawData)
               || string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
    }

    /**
     * Validates that a certificate chain can be built from the responder certificate to the issuer.
     * Uses Java's CertPathBuilder to construct and validate the chain.
     */
    // CLR: CertPathBuilder.getInstance("PKIX") with the issuer as the only TrustAnchor and the response's certificates
    // as the CertStore. A PKIX trust anchor can be any CA certificate, while the BCL chain engine anchors a chain at a
    // self-signed root: with the issuer (an intermediate CA, as in validateSignatureWithIncludedChainSucceeds and real
    // delegated responders) as the only CustomTrustStore certificate, Windows reports PartialChain (measured). So the
    // X509Chain is built from the responder certificate with the response's certificates and the issuer in its
    // ExtraStore (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy?view=net-10.0),
    // and the path is the part before the first chain element that is the trust anchor, matched by name and public key
    // as IsIssuingCa compares them (PKIX matches a TrustAnchor by its name and key). The path is trusted when that
    // element exists and no element of the path has a status (signature, validity period, basic constraints, ...);
    // the statuses that concern the rest of the chain above the anchor (UntrustedRoot, PartialChain) do not count, as
    // PKIX stops at the anchor. setRevocationEnabled(false) is X509RevocationMode.NoCheck. A path that cannot be built is
    // the CertPathBuilderException, whose message is the chain status here. The CertPath of the result excludes the
    // trust anchor, so its size is the index of the anchor element. The algorithm exceptions of CertStore.getInstance
    // and CertPathBuilder.getInstance have no counterpart.
    private static void ValidateCertificateChain(X509Certificate2 responderCertificate,
                                                 X509Certificate2[] allCerts,
                                                 X509Certificate2 issuerCertificate)
    {
        // Create a CertStore with all the certificates from the OCSP response
        using X509Chain builder = new X509Chain();
        builder.ChainPolicy.ExtraStore.AddRange(allCerts);

        // Set up the target certificate selector for the responder certificate
        // (X509Chain.Build(responderCertificate) below.)

        // Set up trust anchor with the issuer certificate
        builder.ChainPolicy.ExtraStore.Add(issuerCertificate);

        // Build PKIX parameters
        builder.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Don't check revocation when validating OCSP response

        // Build and validate the certificate path
        builder.Build(responderCertificate);
        int anchor = -1;

        for (int i = 0; i < builder.ChainElements.Count; i++)
        {
            if (IsIssuingCa(builder.ChainElements[i].Certificate, issuerCertificate))
            {
                anchor = i;
                break;
            }
        }

        if (anchor <= 0)
        {
            throw new CryptographicException("OCSP responder certificate is not trusted by issuer: "
                    + ChainStatusText(builder.ChainStatus));
        }

        for (int i = 0; i < anchor; i++)
        {
            X509ChainStatus[] status = builder.ChainElements[i].ChainElementStatus;
            if (status.Length != 0)
            {
                throw new CryptographicException("OCSP responder certificate is not trusted by issuer: "
                        + ChainStatusText(status));
            }
        }

        // RFC 6960 https://datatracker.ietf.org/doc/html/rfc6960#section-4.2.2.2
        // "Includes a value of id-kp-OCSPSigning in an extended key usage extension
        // and is issued by the CA that issued the certificate in question as stated above."
        if (anchor > 1)
        {
            throw new CryptographicException("OCSP responder certificate was not issued by the certificate issuer: "
                    + issuerCertificate.SubjectName.Name);
        }
    }

    private static string ChainStatusText(X509ChainStatus[] chainStatus)
    {
        List<string> statuses = new List<string>();

        foreach (X509ChainStatus status in chainStatus)
        {
            statuses.Add(status.Status + " (" + status.StatusInformation.Trim() + ")");
        }

        return string.Join(", ", statuses);
    }

    /**
     * Parse OCSP endpoint URL from Certificate
     *
     * @param cert Certificate to be parsed
     * @return OCSP endpoint URL
     * @throws NullPointerException     If we couldn't locate OCSP responder URL
     * @throws IllegalArgumentException If we couldn't parse X509Certificate into JcaX509CertificateHolder
     */
    // CLR: AuthorityInformationAccess.fromExtensions is the BCL's X509AuthorityInformationAccessExtension, and the
    // id-ad-ocsp access descriptions are its EnumerateOcspUris(), which yields "the URIs ... for the OCSP access method"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509authorityinformationaccessextension.enumerateocspuris?view=net-10.0);
    // a description whose location is not a uniformResourceIdentifier is skipped there, where Java returns the string
    // form of the first one. The certificate needs no holder conversion, so the IllegalArgumentException is the
    // CryptographicException the BCL throws for a malformed extension.
    private static string ParseOcspUrlFromCertificate(X509Certificate2 cert)
    {
        string ocspUrl = null;

        try
        {
            X509Extension extension = cert.Extensions["1.3.6.1.5.5.7.1.1"];
            X509AuthorityInformationAccessExtension aiaExtension = extension == null ? null :
                    new X509AuthorityInformationAccessExtension(extension.RawData, extension.Critical);

            // Lookup for OCSP responder url
            if (aiaExtension != null)
            {
                foreach (string accessLocation in aiaExtension.EnumerateOcspUris())
                {
                    ocspUrl = accessLocation;
                    break;
                }
            }
        }
        catch (CryptographicException e)
        {
            // Though this should never happen
            throw new ArgumentException("Error while parsing X509Certificate into JcaX509CertificateHolder", e);
        }

        if (ocspUrl != null)
        {
            return ocspUrl;
        }

        throw new NoOcspResponderException("Unable to find OCSP responder URL in Certificate");
    }

    // CLR-only helper: URL.getPath(), empty when the URL has no path ("http://host"); Uri.AbsolutePath is "/" then, so the
    // original string tells whether a path follows the authority. Uri.AbsolutePath is the escaped path with its dot
    // segments removed, where getPath() returns it as written.
    private static string UrlPath(Uri uri)
    {
        string original = uri.OriginalString;
        int authority = original.IndexOf("//", StringComparison.Ordinal);
        int end = original.IndexOfAny(new[] { '/', '?', '#' }, authority < 0 ? 0 : authority + 2);
        return end >= 0 && original[end] == '/' ? uri.AbsolutePath : string.Empty;
    }

    // CLR-only helper: future.cause() of a completed Task: the exception of a faulted Task, a TaskCanceledException for a
    // cancelled one (a cancelled Java future's cause is a CancellationException) and null for a successful one.
    private static Exception Cause(Task future)
    {
        if (future.IsFaulted)
        {
            return future.Exception.InnerExceptions.Count == 1 ? future.Exception.InnerException : future.Exception;
        }

        return future.IsCanceled ? new TaskCanceledException(future) : null;
    }

    // CLR: ChannelInitializer<SocketChannel> is ChannelInitializer<ISocketChannel>, and Promise<OCSPResp> is
    // TaskCompletionSource<OcspResp>.
    internal sealed class Initializer : ChannelInitializer<ISocketChannel>
    {

        private readonly TaskCompletionSource<OcspResp> responsePromise;
        private readonly long timeoutMillis;

        internal Initializer(TaskCompletionSource<OcspResp> responsePromise, long timeoutMillis)
        {
            this.responsePromise = ObjectUtil.CheckNotNull(responsePromise, "responsePromise");
            this.timeoutMillis = ObjectUtil.CheckPositive(timeoutMillis, "timeoutMillis");
        }

        protected override void InitChannel(ISocketChannel socketChannel)
        {
            IChannelPipeline pipeline = socketChannel.Pipeline;
            pipeline.AddLast(new HttpClientCodec());
            pipeline.AddLast(new HttpObjectAggregator(OCSP_RESPONSE_MAX_SIZE));
            pipeline.AddLast(new OcspHttpHandler(responsePromise, timeoutMillis));
        }
    }

    private OcspClient()
    {
        // Prevent outside initialization
    }
}
