/*
 * Copyright 2015 The Netty Project
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
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Resolver;

/**
 * A {@link InetNameResolver} that resolves using JDK's built-in domain name lookup mechanism.
 * Note that this resolver performs a blocking name lookup from the caller thread.
 */
// CLR: use the OS resolver through System.Net.Dns asynchronously instead of blocking the caller thread.
// SocketUtils.addressByName/allAddressesByName (InetAddress.getByName/getAllByName) are replaced by the private
// helpers below, which keep the JDK literal and ordering rules that Dns.GetHostAddresses does not share.
// UnknownHostException is Common's UnknownHostException (owner decision 2026-11-25; it was SocketException with
// SocketError.HostNotFound before). Dns.GetHostAddressesAsync reports a failed lookup with a SocketException ("An error
// is encountered when resolving hostNameOrAddress", https://learn.microsoft.com/dotnet/api/system.net.dns.gethostaddressesasync),
// where getAllByName throws UnknownHostException; that SocketException becomes the cause of an UnknownHostException
// whose message is the host name.
public class DefaultNameResolver : InetNameResolver
{
    // CLR: InetAddress orders lookup results by java.net.preferIPv6Addresses: "false" (default) puts IPv4 first,
    // "true" puts IPv6 first and "system" keeps the resolver's order.
    private static readonly string PREFER_IPV6_ADDRESSES =
        SystemPropertyUtil.Get("java.net.preferIPv6Addresses", "false");

    public DefaultNameResolver(IEventExecutor executor) : base(executor) { }

    protected override async Task<IPAddress> DoResolveAsync(string inetHost, CancellationToken cancellationToken)
    {
        return await AddressByName(inetHost, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<IReadOnlyList<IPAddress>> DoResolveAllAsync(string inetHost, CancellationToken cancellationToken)
    {
        return await AllAddressesByName(inetHost, cancellationToken).ConfigureAwait(false);
    }

    // InetAddress.getByName returns the first address of getAllByName.
    private static async Task<IPAddress> AddressByName(string hostname, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = await AllAddressesByName(hostname, cancellationToken).ConfigureAwait(false);
        return addresses[0];
    }

    private static async Task<IPAddress[]> AllAddressesByName(string hostname, CancellationToken cancellationToken)
    {
        // InetAddress treats null and the empty host as loopback; Dns treats the empty host as this machine.
        if (string.IsNullOrEmpty(hostname))
        {
            return new[] { IPAddress.Loopback };
        }
        string host = hostname;
        bool ipv6Expected = false;
        if (host[0] == '[')
        {
            // This is supposed to be an IPv6 literal
            if (host.Length > 2 && host[^1] == ']')
            {
                host = host[1..^1];
                ipv6Expected = true;
            }
            else
            {
                throw InvalidIPv6LiteralException(host, false);
            }
        }
        // Check and try to parse host string as an IP address literal
        if (Uri.IsHexDigit(host[0]) || CharUnicodeInfo.GetDecimalDigitValue(host[0]) >= 0 || host[0] == ':')
        {
            IPAddress addr = null;
            if (!ipv6Expected)
            {
                // check if it is IPv4 address only if host is not wrapped in '[]'
                addr = TextToNumericFormatV4(host);
                // CLR: the JDK rejects BSD-style IPv4 forms (hex or octal parts) instead of resolving them as a
                // name; IPAddress.TryParse would accept them, and Dns would return the parsed address.
                if (addr == null && IPAddress.TryParse(host, out var bsd) && bsd.AddressFamily == AddressFamily.InterNetwork)
                {
                    throw new UnknownHostException(hostname);
                }
            }
            if (addr == null)
            {
                // Try to parse host string as an IPv6 literal
                if (IPAddress.TryParse(host, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // An IPv4-mapped literal becomes an Inet4Address.
                    addr = v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6;
                }
                else if (host.Contains(':') || ipv6Expected)
                {
                    throw InvalidIPv6LiteralException(host, ipv6Expected);
                }
            }
            if (addr != null)
            {
                return new[] { addr };
            }
        }
        else if (ipv6Expected)
        {
            // We were expecting an IPv6 Literal, but got something else
            throw InvalidIPv6LiteralException(host, true);
        }
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException e)
        {
            throw new UnknownHostException(hostname, e);
        }
        if (addresses.Length == 0)
        {
            throw new UnknownHostException(hostname);
        }
        return PREFER_IPV6_ADDRESSES switch
        {
            "true" => addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6 ? 0 : 1).ToArray(),
            "system" => addresses,
            _ => addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray(),
        };
    }

    // CLR: UnknownHostException(host + ": invalid IPv6 address literal").
    private static UnknownHostException InvalidIPv6LiteralException(string host, bool wrapInBrackets)
    {
        return new UnknownHostException(
                (wrapInBrackets ? "[" + host + "]" : host) + ": invalid IPv6 address literal");
    }

    // CLR: IPAddressUtil.textToNumericFormatV4. One to four decimal parts; the last part fills the remaining bytes.
    // Leading zeros are decimal, unlike IPAddress.TryParse, which reads them as octal.
    private static IPAddress TextToNumericFormatV4(string src)
    {
        if (src.Length == 0)
        {
            return null;
        }
        byte[] res = new byte[4];
        long tmpValue = 0;
        int currByte = 0;
        bool newOctet = true;
        foreach (char c in src)
        {
            if (c == '.')
            {
                if (newOctet || tmpValue < 0 || tmpValue > 0xff || currByte == 3)
                {
                    return null;
                }
                res[currByte++] = (byte)(tmpValue & 0xff);
                tmpValue = 0;
                newOctet = true;
            }
            else
            {
                int digit = CharUnicodeInfo.GetDecimalDigitValue(c);
                if (digit < 0)
                {
                    return null;
                }
                tmpValue *= 10;
                tmpValue += digit;
                if (tmpValue > 0xffffffffL)
                {
                    return null;
                }
                newOctet = false;
            }
        }
        if (newOctet || tmpValue < 0 || tmpValue >= 1L << ((4 - currByte) * 8))
        {
            return null;
        }
        switch (currByte)
        {
            case 0:
                res[0] = (byte)((tmpValue >> 24) & 0xff);
                goto case 1;
            case 1:
                res[1] = (byte)((tmpValue >> 16) & 0xff);
                goto case 2;
            case 2:
                res[2] = (byte)((tmpValue >> 8) & 0xff);
                goto case 3;
            case 3:
                res[3] = (byte)(tmpValue & 0xff);
                break;
        }
        return new IPAddress(res);
    }
}
