/*
 * Copyright 2020 The Netty Project
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
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the handler port runs SslContext on SslStream (owner decision), and SslContext.newEngine(alloc) with its
// SSLEngine is not ported (src/Netty.NET.Handler/Ssl/SslContext.cs: the abstract engine factory is replaced by
// NewHandler). engine.getEnabledCipherSuites() is the list the context enables on every engine it creates, which
// SslContext.CipherSuites() returns ("Returns the list of enabled cipher suites, in the order of preference"): the
// Http2SecurityUtil.CIPHERS that SupportedCipherSuiteFilter keeps among the names SslStream supports (the
// TlsCipherSuite names, docs/handler-porting.md, setEnabledCipherSuites). Creating a handler is not needed for that list
// and would fail on Windows, which has no CipherSuitesPolicy (docs/handler-porting.md). Assumptions.assumeTrue is
// Assert.SkipUnless (xunit v3 dynamic skip).
public class Http2SecurityUtilTest
{

    [Fact]
    public void TestTLSv13CiphersIncluded()
    {
        Xunit.Assert.SkipUnless(SslProvider.IsTlsv13Supported(SslProvider.JDK), "TLSv1.3 is not supported");
        TestCiphersIncluded("TLSv1.3");
    }

    [Fact]
    public void TestTLSv12CiphersIncluded()
    {
        TestCiphersIncluded("TLSv1.2");
    }

    private static void TestCiphersIncluded(string protocol)
    {
        SslContext context = SslContextBuilder.ForClient().SslProvider(SslProvider.JDK).Protocols(protocol)
                .Ciphers(Http2SecurityUtil.CIPHERS, SupportedCipherSuiteFilter.INSTANCE).Build();
        Assert.True(context.CipherSuites().Count > 0, "No " + protocol + " ciphers found");
    }
}
