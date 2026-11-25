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

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Resolver.Dns;

/**
 * Provides an infinite sequence of DNS server addresses to {@link DnsNameResolver}.
 */
// CLR: InetSocketAddress is EndPoint and isUnresolved() a DnsEndPoint (docs/handler-porting.md Addresses);
// Iterable<? extends InetSocketAddress> is IEnumerable<EndPoint> (covariant, so a list of IPEndPoint is accepted) and the
// varargs overloads take params EndPoint[]. The anonymous DefaultDnsServerAddresses subclasses of sequential0 and
// shuffled0 are the named nested SequentialDnsServerAddresses and ShuffledDnsServerAddresses. The @Deprecated
// defaultAddressList() and defaultAddresses(), whose @deprecated text names DefaultDnsServerAddressStreamProvider's
// methods, are not ported (owner decision 2026-11-16). SocketAddress is the CLR counterpart of
// SocketUtils.socketAddress(hostname, port) that the providers share. IllegalArgumentException is ArgumentException and
// an address prints as EndPoint.ToString().
public abstract class DnsServerAddresses
{

    /**
     * Returns the {@link DnsServerAddresses} that yields the specified {@code addresses} sequentially. Once the
     * last address is yielded, it will start again from the first address.
     */
    public static DnsServerAddresses Sequential(IEnumerable<EndPoint> addresses)
    {
        return Sequential0(Sanitize(addresses));
    }

    /**
     * Returns the {@link DnsServerAddresses} that yields the specified {@code addresses} sequentially. Once the
     * last address is yielded, it will start again from the first address.
     */
    public static DnsServerAddresses Sequential(params EndPoint[] addresses)
    {
        return Sequential0(Sanitize(addresses));
    }

    private static DnsServerAddresses Sequential0(List<EndPoint> addresses)
    {
        if (addresses.Count == 1)
        {
            return Singleton(addresses[0]);
        }

        return new SequentialDnsServerAddresses(addresses);
    }

    private sealed class SequentialDnsServerAddresses : DefaultDnsServerAddresses
    {
        internal SequentialDnsServerAddresses(List<EndPoint> addresses) : base("sequential", addresses)
        {
        }

        public override IDnsServerAddressStream Stream()
        {
            return new SequentialDnsServerAddressStream(addresses, 0);
        }
    }

    /**
     * Returns the {@link DnsServerAddresses} that yields the specified {@code address} in a shuffled order. Once all
     * addresses are yielded, the addresses are shuffled again.
     */
    public static DnsServerAddresses Shuffled(IEnumerable<EndPoint> addresses)
    {
        return Shuffled0(Sanitize(addresses));
    }

    /**
     * Returns the {@link DnsServerAddresses} that yields the specified {@code addresses} in a shuffled order. Once all
     * addresses are yielded, the addresses are shuffled again.
     */
    public static DnsServerAddresses Shuffled(params EndPoint[] addresses)
    {
        return Shuffled0(Sanitize(addresses));
    }

    private static DnsServerAddresses Shuffled0(List<EndPoint> addresses)
    {
        if (addresses.Count == 1)
        {
            return Singleton(addresses[0]);
        }

        return new ShuffledDnsServerAddresses(addresses);
    }

    private sealed class ShuffledDnsServerAddresses : DefaultDnsServerAddresses
    {
        internal ShuffledDnsServerAddresses(List<EndPoint> addresses) : base("shuffled", addresses)
        {
        }

        public override IDnsServerAddressStream Stream()
        {
            return new ShuffledDnsServerAddressStream(addresses);
        }
    }

    /**
     * Returns the {@link DnsServerAddresses} that yields the specified {@code addresses} in a rotational sequential
     * order. It is similar to {@link #sequential(Iterable)}, but each {@link DnsServerAddressStream} starts from
     * a different starting point.  For example, the first {@link #stream()} will start from the first address, the
     * second one will start from the second address, and so on.
     */
    public static DnsServerAddresses Rotational(IEnumerable<EndPoint> addresses)
    {
        return Rotational0(Sanitize(addresses));
    }

