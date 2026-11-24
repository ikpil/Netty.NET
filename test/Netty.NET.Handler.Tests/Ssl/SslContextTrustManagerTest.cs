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

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Handler.Ssl;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the TrustManagerFactory is the X509ChainPolicy that SslContext.buildTrustManagerFactory builds (ssl design decision
// 3, see src/Netty.NET.Handler/Ssl/SslContext.cs) and its X509TrustManager is an X509Chain whose ChainPolicy is that
// policy, as pkitesting X509Bundle.ToTrustManager maps it (src/Netty.NET.PkiTesting/X509Bundle.cs). X509Chain.Build
// "Builds an X.509 chain using the policy specified in X509ChainPolicy" and returns "true if the X.509 certificate is
// valid; otherwise, false" instead of throwing
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chain.build?view=net-10.0),
// so checkServerTrusted is the private CheckServerTrusted helper, which throws the CryptographicException that stands for
// CertificateException (as src/Netty.NET.Handler/Ssl/Util/FingerprintTrustManagerFactory.cs maps it) when Build returns
// false. String.format is string.Format in the invariant culture and Arrays.asList(caResources) prints as
// AbstractCollection.toString() ("[a, b]").
public class SslContextTrustManagerTest
{
    [Fact]
    public void TestUsingAllCAs()
    {
        RunTests(new string[] { "tm_test_ca_1a.pem", "tm_test_ca_1b.pem",
                "tm_test_ca_2.pem" }, new string[] { "tm_test_eec_1.pem",
                "tm_test_eec_2.pem", "tm_test_eec_3.pem" }, new bool[] {
                true, true, true });
    }

    [Fact]
    public void TestUsingAllCAsWithDuplicates()
    {
        RunTests(new string[] { "tm_test_ca_1a.pem", "tm_test_ca_1b.pem",
                "tm_test_ca_2.pem", "tm_test_ca_2.pem" },
                new string[] { "tm_test_eec_1.pem", "tm_test_eec_2.pem",
                        "tm_test_eec_3.pem" },
                new bool[] { true, true, true });
    }

    [Fact]
    public void TestUsingCAsOneAandB()
    {
        RunTests(new string[] { "tm_test_ca_1a.pem", "tm_test_ca_1b.pem", },
                new string[] { "tm_test_eec_1.pem", "tm_test_eec_2.pem",
                        "tm_test_eec_3.pem" }, new bool[] { true, true,
                        false });
    }

    [Fact]
    public void TestUsingCAsOneAandTwo()
    {
        RunTests(new string[] { "tm_test_ca_1a.pem", "tm_test_ca_2.pem" },
                new string[] { "tm_test_eec_1.pem", "tm_test_eec_2.pem",
                        "tm_test_eec_3.pem" }, new bool[] { true, false,
                        true });
    }

    /**
     *
     * @param caResources
     *            an array of paths to CA Certificates in PEM format to load
     *            from the classpath (relative to this class).
     * @param eecResources
     *            an array of paths to Server Certificates in PEM format in to
     *            load from the classpath (relative to this class).
     * @param expectations
     *            an array of expecting results for each EEC Server Certificate
     *            (the array is expected to have the same length the previous
     *            argument, and be arrange in matching order: true means
     *            expected to be valid, false otherwise.
     */
    private static void RunTests(string[] caResources, string[] eecResources,
            bool[] expectations)
    {
        X509Chain tm = GetTrustManager(caResources);

        X509Certificate2[] eecCerts = Java8SslTestUtils.LoadCertCollection(eecResources);

        for (int i = 0; i < eecResources.Length; i++)
        {
            X509Certificate2 eecCert = eecCerts[i];
            Assert.NotNull(eecCert, "Cannot use cert " + eecResources[i]);
            try
            {
                CheckServerTrusted(tm, new X509Certificate2[] { eecCert }, "RSA");
                if (!expectations[i])
                {
                    Assert.Fail(string.Format(CultureInfo.InvariantCulture,
                            "Certificate {0} was expected not to be valid when using CAs {1}, but its "
                                    + "verification passed.", eecResources[i],
                            "[" + string.Join(", ", caResources) + "]"));
                }
            }
            catch (CryptographicException)
            {
                if (expectations[i])
                {
                    Assert.Fail(string.Format(CultureInfo.InvariantCulture,
                            "Certificate {0} was expected to be valid when using CAs {1}, but its "
                                    + "verification failed.", eecResources[i],
                            "[" + string.Join(", ", caResources) + "]"));
                }
            }
        }
    }

    // CLR: tmf.getTrustManagers() of an X509ChainPolicy is the one X509Chain built with it (see the class comment), so
    // the search for an X509TrustManager and its "Unable to find any X509TrustManager from this factory." failure have
    // no counterpart.
    private static X509Chain GetTrustManager(string[] resourceNames)
    {
        X509Certificate2[] certCollection = Java8SslTestUtils.LoadCertCollection(resourceNames);
        X509ChainPolicy tmf = SslContext.BuildTrustManagerFactory(
                certCollection, null, null);

        X509Chain tm = new X509Chain();
        tm.ChainPolicy = tmf;
        return tm;
    }

    // CLR: X509TrustManager.checkServerTrusted(chain, authType); see the class comment. The authentication type has no
    // X509Chain counterpart; the chain builds from its first (end entity) certificate.
    private static void CheckServerTrusted(X509Chain tm, X509Certificate2[] chain, string authType)
    {
        if (!tm.Build(chain[0]))
        {
            throw new CryptographicException("Certificate chain is not trusted: " + chain[0].Subject);
        }
    }
}
