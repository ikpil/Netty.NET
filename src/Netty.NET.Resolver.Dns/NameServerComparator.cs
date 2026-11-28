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
using System.Net;
using System.Net.Sockets;

namespace Netty.NET.Resolver.Dns;

/**
 * Special {@link Comparator} implementation to sort the nameservers to use when follow redirects.
 *
 * This implementation follows all the semantics listed in the
 * <a href="https://docs.oracle.com/javase/8/docs/api/java/util/Comparator.html">Comparator apidocs</a>
 * with the limitation that {@link InetSocketAddress#equals(Object)} will not result in the same return value as
 * {@link #compare(InetSocketAddress, InetSocketAddress)}. This is completely fine as this should only be used
 * to sort {@link List}s.
 */
// CLR: Comparator<InetSocketAddress> is IComparer<EndPoint>. Class<? extends InetAddress> preferredAddressType is
// the AddressFamily of the preferred address (Inet4Address.class is InterNetwork, Inet6Address.class
// InterNetworkV6), the handler port's mapping of instanceof Inet4Address/Inet6Address, so getClass() equality and
// isAssignableFrom are AddressFamily comparisons; an AddressFamily is a value and cannot be null, so checkNotNull
// has no counterpart. Serializable (serialVersionUID) has no counterpart.
public sealed class NameServerComparator : IComparer<EndPoint>
{

    private readonly AddressFamily preferredAddressType;

    public NameServerComparator(AddressFamily preferredAddressType)
    {
        this.preferredAddressType = preferredAddressType;
    }

    public int Compare(EndPoint addr1, EndPoint addr2)
    {
        if (addr1.Equals(addr2))
        {
            return 0;
        }

        if (!(addr1 is DnsEndPoint) && !(addr2 is DnsEndPoint))
        {
            AddressFamily family1 = ((IPEndPoint) addr1).Address.AddressFamily;
            if (family1 == ((IPEndPoint) addr2).Address.AddressFamily)
            {
                return 0;
            }

            return preferredAddressType == family1 ? -1 : 1;
        }

        if (addr1 is DnsEndPoint && addr2 is DnsEndPoint)
        {
            return 0;
        }

        return addr1 is DnsEndPoint ? 1 : -1;
    }
}
