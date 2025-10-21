/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Common.Collections;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * Builder that allows to build {@link Mapping}s that support
 * <a href="https://tools.ietf.org/search/rfc6125#section-6.4">DNS wildcard</a> matching.
 * @param <V> the type of the value that we map to.
 */
public class DomainWildcardMappingBuilder<T> where T : class
{
    private readonly T defaultValue;
    private readonly IDictionary<string, T> map;

    /**
     * Constructor with default initial capacity of the map holding the mappings
     *
     * @param defaultValue the default value for {@link Mapping#map(object)} )} to return
     *                     when nothing matches the input
     */
    public DomainWildcardMappingBuilder(T defaultValue)
        : this(4, defaultValue)
    {
    }

    /**
     * Constructor with initial capacity of the map holding the mappings
     *
     * @param initialCapacity initial capacity for the internal map
     * @param defaultValue    the default value for {@link Mapping#map(object)} to return
     *                        when nothing matches the input
     */
    public DomainWildcardMappingBuilder(int initialCapacity, T defaultValue)
    {
        this.defaultValue = checkNotNull(defaultValue, "defaultValue");
        map = new LinkedHashMap<string, T>(initialCapacity);
    }

    /**
     * Adds a mapping that maps the specified (optionally wildcard) host name to the specified output value.
     * {@code null} values are forbidden for both hostnames and values.
     * <p>
     * <a href="https://tools.ietf.org/search/rfc6125#section-6.4">DNS wildcard</a> is supported as hostname. The
     * wildcard will only match one sub-domain deep and only when wildcard is used as the most-left label.
     *
     * For example:
     *
     * <p>
     *  *.netty.io will match xyz.netty.io but NOT abc.xyz.netty.io
     * </p>
     *
     * @param hostname the host name (optionally wildcard)
     * @param output   the output value that will be returned by {@link Mapping#map(object)}
     *                 when the specified host name matches the specified input host name
     */
    public DomainWildcardMappingBuilder<T> add(string hostname, T output)
    {
        map.Add(normalizeHostName(hostname),
            checkNotNull(output, "output"));
        return this;
    }

    private string normalizeHostName(string hostname)
    {
        checkNotNull(hostname, "hostname");
        if (string.IsNullOrEmpty(hostname) || hostname[0] == '.')
        {
            throw new ArgumentException("Hostname '" + hostname + "' not valid");
        }

        hostname = ImmutableDomainWildcardMapping<T>.normalize(checkNotNull(hostname, "hostname"));
        if (hostname[0] == '*')
        {
            if (hostname.Length < 3 || hostname[1] != '.')
            {
                throw new ArgumentException("Wildcard Hostname '" + hostname + "'not valid");
            }

            return hostname.Substring(1);
        }

        return hostname;
    }

    /**
     * Creates a new instance of an immutable {@link Mapping}.
     *
     * @return new {@link Mapping} instance
     */
    public IMapping<string, T> build()
    {
        return new ImmutableDomainWildcardMapping<T>(defaultValue, map);
    }
}