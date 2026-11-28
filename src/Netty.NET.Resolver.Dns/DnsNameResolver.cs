/*
 * Copyright 2014 The Netty Project
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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Resolver.Dns;

/**
 * A DNS-based {@link InetNameResolver}.
 */
// CLR: Cancellation completes the resolver's internal promise. Listeners run on the resolver event loop so they
// observe the same ordering as the resolution state machine.
// A failed transport setup task carries no channel, so the diagnostic can only include the failure.
public class DnsNameResolver : InetNameResolver
{
    /**
     * An attribute used to mark all channels created by the {@link DnsNameResolver}.
     */
    public static readonly AttributeKey<object> DNS_PIPELINE_ATTRIBUTE =
            AttributeKey<object>.NewInstance("io.netty.resolver.dns.pipeline");

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DnsNameResolver));
    private const string LOCALHOST = "localhost";
    private static readonly string DOT_LOCALHOST = '.' + LOCALHOST;
    private static readonly string WINDOWS_HOST_NAME;
    private static readonly IDnsRecord[] EMPTY_ADDITIONALS = new IDnsRecord[0];
    private static readonly DnsRecordType[] IPV4_ONLY_RESOLVED_RECORD_TYPES =
            {DnsRecordType.A};
    private static readonly SocketProtocolFamily[] IPV4_ONLY_RESOLVED_PROTOCOL_FAMILIES =
            {SocketProtocolFamily.Inet};
    private static readonly DnsRecordType[] IPV4_PREFERRED_RESOLVED_RECORD_TYPES =
            {DnsRecordType.A, DnsRecordType.AAAA};
    private static readonly SocketProtocolFamily[] IPV4_PREFERRED_RESOLVED_PROTOCOL_FAMILIES =
            {SocketProtocolFamily.Inet, SocketProtocolFamily.Inet6};
    private static readonly DnsRecordType[] IPV6_ONLY_RESOLVED_RECORD_TYPES =
            {DnsRecordType.AAAA};
    private static readonly SocketProtocolFamily[] IPV6_ONLY_RESOLVED_PROTOCOL_FAMILIES =
            {SocketProtocolFamily.Inet6};
    private static readonly DnsRecordType[] IPV6_PREFERRED_RESOLVED_RECORD_TYPES =
            {DnsRecordType.AAAA, DnsRecordType.A};
    private static readonly SocketProtocolFamily[] IPV6_PREFERRED_RESOLVED_PROTOCOL_FAMILIES =
            {SocketProtocolFamily.Inet6, SocketProtocolFamily.Inet};

    private static readonly IChannelHandler NOOP_HANDLER = new NoopHandler();

    private sealed class NoopHandler : ChannelHandlerAdapter
    {
        public override bool IsSharable => true;
    }

    /**
     * System property to control RFC 6761 localhost resolution.
     * <p>
     * When {@code true} (the default), {@code localhost} and {@code *.localhost}
     * are resolved to the loopback address without querying DNS servers. When
     * {@code false}, only the pre-#16749 rules apply (Windows {@code localhost}
     * and the Windows machine hostname). Set to {@code false} when
     * {@code *.localhost} hostnames must resolve via DNS to non-loopback
     * addresses.
     *
     * @see <a href="https://github.com/netty/netty/issues/16744">Issue 16744</a>
     * @see <a href="https://www.rfc-editor.org/rfc/rfc6761.html#section-6.3">RFC 6761</a>
     */
    private const string RESOLVE_LOCALHOST_WITHOUT_DNS_PROPERTY =
            "io.netty.resolver.dns.resolveLocalhostWithoutDns";

    /** The runtime value of {@link #RESOLVE_LOCALHOST_WITHOUT_DNS_PROPERTY}. */
    // CLR: @VisibleForTesting package-private static field: internal (the tests of the package set it).
    internal static bool resolveLocalhostWithoutDns;

    internal static readonly ResolvedAddressTypes DEFAULT_RESOLVE_ADDRESS_TYPES;
    internal static readonly string[] DEFAULT_SEARCH_DOMAINS;
    private static readonly UnixResolverOptions DEFAULT_OPTIONS;

    // CLR: the static initializer is the static constructor; the static field initializers (including
    // DATAGRAM_DECODER, DATAGRAM_ENCODER and RELEASE_LISTENER below) run before it, which no initializer depends
    // on. The Windows host name of InetAddress.getLocalHost().getHostName() ("retrieving the name of the host from
    // the system", and getHostName() returns the name the InetAddress "was created with",
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetAddress.html) is
    // Dns.GetHostName(), which "Gets the host name of the local computer"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.dns.gethostname?view=net-10.0). On Windows the
    // search domains come from getSearchDomainsHack(), whose reflective sun.net.dns.ResolverConfiguration branch
    // only runs on Java 8 (javaVersion() < 9) and is not ported, so it returns an empty list as it does on Java 9
    // and later (see GetSearchDomainsHack). Arrays.toString(array) is "equal to... Arrays.asList(a).toString()"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html), the "[a, b]" text of
    // ArraysToString.
    static DnsNameResolver()
    {
        if (NetUtil.IsIpV4StackPreferred || !AnyInterfaceSupportsIpV6())
        {
            DEFAULT_RESOLVE_ADDRESS_TYPES = Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Only;
        }
        else
        {
            if (NetUtil.IsIpV6AddressesPreferred)
            {
                DEFAULT_RESOLVE_ADDRESS_TYPES = Netty.NET.Resolver.ResolvedAddressTypes.Ipv6Preferred;
            }
            else
            {
                DEFAULT_RESOLVE_ADDRESS_TYPES = Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Preferred;
            }
        }

        logger.Debug("Default ResolvedAddressTypes: {}", DEFAULT_RESOLVE_ADDRESS_TYPES);

        string hostName;

        try
        {
            hostName = PlatformDependent.IsWindows() ? System.Net.Dns.GetHostName() : null;
        }
        catch (Exception)
        {
            hostName = null;
        }

        WINDOWS_HOST_NAME = hostName;
        logger.Debug("Windows hostname: {}", WINDOWS_HOST_NAME);

        string[] searchDomains;

        try
        {
            IReadOnlyList<string> list = PlatformDependent.IsWindows()
                    ? GetSearchDomainsHack()
                    : UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverSearchDomains();
            searchDomains = list.ToArray();
        }
        catch (Exception)
        {
            // Failed to get the system name search domain list.
            searchDomains = EmptyArrays.EMPTY_STRINGS;
        }

        DEFAULT_SEARCH_DOMAINS = searchDomains;
        logger.Debug("Default search domains: {}", ArraysToString(DEFAULT_SEARCH_DOMAINS));

        UnixResolverOptions options;

        try
        {
            options = UnixResolverDnsServerAddressStreamProvider.ParseEtcResolverOptions();
        }
        catch (Exception)
        {
            options = UnixResolverOptions.NewBuilder().Build();
        }

        DEFAULT_OPTIONS = options;
        logger.Debug("Default {}", DEFAULT_OPTIONS);

        resolveLocalhostWithoutDns =
                SystemPropertyUtil.GetBoolean(RESOLVE_LOCALHOST_WITHOUT_DNS_PROPERTY, true);

        if (logger.IsDebugEnabled())
        {
            logger.Debug("-D{}: {}", RESOLVE_LOCALHOST_WITHOUT_DNS_PROPERTY, resolveLocalhostWithoutDns);
        }
    }

    /**
     * Returns {@code true} if any {@link NetworkInterface} supports {@code IPv6}, {@code false} otherwise.
     */
    // CLR: NetworkInterface.getInetAddresses() is GetIPProperties().UnicastAddresses, "the unicast addresses assigned
    // to this interface" (https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.ipinterfaceproperties.unicastaddresses?view=net-10.0);
    // instanceof Inet6Address is AddressFamily.InterNetworkV6, isAnyLocalAddress() ("a wildcard address") equality with
    // IPAddress.IPv6Any, "equivalent to 0:0:0:0:0:0:0:0"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.ipv6any?view=net-10.0), isLoopbackAddress()
    // IPAddress.IsLoopback
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.isloopback?view=net-10.0) and
    // isLinkLocalAddress() IsIPv6LinkLocal
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.isipv6linklocal?view=net-10.0).
    private static bool AnyInterfaceSupportsIpV6()
    {
        foreach (NetworkInterface iface in NetUtil.NETWORK_INTERFACES)
        {
            foreach (UnicastIPAddressInformation info in iface.GetIPProperties().UnicastAddresses)
            {
                IPAddress inetAddress = info.Address;
                if (inetAddress.AddressFamily == AddressFamily.InterNetworkV6 && !inetAddress.Equals(IPAddress.IPv6Any) &&
                        !IPAddress.IsLoopback(inetAddress) && !inetAddress.IsIPv6LinkLocal)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // CLR: the PlatformDependent.javaVersion() < 9 branch reads the JDK-internal sun.net.dns.ResolverConfiguration
    // through reflection (Java 8 only); the CLR is not a Java runtime and Common has no javaVersion(), so only the
    // branch for Java 9 and later is kept: an empty list. Its comments are recorded in the manifest (jvm-only-replaced).
    private static IReadOnlyList<string> GetSearchDomainsHack()
    {
        return Array.Empty<string>();
    }

    private static readonly DatagramDnsResponseDecoder DATAGRAM_DECODER = new DatagramDecoder();

    private sealed class DatagramDecoder : DatagramDnsResponseDecoder
    {
        protected override IDnsResponse DecodeResponse(IChannelHandlerContext ctx, DatagramPacket packet)
        {
            IDnsResponse response = base.DecodeResponse(ctx, packet);

            if (packet.Content.IsReadable())
            {
                // If there is still something to read we did stop parsing because of a truncated message.
                // This can happen if we enabled EDNS0 but our MTU is not big enough to handle all the
                // data.
                response.SetTruncated(true);

                if (logger.IsDebugEnabled())
                {
                    logger.Debug("{} RECEIVED: UDP [{}: {}] truncated packet received, consider adjusting "
                                    + "maxPayloadSize for the {}.", ctx.Channel, response.Id(), packet.Sender,
                            StringUtil.SimpleClassName(typeof(DnsNameResolver)));
                }
            }

            return response;
        }
    }

    private static readonly DatagramDnsQueryEncoder DATAGRAM_ENCODER = new DatagramDnsQueryEncoder();

    private static readonly Action<Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>>> RELEASE_LISTENER = future =>
    {
        if (future.IsCompletedSuccessfully)
        {
            IAddressedEnvelope<IDnsResponse, IPEndPoint> result = future.Result;
            ReferenceCountUtil.Release(result);
        }
    };

    // Comparator that ensures we will try first to use the nameservers that use our preferred address type.
    private readonly IComparer<EndPoint> nameServerComparator;
    /**
     * Manages the {@link DnsQueryContext}s in progress and their query IDs.
     */
    private readonly DnsQueryContextManager queryContextManager = new DnsQueryContextManager();

    /**
     * Cache for {@link #doResolve(String, Promise)} and {@link #doResolveAll(String, Promise)}.
     */
    private readonly IDnsCache resolveCache;
    private readonly IAuthoritativeDnsServerCache authoritativeDnsServerCache;
    private readonly IDnsCnameCache cnameCache;
    private readonly IDnsServerAddressStream queryDnsServerAddressStream;

    private readonly long queryTimeoutMillis;
    private readonly int maxQueriesPerResolve;
    private readonly ResolvedAddressTypes resolvedAddressTypes;
    private readonly SocketProtocolFamily[] resolvedInternetProtocolFamilies;
    private readonly bool recursionDesired;
    private readonly int maxPayloadSize;
    private readonly bool optResourceEnabled;
    private readonly IHostsFileEntriesResolver hostsFileEntriesResolver;
    private readonly IDnsServerAddressStreamProvider dnsServerAddressStreamProvider;
    private readonly string[] searchDomains;
    private readonly int ndots;
    private readonly bool supportsAAAARecords;
    private readonly bool supportsARecords;
    private readonly SocketProtocolFamily preferredAddressType;
    private readonly DnsRecordType[] resolveRecordTypes;
    private readonly bool decodeIdn;
    private readonly IDnsQueryLifecycleObserverFactory dnsQueryLifecycleObserverFactory;
    private readonly bool completeOncePreferredResolved;
    private readonly IDnsResolveChannelProvider resolveChannelProvider;
    private readonly Bootstrap socketBootstrap;
    private readonly bool retryWithTcpOnTimeout;

    private readonly int maxNumConsolidation;
    private readonly Dictionary<IDnsQuestion, TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>>>
            inflightLookups;

    // CLR: the scheduler of every listener of this resolver (see the class comment).
    private readonly EventExecutorTaskScheduler listenerScheduler;

    // CLR: The two public constructors are @Deprecated with "Use {@link DnsNameResolverBuilder}" and are not
    // ported; their comments are recorded in the manifest. The ResolvedAddressTypes enum cannot be null, so
    // "resolvedAddressTypes != null ?" has no counterpart. Bootstrap.group(EventLoop) takes the IEventLoop, which
    // is an IEventLoopGroup (Transport); channelFactory(...)/attr(...)/option(...)/handler(...) are the Bootstrap
    // methods of the same names.
    internal DnsNameResolver(
            IEventLoop eventLoop,
            Func<IDatagramChannel> channelFactory,
            Func<ISocketChannel> socketChannelFactory,
            bool retryWithTcpOnTimeout,
            IDnsCache resolveCache,
            IDnsCnameCache cnameCache,
            IAuthoritativeDnsServerCache authoritativeDnsServerCache,
            EndPoint localAddress,
            IDnsQueryLifecycleObserverFactory dnsQueryLifecycleObserverFactory,
            long queryTimeoutMillis,
            ResolvedAddressTypes resolvedAddressTypes,
            bool recursionDesired,
            int maxQueriesPerResolve,
            bool traceEnabled,
            int maxPayloadSize,
            bool optResourceEnabled,
            IHostsFileEntriesResolver hostsFileEntriesResolver,
            IDnsServerAddressStreamProvider dnsServerAddressStreamProvider,
            IDnsServerAddressStream queryDnsServerAddressStream,
            string[] searchDomains,
            int ndots,
            bool decodeIdn,
            bool completeOncePreferredResolved,
            int maxNumConsolidation, DnsNameResolverChannelStrategy datagramChannelStrategy) : base(eventLoop)
    {
        IEventLoop listenerLoop = eventLoop;
        listenerScheduler = new EventExecutorTaskScheduler(() => listenerLoop);
        this.queryTimeoutMillis = queryTimeoutMillis >= 0
            ? queryTimeoutMillis
            : DEFAULT_OPTIONS.Timeout() * 1000L;
        this.resolvedAddressTypes = resolvedAddressTypes;
        this.recursionDesired = recursionDesired;
        this.maxQueriesPerResolve = maxQueriesPerResolve > 0 ? maxQueriesPerResolve : DEFAULT_OPTIONS.Attempts();
        this.maxPayloadSize = ObjectUtil.CheckPositive(maxPayloadSize, "maxPayloadSize");
        this.optResourceEnabled = optResourceEnabled;
        this.hostsFileEntriesResolver = ObjectUtil.CheckNotNull(hostsFileEntriesResolver, "hostsFileEntriesResolver");
        this.dnsServerAddressStreamProvider =
                ObjectUtil.CheckNotNull(dnsServerAddressStreamProvider, "dnsServerAddressStreamProvider");
        this.queryDnsServerAddressStream =
                ObjectUtil.CheckNotNull(queryDnsServerAddressStream, "queryDnsServerAddressStream");
        this.resolveCache = ObjectUtil.CheckNotNull(resolveCache, "resolveCache");
        this.cnameCache = ObjectUtil.CheckNotNull(cnameCache, "cnameCache");
        this.dnsQueryLifecycleObserverFactory = traceEnabled ?
                dnsQueryLifecycleObserverFactory is NoopDnsQueryLifecycleObserverFactory ?
                        new LoggingDnsQueryLifeCycleObserverFactory() :
                        new BiDnsQueryLifecycleObserverFactory(new LoggingDnsQueryLifeCycleObserverFactory(),
                                                               dnsQueryLifecycleObserverFactory) :
                ObjectUtil.CheckNotNull(dnsQueryLifecycleObserverFactory, "dnsQueryLifecycleObserverFactory");
        this.searchDomains = searchDomains != null ? (string[]) searchDomains.Clone() : DEFAULT_SEARCH_DOMAINS;
        this.ndots = ndots >= 0 ? ndots : DEFAULT_OPTIONS.Ndots();
        this.decodeIdn = decodeIdn;
        this.completeOncePreferredResolved = completeOncePreferredResolved;
        this.retryWithTcpOnTimeout = retryWithTcpOnTimeout;

        if (socketChannelFactory == null)
        {
            socketBootstrap = null;
        }
        else
        {
            socketBootstrap = new Bootstrap();
            socketBootstrap.Option(ChannelOption.SO_REUSEADDR, true)
                    .Group(Executor)
                    .ChannelFactory(socketChannelFactory)
                    .Attr(DNS_PIPELINE_ATTRIBUTE, (object) true)
                    .Handler(NOOP_HANDLER);

            if (queryTimeoutMillis > 0 && queryTimeoutMillis <= int.MaxValue)
            {
                // Set the connect timeout to the same as queryTimeout as otherwise it might take a long
                // time for the query to fail in case of a connection timeout.
                socketBootstrap.Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, (int) queryTimeoutMillis);
            }
        }

        switch (this.resolvedAddressTypes)
        {
            case Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Only:
                supportsAAAARecords = false;
                supportsARecords = true;
                resolveRecordTypes = IPV4_ONLY_RESOLVED_RECORD_TYPES;
                resolvedInternetProtocolFamilies = IPV4_ONLY_RESOLVED_PROTOCOL_FAMILIES;
                break;
            case Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Preferred:
                supportsAAAARecords = true;
                supportsARecords = true;
                resolveRecordTypes = IPV4_PREFERRED_RESOLVED_RECORD_TYPES;
                resolvedInternetProtocolFamilies = IPV4_PREFERRED_RESOLVED_PROTOCOL_FAMILIES;
                break;
            case Netty.NET.Resolver.ResolvedAddressTypes.Ipv6Only:
                supportsAAAARecords = true;
                supportsARecords = false;
                resolveRecordTypes = IPV6_ONLY_RESOLVED_RECORD_TYPES;
                resolvedInternetProtocolFamilies = IPV6_ONLY_RESOLVED_PROTOCOL_FAMILIES;
                break;
            case Netty.NET.Resolver.ResolvedAddressTypes.Ipv6Preferred:
                supportsAAAARecords = true;
                supportsARecords = true;
                resolveRecordTypes = IPV6_PREFERRED_RESOLVED_RECORD_TYPES;
                resolvedInternetProtocolFamilies = IPV6_PREFERRED_RESOLVED_PROTOCOL_FAMILIES;
                break;
            default:
                throw new ArgumentException("Unknown ResolvedAddressTypes " + resolvedAddressTypes);
        }

        preferredAddressType = PreferredAddressType(this.resolvedAddressTypes);
        this.authoritativeDnsServerCache =
                ObjectUtil.CheckNotNull(authoritativeDnsServerCache, "authoritativeDnsServerCache");
        nameServerComparator = new NameServerComparator(AddressType(preferredAddressType).Value);
        this.maxNumConsolidation = maxNumConsolidation;

        if (maxNumConsolidation > 0)
        {
            inflightLookups =
                    new Dictionary<IDnsQuestion, TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>>>();
        }
        else
        {
            inflightLookups = null;
        }

        DnsResponseHandler responseHandler = new DnsResponseHandler(queryContextManager);
        Bootstrap bootstrap = new Bootstrap()
                .ChannelFactory(channelFactory)
                .Group(eventLoop)
                .Attr(DNS_PIPELINE_ATTRIBUTE, (object) true)
                .Handler(new DatagramChannelInitializer(maxPayloadSize, responseHandler));

        if (localAddress == null)
        {
            // CLR: the constructor's @SuppressWarnings("deprecation") covers the deprecated
            // DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION option, which Transport ports with [Obsolete] (CS0612).
#pragma warning disable CS0612
            bootstrap.Option(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, true);
#pragma warning restore CS0612
        }

        this.resolveChannelProvider = NewProvider(datagramChannelStrategy, bootstrap, localAddress, listenerScheduler);
    }

    private sealed class DatagramChannelInitializer : ChannelInitializer<IDatagramChannel>
    {
        private readonly int maxPayloadSize;
        private readonly DnsResponseHandler responseHandler;

        internal DatagramChannelInitializer(int maxPayloadSize, DnsResponseHandler responseHandler)
        {
            this.maxPayloadSize = maxPayloadSize;
            this.responseHandler = responseHandler;
        }

        protected override void InitChannel(IDatagramChannel ch)
        {
            ch.Config.SetRecvByteBufAllocator(new FixedRecvByteBufAllocator(maxPayloadSize));
            ch.Pipeline.AddLast(DATAGRAM_ENCODER, DATAGRAM_DECODER, responseHandler);
        }
    }

    // CLR: the providers also take the listener scheduler for the listener that closes a per-resolution channel.
    private static IDnsResolveChannelProvider NewProvider(DnsNameResolverChannelStrategy channelStrategy,
                                                         Bootstrap bootstrap, EndPoint localAddress,
                                                         TaskScheduler listenerScheduler)
    {
        switch (channelStrategy)
        {
            case DnsNameResolverChannelStrategy.ChannelPerResolver:
                return new DnsResolveChannelPerResolverProvider(bootstrap, localAddress);
            case DnsNameResolverChannelStrategy.ChannelPerResolution:
                return new DnsResolveChannelPerResolutionProvider(bootstrap, localAddress, listenerScheduler);
            default:
                throw new ArgumentException("Unknown DnsNameResolverChannelStrategy: " + channelStrategy);
        }
    }

    internal static SocketProtocolFamily PreferredAddressType(ResolvedAddressTypes resolvedAddressTypes)
    {
        switch (resolvedAddressTypes)
        {
        case Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Only:
        case Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Preferred:
            return SocketProtocolFamily.Inet;
        case Netty.NET.Resolver.ResolvedAddressTypes.Ipv6Only:
        case Netty.NET.Resolver.ResolvedAddressTypes.Ipv6Preferred:
            return SocketProtocolFamily.Inet6;
        default:
            throw new ArgumentException("Unknown ResolvedAddressTypes " + resolvedAddressTypes);
        }
    }

    // Only here to override in unit tests.
    // CLR: package-private and overridable: internal virtual (a friend test assembly overrides it with internal override).
    internal virtual IPEndPoint NewRedirectServerAddress(IPAddress server)
    {
        return new IPEndPoint(server, DefaultDnsServerAddressStreamProvider.DNS_PORT);
    }

    internal IDnsQueryLifecycleObserverFactory DnsQueryLifecycleObserverFactory()
    {
        return dnsQueryLifecycleObserverFactory;
    }

    /**
     * Creates a new {@link DnsServerAddressStream} to following a redirected DNS query. By overriding this
     * it provides the opportunity to sort the name servers before following a redirected DNS query.
     *
     * @param hostname the hostname.
     * @param nameservers The addresses of the DNS servers which are used in the event of a redirect. This may
     *                    contain resolved and unresolved addresses so the used {@link DnsServerAddressStream} must
     *                    allow unresolved addresses if you want to include these as well.
     * @return A {@link DnsServerAddressStream} which will be used to follow the DNS redirect or {@code null} if
     *         none should be followed.
     */
    // CLR: protected internal (Java's protected member is also visible in the package, where DnsResolveContext calls
    // it); List<InetSocketAddress> is List<EndPoint> (resolved IPEndPoint or unresolved DnsEndPoint), sorted in place.
    // Collections.sort "is guaranteed to be stable"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Collections.html) while List<T>.Sort is
    // not, so the list is ordered with Enumerable.OrderBy, which "performs a stable sort"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.orderby?view=net-10.0), and written back.
    protected internal virtual IDnsServerAddressStream NewRedirectDnsServerStream(
            string hostname, List<EndPoint> nameservers)
    {
        IDnsServerAddressStream cached = AuthoritativeDnsServerCache().Get(hostname);
        if (cached == null || cached.Size() == 0)
        {
            // If there is no cache hit (which may be the case for example when a NoopAuthoritativeDnsServerCache
            // is used), we will just directly use the provided nameservers.
            SortStable(nameservers, nameServerComparator);

            return new SequentialDnsServerAddressStream(nameservers, 0);
        }

        return cached;
    }

    // CLR: Collections.sort(list, comparator), stable (see NewRedirectDnsServerStream).
    internal static void SortStable<T>(List<T> list, IComparer<T> comparator)
    {
        List<T> sorted = list.OrderBy(e => e, comparator).ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            list[i] = sorted[i];
        }
    }

    /**
     * Returns the resolution cache.
     */
    public IDnsCache ResolveCache()
    {
        return resolveCache;
    }

    /**
     * Returns the {@link DnsCnameCache}.
     */
    public IDnsCnameCache CnameCache()
    {
        return cnameCache;
    }

    /**
     * Returns the cache used for authoritative DNS servers for a domain.
     */
    public IAuthoritativeDnsServerCache AuthoritativeDnsServerCache()
    {
        return authoritativeDnsServerCache;
    }

    /**
     * Returns the timeout of each DNS query performed by this resolver (in milliseconds).
     * The default value is 5 seconds.
     */
    public long QueryTimeoutMillis()
    {
        return queryTimeoutMillis;
    }

    /**
     * Returns the dns server address stream used for DNS queries (not resolve).
     */
    public IDnsServerAddressStream QueryDnsServerAddressStream()
    {
        return queryDnsServerAddressStream;
    }

    /**
     * Returns the {@link ResolvedAddressTypes} resolved by {@link #resolve(String)}.
     * The default value depends on the value of the system property {@code "java.net.preferIPv6Addresses"}.
     */
    public ResolvedAddressTypes ResolvedAddressTypes()
    {
        return resolvedAddressTypes;
    }

    internal SocketProtocolFamily[] ResolvedInternetProtocolFamiliesUnsafe()
    {
        return resolvedInternetProtocolFamilies;
    }

    internal string[] SearchDomains()
    {
        return searchDomains;
    }

    internal int Ndots()
    {
        return ndots;
    }

    internal bool SupportsAAAARecords()
    {
        return supportsAAAARecords;
    }

    internal bool SupportsARecords()
    {
        return supportsARecords;
    }

    internal SocketProtocolFamily PreferredAddressType()
    {
        return preferredAddressType;
    }

    internal DnsRecordType[] ResolveRecordTypes()
    {
        return resolveRecordTypes;
    }

    internal bool IsDecodeIdn()
    {
        return decodeIdn;
    }

    /**
     * Returns {@code true} if and only if this resolver sends a DNS query with the RD (recursion desired) flag set.
     * The default value is {@code true}.
     */
    public bool IsRecursionDesired()
    {
        return recursionDesired;
    }

    /**
     * Returns the maximum allowed number of DNS queries to send when resolving a host name.
     * The default value is {@code 8}.
     */
    public int MaxQueriesPerResolve()
    {
        return maxQueriesPerResolve;
    }

    /**
     * Returns the capacity of the datagram packet buffer (in bytes).  The default value is {@code 4096} bytes.
     */
    public int MaxPayloadSize()
    {
        return maxPayloadSize;
    }

    /**
     * Returns the automatic inclusion of a optional records that tries to give the remote DNS server a hint about how
     * much data the resolver can read per response is enabled.
     */
    public bool IsOptResourceEnabled()
    {
        return optResourceEnabled;
    }

    /**
     * Returns the component that tries to resolve hostnames against the hosts file prior to asking to
     * remotes DNS servers.
     */
    public IHostsFileEntriesResolver HostsFileEntriesResolver()
    {
        return hostsFileEntriesResolver;
    }

    /**
     * Closes the internal datagram channel used for sending and receiving DNS messages, and clears all DNS resource
     * records from the cache. Attempting to send a DNS query or to resolve a domain name will fail once this method
     * has been called.
     */
    public override void Dispose()
    {
        resolveChannelProvider.Close();
        resolveCache.Clear();
        cnameCache.Clear();
        authoritativeDnsServerCache.Clear();
    }

    // CLR: executor() overrides with the covariant EventLoop return type. SimpleNameResolver exposes the executor as the
    // Executor property, so this property hides it with the IEventLoop type; it is protected internal because Java's
    // protected member is also visible in the package (the resolve contexts read parent.executor()).
    protected internal new IEventLoop Executor => (IEventLoop) base.Executor;

    private IPAddress ResolveHostsFileEntry(string hostname)
    {
        if (hostsFileEntriesResolver == null)
        {
            return null;
        }

        IPAddress address = hostsFileEntriesResolver.Address(hostname, resolvedAddressTypes);
        return address == null && IsLocalHostAddress(hostname)? GetLocalHostAddress() : address;
    }

    private IReadOnlyList<IPAddress> ResolveHostsFileEntries(string hostname)
    {
        if (hostsFileEntriesResolver == null)
        {
            return null;
        }

        IReadOnlyList<IPAddress> addresses;

        if (hostsFileEntriesResolver is DefaultHostsFileEntriesResolver)
        {
            addresses = ((DefaultHostsFileEntriesResolver) hostsFileEntriesResolver)
                    .Addresses(hostname, resolvedAddressTypes);
        }
        else
        {
            IPAddress address = hostsFileEntriesResolver.Address(hostname, resolvedAddressTypes);
            addresses = address != null? SingletonList(address) : null;
        }

        return addresses == null && IsLocalHostAddress(hostname)?
                SingletonList(GetLocalHostAddress()) : addresses;
    }

    /**
     * Checks whether the given hostname refers to the current computer. This is the case for:
     * <ul>
     *     <li>localhost.</li>
     *     <li>any domain within .localhost.</li>
     *     <li>the hostname of the local computer on Windows</li>
     * </ul>
     * <p>
     * According to RFC 6761 Section 6.3, localhost and subdomains of localhost should be resolved to the loopback
     * address by name resolution libraries without querying DNS servers. The hostname of the local machine can usually
     * be resolved from the hosts file, but on Windows, this is no longer possible.
     * <p>
     * RFC 6761 behavior for {@code localhost} and {@code *.localhost} can be
     * disabled via the {@code io.netty.resolver.dns.resolveLocalhostWithoutDns}
     * system property.
     *
     * @param hostname the hostname that's being looked up
     * @return true if the hostname should point to the loopback address. False otherwise.
     * @see <a href="https://github.com/netty/netty/issues/5386">Issue 5386</a>
     * @see <a href="https://github.com/netty/netty/issues/11142">Issue 11142</a>
     * @see <a href="https://github.com/netty/netty/issues/16744">Issue 16744</a>
     * @see <a href="https://www.rfc-editor.org/rfc/rfc6761.html#section-6.3">RFC 6761</a>
     */
    private static bool IsLocalHostAddress(string hostname)
    {
        if (PlatformDependent.IsWindows() && WINDOWS_HOST_NAME != null &&
            string.Equals(WINDOWS_HOST_NAME, hostname, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!resolveLocalhostWithoutDns)
        {
            return PlatformDependent.IsWindows() && string.Equals(LOCALHOST, hostname, StringComparison.OrdinalIgnoreCase);
        }

        if (hostname.EndsWith('.'))
        {
            hostname = hostname.Substring(0, hostname.Length - 1);
        }

        return string.Equals(hostname, LOCALHOST, StringComparison.OrdinalIgnoreCase)
                || hostname.ToLowerInvariant().EndsWith(DOT_LOCALHOST, StringComparison.Ordinal);
    }

    private IPAddress GetLocalHostAddress()
    {
        switch (resolvedAddressTypes)
        {
        case Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Only:
        case Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Preferred:
            return NetUtil.LOCALHOST4;
        case Netty.NET.Resolver.ResolvedAddressTypes.Ipv6Only:
        case Netty.NET.Resolver.ResolvedAddressTypes.Ipv6Preferred:
            return NetUtil.LOCALHOST6;
        default:
            throw new InvalidOperationException("Unknown ResolvedAddressTypes " + resolvedAddressTypes);
        }
    }

    /**
     * Resolves the specified name into an address.
     *
     * @param inetHost the name to resolve
     * @param additionals additional records ({@code OPT})
     *
     * @return the address as the result of the resolution
     */
    /**
     * Resolves the specified name into an address.
     *
     * @param inetHost the name to resolve
     * @param additionals additional records ({@code OPT})
     * @param promise the {@link Promise} which will be fulfilled when the name resolution is finished
     *
     * @return the address as the result of the resolution
     */
    // CLR: the Promise overload folds into this one (Resolver API policy); an exception of the hook fails the returned
    // Task as promise.setFailure(e) fails the promise. A canceled token returns a canceled Task, as
    // SimpleNameResolver.ResolveAsync does.
    public Task<IPAddress> ResolveAsync(string inetHost, IEnumerable<IDnsRecord> additionals,
                                        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IPAddress>(cancellationToken);
        }

        IDnsRecord[] additionalsArray = ToArray(additionals, true);

        try
        {
            return DoResolveAsync(inetHost, additionalsArray, resolveCache, cancellationToken);
        }
        catch (Exception e)
        {
            return Task.FromException<IPAddress>(e);
        }
    }

    /**
     * Resolves the specified host name and port into a list of address.
     *
     * @param inetHost the name to resolve
     * @param additionals additional records ({@code OPT})
     *
     * @return the list of the address as the result of the resolution
     */
    /**
     * Resolves the specified host name and port into a list of address.
     *
     * @param inetHost the name to resolve
     * @param additionals additional records ({@code OPT})
     * @param promise the {@link Promise} which will be fulfilled when the name resolution is finished
     *
     * @return the list of the address as the result of the resolution
     */
    // CLR: the Promise overload folds into this one (see ResolveAsync).
    public Task<IReadOnlyList<IPAddress>> ResolveAllAsync(string inetHost, IEnumerable<IDnsRecord> additionals,
                                                          CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<IPAddress>>(cancellationToken);
        }

        IDnsRecord[] additionalsArray = ToArray(additionals, true);

        try
        {
            return DoResolveAllAsync(inetHost, additionalsArray, resolveCache, cancellationToken);
        }
        catch (Exception e)
        {
            return Task.FromException<IReadOnlyList<IPAddress>>(e);
        }
    }

    protected override Task<IPAddress> DoResolveAsync(string inetHost, CancellationToken cancellationToken)
    {
        return DoResolveAsync(inetHost, EMPTY_ADDITIONALS, resolveCache, cancellationToken);
    }

    /**
     * Resolves the {@link DnsRecord}s that are matched by the specified {@link DnsQuestion}. Unlike
     * {@link #query(DnsQuestion)}, this method handles redirection, CNAMEs and multiple name servers.
     * If the specified {@link DnsQuestion} is {@code A} or {@code AAAA}, this method looks up the configured
     * {@link HostsFileEntries} before sending a query to the name servers. If a match is found in the
     * {@link HostsFileEntries}, a synthetic {@code A} or {@code AAAA} record will be returned.
     *
     * @param question the question
     *
     * @return the list of the {@link DnsRecord}s as the result of the resolution
     */
    public Task<IReadOnlyList<IDnsRecord>> ResolveAllAsync(IDnsQuestion question,
                                                           CancellationToken cancellationToken = default)
    {
        return ResolveAll(question, EMPTY_ADDITIONALS, NewPromise<IReadOnlyList<IDnsRecord>>(cancellationToken));
    }

    /**
     * Resolves the {@link DnsRecord}s that are matched by the specified {@link DnsQuestion}. Unlike
     * {@link #query(DnsQuestion)}, this method handles redirection, CNAMEs and multiple name servers.
     * If the specified {@link DnsQuestion} is {@code A} or {@code AAAA}, this method looks up the configured
     * {@link HostsFileEntries} before sending a query to the name servers. If a match is found in the
     * {@link HostsFileEntries}, a synthetic {@code A} or {@code AAAA} record will be returned.
     *
     * @param question the question
     * @param additionals additional records ({@code OPT})
     *
     * @return the list of the {@link DnsRecord}s as the result of the resolution
     */
    /**
     * Resolves the {@link DnsRecord}s that are matched by the specified {@link DnsQuestion}. Unlike
     * {@link #query(DnsQuestion)}, this method handles redirection, CNAMEs and multiple name servers.
     * If the specified {@link DnsQuestion} is {@code A} or {@code AAAA}, this method looks up the configured
     * {@link HostsFileEntries} before sending a query to the name servers. If a match is found in the
     * {@link HostsFileEntries}, a synthetic {@code A} or {@code AAAA} record will be returned.
     *
     * @param question the question
     * @param additionals additional records ({@code OPT})
     * @param promise the {@link Promise} which will be fulfilled when the resolution is finished
     *
     * @return the list of the {@link DnsRecord}s as the result of the resolution
     */
    // CLR: the Promise overload folds into this one (Resolver API policy).
    public Task<IReadOnlyList<IDnsRecord>> ResolveAllAsync(IDnsQuestion question, IEnumerable<IDnsRecord> additionals,
                                                           CancellationToken cancellationToken = default)
    {
        IDnsRecord[] additionalsArray = ToArray(additionals, true);
        return ResolveAll(question, additionalsArray, NewPromise<IReadOnlyList<IDnsRecord>>(cancellationToken));
    }

    private Task<IReadOnlyList<IDnsRecord>> ResolveAll(IDnsQuestion question, IDnsRecord[] additionals,
                                                       TaskCompletionSource<IReadOnlyList<IDnsRecord>> promise)
    {
        ObjectUtil.CheckNotNull(question, "question");
        ObjectUtil.CheckNotNull(promise, "promise");

        // Respect /etc/hosts as well if the record type is A or AAAA.
        DnsRecordType type = question.Type();
        string hostname = question.Name();

        if (type == DnsRecordType.A || type == DnsRecordType.AAAA)
        {
            IReadOnlyList<IPAddress> hostsFileEntries = ResolveHostsFileEntries(hostname);
            if (hostsFileEntries != null)
            {
                List<IDnsRecord> result = new List<IDnsRecord>();

                foreach (IPAddress hostsFileEntry in hostsFileEntries)
                {
                    ByteBuf content = null;

                    if (hostsFileEntry.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (type == DnsRecordType.A)
                        {
                            content = Unpooled.WrappedBuffer(hostsFileEntry.GetAddressBytes());
                        }
                    }
                    else if (hostsFileEntry.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        if (type == DnsRecordType.AAAA)
                        {
                            content = Unpooled.WrappedBuffer(hostsFileEntry.GetAddressBytes());
                        }
                    }

                    if (content != null)
                    {
                        // Our current implementation does not support reloading the hosts file,
                        // so use a fairly large TTL (1 day, i.e. 86400 seconds).
                        result.Add(new DefaultDnsRawRecord(hostname, type, 86400, content));
                    }
                }

                if (result.Count != 0)
                {
                    if (!TrySuccess<IReadOnlyList<IDnsRecord>>(promise, result))
                    {
                        // We were not able to transfer ownership, release the records to prevent leaks.
                        foreach (IDnsRecord r in result)
                        {
                            ReferenceCountUtil.SafeRelease(r);
                        }
                    }

                    return promise.Task;
                }
            }
        }

        Task<IChannel> f = resolveChannelProvider.NextResolveChannel(promise.Task);
        if (f.IsCompleted)
        {
            ResolveAllNow(f, hostname, question, additionals, promise);
        }
        else
        {
            AddListener(f, f1 => ResolveAllNow(f1, hostname, question, additionals, promise));
        }

        return promise.Task;
    }

    private void ResolveAllNow(Task<IChannel> f, string hostname, IDnsQuestion question,
                               IDnsRecord[] additionals, TaskCompletionSource<IReadOnlyList<IDnsRecord>> promise)
    {
        if (f.IsCompletedSuccessfully)
        {
            // It was not A/AAAA question or there was no entry in /etc/hosts.
            IDnsServerAddressStream nameServerAddrs =
                    dnsServerAddressStreamProvider.NameServerAddressStream(hostname);

            new DnsRecordResolveContext(this, f.Result, promise.Task, question, additionals,
                    nameServerAddrs, maxQueriesPerResolve).Resolve(promise);
        }
        else
        {
            UnknownHostException e = ToException(f, hostname, question, additionals);
            promise.SetException(e);
        }
    }

    // CLR: the failed Task<IChannel> has no channel (see the class comment), so the message ends after "transport";
    // f.cause() is the inner exception (initCause).
    private static UnknownHostException ToException(
            Task<IChannel> f, string hostname, IDnsQuestion question, IDnsRecord[] additionals)
    {
        UnknownHostException e = new UnknownHostException(
                "Failed to resolve '" + hostname + "', couldn't setup transport", Cause(f));

        if (question != null)
        {
            ReferenceCountUtil.Release(question);
        }

        foreach (IDnsRecord record in additionals)
        {
            ReferenceCountUtil.Release(record);
        }

        return e;
    }

    // CLR: Iterable<DnsRecord> is IEnumerable<IDnsRecord> and instanceof Collection is ICollection<IDnsRecord>.
    private static IDnsRecord[] ToArray(IEnumerable<IDnsRecord> additionals, bool validateType)
    {
        ObjectUtil.CheckNotNull(additionals, "additionals");

        if (additionals is ICollection<IDnsRecord>)
        {
            ICollection<IDnsRecord> records = (ICollection<IDnsRecord>) additionals;

            foreach (IDnsRecord r in additionals)
            {
                ValidateAdditional(r, validateType);
            }

            IDnsRecord[] array = new IDnsRecord[records.Count];
            records.CopyTo(array, 0);
            return array;
        }

        using IEnumerator<IDnsRecord> additionalsIt = additionals.GetEnumerator();
        if (!additionalsIt.MoveNext())
        {
            return EMPTY_ADDITIONALS;
        }

        List<IDnsRecord> recordList = new List<IDnsRecord>();

        do
        {
            IDnsRecord r = additionalsIt.Current;
            ValidateAdditional(r, validateType);
            recordList.Add(r);
        } while (additionalsIt.MoveNext());

        return recordList.ToArray();
    }

    private static void ValidateAdditional(IDnsRecord record, bool validateType)
    {
        ObjectUtil.CheckNotNull(record, "record");
        if (validateType && record is IDnsRawRecord)
        {
            throw new ArgumentException("DnsRawRecord implementations not allowed: " + record);
        }
    }

    private IPAddress LoopbackAddress()
    {
        switch (PreferredAddressType())
        {
            case SocketProtocolFamily.Inet:
                return NetUtil.LOCALHOST4;
            case SocketProtocolFamily.Inet6:
                return NetUtil.LOCALHOST6;
            default:
                throw new NotSupportedException("Only INET and INET6 are supported");
        }
    }

    /**
     * Hook designed for extensibility so one can pass a different cache on each resolution attempt
     * instead of using the global one.
     */
    // CLR: the hook that completes the caller's Promise returns the Task of the promise it creates (Resolver API
    // policy); setSuccess is SetResult. "throws Exception" has no counterpart.
    protected virtual Task<IPAddress> DoResolveAsync(string inetHost,
                             IDnsRecord[] additionals,
                             IDnsCache resolveCache,
                             CancellationToken cancellationToken)
    {
        TaskCompletionSource<IPAddress> promise = NewPromise<IPAddress>(cancellationToken);

        if (inetHost == null || inetHost.Length == 0)
        {
            // If an empty hostname is used we should use "localhost", just like InetAddress.getByName(...) does.
            promise.SetResult(LoopbackAddress());
            return promise.Task;
        }

        IPAddress address = NetUtil.CreateInetAddressFromIpAddressString(inetHost);
        if (address != null)
        {
            // The inetHost is actually an ipaddress.
            promise.SetResult(address);
            return promise.Task;
        }

        string hostname = Hostname(inetHost);

        IPAddress hostsFileEntry = ResolveHostsFileEntry(hostname);
        if (hostsFileEntry != null)
        {
            promise.SetResult(hostsFileEntry);
            return promise.Task;
        }

        if (!DoResolveCached(hostname, additionals, promise, resolveCache))
        {
            Task<IChannel> f = resolveChannelProvider.NextResolveChannel(promise.Task);
            if (f.IsCompleted)
            {
                DoResolveNow(f, hostname, additionals, promise, resolveCache);
            }
            else
            {
                AddListener(f, f1 =>
                        DoResolveNow(f1, hostname, additionals, promise, resolveCache));
            }
        }

        return promise.Task;
    }

    private void DoResolveNow(Task<IChannel> f, string hostname, IDnsRecord[] additionals,
                              TaskCompletionSource<IPAddress> promise,
                              IDnsCache resolveCache)
    {
        if (f.IsCompletedSuccessfully)
        {
            DoResolveUncached(f.Result, hostname, additionals, promise,
                    resolveCache, completeOncePreferredResolved);
        }
        else
        {
            UnknownHostException e = ToException(f, hostname, null, additionals);
            promise.SetException(e);
        }
    }

    private bool DoResolveCached(string hostname,
                                    IDnsRecord[] additionals,
                                    TaskCompletionSource<IPAddress> promise,
                                    IDnsCache resolveCache)
    {
        IReadOnlyList<IDnsCacheEntry> cachedEntries = resolveCache.Get(hostname, additionals);
        if (cachedEntries == null || cachedEntries.Count == 0)
        {
            return false;
        }

        Exception cause = cachedEntries[0].Cause();
        if (cause == null)
        {
            int numEntries = cachedEntries.Count;

            // Find the first entry with the preferred address type.
            foreach (SocketProtocolFamily f in resolvedInternetProtocolFamilies)
            {
                for (int i = 0; i < numEntries; i++)
                {
                    IDnsCacheEntry e = cachedEntries[i];
                    AddressFamily? addressType = AddressType(f);
                    if (addressType != null && e.Address() != null && e.Address().AddressFamily == addressType)
                    {
                        TrySuccess(promise, e.Address());
                        return true;
                    }
                }
            }

            return false;
        }
        else
        {
            TryFailure(promise, cause);
            return true;
        }
    }

    // CLR: Class<? extends InetAddress> is the AddressFamily of the address (Inet4Address.class InterNetwork,
    // Inet6Address.class InterNetworkV6), the mapping infra's NameServerComparator takes; isInstance(address) is
    // an AddressFamily comparison and the null Class a null AddressFamily?.
    internal static AddressFamily? AddressType(SocketProtocolFamily f)
    {
        switch (f)
        {
            case SocketProtocolFamily.Inet:
                return AddressFamily.InterNetwork;
            case SocketProtocolFamily.Inet6:
                return AddressFamily.InterNetworkV6;
            default:
                return null;
        }
    }

    internal static bool TrySuccess<T>(TaskCompletionSource<T> promise, T result)
    {
        bool notifiedRecords = promise.TrySetResult(result);
        if (!notifiedRecords)
        {
            // There is nothing really wrong with not be able to notify the promise as we may have raced here because
            // of multiple queries that have been executed. Log it with trace level anyway just in case the user
            // wants to better understand what happened.
            logger.Trace("Failed to notify success ({}) to a promise: {}", result, promise);
        }

        return notifiedRecords;
    }

    private static void TryFailure<T>(TaskCompletionSource<T> promise, Exception cause)
    {
        if (!promise.TrySetException(cause))
        {
            // There is nothing really wrong with not be able to notify the promise as we may have raced here because
            // of multiple queries that have been executed. Log it with trace level anyway just in case the user
            // wants to better understand what happened.
            logger.Trace("Failed to notify failure to a promise: {}", promise, cause);
        }
    }

    private void DoResolveUncached(IChannel channel,
                                   string hostname,
                                   IDnsRecord[] additionals,
                                   TaskCompletionSource<IPAddress> promise,
                                   IDnsCache resolveCache, bool completeEarlyIfPossible)
    {
        TaskCompletionSource<IReadOnlyList<IPAddress>> allPromise = NewPromise<IReadOnlyList<IPAddress>>();
        DoResolveAllUncached(channel, hostname, additionals, promise.Task, allPromise,
                resolveCache, completeEarlyIfPossible);
        AddListener(allPromise.Task, future =>
        {
            if (future.IsCompletedSuccessfully)
            {
                TrySuccess(promise, future.Result[0]);
            }
            else
            {
                TryFailure(promise, Cause(future));
            }
        });
    }

    protected override Task<IReadOnlyList<IPAddress>> DoResolveAllAsync(string inetHost,
                                                                         CancellationToken cancellationToken)
    {
        return DoResolveAllAsync(inetHost, EMPTY_ADDITIONALS, resolveCache, cancellationToken);
    }

    /**
     * Hook designed for extensibility so one can pass a different cache on each resolution attempt
     * instead of using the global one.
     */
    // CLR: the hook that completes the caller's Promise returns the Task of the promise it creates (see
    // DoResolveAsync).
    protected virtual Task<IReadOnlyList<IPAddress>> DoResolveAllAsync(string inetHost,
                                IDnsRecord[] additionals,
                                IDnsCache resolveCache,
                                CancellationToken cancellationToken)
    {
        TaskCompletionSource<IReadOnlyList<IPAddress>> promise =
                NewPromise<IReadOnlyList<IPAddress>>(cancellationToken);

        if (inetHost == null || inetHost.Length == 0)
        {
            // If an empty hostname is used we should use "localhost", just like InetAddress.getAllByName(...) does.
            promise.SetResult(SingletonList(LoopbackAddress()));
            return promise.Task;
        }

        IPAddress address = NetUtil.CreateInetAddressFromIpAddressString(inetHost);
        if (address != null)
        {
            // The unresolvedAddress was created via a String that contains an ipaddress.
            promise.SetResult(SingletonList(address));
            return promise.Task;
        }

        string hostname = Hostname(inetHost);

        IReadOnlyList<IPAddress> hostsFileEntries = ResolveHostsFileEntries(hostname);
        if (hostsFileEntries != null)
        {
            promise.SetResult(hostsFileEntries);
            return promise.Task;
        }

        if (!DoResolveAllCached(hostname, additionals, promise, resolveCache, this.SearchDomains(),
                Ndots(), resolvedInternetProtocolFamilies))
        {
            Task<IChannel> f = resolveChannelProvider.NextResolveChannel(promise.Task);
            if (f.IsCompleted)
            {
                DoResolveAllNow(f, hostname, additionals, promise, resolveCache);
            }
            else
            {
                AddListener(f, f1 =>
                        DoResolveAllNow(f1, hostname, additionals, promise, resolveCache));
            }
        }

        return promise.Task;
    }

    private void DoResolveAllNow(Task<IChannel> f, string hostname, IDnsRecord[] additionals,
                              TaskCompletionSource<IReadOnlyList<IPAddress>> promise,
                              IDnsCache resolveCache)
    {
        if (f.IsCompletedSuccessfully)
        {
            DoResolveAllUncached(f.Result, hostname, additionals, promise.Task, promise,
                    resolveCache, completeOncePreferredResolved);
        }
        else
        {
            UnknownHostException e = ToException(f, hostname, null, additionals);
            promise.SetException(e);
        }
    }

    private static bool HasEntries(IReadOnlyList<IDnsCacheEntry> cachedEntries)
    {
        return cachedEntries != null && cachedEntries.Count != 0;
    }

    internal static bool DoResolveAllCached(string hostname,
                                      IDnsRecord[] additionals,
                                      TaskCompletionSource<IReadOnlyList<IPAddress>> promise,
                                      IDnsCache resolveCache,
                                      string[] searchDomains,
                                      int ndots,
                                      SocketProtocolFamily[] resolvedInternetProtocolFamilies)
    {
        IReadOnlyList<IDnsCacheEntry> cachedEntries = resolveCache.Get(hostname, additionals);
        if (!HasEntries(cachedEntries) && searchDomains != null && ndots != 0
                && !StringUtil.EndsWith(hostname, '.'))
        {
            foreach (string searchDomain in searchDomains)
            {
                string initialHostname = hostname + '.' + searchDomain;
                cachedEntries = resolveCache.Get(initialHostname, additionals);
                if (HasEntries(cachedEntries))
                {
                    break;
                }
            }
        }

        if (!HasEntries(cachedEntries))
        {
            return false;
        }

        Exception cause = cachedEntries[0].Cause();
        if (cause == null)
        {
            List<IPAddress> result = null;
            int numEntries = cachedEntries.Count;

            foreach (SocketProtocolFamily f in resolvedInternetProtocolFamilies)
            {
                for (int i = 0; i < numEntries; i++)
                {
                    IDnsCacheEntry e = cachedEntries[i];
                    AddressFamily? addressType = AddressType(f);
                    if (addressType != null && e.Address() != null && e.Address().AddressFamily == addressType)
                    {
                        if (result == null)
                        {
                            result = new List<IPAddress>(numEntries);
                        }

                        result.Add(e.Address());
                    }
                }
            }

            if (result != null)
            {
                TrySuccess<IReadOnlyList<IPAddress>>(promise, result);
                return true;
            }

            return false;
        }
        else
        {
            TryFailure(promise, cause);
            return true;
        }
    }

    // CLR: Promise<?> originalPromise is only read for isCancelled(), so it is the Task of the original promise.
    private void DoResolveAllUncached(IChannel channel,
                                      string hostname,
                                      IDnsRecord[] additionals,
                                      Task originalPromise,
                                      TaskCompletionSource<IReadOnlyList<IPAddress>> promise,
                                      IDnsCache resolveCache,
                                      bool completeEarlyIfPossible)
    {
        // Call doResolveUncached0(...) in the EventLoop as we may need to submit multiple queries which would need
        // to submit multiple Runnable at the end if we are not already on the EventLoop.
        IEventExecutor executor = Executor;
        if (executor.InEventLoop())
        {
            DoResolveAllUncached0(channel, hostname, additionals, originalPromise,
                                  promise, resolveCache, completeEarlyIfPossible);
        }
        else
        {
            executor.Execute(() =>
                    DoResolveAllUncached0(channel, hostname, additionals, originalPromise,
                                          promise, resolveCache, completeEarlyIfPossible));
        }
    }

    private void DoResolveAllUncached0(IChannel channel,
                                       string hostname,
                                       IDnsRecord[] additionals,
                                       Task originalPromise,
                                       TaskCompletionSource<IReadOnlyList<IPAddress>> promise,
                                       IDnsCache resolveCache,
                                       bool completeEarlyIfPossible)
    {

        Debug.Assert(Executor.InEventLoop());
        IDnsServerAddressStream nameServerAddrs =
                dnsServerAddressStreamProvider.NameServerAddressStream(hostname);
        DnsAddressResolveContext ctx = new DnsAddressResolveContext(this, channel,
                originalPromise, hostname, additionals, nameServerAddrs, maxQueriesPerResolve, resolveCache,
                authoritativeDnsServerCache, completeEarlyIfPossible);
        ctx.Resolve(promise);
    }

    private static string Hostname(string inetHost)
    {
        string hostname = IDN.ToASCII(inetHost);
        // Check for https://bugs.java.com/bugdatabase/view_bug.do?bug_id=6894622
        if (StringUtil.EndsWith(inetHost, '.') && !StringUtil.EndsWith(hostname, '.'))
        {
            hostname += ".";
        }

        return hostname;
    }

    /**
     * Sends a DNS query with the specified question.
     */
    // CLR: query(DnsQuestion) and query(DnsQuestion, Promise) (same comment) fold into this overload.
    public Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>> QueryAsync(IDnsQuestion question,
            CancellationToken cancellationToken = default)
    {
        return QueryAsync(NextNameServerAddress(), question, Array.Empty<IDnsRecord>(), cancellationToken);
    }

    /**
     * Sends a DNS query with the specified question with additional records.
     */
    public Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>> QueryAsync(
            IDnsQuestion question, IEnumerable<IDnsRecord> additionals, CancellationToken cancellationToken = default)
    {
        return QueryAsync(NextNameServerAddress(), question, additionals, cancellationToken);
    }

    // CLR: the query name servers are resolved, so the stream element is cast to IPEndPoint; an unresolved one
    // (DnsEndPoint) throws InvalidCastException here, where the JDK fails the datagram send.
    private IPEndPoint NextNameServerAddress()
    {
        return (IPEndPoint) queryDnsServerAddressStream.Next();
    }

    /**
     * Sends a DNS query with the specified question using the specified name server list.
     */
    // CLR: query(InetSocketAddress, DnsQuestion) and its Promise overload (same comment) fold into this overload.
    public Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>> QueryAsync(
            IPEndPoint nameServerAddr, IDnsQuestion question, CancellationToken cancellationToken = default)
    {
        return QueryAsync(nameServerAddr, question, Array.Empty<IDnsRecord>(), cancellationToken);
    }

    /**
     * Sends a DNS query with the specified question with additional records using the specified name server list.
     */
    // CLR: the Promise overload (same comment) folds into this one. When the channel is not ready yet, Java returns a
    // second promise p that PromiseNotifier.cascade(qf, p) completes from the query future, which is the caller's
    // promise itself; the CLR returns that promise's Task (PromiseNotifier: "Share the same read-only Task when
    // appropriate", docs/common-task-composition.md), and executor().newFailedFuture(e) is Task.FromException(e).
    public Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>> QueryAsync(
            IPEndPoint nameServerAddr, IDnsQuestion question,
            IEnumerable<IDnsRecord> additionals,
            CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> promise =
                NewPromise<IAddressedEnvelope<IDnsResponse, IPEndPoint>>(cancellationToken);

        Task<IChannel> f = resolveChannelProvider.NextResolveChannel(promise.Task);
        IDnsRecord[] additionalsArray = ToArray(additionals, false);

        if (f.IsCompleted)
        {
            if (f.IsCompletedSuccessfully)
            {
                return DoQuery(f.Result, nameServerAddr, question,
                        NoopDnsQueryLifecycleObserver.INSTANCE, additionalsArray,
                        true, promise).Task;
            }
            else
            {
                UnknownHostException e = ToException(f, question.Name(), question, additionalsArray);
                promise.SetException(e);

                return Task.FromException<IAddressedEnvelope<IDnsResponse, IPEndPoint>>(e);
            }
        }
        else
        {
            AddListener(f, f1 =>
            {
                if (f1.IsCompletedSuccessfully)
                {
                    DoQuery(f1.Result, nameServerAddr, question, NoopDnsQueryLifecycleObserver.INSTANCE,
                            additionalsArray, true, promise);
                }
                else
                {
                    UnknownHostException e = ToException(f1, question.Name(), question, additionalsArray);
                    promise.SetException(e);
                }
            });
            return promise.Task;
        }
    }

    /**
     * Returns {@code true} if the {@link Throwable} was caused by an timeout or transport error.
     * These methods can be used on the {@link Future#cause()} that is returned by the various methods exposed by this
     * {@link DnsNameResolver}.
     */
    // CLR: The cause of a faulted Task is its exception (Cause(Task)).
    public static bool IsTransportOrTimeoutError(Exception cause)
    {
        return cause != null && cause.InnerException is DnsNameResolverException;
    }

    /**
     * Returns {@code true} if the {@link Throwable} was caused by an timeout.
     * These methods can be used on the {@link Future#cause()} that is returned by the various methods exposed by this
     * {@link DnsNameResolver}.
     */
    public static bool IsTimeoutError(Exception cause)
    {
        return cause != null && cause.InnerException is DnsNameResolverTimeoutException;
    }

    // CLR: the Future returned is the promise itself (castPromise), so the promise is returned; the unchecked
    // cast(...) between the Promise<AddressedEnvelope<? extends DnsResponse,...>> and
    // Promise<AddressedEnvelope<DnsResponse,...>> views has no counterpart (one TaskCompletionSource type).
    internal TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> DoQuery(
            IChannel channel,
            IPEndPoint nameServerAddr, IDnsQuestion question,
            IDnsQueryLifecycleObserver queryLifecycleObserver,
            IDnsRecord[] additionals, bool flush,
            TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> promise)
    {
        TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> castPromise =
                ObjectUtil.CheckNotNull(promise, "promise");
        int payloadSize = IsOptResourceEnabled() ? MaxPayloadSize() : 0;

        if (inflightLookups != null && (additionals == null || additionals.Length == 0))
        {
            inflightLookups.TryGetValue(question, out TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> inflight);

            if (inflight != null)
            {
                // We have a query / response inflight, let's just cascade on it to reduce the network traffic.
                AddListener(inflight.Task, f =>
                {
                    if (f.IsCompletedSuccessfully)
                    {
                        // Notify the observer and after that the promise
                        queryLifecycleObserver.QuerySucceed();
                        IAddressedEnvelope<IDnsResponse, IPEndPoint> result = f.Result;

                        // Retain the result as the listener on the promise is responsible to release it.
                        ReferenceCountUtil.Retain(result);
                        promise.SetResult(result);
                    }
                    else
                    {
                        Exception cause = Cause(f);
                        if (IsTimeoutError(cause))
                        {
                            DoQueryNow(channel, nameServerAddr, question, queryLifecycleObserver,
                                    additionals, flush, payloadSize, castPromise);
                        }
                        else
                        {
                            // Notify the observer and after that the promise
                            queryLifecycleObserver.QueryFailed(cause);
                            promise.SetException(cause);
                        }
                    }
                });
                return castPromise;
            }
            else if (inflightLookups.Count < maxNumConsolidation)
            {
                // Create a new promise as we need to ensure we are the first that will add a listener to it to retain
                // the result before anyone can release it.
                TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> newPromise =
                        NewPromise<IAddressedEnvelope<IDnsResponse, IPEndPoint>>();
                inflightLookups.TryGetValue(question,
                        out TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> old);
                inflightLookups[question] = newPromise;
                Debug.Assert(old == null);

                AddListener(newPromise.Task, f =>
                {
                    // Remove the promise and add another listener to it that will call release() on the result.
                    // As the execution of the listeners is guaranteed to be in the same order as how these were added
                    // we know that all previous added listeners had a chance to handle the result already.
                    inflightLookups.Remove(question,
                            out TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> p);
                    Debug.Assert(p == newPromise);

                    if (f.IsCompletedSuccessfully)
                    {
                        // On success we need to retain the result so listeners that are added after this one
                        // will still be able to use the result.
                        IAddressedEnvelope<IDnsResponse, IPEndPoint> result = f.Result;
                        ReferenceCountUtil.Retain(result);
                        promise.SetResult(result);
                    }
                    else
                    {
                        promise.SetException(Cause(f));
                    }

                    // CLR: a continuation added to a completed Task is queued at once, and on the event loop the
                    // scheduler runs it inline before the continuations that are still being notified, while
                    // DefaultPromise appends a listener added during notification after them. Executing the release
                    // through the executor keeps it after the listeners added before this one.
                    Executor.Execute(() => RELEASE_LISTENER(p.Task));
                });

                DoQueryNow(channel, nameServerAddr, question, queryLifecycleObserver,
                        additionals, flush, payloadSize, newPromise);

                // Return the original castPromise which will be notified by the newPromise that we used above.
                // This was it's impossible for the user to add any extra listeners to the newPromise itself, which
                // is needed to guarantee the correct life-cycle of the reference counted response.
                return castPromise;
            }
        }

        DoQueryNow(channel, nameServerAddr, question, queryLifecycleObserver,
                additionals, flush, payloadSize, castPromise);
        return castPromise;
    }

    private void DoQueryNow(
            IChannel channel,
            IPEndPoint nameServerAddr, IDnsQuestion question,
            IDnsQueryLifecycleObserver queryLifecycleObserver,
            IDnsRecord[] additionals, bool flush,
            int payloadSize,
            TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> promise)
    {
        DnsQueryContext queryContext = new DatagramDnsQueryContext(channel, nameServerAddr,
                queryContextManager, queryLifecycleObserver, payloadSize,
                IsRecursionDesired(), QueryTimeoutMillis(), question, additionals,
                promise, socketBootstrap, retryWithTcpOnTimeout);
        queryContext.WriteQuery(flush);
    }

    internal IDnsServerAddressStream NewNameServerAddressStream(string hostname)
    {
        return dnsServerAddressStreamProvider.NameServerAddressStream(hostname);
    }

    // CLR: Future.cause(): the exception of a faulted Task (unwrapped from its AggregateException), a
    // TaskCanceledException (an OperationCanceledException, the counterpart of CancellationException) for a canceled
    // Task, and null otherwise. Shared by the query and resolve contexts.
    internal static Exception Cause(Task future)
    {
        if (future.IsFaulted)
        {
            return future.Exception.InnerException;
        }
        if (future.IsCanceled)
        {
            return new TaskCanceledException(future);
        }

        return null;
    }

    // CLR: executor().newPromise() of a caller's resolution or query: a TaskCompletionSource whose continuations run
    // asynchronously (they are queued to their scheduler; see the class comment). The caller's CancellationToken
    // stands for Future.cancel(...) of the returned future: CancellationToken.Register runs the callback "when this
    // CancellationToken is canceled" and "immediately and synchronously" if it already is
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.register?view=net-10.0), and
    // TrySetCanceled returns false once the Task is "already in one of the three final states"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1.trysetcanceled?view=net-10.0);
    // the registration is disposed when the promise completes.
    internal static TaskCompletionSource<T> NewPromise<T>(CancellationToken cancellationToken)
    {
        TaskCompletionSource<T> promise = NewPromise<T>();

        if (cancellationToken.CanBeCanceled)
        {
            CancellationTokenRegistration registration =
                    cancellationToken.Register(() => promise.TrySetCanceled(cancellationToken));
            _ = promise.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration) state).Dispose(),
                    registration, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }

        return promise;
    }

    // CLR: executor().newPromise() / channel.eventLoop().newPromise() of an internal promise.
    internal static TaskCompletionSource<T> NewPromise<T>()
    {
        return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // CLR: future.addListener(listener): a continuation on the listener scheduler (see the class comment).
    internal void AddListener<TResult>(Task<TResult> future, Action<Task<TResult>> listener)
    {
        _ = future.ContinueWith(listener, CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
    }

    // CLR: Collections.singletonList(e), an immutable one-element list.
    private static IReadOnlyList<T> SingletonList<T>(T e)
    {
        return Array.AsReadOnly(new[] { e });
    }

    // CLR: Arrays.toString(Object[]), "[a, b]" (see the static constructor).
    internal static string ArraysToString<T>(T[] a)
    {
        if (a == null)
        {
            return "null";
        }

        return "[" + string.Join(", ", a.Select(e => e == null ? "null" : e.ToString())) + "]";
    }

    // CLR: the inbound handler casts msg to DatagramDnsResponse.
    private sealed class DnsResponseHandler : ChannelInboundHandlerAdapter
    {

        private readonly DnsQueryContextManager queryContextManager;

        internal DnsResponseHandler(DnsQueryContextManager queryContextManager)
        {
            this.queryContextManager = queryContextManager;
        }

        public override bool IsSharable => true;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            IChannel qCh = ctx.Channel;
            DatagramDnsResponse res = (DatagramDnsResponse) msg;
            int queryId = res.Id();
            logger.Debug("{} RECEIVED: UDP [{}: {}], {}", qCh, queryId, res.Sender, res);

            DnsQueryContext qCtx = queryContextManager.Get(res.Sender, queryId);
            if (qCtx == null)
            {
                logger.Debug("{} Received a DNS response with an unknown ID: UDP [{}: {}]",
                        qCh, queryId, res.Sender);
                res.Release();
                return;
            }
            else if (qCtx.IsDone())
            {
                logger.Debug("{} Received a DNS response for a query that was timed out or cancelled: UDP [{}: {}]",
                        qCh, queryId, res.Sender);
                res.Release();
                return;
            }

            // The context will handle truncation itself.
            qCtx.FinishSuccess(res, res.IsTruncated());
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause is CorruptedFrameException)
            {
                logger.Debug("{} Unable to decode DNS response: UDP", ctx.Channel, cause);
            }
            else
            {
                logger.Warn("{} Unexpected exception: UDP", ctx.Channel, cause);
            }
        }
    }

    private interface IDnsResolveChannelProvider
    {

        /**
         * Return the next {@link ChannelFuture} that contains the {@link Channel} that should be used for resolving
         * a chain of queries.
         *
         * @param resolutionFuture  the {@link Future} that will be notified once th resolution completes.
         * @return                  the {@link ChannelFuture}
         */
        Task<IChannel> NextResolveChannel(Task resolutionFuture);

        /**
         * Close the {@link DnsResolveChannelProvider} and so cleanup resources if needed.
         */
        void Close();
    }

    private static Task<IChannel> RegisterOrBind(Bootstrap bootstrap, EndPoint localAddress)
    {
        return localAddress == null ? bootstrap.RegisterAsync() : bootstrap.BindAsync(localAddress);
    }

    // CLR: f.channel().close(): the channel of a completed registration is closed at once and that of a pending one
    // when the registration completes; a failed registration has no channel, and the bootstrap closes it.
    private static void CloseChannel(Task<IChannel> f)
    {
        if (f.IsCompletedSuccessfully)
        {
            _ = f.Result.CloseAsync();
        }
        else if (!f.IsCompleted)
        {
            _ = f.ContinueWith(static future =>
            {
                if (future.IsCompletedSuccessfully)
                {
                    _ = future.Result.CloseAsync();
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private sealed class DnsResolveChannelPerResolverProvider : IDnsResolveChannelProvider
    {

        private readonly Task<IChannel> resolveChannelFuture;

        internal DnsResolveChannelPerResolverProvider(Bootstrap bootstrap, EndPoint localAddress)
        {
            resolveChannelFuture = RegisterOrBind(bootstrap, localAddress);
        }

        public Task<IChannel> NextResolveChannel(Task resolutionFuture)
        {
            return resolveChannelFuture;
        }

        public void Close()
        {
            CloseChannel(resolveChannelFuture);
        }
    }

    private sealed class DnsResolveChannelPerResolutionProvider : IDnsResolveChannelProvider
    {

        private readonly Bootstrap bootstrap;
        private readonly EndPoint localAddress;
        private readonly TaskScheduler listenerScheduler;

        internal DnsResolveChannelPerResolutionProvider(Bootstrap bootstrap, EndPoint localAddress,
                                                        TaskScheduler listenerScheduler)
        {
            this.bootstrap = bootstrap;
            this.localAddress = localAddress;
            this.listenerScheduler = listenerScheduler;
        }

        public Task<IChannel> NextResolveChannel(Task resolutionFuture)
        {
            Task<IChannel> f = RegisterOrBind(bootstrap, localAddress);
            _ = resolutionFuture.ContinueWith(future =>
            {
                // Always just close the Channel once the resolution is considered complete.
                CloseChannel(f);
            }, CancellationToken.None, TaskContinuationOptions.None, listenerScheduler);
            return f;
        }

        public void Close()
        {
            // NOOP
        }
    }
}
