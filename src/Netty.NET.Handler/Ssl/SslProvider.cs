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

using System.Diagnostics;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * An enumeration of SSL/TLS protocol providers.
 */
// CLR: a Java enum with methods is a sealed class whose constants are its static readonly instances, as codec-base
// Base64Dialect ports one (src/Netty.NET.Handler.Codec/Base64Codec/Base64Dialect.cs): a C# enum cannot carry the static
// support probes that Java call sites reach as SslProvider.isAlpnSupported(...). ToString() returns the constant name
// (Enum.toString()); the switch statements compare the instances. SslStream is the only TLS engine (owner decision
// 2026-11-24, docs/handler-ssl-design.md): JDK stands for it, and OPENSSL/OPENSSL_REFCNT, whose OpenSsl (netty-tcnative)
// is library-not-ported, report as unavailable. The java.security.Provider parameters have no BCL counterpart and are
// dropped, as SslUtils.IsTLSv13SupportedByJDK()/IsTLSv13EnabledByJDK() dropped them (the provider == null branch is kept):
// isTlsv13Supported(SslProvider, Provider) is not ported, its Javadoc is the one of isTlsv13Supported(SslProvider).
public sealed class SslProvider
{
    /**
     * JDK's default implementation.
     */
    public static readonly SslProvider JDK = new SslProvider(nameof(JDK));
    /**
     * OpenSSL-based implementation.
     */
    public static readonly SslProvider OPENSSL = new SslProvider(nameof(OPENSSL));
    /**
     * OpenSSL-based implementation which does not have finalizers and instead implements {@link ReferenceCounted}.
     */
    [UnstableApi]
    public static readonly SslProvider OPENSSL_REFCNT = new SslProvider(nameof(OPENSSL_REFCNT));

    private readonly string name;

    private SslProvider(string name)
    {
        this.name = name;
    }

    public override string ToString()
    {
        return name;
    }

    /**
     * Returns {@code true} if the specified {@link SslProvider} supports
     * <a href="https://tools.ietf.org/html/rfc7301#section-6">TLS ALPN Extension</a>, {@code false} otherwise.
     */
    // CLR: JdkAlpnApplicationProtocolNegotiator.isAlpnSupported() (a @Deprecated type, not ported) asks the JDK; SslStream
    // takes the ALPN protocol list as SslClientAuthenticationOptions/SslServerAuthenticationOptions.ApplicationProtocols
    // ("Gets or sets a list of ALPN protocols",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions?view=net-10.0) and
    // negotiated h2 in the measurements of docs/handler-ssl-design.md, so JDK answers true. OpenSsl.isAlpnSupported() is
    // false: OpenSsl is library-not-ported.
    public static bool IsAlpnSupported(SslProvider provider)
    {
        if (provider == JDK)
        {
            return true;
        }
        if (provider == OPENSSL || provider == OPENSSL_REFCNT)
        {
            return false;
        }
        throw new UnreachableException("Unexpected SslProvider: " + provider);
    }

    /**
     * Returns {@code true} if the specified {@link SslProvider} supports
     * <a href="https://tools.ietf.org/html/rfc8446">TLS 1.3</a>, {@code false} otherwise.
     */
    // CLR: the JDK answer is SslUtils.IsTLSv13SupportedByJDK(), the in-memory SslStream probe of wave 1; OpenSsl is
    // library-not-ported, so OPENSSL/OPENSSL_REFCNT answer false. java.lang.Error for an unexpected constant is
    // UnreachableException, as codec-base LengthFieldPrepender maps it.
    public static bool IsTlsv13Supported(SslProvider sslProvider)
    {
        if (sslProvider == JDK)
        {
            return SslUtils.IsTLSv13SupportedByJDK();
        }
        if (sslProvider == OPENSSL || sslProvider == OPENSSL_REFCNT)
        {
            return false;
        }
        throw new UnreachableException("Unexpected SslProvider: " + sslProvider);
    }

    /**
     * Returns {@code true} if the specified {@link SslProvider} supports the specified {@link SslContextOption},
     * {@code false} otherwise.
     */
    // CLR: SslContextOption<?> is the non-generic SslContextOption base; OpenSsl.isOptionSupported is library-not-ported,
    // so OPENSSL/OPENSSL_REFCNT answer false.
    public static bool IsOptionSupported(SslProvider sslProvider, SslContextOption option)
    {
        if (sslProvider == JDK)
        {
            // We currently don't support any SslContextOptions when using the JDK implementation
            return false;
        }
        if (sslProvider == OPENSSL || sslProvider == OPENSSL_REFCNT)
        {
            return false;
        }
        throw new UnreachableException("Unexpected SslProvider: " + sslProvider);
    }

    /**
     * Returns {@code true} if the specified {@link SslProvider} enables
     * <a href="https://tools.ietf.org/html/rfc8446">TLS 1.3</a> by default, {@code false} otherwise.
     */
    // CLR: without the Provider parameter (see the class comment); the JDK answer is SslUtils.IsTLSv13EnabledByJDK().
    internal static bool IsTlsv13EnabledByDefault(SslProvider sslProvider)
    {
        if (sslProvider == JDK)
        {
            return SslUtils.IsTLSv13EnabledByJDK();
        }
        if (sslProvider == OPENSSL || sslProvider == OPENSSL_REFCNT)
        {
            return false;
        }
        throw new UnreachableException("Unexpected SslProvider: " + sslProvider);
    }
}
