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

    public DefaultHostsFileEntriesResolver() : this(HostsFileEntriesProvider.Parser(), DefaultRefreshInterval) { }

    // for testing purpose only
    internal DefaultHostsFileEntriesResolver(HostsFileEntriesProvider.IParser hostsFileParser, long refreshIntervalNanos)
    {
        ArgumentNullException.ThrowIfNull(hostsFileParser);
        ArgumentOutOfRangeException.ThrowIfNegative(refreshIntervalNanos);
        _hostsFileParser = hostsFileParser;
        _refreshInterval = refreshIntervalNanos;
        _entries = ParseEntries(hostsFileParser);
    }

    public IPAddress Address(string inetHost, ResolvedAddressTypes resolvedAddressTypes)
    {
        var addresses = Addresses(inetHost, resolvedAddressTypes);
        return addresses != null && addresses.Count != 0 ? addresses[0] : null;
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
        var entries = Volatile.Read(ref _entries);
        entries.Ipv4Entries().TryGetValue(normalized, out var ipv4);
        entries.Ipv6Entries().TryGetValue(normalized, out var ipv6);
        return resolvedAddressTypes switch
        {
            ResolvedAddressTypes.Ipv4Only => ipv4,
            ResolvedAddressTypes.Ipv6Only => ipv6,
            ResolvedAddressTypes.Ipv4Preferred => ipv4 != null ? AllAddresses(ipv4, ipv6) : ipv6,
            ResolvedAddressTypes.Ipv6Preferred => ipv6 != null ? AllAddresses(ipv6, ipv4) : ipv4,
            _ => throw new ArgumentOutOfRangeException(nameof(resolvedAddressTypes))
        };
    }

    private void EnsureHostsFileEntriesAreFresh()
    {
        if (_refreshInterval == 0)
            return;
        long last = Volatile.Read(ref _lastRefresh);
        long now = Ticker.SystemTicker().NanoTime();
        if (unchecked(now - last) > _refreshInterval && Interlocked.CompareExchange(ref _lastRefresh, now, last) == last)
            Volatile.Write(ref _entries, ParseEntries(_hostsFileParser));
    }

    // package-private for testing purposes
    internal string Normalize(string inetHost)
    {
        ArgumentNullException.ThrowIfNull(inetHost);
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

    private static HostsFileEntriesProvider ParseEntries(HostsFileEntriesProvider.IParser parser)
    {
        if (OperatingSystem.IsWindows())
        {
            // Ony windows there seems to be no standard for the encoding used for the hosts file, so let us
            // try multiple until we either were able to parse it or there is none left and so we return an
            // empty instance.
            // CLR: try both UTF-16 byte orders explicitly; Java UTF_16 recognizes either BOM.
            return parser.ParseSilently(Encoding.Default, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF8);
        }
        return parser.ParseSilently();
    }
}
