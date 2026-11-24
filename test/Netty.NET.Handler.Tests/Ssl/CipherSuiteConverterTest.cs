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

using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: @Isolated runs the class alone because it clears the process-wide converter cache; the xUnit counterpart is a
// collection with DisableParallelization (https://xunit.net/docs/running-tests-in-parallel), as the Buffer tests port
// @Isolated (test/Netty.NET.Buffer.Tests/AdaptiveByteBufAllocatorGrowthTest.cs).
[CollectionDefinition(nameof(CipherSuiteConverterCollection), DisableParallelization = true)]
public sealed class CipherSuiteConverterCollection;

[Collection(nameof(CipherSuiteConverterCollection))]
public class CipherSuiteConverterTest
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(CipherSuiteConverterTest));

    [Fact]
    public void TestJ2OMappings()
    {
        TestJ2OMapping("TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256", "ECDHE-ECDSA-AES128-SHA256");
        TestJ2OMapping("TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256", "ECDHE-RSA-AES128-SHA256");
        TestJ2OMapping("TLS_RSA_WITH_AES_128_CBC_SHA256", "AES128-SHA256");
        TestJ2OMapping("TLS_ECDH_ECDSA_WITH_AES_128_CBC_SHA256", "ECDH-ECDSA-AES128-SHA256");
        TestJ2OMapping("TLS_ECDH_RSA_WITH_AES_128_CBC_SHA256", "ECDH-RSA-AES128-SHA256");
        TestJ2OMapping("TLS_DHE_RSA_WITH_AES_128_CBC_SHA256", "DHE-RSA-AES128-SHA256");
        TestJ2OMapping("TLS_DHE_DSS_WITH_AES_128_CBC_SHA256", "DHE-DSS-AES128-SHA256");
        TestJ2OMapping("TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA", "ECDHE-ECDSA-AES128-SHA");
        TestJ2OMapping("TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA", "ECDHE-RSA-AES128-SHA");
        TestJ2OMapping("TLS_RSA_WITH_AES_128_CBC_SHA", "AES128-SHA");
        TestJ2OMapping("TLS_ECDH_ECDSA_WITH_AES_128_CBC_SHA", "ECDH-ECDSA-AES128-SHA");
        TestJ2OMapping("TLS_ECDH_RSA_WITH_AES_128_CBC_SHA", "ECDH-RSA-AES128-SHA");
        TestJ2OMapping("TLS_DHE_RSA_WITH_AES_128_CBC_SHA", "DHE-RSA-AES128-SHA");
        TestJ2OMapping("TLS_DHE_DSS_WITH_AES_128_CBC_SHA", "DHE-DSS-AES128-SHA");
        TestJ2OMapping("TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256", "ECDHE-ECDSA-AES128-GCM-SHA256");
        TestJ2OMapping("TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256", "ECDHE-RSA-AES128-GCM-SHA256");
        TestJ2OMapping("TLS_RSA_WITH_AES_128_GCM_SHA256", "AES128-GCM-SHA256");
        TestJ2OMapping("TLS_ECDH_ECDSA_WITH_AES_128_GCM_SHA256", "ECDH-ECDSA-AES128-GCM-SHA256");
        TestJ2OMapping("TLS_ECDH_RSA_WITH_AES_128_GCM_SHA256", "ECDH-RSA-AES128-GCM-SHA256");
        TestJ2OMapping("TLS_DHE_RSA_WITH_AES_128_GCM_SHA256", "DHE-RSA-AES128-GCM-SHA256");
        TestJ2OMapping("TLS_DHE_DSS_WITH_AES_128_GCM_SHA256", "DHE-DSS-AES128-GCM-SHA256");
        TestJ2OMapping("TLS_ECDHE_ECDSA_WITH_3DES_EDE_CBC_SHA", "ECDHE-ECDSA-DES-CBC3-SHA");
        TestJ2OMapping("TLS_ECDHE_RSA_WITH_3DES_EDE_CBC_SHA", "ECDHE-RSA-DES-CBC3-SHA");
        TestJ2OMapping("SSL_RSA_WITH_3DES_EDE_CBC_SHA", "DES-CBC3-SHA");
        TestJ2OMapping("TLS_ECDH_ECDSA_WITH_3DES_EDE_CBC_SHA", "ECDH-ECDSA-DES-CBC3-SHA");
        TestJ2OMapping("TLS_ECDH_RSA_WITH_3DES_EDE_CBC_SHA", "ECDH-RSA-DES-CBC3-SHA");
        TestJ2OMapping("SSL_DHE_RSA_WITH_3DES_EDE_CBC_SHA", "DHE-RSA-DES-CBC3-SHA");
        TestJ2OMapping("SSL_DHE_DSS_WITH_3DES_EDE_CBC_SHA", "DHE-DSS-DES-CBC3-SHA");
        TestJ2OMapping("TLS_ECDHE_ECDSA_WITH_RC4_128_SHA", "ECDHE-ECDSA-RC4-SHA");
        TestJ2OMapping("TLS_ECDHE_RSA_WITH_RC4_128_SHA", "ECDHE-RSA-RC4-SHA");
        TestJ2OMapping("SSL_RSA_WITH_RC4_128_SHA", "RC4-SHA");
        TestJ2OMapping("TLS_ECDH_ECDSA_WITH_RC4_128_SHA", "ECDH-ECDSA-RC4-SHA");
        TestJ2OMapping("TLS_ECDH_RSA_WITH_RC4_128_SHA", "ECDH-RSA-RC4-SHA");
        TestJ2OMapping("SSL_RSA_WITH_RC4_128_MD5", "RC4-MD5");
        TestJ2OMapping("TLS_DH_anon_WITH_AES_128_GCM_SHA256", "ADH-AES128-GCM-SHA256");
        TestJ2OMapping("TLS_DH_anon_WITH_AES_128_CBC_SHA256", "ADH-AES128-SHA256");
        TestJ2OMapping("TLS_ECDH_anon_WITH_AES_128_CBC_SHA", "AECDH-AES128-SHA");
        TestJ2OMapping("TLS_DH_anon_WITH_AES_128_CBC_SHA", "ADH-AES128-SHA");
        TestJ2OMapping("TLS_ECDH_anon_WITH_3DES_EDE_CBC_SHA", "AECDH-DES-CBC3-SHA");
        TestJ2OMapping("SSL_DH_anon_WITH_3DES_EDE_CBC_SHA", "ADH-DES-CBC3-SHA");
        TestJ2OMapping("TLS_ECDH_anon_WITH_RC4_128_SHA", "AECDH-RC4-SHA");
        TestJ2OMapping("SSL_DH_anon_WITH_RC4_128_MD5", "ADH-RC4-MD5");
        TestJ2OMapping("SSL_RSA_WITH_DES_CBC_SHA", "DES-CBC-SHA");
        TestJ2OMapping("SSL_DHE_RSA_WITH_DES_CBC_SHA", "DHE-RSA-DES-CBC-SHA");
        TestJ2OMapping("SSL_DHE_DSS_WITH_DES_CBC_SHA", "DHE-DSS-DES-CBC-SHA");
        TestJ2OMapping("SSL_DH_anon_WITH_DES_CBC_SHA", "ADH-DES-CBC-SHA");
        TestJ2OMapping("SSL_RSA_EXPORT_WITH_DES40_CBC_SHA", "EXP-DES-CBC-SHA");
        TestJ2OMapping("SSL_DHE_RSA_EXPORT_WITH_DES40_CBC_SHA", "EXP-DHE-RSA-DES-CBC-SHA");
        TestJ2OMapping("SSL_DHE_DSS_EXPORT_WITH_DES40_CBC_SHA", "EXP-DHE-DSS-DES-CBC-SHA");
        TestJ2OMapping("SSL_DH_anon_EXPORT_WITH_DES40_CBC_SHA", "EXP-ADH-DES-CBC-SHA");
        TestJ2OMapping("SSL_RSA_EXPORT_WITH_RC4_40_MD5", "EXP-RC4-MD5");
        TestJ2OMapping("SSL_DH_anon_EXPORT_WITH_RC4_40_MD5", "EXP-ADH-RC4-MD5");
        TestJ2OMapping("TLS_RSA_WITH_NULL_SHA256", "NULL-SHA256");
        TestJ2OMapping("TLS_ECDHE_ECDSA_WITH_NULL_SHA", "ECDHE-ECDSA-NULL-SHA");
        TestJ2OMapping("TLS_ECDHE_RSA_WITH_NULL_SHA", "ECDHE-RSA-NULL-SHA");
        TestJ2OMapping("SSL_RSA_WITH_NULL_SHA", "NULL-SHA");
        TestJ2OMapping("TLS_ECDH_ECDSA_WITH_NULL_SHA", "ECDH-ECDSA-NULL-SHA");
        TestJ2OMapping("TLS_ECDH_RSA_WITH_NULL_SHA", "ECDH-RSA-NULL-SHA");
        TestJ2OMapping("TLS_ECDH_anon_WITH_NULL_SHA", "AECDH-NULL-SHA");
        TestJ2OMapping("SSL_RSA_WITH_NULL_MD5", "NULL-MD5");
        TestJ2OMapping("TLS_KRB5_WITH_3DES_EDE_CBC_SHA", "KRB5-DES-CBC3-SHA");
        TestJ2OMapping("TLS_KRB5_WITH_3DES_EDE_CBC_MD5", "KRB5-DES-CBC3-MD5");
        TestJ2OMapping("TLS_KRB5_WITH_RC4_128_SHA", "KRB5-RC4-SHA");
        TestJ2OMapping("TLS_KRB5_WITH_RC4_128_MD5", "KRB5-RC4-MD5");
        TestJ2OMapping("TLS_KRB5_WITH_DES_CBC_SHA", "KRB5-DES-CBC-SHA");
        TestJ2OMapping("TLS_KRB5_WITH_DES_CBC_MD5", "KRB5-DES-CBC-MD5");
        TestJ2OMapping("TLS_KRB5_EXPORT_WITH_DES_CBC_40_SHA", "EXP-KRB5-DES-CBC-SHA");
        TestJ2OMapping("TLS_KRB5_EXPORT_WITH_DES_CBC_40_MD5", "EXP-KRB5-DES-CBC-MD5");
        TestJ2OMapping("TLS_KRB5_EXPORT_WITH_RC4_40_SHA", "EXP-KRB5-RC4-SHA");
        TestJ2OMapping("TLS_KRB5_EXPORT_WITH_RC4_40_MD5", "EXP-KRB5-RC4-MD5");
        TestJ2OMapping("SSL_RSA_EXPORT_WITH_RC2_CBC_40_MD5", "EXP-RC2-CBC-MD5");
        TestJ2OMapping("TLS_DHE_DSS_WITH_AES_256_CBC_SHA", "DHE-DSS-AES256-SHA");
        TestJ2OMapping("TLS_DHE_RSA_WITH_AES_256_CBC_SHA", "DHE-RSA-AES256-SHA");
        TestJ2OMapping("TLS_DH_anon_WITH_AES_256_CBC_SHA", "ADH-AES256-SHA");
        TestJ2OMapping("TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA", "ECDHE-ECDSA-AES256-SHA");
        TestJ2OMapping("TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA", "ECDHE-RSA-AES256-SHA");
        TestJ2OMapping("TLS_ECDH_ECDSA_WITH_AES_256_CBC_SHA", "ECDH-ECDSA-AES256-SHA");
        TestJ2OMapping("TLS_ECDH_RSA_WITH_AES_256_CBC_SHA", "ECDH-RSA-AES256-SHA");
        TestJ2OMapping("TLS_ECDH_anon_WITH_AES_256_CBC_SHA", "AECDH-AES256-SHA");
        TestJ2OMapping("TLS_KRB5_EXPORT_WITH_RC2_CBC_40_MD5", "EXP-KRB5-RC2-CBC-MD5");
        TestJ2OMapping("TLS_KRB5_EXPORT_WITH_RC2_CBC_40_SHA", "EXP-KRB5-RC2-CBC-SHA");
        TestJ2OMapping("TLS_RSA_WITH_AES_256_CBC_SHA", "AES256-SHA");

        // For historical reasons the CHACHA20 ciphers do not follow OpenSSL's custom naming
        // convention and omits the HMAC algorithm portion of the name.
        TestJ2OMapping("TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256", "ECDHE-RSA-CHACHA20-POLY1305");
        TestJ2OMapping("TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256", "ECDHE-ECDSA-CHACHA20-POLY1305");
        TestJ2OMapping("TLS_DHE_RSA_WITH_CHACHA20_POLY1305_SHA256", "DHE-RSA-CHACHA20-POLY1305");
        TestJ2OMapping("TLS_PSK_WITH_CHACHA20_POLY1305_SHA256", "PSK-CHACHA20-POLY1305");
        TestJ2OMapping("TLS_ECDHE_PSK_WITH_CHACHA20_POLY1305_SHA256", "ECDHE-PSK-CHACHA20-POLY1305");
        TestJ2OMapping("TLS_DHE_PSK_WITH_CHACHA20_POLY1305_SHA256", "DHE-PSK-CHACHA20-POLY1305");
        TestJ2OMapping("TLS_RSA_PSK_WITH_CHACHA20_POLY1305_SHA256", "RSA-PSK-CHACHA20-POLY1305");

        TestJ2OMapping("TLS_AES_128_GCM_SHA256", "TLS_AES_128_GCM_SHA256");
        TestJ2OMapping("TLS_AES_256_GCM_SHA384", "TLS_AES_256_GCM_SHA384");
        TestJ2OMapping("TLS_CHACHA20_POLY1305_SHA256", "TLS_CHACHA20_POLY1305_SHA256");
    }

    private static void TestJ2OMapping(string javaCipherSuite, string openSslCipherSuite)
    {
        string actual = CipherSuiteConverter.ToOpenSslUncached(javaCipherSuite, false);
        logger.Info("{} => {}", javaCipherSuite, actual);
        Assert.Equal(openSslCipherSuite, actual);
    }

    [Fact]
    public void TestO2JMappings()
    {
        TestO2JMapping("ECDHE_ECDSA_WITH_AES_128_CBC_SHA256", "ECDHE-ECDSA-AES128-SHA256");
        TestO2JMapping("ECDHE_RSA_WITH_AES_128_CBC_SHA256", "ECDHE-RSA-AES128-SHA256");
        TestO2JMapping("RSA_WITH_AES_128_CBC_SHA256", "AES128-SHA256");
        TestO2JMapping("ECDH_ECDSA_WITH_AES_128_CBC_SHA256", "ECDH-ECDSA-AES128-SHA256");
        TestO2JMapping("ECDH_RSA_WITH_AES_128_CBC_SHA256", "ECDH-RSA-AES128-SHA256");
        TestO2JMapping("DHE_RSA_WITH_AES_128_CBC_SHA256", "DHE-RSA-AES128-SHA256");
        TestO2JMapping("DHE_DSS_WITH_AES_128_CBC_SHA256", "DHE-DSS-AES128-SHA256");
        TestO2JMapping("ECDHE_ECDSA_WITH_AES_128_CBC_SHA", "ECDHE-ECDSA-AES128-SHA");
        TestO2JMapping("ECDHE_RSA_WITH_AES_128_CBC_SHA", "ECDHE-RSA-AES128-SHA");
        TestO2JMapping("RSA_WITH_AES_128_CBC_SHA", "AES128-SHA");
        TestO2JMapping("ECDH_ECDSA_WITH_AES_128_CBC_SHA", "ECDH-ECDSA-AES128-SHA");
        TestO2JMapping("ECDH_RSA_WITH_AES_128_CBC_SHA", "ECDH-RSA-AES128-SHA");
        TestO2JMapping("DHE_RSA_WITH_AES_128_CBC_SHA", "DHE-RSA-AES128-SHA");
        TestO2JMapping("DHE_DSS_WITH_AES_128_CBC_SHA", "DHE-DSS-AES128-SHA");
        TestO2JMapping("ECDHE_ECDSA_WITH_AES_128_GCM_SHA256", "ECDHE-ECDSA-AES128-GCM-SHA256");
        TestO2JMapping("ECDHE_RSA_WITH_AES_128_GCM_SHA256", "ECDHE-RSA-AES128-GCM-SHA256");
        TestO2JMapping("RSA_WITH_AES_128_GCM_SHA256", "AES128-GCM-SHA256");
        TestO2JMapping("ECDH_ECDSA_WITH_AES_128_GCM_SHA256", "ECDH-ECDSA-AES128-GCM-SHA256");
        TestO2JMapping("ECDH_RSA_WITH_AES_128_GCM_SHA256", "ECDH-RSA-AES128-GCM-SHA256");
        TestO2JMapping("DHE_RSA_WITH_AES_128_GCM_SHA256", "DHE-RSA-AES128-GCM-SHA256");
        TestO2JMapping("DHE_DSS_WITH_AES_128_GCM_SHA256", "DHE-DSS-AES128-GCM-SHA256");
        TestO2JMapping("ECDHE_ECDSA_WITH_3DES_EDE_CBC_SHA", "ECDHE-ECDSA-DES-CBC3-SHA");
        TestO2JMapping("ECDHE_RSA_WITH_3DES_EDE_CBC_SHA", "ECDHE-RSA-DES-CBC3-SHA");
        TestO2JMapping("RSA_WITH_3DES_EDE_CBC_SHA", "DES-CBC3-SHA");
        TestO2JMapping("ECDH_ECDSA_WITH_3DES_EDE_CBC_SHA", "ECDH-ECDSA-DES-CBC3-SHA");
        TestO2JMapping("ECDH_RSA_WITH_3DES_EDE_CBC_SHA", "ECDH-RSA-DES-CBC3-SHA");
        TestO2JMapping("DHE_RSA_WITH_3DES_EDE_CBC_SHA", "DHE-RSA-DES-CBC3-SHA");
        TestO2JMapping("DHE_DSS_WITH_3DES_EDE_CBC_SHA", "DHE-DSS-DES-CBC3-SHA");
        TestO2JMapping("ECDHE_ECDSA_WITH_RC4_128_SHA", "ECDHE-ECDSA-RC4-SHA");
        TestO2JMapping("ECDHE_RSA_WITH_RC4_128_SHA", "ECDHE-RSA-RC4-SHA");
        TestO2JMapping("RSA_WITH_RC4_128_SHA", "RC4-SHA");
        TestO2JMapping("ECDH_ECDSA_WITH_RC4_128_SHA", "ECDH-ECDSA-RC4-SHA");
        TestO2JMapping("ECDH_RSA_WITH_RC4_128_SHA", "ECDH-RSA-RC4-SHA");
        TestO2JMapping("RSA_WITH_RC4_128_MD5", "RC4-MD5");
        TestO2JMapping("DH_anon_WITH_AES_128_GCM_SHA256", "ADH-AES128-GCM-SHA256");
        TestO2JMapping("DH_anon_WITH_AES_128_CBC_SHA256", "ADH-AES128-SHA256");
        TestO2JMapping("ECDH_anon_WITH_AES_128_CBC_SHA", "AECDH-AES128-SHA");
        TestO2JMapping("DH_anon_WITH_AES_128_CBC_SHA", "ADH-AES128-SHA");
        TestO2JMapping("ECDH_anon_WITH_3DES_EDE_CBC_SHA", "AECDH-DES-CBC3-SHA");
        TestO2JMapping("DH_anon_WITH_3DES_EDE_CBC_SHA", "ADH-DES-CBC3-SHA");
        TestO2JMapping("ECDH_anon_WITH_RC4_128_SHA", "AECDH-RC4-SHA");
        TestO2JMapping("DH_anon_WITH_RC4_128_MD5", "ADH-RC4-MD5");
        TestO2JMapping("RSA_WITH_DES_CBC_SHA", "DES-CBC-SHA");
        TestO2JMapping("DHE_RSA_WITH_DES_CBC_SHA", "DHE-RSA-DES-CBC-SHA");
        TestO2JMapping("DHE_DSS_WITH_DES_CBC_SHA", "DHE-DSS-DES-CBC-SHA");
        TestO2JMapping("DH_anon_WITH_DES_CBC_SHA", "ADH-DES-CBC-SHA");
        TestO2JMapping("RSA_EXPORT_WITH_DES_CBC_40_SHA", "EXP-DES-CBC-SHA");
        TestO2JMapping("DHE_RSA_EXPORT_WITH_DES_CBC_40_SHA", "EXP-DHE-RSA-DES-CBC-SHA");
        TestO2JMapping("DHE_DSS_EXPORT_WITH_DES_CBC_40_SHA", "EXP-DHE-DSS-DES-CBC-SHA");
        TestO2JMapping("DH_anon_EXPORT_WITH_DES_CBC_40_SHA", "EXP-ADH-DES-CBC-SHA");
        TestO2JMapping("RSA_EXPORT_WITH_RC4_40_MD5", "EXP-RC4-MD5");
        TestO2JMapping("DH_anon_EXPORT_WITH_RC4_40_MD5", "EXP-ADH-RC4-MD5");
        TestO2JMapping("RSA_WITH_NULL_SHA256", "NULL-SHA256");
        TestO2JMapping("ECDHE_ECDSA_WITH_NULL_SHA", "ECDHE-ECDSA-NULL-SHA");
        TestO2JMapping("ECDHE_RSA_WITH_NULL_SHA", "ECDHE-RSA-NULL-SHA");
        TestO2JMapping("RSA_WITH_NULL_SHA", "NULL-SHA");
        TestO2JMapping("ECDH_ECDSA_WITH_NULL_SHA", "ECDH-ECDSA-NULL-SHA");
        TestO2JMapping("ECDH_RSA_WITH_NULL_SHA", "ECDH-RSA-NULL-SHA");
        TestO2JMapping("ECDH_anon_WITH_NULL_SHA", "AECDH-NULL-SHA");
        TestO2JMapping("RSA_WITH_NULL_MD5", "NULL-MD5");
        TestO2JMapping("KRB5_WITH_3DES_EDE_CBC_SHA", "KRB5-DES-CBC3-SHA");
        TestO2JMapping("KRB5_WITH_3DES_EDE_CBC_MD5", "KRB5-DES-CBC3-MD5");
        TestO2JMapping("KRB5_WITH_RC4_128_SHA", "KRB5-RC4-SHA");
        TestO2JMapping("KRB5_WITH_RC4_128_MD5", "KRB5-RC4-MD5");
        TestO2JMapping("KRB5_WITH_DES_CBC_SHA", "KRB5-DES-CBC-SHA");
        TestO2JMapping("KRB5_WITH_DES_CBC_MD5", "KRB5-DES-CBC-MD5");
        TestO2JMapping("KRB5_EXPORT_WITH_DES_CBC_40_SHA", "EXP-KRB5-DES-CBC-SHA");
        TestO2JMapping("KRB5_EXPORT_WITH_DES_CBC_40_MD5", "EXP-KRB5-DES-CBC-MD5");
        TestO2JMapping("KRB5_EXPORT_WITH_RC4_40_SHA", "EXP-KRB5-RC4-SHA");
        TestO2JMapping("KRB5_EXPORT_WITH_RC4_40_MD5", "EXP-KRB5-RC4-MD5");
        TestO2JMapping("RSA_EXPORT_WITH_RC2_CBC_40_MD5", "EXP-RC2-CBC-MD5");
        TestO2JMapping("DHE_DSS_WITH_AES_256_CBC_SHA", "DHE-DSS-AES256-SHA");
        TestO2JMapping("DHE_RSA_WITH_AES_256_CBC_SHA", "DHE-RSA-AES256-SHA");
        TestO2JMapping("DH_anon_WITH_AES_256_CBC_SHA", "ADH-AES256-SHA");
        TestO2JMapping("ECDHE_ECDSA_WITH_AES_256_CBC_SHA", "ECDHE-ECDSA-AES256-SHA");
        TestO2JMapping("ECDHE_RSA_WITH_AES_256_CBC_SHA", "ECDHE-RSA-AES256-SHA");
        TestO2JMapping("ECDH_ECDSA_WITH_AES_256_CBC_SHA", "ECDH-ECDSA-AES256-SHA");
        TestO2JMapping("ECDH_RSA_WITH_AES_256_CBC_SHA", "ECDH-RSA-AES256-SHA");
        TestO2JMapping("ECDH_anon_WITH_AES_256_CBC_SHA", "AECDH-AES256-SHA");
        TestO2JMapping("KRB5_EXPORT_WITH_RC2_CBC_40_MD5", "EXP-KRB5-RC2-CBC-MD5");
        TestO2JMapping("KRB5_EXPORT_WITH_RC2_CBC_40_SHA", "EXP-KRB5-RC2-CBC-SHA");
        TestO2JMapping("RSA_WITH_AES_256_CBC_SHA", "AES256-SHA");

        // Test the known mappings that actually do not exist in Java
        TestO2JMapping("EDH_DSS_WITH_3DES_EDE_CBC_SHA", "EDH-DSS-DES-CBC3-SHA");
        TestO2JMapping("RSA_WITH_SEED_SHA", "SEED-SHA");
        TestO2JMapping("RSA_WITH_CAMELLIA128_SHA", "CAMELLIA128-SHA");
        TestO2JMapping("RSA_WITH_IDEA_CBC_SHA", "IDEA-CBC-SHA");
        TestO2JMapping("PSK_WITH_AES_128_CBC_SHA", "PSK-AES128-CBC-SHA");
        TestO2JMapping("PSK_WITH_3DES_EDE_CBC_SHA", "PSK-3DES-EDE-CBC-SHA");
        TestO2JMapping("KRB5_WITH_IDEA_CBC_SHA", "KRB5-IDEA-CBC-SHA");
        TestO2JMapping("KRB5_WITH_IDEA_CBC_MD5", "KRB5-IDEA-CBC-MD5");
        TestO2JMapping("PSK_WITH_RC4_128_SHA", "PSK-RC4-SHA");
        TestO2JMapping("ECDHE_RSA_WITH_AES_256_GCM_SHA384", "ECDHE-RSA-AES256-GCM-SHA384");
        TestO2JMapping("ECDHE_ECDSA_WITH_AES_256_GCM_SHA384", "ECDHE-ECDSA-AES256-GCM-SHA384");
        TestO2JMapping("ECDHE_RSA_WITH_AES_256_CBC_SHA384", "ECDHE-RSA-AES256-SHA384");
        TestO2JMapping("ECDHE_ECDSA_WITH_AES_256_CBC_SHA384", "ECDHE-ECDSA-AES256-SHA384");
        TestO2JMapping("DHE_DSS_WITH_AES_256_GCM_SHA384", "DHE-DSS-AES256-GCM-SHA384");
        TestO2JMapping("DHE_RSA_WITH_AES_256_GCM_SHA384", "DHE-RSA-AES256-GCM-SHA384");
        TestO2JMapping("DHE_RSA_WITH_AES_256_CBC_SHA256", "DHE-RSA-AES256-SHA256");
        TestO2JMapping("DHE_DSS_WITH_AES_256_CBC_SHA256", "DHE-DSS-AES256-SHA256");
        TestO2JMapping("DHE_RSA_WITH_CAMELLIA256_SHA", "DHE-RSA-CAMELLIA256-SHA");
        TestO2JMapping("DHE_DSS_WITH_CAMELLIA256_SHA", "DHE-DSS-CAMELLIA256-SHA");
        TestO2JMapping("ECDH_RSA_WITH_AES_256_GCM_SHA384", "ECDH-RSA-AES256-GCM-SHA384");
        TestO2JMapping("ECDH_ECDSA_WITH_AES_256_GCM_SHA384", "ECDH-ECDSA-AES256-GCM-SHA384");
        TestO2JMapping("ECDH_RSA_WITH_AES_256_CBC_SHA384", "ECDH-RSA-AES256-SHA384");
        TestO2JMapping("ECDH_ECDSA_WITH_AES_256_CBC_SHA384", "ECDH-ECDSA-AES256-SHA384");
        TestO2JMapping("RSA_WITH_AES_256_GCM_SHA384", "AES256-GCM-SHA384");
        TestO2JMapping("RSA_WITH_AES_256_CBC_SHA256", "AES256-SHA256");
        TestO2JMapping("RSA_WITH_CAMELLIA256_SHA", "CAMELLIA256-SHA");
        TestO2JMapping("PSK_WITH_AES_256_CBC_SHA", "PSK-AES256-CBC-SHA");
        TestO2JMapping("DHE_RSA_WITH_SEED_SHA", "DHE-RSA-SEED-SHA");
        TestO2JMapping("DHE_DSS_WITH_SEED_SHA", "DHE-DSS-SEED-SHA");
        TestO2JMapping("DHE_RSA_WITH_CAMELLIA128_SHA", "DHE-RSA-CAMELLIA128-SHA");
        TestO2JMapping("DHE_DSS_WITH_CAMELLIA128_SHA", "DHE-DSS-CAMELLIA128-SHA");
        TestO2JMapping("EDH_RSA_WITH_3DES_EDE_CBC_SHA", "EDH-RSA-DES-CBC3-SHA");
        TestO2JMapping("SRP_DSS_WITH_AES_256_CBC_SHA", "SRP-DSS-AES-256-CBC-SHA");
        TestO2JMapping("SRP_RSA_WITH_AES_256_CBC_SHA", "SRP-RSA-AES-256-CBC-SHA");
        TestO2JMapping("SRP_WITH_AES_256_CBC_SHA", "SRP-AES-256-CBC-SHA");
        TestO2JMapping("DH_anon_WITH_AES_256_GCM_SHA384", "ADH-AES256-GCM-SHA384");
        TestO2JMapping("DH_anon_WITH_AES_256_CBC_SHA256", "ADH-AES256-SHA256");
        TestO2JMapping("DH_anon_WITH_CAMELLIA256_SHA", "ADH-CAMELLIA256-SHA");
        TestO2JMapping("SRP_DSS_WITH_AES_128_CBC_SHA", "SRP-DSS-AES-128-CBC-SHA");
        TestO2JMapping("SRP_RSA_WITH_AES_128_CBC_SHA", "SRP-RSA-AES-128-CBC-SHA");
        TestO2JMapping("SRP_WITH_AES_128_CBC_SHA", "SRP-AES-128-CBC-SHA");
        TestO2JMapping("DH_anon_WITH_SEED_SHA", "ADH-SEED-SHA");
        TestO2JMapping("DH_anon_WITH_CAMELLIA128_SHA", "ADH-CAMELLIA128-SHA");
        TestO2JMapping("RSA_WITH_RC2_CBC_MD5", "RC2-CBC-MD5");
        TestO2JMapping("SRP_DSS_WITH_3DES_EDE_CBC_SHA", "SRP-DSS-3DES-EDE-CBC-SHA");
        TestO2JMapping("SRP_RSA_WITH_3DES_EDE_CBC_SHA", "SRP-RSA-3DES-EDE-CBC-SHA");
        TestO2JMapping("SRP_WITH_3DES_EDE_CBC_SHA", "SRP-3DES-EDE-CBC-SHA");
        TestO2JMapping("RSA_WITH_3DES_EDE_CBC_MD5", "DES-CBC3-MD5");
        TestO2JMapping("EDH_RSA_WITH_DES_CBC_SHA", "EDH-RSA-DES-CBC-SHA");
        TestO2JMapping("EDH_DSS_WITH_DES_CBC_SHA", "EDH-DSS-DES-CBC-SHA");
        TestO2JMapping("RSA_WITH_DES_CBC_MD5", "DES-CBC-MD5");
        TestO2JMapping("EDH_RSA_EXPORT_WITH_DES_CBC_40_SHA", "EXP-EDH-RSA-DES-CBC-SHA");
        TestO2JMapping("EDH_DSS_EXPORT_WITH_DES_CBC_40_SHA", "EXP-EDH-DSS-DES-CBC-SHA");

        // For historical reasons the CHACHA20 ciphers do not follow OpenSSL's custom naming
        // convention and omits the HMAC algorithm portion of the name.
        TestO2JMapping("ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256", "ECDHE-RSA-CHACHA20-POLY1305");
        TestO2JMapping("ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256", "ECDHE-ECDSA-CHACHA20-POLY1305");
        TestO2JMapping("DHE_RSA_WITH_CHACHA20_POLY1305_SHA256", "DHE-RSA-CHACHA20-POLY1305");
        TestO2JMapping("PSK_WITH_CHACHA20_POLY1305_SHA256", "PSK-CHACHA20-POLY1305");
        TestO2JMapping("ECDHE_PSK_WITH_CHACHA20_POLY1305_SHA256", "ECDHE-PSK-CHACHA20-POLY1305");
        TestO2JMapping("DHE_PSK_WITH_CHACHA20_POLY1305_SHA256", "DHE-PSK-CHACHA20-POLY1305");
        TestO2JMapping("RSA_PSK_WITH_CHACHA20_POLY1305_SHA256", "RSA-PSK-CHACHA20-POLY1305");
    }

    private static void TestO2JMapping(string javaCipherSuite, string openSslCipherSuite)
    {
        string actual = CipherSuiteConverter.ToJavaUncached(openSslCipherSuite);
        logger.Info("{} => {}", openSslCipherSuite, actual);
        Assert.Equal(javaCipherSuite, actual);
    }

    [Fact]
    public void TestCachedJ2OMappings()
    {
        TestCachedJ2OMapping("TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256", "ECDHE-ECDSA-AES128-SHA256");
    }

    [Fact]
    public void TestUnknownOpenSSLCiphersToJava()
    {
        TestUnknownOpenSSLCiphersToJava0("(NONE)");
        TestUnknownOpenSSLCiphersToJava0("unknown");
        TestUnknownOpenSSLCiphersToJava0("");
    }

    [Fact]
    public void TestUnknownJavaCiphersToOpenSSL()
    {
        TestUnknownJavaCiphersToOpenSSL0("(NONE)");
        TestUnknownJavaCiphersToOpenSSL0("unknown");
        TestUnknownJavaCiphersToOpenSSL0("");
    }

    // CLR: xUnit1024 forbids overloading the TestUnknownOpenSSLCiphersToJava test name.
    private static void TestUnknownOpenSSLCiphersToJava0(string openSslCipherSuite)
    {
        CipherSuiteConverter.ClearCache();

        Assert.Null(CipherSuiteConverter.ToJava(openSslCipherSuite, "TLS"));
        Assert.Null(CipherSuiteConverter.ToJava(openSslCipherSuite, "SSL"));
    }

    // CLR: xUnit1024 forbids overloading the TestUnknownJavaCiphersToOpenSSL test name.
    private static void TestUnknownJavaCiphersToOpenSSL0(string javaCipherSuite)
    {
        CipherSuiteConverter.ClearCache();

        Assert.Null(CipherSuiteConverter.ToOpenSsl(javaCipherSuite, false));
        Assert.Null(CipherSuiteConverter.ToOpenSsl(javaCipherSuite, true));
    }

    private static void TestCachedJ2OMapping(string javaCipherSuite, string openSslCipherSuite)
    {
        CipherSuiteConverter.ClearCache();

        // For TLSv1.3 this should make no diffierence if boringSSL is true or false
        string actual1 = CipherSuiteConverter.ToOpenSsl(javaCipherSuite, false);
        Assert.Equal(openSslCipherSuite, actual1);
        string actual2 = CipherSuiteConverter.ToOpenSsl(javaCipherSuite, true);
        Assert.Equal(actual1, actual2);

        // Ensure that the cache entries have been created.
        Assert.True(CipherSuiteConverter.IsJ2OCached(javaCipherSuite, actual1));
        Assert.True(CipherSuiteConverter.IsO2JCached(actual1, "", javaCipherSuite.Substring(4)));
        Assert.True(CipherSuiteConverter.IsO2JCached(actual1, "SSL", "SSL_" + javaCipherSuite.Substring(4)));
        Assert.True(CipherSuiteConverter.IsO2JCached(actual1, "TLS", "TLS_" + javaCipherSuite.Substring(4)));

        string actual3 = CipherSuiteConverter.ToOpenSsl(javaCipherSuite, false);
        Assert.Equal(openSslCipherSuite, actual3);

        // Test if the returned cipher strings are identical,
        // so that the TLS sessions with the same cipher suite do not create many strings.
        Assert.Same(actual1, actual3);
    }

    [Fact]
    public void TestCachedO2JMappings()
    {
        TestCachedO2JMapping("ECDHE_ECDSA_WITH_AES_128_CBC_SHA256", "ECDHE-ECDSA-AES128-SHA256");
    }

    private static void TestCachedO2JMapping(string javaCipherSuite, string openSslCipherSuite)
    {
        CipherSuiteConverter.ClearCache();

        string tlsExpected = "TLS_" + javaCipherSuite;
        string sslExpected = "SSL_" + javaCipherSuite;

        string tlsActual1 = CipherSuiteConverter.ToJava(openSslCipherSuite, "TLS");
        string sslActual1 = CipherSuiteConverter.ToJava(openSslCipherSuite, "SSL");
        Assert.Equal(tlsExpected, tlsActual1);
        Assert.Equal(sslExpected, sslActual1);

        // Ensure that the cache entries have been created.
        Assert.True(CipherSuiteConverter.IsO2JCached(openSslCipherSuite, "", javaCipherSuite));
        Assert.True(CipherSuiteConverter.IsO2JCached(openSslCipherSuite, "SSL", sslExpected));
        Assert.True(CipherSuiteConverter.IsO2JCached(openSslCipherSuite, "TLS", tlsExpected));
        Assert.True(CipherSuiteConverter.IsJ2OCached(tlsExpected, openSslCipherSuite));
        Assert.True(CipherSuiteConverter.IsJ2OCached(sslExpected, openSslCipherSuite));

        string tlsActual2 = CipherSuiteConverter.ToJava(openSslCipherSuite, "TLS");
        string sslActual2 = CipherSuiteConverter.ToJava(openSslCipherSuite, "SSL");
        Assert.Equal(tlsExpected, tlsActual2);
        Assert.Equal(sslExpected, sslActual2);

        // Test if the returned cipher strings are identical,
        // so that the TLS sessions with the same cipher suite do not create many strings.
        Assert.Same(tlsActual1, tlsActual2);
        Assert.Same(sslActual1, sslActual2);
    }

    [Fact]
    public void TestTlsv13Mappings()
    {
        CipherSuiteConverter.ClearCache();

        Assert.Equal("TLS_AES_128_GCM_SHA256",
                     CipherSuiteConverter.ToJava("TLS_AES_128_GCM_SHA256", "TLS"));
        Assert.Null(CipherSuiteConverter.ToJava("TLS_AES_128_GCM_SHA256", "SSL"));
        Assert.Equal("TLS_AES_256_GCM_SHA384",
                     CipherSuiteConverter.ToJava("TLS_AES_256_GCM_SHA384", "TLS"));
        Assert.Null(CipherSuiteConverter.ToJava("TLS_AES_256_GCM_SHA384", "SSL"));
        Assert.Equal("TLS_CHACHA20_POLY1305_SHA256",
                     CipherSuiteConverter.ToJava("TLS_CHACHA20_POLY1305_SHA256", "TLS"));
        Assert.Null(CipherSuiteConverter.ToJava("TLS_CHACHA20_POLY1305_SHA256", "SSL"));

        // BoringSSL use different cipher naming then OpenSSL so we need to test for both
        Assert.Equal("TLS_AES_128_GCM_SHA256",
                     CipherSuiteConverter.ToOpenSsl("TLS_AES_128_GCM_SHA256", false));
        Assert.Equal("TLS_AES_256_GCM_SHA384",
                     CipherSuiteConverter.ToOpenSsl("TLS_AES_256_GCM_SHA384", false));
        Assert.Equal("TLS_CHACHA20_POLY1305_SHA256",
                     CipherSuiteConverter.ToOpenSsl("TLS_CHACHA20_POLY1305_SHA256", false));

        Assert.Equal("AEAD-AES128-GCM-SHA256",
                     CipherSuiteConverter.ToOpenSsl("TLS_AES_128_GCM_SHA256", true));
        Assert.Equal("AEAD-AES256-GCM-SHA384",
                     CipherSuiteConverter.ToOpenSsl("TLS_AES_256_GCM_SHA384", true));
        Assert.Equal("AEAD-CHACHA20-POLY1305-SHA256",
                     CipherSuiteConverter.ToOpenSsl("TLS_CHACHA20_POLY1305_SHA256", true));
    }
}
