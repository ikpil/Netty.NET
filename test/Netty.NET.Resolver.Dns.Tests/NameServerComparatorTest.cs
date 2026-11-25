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

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: @BeforeAll before() is the static constructor that assigns the static fields. new InetSocketAddress(
// InetAddress.getByAddress(host, bytes), 53) is an IPEndPoint of new IPAddress(bytes) (the comparator never reads the
// host name, which IPAddress does not carry) and createUnresolved a DnsEndPoint; Inet4Address.class/Inet6Address.class are
// AddressFamily.InterNetwork/InterNetworkV6 (NameServerComparator). Collections.sort(list, comparator), which "is
// guaranteed to be stable", is Enumerable.OrderBy, which "performs a stable sort" (see DefaultAuthoritativeDnsServerCache),
// and List equality is Assert.Equal over the sequences.
public class NameServerComparatorTest
{

    private static readonly EndPoint IPV4ADDRESS1;
    private static readonly EndPoint IPV4ADDRESS2;
    private static readonly EndPoint IPV4ADDRESS3;

    private static readonly EndPoint IPV6ADDRESS1;
    private static readonly EndPoint IPV6ADDRESS2;

    private static readonly EndPoint UNRESOLVED1;
    private static readonly EndPoint UNRESOLVED2;
    private static readonly EndPoint UNRESOLVED3;

    static NameServerComparatorTest()
    {
        IPV4ADDRESS1 = new IPEndPoint(new IPAddress(new byte[] { 10, 0, 0, 1 }), 53);
        IPV4ADDRESS2 = new IPEndPoint(new IPAddress(new byte[] { 10, 0, 0, 2 }), 53);
        IPV4ADDRESS3 = new IPEndPoint(new IPAddress(new byte[] { 10, 0, 0, 3 }), 53);

        IPV6ADDRESS1 = new IPEndPoint(new IPAddress(
                new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }), 53);
        IPV6ADDRESS2 = new IPEndPoint(new IPAddress(
                new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2 }), 53);

        UNRESOLVED1 = new DnsEndPoint("ns3", 53);
        UNRESOLVED2 = new DnsEndPoint("ns4", 53);
        UNRESOLVED3 = new DnsEndPoint("ns5", 53);
    }

    [Fact]
    public void TestCompareResolvedOnly()
    {
        NameServerComparator comparator = new NameServerComparator(AddressFamily.InterNetwork);
        int x = comparator.Compare(IPV4ADDRESS1, IPV6ADDRESS1);
        int y = comparator.Compare(IPV6ADDRESS1, IPV4ADDRESS1);

        Assert.Equal(-1, x);
        Assert.Equal(x, -y);

        Assert.Equal(0, comparator.Compare(IPV4ADDRESS1, IPV4ADDRESS1));
        Assert.Equal(0, comparator.Compare(IPV6ADDRESS1, IPV6ADDRESS1));
    }

    [Fact]
    public void TestCompareUnresolvedSimple()
    {
        NameServerComparator comparator = new NameServerComparator(AddressFamily.InterNetwork);
        int x = comparator.Compare(IPV4ADDRESS1, UNRESOLVED1);
        int y = comparator.Compare(UNRESOLVED1, IPV4ADDRESS1);

        Assert.Equal(-1, x);
        Assert.Equal(x, -y);
        Assert.Equal(0, comparator.Compare(IPV4ADDRESS1, IPV4ADDRESS1));
        Assert.Equal(0, comparator.Compare(UNRESOLVED1, UNRESOLVED1));
    }

    [Fact]
    public void TestCompareUnresolvedOnly()
    {
        NameServerComparator comparator = new NameServerComparator(AddressFamily.InterNetwork);
        int x = comparator.Compare(UNRESOLVED1, UNRESOLVED2);
        int y = comparator.Compare(UNRESOLVED2, UNRESOLVED1);

        Assert.Equal(0, x);
        Assert.Equal(x, -y);

        Assert.Equal(0, comparator.Compare(UNRESOLVED1, UNRESOLVED1));
        Assert.Equal(0, comparator.Compare(UNRESOLVED2, UNRESOLVED2));
    }

    [Fact]
    public void TestSortAlreadySortedPreferred()
    {
        List<EndPoint> expected = new List<EndPoint> { IPV4ADDRESS1, IPV4ADDRESS2, IPV4ADDRESS3 };
        List<EndPoint> addresses = new List<EndPoint>(expected);
        NameServerComparator comparator = new NameServerComparator(AddressFamily.InterNetwork);

        addresses = Sort(addresses, comparator);

        Assert.Equal(expected, addresses);
    }

    [Fact]
    public void TestSortAlreadySortedNotPreferred()
    {
        List<EndPoint> expected = new List<EndPoint> { IPV4ADDRESS1, IPV4ADDRESS2, IPV4ADDRESS3 };
        List<EndPoint> addresses = new List<EndPoint>(expected);
        NameServerComparator comparator = new NameServerComparator(AddressFamily.InterNetworkV6);

        addresses = Sort(addresses, comparator);

        Assert.Equal(expected, addresses);
    }

    [Fact]
    public void TestSortAlreadySortedUnresolved()
    {
        List<EndPoint> expected = new List<EndPoint> { UNRESOLVED1, UNRESOLVED2, UNRESOLVED3 };
        List<EndPoint> addresses = new List<EndPoint>(expected);
        NameServerComparator comparator = new NameServerComparator(AddressFamily.InterNetworkV6);

        addresses = Sort(addresses, comparator);

        Assert.Equal(expected, addresses);
    }

    [Fact]
    public void TestSortAlreadySortedMixed()
    {
        List<EndPoint> expected = new List<EndPoint> {
                IPV4ADDRESS1, IPV4ADDRESS2, IPV6ADDRESS1, IPV6ADDRESS2, UNRESOLVED1, UNRESOLVED2 };

        List<EndPoint> addresses = new List<EndPoint>(expected);
        NameServerComparator comparator = new NameServerComparator(AddressFamily.InterNetwork);

        addresses = Sort(addresses, comparator);

        Assert.Equal(expected, addresses);
    }

    [Fact]
    public void TestSort1()
    {
        List<EndPoint> expected = new List<EndPoint> {
                IPV4ADDRESS1, IPV4ADDRESS2, IPV6ADDRESS1, IPV6ADDRESS2, UNRESOLVED1, UNRESOLVED2 };
        List<EndPoint> addresses = new List<EndPoint>(
                new[] { IPV6ADDRESS1, IPV4ADDRESS1, IPV6ADDRESS2, UNRESOLVED1, UNRESOLVED2, IPV4ADDRESS2 });
        NameServerComparator comparator = new NameServerComparator(AddressFamily.InterNetwork);

        addresses = Sort(addresses, comparator);

        Assert.Equal(expected, addresses);
    }

    [Fact]
    public void TestSort2()
    {
        List<EndPoint> expected = new List<EndPoint> {
                IPV4ADDRESS1, IPV4ADDRESS2, IPV6ADDRESS1, IPV6ADDRESS2, UNRESOLVED1, UNRESOLVED2 };
        List<EndPoint> addresses = new List<EndPoint>(
                new[] { IPV4ADDRESS1, IPV6ADDRESS1, IPV6ADDRESS2, UNRESOLVED1, IPV4ADDRESS2, UNRESOLVED2 });
        NameServerComparator comparator = new NameServerComparator(AddressFamily.InterNetwork);

        addresses = Sort(addresses, comparator);

        Assert.Equal(expected, addresses);
    }

    // CLR: Collections.sort(list, comparator) (see the class comment).
    private static List<EndPoint> Sort(List<EndPoint> list, IComparer<EndPoint> comparator)
    {
        return list.OrderBy(a => a, comparator).ToList();
    }
}
