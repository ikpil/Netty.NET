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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Moq;
using Netty.NET.Common.Concurrent;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Resolver.Tests;

public class DefaultHostsFileEntriesResolverTest
{
    private static readonly Dictionary<string, IReadOnlyList<IPAddress>> LOCALHOST_V4_ADDRESSES =
            new() { ["localhost"] = new[] { IPAddress.Loopback } };
    private static readonly Dictionary<string, IReadOnlyList<IPAddress>> LOCALHOST_V6_ADDRESSES =
            new() { ["localhost"] = new[] { IPAddress.IPv6Loopback } };
    private const long ENTRIES_TTL = 60_000_000_000L;

    /**
     * show issue https://github.com/netty/netty/issues/5182
     * HostsFileParser tries to resolve hostnames as case-sensitive
     */
    [Fact]
    public void TestCaseInsensitivity()
    {
        DefaultHostsFileEntriesResolver resolver = new DefaultHostsFileEntriesResolver();
        //normalized somehow
        Assert.Equal(resolver.Normalize("localhost"), resolver.Normalize("LOCALHOST"));
    }

    [Fact]
    public void ShouldntFindWhenAddressTypeDoesntMatch()
    {
        HostsFileEntriesProvider.IParser parser = GivenHostsParserWith(
                LOCALHOST_V4_ADDRESSES,
                new Dictionary<string, IReadOnlyList<IPAddress>>()
        );

        DefaultHostsFileEntriesResolver resolver = new DefaultHostsFileEntriesResolver(parser, ENTRIES_TTL);

        IPAddress address = resolver.Address("localhost", ResolvedAddressTypes.Ipv6Only);
        Assert.Null(address, "Should pick an IPv6 address");
    }

    [Fact]
    public void ShouldPickIpv4WhenBothAreDefinedButIpv4IsPreferred()
    {
        HostsFileEntriesProvider.IParser parser = GivenHostsParserWith(
                LOCALHOST_V4_ADDRESSES,
                LOCALHOST_V6_ADDRESSES
        );

        DefaultHostsFileEntriesResolver resolver = new DefaultHostsFileEntriesResolver(parser, ENTRIES_TTL);

        IPAddress address = resolver.Address("localhost", ResolvedAddressTypes.Ipv4Preferred);
        Assert.Equal(AddressFamily.InterNetwork, address.AddressFamily, "Should pick an IPv4 address");
    }

    [Fact]
    public void ShouldPickIpv6WhenBothAreDefinedButIpv6IsPreferred()
    {
        HostsFileEntriesProvider.IParser parser = GivenHostsParserWith(
                LOCALHOST_V4_ADDRESSES,
                LOCALHOST_V6_ADDRESSES
        );

        DefaultHostsFileEntriesResolver resolver = new DefaultHostsFileEntriesResolver(parser, ENTRIES_TTL);

        IPAddress address = resolver.Address("localhost", ResolvedAddressTypes.Ipv6Preferred);
        Assert.Equal(AddressFamily.InterNetworkV6, address.AddressFamily, "Should pick an IPv6 address");
    }

    [Fact]
    public void ShouldntFindWhenAddressesTypeDoesntMatch()
    {
        HostsFileEntriesProvider.IParser parser = GivenHostsParserWith(
                LOCALHOST_V4_ADDRESSES,
                new Dictionary<string, IReadOnlyList<IPAddress>>()
        );

        DefaultHostsFileEntriesResolver resolver = new DefaultHostsFileEntriesResolver(parser, ENTRIES_TTL);

        IReadOnlyList<IPAddress> addresses = resolver.Addresses("localhost", ResolvedAddressTypes.Ipv6Only);
        Assert.Null(addresses, "Should pick an IPv6 address");
    }

    [Fact]
    public void ShouldPickIpv4FirstWhenBothAreDefinedButIpv4IsPreferred()
    {
        HostsFileEntriesProvider.IParser parser = GivenHostsParserWith(
                LOCALHOST_V4_ADDRESSES,
                LOCALHOST_V6_ADDRESSES
        );

        DefaultHostsFileEntriesResolver resolver = new DefaultHostsFileEntriesResolver(parser, ENTRIES_TTL);

        IReadOnlyList<IPAddress> addresses = resolver.Addresses("localhost", ResolvedAddressTypes.Ipv4Preferred);
        Assert.NotNull(addresses);
        Assert.Equal(2, addresses.Count);
        Assert.Equal(AddressFamily.InterNetwork, addresses[0].AddressFamily, "Should pick an IPv4 address");
        Assert.Equal(AddressFamily.InterNetworkV6, addresses[1].AddressFamily, "Should pick an IPv6 address");
    }

