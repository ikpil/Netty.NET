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
    public static IParser Parser()
    {
        return ParserImpl.Instance;
    }

    internal static readonly HostsFileEntriesProvider Empty = new(
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
    public IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> Ipv4Entries()
    {
        return _ipv4Entries;
    }

    /**
     * The IPv6 entries.
     *
     * @return the IPv6 entries
     */
    public IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> Ipv6Entries()
    {
        return _ipv6Entries;
    }

    // CLR: Java StandardCharsets.UTF_16 decoding has no Encoding counterpart: a leading BOM selects the byte order
    // and is consumed, and input without a BOM is big-endian. Encoding.Unicode is little-endian only, so this
    // instance is recognized by identity and decoded explicitly.
    internal static readonly Encoding UTF_16 = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);

    private sealed class ParserImpl : IParser
    {
        private const string WINDOWS_DEFAULT_SYSTEM_ROOT = "C:\\Windows";
        private const string WINDOWS_HOSTS_FILE_RELATIVE_PATH = "\\system32\\drivers\\etc\\hosts";
        private const string X_PLATFORMS_HOSTS_FILE_PATH = "/etc/hosts";

        private static readonly Regex WHITESPACES = new("[ \t]+", RegexOptions.CultureInvariant);
        private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(IParser));
        internal static readonly ParserImpl Instance = new();

        private ParserImpl()
        {
            // singleton
        }

        public HostsFileEntriesProvider Parse()
        {
            return Parse(LocateHostsFile(), Encoding.Default);
        }

        public HostsFileEntriesProvider Parse(params Encoding[] charsets)
        {
            return Parse(LocateHostsFile(), charsets);
        }

        public HostsFileEntriesProvider Parse(FileInfo file, params Encoding[] charsets)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(charsets);

            if (charsets.Length == 0)
                charsets = new[] { Encoding.Default };

            // CLR: FileInfo caches Exists; refresh so each call checks the file system like File.exists().
            // Exists is false for a directory, as file.exists() && file.isFile() requires.
            file.Refresh();

            if (file.Exists)
            {
                foreach (Encoding charset in charsets)
                {
                    ArgumentNullException.ThrowIfNull(charset);

                    // CLR: decode the whole file like InputStreamReader (see Decode for the BOM rules).
                    using (var reader = new StringReader(Decode(File.ReadAllBytes(file.FullName), charset)))
                    {
                        HostsFileEntriesProvider entries = Parse(reader);
                        if (!ReferenceEquals(entries, Empty))
                        {
                            return entries;
                        }
                    }
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
                    var lineParts = WHITESPACES.Split(line);
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

                    // loop over hostname and aliases
                    for (int i = 1; i < lineParts.Length; i++)
                    {
                        string hostname = lineParts[i];
                        string hostnameLower = hostname.ToLowerInvariant();
                        // CLR: getByAddress(hostname, ipBytes) creates one address per name. IPAddress is mutable
                        // (ScopeId), so names never share an instance. An IPv4-mapped literal becomes IPv4 as
                        // InetAddress.getByAddress does.
                        var address = new IPAddress(ipBytes);
                        if (address.IsIPv4MappedToIPv6)
                        {
                            address = address.MapToIPv4();
                        }

                        Dictionary<string, IReadOnlyList<IPAddress>> entries =
                            address.AddressFamily == AddressFamily.InterNetwork ? ipv4Entries : ipv6Entries;
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

        public HostsFileEntriesProvider ParseSilently()
        {
            return ParseSilently(LocateHostsFile(), Encoding.Default);
        }

        public HostsFileEntriesProvider ParseSilently(params Encoding[] charsets)
        {
            return ParseSilently(LocateHostsFile(), charsets);
        }

        public HostsFileEntriesProvider ParseSilently(FileInfo file, params Encoding[] charsets)
        {
            try
            {
                return Parse(file, charsets);
            }
            catch (IOException e)
            {
                logger.Warn("Failed to load and parse hosts file at " + file.FullName, e);
                return Empty;
            }
            // CLR file access denial is UnauthorizedAccessException, not Java IOException.
            catch (UnauthorizedAccessException e)
            {
                logger.Warn("Failed to load and parse hosts file at " + file.FullName, e);
                return Empty;
            }
        }

        private static FileInfo LocateHostsFile()
        {
            FileInfo hostsFile;

            if (OperatingSystem.IsWindows())
            {
                // CLR: as System.getenv("SystemRoot") + path, a missing variable yields the "null" path segment.
                hostsFile = new FileInfo((Environment.GetEnvironmentVariable("SystemRoot") ?? "null") + WINDOWS_HOSTS_FILE_RELATIVE_PATH);
                if (!hostsFile.Exists)
                {
                    hostsFile = new FileInfo(WINDOWS_DEFAULT_SYSTEM_ROOT + WINDOWS_HOSTS_FILE_RELATIVE_PATH);
                }
            }
            else
            {
                hostsFile = new FileInfo(X_PLATFORMS_HOSTS_FILE_PATH);
            }

            return hostsFile;
        }

        private static string Decode(byte[] bytes, Encoding charset)
        {
            if (ReferenceEquals(charset, UTF_16))
            {
                if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                {
                    return new UnicodeEncoding(bigEndian: false, byteOrderMark: false).GetString(bytes, 2, bytes.Length - 2);
                }
                if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                {
                    return new UnicodeEncoding(bigEndian: true, byteOrderMark: false).GetString(bytes, 2, bytes.Length - 2);
                }

                return new UnicodeEncoding(bigEndian: true, byteOrderMark: false).GetString(bytes);
            }

            // An Encoding that declares a preamble consumes it, as Java's BOM-consuming UTF-16 charsets do. A
            // preamble-less Encoding keeps a leading BOM as U+FEFF, as Java UTF_8, UTF_16LE and UTF_16BE do.
            ReadOnlySpan<byte> preamble = charset.Preamble;
            if (preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble))
            {
                return charset.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
            }

            return charset.GetString(bytes);
        }
    }
}
