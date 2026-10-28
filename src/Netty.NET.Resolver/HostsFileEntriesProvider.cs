/*
 * Copyright 2021 The Netty Project
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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Netty.NET.Common;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver;

/**
 * A container of hosts file entries
 */
public sealed class HostsFileEntriesProvider
{
    public interface IParser
    {
        /**
         * Parses the hosts file at standard OS location using the system default {@link Charset} for decoding.
         *
         * @return a new {@link HostsFileEntriesProvider}
         * @throws IOException file could not be read
         */
        HostsFileEntriesProvider Parse();
        /**
         * Parses the hosts file at standard OS location using the given {@link Charset}s one after another until
         * parse something or none is left.
         *
         * @param charsets the {@link Charset}s to try as file encodings when parsing
         * @return a new {@link HostsFileEntriesProvider}
         * @throws IOException file could not be read
         */
        HostsFileEntriesProvider Parse(params Encoding[] charsets);
        /**
         * Parses the provided hosts file using the given {@link Charset}s one after another until
         * parse something or none is left. In case {@link Charset}s are not provided,
         * the system default {@link Charset} is used for decoding.
         *
         * @param file the file to be parsed
         * @param charsets the {@link Charset}s to try as file encodings when parsing, in case {@link Charset}s
         * are not provided, the system default {@link Charset} is used for decoding
         * @return a new {@link HostsFileEntriesProvider}
         * @throws IOException file could not be read
         */
        HostsFileEntriesProvider Parse(FileInfo file, params Encoding[] charsets);
        /**
         * Performs the parsing operation using the provided reader of hosts file format.
         *
         * @param reader the reader of hosts file format
         * @return a new {@link HostsFileEntriesProvider}
         */
        HostsFileEntriesProvider Parse(TextReader reader);
        /**
         * Parses the hosts file at standard OS location using the system default {@link Charset} for decoding.
         *
         * @return a new {@link HostsFileEntriesProvider}
         */
        HostsFileEntriesProvider ParseSilently();
        /**
         * Parses the hosts file at standard OS location using the given {@link Charset}s one after another until
         * parse something or none is left.
         *
         * @param charsets the {@link Charset}s to try as file encodings when parsing
         * @return a new {@link HostsFileEntriesProvider}
         */
        HostsFileEntriesProvider ParseSilently(params Encoding[] charsets);
        /**
         * Parses the provided hosts file using the given {@link Charset}s one after another until
         * parse something or none is left. In case {@link Charset}s are not provided,
         * the system default {@link Charset} is used for decoding.
         *
         * @param file the file to be parsed
         * @param charsets the {@link Charset}s to try as file encodings when parsing, in case {@link Charset}s
         * are not provided, the system default {@link Charset} is used for decoding
         * @return a new {@link HostsFileEntriesProvider}
         */
        HostsFileEntriesProvider ParseSilently(FileInfo file, params Encoding[] charsets);
    }

    /**
     * Creates a parser for {@link HostsFileEntriesProvider}.
     *
     * @return a new {@link HostsFileEntriesProvider.Parser}
     */
    public static IParser Parser() => ParserImpl.Instance;

    public static readonly HostsFileEntriesProvider Empty = new(
        new Dictionary<string, IReadOnlyList<IPAddress>>(), new Dictionary<string, IReadOnlyList<IPAddress>>());

    private readonly IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> _ipv4Entries;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> _ipv6Entries;

    internal HostsFileEntriesProvider(IDictionary<string, IReadOnlyList<IPAddress>> ipv4Entries,
        IDictionary<string, IReadOnlyList<IPAddress>> ipv6Entries)
    {
        _ipv4Entries = new ReadOnlyDictionary<string, IReadOnlyList<IPAddress>>(new Dictionary<string, IReadOnlyList<IPAddress>>(ipv4Entries));
        _ipv6Entries = new ReadOnlyDictionary<string, IReadOnlyList<IPAddress>>(new Dictionary<string, IReadOnlyList<IPAddress>>(ipv6Entries));
    }

    /**
     * The IPv4 entries.
     *
     * @return the IPv4 entries
     */
    public IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> Ipv4Entries() => _ipv4Entries;
    /**
     * The IPv6 entries.
     *
     * @return the IPv6 entries
     */
    public IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> Ipv6Entries() => _ipv6Entries;

    private sealed class ParserImpl : IParser
    {
        private static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance(typeof(IParser));
        private static readonly Regex Whitespaces = new("[ \t]+", RegexOptions.CultureInvariant);
        internal static readonly ParserImpl Instance = new();

        private ParserImpl()
        {
            // singleton
        }

