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
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Netty.NET.Common;

namespace Netty.NET.Resolver.Dns;

// CLR: the package-private final class is internal sealed. The name server address of a query is resolved, so
// InetSocketAddress is IPEndPoint (docs/handler-porting.md, Addresses) and the "assert !nameServerAddr.isUnresolved()"
// checks have no counterpart: an IPEndPoint is always resolved. Map/HashMap is Dictionary and IntObjectHashMap the
// BCL Dictionary<int, V> (owner decision 2026-11-19, confirmed 2026-11-25); put(k, v) returns the replaced value, so the
// asserted puts read the old value with TryGetValue before the indexer stores the new one. synchronized (map) is
// lock (map) and the synchronized methods of DnsQueryContextMap lock (this), the monitor Java uses. instanceof
// Inet4Address/Inet6Address is the AddressFamily (docs/handler-porting.md, Addresses); isLoopbackAddress() is
// IPAddress.IsLoopback ("Indicates whether the specified IP address is the loopback address",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.isloopback?view=net-10.0). The BCL has no
// isIPv4CompatibleAddress(); Inet6Address names "::d.d.d.d" "the general forms of ... an IPv4-compatible IPv6 address"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/Inet6Address.html), so
// IsIPv4CompatibleAddress tests the first twelve bytes for zero. InetAddress.getByAddress(bytes) is new
// IPAddress(bytes); its UnknownHostException (wrong length) is the ArgumentException of the constructor
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.-ctor?view=net-10.0) and java.lang.Error is
// InvalidOperationException, as infra's DnsAddressDecoder maps them. assert is Debug.Assert.
internal sealed class DnsQueryContextManager
{

    /**
     * A map whose key is the DNS server address and value is the map of the DNS query ID and its corresponding
     * {@link DnsQueryContext}.
     */
    private readonly Dictionary<IPEndPoint, DnsQueryContextMap> map =
            new Dictionary<IPEndPoint, DnsQueryContextMap>();

    /**
     * Add {@link DnsQueryContext} to the context manager and return the ID that should be used for the query.
     * This method will return {@code -1} if an ID could not be generated and the context was not stored.
     *
     * @param nameServerAddr    The {@link InetSocketAddress} of the nameserver to query.
     * @param qCtx              The {@link {@link DnsQueryContext} to store.
     * @return                  the ID that should be used or {@code -1} if none could be generated.
     */
    internal int Add(IPEndPoint nameServerAddr, DnsQueryContext qCtx)
    {
        DnsQueryContextMap contexts = GetOrCreateContextMap(nameServerAddr);
        return contexts.Add(qCtx);
    }

    /**
     * Return the {@link DnsQueryContext} for the given {@link InetSocketAddress} and id or {@code null} if
     * none could be found.
     *
     * @param nameServerAddr    The {@link InetSocketAddress} of the nameserver.
     * @param id                The id that identifies the {@link DnsQueryContext} and was used for the query.
     * @return                  The context or {@code null} if none could be found.
     */
    internal DnsQueryContext Get(IPEndPoint nameServerAddr, int id)
    {
        DnsQueryContextMap contexts = GetContextMap(nameServerAddr);
        if (contexts == null)
        {
            return null;
        }

        return contexts.Get(id);
    }

    /**
     * Remove the {@link DnsQueryContext} for the given {@link InetSocketAddress} and id or {@code null} if
     * none could be found.
     *
     * @param nameServerAddr    The {@link InetSocketAddress} of the nameserver.
     * @param id                The id that identifies the {@link DnsQueryContext} and was used for the query.
     * @return                  The context or {@code null} if none could be removed.
     */
    internal DnsQueryContext Remove(IPEndPoint nameServerAddr, int id)
    {
        DnsQueryContextMap contexts = GetContextMap(nameServerAddr);
        if (contexts == null)
        {
            return null;
        }

        return contexts.Remove(id);
    }

    private DnsQueryContextMap GetContextMap(IPEndPoint nameServerAddr)
    {
        lock (map)
        {
            map.TryGetValue(nameServerAddr, out DnsQueryContextMap contexts);
            return contexts;
        }
    }

