/*
 * Copyright 2015 The Netty Project
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

// CLR: testSslRenegotiationRejected, the only test of this class, is CLR-inapplicable (recorded with its reason and
// evidence in the transport porting manifest), so the class holds no code. Its subject is an OpenSSL server rejecting a
// client-initiated renegotiation: after the first handshake the client restricts its engine to the first enabled cipher
// suite and calls renegotiate(), and the test asserts that the server's exceptionCaught receives a DecoderException
// caused by SSLHandshakeException and that the client's second handshake event fails with ClosedChannelException.
// The source runs it only with OpenSSL: @DisabledIf("openSslNotAvailable"), assumeTrue(OpenSsl.isRenegotiationSupported())
// and assumeTrue(OpenSsl.isAvailable()), and data() puts only OpenSslServerContext servers into its rows (none without
// OpenSSL). OpenSsl and OpenSslServerContext are library-not-ported (netty-tcnative; owner decision 2026-11-24,
// docs/handler-ssl-design.md), and the client context, the @Deprecated JdkSslClientContext, is deprecated-not-ported
// (its replacement is SslContextBuilder). The scenario has no SslStream counterpart either: SslStream renegotiates only
// as a server (SslStream.NegotiateClientCertificateAsync, "Negotiates the client certificate on the authenticated
// connection",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.negotiateclientcertificateasync?view=net-10.0),
// so a client SslHandler.Renegotiate() fails its new handshake with NotSupportedException and closes the channel without
// sending a renegotiation request to the server (docs/handler-porting.md, "SslHandler on SslStream" and "Measurements and
// platform limits"). The engine calls of the body (setEnabledProtocols, getEnabledCipherSuites, setEnabledCipherSuites)
// would also need the internal SslHandler.Engine(), and SslStream takes protocols and cipher suites only through its
// authentication options. data(), newSslHandler, reset() and TestHandler only serve that test and are not ported. The
// comments of the test body:
// As we test renegotiation we should use a protocol that support it.
// Use the first previous enabled ciphersuite and try to renegotiate.
public class SocketSslClientRenegotiateTest : AbstractSocketTest
{
}
