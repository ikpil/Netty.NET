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
using System.Net;
using System.Text;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver;

/**
 * Default {@link HostsFileEntriesResolver} that resolves hosts file entries only once.
 */
public sealed class DefaultHostsFileEntriesResolver : IHostsFileEntriesResolver
{
    private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(DefaultHostsFileEntriesResolver));
    private static readonly long DefaultRefreshInterval = SystemPropertyUtil.GetLong("io.netty.hostsFileRefreshInterval", /*nanos*/0);
    private readonly long _refreshInterval;
    private long _lastRefresh = Ticker.SystemTicker().NanoTime();
    private readonly HostsFileEntriesProvider.IParser _hostsFileParser;
    // CLR: publish both address families as one snapshot.
    private HostsFileEntriesProvider _entries;

    static DefaultHostsFileEntriesResolver()
    {
        Logger.Debug("-Dio.netty.hostsFileRefreshInterval: {}", DefaultRefreshInterval);
    }

    public DefaultHostsFileEntriesResolver() : this(HostsFileEntriesProvider.Parser(), DefaultRefreshInterval)
    {
    }

    // for testing purpose only
    internal DefaultHostsFileEntriesResolver(HostsFileEntriesProvider.IParser hostsFileParser, long refreshInterval)
    {
        _hostsFileParser = hostsFileParser;
        _refreshInterval = ObjectUtil.CheckPositiveOrZero(refreshInterval, "refreshInterval");
        _entries = ParseEntries(hostsFileParser);
    }

    public IPAddress Address(string inetHost, ResolvedAddressTypes resolvedAddressTypes)
    {
        return FirstAddress(Addresses(inetHost, resolvedAddressTypes));
    }

    /**
     * Resolves all addresses of a hostname against the entries in a hosts file, depending on the specified
     * {@link ResolvedAddressTypes}.
     *
     * @param inetHost the hostname to resolve
     * @param resolvedAddressTypes the address types to resolve
     * @return all matching addresses or {@code null} in case the hostname cannot be resolved
     */
    public IReadOnlyList<IPAddress> Addresses(string inetHost, ResolvedAddressTypes resolvedAddressTypes)
    {
        string normalized = Normalize(inetHost);
        EnsureHostsFileEntriesAreFresh();
        // CLR: read both families from one published snapshot.
        HostsFileEntriesProvider entries = Volatile.Read(ref _entries);
        IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> inet4Entries = entries.Ipv4Entries();
        IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> inet6Entries = entries.Ipv6Entries();
        switch (resolvedAddressTypes)
        {
            case ResolvedAddressTypes.Ipv4Only:
                return inet4Entries.GetValueOrDefault(normalized);
            case ResolvedAddressTypes.Ipv6Only:
                return inet6Entries.GetValueOrDefault(normalized);
            case ResolvedAddressTypes.Ipv4Preferred:
                IReadOnlyList<IPAddress> allInet4Addresses = inet4Entries.GetValueOrDefault(normalized);
                return allInet4Addresses != null ? AllAddresses(allInet4Addresses, inet6Entries.GetValueOrDefault(normalized)) :
                        inet6Entries.GetValueOrDefault(normalized);
            case ResolvedAddressTypes.Ipv6Preferred:
                IReadOnlyList<IPAddress> allInet6Addresses = inet6Entries.GetValueOrDefault(normalized);
                return allInet6Addresses != null ? AllAddresses(allInet6Addresses, inet4Entries.GetValueOrDefault(normalized)) :
                        inet4Entries.GetValueOrDefault(normalized);
            default:
                throw new ArgumentException("Unknown ResolvedAddressTypes " + resolvedAddressTypes);
        }
    }

    private void EnsureHostsFileEntriesAreFresh()
    {
        long interval = _refreshInterval;
        if (interval == 0)
        {
            return;
        }
        long last = Volatile.Read(ref _lastRefresh);
        long currentTime = Ticker.SystemTicker().NanoTime();
        if (unchecked(currentTime - last) > interval)
        {
            if (Interlocked.CompareExchange(ref _lastRefresh, currentTime, last) == last)
            {
                Volatile.Write(ref _entries, ParseEntries(_hostsFileParser));
            }
        }
    }

    // package-private for testing purposes
    internal string Normalize(string inetHost)
    {
        ArgumentNullException.ThrowIfNull(inetHost);
        // CLR: ToLowerInvariant equals toLowerCase(Locale.ENGLISH) for ASCII host names; for non-ASCII input Java also
        // applies special casing (for example U+0130 and final sigma) that the CLR simple case mapping does not.
        return inetHost.ToLowerInvariant();
    }

    private static IReadOnlyList<IPAddress> AllAddresses(IReadOnlyList<IPAddress> a, IReadOnlyList<IPAddress> b)
    {
        var result = new List<IPAddress>(a.Count + (b?.Count ?? 0));
        result.AddRange(a);
        if (b != null)
            result.AddRange(b);
        return result;
    }

    private static IPAddress FirstAddress(IReadOnlyList<IPAddress> addresses)
    {
        return addresses != null && addresses.Count != 0 ? addresses[0] : null;
    }

    private static HostsFileEntriesProvider ParseEntries(HostsFileEntriesProvider.IParser parser)
    {
        if (OperatingSystem.IsWindows())
        {
            // Ony windows there seems to be no standard for the encoding used for the hosts file, so let us
            // try multiple until we either were able to parse it or there is none left and so we return an
            // empty instance.
            // CLR: CharsetUtil.UTF_8 keeps a BOM as U+FEFF, which a preamble-less UTF8Encoding reproduces.
            return parser.ParseSilently(Encoding.Default, HostsFileEntriesProvider.UTF_16, new UTF8Encoding(false));
        }
        return parser.ParseSilently();
    }
}