    private DnsQueryContextMap GetOrCreateContextMap(IPEndPoint nameServerAddr)
    {
        lock (map)
        {
            if (map.TryGetValue(nameServerAddr, out DnsQueryContextMap contexts))
            {
                return contexts;
            }

            DnsQueryContextMap newContexts = new DnsQueryContextMap();
            IPAddress a = nameServerAddr.Address;
            int port = nameServerAddr.Port;
            map.TryGetValue(nameServerAddr, out DnsQueryContextMap old);
            map[nameServerAddr] = newContexts;
            // Assert that we didn't replace an existing mapping.
            Debug.Assert(old == null, "DnsQueryContextMap already exists for " + nameServerAddr);

            IPEndPoint extraAddress = null;

            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                // Also add the mapping for the IPv4-compatible IPv6 address.
                IPAddress a4 = a;
                if (IPAddress.IsLoopback(a4))
                {
                    extraAddress = new IPEndPoint(NetUtil.LOCALHOST6, port);
                }
                else
                {
                    extraAddress = new IPEndPoint(ToCompactAddress(a4), port);
                }
            }
            else if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // Also add the mapping for the IPv4 address if this IPv6 address is compatible.
                IPAddress a6 = a;
                if (IPAddress.IsLoopback(a6))
                {
                    extraAddress = new IPEndPoint(NetUtil.LOCALHOST4, port);
                }
                else if (IsIPv4CompatibleAddress(a6))
                {
                    extraAddress = new IPEndPoint(ToIPv4Address(a6), port);
                }
            }

            if (extraAddress != null)
            {
                map.TryGetValue(extraAddress, out old);
                map[extraAddress] = newContexts;
                // Assert that we didn't replace an existing mapping.
                Debug.Assert(old == null, "DnsQueryContextMap already exists for " + extraAddress);
            }

            return newContexts;
        }
    }

    private static IPAddress ToCompactAddress(IPAddress a4)
    {
        byte[] b4 = a4.GetAddressBytes();
        byte[] b6 = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, b4[0], b4[1], b4[2], b4[3] };

        try
        {
            return new IPAddress(b6);
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException(e.Message, e);
        }
    }

    private static IPAddress ToIPv4Address(IPAddress a6)
    {
        Debug.Assert(IsIPv4CompatibleAddress(a6));

        byte[] b6 = a6.GetAddressBytes();
        byte[] b4 = { b6[12], b6[13], b6[14], b6[15] };

        try
        {
            return new IPAddress(b4);
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException(e.Message, e);
        }
    }

    // CLR: Inet6Address.isIPv4CompatibleAddress(): the IPv6 address ::d.d.d.d (the first twelve bytes are zero).
    private static bool IsIPv4CompatibleAddress(IPAddress a6)
    {
        byte[] b6 = a6.GetAddressBytes();

        for (int i = 0; i < 12; i++)
        {
            if (b6[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class DnsQueryContextMap
    {

        private readonly DnsQueryIdSpace idSpace = new DnsQueryIdSpace();

        // We increment on every usage so start with -1, this will ensure we start with 0 as first id.
        private readonly Dictionary<int, DnsQueryContext> map = new Dictionary<int, DnsQueryContext>();

        internal int Add(DnsQueryContext ctx)
        {
            lock (this)
            {
                int id = idSpace.NextId();
                if (id == -1)
                {
                    // -1 means that we couldn't reserve an id to use. In this case return early and not store the
                    // context in the map.
                    return -1;
                }

                map.TryGetValue(id, out DnsQueryContext oldCtx);
                map[id] = ctx;
                Debug.Assert(oldCtx == null);
                return id;
            }
        }

        internal DnsQueryContext Get(int id)
        {
            lock (this)
            {
                map.TryGetValue(id, out DnsQueryContext ctx);
                return ctx;
            }
        }

        internal DnsQueryContext Remove(int id)
        {
            lock (this)
            {
                map.Remove(id, out DnsQueryContext result);

                if (result != null)
                {
                    idSpace.PushId(id);
                }

                Debug.Assert(result != null, "DnsQueryContext not found, id: " + id);
                return result;
            }
        }
    }
}
