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

using System;
using Netty.NET.PkiTesting;

namespace Netty.NET.Handler.Ssl.Ocsp.Tests;

// CLR: @BeforeAll checkOcspSupported() assumes OpenSsl.isOcspSupported(); netty-tcnative (OpenSsl,
// ReferenceCountedOpenSslEngine, ReferenceCountedOpenSslContext and OCSP stapling) is not ported
// (docs/handler-ssl-design.md, owner decision 3), so there is no such assumption to evaluate. The two JDK cases do not
// use OpenSSL: they check that the JDK provider rejects enableOcsp(true), which SslContext does for SslProvider.JDK
// ("OCSP is not supported with this SslProvider"), so they run unconditionally here. The other twelve cases drive
// OpenSSL engines (testClientOcspNotEnabled*, testServerOcspNotEnabled*, testClientAcceptingOcspStaple*,
// testClientRejectingOcspStaple*, testServerHasNoStaple*, testClientException*) and are recorded as CLR-inapplicable in
// the manifest; their original comments are kept at the end of the class.
// IllegalArgumentException is ArgumentException (docs/codec-base-porting.md), and the anonymous Executable classes are
// lambdas. ssc.toKeyManagerFactory() is pkitesting's ToKeyManagerFactory(), an SslStreamCertificateContext.
public class OcspTest
{
    private static readonly X509Bundle ssc = new CertificateBuilder()
            .Subject("cn=localhost")
            .SetIsCertificateAuthority(true)
            .BuildSelfSigned();

    [Fact]
    public void TestJdkClientEnableOcsp()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SslContextBuilder.ForClient()
                    .SslProvider(SslProvider.JDK)
                    .EnableOcsp(true)
                    .Build();
        });
    }

    [Fact]
    public void TestJdkServerEnableOcsp()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            SslContextBuilder.ForServer(ssc.ToKeyManagerFactory())
                    .SslProvider(SslProvider.JDK)
                    .EnableOcsp(true)
                    .Build();
        });
    }

    // CLR: the original comments of the CLR-inapplicable OpenSSL cases, kept without their code: the Javadoc of the
    // testClientAcceptingOcspStaple, testClientRejectingOcspStaple, testServerHasNoStaple and testClientException helpers,
    // and the comment in newOcspResponse(), the bogus staple those cases send.

    /**
     * The Server provides an OCSP staple and the Client accepts it.
     */

    /**
     * The Server provides an OCSP staple and the Client rejects it.
     */

    /**
     * The server has OCSP stapling enabled but doesn't provide a staple.
     */

    /**
     * Testing what happens if the {@link OcspClientCallback} throws an {@link Exception}.
     *
     * The exception should bubble up on the client side and the connection should get closed.
     */

    // Assume we got the OCSP staple from somewhere. Using a bogus byte[]
    // in the test because getting a true staple from the CA is quite involved.
    // It requires HttpCodec and Bouncycastle and the test may be very unreliable
    // because the OCSP responder servers are basically being DDoS'd by the
    // Internet.
}
