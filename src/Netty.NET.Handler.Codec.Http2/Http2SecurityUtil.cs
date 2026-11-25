/*
 * Copyright 2014 The Netty Project
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

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Provides utilities related to security requirements specific to HTTP/2.
 */
// CLR: the final class with a private constructor is a static class. Collections.unmodifiableList is ReadOnlyCollection
// (as the handler port's JdkSslContext), exposed as IList<string>, which SslContextBuilder.Ciphers(IEnumerable<string>,
// ICipherSuiteFilter) takes. The names stay the JSSE names, which are the IANA names: they are also the names of the
// System.Net.Security.TlsCipherSuite members that the handler's SslStream port maps cipher-suite names to ("Both field
// names and values are based on the TLS Cipher Suites list ... from the Internet Assigned Numbers Authority (IANA)",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.security.tlsciphersuite?view=net-10.0; all nine names below
// are members of that enum), so the list can be passed to SslContextBuilder.Ciphers unchanged
// (docs/handler-porting.md, setEnabledCipherSuites). Restricting SslStream to these suites needs a CipherSuitesPolicy,
// which is marked UnsupportedOSPlatform("windows") and UnsupportedOSPlatform("android")
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.ciphersuitespolicy?view=net-10.0): on those platforms
// a context built with these ciphers fails when it creates a handler (docs/handler-porting.md), while the filtered list
// itself (SslContext.CipherSuites()) is available everywhere. The static block is the static constructor, which runs
// after the field initializers ("the initializers run immediately before the static constructor",
// https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors).
public static class Http2SecurityUtil
{
    /**
     * The following list is derived from <a
     * href="https://docs.oracle.com/javase/8/docs/technotes/guides/security/SunProviders.html">SunJSSE Supported
     * Ciphers</a> and <a
     * href="https://wiki.mozilla.org/Security/Server_Side_TLS#Modern_compatibility">Mozilla Modern Cipher
     * Suites</a> in accordance with the <a
     * href="https://tools.ietf.org/html/rfc7540#section-9.2.2">HTTP/2 Specification</a>.
     *
     * According to the <a href="https://docs.oracle.com/javase/8/docs/technotes/guides/security/StandardNames.html">
     * JSSE documentation</a> "the names mentioned in the TLS RFCs prefixed with TLS_ are functionally equivalent
     * to the JSSE cipher suites prefixed with SSL_".
     * Both variants are used to support JVMs supporting the one or the other.
     */
    public static readonly IList<string> CIPHERS;

    /**
     * <a href="https://wiki.mozilla.org/Security/Server_Side_TLS#Intermediate_compatibility_.28recommended.29"
     * >Mozilla Modern Cipher Suites Intermediate compatibility</a> minus the following cipher suites that are black
     * listed by the <a href="https://tools.ietf.org/html/rfc7540#appendix-A">HTTP/2 RFC</a>.
     */
    private static readonly IList<string> CIPHERS_JAVA_MOZILLA_MODERN_SECURITY = new ReadOnlyCollection<string>(
            new[]
            {
            /* openssl = ECDHE-ECDSA-AES128-GCM-SHA256 */
            "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256",

            /* REQUIRED BY HTTP/2 SPEC */
            /* openssl = ECDHE-RSA-AES128-GCM-SHA256 */
            "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256",
            /* REQUIRED BY HTTP/2 SPEC */

            /* openssl = ECDHE-ECDSA-AES256-GCM-SHA384 */
            "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384",
            /* openssl = ECDHE-RSA-AES256-GCM-SHA384 */
            "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
            /* openssl = ECDHE-ECDSA-CHACHA20-POLY1305 */
            "TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256",
            /* openssl = ECDHE-RSA-CHACHA20-POLY1305 */
            "TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256",

            /* TLS 1.3 ciphers */
            "TLS_AES_128_GCM_SHA256",
            "TLS_AES_256_GCM_SHA384",
            "TLS_CHACHA20_POLY1305_SHA256"
            });

    static Http2SecurityUtil()
    {
        CIPHERS = new ReadOnlyCollection<string>(new List<string>(CIPHERS_JAVA_MOZILLA_MODERN_SECURITY));
    }
}
