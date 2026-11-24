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

using System;
using System.Security.Cryptography;
using Netty.NET.Buffer;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: testPemEncodedOpenSsl and testPemEncodedOpenSslRef (and their helpers testPemEncoded and assertRelease) are not
// ported: they build a server context from a PemPrivateKey/PemX509Certificate with SslProvider.OPENSSL/OPENSSL_REFCNT
// after OpenSsl.ensureAvailability() and assert it is a ReferenceCountedOpenSslContext; OpenSSL (netty-tcnative) is
// library-not-ported (owner decision 2026-11-24, docs/handler-ssl-design.md) and SslProvider.OPENSSL/OPENSSL_REFCNT cannot
// be built (src/Netty.NET.Handler/Ssl/SslContext.cs). They are recorded as CLR-inapplicable in
// docs/handler-porting-manifest.json.
public class PemEncodedTest
{

    // CLR: java.security.PrivateKey is AsymmetricAlgorithm (ssl design decision 3) and getEncoded() is its PKCS #8 export,
    // public virtual byte[] ExportPkcs8PrivateKey(), which "Exports the current key in the PKCS#8 PrivateKeyInfo format"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm.exportpkcs8privatekey?view=net-10.0),
    // so the anonymous PrivateKey whose getEncoded() returns null is the nested NullEncodedPrivateKey, whose override
    // returns null (getAlgorithm()/getFormat(), which return null too, have no AsymmetricAlgorithm member that ToPEM reads).
    // PemPrivateKey.toPEM is internal (package-private in Java). IllegalArgumentException is ArgumentException.
    [Fact]
    public void TestEncodedReturnsNull()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            PemPrivateKey.ToPEM(UnpooledByteBufAllocator.Default, true, new NullEncodedPrivateKey());
        });
    }

    private sealed class NullEncodedPrivateKey : AsymmetricAlgorithm
    {
        public override byte[] ExportPkcs8PrivateKey()
        {
            return null;
        }
    }

}
