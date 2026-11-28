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

using System.IO;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Common.Internal;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: a class with a private constructor and only static members is a static class. Of its members only
// loadCertCollection has a ported subject; the others are not ported:
// - setSNIMatcher(SSLParameters, byte[]) installs a javax.net.ssl.SNIMatcher in the SSLParameters of a server engine
//   ("This method is only useful to SSLSockets or SSLEngines operating in server mode",
//   https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLParameters.html); SslStream has no
//   SSLParameters and SslServerAuthenticationOptions has no SNI matcher list (its properties,
//   https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslserverauthenticationoptions?view=net-10.0), and
//   no ported test calls it.
// - conscryptProvider() returns org.conscrypt.OpenSSLProvider; Conscrypt is library-not-ported (owner decision
//   2026-11-24, docs/handler-ssl-design.md) together with the tests that use it.
// - wrapSSLEngineForTesting(SSLEngine) wraps a ReferenceCountedOpenSslEngine in OpenSslErrorStackAssertSSLEngine, both
//   library-not-ported (netty-tcnative), and returns any other SSLEngine as is; there is no SSLEngine to wrap (owner
//   decision 2: no SSLEngine facade). Its Javadoc:
/**
 * Wraps the given {@link SSLEngine} to add extra tests while executing methods if possible / needed.
 */
public static class Java8SslTestUtils
{

    // CLR: CertificateFactory.getInstance("X.509").generateCertificate(InputStream) is X509CertificateLoader.LoadCertificate
    // of the resource bytes, which reads a PEM or DER certificate (as src/Netty.NET.Handler/Ssl/SslContext.cs and
    // Ssl/Util/LazyX509CertificateTest.cs use it); X509Certificate is X509Certificate2 (ssl design decision 3). The class
    // path resource next to SslContextTest is the file deployed next to the test assembly
    // (ResourcesUtil.GetFile(typeof(SslContextTest), name), see Netty.NET.Handler.Tests.csproj); a missing file is the null
    // stream of getResourceAsStream.
    public static X509Certificate2[] LoadCertCollection(params string[] resourceNames)
    {

        X509Certificate2[] certCollection = new X509Certificate2[resourceNames.Length];

        for (int i = 0; i < resourceNames.Length; i++)
        {
            string resourceName = resourceNames[i];

            using (System.IO.Stream @is = GetResourceAsStream(resourceName))
            {
                Assert.NotNull(@is, "Cannot find " + resourceName);
                MemoryStream bytes = new MemoryStream();
                @is.CopyTo(bytes);
                certCollection[i] = X509CertificateLoader.LoadCertificate(bytes.ToArray());
            }
        }

        return certCollection;
    }

    private static System.IO.Stream GetResourceAsStream(string resourceName)
    {
        FileInfo file = ResourcesUtil.GetFile(typeof(SslContextTest), resourceName);
        return file.Exists ? file.OpenRead() : null;
    }
}
