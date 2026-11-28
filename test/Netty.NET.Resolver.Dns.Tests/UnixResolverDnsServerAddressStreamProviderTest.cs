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

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: the @TempDir Path parameter is the tempDir field: JUnit creates a temporary directory for the test and deletes it
// recursively afterwards (https://docs.junit.org/current/api/org.junit.jupiter.api/org/junit/jupiter/api/io/TempDir.html);
// xUnit creates a test class instance per test, so the constructor creates the directory with
// Directory.CreateTempSubdirectory, "a uniquely named, empty directory in the current user's temporary directory"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.createtempsubdirectory?view=net-10.0), and Dispose()
// deletes it. File is FileInfo and Path a string; Files.write(path, bytes) is File.WriteAllBytes;
// tempDir.resolve(name).toFile().listFiles() of a missing directory is null (File.listFiles); UUID.randomUUID() is
// Guid.NewGuid(). InetSocketAddress.getHostString() of the resolved name servers is the IPEndPoint's address text
// (DefaultAuthoritativeDnsServerCache.GetHostString), which equals the literal of the file for these IPv4 addresses.
// assertEquals with a message is XunitAssertMessages.AssertM.
public class UnixResolverDnsServerAddressStreamProviderTest : IDisposable
{
    private readonly string tempDir = Directory.CreateTempSubdirectory("netty-dns-test").FullName;

