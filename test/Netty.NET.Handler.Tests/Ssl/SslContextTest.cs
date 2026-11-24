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

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: java.security.PrivateKey is AsymmetricAlgorithm, File is FileInfo and X509Certificate X509Certificate2 (ssl design
// decision 3, src/Netty.NET.Handler/Ssl/SslContext.cs); SslContext.toPrivateKey and toX509Certificates are protected
// static members reached through the package, which are protected internal here (the three-argument toPrivateKey is
// internal). getClass() is GetType() and both ResourcesUtil.getFile(getClass(), name) and
// new File(getClass().getResource(name).getFile()) are ResourcesUtil.GetFile(GetType(), name), the file deployed next to
// the test assembly (Netty.NET.Handler.Tests.csproj). The anonymous Executables are lambdas. The JCA exceptions that a
// failed key or certificate parse throws (IOException from EncryptedPrivateKeyInfo, InvalidKeySpecException from
// KeyFactory.generatePrivate, CertificateException) are the CryptographicException the BCL throws for them
// (SslContext.cs: "the last import failure (CryptographicException, the BCL report of every GeneralSecurityException above
// and of a malformed structure) is rethrown"). ReferenceCountUtil.release(sslContext) is kept: JdkSslContext is not
// reference counted, so it returns false, as in Java.
//
// The PKCS #1 tests that need Bouncy Castle to read a key are not ported: testPkcs1Des3EncryptedRsa,
// testPkcs1AesEncryptedRsa (the legacy OpenSSL "Proc-Type: 4,ENCRYPTED" PEM form, which only BouncyCastlePemReader reads),
// testPkcs1UnencryptedDsa, testPkcs1Des3EncryptedDsa and testPkcs1AesEncryptedDsa (PKCS #1 "DSA PRIVATE KEY", which only
// Bouncy Castle reads). Bouncy Castle is library-not-ported (owner decision 2026-11-24, docs/handler-ssl-design.md), and the
// BCL PEM readers that replace its first attempt (SslContext.ReadPemPrivateKey) do not read either form (measured, see
// SslContext.cs); the fallback PKCS #8 path rejects them as the Java fallback does. Their failure cases (no, empty or
// wrong password) do not need Bouncy Castle and are ported. The five tests are recorded as CLR-inapplicable in
// docs/handler-porting-manifest.json.
public abstract class SslContextTest
{

    [Fact]
    public void TestUnencryptedEmptyPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(
                    ResourcesUtil.GetFile(GetType(), "test2_unencrypted.pem"), "");
        });
    }

    [Fact]
    public void TestUnEncryptedNullPassword()
    {
        AsymmetricAlgorithm key = SslContext.ToPrivateKey(
                ResourcesUtil.GetFile(GetType(), "test2_unencrypted.pem"), null);
        Assert.NotNull(key);
    }

    [Fact]
    public void TestEncryptedEmptyPassword()
    {
        AsymmetricAlgorithm key = SslContext.ToPrivateKey(
                ResourcesUtil.GetFile(GetType(), "test_encrypted_empty_pass.pem"), "");
        Assert.NotNull(key);
    }

    [Fact]
    public void TestEncryptedNullPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(
                    ResourcesUtil.GetFile(GetType(), "test_encrypted_empty_pass.pem"), null);
        });
    }

    [Fact]
    public void TestSslContextWithEncryptedPrivateKey()
    {
        FileInfo keyFile = ResourcesUtil.GetFile(GetType(), "test_encrypted.pem");
        FileInfo crtFile = ResourcesUtil.GetFile(GetType(), "test.crt");

        ReferenceCountUtil.Release(NewSslContext(crtFile, keyFile, "12345"));
    }

    [Fact]
    public void TestSslContextWithEncryptedPrivateKey2()
    {
        FileInfo keyFile = ResourcesUtil.GetFile(GetType(), "test2_encrypted.pem");
        FileInfo crtFile = ResourcesUtil.GetFile(GetType(), "test2.crt");

        ReferenceCountUtil.Release(NewSslContext(crtFile, keyFile, "12345"));
    }

    [Fact]
    public void TestSslContextWithUnencryptedPrivateKey()
    {
        FileInfo keyFile = ResourcesUtil.GetFile(GetType(), "test_unencrypted.pem");
        FileInfo crtFile = ResourcesUtil.GetFile(GetType(), "test.crt");

        ReferenceCountUtil.Release(NewSslContext(crtFile, keyFile, null));
    }

    // CLR: newSslContext of the two subclasses called the @Deprecated JdkSslClientContext/JdkSslServerContext constructors,
    // which wrap a key that cannot be read in SSLException; they are not ported (deprecated-not-ported, their replacement
    // is SslContextBuilder) and the subclasses build the context with SslContextBuilder.keyManager(File, File, String)/
    // forServer(File, File, String), which report it as IllegalArgumentException ("File does not contain valid private
    // key: ...", src/Netty.NET.Handler/Ssl/SslContextBuilder.cs, as the Java builder does), an ArgumentException whose
    // InnerException is the key's CryptographicException.
    [Fact]
    public void TestSslContextWithUnencryptedPrivateKeyEmptyPass()
    {
        FileInfo keyFile = ResourcesUtil.GetFile(GetType(), "test_unencrypted.pem");
        FileInfo crtFile = ResourcesUtil.GetFile(GetType(), "test.crt");

        ArgumentException e = Assert.Throws<ArgumentException>(() =>
        {
            NewSslContext(crtFile, keyFile, "");
        });
        Assert.IsAssignableFrom<CryptographicException>(e.InnerException);
    }

    // CLR: the assumption asks a JDK SSLEngine whether it rejects the anonymous suite (setEnabledCipherSuites throws
    // "IllegalArgumentException - when one or more of the ciphers named by the parameter is not supported",
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLEngine.html). SslStream has no engine
    // to ask: a cipher suite restriction is a CipherSuitesPolicy, which cannot be created on Windows at all
    // (src/Netty.NET.Handler/Ssl/JdkSslContext.cs SetCipherSuitesPolicy), and the supported names are the TlsCipherSuite
    // names, which include TLS_DH_anon_WITH_DES_CBC_SHA. So the assumption has no counterpart and the test always runs:
    // the context's default cipher suites (filtered from SslUtils.DEFAULT_CIPHER_SUITES) must not contain the anonymous
    // suite.
    [Fact]
    public void TestSupportedCiphers()
    {
        string unsupportedCipher = "TLS_DH_anon_WITH_DES_CBC_SHA";
        FileInfo keyFile = ResourcesUtil.GetFile(GetType(), "test_unencrypted.pem");
        FileInfo crtFile = ResourcesUtil.GetFile(GetType(), "test.crt");

        SslContext sslContext = NewSslContext(crtFile, keyFile, null);
        Assert.DoesNotContain(unsupportedCipher, sslContext.CipherSuites());
        ReferenceCountUtil.Release(sslContext);
    }

    // CLR: the JDK CertificateFactory rejects the explicit EC domain parameters of ec_params_unsupported.pem with a
    // CertificateException, a JDK-only behaviour: X509CertificateLoader loads the certificate, and even its public key as
    // a 256-bit ECDsa key (measured on this host, Windows 11 10.0.26300, .NET 10). The assertion is adapted to the
    // measured behaviour: toX509Certificates returns the one certificate of the file instead of throwing.
    [Fact]
    public void TestUnsupportedParams()
    {
        X509Certificate2[] certs = SslContext.ToX509Certificates(
                ResourcesUtil.GetFile(GetType(), "ec_params_unsupported.pem"));
        Assert.Single(certs);
    }

    protected abstract SslContext NewSslContext(FileInfo crtFile, FileInfo keyFile, string pass);

    [Fact]
    public void TestPkcs1UnencryptedRsa()
    {
        AsymmetricAlgorithm key = SslContext.ToPrivateKey(
                ResourcesUtil.GetFile(GetType(), "rsa_pkcs1_unencrypted.key"), null);
        Assert.NotNull(key);
    }

    [Fact]
    public void TestPkcs8UnencryptedRsa()
    {
        AsymmetricAlgorithm key = SslContext.ToPrivateKey(
                ResourcesUtil.GetFile(GetType(), "rsa_pkcs8_unencrypted.key"), null);
        Assert.NotNull(key);
    }

    [Fact]
    public void TestPkcs8AesEncryptedRsa()
    {
        AsymmetricAlgorithm key = SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "rsa_pkcs8_aes_encrypted.key"),
                "example");
        Assert.NotNull(key);
    }

    [Fact]
    public void TestPkcs8Des3EncryptedRsa()
    {
        AsymmetricAlgorithm key = SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "rsa_pkcs8_des3_encrypted.key"),
                "example");
        Assert.NotNull(key);
    }

    [Fact]
    public void TestPkcs8Pbes2()
    {
        AsymmetricAlgorithm key = SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "rsa_pbes2_enc_pkcs8.key"),
                "12345678", false);
        Assert.NotNull(key);
    }

    [Fact]
    public void TestPkcs1UnencryptedRsaEmptyPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(
                    ResourcesUtil.GetFile(GetType(), "rsa_pkcs1_unencrypted.key"), "");
        });
    }

    [Fact]
    public void TestPkcs1Des3EncryptedRsaNoPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "rsa_pkcs1_des3_encrypted.key"), null);
        });
    }

    [Fact]
    public void TestPkcs1AesEncryptedRsaNoPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "rsa_pkcs1_aes_encrypted.key"), null);
        });
    }

    [Fact]
    public void TestPkcs1Des3EncryptedRsaEmptyPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "rsa_pkcs1_des3_encrypted.key"), "");
        });
    }

    [Fact]
    public void TestPkcs1AesEncryptedRsaEmptyPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "rsa_pkcs1_aes_encrypted.key"), "");
        });
    }

    [Fact]
    public void TestPkcs1Des3EncryptedRsaWrongPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "rsa_pkcs1_des3_encrypted.key"), "wrong");
        });
    }

    [Fact]
    public void TestPkcs1AesEncryptedRsaWrongPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "rsa_pkcs1_aes_encrypted.key"), "wrong");
        });
    }

    [Fact]
    public void TestPkcs1UnencryptedDsaEmptyPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            AsymmetricAlgorithm key = SslContext.ToPrivateKey(
                    ResourcesUtil.GetFile(GetType(), "dsa_pkcs1_unencrypted.key"), "");
        });
    }

    [Fact]
    public void TestPkcs1Des3EncryptedDsaNoPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "dsa_pkcs1_des3_encrypted.key"), null);
        });
    }

    [Fact]
    public void TestPkcs1AesEncryptedDsaNoPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "dsa_pkcs1_aes_encrypted.key"), null);
        });
    }

    [Fact]
    public void TestPkcs1Des3EncryptedDsaEmptyPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "dsa_pkcs1_des3_encrypted.key"), "");
        });
    }

    [Fact]
    public void TestPkcs1AesEncryptedDsaEmptyPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "dsa_pkcs1_aes_encrypted.key"), "");
        });
    }

    [Fact]
    public void TestPkcs1Des3EncryptedDsaWrongPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "dsa_pkcs1_des3_encrypted.key"), "wrong");
        });
    }

    [Fact]
    public void TestPkcs1AesEncryptedDsaWrongPassword()
    {
        Assert.Throws<CryptographicException>(() =>
        {
            SslContext.ToPrivateKey(ResourcesUtil.GetFile(GetType(), "dsa_pkcs1_aes_encrypted.key"), "wrong");
        });
    }
}
