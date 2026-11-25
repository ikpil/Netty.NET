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

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Resolver.Dns;

/**
 * A {@link DnsNameResolver} builder.
 */
// CLR: ChannelFactory<? extends DatagramChannel/SocketChannel> is Func<IDatagramChannel>/Func<ISocketChannel> and a
// Class<? extends DatagramChannel/SocketChannel> setter uses Transport's ReflectiveChannelFactory<T>(Type).NewChannel, as
// Bootstrap.Channel(Type) does (docs/transport-porting.md; the Type parameter keeps the
// DynamicallyAccessedMembers(PublicParameterlessConstructor) annotation of that constructor). EventLoop is IEventLoop,
// SocketAddress EndPoint and new InetSocketAddress(0) the wildcard IPEndPoint(IPAddress.Any, 0), as Transport's
// Bootstrap.LocalAddress(int) maps it (NioDatagramChannel binds IPAddress.Any as IPv6Any on a dual-mode socket). Integer
// fields are int? (ObjectUtil.IntValue(int?, int) is intValue). The protected getters of this final class are internal,
// because C# reports a new protected member of a sealed type (CS0628) and Java's protected member is visible in the
// package, where DnsAddressResolverGroup calls them. The enum parameters (ResolvedAddressTypes,
// DnsNameResolverChannelStrategy) cannot be null, so their checkNotNull has no counterpart. A member named
// ResolvedAddressTypes hides the enum type in expressions inside the class, so its constants are written
// Netty.NET.Resolver.ResolvedAddressTypes.X. LinkedHashSet is Common's LinkedHashSet<T> (insertion order, ordinal
// string equality). @Deprecated members whose @deprecated text names a replacement (channelFactory(ChannelFactory),
// channelType(Class), authoritativeDnsServerCache(DnsCache), computeResolvedAddressTypes(InternetProtocolFamily...)
// with its private helper toSocketProtocolFamilies, and traceEnabled(boolean)) are not ported (owner decision
// 2026-11-16); their comments are recorded in the manifest. The traceEnabled field therefore keeps its default (false)
// and copy() copies it directly. The @Deprecated channelFactory() getter names no replacement and is ported with
// [Obsolete]. IllegalStateException/IllegalArgumentException are InvalidOperationException/ArgumentException.
public sealed class DnsNameResolverBuilder
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DnsNameResolverBuilder));
    internal static readonly EndPoint DEFAULT_LOCAL_ADDRESS = new IPEndPoint(IPAddress.Any, 0);

    internal volatile IEventLoop eventLoop;
    private Func<IDatagramChannel> datagramChannelFactory;
    private Func<ISocketChannel> socketChannelFactory;
    private bool retryOnTimeout;

    private IDnsCache resolveCache;
    private IDnsCnameCache cnameCache;
    private IAuthoritativeDnsServerCache authoritativeDnsServerCache;
    private EndPoint localAddress = DEFAULT_LOCAL_ADDRESS;
    private int? minTtl;
    private int? maxTtl;
    private int? negativeTtl;
    private long queryTimeoutMillis = -1;
    private ResolvedAddressTypes resolvedAddressTypes = DnsNameResolver.DEFAULT_RESOLVE_ADDRESS_TYPES;
    private bool completeOncePreferredResolved;
    private bool recursionDesired = true;
    private int maxQueriesPerResolve = -1;
    private bool traceEnabled;
    private int maxPayloadSize = 4096;
    private bool optResourceEnabled = true;
    private IHostsFileEntriesResolver hostsFileEntriesResolver = IHostsFileEntriesResolver.Default;
    private IDnsServerAddressStreamProvider dnsServerAddressStreamProvider =
            DnsServerAddressStreamProviders.PlatformDefault();
    private IDnsServerAddressStream queryDnsServerAddressStream;
    private IDnsQueryLifecycleObserverFactory dnsQueryLifecycleObserverFactory =
            NoopDnsQueryLifecycleObserverFactory.INSTANCE;
    private string[] searchDomains;
    private int ndots = -1;
    private bool decodeIdn = true;

    private int maxNumConsolidation;
    private DnsNameResolverChannelStrategy datagramChannelStrategy = DnsNameResolverChannelStrategy.ChannelPerResolver;

    /**
     * Creates a new builder.
     */
    public DnsNameResolverBuilder()
    {
    }

    /**
     * Creates a new builder.
     *
     * @param eventLoop the {@link EventLoop} which will perform the communication with the DNS
     * servers.
     */
    public DnsNameResolverBuilder(IEventLoop eventLoop)
    {
        EventLoop(eventLoop);
    }

    /**
     * Sets the {@link EventLoop} which will perform the communication with the DNS servers.
     *
     * @param eventLoop the {@link EventLoop}
     * @return {@code this}
     */
    public DnsNameResolverBuilder EventLoop(IEventLoop eventLoop)
    {
        this.eventLoop = eventLoop;
        return this;
    }

    // CLR: @Deprecated without a @deprecated text (no replacement named): ported with [Obsolete] (owner decision
    // 2026-11-16); protected in the final class is internal (see the class comment).
    [Obsolete]
    internal Func<IDatagramChannel> ChannelFactory()
    {
        return this.datagramChannelFactory;
    }

    internal Func<IDatagramChannel> DatagramChannelFactory()
    {
        return this.datagramChannelFactory;
    }

    /**
     * Sets the {@link ChannelFactory} that will create a {@link DatagramChannel}.
     * <p>
     * If <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a> should be supported as well it is required
     * to call the {@link #socketChannelFactory(ChannelFactory)} or {@link #socketChannelType(Class)} method.
     *
     * @param datagramChannelFactory the {@link ChannelFactory}
     * @return {@code this}
     */
    public DnsNameResolverBuilder DatagramChannelFactory(
            Func<IDatagramChannel> datagramChannelFactory)
    {
        this.datagramChannelFactory = datagramChannelFactory;
        return this;
    }

    /**
     * Sets the {@link ChannelFactory} as a {@link ReflectiveChannelFactory} of this type.
     * Use as an alternative to {@link #datagramChannelFactory(ChannelFactory)}.
     * <p>
     * If <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a> should be supported as well it is required
     * to call the {@link #socketChannelFactory(ChannelFactory)} or {@link #socketChannelType(Class)} method.
     *
     * @param channelType the type
     * @return {@code this}
     */
    public DnsNameResolverBuilder DatagramChannelType(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type channelType)
    {
        return DatagramChannelFactory(new ReflectiveChannelFactory<IDatagramChannel>(channelType).NewChannel);
    }

    /**
     * Sets the {@link ChannelFactory} that will create a {@link SocketChannel} for
     * <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a> if needed.
     * <p>
     * TCP fallback is <strong>not</strong> enabled by default and must be enabled by providing a non-null
     * {@link ChannelFactory} for this method.
     *
     * @param channelFactory the {@link ChannelFactory} or {@code null}
     *                       if <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a> should not be supported.
     *                       By default, TCP fallback is not enabled.
     * @return {@code this}
     */
    public DnsNameResolverBuilder SocketChannelFactory(Func<ISocketChannel> channelFactory)
    {
        return SocketChannelFactory(channelFactory, false);
    }

    /**
     * Sets the {@link ChannelFactory} as a {@link ReflectiveChannelFactory} of this type for
     * <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a> if needed.
     * Use as an alternative to {@link #socketChannelFactory(ChannelFactory)}.
     * <p>
     * TCP fallback is <strong>not</strong> enabled by default and must be enabled by providing a non-null
     * {@code channelType} for this method.
     *
     * @param channelType the type or {@code null} if <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a>
     *                    should not be supported. By default, TCP fallback is not enabled.
     * @return {@code this}
     */
    public DnsNameResolverBuilder SocketChannelType(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type channelType)
    {
        return SocketChannelType(channelType, false);
    }

    /**
     * Sets the {@link ChannelFactory} that will create a {@link SocketChannel} for
     * <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a> if needed.
     * <p>
     * TCP fallback is <strong>not</strong> enabled by default and must be enabled by providing a non-null
     * {@link ChannelFactory} for this method.
     *
     * @param channelFactory the {@link ChannelFactory} or {@code null}
     *                       if <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a> should not be supported.
     *                       By default, TCP fallback is not enabled.
     * @param retryOnTimeout if {@code true} the {@link DnsNameResolver} will also fallback to TCP if a timeout
     *                       was detected, if {@code false} it will only try to use TCP if the response was marked
     *                       as truncated.
     * @return {@code this}
     */
    public DnsNameResolverBuilder SocketChannelFactory(
            Func<ISocketChannel> channelFactory, bool retryOnTimeout)
    {
        this.socketChannelFactory = channelFactory;
        this.retryOnTimeout = retryOnTimeout;
        return this;
    }

    /**
     * Sets the {@link ChannelFactory} as a {@link ReflectiveChannelFactory} of this type for
     * <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a> if needed.
     * Use as an alternative to {@link #socketChannelFactory(ChannelFactory)}.
     * <p>
     * TCP fallback is <strong>not</strong> enabled by default and must be enabled by providing a non-null
     * {@code channelType} for this method.
     *
     * @param channelType the type or {@code null} if <a href="https://tools.ietf.org/html/rfc7766">TCP fallback</a>
     *                    should not be supported. By default, TCP fallback is not enabled.
     * @param retryOnTimeout if {@code true} the {@link DnsNameResolver} will also fallback to TCP if a timeout
     *                       was detected, if {@code false} it will only try to use TCP if the response was marked
     *                       as truncated.
     * @return {@code this}
     */
    public DnsNameResolverBuilder SocketChannelType(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type channelType,
            bool retryOnTimeout)
    {
        if (channelType == null)
        {
            return SocketChannelFactory(null, retryOnTimeout);
        }
        return SocketChannelFactory(new ReflectiveChannelFactory<ISocketChannel>(channelType).NewChannel, retryOnTimeout);
    }

    /**
     * Sets the cache for resolution results.
     *
     * @param resolveCache the DNS resolution results cache
     * @return {@code this}
     */
    public DnsNameResolverBuilder ResolveCache(IDnsCache resolveCache)
    {
        this.resolveCache  = resolveCache;
        return this;
    }

    /**
     * Sets the cache for {@code CNAME} mappings.
     *
     * @param cnameCache the cache used to cache {@code CNAME} mappings for a domain.
     * @return {@code this}
     */
    public DnsNameResolverBuilder CnameCache(IDnsCnameCache cnameCache)
    {
        this.cnameCache  = cnameCache;
        return this;
    }

    /**
     * Set the factory used to generate objects which can observe individual DNS queries.
     * @param lifecycleObserverFactory the factory used to generate objects which can observe individual DNS queries.
     * @return {@code this}
     */
    public DnsNameResolverBuilder DnsQueryLifecycleObserverFactory(IDnsQueryLifecycleObserverFactory
                                                                           lifecycleObserverFactory)
    {
        this.dnsQueryLifecycleObserverFactory =
                ObjectUtil.CheckNotNull(lifecycleObserverFactory, "lifecycleObserverFactory");
        return this;
    }

    /**
     * Sets the cache for authoritative NS servers
     *
     * @param authoritativeDnsServerCache the authoritative NS servers cache
     * @return {@code this}
     */
    public DnsNameResolverBuilder AuthoritativeDnsServerCache(IAuthoritativeDnsServerCache authoritativeDnsServerCache)
    {
        this.authoritativeDnsServerCache = authoritativeDnsServerCache;
        return this;
    }

    /**
     * Configure the address that will be used to bind too. If {@code null} the default will be used.
     * @param localAddress the bind address
     * @return {@code this}
     */
    public DnsNameResolverBuilder LocalAddress(EndPoint localAddress)
    {
        this.localAddress = localAddress;
        return this;
    }

    /**
     * Sets the minimum and maximum TTL of the cached DNS resource records (in seconds). If the TTL of the DNS
     * resource record returned by the DNS server is less than the minimum TTL or greater than the maximum TTL,
     * this resolver will ignore the TTL from the DNS server and use the minimum TTL or the maximum TTL instead
     * respectively.
     * The default value is {@code 0} and {@link Integer#MAX_VALUE}, which practically tells this resolver to
     * respect the TTL from the DNS server.
     *
     * @param minTtl the minimum TTL
     * @param maxTtl the maximum TTL
     * @return {@code this}
     */
    public DnsNameResolverBuilder Ttl(int minTtl, int maxTtl)
    {
        this.maxTtl = maxTtl;
        this.minTtl = minTtl;
        return this;
    }

    /**
     * Sets the TTL of the cache for the failed DNS queries (in seconds).
     *
     * @param negativeTtl the TTL for failed cached queries
     * @return {@code this}
     */
    public DnsNameResolverBuilder NegativeTtl(int negativeTtl)
    {
        this.negativeTtl = negativeTtl;
        return this;
    }

    /**
     * Sets the timeout of each DNS query performed by this resolver (in milliseconds).
     * {@code 0} disables the timeout. If not set or a negative number is set, the default timeout is used.
     *
     * @param queryTimeoutMillis the query timeout
     * @return {@code this}
     */
    public DnsNameResolverBuilder QueryTimeoutMillis(long queryTimeoutMillis)
    {
        this.queryTimeoutMillis = queryTimeoutMillis;
        return this;
    }

    /**
     * Compute a {@link ResolvedAddressTypes} from some {@link SocketProtocolFamily}s.
     * An empty input will return the default value, based on "java.net" System properties.
     * Valid inputs are (), (IPv4), (IPv6), (Ipv4, IPv6) and (IPv6, IPv4).
     * @param socketProtocolFamilies a valid sequence of {@link SocketProtocolFamily}s
     * @return a {@link ResolvedAddressTypes}
     */
    public static ResolvedAddressTypes ToResolvedAddressTypes(params SocketProtocolFamily[] socketProtocolFamilies)
    {
        if (socketProtocolFamilies == null || socketProtocolFamilies.Length == 0)
        {
            return DnsNameResolver.DEFAULT_RESOLVE_ADDRESS_TYPES;
        }
        if (socketProtocolFamilies.Length > 2)
        {
            throw new ArgumentException("No more than 2 socketProtocolFamilies");
        }

        switch(socketProtocolFamilies[0])
        {
            case SocketProtocolFamily.Inet:
                return (socketProtocolFamilies.Length >= 2
                        && socketProtocolFamilies[1] == SocketProtocolFamily.Inet6) ?
                        Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Preferred :
                        Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Only;
            case SocketProtocolFamily.Inet6:
                return (socketProtocolFamilies.Length >= 2
                        && socketProtocolFamilies[1] == SocketProtocolFamily.Inet) ?
                        Netty.NET.Resolver.ResolvedAddressTypes.Ipv6Preferred :
                        Netty.NET.Resolver.ResolvedAddressTypes.Ipv6Only;
            default:
                throw new ArgumentException(
                        "Couldn't resolve ResolvedAddressTypes from InternetProtocolFamily array");
        }
    }

    /**
     * Sets the list of the protocol families of the address resolved.
     * You can use {@link DnsNameResolverBuilder#computeResolvedAddressTypes(InternetProtocolFamily...)}
     * to get a {@link ResolvedAddressTypes} out of some {@link InternetProtocolFamily}s.
     *
     * @param resolvedAddressTypes the address types
     * @return {@code this}
     */
    public DnsNameResolverBuilder ResolvedAddressTypes(ResolvedAddressTypes resolvedAddressTypes)
    {
        this.resolvedAddressTypes = resolvedAddressTypes;
        return this;
    }

    /**
     * If {@code true} {@link DnsNameResolver#resolveAll(String)} will notify the returned {@link Future} as
     * soon as all queries for the preferred address-type are complete.
     *
     * @param completeOncePreferredResolved {@code true} to enable, {@code false} to disable.
     * @return {@code this}
     */
    public DnsNameResolverBuilder CompleteOncePreferredResolved(bool completeOncePreferredResolved)
    {
        this.completeOncePreferredResolved = completeOncePreferredResolved;
        return this;
    }

    /**
     * Sets if this resolver has to send a DNS query with the RD (recursion desired) flag set.
     *
     * @param recursionDesired true if recursion is desired
     * @return {@code this}
     */
    public DnsNameResolverBuilder RecursionDesired(bool recursionDesired)
    {
        this.recursionDesired = recursionDesired;
        return this;
    }

    /**
     * Sets the maximum allowed number of DNS queries to send when resolving a host name.
     *
     * @param maxQueriesPerResolve the max number of queries
     * @return {@code this}
     */
    public DnsNameResolverBuilder MaxQueriesPerResolve(int maxQueriesPerResolve)
    {
        this.maxQueriesPerResolve = maxQueriesPerResolve;
        return this;
    }

    /**
     * Sets the capacity of the datagram packet buffer (in bytes).  The default value is {@code 4096} bytes.
     *
     * @param maxPayloadSize the capacity of the datagram packet buffer
     * @return {@code this}
     */
    public DnsNameResolverBuilder MaxPayloadSize(int maxPayloadSize)
    {
        this.maxPayloadSize = maxPayloadSize;
        return this;
    }

    /**
     * Enable the automatic inclusion of a optional records that tries to give the remote DNS server a hint about
     * how much data the resolver can read per response. Some DNSServer may not support this and so fail to answer
     * queries. If you find problems you may want to disable this.
     *
     * @param optResourceEnabled if optional records inclusion is enabled
     * @return {@code this}
     */
    public DnsNameResolverBuilder OptResourceEnabled(bool optResourceEnabled)
    {
        this.optResourceEnabled = optResourceEnabled;
        return this;
    }

    /**
     * @param hostsFileEntriesResolver the {@link HostsFileEntriesResolver} used to first check
     *                                 if the hostname is locally aliased.
     * @return {@code this}
     */
    public DnsNameResolverBuilder HostsFileEntriesResolver(IHostsFileEntriesResolver hostsFileEntriesResolver)
    {
        this.hostsFileEntriesResolver = hostsFileEntriesResolver;
        return this;
    }

    internal IDnsServerAddressStreamProvider NameServerProvider()
    {
        return this.dnsServerAddressStreamProvider;
    }

    /**
     * Set the {@link DnsServerAddressStreamProvider} which is used to determine which DNS server is used to resolve
     * each hostname.
     * @return {@code this}.
     */
    public DnsNameResolverBuilder NameServerProvider(IDnsServerAddressStreamProvider dnsServerAddressStreamProvider)
    {
        this.dnsServerAddressStreamProvider =
                ObjectUtil.CheckNotNull(dnsServerAddressStreamProvider, "dnsServerAddressStreamProvider");
        return this;
    }

    internal IDnsServerAddressStream QueryServerAddressStream()
    {
        return this.queryDnsServerAddressStream;
    }

    /**
     * Set the {@link DnsServerAddressStream} which provides the server address for DNS queries.
     * @return {@code this}.
     */
    public DnsNameResolverBuilder QueryServerAddressStream(IDnsServerAddressStream queryServerAddressStream)
    {
        this.queryDnsServerAddressStream =
                ObjectUtil.CheckNotNull(queryServerAddressStream, "queryServerAddressStream");
        return this;
    }

    /**
     * Set the list of search domains of the resolver.
     *
     * @param searchDomains the search domains
     * @return {@code this}
     */
    // CLR: Iterable<String> is IEnumerable<string>; toArray(EMPTY_STRINGS) is a copy of the set in insertion order.
    public DnsNameResolverBuilder SearchDomains(IEnumerable<string> searchDomains)
    {
        ObjectUtil.CheckNotNull(searchDomains, "searchDomains");

        LinkedHashSet<string> domains = new LinkedHashSet<string>(4, StringComparer.Ordinal);

        foreach (string f in searchDomains)
        {
            if (f == null)
            {
                break;
            }

            // Avoid duplicate entries.
            domains.Add(f);
        }

        this.searchDomains = domains.ToArray();
        return this;
    }

  /**
   * Set the number of dots which must appear in a name before an initial absolute query is made.
   * The default value is {@code 1}.
   *
   * @param ndots the ndots value
   * @return {@code this}
   */
    public DnsNameResolverBuilder Ndots(int ndots)
    {
        this.ndots = ndots;
        return this;
    }

   internal IDnsCache GetOrNewCache()
   {
        if (this.resolveCache != null)
        {
            return this.resolveCache;
        }
        return new DefaultDnsCache(ObjectUtil.IntValue(minTtl, 0), ObjectUtil.IntValue(maxTtl, int.MaxValue),
                ObjectUtil.IntValue(negativeTtl, 0));
    }

   internal IAuthoritativeDnsServerCache GetOrNewAuthoritativeDnsServerCache()
   {
        if (this.authoritativeDnsServerCache != null)
        {
            return this.authoritativeDnsServerCache;
        }
        return new DefaultAuthoritativeDnsServerCache(
                ObjectUtil.IntValue(minTtl, 0), ObjectUtil.IntValue(maxTtl, int.MaxValue),
                // Let us use the sane ordering as DnsNameResolver will be used when returning
                // nameservers from the cache.
                new NameServerComparator(DnsNameResolver.AddressType(
                        DnsNameResolver.PreferredAddressType(resolvedAddressTypes)).Value));
    }

    private IDnsServerAddressStream NewQueryServerAddressStream(
            IDnsServerAddressStreamProvider dnsServerAddressStreamProvider)
    {
        return new ThreadLocalNameServerAddressStream(dnsServerAddressStreamProvider);
    }

   internal IDnsCnameCache GetOrNewCnameCache()
   {
        if (this.cnameCache != null)
        {
            return this.cnameCache;
        }
        return new DefaultDnsCnameCache(
                ObjectUtil.IntValue(minTtl, 0), ObjectUtil.IntValue(maxTtl, int.MaxValue));
    }

    /**
     * Set if domain / host names should be decoded to unicode when received.
     * See <a href="https://tools.ietf.org/html/rfc3492">rfc3492</a>.
     *
     * @param decodeIdn if should get decoded
     * @return {@code this}
     */
    public DnsNameResolverBuilder DecodeIdn(bool decodeIdn)
    {
        this.decodeIdn = decodeIdn;
        return this;
    }

    /**
     * Set the maximum size of the cache that is used to consolidate lookups for different hostnames when in-flight.
     * This means if multiple lookups are done for the same hostname and still in-flight only one actual query will
     * be made and the result will be cascaded to the others.
     *
     * @param maxNumConsolidation the maximum lookups to consolidate (different hostnames), or {@code 0} if
     *                            no consolidation should be performed.
     * @return {@code this}
     */
    public DnsNameResolverBuilder ConsolidateCacheSize(int maxNumConsolidation)
    {
        this.maxNumConsolidation = ObjectUtil.CheckPositiveOrZero(maxNumConsolidation, "maxNumConsolidation");
        return this;
    }

    /**
     * Set the strategy that is used to determine how a {@link DatagramChannel} is used by the resolver for sending
     * queries over UDP protocol.
     *
     * @param datagramChannelStrategy  the {@link DnsNameResolverChannelStrategy} to use when doing queries over
     *                                 UDP protocol.
     * @return {@code this}
     */
    public DnsNameResolverBuilder DatagramChannelStrategy(DnsNameResolverChannelStrategy datagramChannelStrategy)
    {
        this.datagramChannelStrategy = datagramChannelStrategy;
        return this;
    }

    /**
     * Returns a new {@link DnsNameResolver} instance.
     *
     * @return a {@link DnsNameResolver}
     */
    public DnsNameResolver Build()
    {
        if (eventLoop == null)
        {
            throw new InvalidOperationException("eventLoop should be specified to build a DnsNameResolver.");
        }

        // CLR: the locals declared below hide the fields in the whole block (CS0844), so the fields are this.x here.
        if (this.resolveCache != null && (minTtl != null || maxTtl != null || negativeTtl != null))
        {
            logger.Debug("resolveCache and TTLs are mutually exclusive. TTLs are ignored.");
        }

        if (this.cnameCache != null && (minTtl != null || maxTtl != null || negativeTtl != null))
        {
            logger.Debug("cnameCache and TTLs are mutually exclusive. TTLs are ignored.");
        }

        if (this.authoritativeDnsServerCache != null && (minTtl != null || maxTtl != null || negativeTtl != null))
        {
            logger.Debug("authoritativeDnsServerCache and TTLs are mutually exclusive. TTLs are ignored.");
        }

        IDnsCache resolveCache = GetOrNewCache();
        IDnsCnameCache cnameCache = GetOrNewCnameCache();
        IAuthoritativeDnsServerCache authoritativeDnsServerCache = GetOrNewAuthoritativeDnsServerCache();

        IDnsServerAddressStream queryDnsServerAddressStream = this.queryDnsServerAddressStream != null ?
                this.queryDnsServerAddressStream : NewQueryServerAddressStream(dnsServerAddressStreamProvider);

        return new DnsNameResolver(
                eventLoop,
                datagramChannelFactory,
                socketChannelFactory,
                retryOnTimeout,
                resolveCache,
                cnameCache,
                authoritativeDnsServerCache,
                localAddress,
                dnsQueryLifecycleObserverFactory,
                queryTimeoutMillis,
                resolvedAddressTypes,
                recursionDesired,
                maxQueriesPerResolve,
                traceEnabled,
                maxPayloadSize,
                optResourceEnabled,
                hostsFileEntriesResolver,
                dnsServerAddressStreamProvider,
                queryDnsServerAddressStream,
                searchDomains,
                ndots,
                decodeIdn,
                completeOncePreferredResolved,
                maxNumConsolidation,
                datagramChannelStrategy);
    }

    /**
     * Creates a copy of this {@link DnsNameResolverBuilder}
     *
     * @return {@link DnsNameResolverBuilder}
     */
    public DnsNameResolverBuilder Copy()
    {
        DnsNameResolverBuilder copiedBuilder = new DnsNameResolverBuilder();

        if (eventLoop != null)
        {
            copiedBuilder.EventLoop(eventLoop);
        }

        if (datagramChannelFactory != null)
        {
            copiedBuilder.DatagramChannelFactory(datagramChannelFactory);
        }

        copiedBuilder.SocketChannelFactory(socketChannelFactory, retryOnTimeout);

        if (resolveCache != null)
        {
            copiedBuilder.ResolveCache(resolveCache);
        }

        if (cnameCache != null)
        {
            copiedBuilder.CnameCache(cnameCache);
        }
        if (maxTtl != null && minTtl != null)
        {
            copiedBuilder.Ttl(minTtl.Value, maxTtl.Value);
        }

        if (negativeTtl != null)
        {
            copiedBuilder.NegativeTtl(negativeTtl.Value);
        }

        if (authoritativeDnsServerCache != null)
        {
            copiedBuilder.AuthoritativeDnsServerCache(authoritativeDnsServerCache);
        }

        if (dnsQueryLifecycleObserverFactory != null)
        {
            copiedBuilder.DnsQueryLifecycleObserverFactory(dnsQueryLifecycleObserverFactory);
        }

        copiedBuilder.QueryTimeoutMillis(queryTimeoutMillis);
        copiedBuilder.ResolvedAddressTypes(resolvedAddressTypes);
        copiedBuilder.RecursionDesired(recursionDesired);
        copiedBuilder.MaxQueriesPerResolve(maxQueriesPerResolve);
        // CLR: traceEnabled(boolean) is not ported (see the class comment); the field is copied directly.
        copiedBuilder.traceEnabled = traceEnabled;
        copiedBuilder.MaxPayloadSize(maxPayloadSize);
        copiedBuilder.OptResourceEnabled(optResourceEnabled);
        copiedBuilder.HostsFileEntriesResolver(hostsFileEntriesResolver);

        if (dnsServerAddressStreamProvider != null)
        {
            copiedBuilder.NameServerProvider(dnsServerAddressStreamProvider);
        }

        if (queryDnsServerAddressStream != null)
        {
            copiedBuilder.QueryServerAddressStream(queryDnsServerAddressStream);
        }

        if (searchDomains != null)
        {
            copiedBuilder.SearchDomains(searchDomains);
        }

        copiedBuilder.Ndots(ndots);
        copiedBuilder.DecodeIdn(decodeIdn);
        copiedBuilder.CompleteOncePreferredResolved(completeOncePreferredResolved);
        copiedBuilder.LocalAddress(localAddress);
        copiedBuilder.ConsolidateCacheSize(maxNumConsolidation);
        copiedBuilder.DatagramChannelStrategy(datagramChannelStrategy);
        return copiedBuilder;
    }
}
