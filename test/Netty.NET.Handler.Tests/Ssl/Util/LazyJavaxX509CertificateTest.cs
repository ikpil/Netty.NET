/*
 * Copyright 2024 The Netty Project
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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Netty.NET.Handler.Ssl.Util;

namespace Netty.NET.Handler.Tests.Ssl.Util;

// CLR: The source aborts the test (Assumptions.abort) when the JDK no longer creates javax certificates;
// Assert.Skip does the same when the loader fails with a CryptographicException, the javax CertificateException.
// LazyJavaxX509Certificate is a BCL X509Certificate (see its class comment), and the getters are compared through
// the BCL X509Certificate members: getSerialNumber() GetSerialNumber(), getIssuerDN() Issuer, getSubjectDN()
// Subject, getNotBefore()/getNotAfter() GetEffectiveDateString()/GetExpirationDateString(); getVersion(),
// getSigAlgName(), getSigAlgOID() and getSigAlgParams() have no BCL X509Certificate member, so the whole DER
// certificate that contains them (GetRawCertData()) and the key algorithm (GetKeyAlgorithm(),
// GetKeyAlgorithmParameters()) are compared instead.
public class LazyJavaxX509CertificateTest
{

    private const string CERTIFICATE =
            "-----BEGIN CERTIFICATE-----\n" +
                    "MIIEITCCAwmgAwIBAgIUaLL8vLOhWLCLXVHEJqXJhfmsTB8wDQYJKoZIhvcNAQEL\n" +
                    "BQAwgawxCzAJBgNVBAYTAlVTMRYwFAYDVQQIDA1NYXNzYWNodXNldHRzMRIwEAYD\n" +
                    "VQQHDAlDYW1icmlkZ2UxGDAWBgNVBAoMD25ldHR5IHRlc3QgY2FzZTEYMBYGA1UE\n" +
                    "CwwPbmV0dHkgdGVzdCBjYXNlMRgwFgYDVQQDDA9uZXR0eSB0ZXN0IGNhc2UxIzAh\n" +
                    "BgkqhkiG9w0BCQEWFGNjb25uZWxsQGh1YnNwb3QuY29tMB4XDTI0MDEyMTE5MzMy\n" +
                    "MFoXDTI1MDEyMDE5MzMyMFowgawxCzAJBgNVBAYTAlVTMRYwFAYDVQQIDA1NYXNz\n" +
                    "YWNodXNldHRzMRIwEAYDVQQHDAlDYW1icmlkZ2UxGDAWBgNVBAoMD25ldHR5IHRl\n" +
                    "c3QgY2FzZTEYMBYGA1UECwwPbmV0dHkgdGVzdCBjYXNlMRgwFgYDVQQDDA9uZXR0\n" +
                    "eSB0ZXN0IGNhc2UxIzAhBgkqhkiG9w0BCQEWFGNjb25uZWxsQGh1YnNwb3QuY29t\n" +
                    "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAy+qzEZpQMjVdLj0siUcG\n" +
                    "y8LIHOW4S+tgHIKFkF865qWq6FVGbROe2Z0f5W6yIamZkdxzptT0iv+8S5okNNeW\n" +
                    "2NbsN/HNJIRtWfxku1Jh1gBqSkAYIjXyq7+20hIaJTzzxqike9M/Lc14EGb33Ja/\n" +
                    "kDPRV3UtiM3Ntf3eALXKbrWptkbgQngCaTgtfg8IkMAEpP270wZ9fW0lDHv3NPPt\n" +
                    "Zt0QSJzWSqWfu+l4ayvcUQYyNJesx9YmTHSJu69lvT4QApoX8FEiHfNCJ28R50CS\n" +
                    "aIgOpCWUvkH7rqx0p9q393uJRS/S6RlLbU30xUN1fNrVmP/XAapfy+R0PSgiUi8o\n" +
                    "EQIDAQABozkwNzAWBgNVHRIEDzANggt3d3cuZm9vLmNvbTAdBgNVHQ4EFgQUl4FD\n" +
                    "Y8jJ/JHJR68YqPsGUjUJuwgwDQYJKoZIhvcNAQELBQADggEBADVzivYz2M0qsWUc\n" +
                    "jXjCHymwTIr+7ud10um53FbYEAfKWsIY8Pp35fKpFzUwc5wVdCnLU86K/YMKRzNB\n" +
                    "zL2Auow3PJFRvXecOv7dWxNlNneLDcwbVrdNRu6nQXmZUgyz0oUKuJbF+JGtI+7W\n" +
                    "kRw7yhBfki+UCSQWeDqvaWzgmA4Us0N8NFq3euAs4xFbMMPMQWrT9Z7DGchCeRiB\n" +
                    "dkQBvh88vbR3v2Saq14W4Wt5rj2++vXWGQSeAQL6nGbOwc3ohW6isNNV0eGQQTmS\n" +
                    "khS2d/JDZq2XL5RGexf3CA6YYzWiTr9YZHNjuobvLH7mVnA2c8n6Zty/UhfnuK1x\n" +
                    "JbkleFk=\n" +
                    "-----END CERTIFICATE-----";

    [Fact]
    public void TestLazyX509Certificate()
    {
        X509Certificate x509Certificate;

        try
        {
            x509Certificate = X509CertificateLoader.LoadCertificate(Encoding.UTF8.GetBytes(CERTIFICATE));
        }
        catch (CryptographicException)
        {
            Assert.Skip("JDK does not support creating " + typeof(X509Certificate));
            // JDK does not support this anymore
            return;
        }

        LazyJavaxX509Certificate lazyX509Certificate =
                new LazyJavaxX509Certificate(Encoding.UTF8.GetBytes(CERTIFICATE));
        Assert.Equal(x509Certificate.GetRawCertData(), lazyX509Certificate.GetRawCertData());
        Assert.Equal(x509Certificate.GetSerialNumber(), lazyX509Certificate.GetSerialNumber());
        Assert.Equal(x509Certificate.Issuer, lazyX509Certificate.Issuer);
        Assert.Equal(x509Certificate.Subject, lazyX509Certificate.Subject);
        Assert.Equal(x509Certificate.GetEffectiveDateString(), lazyX509Certificate.GetEffectiveDateString());
        Assert.Equal(x509Certificate.GetExpirationDateString(), lazyX509Certificate.GetExpirationDateString());
        Assert.Equal(x509Certificate.GetKeyAlgorithm(), lazyX509Certificate.GetKeyAlgorithm());
        Assert.Equal(x509Certificate.GetKeyAlgorithmParameters(), lazyX509Certificate.GetKeyAlgorithmParameters());
        Assert.Equal(Encoding.UTF8.GetBytes(CERTIFICATE), lazyX509Certificate.GetEncoded());
        Assert.Same(lazyX509Certificate.GetBytes(), lazyX509Certificate.GetBytes());
        lazyX509Certificate.Dispose();
        x509Certificate.Dispose();
    }
}
