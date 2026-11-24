/*
 * Copyright 2020 The Netty Project
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
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Handler.Tests.Ssl;

namespace Netty.NET.Handler.Tests.Ssl.Util;

// CLR: FingerprintTrustManagerFactory hands out its X509TrustManager as a RemoteCertificateValidationCallback (ssl design
// decision 3; see src/Netty.NET.Handler/Ssl/Util/FingerprintTrustManagerFactory.cs): engineGetTrustManagers() is the
// internal EngineGetTrustManagers() (protected in Java, reached through the package), assertInstanceOf(X509TrustManager)
// is Assert.IsAssignableFrom<RemoteCertificateValidationCallback>, and tm.checkClientTrusted(chain, "test") is calling the
// callback with chain[0] and SslPolicyErrors.None. The callback checks the client side only when its sender is a server
// SslStream; with no SslStream the same fingerprint check runs as the server-side check (its failure message says "server"
// instead of "client"). A trusted certificate is a callback that returns true where Java's check returns normally, and an
// untrusted one throws the CryptographicException that stands for CertificateException. IllegalArgumentException is
// ArgumentException and IllegalStateException InvalidOperationException; X509Certificate is X509Certificate2. The static
// initializer's java.lang.Error has no CLR counterpart (every exception derives from Exception), so the failure is
// wrapped in an Exception, as SniHandler wraps causes (src/Netty.NET.Handler/Ssl/SniHandler.cs).
public class FingerprintTrustManagerFactoryTest
{

    private const string FIRST_CERT_SHA1_FINGERPRINT
            = "18:C7:C2:76:1F:DF:72:3B:2A:A7:BB:2C:B0:30:D4:C0:C0:72:AD:84";

    private const string FIRST_CERT_SHA256_FINGERPRINT
            = "1C:53:0E:6B:FF:93:F0:DE:C2:E6:E7:9D:10:53:58:FF:" +
              "DD:8E:68:CD:82:D9:C9:36:9B:43:EE:B3:DC:13:68:FB";

    private static readonly X509Certificate2[] FIRST_CHAIN;

    private static readonly X509Certificate2[] SECOND_CHAIN;

    static FingerprintTrustManagerFactoryTest()
    {
        try
        {
            FIRST_CHAIN = Java8SslTestUtils.LoadCertCollection("test.crt");
            SECOND_CHAIN = Java8SslTestUtils.LoadCertCollection("test2.crt");
        }
        catch (Exception e)
        {
            throw new Exception(e.ToString(), e);
        }
    }

    [Fact]
    public void TestFingerprintWithInvalidLength()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            FingerprintTrustManagerFactory.Builder("SHA-256").Fingerprints("00:00:00").Build();
        });
    }

    [Fact]
    public void TestFingerprintWithUnexpectedCharacters()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            FingerprintTrustManagerFactory.Builder("SHA-256").Fingerprints("00:00:00\n").Build();
        });
    }

    [Fact]
    public void TestWithNoFingerprints()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            FingerprintTrustManagerFactory.Builder("SHA-256").Build();
        });
    }

    [Fact]
    public void TestWithNullFingerprint()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            FingerprintTrustManagerFactory
                    .Builder("SHA-256")
                    .Fingerprints(FIRST_CERT_SHA256_FINGERPRINT, null)
                    .Build();
        });
    }

    // CLR: the SHA-1 constructor FingerprintTrustManagerFactory(String...) is @Deprecated with "@deprecated This deprecated
    // constructor uses SHA-1 that is considered insecure. It is recommended to specify a stronger hash algorithm, such as
    // SHA-256, by calling {@link FingerprintTrustManagerFactory#builder(String)} method." and is not ported (owner
    // decision 2026-11-16); the test builds the same factory with builder("SHA1"), the algorithm that constructor passes
    // to this("SHA1", ...).
    [Fact]
    public void TestValidSHA1Fingerprint()
    {
        FingerprintTrustManagerFactory factory = FingerprintTrustManagerFactory
                .Builder("SHA1")
                .Fingerprints(FIRST_CERT_SHA1_FINGERPRINT)
                .Build();

        Assert.True(factory.EngineGetTrustManagers().Length > 0);
        Assert.IsAssignableFrom<RemoteCertificateValidationCallback>(factory.EngineGetTrustManagers()[0]);
        RemoteCertificateValidationCallback tm = factory.EngineGetTrustManagers()[0];
        Assert.True(tm(null, FIRST_CHAIN[0], null, SslPolicyErrors.None));
    }

    [Fact]
    public void TestTrustedCertificateWithSHA256Fingerprint()
    {
        FingerprintTrustManagerFactory factory = FingerprintTrustManagerFactory
                .Builder("SHA-256")
                .Fingerprints(FIRST_CERT_SHA256_FINGERPRINT)
                .Build();

        X509Certificate2[] keyCertChain = Java8SslTestUtils.LoadCertCollection("test.crt");
        Assert.NotNull(keyCertChain);
        Assert.True(factory.EngineGetTrustManagers().Length > 0);
        Assert.IsAssignableFrom<RemoteCertificateValidationCallback>(factory.EngineGetTrustManagers()[0]);
        RemoteCertificateValidationCallback tm = factory.EngineGetTrustManagers()[0];
        Assert.True(tm(null, keyCertChain[0], null, SslPolicyErrors.None));
    }

    [Fact]
    public void TestUntrustedCertificateWithSHA256Fingerprint()
    {
        FingerprintTrustManagerFactory factory = FingerprintTrustManagerFactory
                .Builder("SHA-256")
                .Fingerprints(FIRST_CERT_SHA256_FINGERPRINT)
                .Build();

        Assert.True(factory.EngineGetTrustManagers().Length > 0);
        Assert.IsAssignableFrom<RemoteCertificateValidationCallback>(factory.EngineGetTrustManagers()[0]);
        RemoteCertificateValidationCallback tm = factory.EngineGetTrustManagers()[0];

        Assert.Throws<CryptographicException>(() =>
        {
            tm(null, SECOND_CHAIN[0], null, SslPolicyErrors.None);
        });
    }

}