    public void Dispose()
    {
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void DefaultLookupShouldReturnResultsIfOnlySingleFileSpecified()
    {
        FileInfo f = BuildFile(tempDir, "domain linecorp.local\n" +
                           "nameserver 127.0.0.2\n" +
                           "nameserver 127.0.0.3\n");
        UnixResolverDnsServerAddressStreamProvider p =
                new UnixResolverDnsServerAddressStreamProvider(f, null);

        IDnsServerAddressStream stream = p.NameServerAddressStream("somehost");
        AssertHostNameEquals("127.0.0.2", stream.Next());
        AssertHostNameEquals("127.0.0.3", stream.Next());
    }

    [Fact]
    public void NameServerAddressStreamShouldBeRotationalWhenRotationOptionsIsPresent()
    {
        FileInfo f = BuildFile(tempDir, "options rotate\n" +
            "domain linecorp.local\n" +
            "nameserver 127.0.0.2\n" +
            "nameserver 127.0.0.3\n" +
            "nameserver 127.0.0.4\n");
        UnixResolverDnsServerAddressStreamProvider p =
            new UnixResolverDnsServerAddressStreamProvider(f, null);

        IDnsServerAddressStream stream = p.NameServerAddressStream("");
        AssertHostNameEquals("127.0.0.2", stream.Next());
        AssertHostNameEquals("127.0.0.3", stream.Next());
        AssertHostNameEquals("127.0.0.4", stream.Next());

        stream = p.NameServerAddressStream("");
        AssertHostNameEquals("127.0.0.3", stream.Next());
        AssertHostNameEquals("127.0.0.4", stream.Next());
        AssertHostNameEquals("127.0.0.2", stream.Next());

        stream = p.NameServerAddressStream("");
        AssertHostNameEquals("127.0.0.4", stream.Next());
        AssertHostNameEquals("127.0.0.2", stream.Next());
        AssertHostNameEquals("127.0.0.3", stream.Next());

        stream = p.NameServerAddressStream("");
        AssertHostNameEquals("127.0.0.2", stream.Next());
        AssertHostNameEquals("127.0.0.3", stream.Next());
        AssertHostNameEquals("127.0.0.4", stream.Next());
    }

    [Fact]
    public void NameServerAddressStreamShouldAlwaysStartFromTheTopWhenRotationOptionsIsAbsent()
    {
        FileInfo f = BuildFile(tempDir, "domain linecorp.local\n" +
            "nameserver 127.0.0.2\n" +
            "nameserver 127.0.0.3\n" +
            "nameserver 127.0.0.4\n");
        UnixResolverDnsServerAddressStreamProvider p =
            new UnixResolverDnsServerAddressStreamProvider(f, null);

        IDnsServerAddressStream stream = p.NameServerAddressStream("");
        AssertHostNameEquals("127.0.0.2", stream.Next());
        AssertHostNameEquals("127.0.0.3", stream.Next());
        AssertHostNameEquals("127.0.0.4", stream.Next());

        stream = p.NameServerAddressStream("");
        AssertHostNameEquals("127.0.0.2", stream.Next());
        AssertHostNameEquals("127.0.0.3", stream.Next());
        AssertHostNameEquals("127.0.0.4", stream.Next());

        stream = p.NameServerAddressStream("");
        AssertHostNameEquals("127.0.0.2", stream.Next());
        AssertHostNameEquals("127.0.0.3", stream.Next());
        AssertHostNameEquals("127.0.0.4", stream.Next());
    }

    [Fact]
    public void DefaultReturnedWhenNoBetterMatch()
    {
        FileInfo f = BuildFile(tempDir, "domain linecorp.local\n" +
                           "nameserver 127.0.0.2\n" +
                           "nameserver 127.0.0.3\n");
        FileInfo f2 = BuildFile(tempDir, "domain squarecorp.local\n" +
                            "nameserver 127.0.0.4\n" +
                            "nameserver 127.0.0.5\n");
        UnixResolverDnsServerAddressStreamProvider p =
                new UnixResolverDnsServerAddressStreamProvider(f, f2);

        IDnsServerAddressStream stream = p.NameServerAddressStream("somehost");
        AssertHostNameEquals("127.0.0.2", stream.Next());
        AssertHostNameEquals("127.0.0.3", stream.Next());
    }

    [Fact]
    public void MoreRefinedSelectionReturnedWhenMatch()
    {
        FileInfo f = BuildFile(tempDir, "domain linecorp.local\n" +
                           "nameserver 127.0.0.2\n" +
                           "nameserver 127.0.0.3\n");
        FileInfo f2 = BuildFile(tempDir, "domain dc1.linecorp.local\n" +
                            "nameserver 127.0.0.4\n" +
                            "nameserver 127.0.0.5\n");
        UnixResolverDnsServerAddressStreamProvider p =
                new UnixResolverDnsServerAddressStreamProvider(f, f2);

        IDnsServerAddressStream stream = p.NameServerAddressStream("myhost.dc1.linecorp.local");
        AssertHostNameEquals("127.0.0.4", stream.Next());
        AssertHostNameEquals("127.0.0.5", stream.Next());
    }

    [Fact]
    public void NdotsOptionIsParsedIfPresent()
    {
        FileInfo f = BuildFile(tempDir, "search localdomain\n" +
            "nameserver 127.0.0.11\n" +
            "options ndots:0\n");
        Assert.Equal(0, UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions(f).Ndots());

        f = BuildFile(tempDir, "search localdomain\n" +
            "nameserver 127.0.0.11\n" +
            "options ndots:123 foo:goo\n");
        Assert.Equal(123, UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions(f).Ndots());
    }

    [Fact]
    public void DefaultValueReturnedIfNdotsOptionsNotPresent()
    {
        FileInfo f = BuildFile(tempDir, "search localdomain\n" +
            "nameserver 127.0.0.11\n");
        Assert.Equal(1, UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions(f).Ndots());
    }

    [Fact]
    public void TimeoutOptionIsParsedIfPresent()
    {
        FileInfo f = BuildFile(tempDir, "search localdomain\n" +
            "nameserver 127.0.0.11\n" +
            "options timeout:0\n");
        Assert.Equal(0, UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions(f).Timeout());

        f = BuildFile(tempDir, "search localdomain\n" +
            "nameserver 127.0.0.11\n" +
            "options foo:bar timeout:124\n");
        Assert.Equal(124, UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions(f).Timeout());
    }

    [Fact]
    public void DefaultValueReturnedIfTimeoutOptionsIsNotPresent()
    {
        FileInfo f = BuildFile(tempDir, "search localdomain\n" +
            "nameserver 127.0.0.11\n");
        Assert.Equal(5, UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions(f).Timeout());
    }

    [Fact]
    public void AttemptsOptionIsParsedIfPresent()
    {
        FileInfo f = BuildFile(tempDir, "search localdomain\n" +
            "nameserver 127.0.0.11\n" +
            "options attempts:0\n");
        Assert.Equal(0, UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions(f).Attempts());

        f = BuildFile(tempDir, "search localdomain\n" +
            "nameserver 127.0.0.11\n" +
            "options foo:bar attempts:12\n");
        Assert.Equal(12, UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions(f).Attempts());
    }

    [Fact]
    public void DefaultValueReturnedIfAttemptsOptionsIsNotPresent()
    {
        FileInfo f = BuildFile(tempDir, "search localdomain\n" +
            "nameserver 127.0.0.11\n");
        Assert.Equal(16, UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions(f).Attempts());
    }

    [Fact]
    public void EmptyEtcResolverDirectoryDoesNotThrow()
    {
        FileInfo f = BuildFile(tempDir, "domain linecorp.local\n" +
                           "nameserver 127.0.0.2\n" +
                           "nameserver 127.0.0.3\n");
        UnixResolverDnsServerAddressStreamProvider p =
                new UnixResolverDnsServerAddressStreamProvider(f, ListFiles(Path.Combine(tempDir, "netty-empty")));

        IDnsServerAddressStream stream = p.NameServerAddressStream("somehost");
        AssertHostNameEquals("127.0.0.2", stream.Next());
    }

    [Fact]
    public void SearchDomainsWithOnlyDomain()
    {
        FileInfo f = BuildFile(tempDir, "domain linecorp.local\n" +
                           "nameserver 127.0.0.2\n");
        IReadOnlyList<string> domains = UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverSearchDomains(f);
        Assert.Equal(new[] { "linecorp.local" }, domains);
    }

    [Fact]
    public void SearchDomainsWithOnlySearch()
    {
        FileInfo f = BuildFile(tempDir, "search linecorp.local\n" +
                           "nameserver 127.0.0.2\n");
        IReadOnlyList<string> domains = UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverSearchDomains(f);
        Assert.Equal(new[] { "linecorp.local" }, domains);
    }

    [Fact]
    public void SearchDomainsWithMultipleSearch()
    {
        FileInfo f = BuildFile(tempDir, "search linecorp.local\n" +
                           "search squarecorp.local\n" +
                           "nameserver 127.0.0.2\n");
        IReadOnlyList<string> domains = UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverSearchDomains(f);
        Assert.Equal(new[] { "linecorp.local", "squarecorp.local" }, domains);
    }

    [Fact]
    public void SearchDomainsWithMultipleSearchSeparatedByWhitespace()
    {
        FileInfo f = BuildFile(tempDir, "search linecorp.local squarecorp.local\n" +
                           "nameserver 127.0.0.2\n");
        IReadOnlyList<string> domains = UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverSearchDomains(f);
        Assert.Equal(new[] { "linecorp.local", "squarecorp.local" }, domains);
    }

    [Fact]
    public void SearchDomainsWithMultipleSearchSeparatedByTab()
    {
        FileInfo f = BuildFile(tempDir, "search linecorp.local\tsquarecorp.local\n" +
                "nameserver 127.0.0.2\n");
        IReadOnlyList<string> domains = UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverSearchDomains(f);
        Assert.Equal(new[] { "linecorp.local", "squarecorp.local" }, domains);
    }

    [Fact]
    public void SearchDomainsPrecedence()
    {
        FileInfo f = BuildFile(tempDir, "domain linecorp.local\n" +
                           "search squarecorp.local\n" +
                           "nameserver 127.0.0.2\n");
        IReadOnlyList<string> domains = UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverSearchDomains(f);
        Assert.Equal(new[] { "squarecorp.local" }, domains);
    }

    [Fact]
    public void IgnoreInvalidEntries()
    {
        FileInfo f = BuildFile(tempDir, "domain netty.local\n" +
                "nameserver nil\n" +
                "nameserver 127.0.0.3\n");
        UnixResolverDnsServerAddressStreamProvider p =
                new UnixResolverDnsServerAddressStreamProvider(f, null);

        IDnsServerAddressStream stream = p.NameServerAddressStream("somehost");
        Assert.Equal(1, stream.Size());
        AssertHostNameEquals("127.0.0.3", stream.Next());
    }

    private FileInfo BuildFile(string tempDir, string contents)
    {
        string path = Path.Combine(tempDir, "netty-dns-" + Guid.NewGuid().ToString().Substring(24) + ".txt");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(contents));

        return new FileInfo(path);
    }

