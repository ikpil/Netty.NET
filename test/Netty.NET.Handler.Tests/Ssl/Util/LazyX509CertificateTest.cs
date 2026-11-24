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

using System.Security.Cryptography.X509Certificates;
using System.Text;
using Netty.NET.Handler.Ssl.Util;

namespace Netty.NET.Handler.Tests.Ssl.Util;

// CLR: CertificateFactory.getInstance("X.509").generateCertificate(stream) is X509CertificateLoader.LoadCertificate, which
// "Loads a single X.509 certificate from data, in either the PEM or DER encoding"
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificateloader.loadcertificate?view=net-10.0),
// and LazyX509Certificate is an X509Certificate2 (see its class comment). Each getter of the source is compared through
// its X509Certificate2 counterpart: getVersion() Version, getSerialNumber() SerialNumberBytes, getIssuerDN() Issuer,
// getIssuerX500Principal() IssuerName.RawData, getSubjectDN() Subject, getNotBefore()/getNotAfter() NotBefore/NotAfter,
// getSigAlgName()/getSigAlgOID() SignatureAlgorithm.FriendlyName/Value, and getKeyUsage(), getExtendedKeyUsage(),
// getBasicConstraints(), getSubjectAlternativeNames() and getIssuerAlternativeNames() the raw data of the key usage
// (2.5.29.15), extended key usage (2.5.29.37), basic constraints (2.5.29.19), subject alternative name (2.5.29.17) and
// issuer alternative name (2.5.29.18) extensions, absent ones compared as null. getTBSCertificate(), getSignature(),
// getSigAlgParams() and the unique IDs have no X509Certificate2 member; RawData, the whole DER certificate that contains
// them, is compared instead. assertEqualSans compares the raw data of an alternative name extension. CharsetUtil.UTF_8
// is Encoding.UTF8.
public class LazyX509CertificateTest
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
        X509Certificate2 x509Certificate = X509CertificateLoader.LoadCertificate(Encoding.UTF8.GetBytes(CERTIFICATE));
        LazyX509Certificate lazyX509Certificate = new LazyX509Certificate(Encoding.UTF8.GetBytes(CERTIFICATE));
        Assert.Equal(x509Certificate.Version, lazyX509Certificate.Version);
        Assert.Equal(x509Certificate.SerialNumberBytes.ToArray(), lazyX509Certificate.SerialNumberBytes.ToArray());
        Assert.Equal(x509Certificate.Issuer, lazyX509Certificate.Issuer);
        Assert.Equal(x509Certificate.IssuerName.RawData, lazyX509Certificate.IssuerName.RawData);
        Assert.Equal(x509Certificate.Subject, lazyX509Certificate.Subject);
        Assert.Equal(x509Certificate.NotBefore, lazyX509Certificate.NotBefore);
        Assert.Equal(x509Certificate.NotAfter, lazyX509Certificate.NotAfter);
        Assert.Equal(x509Certificate.RawData, lazyX509Certificate.RawData);
        Assert.Equal(x509Certificate.SignatureAlgorithm.FriendlyName, lazyX509Certificate.SignatureAlgorithm.FriendlyName);
        Assert.Equal(x509Certificate.SignatureAlgorithm.Value, lazyX509Certificate.SignatureAlgorithm.Value);
        Assert.Equal(ExtensionData(x509Certificate, "2.5.29.15"), ExtensionData(lazyX509Certificate, "2.5.29.15"));
        Assert.Equal(ExtensionData(x509Certificate, "2.5.29.37"), ExtensionData(lazyX509Certificate, "2.5.29.37"));
        Assert.Equal(ExtensionData(x509Certificate, "2.5.29.19"), ExtensionData(lazyX509Certificate, "2.5.29.19"));
        AssertEqualSans(ExtensionData(x509Certificate, "2.5.29.17"), ExtensionData(lazyX509Certificate, "2.5.29.17"));
        AssertEqualSans(ExtensionData(x509Certificate, "2.5.29.18"), ExtensionData(lazyX509Certificate, "2.5.29.18"));
        Assert.Equal(Encoding.UTF8.GetBytes(CERTIFICATE), lazyX509Certificate.GetEncoded());
        lazyX509Certificate.Dispose();
        x509Certificate.Dispose();
    }

    // CLR: the alternative names are compared as the raw data of their extension (see the class comment); a missing
    // extension is null on both sides, as Java's null collection.
    private static void AssertEqualSans(byte[] expectedSans, byte[] actualSans)
    {
        if (expectedSans == null)
        {
            Assert.Null(actualSans);
            return;
        }
        Assert.Equal(expectedSans, actualSans);
    }

    // CLR: the raw data of the extension with the given OID, or null when the certificate has none.
    private static byte[] ExtensionData(X509Certificate2 certificate, string oid)
    {
        X509Extension extension = certificate.Extensions[oid];
        return extension?.RawData;
    }
}
