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

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Netty.NET.Resolver;

/**
 * A parser for hosts files.
 * The produced mappings contain only the first entry per hostname.
 * Consider using {@link HostsFileEntriesProvider} when mappings with all entries per hostname are needed.
 */
/**
     * Can't be instantiated.
     */
// CLR: a static class cannot be instantiated.
public static class HostsFileParser
{
    /**
     * Parse hosts file at standard OS location using the systems default {@link Charset} for decoding.
     *
     * @return a {@link HostsFileEntries}
     */
    public static HostsFileEntries ParseSilently() => ToEntries(HostsFileEntriesProvider.Parser().ParseSilently());
    /**
     * Parse hosts file at standard OS location using the given {@link Charset}s one after each other until
     * we were able to parse something or none is left.
     *
     * @param charsets the {@link Charset}s to try as file encodings when parsing.
     * @return a {@link HostsFileEntries}
     */
    public static HostsFileEntries ParseSilently(params Encoding[] charsets) => ToEntries(HostsFileEntriesProvider.Parser().ParseSilently(charsets));
    /**
     * Parse hosts file at standard OS location using the system default {@link Charset} for decoding.
     *
     * @return a {@link HostsFileEntries}
     * @throws IOException file could not be read
     */
    public static HostsFileEntries Parse() => ToEntries(HostsFileEntriesProvider.Parser().Parse());
    /**
     * Parse a hosts file using the system default {@link Charset} for decoding.
     *
     * @param file the file to be parsed
     * @return a {@link HostsFileEntries}
     * @throws IOException file could not be read
     */
    public static HostsFileEntries Parse(FileInfo file) => ToEntries(HostsFileEntriesProvider.Parser().Parse(file));
    /**
     * Parse a hosts file.
     *
     * @param file the file to be parsed
     * @param charsets the {@link Charset}s to try as file encodings when parsing.
     * @return a {@link HostsFileEntries}
     * @throws IOException file could not be read
     */
    public static HostsFileEntries Parse(FileInfo file, params Encoding[] charsets) => ToEntries(HostsFileEntriesProvider.Parser().Parse(file, charsets));
    /**
     * Parse a reader of hosts file format.
     *
     * @param reader the file to be parsed
     * @return a {@link HostsFileEntries}
     * @throws IOException file could not be read
     */
    public static HostsFileEntries Parse(TextReader reader) => ToEntries(HostsFileEntriesProvider.Parser().Parse(reader));

    private static HostsFileEntries ToEntries(HostsFileEntriesProvider provider)
        => ReferenceEquals(provider, HostsFileEntriesProvider.Empty) ? HostsFileEntries.Empty :
            new HostsFileEntries(FirstValues(provider.Ipv4Entries()), FirstValues(provider.Ipv6Entries()));

    private static Dictionary<string, IPAddress> FirstValues(IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> entries)
    {
        var result = new Dictionary<string, IPAddress>(entries.Count);
        foreach (var entry in entries)
            if (entry.Value.Count != 0)
                result.Add(entry.Key, entry.Value[0]);
        return result;
    }
}
