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

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Resolver.Dns;

// CLR: Comparator<InetAddress> is IComparer<IPAddress>; SocketProtocolFamily is the Transport enum (Inet, Inet6, Unix).
// Class<? extends InetAddress> is the AddressFamily of the preferred address, the handler port's mapping of instanceof
// Inet4Address/Inet6Address (docs/handler-porting.md Addresses), so getClass() equality and isAssignableFrom are
// AddressFamily comparisons. IllegalArgumentException is ArgumentException.
internal sealed class PreferredAddressTypeComparator : IComparer<IPAddress>
{

    private static readonly PreferredAddressTypeComparator IPv4 = new PreferredAddressTypeComparator(AddressFamily.InterNetwork);
    private static readonly PreferredAddressTypeComparator IPv6 = new PreferredAddressTypeComparator(AddressFamily.InterNetworkV6);

    internal static PreferredAddressTypeComparator Comparator(SocketProtocolFamily family)
    {
        switch (family)
        {
            case SocketProtocolFamily.Inet:
                return IPv4;
            case SocketProtocolFamily.Inet6:
                return IPv6;
            default:
                throw new ArgumentException();
        }
    }

    private readonly AddressFamily preferredAddressType;

    private PreferredAddressTypeComparator(AddressFamily preferredAddressType)
    {
        this.preferredAddressType = preferredAddressType;
    }

    public int Compare(IPAddress o1, IPAddress o2)
    {
        if (o1.AddressFamily == o2.AddressFamily)
        {
            return 0;
        }

        return preferredAddressType == o1.AddressFamily ? -1 : 1;
    }
}
