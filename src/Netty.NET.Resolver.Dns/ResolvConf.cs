/*
 * Copyright 2024 The Netty Project
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
using System.IO;
using System.Net;
using Netty.NET.Common;

namespace Netty.NET.Resolver.Dns;

/**
 * Looks up the {@code nameserver}s from the {@code /etc/resolv.conf} file, intended for Linux and macOS.
 */
// CLR: BufferedReader is TextReader (ReadLine returns null at the end, as readLine does); new InputStreamReader over a
// FileInputStream is a StreamReader over File.OpenRead, which decodes UTF-8
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.streamreader.-ctor?view=net-10.0) where the JDK reader uses the
// platform default charset; BoundedInputStream is Common's BoundedInputStream. new InetSocketAddress(ln, 53) is
// DnsServerAddresses.SocketAddress (an IPEndPoint, or a DnsEndPoint when the name does not resolve) and
// List<InetSocketAddress> a read-only IReadOnlyList<EndPoint>. String.trim() is JavaTrim. system() is System(); a member
// named System hides the System namespace inside this class, so the body names no System.* type in full.
// IllegalStateException is InvalidOperationException.
internal sealed class ResolvConf
{
    private readonly IReadOnlyList<EndPoint> nameservers;

    /**
     * Reads from the given reader and extracts the {@code nameserver}s using the syntax of the
     * {@code /etc/resolv.conf} file, see {@code man resolv.conf}.
     *
     * @param reader contents of {@code resolv.conf} are read from this {@link BufferedReader},
     *              up to the caller to close it
     */
    internal static ResolvConf FromReader(TextReader reader)
    {
        return new ResolvConf(reader);
    }

    /**
     * Reads the given file and extracts the {@code nameserver}s using the syntax of the
     * {@code /etc/resolv.conf} file, see {@code man resolv.conf}.
     */
    internal static ResolvConf FromFile(string file)
    {
        // Use 1 MB to be a bit conservative
        using (TextReader reader = new StreamReader(
                new BoundedInputStream(File.OpenRead(file), 1024 * 1024)))
        {
            return FromReader(reader);
        }
    }

    /**
     * Returns the {@code nameserver}s from the {@code /etc/resolv.conf} file. The file is only read once
     * during the lifetime of this class.
     */
    internal static ResolvConf System()
    {
        ResolvConf resolvConv = ResolvConfLazy.machineResolvConf;
        if (resolvConv != null)
        {
            return resolvConv;
        }
        throw new InvalidOperationException("/etc/resolv.conf could not be read");
    }

    private ResolvConf(TextReader reader)
    {
        List<EndPoint> nameservers = new List<EndPoint>();
        string ln;
        while ((ln = reader.ReadLine()) != null)
        {
            ln = JavaTrim(ln);
            if (ln.Length == 0)
            {
                continue;
            }

            if (ln.StartsWith("nameserver", StringComparison.Ordinal))
            {
                ln = ln.Substring("nameserver".Length);
                int cIndex = ln.IndexOf('#');
                if (cIndex != -1)
                {
                    ln = ln.Substring(0, cIndex);
                }
                ln = JavaTrim(ln);
                if (ln.Length == 0)
                {
                    continue;
                }
                nameservers.Add(DnsServerAddresses.SocketAddress(ln, 53));
            }
        }
        this.nameservers = nameservers.AsReadOnly();
    }

    internal IReadOnlyList<EndPoint> GetNameservers()
    {
        return nameservers;
    }

    // CLR: String.trim() removes the characters whose code point is at most U+0020
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), while String.Trim() removes
    // Unicode white space (https://learn.microsoft.com/en-us/dotnet/api/system.string.trim?view=net-10.0); Trim(char[])
    // with U+0000..U+0020 keeps the Java result, as Resolver's HostsFileEntriesProvider does. Shared with
    // UnixResolverDnsServerAddressStreamProvider and DefaultDnsServerAddressStreamProvider.
    private static readonly char[] JAVA_TRIM_CHARS = CreateJavaTrimChars();

    private static char[] CreateJavaTrimChars()
    {
        char[] chars = new char[0x21];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char) i;
        }
        return chars;
    }

    internal static string JavaTrim(string value)
    {
        return value.Trim(JAVA_TRIM_CHARS);
    }

    // CLR: the static initializer is a static constructor. catch (IOException | SecurityException) is IOException and
    // UnauthorizedAccessException, which File.OpenRead throws when the "caller does not have the required permission"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.io.file.openread?view=net-10.0); its DirectoryNotFoundException
    // and FileNotFoundException are IOExceptions.
    private static class ResolvConfLazy
    {
        internal static readonly ResolvConf machineResolvConf;

        static ResolvConfLazy()
        {
            ResolvConf resolvConf;
            try
            {
                resolvConf = ResolvConf.FromFile("/etc/resolv.conf");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                resolvConf = null;
            }
            machineResolvConf = resolvConf;
        }
    }
}