        public HostsFileEntriesProvider Parse() => Parse(LocateHostsFile(), Encoding.Default);
        public HostsFileEntriesProvider Parse(params Encoding[] charsets) => Parse(LocateHostsFile(), charsets);

        public HostsFileEntriesProvider Parse(FileInfo file, params Encoding[] charsets)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(charsets);
            if (charsets.Length == 0)
                charsets = new[] { Encoding.Default };
            if (file.Exists)
            {
                foreach (var charset in charsets)
                {
                    ArgumentNullException.ThrowIfNull(charset);
                    // The caller supplies each encoding in order. Do not let BOM autodetection
                    // turn a failed first encoding into an implicit successful later attempt.
                    using var reader = new StreamReader(file.OpenRead(), charset, detectEncodingFromByteOrderMarks: false);
                    // Java UTF_16 consumes a matching BOM. Keep UTF-16 first-line entries intact
                    // without letting a different requested encoding silently change byte order.
                    if ((charset.CodePage == Encoding.Unicode.CodePage || charset.CodePage == Encoding.BigEndianUnicode.CodePage)
                        && reader.Peek() == '\uFEFF')
                        reader.Read();
                    var entries = Parse(reader);
                    if (!ReferenceEquals(entries, Empty))
                        return entries;
                }
            }
            return Empty;
        }

        public HostsFileEntriesProvider Parse(TextReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            using (reader)
            {
                var ipv4Entries = new Dictionary<string, IReadOnlyList<IPAddress>>();
                var ipv6Entries = new Dictionary<string, IReadOnlyList<IPAddress>>();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    // remove comment
                    int commentPosition = line.IndexOf('#');
                    if (commentPosition != -1)
                        line = line[..commentPosition];
                    // skip empty lines
                    // CLR: Java trim removes code units <= U+0020, not all Unicode whitespace.
                    int start = 0, end = line.Length;
                    while (start < end && line[start] <= ' ')
                        start++;
                    while (end > start && line[end - 1] <= ' ')
                        end--;
                    line = line[start..end];
                    if (line.Length == 0)
                        continue;

                    // split
                    var lineParts = Whitespaces.Split(line);
                    // a valid line should be [IP, hostname, alias*]
                    if (lineParts.Length < 2)
                    {
                        // skip invalid line
                        continue;
                    }
                    byte[] ipBytes = NetUtil.CreateByteArrayFromIpAddressString(lineParts[0]);
                    if (ipBytes == null)
                    {
                        // skip invalid IP
                        continue;
                    }
                    var address = new IPAddress(ipBytes);
                    // InetAddress.getByAddress collapses IPv4-mapped IPv6 literals to Inet4Address.
                    if (address.IsIPv4MappedToIPv6)
                        address = address.MapToIPv4();
                    // loop over hostname and aliases
                    for (int i = 1; i < lineParts.Length; i++)
                    {
                        string hostnameLower = lineParts[i].ToLowerInvariant();
                        var entries = address.AddressFamily == AddressFamily.InterNetwork ? ipv4Entries : ipv6Entries;
                        if (!entries.TryGetValue(hostnameLower, out var addresses))
                        {
                            addresses = new List<IPAddress>();
                            entries.Add(hostnameLower, addresses);
                        }
                        ((List<IPAddress>)addresses).Add(address);
                    }
                }
                return ipv4Entries.Count == 0 && ipv6Entries.Count == 0 ? Empty : new HostsFileEntriesProvider(ipv4Entries, ipv6Entries);
            }
        }

        public HostsFileEntriesProvider ParseSilently() => ParseSilently(LocateHostsFile(), Encoding.Default);
        public HostsFileEntriesProvider ParseSilently(params Encoding[] charsets) => ParseSilently(LocateHostsFile(), charsets);
        public HostsFileEntriesProvider ParseSilently(FileInfo file, params Encoding[] charsets)
        {
            try
            { return Parse(file, charsets); }
            catch (IOException error)
            {
                Logger.Warn("Failed to load and parse hosts file at " + file.FullName, error);
                return Empty;
            }
            // CLR file access denial is UnauthorizedAccessException, not Java IOException.
            catch (UnauthorizedAccessException error)
            {
                Logger.Warn("Failed to load and parse hosts file at " + file.FullName, error);
                return Empty;
            }
        }

        private static FileInfo LocateHostsFile()
        {
            if (!OperatingSystem.IsWindows())
                return new FileInfo("/etc/hosts");
            var path = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "system32", "drivers", "etc", "hosts");
            return new FileInfo(File.Exists(path) ? path : @"C:\Windows\system32\drivers\etc\hosts");
        }
    }
}