    [Fact]
    public void ShouldPickIpv6FirstWhenBothAreDefinedButIpv6IsPreferred()
    {
        HostsFileEntriesProvider.IParser parser = GivenHostsParserWith(
                LOCALHOST_V4_ADDRESSES,
                LOCALHOST_V6_ADDRESSES
        );

        DefaultHostsFileEntriesResolver resolver = new DefaultHostsFileEntriesResolver(parser, ENTRIES_TTL);

        IReadOnlyList<IPAddress> addresses = resolver.Addresses("localhost", ResolvedAddressTypes.Ipv6Preferred);
        Assert.NotNull(addresses);
        Assert.Equal(2, addresses.Count);
        Assert.Equal(AddressFamily.InterNetworkV6, addresses[0].AddressFamily, "Should pick an IPv6 address");
        Assert.Equal(AddressFamily.InterNetwork, addresses[1].AddressFamily, "Should pick an IPv4 address");
    }

    [Fact]
    public void ShouldNotRefreshHostsFileContentBeforeRefreshIntervalElapsed()
    {
        Dictionary<string, IReadOnlyList<IPAddress>> v4Addresses = new Dictionary<string, IReadOnlyList<IPAddress>>(LOCALHOST_V4_ADDRESSES);
        Dictionary<string, IReadOnlyList<IPAddress>> v6Addresses = new Dictionary<string, IReadOnlyList<IPAddress>>(LOCALHOST_V6_ADDRESSES);
        DefaultHostsFileEntriesResolver resolver =
                new DefaultHostsFileEntriesResolver(GivenHostsParserWith(v4Addresses, v6Addresses), ENTRIES_TTL);
        string newHost = Guid.NewGuid().ToString();

        v4Addresses.Add(newHost, new[] { IPAddress.Loopback });
        v6Addresses.Add(newHost, new[] { IPAddress.IPv6Loopback });

        Assert.Null(resolver.Address(newHost, ResolvedAddressTypes.Ipv4Only));
        Assert.Null(resolver.Address(newHost, ResolvedAddressTypes.Ipv6Only));
    }

    [Fact]
    public void ShouldRefreshHostsFileContentAfterRefreshInterval()
    {
        Dictionary<string, IReadOnlyList<IPAddress>> v4Addresses = new Dictionary<string, IReadOnlyList<IPAddress>>(LOCALHOST_V4_ADDRESSES);
        Dictionary<string, IReadOnlyList<IPAddress>> v6Addresses = new Dictionary<string, IReadOnlyList<IPAddress>>(LOCALHOST_V6_ADDRESSES);
        DefaultHostsFileEntriesResolver resolver =
                new DefaultHostsFileEntriesResolver(GivenHostsParserWith(v4Addresses, v6Addresses), /*nanos*/1);
        string newHost = Guid.NewGuid().ToString();

        IPAddress address = resolver.Address(newHost, ResolvedAddressTypes.Ipv6Only);
        Assert.Null(address);
        /*let refreshIntervalNanos = 1 elapse*/
        Thread.Sleep(1);
        v4Addresses.Add(newHost, new[] { IPAddress.Loopback });
        v6Addresses.Add(newHost, new[] { IPAddress.IPv6Loopback });

        Assert.Equal(IPAddress.Loopback, resolver.Address(newHost, ResolvedAddressTypes.Ipv4Only));
        Assert.Equal(IPAddress.IPv6Loopback, resolver.Address(newHost, ResolvedAddressTypes.Ipv6Only));
    }

    private HostsFileEntriesProvider.IParser GivenHostsParserWith(
        Dictionary<string, IReadOnlyList<IPAddress>> inet4Entries,
        Dictionary<string, IReadOnlyList<IPAddress>> inet6Entries)
    {
        var parser = new Mock<HostsFileEntriesProvider.IParser>();
        parser.Setup(p => p.ParseSilently()).Returns(() => new HostsFileEntriesProvider(inet4Entries, inet6Entries));
        parser.Setup(p => p.ParseSilently(It.IsAny<Encoding[]>())).Returns(() => new HostsFileEntriesProvider(inet4Entries, inet6Entries));
        return parser.Object;
    }
}
