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
using System.Collections.ObjectModel;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * Maps a domain name to its associated value object.
 * <p>
 * DNS wildcard is supported as hostname, so you can use {@code *.netty.io} to match both {@code netty.io}
 * and {@code downloads.netty.io}.
 * </p>
 * @deprecated Use {@link DomainWildcardMappingBuilder}}
 */
public class DomainNameMapping<T> : IMapping<string, T>
{
    protected readonly T _defaultValue;
    private readonly IDictionary<string, T> _map;
    private readonly IDictionary<string, T> _unmodifiableMap;

    /**
     * Creates a default, order-sensitive mapping. If your hostnames are in conflict, the mapping
     * will choose the one you add first.
     *
     * @param defaultValue the default value for {@link #map(string)} to return when nothing matches the input
     * @deprecated use {@link DomainNameMappingBuilder} to create and fill the mapping instead
     */
    public DomainNameMapping(T defaultValue)
        : this(4, defaultValue)
    {
    }

    /**
     * Creates a default, order-sensitive mapping. If your hostnames are in conflict, the mapping
     * will choose the one you add first.
     *
     * @param initialCapacity initial capacity for the internal map
     * @param defaultValue    the default value for {@link #map(string)} to return when nothing matches the input
     * @deprecated use {@link DomainNameMappingBuilder} to create and fill the mapping instead
     */
    public DomainNameMapping(int initialCapacity, T defaultValue)
        : this(new LinkedHashMap<string, T>(initialCapacity), defaultValue)
    {
    }

    public DomainNameMapping(IDictionary<string, T> map, T defaultValue)
    {
        _defaultValue = checkNotNull(defaultValue, "defaultValue");
        _map = map;
        _unmodifiableMap = map != null
            ? new ReadOnlyDictionary<string, T>(map)
            : null;
    }

    /**
     * Adds a mapping that maps the specified (optionally wildcard) host name to the specified output value.
     * <p>
     * <a href="https://en.wikipedia.org/wiki/Wildcard_DNS_record">DNS wildcard</a> is supported as hostname.
     * For example, you can use {@code *.netty.io} to match {@code netty.io} and {@code downloads.netty.io}.
     * </p>
     *
     * @param hostname the host name (optionally wildcard)
     * @param output   the output value that will be returned by {@link #map(string)} when the specified host name
     *                 matches the specified input host name
     * @deprecated use {@link DomainNameMappingBuilder} to create and fill the mapping instead
     */
    public virtual DomainNameMapping<T> add(string hostname, T output)
    {
        _map.Add(normalizeHostname(checkNotNull(hostname, "hostname")), checkNotNull(output, "output"));
        return this;
    }

    /**
     * Simple function to match <a href="https://en.wikipedia.org/wiki/Wildcard_DNS_record">DNS wildcard</a>.
     */
    public static bool matches(string template, string hostName)
    {
        if (template.StartsWith("*."))
        {
            return template.regionMatches(2, hostName, 0, hostName.Length)
                   || StringUtil.commonSuffixOfLength(hostName, template, template.Length - 1);
        }

        return template.Equals(hostName);
    }

    /**
     * IDNA ASCII conversion and case normalization
     */
    public static string normalizeHostname(string hostname)
    {
        if (needsNormalization(hostname))
        {
            hostname = IDN.toASCII(hostname, IDN.ALLOW_UNASSIGNED);
        }

        return hostname.toLowerCase(Locale.US);
    }

    private static bool needsNormalization(string hostname)
    {
        int length = hostname.Length;
        for (int i = 0; i < length; i++)
        {
            int c = hostname[i];
            if (c > 0x7F)
            {
                return true;
            }
        }

        return false;
    }

    public virtual T map(string hostname)
    {
        if (hostname != null)
        {
            hostname = normalizeHostname(hostname);

            foreach (var entry in _map)
            {
                if (matches(entry.Key, hostname))
                {
                    return entry.Value;
                }
            }
        }

        return _defaultValue;
    }

    /**
     * Returns a read-only {@link Map} of the domain mapping patterns and their associated value objects.
     */
    public virtual IDictionary<string, T> asMap()
    {
        return _unmodifiableMap;
    }

    public override string ToString()
    {
        return StringUtil.simpleClassName(this) + "(default: " + _defaultValue + ", map: " + map + ')';
    }
}