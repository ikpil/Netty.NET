/*
 * Copyright 2016 The Netty Project
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
using System.Collections.ObjectModel;
using System.Text;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * Builder for immutable {@link DomainNameMapping} instances.
 *
 * @param <V> concrete type of value objects
 * @deprecated Use {@link DomainWildcardMappingBuilder}
 */
public class DomainNameMappingBuilder<T> where T : class
{
    private readonly T _defaultValue;
    private readonly OrderedDictionary<string, T> _map;

    /**
     * Constructor with default initial capacity of the map holding the mappings
     *
     * @param defaultValue the default value for {@link DomainNameMapping#map(String)} to return
     *                     when nothing matches the input
     */
    public DomainNameMappingBuilder(T defaultValue)
        : this(4, defaultValue)
    {
    }

    /**
     * Constructor with initial capacity of the map holding the mappings
     *
     * @param initialCapacity initial capacity for the internal map
     * @param defaultValue    the default value for {@link DomainNameMapping#map(String)} to return
     *                        when nothing matches the input
     */
    public DomainNameMappingBuilder(int initialCapacity, T defaultValue)
    {
        _defaultValue = CheckNotNull(defaultValue, "defaultValue");
        _map = new OrderedDictionary<string, T>(initialCapacity, StringComparer.Ordinal);
    }

    /**
     * Adds a mapping that maps the specified (optionally wildcard) host name to the specified output value.
     * Null values are forbidden for both hostnames and values.
     * <p>
     * <a href="https://en.wikipedia.org/wiki/Wildcard_DNS_record">DNS wildcard</a> is supported as hostname.
     * For example, you can use {@code *.netty.io} to match {@code netty.io} and {@code downloads.netty.io}.
     * </p>
     *
     * @param hostname the host name (optionally wildcard)
     * @param output   the output value that will be returned by {@link DomainNameMapping#map(String)}
     *                 when the specified host name matches the specified input host name
     */
    public DomainNameMappingBuilder<T> Add(string hostname, T output)
    {
        _map[CheckNotNull(hostname, "hostname")] = CheckNotNull(output, "output");
        return this;
    }

    /**
     * Creates a new instance of immutable {@link DomainNameMapping}
     * Attempts to add new mappings to the result object will cause {@link UnsupportedOperationException} to be thrown
     *
     * @return new {@link DomainNameMapping} instance
     */
    public DomainNameMapping<T> Build()
    {
        return new ImmutableDomainNameMapping(_defaultValue, _map);
    }


    /**
     * Immutable mapping from domain name pattern to its associated value object.
     * Mapping is represented by two arrays: keys and values. Key domainNamePatterns[i] is associated with values[i].
     *
     * @param <V> concrete type of value objects
     */
    // CLR pairs each normalized pattern with its value; duplicate normalized patterns retain distinct scan positions.
    private sealed class ImmutableDomainNameMapping : DomainNameMapping<T>
    {
        private readonly KeyValuePair<string, T>[] entries;
        private readonly IReadOnlyDictionary<string, T> map;

        internal ImmutableDomainNameMapping(T defaultValue, OrderedDictionary<string, T> mappings)
            : base(null, defaultValue)
        {
            entries = new KeyValuePair<string, T>[mappings.Count];
            var copy = new OrderedDictionary<string, T>(mappings.Count, StringComparer.Ordinal);
            int index = 0;
            foreach (var entry in mappings)
            {
                string hostname = NormalizeHostname(entry.Key);
                entries[index++] = new KeyValuePair<string, T>(hostname, entry.Value);
                copy[hostname] = entry.Value;
            }
            map = new ReadOnlyDictionary<string, T>(copy);
        }

        public override DomainNameMapping<T> Add(string hostname, T output)
        {
            throw new NotSupportedException("Immutable DomainNameMapping does not support modification after initial creation");
        }

        public override T Map(string hostname)
        {
            if (hostname != null)
            {
                hostname = NormalizeHostname(hostname);
                foreach (var entry in entries)
                    if (Matches(entry.Key, hostname)) return entry.Value;
            }
            return _defaultValue;
        }

        public override IReadOnlyDictionary<string, T> AsMap() => map;

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append("ImmutableDomainNameMapping(default: ").Append(_defaultValue).Append(", map: {");
            bool first = true;
            foreach (var entry in entries)
            {
                if (!first) text.Append(", ");
                first = false;
                text.Append(entry.Key).Append('=').Append(entry.Value);
            }
            return text.Append("})").ToString();
        }
    }
}
