/*
 * Copyright 2025 The Netty Project
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

namespace Netty.NET.Transport.Tests;

// CLR: resumptionWithLargeCertificates, the only test of this class, is CLR-inapplicable (recorded with its reason and
// evidence in the transport porting manifest), so the class holds no code. Its subject is the certificate list limit of
// the OPENSSL provider (netty-tcnative): @EnabledIf("supportKeyManagerAndTLS13") runs it only when OpenSsl.isAvailable(),
// OpenSsl.supportsKeyManagerFactory() and SslProvider.isTlsv13Supported(SslProvider.OPENSSL) hold, and both contexts are
// SslProvider.OPENSSL contexts with OpenSslContextOption.MAX_CERTIFICATE_LIST_BYTES = 32768, so that a client certificate
// carrying an extension of 16384 - 768 to 16384 random bytes (certExtensionSizes(), around the default maximum handshake
// message length) passes a TLSv1.3 handshake with ClientAuth.REQUIRE and an echo (the change that added it is "More
// thorough testing of max_cert_list in OPENSSL provider", netty #14811). OpenSsl, OpenSslContextOption and the OPENSSL
// provider are library-not-ported (owner decision 2026-11-24, docs/handler-ssl-design.md; SslContextBuilder.Build()
// throws NotSupportedException for OPENSSL, docs/handler-porting.md, "Context layer"), so supportKeyManagerAndTLS13() is
// false in this port, as on a JVM without OpenSSL. The JDK provider has no counterpart of the option either: the
// SslStream authentication options have no certificate list or handshake message size setting
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslserverauthenticationoptions?view=net-10.0).
// The @TestInstance(PER_CLASS) @BeforeAll/@AfterAll lifecycle (the ecp256 root certificate and the NIO event loop group),
// supportKeyManagerAndTLS13(), certExtensionSizes() and the test body only serve that test and are not ported.
public class SocketSslLargeCertificateTest
{
}
