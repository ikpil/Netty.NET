/*
 * Copyright 2019 The Netty Project
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
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: InetAddress.getByName of an address literal is IPAddress.Parse (the literal mapping of the resolver-dns brief);
// SocketProtocolFamily.INET/INET6 are the Transport Inet/Inet6. Collections.addAll is a collection initializer and the
// stable Collections.sort is Enumerable.OrderBy (see NameServerComparatorTest).
public class PreferredAddressTypeComparatorTest
{

    [Fact]
    public void TestIpv4()
    {
        IPAddress ipv4Address1 = IPAddress.Parse("10.0.0.1");
        IPAddress ipv4Address2 = IPAddress.Parse("10.0.0.2");
        IPAddress ipv4Address3 = IPAddress.Parse("10.0.0.3");
        IPAddress ipv6Address1 = IPAddress.Parse("::1");
        IPAddress ipv6Address2 = IPAddress.Parse("::2");
        IPAddress ipv6Address3 = IPAddress.Parse("::3");

        PreferredAddressTypeComparator ipv4 = PreferredAddressTypeComparator.Comparator(SocketProtocolFamily.Inet);

        List<IPAddress> addressList = new List<IPAddress> { ipv4Address1, ipv4Address2, ipv6Address1,
                ipv6Address2, ipv4Address3, ipv6Address3 };
        addressList = addressList.OrderBy(a => a, ipv4).ToList();

        Assert.Equal(new List<IPAddress> { ipv4Address1, ipv4Address2, ipv4Address3, ipv6Address1,
                ipv6Address2, ipv6Address3 }, addressList);
    }

    [Fact]
    public void TestIpv6()
    {
        IPAddress ipv4Address1 = IPAddress.Parse("10.0.0.1");
        IPAddress ipv4Address2 = IPAddress.Parse("10.0.0.2");
        IPAddress ipv4Address3 = IPAddress.Parse("10.0.0.3");
        IPAddress ipv6Address1 = IPAddress.Parse("::1");
        IPAddress ipv6Address2 = IPAddress.Parse("::2");
        IPAddress ipv6Address3 = IPAddress.Parse("::3");

        PreferredAddressTypeComparator ipv4 = PreferredAddressTypeComparator.Comparator(SocketProtocolFamily.Inet6);

        List<IPAddress> addressList = new List<IPAddress> { ipv4Address1, ipv4Address2, ipv6Address1,
                ipv6Address2, ipv4Address3, ipv6Address3 };
        addressList = addressList.OrderBy(a => a, ipv4).ToList();

        Assert.Equal(new List<IPAddress> { ipv6Address1,
                ipv6Address2, ipv6Address3, ipv4Address1, ipv4Address2, ipv4Address3 }, addressList);
    }
}
