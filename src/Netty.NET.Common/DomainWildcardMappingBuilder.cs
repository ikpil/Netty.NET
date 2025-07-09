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
namespace Netty.NET.Common;






/**
 * Builder that allows to build {@link Mapping}s that support
 * <a href="https://tools.ietf.org/search/rfc6125#section-6.4">DNS wildcard</a> matching.
 * @param <V> the type of the value that we map to.
 */
public class DomainWildcardMappingBuilder<V> {

    private readonly V defaultValue;
    private readonly Map<string, V> map;

    /**
     * Constructor with default initial capacity of the map holding the mappings
     *
     * @param defaultValue the default value for {@link Mapping#map(object)} )} to return
     *                     when nothing matches the input
     */
    public DomainWildcardMappingBuilder(V defaultValue) {
        this(4, defaultValue);
    }

    /**
     * Constructor with initial capacity of the map holding the mappings
     *
     * @param initialCapacity initial capacity for the internal map
     * @param defaultValue    the default value for {@link Mapping#map(object)} to return
     *                        when nothing matches the input
     */
    public DomainWildcardMappingBuilder(int initialCapacity, V defaultValue) {
        this.defaultValue = checkNotNull(defaultValue, "defaultValue");
        map = new LinkedHashMap<string, V>(initialCapacity);
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
    public DomainWildcardMappingBuilder<V> add(string hostname, V output) {
        map.put(normalizeHostName(hostname),
                checkNotNull(output, "output"));
        return this;
    }

    private string normalizeHostName(string hostname) {
        checkNotNull(hostname, "hostname");
        if (hostname.isEmpty() || hostname.charAt(0) == '.') {
            throw new ArgumentException("Hostname '" + hostname + "' not valid");
        }
        hostname = ImmutableDomainWildcardMapping.normalize(checkNotNull(hostname, "hostname"));
        if (hostname.charAt(0) == '*') {
            if (hostname.length() < 3 || hostname.charAt(1) != '.') {
                throw new ArgumentException("Wildcard Hostname '" + hostname + "'not valid");
            }
            return hostname.substring(1);
        }
        return hostname;
    }
    /**
     * Creates a new instance of an immutable {@link Mapping}.
     *
     * @return new {@link Mapping} instance
     */
    public Mapping<string, V> build() {
        return new ImmutableDomainWildcardMapping<V>(defaultValue, map);
    }

    private static readonly class ImmutableDomainWildcardMapping<V> : Mapping<string, V> {
        private static readonly string REPR_HEADER = "ImmutableDomainWildcardMapping(default: ";
        private static readonly string REPR_MAP_OPENING = ", map: ";
        private static readonly string REPR_MAP_CLOSING = ")";

        private readonly V defaultValue;
        private readonly Map<string, V> map;

        ImmutableDomainWildcardMapping(V defaultValue, Map<string, V> map) {
            this.defaultValue = defaultValue;
            this.map = new LinkedHashMap<string, V>(map);
        }

        @Override
        public V map(string hostname) {
            if (hostname != null) {
                hostname = normalize(hostname);

                // Let's try an exact match first
                V value = map.get(hostname);
                if (value != null) {
                    return value;
                }

                // No exact match, let's try a wildcard match.
                int idx = hostname.indexOf('.');
                if (idx != -1) {
                    value = map.get(hostname.substring(idx));
                    if (value != null) {
                        return value;
                    }
                }
            }

            return defaultValue;
        }

        @SuppressWarnings("deprecation")
        static string normalize(string hostname) {
            return DomainNameMapping.normalizeHostname(hostname);
        }

        @Override
        public string toString() {
            StringBuilder sb = new StringBuilder();
            sb.append(REPR_HEADER).append(defaultValue).append(REPR_MAP_OPENING).append('{');

            for (Map.Entry<string, V> entry : map.entrySet()) {
                string hostname = entry.getKey();
                if (hostname.charAt(0) == '.') {
                    hostname = '*' + hostname;
                }
                sb.append(hostname).append('=').append(entry.getValue()).append(", ");
            }
            sb.setLength(sb.length() - 2);
            return sb.append('}').append(REPR_MAP_CLOSING).toString();
        }
    }
}
