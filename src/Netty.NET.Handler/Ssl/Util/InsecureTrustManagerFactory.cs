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

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Handler.Ssl.Util;

/**
 * An insecure {@link TrustManagerFactory} that trusts all X.509 certificates without any verification.
 * <p>
 * <strong>NOTE:</strong>
 * Never use this {@link TrustManagerFactory} in production.
 * It is purely for testing purposes, and thus it is very insecure.
 * </p>
 */
// CLR: a TrustManagerFactory that only hands out one TrustManager is the RemoteCertificateValidationCallback that
// decides whether "the specified certificate is accepted for authentication"
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.remotecertificatevalidationcallback?view=net-10.0;
// owner decision 2026-11-24, docs/handler-ssl-design.md: TrustManagerFactory/TrustManager map to X509ChainPolicy or a
// RemoteCertificateValidationCallback). So INSTANCE is that callback and stays usable at its Java call sites
// (SslContextBuilder.TrustManager(InsecureTrustManagerFactory.INSTANCE)). The class only holds statics (the source's
// private constructor only prevents instantiation), so it is static. The JCA SPI members engineInit(KeyStore),
// engineInit(ManagerFactoryParameters) and engineGetTrustManagers() of SimpleTrustManagerFactory have no counterpart.
public static class InsecureTrustManagerFactory
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(InsecureTrustManagerFactory));

    public static readonly RemoteCertificateValidationCallback INSTANCE = Verify;

    // This needs to be X509ExtendedTrustManager so hostname verification is skipped as well.
    // Otherwise the JDK will internally wrap it with AbstractTrustManagerWrapper and add hostname verification
    // by itself.
    // CLR: the anonymous X509ExtendedTrustManager tm is the static Verify method with checkClientTrusted/
    // checkServerTrusted: the callback receives the hostname verification result as
    // SslPolicyErrors.RemoteCertificateNameMismatch and accepts it like every other error, so hostname verification is
    // skipped as well. The sender is the authenticating SslStream (measured), whose IsServer tells whether the peer is a
    // client (checkClientTrusted) or a server (checkServerTrusted); chain[0] is the remote certificate, and the
    // Socket/SSLEngine overloads, which only forward, and getAcceptedIssuers() have no callback counterpart. A missing
    // remote certificate (null), for which JSSE does not call the trust manager, is accepted too.
    private static bool Verify(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        if (certificate != null)
        {
            X509Certificate[] peerChain = { certificate };

            if (sender is SslStream stream && stream.IsServer)
            {
                CheckClientTrusted(peerChain, null);
            }
            else
            {
                CheckServerTrusted(peerChain, null);
            }
        }

        return true;
    }

    // CLR: getSubjectDN() is X509Certificate.Subject (the distinguished name of the subject).
    private static void CheckClientTrusted(X509Certificate[] chain, string s)
    {
        if (logger.IsDebugEnabled())
        {
            logger.Debug("Accepting a client certificate: " + chain[0].Subject);
        }
    }

    private static void CheckServerTrusted(X509Certificate[] chain, string s)
    {
        if (logger.IsDebugEnabled())
        {
            logger.Debug("Accepting a server certificate: " + chain[0].Subject);
        }
    }
}
