/*
 * Copyright 2012 The Netty Project
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
using System.Net;
using System.Net.Sockets;
using Netty.NET.Common;

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * Internet Protocol (IP) families used by the {@link DatagramChannel}
 *
 * @deprecated use {@link SocketProtocolFamily}.
 */
[Obsolete("use SocketProtocolFamily.")]
public enum InternetProtocolFamily
{
    // CLR: the constructor arguments (addressType, addressNumber) are returned by AddressType()/AddressNumber(),
    // because a C# enum member cannot carry instance fields.
    IPv4,
    IPv6,
}

// CLR: a C# enum cannot declare methods. C# 14 extension members keep the original call shapes:
// family.Localhost() and InternetProtocolFamily.Of(address).
#pragma warning disable CS0618 // The extensions belong to the obsolete type itself.
public static class InternetProtocolFamilyExtensions
{
    extension(InternetProtocolFamily @this)
    {
        /**
         * Returns the address type of this protocol family.
         */
        // CLR: IPAddress is one class for both families; IPAddress.AddressFamily distinguishes
        // the Inet4Address.class and Inet6Address.class address types.
        public AddressFamily AddressType()
        {
            switch (@this)
            {
                case InternetProtocolFamily.IPv4:
                    return AddressFamily.InterNetwork;
                case InternetProtocolFamily.IPv6:
                    return AddressFamily.InterNetworkV6;
                default:
                    throw new InvalidOperationException("Unsupported family " + @this);
            }
        }

        /**
         * Returns the
         * <a href="https://www.iana.org/assignments/address-family-numbers/address-family-numbers.xhtml">address number</a>
         * of the family.
         */
        public int AddressNumber()
        {
            switch (@this)
            {
                case InternetProtocolFamily.IPv4:
                    return 1;
                case InternetProtocolFamily.IPv6:
                    return 2;
                default:
                    throw new InvalidOperationException("Unsupported family " + @this);
            }
        }

        /**
         * Returns the {@link InetAddress} that represent the {@code LOCALHOST} for the family.
         */
        public IPAddress Localhost()
        {
            switch (@this)
            {
                case InternetProtocolFamily.IPv4:
                    return NetUtil.LOCALHOST4;
                case InternetProtocolFamily.IPv6:
                    return NetUtil.LOCALHOST6;
                default:
                    throw new InvalidOperationException("Unsupported family " + @this);
            }
        }

        public SocketProtocolFamily ToSocketProtocolFamily()
        {
            switch (@this)
            {
                case InternetProtocolFamily.IPv4:
                    return SocketProtocolFamily.Inet;
                case InternetProtocolFamily.IPv6:
                    return SocketProtocolFamily.Inet6;
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    extension(InternetProtocolFamily)
    {
        /**
         * Returns the {@link InternetProtocolFamily} for the given {@link InetAddress}.
         */
        // CLR: Java converts IPv4-mapped IPv6 input to Inet4Address; a CLR IPAddress keeps the mapped
        // InterNetworkV6 form, so IsIPv4MappedToIPv6 selects IPv4 as instanceof Inet4Address would.
        public static InternetProtocolFamily Of(IPAddress address)
        {
            if (address != null &&
                (address.AddressFamily == AddressFamily.InterNetwork || address.IsIPv4MappedToIPv6))
            {
                return InternetProtocolFamily.IPv4;
            }
            if (address != null && address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return InternetProtocolFamily.IPv6;
            }

            // CLR: Java concatenation renders a null address as "null".
            throw new ArgumentException("address " + (address?.ToString() ?? "null") + " not supported");
        }
    }
}
#pragma warning restore CS0618
