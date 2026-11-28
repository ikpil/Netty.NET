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

using System;
using System.IO;
using System.Linq;
using System.Net;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: @DisabledOnOs(WINDOWS) is Assert.SkipWhen(OperatingSystem.IsWindows()), the dynamic skip that the other
// test projects use for assumptions (test/Netty.NET.Handler.Codec.Compression.Tests/JdkZlibDecompressorTest.cs);
// AssertJ isGreaterThan(0) is Assert.True(count > 0). The source method scenarios() is Scenarios_(), because xUnit
// rejects a test method that shares its name with an overload (xUnit1024), the trailing-underscore rule of
// docs/codec-http-porting.md (Name clashes). new BufferedReader(new StringReader(..)) is a StringReader; new
// InetSocketAddress(n, 53) of an address literal is new IPEndPoint(IPAddress.Parse(n), 53) (the literal mapping of
// the resolver-dns brief), built with LINQ Select in place of the anonymous Function; assertIterableEquals is
// Assert.Equal over the sequences.
public class ResolvConfTest
{
    [Fact]
    public void ReadSystem()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "@DisabledOnOs(OS.WINDOWS)");
        Assert.True(ResolvConf.System().GetNameservers().Count > 0);
    }

    [Theory]
    [MemberData(nameof(Scenarios_))]
    public void Scenarios(string resolvConf, string[] nameservers)
    {
        Assert.Equal(
                nameservers.Select(n => (EndPoint) new IPEndPoint(IPAddress.Parse(n), 53)).ToList(),
                ResolvConf.FromReader(new StringReader(resolvConf)).GetNameservers());
    }

    public static TheoryData<string, string[]> Scenarios_()
    {
        return new TheoryData<string, string[]>
        {
                { "", Array.Empty<string>() },
                {
                        "# some comment\n"
                                + "# nameserver hello\n"
                                + "\n"
                                + "nameserver 1.2.3.4\n"
                                + "nameserver 127.1.2.3",
                        new[] { "1.2.3.4", "127.1.2.3" } },
                {
                        "# some comment\n"
                                + "# nameserver hello\n"
                                + "\n"
                                + "nameserver 1.2.3.4\n"
                                + "nameserver 127.1.2.3",
                        new[] { "1.2.3.4", "127.1.2.3" } },
                {
                        "# some comment\n"
                                + "nameserver 1.2.3.4 # comment\n"
                                + "nameserver 127.1.2.3",
                        new[] { "1.2.3.4", "127.1.2.3" } },
                {
                        "# some comment\n" + "nameserver 0:0:0:0:0:0:0:1\n" + "nameserver 127.0.0.1",
                        new[] { "0:0:0:0:0:0:0:1", "127.0.0.1" } },
                {
                        "# options and search are ignored\n"
                                + "nameserver 127.0.0.53\n"
                                + "options edns0 trust-ad"
                                + "search netty.io projectnessie.org",
                        new[] { "127.0.0.53" } }
        };
    }
}
