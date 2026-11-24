/*
 * Copyright 2018 The Netty Project
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

using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Tests.Ssl;

public class SignatureAlgorithmConverterTest
{

    [Fact]
    public void TestWithEncryption()
    {
        Assert.Equal("SHA512withRSA", SignatureAlgorithmConverter.ToJavaName("sha512WithRSAEncryption"));
    }

    [Fact]
    public void TestWithDash()
    {
        Assert.Equal("SHA256withECDSA", SignatureAlgorithmConverter.ToJavaName("ecdsa-with-SHA256"));
    }

    [Fact]
    public void TestWithUnderscore()
    {
        Assert.Equal("SHA256withDSA", SignatureAlgorithmConverter.ToJavaName("dsa_with_SHA256"));
    }

    [Fact]
    public void TestBoringSSLOneUnderscore()
    {
        Assert.Equal("SHA256withECDSA", SignatureAlgorithmConverter.ToJavaName("ecdsa_sha256"));
    }

    [Fact]
    public void TestBoringSSLPkcs1()
    {
        Assert.Equal("SHA256withRSA", SignatureAlgorithmConverter.ToJavaName("rsa_pkcs1_sha256"));
    }

    [Fact]
    public void TestBoringSSLPSS()
    {
        Assert.Equal("SHA256withRSA", SignatureAlgorithmConverter.ToJavaName("rsa_pss_rsae_sha256"));
    }

    [Fact]
    public void TestEd25519()
    {
        Assert.Equal("Ed25519", SignatureAlgorithmConverter.ToJavaName("ed25519"));
    }

    [Fact]
    public void TestEd448()
    {
        Assert.Equal("Ed448", SignatureAlgorithmConverter.ToJavaName("ed448"));
    }

    [Fact]
    public void TestInvalid()
    {
        Assert.Null(SignatureAlgorithmConverter.ToJavaName("ThisIsSomethingInvalid"));
    }
}