    [Fact]
    public void IgnoreComments()
    {
        FileInfo f = BuildFile(tempDir, "domain linecorp.local\n" +
                "nameserver 127.0.0.2 #somecomment\n");
        UnixResolverDnsServerAddressStreamProvider p =
                new UnixResolverDnsServerAddressStreamProvider(f, null);

        IDnsServerAddressStream stream = p.NameServerAddressStream("somehost");
        AssertHostNameEquals("127.0.0.2", stream.Next());
    }

    [Fact]
    public void Ipv6Nameserver()
    {
        FileInfo f = BuildFile(tempDir, "search localdomain\n" +
                "nameserver 10.211.55.1\n" +
                "nameserver fe80::21c:42ff:fe00:18%nonexisting\n");
        UnixResolverDnsServerAddressStreamProvider p =
                new UnixResolverDnsServerAddressStreamProvider(f, null);

        IDnsServerAddressStream stream = p.NameServerAddressStream("somehost");
        AssertHostNameEquals("10.211.55.1", stream.Next());
    }

    private static void AssertHostNameEquals(string expectedHostname, EndPoint next)
    {
        Assert.Equal(expectedHostname, GetHostString(next), "unexpected hostname: " + next);
    }

    // CLR: InetSocketAddress.getHostString() (see the class comment).
    private static string GetHostString(EndPoint address)
    {
        return address is DnsEndPoint unresolved ? unresolved.Host : ((IPEndPoint) address).Address.ToString();
    }

    // CLR: File.listFiles() (see the class comment).
    private static FileInfo[] ListFiles(string dir)
    {
        return Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles() : null;
    }
}
