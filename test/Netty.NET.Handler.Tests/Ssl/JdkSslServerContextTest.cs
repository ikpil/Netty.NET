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

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the inherited SslContextTest tests run for this class as in Java. JdkSslServerContext is @Deprecated with
// "@deprecated Use {@link SslContextBuilder} to create {@link JdkSslContext} instances and only use {@link JdkSslContext} in
// your code." and is not ported (deprecated-not-ported), so newSslContext builds the server context of
// JdkSslServerContext(certChainFile, keyFile, keyPassword) with SslContextBuilder.forServer(File, File, String) and the JDK
// provider (see SslContextTest.TestSslContextWithUnencryptedPrivateKeyEmptyPass for the exception it reports).
//
// Not ported (recorded as CLR-inapplicable in docs/handler-porting-manifest.json):
// - testWrappingOfTrustManager calls the package-private JdkSslServerContext.checkIfWrappingTrustManagerIsSupported(),
//   which asks whether the JDK lets the deprecated server context wrap its X509ExtendedTrustManager in an
//   EnhancingX509ExtendedTrustManager; JdkSslServerContext is not ported and EnhancingX509ExtendedTrustManager is
//   jvm-only-replaced (SslStream reports a host name mismatch as SslPolicyErrors.RemoteCertificateNameMismatch, see
//   src/Netty.NET.Handler/Ssl/JdkSslContext.cs).
// - testTrustManagerFactoryReturningNullDoesNotThrowNpe and its NullReturningTrustManagerFactorySpi: a TrustManagerFactory
//   whose SPI returns a null trust manager array cannot be built from an X509ChainPolicy, and the wrapping it guards is
//   jvm-only-replaced (see JdkSslClientContextTest). It also uses the @Deprecated SelfSignedCertificate.
// Their original comments:
// A TrustManagerFactory whose getTrustManagers() legitimately returns null
// (e.g., asking SSLContext.init to fall back to the JDK default trust store)
// previously NPE'd inside wrapTrustManagerIfNeeded. Verify the server context
// now builds without throwing.
// TrustManagerFactory must be initialized before SslContextBuilder will accept it;
// without this call the builder throws before reaching the code path under test.
// Success is "did not throw". Before the fix this call produced
// SSLException("failed to initialize the server-side SSL context")
// caused by NullPointerException from wrapTrustManagerIfNeeded.
// The Provider(String, double, String) constructor is deprecated since JDK 9 in
// favor of (String, String, String), but the replacement is unavailable on the
// JDK 8 source level this module targets, so we suppress the deprecation warning.
// no-op
public class JdkSslServerContextTest : SslContextTest
{

    protected override SslContext NewSslContext(FileInfo crtFile, FileInfo keyFile, string pass)
    {
        return SslContextBuilder.ForServer(crtFile, keyFile, pass)
                .SslProvider(SslProvider.JDK)
                .Build();
    }
}
