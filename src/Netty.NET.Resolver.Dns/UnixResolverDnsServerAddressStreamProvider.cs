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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver.Dns;

/**
 * Able to parse files such as <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a> and
 * <a href="https://developer.apple.com/legacy/library/documentation/Darwin/Reference/ManPages/man5/resolver.5.html">
 * /etc/resolver</a> to respect the system default domain servers.
 */
// CLR: File is FileInfo (as Resolver's HostsFileEntriesProvider maps it): getName() is Name, isFile() is Exists ("false if
// the file does not exist or if the file is a directory",
// https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.exists?view=net-10.0), File.toString() "the pathname string" is FileInfo.ToString(), "the original path that was passed to the
// FileInfo constructor" (https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.tostring?view=net-10.0), and
// new File(dir).listFiles(), which returns null when the path is not a directory or an I/O error occurs
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/File.html), is ListFiles (DirectoryInfo.GetFiles,
// files only; the source skips directories with isFile()). FileReader/BufferedReader are a FileStream and a StreamReader
// (UTF-8, where FileReader uses the platform default charset), closed in the same finally blocks. Map/HashMap are
// Dictionary<string, DnsServerAddresses> (owner decision 2026-11-19): values().iterator().next() takes the first
// inserted value, where HashMap takes the first in hash order; this only matters for a resolv.conf whose domain lines
// map more than one domain and none is the file name. List<InetSocketAddress> is List<EndPoint>; SocketUtils.socketAddress
// is DnsServerAddresses.SocketAddress and isUnresolved() a DnsEndPoint. A scoped IPv6 literal whose scope names no
// interface ("fe80::1%nonexisting") is unresolved in the JDK and filtered below, while Resolver's DefaultNameResolver
// parses it with IPAddress.TryParse into a resolved address. System.getenv is Environment.GetEnvironmentVariable (null
// when unset, https://learn.microsoft.com/en-us/dotnet/api/system.environment.getenvironmentvariable?view=net-10.0).
// Integer.parseInt accepts an optional sign and decimal digits only
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html): it is int.Parse with
// NumberStyles.AllowLeadingSign and the invariant culture, whose FormatException/OverflowException
// (https://learn.microsoft.com/en-us/dotnet/api/system.int32.parse?view=net-10.0) stand for NumberFormatException; as
// NumberFormatException extends IllegalArgumentException
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/NumberFormatException.html), the
// catch (IllegalArgumentException) of parse also catches them. Pattern.split is SplitWhitespace. String.trim() is
// ResolvConf.JavaTrim; startsWith/contains are ordinal. IllegalArgumentException is ArgumentException; Java's checked
// IOException clauses have no counterpart.
public sealed class UnixResolverDnsServerAddressStreamProvider : IDnsServerAddressStreamProvider
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(UnixResolverDnsServerAddressStreamProvider));

    // CLR: Java's \s is [ \t\n\x0B\f\r] (ASCII white space), while the.NET \s also matches Unicode white space, so
    // the class is written out.
    private static readonly Regex WHITESPACE_PATTERN = new Regex("[ \\t\\n\\x0B\\f\\r]+");
    private static readonly string RES_OPTIONS = Environment.GetEnvironmentVariable("RES_OPTIONS");

    private const string ETC_RESOLV_CONF_FILE = "/etc/resolv.conf";
    private const string ETC_RESOLVER_DIR = "/etc/resolver";
    private const string NAMESERVER_ROW_LABEL = "nameserver";
    private const string SORTLIST_ROW_LABEL = "sortlist";
    private const string OPTIONS_ROW_LABEL = "options ";
    private const string OPTIONS_ROTATE_FLAG = "rotate";
    private const string DOMAIN_ROW_LABEL = "domain";
    private const string SEARCH_ROW_LABEL = "search";
    private const string PORT_ROW_LABEL = "port";

    private readonly DnsServerAddresses defaultNameServerAddresses;
    private readonly Dictionary<string, DnsServerAddresses> domainToNameServerStreamMap;

    /**
     * Attempt to parse {@code /etc/resolv.conf} and files in the {@code /etc/resolver} directory by default.
     * A failure to parse will return {@link DefaultDnsServerAddressStreamProvider}.
     */
    internal static IDnsServerAddressStreamProvider ParseSilently()
    {
        try
        {
            UnixResolverDnsServerAddressStreamProvider nameServerCache =
                    new UnixResolverDnsServerAddressStreamProvider(ETC_RESOLV_CONF_FILE, ETC_RESOLVER_DIR);
            return nameServerCache.MayOverrideNameServers() ? nameServerCache
                                                            : DefaultDnsServerAddressStreamProvider.INSTANCE;
        }
        catch (Exception e)
        {
            if (logger.IsDebugEnabled())
            {
                logger.Debug("failed to parse {} and/or {}", ETC_RESOLV_CONF_FILE, ETC_RESOLVER_DIR, e);
            }

            return DefaultDnsServerAddressStreamProvider.INSTANCE;
        }
    }

    /**
     * Parse a file of the format <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a> which may contain
     * the default DNS server to use, and also overrides for individual domains. Also parse list of files of the format
     * <a href="
     * https://developer.apple.com/legacy/library/documentation/Darwin/Reference/ManPages/man5/resolver.5.html">
     * /etc/resolver</a> which may contain multiple files to override the name servers used for multiple domains.
     * @param etcResolvConf <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a>.
     * @param etcResolverFiles List of files of the format defined in
     * <a href="
     * https://developer.apple.com/legacy/library/documentation/Darwin/Reference/ManPages/man5/resolver.5.html">
     * /etc/resolver</a>.
     * @throws IOException If an error occurs while parsing the input files.
     */
    public UnixResolverDnsServerAddressStreamProvider(FileInfo etcResolvConf, params FileInfo[] etcResolverFiles)
    {
        Dictionary<string, DnsServerAddresses> etcResolvConfMap = Parse(ObjectUtil.CheckNotNull(etcResolvConf, "etcResolvConf"));
        bool useEtcResolverFiles = etcResolverFiles != null && etcResolverFiles.Length != 0;
        domainToNameServerStreamMap = useEtcResolverFiles ? Parse(etcResolverFiles) : etcResolvConfMap;

        etcResolvConfMap.TryGetValue(etcResolvConf.Name, out DnsServerAddresses defaultNameServerAddresses);

        if (defaultNameServerAddresses == null)
        {
            ICollection<DnsServerAddresses> values = etcResolvConfMap.Values;
            if (values.Count == 0)
            {
                throw new ArgumentException(etcResolvConf + " didn't provide any name servers");
            }

            using (IEnumerator<DnsServerAddresses> iterator = values.GetEnumerator())
            {
                iterator.MoveNext();
                this.defaultNameServerAddresses = iterator.Current;
            }
        }
        else
        {
            this.defaultNameServerAddresses = defaultNameServerAddresses;
        }

        if (useEtcResolverFiles)
        {
            foreach (KeyValuePair<string, DnsServerAddresses> entry in etcResolvConfMap)
            {
                domainToNameServerStreamMap[entry.Key] = entry.Value;
            }
        }
    }

    /**
     * Parse a file of the format <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a> which may contain
     * the default DNS server to use, and also overrides for individual domains. Also parse a directory of the format
     * <a href="
     * https://developer.apple.com/legacy/library/documentation/Darwin/Reference/ManPages/man5/resolver.5.html">
     * /etc/resolver</a> which may contain multiple files to override the name servers used for multiple domains.
     * @param etcResolvConf <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a>.
     * @param etcResolverDir Directory containing files of the format defined in
     * <a href="
     * https://developer.apple.com/legacy/library/documentation/Darwin/Reference/ManPages/man5/resolver.5.html">
     * /etc/resolver</a>.
     * @throws IOException If an error occurs while parsing the input files.
     */
    public UnixResolverDnsServerAddressStreamProvider(string etcResolvConf, string etcResolverDir) :
        this(etcResolvConf == null ? null : new FileInfo(etcResolvConf),
             etcResolverDir == null ? null : ListFiles(etcResolverDir))
    {
    }

    // CLR: new File(dir).listFiles() (see the class comment).
    private static FileInfo[] ListFiles(string dir)
    {
        try
        {
            DirectoryInfo directory = new DirectoryInfo(dir);
            return directory.Exists ? directory.GetFiles() : null;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IDnsServerAddressStream NameServerAddressStream(string hostname)
    {
        for (;;)
        {
            // CLR: String.indexOf(ch, 1) returns -1 when 1 is past the end, while String.IndexOf(char, int) throws for
            // a start index greater than the length, so the empty host name is checked first.
            int i = hostname.Length == 0 ? -1 : hostname.IndexOf('.', 1);
            if (i < 0 || i == hostname.Length - 1)
            {
                return defaultNameServerAddresses.Stream();
            }

            domainToNameServerStreamMap.TryGetValue(hostname, out DnsServerAddresses addresses);
            if (addresses != null)
            {
                return addresses.Stream();
            }

            hostname = hostname.Substring(i + 1);
        }
    }

    private bool MayOverrideNameServers()
    {
        return domainToNameServerStreamMap.Count != 0 || defaultNameServerAddresses.Stream().Next() != null;
    }

    private static Dictionary<string, DnsServerAddresses> Parse(params FileInfo[] etcResolverFiles)
    {
        Dictionary<string, DnsServerAddresses> domainToNameServerStreamMap =
                new Dictionary<string, DnsServerAddresses>(etcResolverFiles.Length << 1);
        bool rotateGlobal = RES_OPTIONS != null && RES_OPTIONS.Contains(OPTIONS_ROTATE_FLAG);

        foreach (FileInfo etcResolverFile in etcResolverFiles)
        {
            if (!etcResolverFile.Exists)
            {
                continue;
            }

            FileStream fr = etcResolverFile.OpenRead();
            StreamReader br = null;

            try
            {
                br = new StreamReader(fr);
                List<EndPoint> addresses = new List<EndPoint>(2);
                string domainName = etcResolverFile.Name;
                bool rotate = rotateGlobal;
                int port = DefaultDnsServerAddressStreamProvider.DNS_PORT;
                string line;

                while ((line = br.ReadLine()) != null)
                {
                    line = ResolvConf.JavaTrim(line);

                    try
                    {
                        char c;
                        if (line.Length == 0 || (c = line[0]) == '#' || c == ';')
                        {
                            continue;
                        }

                        if (!rotate && line.StartsWith(OPTIONS_ROW_LABEL, StringComparison.Ordinal))
                        {
                            rotate = line.Contains(OPTIONS_ROTATE_FLAG);
                        }
                        else if (line.StartsWith(NAMESERVER_ROW_LABEL, StringComparison.Ordinal))
                        {
                            int i = StringUtil.IndexOfNonWhiteSpace(line, NAMESERVER_ROW_LABEL.Length);
                            if (i < 0)
                            {
                                throw new ArgumentException("error parsing label " + NAMESERVER_ROW_LABEL +
                                        " in file " + etcResolverFile + ". value: " + line);
                            }

                            string maybeIP;
                            int x = StringUtil.IndexOfWhiteSpace(line, i);
                            if (x == -1)
                            {
                                maybeIP = line.Substring(i);
                            }
                            else
                            {
                                // ignore comments
                                int idx = StringUtil.IndexOfNonWhiteSpace(line, x);
                                if (idx == -1 || line[idx] != '#')
                                {
                                    throw new ArgumentException("error parsing label " + NAMESERVER_ROW_LABEL +
                                            " in file " + etcResolverFile + ". value: " + line);
                                }

                                maybeIP = line.Substring(i, x - i);
                            }

                            // There may be a port appended onto the IP address so we attempt to extract it.
                            if (!NetUtil.IsValidIpV4Address(maybeIP) && !NetUtil.IsValidIpV6Address(maybeIP))
                            {
                                i = maybeIP.LastIndexOf('.');
                                if (i + 1 >= maybeIP.Length)
                                {
                                    throw new ArgumentException("error parsing label " + NAMESERVER_ROW_LABEL +
                                            " in file " + etcResolverFile + ". invalid IP value: " + line);
                                }

                                port = ParseInt(maybeIP.Substring(i + 1));
                                maybeIP = maybeIP.Substring(0, i);
                            }

                            EndPoint addr = DnsServerAddresses.SocketAddress(maybeIP, port);
                            // Check if the address is resolved and only if this is the case use it. Otherwise just
                            // ignore it. This is needed to filter out invalid entries, as if for example an ipv6
                            // address is used with a scope that represent a network interface that does not exists
                            // on the host.
                            if (!(addr is DnsEndPoint))
                            {
                                addresses.Add(addr);
                            }
                        }
                        else if (line.StartsWith(DOMAIN_ROW_LABEL, StringComparison.Ordinal))
                        {
                            int i = StringUtil.IndexOfNonWhiteSpace(line, DOMAIN_ROW_LABEL.Length);
                            if (i < 0)
                            {
                                throw new ArgumentException("error parsing label " + DOMAIN_ROW_LABEL +
                                        " in file " + etcResolverFile + " value: " + line);
                            }

                            domainName = line.Substring(i);

                            if (addresses.Count != 0)
                            {
                                PutIfAbsent(domainToNameServerStreamMap, domainName, addresses, rotate);
                            }

                            addresses = new List<EndPoint>(2);
                        }
                        else if (line.StartsWith(PORT_ROW_LABEL, StringComparison.Ordinal))
                        {
                            int i = StringUtil.IndexOfNonWhiteSpace(line, PORT_ROW_LABEL.Length);
                            if (i < 0)
                            {
                                throw new ArgumentException("error parsing label " + PORT_ROW_LABEL +
                                        " in file " + etcResolverFile + " value: " + line);
                            }

                            port = ParseInt(line.Substring(i));
                        }
                        else if (line.StartsWith(SORTLIST_ROW_LABEL, StringComparison.Ordinal))
                        {
                            logger.Info("row type {} not supported. Ignoring line: {}", SORTLIST_ROW_LABEL, line);
                        }
                    }
                    catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
                    {
                        logger.Warn("Could not parse entry. Ignoring line: {}", line, e);
                    }
                }

                if (addresses.Count != 0)
                {
                    PutIfAbsent(domainToNameServerStreamMap, domainName, addresses, rotate);
                }
            }
            finally
            {
                if (br == null)
                {
                    fr.Dispose();
                }
                else
                {
                    br.Dispose();
                }
            }
        }

        return domainToNameServerStreamMap;
    }

    private static void PutIfAbsent(Dictionary<string, DnsServerAddresses> domainToNameServerStreamMap,
                                    string domainName,
                                    List<EndPoint> addresses,
                                    bool rotate)
    {
        // TODO(scott): sortlist is being ignored.
        DnsServerAddresses addrs = rotate
            ? DnsServerAddresses.Rotational(addresses)
            : DnsServerAddresses.Sequential(addresses);
        PutIfAbsent(domainToNameServerStreamMap, domainName, addrs);
    }

    // CLR: Map.put returns the previous value; TryGetValue reads it before the indexer stores the new one.
    private static void PutIfAbsent(Dictionary<string, DnsServerAddresses> domainToNameServerStreamMap,
                                    string domainName,
                                    DnsServerAddresses addresses)
    {
        domainToNameServerStreamMap.TryGetValue(domainName, out DnsServerAddresses existingAddresses);
        domainToNameServerStreamMap[domainName] = addresses;

        if (existingAddresses != null)
        {
            domainToNameServerStreamMap[domainName] = existingAddresses;

            if (logger.IsDebugEnabled())
            {
                logger.Debug("Domain name {} already maps to addresses {} so new addresses {} will be discarded",
                        domainName, existingAddresses, addresses);
            }
        }
    }

    /**
     * Parse <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a> and return options of interest, namely:
     * timeout, attempts and ndots.
     * @return The options values provided by /etc/resolve.conf.
     * @throws IOException If a failure occurs parsing the file.
     */
    internal static UnixResolverOptions ParseEtcResolverOptions()
    {
        return ParseEtcResolverOptions(new FileInfo(ETC_RESOLV_CONF_FILE));
    }

    /**
     * Parse a file of the format <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a> and return options
     * of interest, namely: timeout, attempts and ndots.
     * @param etcResolvConf a file of the format <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a>.
     * @return The options values provided by /etc/resolve.conf.
     * @throws IOException If a failure occurs parsing the file.
     */
    internal static UnixResolverOptions ParseEtcResolverOptions(FileInfo etcResolvConf)
    {
        UnixResolverOptions.Builder optionsBuilder = UnixResolverOptions.NewBuilder();

        FileStream fr = etcResolvConf.OpenRead();
        StreamReader br = null;

        try
        {
            br = new StreamReader(fr);
            string line;

            while ((line = br.ReadLine()) != null)
            {
                if (line.StartsWith(OPTIONS_ROW_LABEL, StringComparison.Ordinal))
                {
                    ParseResOptions(line.Substring(OPTIONS_ROW_LABEL.Length), optionsBuilder);
                    break;
                }
            }
        }
        finally
        {
            if (br == null)
            {
                fr.Dispose();
            }
            else
            {
                br.Dispose();
            }
        }

        // amend options
        if (RES_OPTIONS != null)
        {
            ParseResOptions(RES_OPTIONS, optionsBuilder);
        }

        return optionsBuilder.Build();
    }

    private static void ParseResOptions(string line, UnixResolverOptions.Builder builder)
    {
        string[] opts = SplitWhitespace(line);

        foreach (string opt in opts)
        {
            try
            {
                if (opt.StartsWith("ndots:", StringComparison.Ordinal))
                {
                    builder.SetNdots(ParseResIntOption(opt, "ndots:"));
                }
                else if (opt.StartsWith("attempts:", StringComparison.Ordinal))
                {
                    builder.SetAttempts(ParseResIntOption(opt, "attempts:"));
                }
                else if (opt.StartsWith("timeout:", StringComparison.Ordinal))
                {
                    builder.SetTimeout(ParseResIntOption(opt, "timeout:"));
                }
            }
            catch (Exception ignore) when (ignore is FormatException || ignore is OverflowException)
            {
                // skip bad int values from resolv.conf to keep value already set in UnixResolverOptions
            }
        }
    }

    private static int ParseResIntOption(string opt, string fullLabel)
    {
        string optValue = opt.Substring(fullLabel.Length);
        return ParseInt(optValue);
    }

    /**
     * Parse a file of the format <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a> and return the
     * list of search domains found in it or an empty list if not found.
     * @return List of search domains.
     * @throws IOException If a failure occurs parsing the file.
     */
    internal static IReadOnlyList<string> ParseEtcResolverSearchDomains()
    {
        return ParseEtcResolverSearchDomains(new FileInfo(ETC_RESOLV_CONF_FILE));
    }

    /**
     * Parse a file of the format <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a> and return the
     * list of search domains found in it or an empty list if not found.
     * @param etcResolvConf a file of the format <a href="https://linux.die.net/man/5/resolver">/etc/resolv.conf</a>.
     * @return List of search domains.
     * @throws IOException If a failure occurs parsing the file.
     */
    internal static IReadOnlyList<string> ParseEtcResolverSearchDomains(FileInfo etcResolvConf)
    {
        string localDomain = null;
        List<string> searchDomains = new List<string>();

        FileStream fr = etcResolvConf.OpenRead();
        StreamReader br = null;

        try
        {
            br = new StreamReader(fr);
            string line;

            while ((line = br.ReadLine()) != null)
            {
                if (localDomain == null && line.StartsWith(DOMAIN_ROW_LABEL, StringComparison.Ordinal))
                {
                    int i = StringUtil.IndexOfNonWhiteSpace(line, DOMAIN_ROW_LABEL.Length);
                    if (i >= 0)
                    {
                        localDomain = line.Substring(i);
                    }
                }
                else if (line.StartsWith(SEARCH_ROW_LABEL, StringComparison.Ordinal))
                {
                    int i = StringUtil.IndexOfNonWhiteSpace(line, SEARCH_ROW_LABEL.Length);
                    if (i >= 0)
                    {
                        // May contain more then one entry, either separated by whitespace or tab.
                        // See https://linux.die.net/man/5/resolver
                        string[] domains = SplitWhitespace(line.Substring(i));
                        searchDomains.AddRange(domains);
                    }
                }
            }
        }
        finally
        {
            if (br == null)
            {
                fr.Dispose();
            }
            else
            {
                br.Dispose();
            }
        }

        // return what was on the 'domain' line only if there were no 'search' lines
        return localDomain != null && searchDomains.Count == 0
                ? new[] { localDomain }
                : searchDomains;
    }

    // CLR: Integer.parseInt (see the class comment).
    private static int ParseInt(string s)
    {
        return int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    // CLR: WHITESPACE_PATTERN.split(input): without a match the result is the input itself, and otherwise "trailing
    // empty strings are therefore not included in the resulting array"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), while Regex.Split keeps them.
    private static string[] SplitWhitespace(string input)
    {
        if (!WHITESPACE_PATTERN.IsMatch(input))
        {
            return new[] { input };
        }

        string[] parts = WHITESPACE_PATTERN.Split(input);
        int size = parts.Length;

        while (size > 0 && parts[size - 1].Length == 0)
        {
            size--;
        }

        if (size == parts.Length)
        {
            return parts;
        }

        string[] result = new string[size];
        Array.Copy(parts, result, size);
        return result;
    }

}
