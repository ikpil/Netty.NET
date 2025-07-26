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

using System.Collections.Generic;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * Builder for immutable {@link DomainNameMapping} instances.
 *
 * @param <V> concrete type of value objects
 * @deprecated Use {@link DomainWildcardMappingBuilder}
 */
public class DomainNameMappingBuilder<T>
{
    private readonly T _defaultValue;
    private readonly IDictionary<string, T> _map;

    /**
     * Constructor with default initial capacity of the map holding the mappings
     *
     * @param defaultValue the default value for {@link DomainNameMapping#map(string)} to return
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
     * @param defaultValue    the default value for {@link DomainNameMapping#map(string)} to return
     *                        when nothing matches the input
     */
    public DomainNameMappingBuilder(int initialCapacity, T defaultValue)
    {
        _defaultValue = checkNotNull(defaultValue, "defaultValue");
        _map = new LinkedHashMap<string, T>(initialCapacity);
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
     * @param output   the output value that will be returned by {@link DomainNameMapping#map(string)}
     *                 when the specified host name matches the specified input host name
     */
    public DomainNameMappingBuilder<T> add(string hostname, T output)
    {
        _map.Add(checkNotNull(hostname, "hostname"), checkNotNull(output, "output"));
        return this;
    }

    /**
     * Creates a new instance of immutable {@link DomainNameMapping}
     * Attempts to add new mappings to the result object will cause {@link UnsupportedOperationException} to be thrown
     *
     * @return new {@link DomainNameMapping} instance
     */
    public DomainNameMapping<T> build()
    {
        return new ImmutableDomainNameMapping<T>(_defaultValue, _map);
    }
}