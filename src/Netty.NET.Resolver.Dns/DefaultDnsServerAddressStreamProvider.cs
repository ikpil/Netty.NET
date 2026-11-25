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
using System.Net;
using System.Net.Sockets;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver.Dns;

/**
 * A {@link DnsServerAddressStreamProvider} which will use predefined default DNS servers to use for DNS resolution.
 * These defaults do not respect your host's machines defaults.
 * <p>
 * This may use the JDK's blocking DNS resolution to bootstrap the default DNS server addresses.
 */
// CLR: the static initializer is a static constructor (after the INSTANCE field initializer, as in the source order);
// List<InetSocketAddress> is List<EndPoint>/IReadOnlyList<EndPoint> and SocketUtils.socketAddress is
// DnsServerAddresses.SocketAddress. PlatformDependent, NetUtil and SystemPropertyUtil are Common's;
// NetUtil.LOCALHOST instanceof Inet6Address is its AddressFamily InterNetworkV6 (docs/handler-porting.md Addresses).
// The PlatformDependent.javaVersion() < 9 branch, which reads sun.net.dns.ResolverConfiguration by reflection, is not
// ported: the CLR is not a Java 8 runtime and Common has no javaVersion(); its comments are recorded in the manifest
// (jvm-only-replaced). ExceptionInInitializerError is an InvalidOperationException thrown from the static constructor,
// which reaches callers as TypeInitializationException (docs/codec-compression-porting.md). A List argument of a log
// message prints as the AbstractCollection.toString() text "[a, b]" (docs/handler-porting.md), with each address as
// EndPoint.ToString(). String.split(",") drops trailing empty strings
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), which string.Split keeps, and
// String.trim() is ResolvConf.JavaTrim.
public sealed class DefaultDnsServerAddressStreamProvider : IDnsServerAddressStreamProvider
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(DefaultDnsServerAddressStreamProvider));
    private const string DEFAULT_FALLBACK_SERVER_PROPERTY = "io.netty.resolver.dns.defaultNameServerFallback";
    public static readonly DefaultDnsServerAddressStreamProvider INSTANCE = new DefaultDnsServerAddressStreamProvider();

    private static readonly IReadOnlyList<EndPoint> DEFAULT_NAME_SERVER_LIST;
    private static readonly DnsServerAddresses DEFAULT_NAME_SERVERS;
    internal const int DNS_PORT = 53;

    static DefaultDnsServerAddressStreamProvider()
    {
        List<EndPoint> defaultNameServers = new List<EndPoint>(2);
        if (!PlatformDependent.IsAndroid())
        {
            // Skip this on Android; it has neither /etc/resolv.conf nor JNDI classes.
            // See https://github.com/netty/netty/issues/8654
            if (!PlatformDependent.IsWindows())
            {
                // Try reading /etc/resolv.conf. It's usually found on Linux or macOS, but can also be missing.
                try
                {
                    defaultNameServers.AddRange(ResolvConf.System().GetNameservers());
                }
                catch (InvalidOperationException e)
                {
                    string fallbackMessage = "Failed to get name servers from /etc/resolv.conf; will fall back to JNDI";
                    if (logger.IsDebugEnabled())
                    {
                        // Always log at INFO, but only include stack trace if DEBUG is enabled.
                        logger.Info(fallbackMessage, e);
                    }
                    else
                    {
                        logger.Info(fallbackMessage);
                    }
                    DirContextUtils.AddNameServers(defaultNameServers, DNS_PORT);
                }
            }
            else
            {
                DirContextUtils.AddNameServers(defaultNameServers, DNS_PORT);
            }
        }

        if (defaultNameServers.Count != 0)
        {
            if (logger.IsDebugEnabled())
            {
                logger.Debug(
                        "Default DNS servers: {} (sun.net.dns.ResolverConfiguration)", ListToString(defaultNameServers));
            }
        }
        else
        {
            string defaultNameserverString = SystemPropertyUtil.Get(DEFAULT_FALLBACK_SERVER_PROPERTY, null);
            if (defaultNameserverString != null)
            {
                foreach (string server in Split(defaultNameserverString))
                {
                    string dns = ResolvConf.JavaTrim(server);
                    if (!NetUtil.IsValidIpV4Address(dns) && !NetUtil.IsValidIpV6Address(dns))
                    {
                        throw new InvalidOperationException(DEFAULT_FALLBACK_SERVER_PROPERTY + " doesn't" +
                                " contain a valid list of NameServers: " + defaultNameserverString);
                    }
                    defaultNameServers.Add(DnsServerAddresses.SocketAddress(ResolvConf.JavaTrim(server), DNS_PORT));
                }
                if (defaultNameServers.Count == 0)
                {
                    throw new InvalidOperationException(DEFAULT_FALLBACK_SERVER_PROPERTY + " doesn't" +
                            " contain a valid list of NameServers: " + defaultNameserverString);
                }

                if (logger.IsWarnEnabled())
                {
                    logger.Warn(
                            "Default DNS servers: {} (Configured by {} system property)",
                            ListToString(defaultNameServers), DEFAULT_FALLBACK_SERVER_PROPERTY);
                }
            }
            else
            {
                // Depending if IPv6 or IPv4 is used choose the correct DNS servers provided by google:
                // https://developers.google.com/speed/public-dns/docs/using
                // https://docs.oracle.com/javase/7/docs/api/java/net/doc-files/net-properties.html
                if (NetUtil.IsIpV6AddressesPreferred ||
                        (NetUtil.LOCALHOST.AddressFamily == AddressFamily.InterNetworkV6 && !NetUtil.IsIpV4StackPreferred))
                {
                    defaultNameServers.AddRange(new[]
                    {
                            DnsServerAddresses.SocketAddress("2001:4860:4860::8888", DNS_PORT),
                            DnsServerAddresses.SocketAddress("2001:4860:4860::8844", DNS_PORT)
                    });
                }
                else
                {
                    defaultNameServers.AddRange(new[]
                    {
                            DnsServerAddresses.SocketAddress("8.8.8.8", DNS_PORT),
                            DnsServerAddresses.SocketAddress("8.8.4.4", DNS_PORT)
                    });
                }

                if (logger.IsWarnEnabled())
                {
                    logger.Warn(
                            "Default DNS servers: {} (Google Public DNS as a fallback)", ListToString(defaultNameServers));
                }
            }
        }

        DEFAULT_NAME_SERVER_LIST = defaultNameServers.AsReadOnly();
        DEFAULT_NAME_SERVERS = DnsServerAddresses.Sequential(DEFAULT_NAME_SERVER_LIST);
    }

    private DefaultDnsServerAddressStreamProvider()
    {
    }

    public IDnsServerAddressStream NameServerAddressStream(string hostname)
    {
        return DEFAULT_NAME_SERVERS.Stream();
    }

    /**
     * Returns the list of the system DNS server addresses. If it failed to retrieve the list of the system DNS server
     * addresses from the environment, it will return {@code "8.8.8.8"} and {@code "8.8.4.4"}, the addresses of the
     * Google public DNS servers.
     */
    public static IReadOnlyList<EndPoint> DefaultAddressList()
    {
        return DEFAULT_NAME_SERVER_LIST;
    }

    /**
     * Returns the {@link DnsServerAddresses} that yields the system DNS server addresses sequentially. If it failed to
     * retrieve the list of the system DNS server addresses from the environment, it will use {@code "8.8.8.8"} and
     * {@code "8.8.4.4"}, the addresses of the Google public DNS servers.
     * <p>
     * This method has the same effect with the following code:
     * <pre>
     * DnsServerAddresses.sequential(DnsServerAddresses.defaultAddressList());
     * </pre>
     * </p>
     */
    public static DnsServerAddresses DefaultAddresses()
    {
        return DEFAULT_NAME_SERVERS;
    }

    // CLR: AbstractCollection.toString() of a log argument (see the class comment).
    private static string ListToString(List<EndPoint> list)
    {
        return "[" + string.Join(", ", list) + "]";
    }

    // CLR: String.split(",") (see the class comment).
    private static string[] Split(string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length == 1)
        {
            return parts;
        }
        int size = parts.Length;
        while (size > 0 && parts[size - 1].Length == 0)
        {
            size--;
        }
        Array.Resize(ref parts, size);
        return parts;
    }
}
