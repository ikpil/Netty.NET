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

using System.IO;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the inherited SslContextTest tests run for this class as in Java. JdkSslClientContext is @Deprecated with
// "@deprecated Use {@link SslContextBuilder} to create {@link JdkSslContext} instances and only use {@link JdkSslContext} in
// your code." and is not ported (deprecated-not-ported), so newSslContext builds the same client context with
// SslContextBuilder: the constructor's trustCertCollectionFile re-initializes the given InsecureTrustManagerFactory, whose
// engineInit does nothing, so the trust is InsecureTrustManagerFactory.INSTANCE alone (a RemoteCertificateValidationCallback,
// src/Netty.NET.Handler/Ssl/Util/InsecureTrustManagerFactory.cs); keyCertChainFile/keyFile/keyPassword are
// keyManager(File, File, String); ciphers null with IdentityCipherSuiteFilter.INSTANCE and session cache size and timeout 0
// are the builder defaults; ApplicationProtocolConfig.DISABLED is applicationProtocolConfig(DISABLED); the provider is JDK.
// SSLException is not thrown here (see SslContextTest.TestSslContextWithUnencryptedPrivateKeyEmptyPass).
//
// testTrustManagerFactoryReturningNullDoesNotThrowNpe and its NullReturningTrustManagerFactorySpi are not ported: they
// subclass javax.net.ssl.TrustManagerFactory with a TrustManagerFactorySpi whose engineGetTrustManagers() returns null and
// check that the null array does not reach the EnhancingX509ExtendedTrustManager/resumption wrapping of the JDK context
// (issue 14488). The TrustManagerFactory is an X509ChainPolicy (ssl design decision 3), which has no trust manager array
// and no SPI, and that wrapping is jvm-only-replaced (EnhancingX509ExtendedTrustManager, ResumptionController), so the
// null case cannot be built; the platform-default trust of a context without a policy is the null CertificateChainPolicy
// of src/Netty.NET.Handler/Ssl/JdkSslContext.cs. The test is recorded as CLR-inapplicable in
// docs/handler-porting-manifest.json. Its original comments:
// Reproduces https://github.com/netty/netty/issues/14488
// Before the fix, wrapIfNeeded did tms.length without a null check, so a
// TrustManagerFactory whose getTrustManagers() returns null surfaced as an
// opaque NullPointerException wrapped in SSLException. After the fix the
// context is built successfully, matching pre-4.1.114 behavior that relied
// on SSLContext.init accepting a null TrustManager[] to use platform defaults.
// TrustManagerFactory must be initialized before SslContextBuilder will accept it;
// without this call the builder throws before reaching the code path under test.
// Success is "did not throw". Before the fix this call produced
// SSLException("failed to initialize the client-side SSL context")
// caused by NullPointerException from wrapIfNeeded.
// The Provider(String, double, String) constructor is deprecated since JDK 9 in
// favor of (String, String, String), but the replacement is unavailable on the
// JDK 8 source level this module targets, so we suppress the deprecation warning.
// no-op
public class JdkSslClientContextTest : SslContextTest
{
    protected override SslContext NewSslContext(FileInfo crtFile, FileInfo keyFile, string pass)
    {
        return SslContextBuilder.ForClient()
                .SslProvider(SslProvider.JDK)
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .KeyManager(crtFile, keyFile, pass)
                .ApplicationProtocolConfig(ApplicationProtocolConfig.DISABLED)
                .Build();
    }
}