    /**
     * Returns the {@link DnsServerAddresses} that yields the specified {@code addresses} in a rotational sequential
     * order. It is similar to {@link #sequential(Iterable)}, but each {@link DnsServerAddressStream} starts from
     * a different starting point.  For example, the first {@link #stream()} will start from the first address, the
     * second one will start from the second address, and so on.
     */
    public static DnsServerAddresses Rotational(params EndPoint[] addresses)
    {
        return Rotational0(Sanitize(addresses));
    }

    private static DnsServerAddresses Rotational0(List<EndPoint> addresses)
    {
        if (addresses.Count == 1)
        {
            return Singleton(addresses[0]);
        }

        return new RotationalDnsServerAddresses(addresses);
    }

    /**
     * Returns the {@link DnsServerAddresses} that yields only a single {@code address}.
     */
    public static DnsServerAddresses Singleton(EndPoint address)
    {
        ObjectUtil.CheckNotNull(address, "address");
        if (address is DnsEndPoint)
        {
            throw new ArgumentException("cannot use an unresolved DNS server address: " + address);
        }

        return new SingletonDnsServerAddresses(address);
    }

    // CLR: addresses instanceof Collection is the covariant IReadOnlyCollection<EndPoint> test, so a List<IPEndPoint>
    // also presizes the list; checkNonEmpty(list, "list") is Common's ObjectUtil.CheckNonEmpty.
    private static List<EndPoint> Sanitize(IEnumerable<EndPoint> addresses)
    {
        ObjectUtil.CheckNotNull(addresses, "addresses");

        List<EndPoint> list;
        if (addresses is IReadOnlyCollection<EndPoint> collection)
        {
            list = new List<EndPoint>(collection.Count);
        }
        else
        {
            list = new List<EndPoint>(4);
        }

        foreach (EndPoint a in addresses)
        {
            if (a == null)
            {
                break;
            }
            if (a is DnsEndPoint)
            {
                throw new ArgumentException("cannot use an unresolved DNS server address: " + a);
            }
            list.Add(a);
        }

        ObjectUtil.CheckNonEmpty(list, "list");
        return list;
    }

    private static List<EndPoint> Sanitize(EndPoint[] addresses)
    {
        ObjectUtil.CheckNotNull(addresses, "addresses");

        List<EndPoint> list = new List<EndPoint>(addresses.Length);
        foreach (EndPoint a in addresses)
        {
            if (a == null)
            {
                break;
            }
            if (a is DnsEndPoint)
            {
                throw new ArgumentException("cannot use an unresolved DNS server address: " + a);
            }
            list.Add(a);
        }

        if (list.Count == 0)
        {
            // CLR: the JDK returns the unmodifiable default list itself; a copy keeps List<EndPoint> mutable for
            // ShuffledDnsServerAddressStream, which shuffles the list it is given.
            return new List<EndPoint>(DefaultDnsServerAddressStreamProvider.DefaultAddressList());
        }

        return list;
    }

    /**
     * Starts a new infinite stream of DNS server addresses. This method is invoked by {@link DnsNameResolver} on every
     * uncached {@link DnsNameResolver#resolve(String)}or {@link DnsNameResolver#resolveAll(String)}.
     */
    public abstract IDnsServerAddressStream Stream();

    // CLR: SocketUtils.socketAddress(hostname, port) is new InetSocketAddress(hostname, port), which looks the host up
    // with InetAddress.getByName and keeps the address unresolved when the lookup fails
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html). As Transport's
    // internal AbstractBootstrap.SocketAddress does, the lookup is Resolver's DefaultNameResolver (the JDK literal rules;
    // UnknownHostException is Common's UnknownHostException) and an unresolved address is a DnsEndPoint. Like getByName it blocks the
    // caller for a name that is not a literal. Shared by ResolvConf, DirContextUtils,
    // DefaultDnsServerAddressStreamProvider and UnixResolverDnsServerAddressStreamProvider.
    internal static EndPoint SocketAddress(string hostname, int port)
    {
        if (hostname == null)
        {
            throw new ArgumentException("hostname can't be null");
        }
        IPAddress addr;
        try
        {
            addr = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE).ResolveAsync(hostname).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is UnknownHostException || e is ArgumentException)
        {
            return new DnsEndPoint(hostname, port);
        }
        return new IPEndPoint(addr, port);
    }
}
