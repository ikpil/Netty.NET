/*
 * Copyright 2017 The Netty Project
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
using System.Collections.ObjectModel;
using System.Net;

namespace Netty.NET.Resolver;

/**
 * A container of hosts file entries.
 * The mappings contain only the first entry per hostname.
 * Consider using {@link HostsFileEntriesProvider} when mappings with all entries per hostname are needed.
 */
public sealed class HostsFileEntries
{
    /**
     * Empty entries
     */
    public static readonly HostsFileEntries Empty = new(new Dictionary<string, IPAddress>(), new Dictionary<string, IPAddress>());

    private readonly IReadOnlyDictionary<string, IPAddress> _inet4Entries;
    private readonly IReadOnlyDictionary<string, IPAddress> _inet6Entries;
    public HostsFileEntries(IDictionary<string, IPAddress> inet4Entries, IDictionary<string, IPAddress> inet6Entries)
    {
        _inet4Entries = new ReadOnlyDictionary<string, IPAddress>(new Dictionary<string, IPAddress>(inet4Entries));
        _inet6Entries = new ReadOnlyDictionary<string, IPAddress>(new Dictionary<string, IPAddress>(inet6Entries));
    }
    /**
     * The IPv4 entries
     * @return the IPv4 entries
     */
    public IReadOnlyDictionary<string, IPAddress> Inet4Entries() => _inet4Entries;
    /**
     * The IPv6 entries
     * @return the IPv6 entries
     */
    public IReadOnlyDictionary<string, IPAddress> Inet6Entries() => _inet6Entries;
}
