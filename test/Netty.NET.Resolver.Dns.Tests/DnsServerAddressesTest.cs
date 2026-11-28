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

using System.Collections.Generic;
using System.Net;
using Netty.NET.Common;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: new InetSocketAddress(NetUtil.LOCALHOST, port) is an IPEndPoint; the static import of defaultAddressList() is
// DefaultDnsServerAddressStreamProvider.DefaultAddressList() and AssertJ isGreaterThan(0) is Assert.True(count > 0);
// Collections.newSetFromMap(new IdentityHashMap()) is a HashSet with ReferenceEqualityComparer.Instance.
public class DnsServerAddressesTest
{

    private static readonly IPEndPoint ADDR1 = new IPEndPoint(NetUtil.LOCALHOST, 1);
    private static readonly IPEndPoint ADDR2 = new IPEndPoint(NetUtil.LOCALHOST, 2);
    private static readonly IPEndPoint ADDR3 = new IPEndPoint(NetUtil.LOCALHOST, 3);

    [Fact]
    public void TestDefaultAddresses()
    {
        Assert.True(DefaultDnsServerAddressStreamProvider.DefaultAddressList().Count > 0);
    }

    [Fact]
    public void TestSequential()
    {
        DnsServerAddresses seq = DnsServerAddresses.Sequential(ADDR1, ADDR2, ADDR3);
        Assert.NotSame(seq.Stream(), seq.Stream());

        for (int j = 0; j < 2; j ++)
        {
            IDnsServerAddressStream i = seq.Stream();
            AssertNext(i, ADDR1);
            AssertNext(i, ADDR2);
            AssertNext(i, ADDR3);
            AssertNext(i, ADDR1);
            AssertNext(i, ADDR2);
            AssertNext(i, ADDR3);
        }
    }

    [Fact]
    public void TestRotational()
    {
        DnsServerAddresses seq = DnsServerAddresses.Rotational(ADDR1, ADDR2, ADDR3);

        IDnsServerAddressStream i = seq.Stream();
        AssertNext(i, ADDR1);
        AssertNext(i, ADDR2);
        AssertNext(i, ADDR3);
        AssertNext(i, ADDR1);
        AssertNext(i, ADDR2);
        AssertNext(i, ADDR3);

        i = seq.Stream();
        AssertNext(i, ADDR2);
        AssertNext(i, ADDR3);
        AssertNext(i, ADDR1);
        AssertNext(i, ADDR2);
        AssertNext(i, ADDR3);
        AssertNext(i, ADDR1);

        i = seq.Stream();
        AssertNext(i, ADDR3);
        AssertNext(i, ADDR1);
        AssertNext(i, ADDR2);
        AssertNext(i, ADDR3);
        AssertNext(i, ADDR1);
        AssertNext(i, ADDR2);

        i = seq.Stream();
        AssertNext(i, ADDR1);
        AssertNext(i, ADDR2);
        AssertNext(i, ADDR3);
        AssertNext(i, ADDR1);
        AssertNext(i, ADDR2);
        AssertNext(i, ADDR3);
    }

    [Fact]
    public void TestShuffled()
    {
        DnsServerAddresses seq = DnsServerAddresses.Shuffled(ADDR1, ADDR2, ADDR3);

        // Ensure that all three addresses are returned by the iterator.
        // In theory, this test can fail at extremely low chance, but we don't really care.
        ISet<EndPoint> set = new HashSet<EndPoint>(ReferenceEqualityComparer.Instance);
        IDnsServerAddressStream i = seq.Stream();

        for (int j = 0; j < 1048576; j ++)
        {
            set.Add(i.Next());
        }

        Assert.Equal(3, set.Count);
        Assert.NotSame(seq.Stream(), seq.Stream());
    }

    [Fact]
    public void TestSingleton()
    {
        DnsServerAddresses seq = DnsServerAddresses.Singleton(ADDR1);

        // Should return the same iterator instance for least possible footprint.
        Assert.Same(seq.Stream(), seq.Stream());

        IDnsServerAddressStream i = seq.Stream();
        AssertNext(i, ADDR1);
        AssertNext(i, ADDR1);
        AssertNext(i, ADDR1);
    }

    private static void AssertNext(IDnsServerAddressStream i, IPEndPoint addr)
    {
        Assert.Same(i.Next(), addr);
    }
}
