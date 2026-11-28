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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Dns;
using DnsResponseCode = Netty.NET.Handler.Codec.Dns.DnsResponseCode;
using Netty.NET.Resolver.Dns.Tests.ApacheDs;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Dns.DnsRecordType;
using static Netty.NET.Resolver.Dns.DnsNameResolver;
using static Netty.NET.Resolver.Dns.DnsResolveContext;
using static Netty.NET.Resolver.Dns.DnsServerAddresses;
using static Netty.NET.Resolver.Dns.Tests.TestDnsServer;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Resolver.Dns.Tests;

#pragma warning disable xUnit1051 // The resolver calls run without the test's cancellation token, as the source's futures do.
#pragma warning disable xUnit1069 // @Timeout fails the test after the time; the resolver futures are not tied to the test's token, as in the source.

// CLR: resolver-dns has no junit-platform.properties (only buffer, codec-http, codec-http2 and handler enable JUnit's
// parallel mode), so the source runs its test classes one after another; xUnit runs test classes in parallel. This
// class sets the process-wide DnsResolveContext.TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS, which every resolve reads, and its
// timeouts are tight, so it runs in a collection with DisableParallelization, which is "guaranteed not to run in
// parallel against any other test" (https://xunit.net/docs/running-tests-in-parallel), as handler's LoggingHandlerTest.
[CollectionDefinition(nameof(DnsNameResolverTestCollection), DisableParallelization = true)]
public sealed class DnsNameResolverTestCollection;

// CLR: the apacheds-protocol-dns/MINA imports are the ApacheDs stand-in. The static fields and static blocks run
// in their textual order in the static constructor; @BeforeAll init() and @AfterAll destroy() run from the class
// fixture BeforeAndAfterAll, which xUnit creates before the first test of the class and disposes after the last
// (https://xunit.net/docs/shared-context, "Class Fixtures"; as DnsNameResolverBuilderTest); shutdownGracefully()
// is ShutdownGracefullyAsync() without waiting, as in the source. @ParameterizedTest
// @EnumSource(DnsNameResolverChannelStrategy.class) is [Theory] [MemberData(nameof(ChannelStrategies))] over every
// value of the C# enum (Enum.GetValues), and @Timeout(value, MILLISECONDS) is the theory's Timeout (xUnit fails an
// async test that runs longer). The resolver API follows the resolver-dns mapping : resolve/resolveAll/query are
// ResolveAsync/ResolveAllAsync/QueryAsync, syncUninterruptibly().getNow() is an awaited Task, close() is
// Dispose(), future.cause() is DnsNameResolver.Cause(task) and awaitUninterruptibly() is the CLR-only
// AwaitUninterruptibly helper; envelopes and records are released as in the source. java.net UnknownHostException
// is Common's UnknownHostException. InetAddress is IPAddress, InetSocketAddress IPEndPoint (resolved) or
// DnsEndPoint (unresolved), DnsRecord IDnsRecord, EventLoop IEventLoop, Throwable Exception,
// ResolvedAddressTypes.IPV4_ONLY/... Ipv4Only/..., HashMap Dictionary, ConcurrentLinkedQueue ConcurrentQueue,
// Collections.unmodifiableSet ImmutableHashSet. JUnit asserts with messages use XunitAssertMessages. A private
// helper that overloads a test method name takes a trailing '_' (xUnit1024: test methods cannot have overloads).
// CLR: inside a DnsNameResolver subclass the simple name ResolvedAddressTypes binds to the inherited
// ResolvedAddressTypes() method, so the enum is written Netty.NET.Resolver.ResolvedAddressTypes there. The anonymous
// DnsNameResolver subclasses use the internal (package-private) 25-argument constructor with
// new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel and
// IHostsFileEntriesResolver.Default; newRedirectServerAddress is an internal override and newRedirectDnsServerStream a
// protected internal override (core notes). getHostAddress() is the CLR-only GetHostAddress (the JDK text: uncompressed
// IPv6 groups, as handler's IpSubnetFilterRule.GetHostAddress) and NetUtil.toAddressString NetUtil.ToAddressString.
// assertThrows(T.class, executable) is Assert.ThrowsAnyAsync<T> over the awaited call (JUnit accepts subtypes;
// docs/codec-http-porting.md: "assertThrows(T.class) is Assert.ThrowsAny<T>"), assertInstanceOf is
// Assert.IsAssignableFrom, and AssertJ/JUnit size checks are Assert.Single/Empty/Contains (xUnit2013, xUnit2017).
// Collections.singletonMap(k, v) is a one-entry dictionary, ConcurrentHashMap ConcurrentDictionary and
// CopyOnWriteArrayList a ConcurrentQueue; group.shutdownGracefully(0, 0, SECONDS) of a test-local group is
// ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero) without waiting.
// CLR: the anonymous RecordStore, TestDnsServer, ChannelInboundHandlerAdapter,
// DnsServerAddressStreamProvider and DnsServerResponseFeedbackAddressStream classes are named nested classes whose
// constructors take the captured locals (C# anonymous types derive from Object and "can't add methods, events, or
// custom operators", https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/anonymous-types,
// as transport's SocketHalfClosedSourceTest records). Collections.singleton(...) and
// Collections.emptySet()/emptyList() are ImmutableHashSet/ImmutableList, Collections.singletonMap(k, v) a one-entry
// Dictionary<string, object>, LinkedHashSet Common's LinkedHashSet, ConcurrentLinkedQueue ConcurrentQueue
// (poll() is TryDequeue, null when empty), AtomicBoolean/AtomicInteger/AtomicReference Common's (AtomicInteger.Set is
// Interlocked.Exchange and returns the previous value, so getAndSet(0) is Set(0)); toLowerCase() is
// ToLower(CultureInfo.CurrentCulture), as TestDnsServer maps it. InetAddress.getByAddress(bytes) is new IPAddress(bytes)
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.-ctor?view=net-10.0) and getHostAddress() of an
// IPv4 address its ToString(). java.net.ServerSocket/Socket/DatagramSocket are BCL Sockets and the stream of a socket
// is a NetworkStream over it; java.nio.ByteBuffer is the ApacheDs IoBuffer stand-in, which keeps the java.nio.Buffer
// position/limit rules and big-endian order. ThreadInterruptedException is the CLR counterpart of InterruptedException
// ("thrown when a Thread is interrupted while it is in a waiting state",
// https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadinterruptedexception?view=net-10.0) and
// CountDownLatch is CountdownEvent ("signaled when its count reaches zero",
// https://learn.microsoft.com/en-us/dotnet/api/system.threading.countdownevent?view=net-10.0), as handler-proxy's
// ProxyHandlerTest maps it. getSuppressed() is Common's ThrowableUtil.GetSuppressed.
[Collection(nameof(DnsNameResolverTestCollection))]
public class DnsNameResolverTest : IClassFixture<DnsNameResolverTest.BeforeAndAfterAll>
{
    private static readonly IInternalLogger logger;
    private const long DEFAULT_TEST_TIMEOUT_MS = 30000;

    // Using the top-100 web sites ranked in Alexa.com (Oct 2014)
    // Please use the following series of shell commands to get this up-to-date:
    // $ curl -O https://s3.amazonaws.com/alexa-static/top-1m.csv.zip
    // $ unzip -o top-1m.csv.zip top-1m.csv
    // $ head -100 top-1m.csv | cut -d, -f2 | cut -d/ -f1 | while read L; do echo '"'"$L"'",'; done > topsites.txt
    private static readonly ISet<string> DOMAINS;

    private static readonly Dictionary<string, string> DOMAINS_PUNYCODE;

    internal static readonly ISet<string> DOMAINS_ALL;

    /**
     * The list of the domain names to exclude from {@link #testResolveAorAAAA(DnsNameResolverChannelStrategy)}.
     */
    private static readonly ISet<string> EXCLUSIONS_RESOLVE_A;

    /**
     * The list of the domain names to exclude from {@link #testResolveAAAA(DnsNameResolverChannelStrategy)}.
     * Unfortunately, there are only handful of domain names with IPv6 addresses.
     */
    private static readonly ISet<string> EXCLUSIONS_RESOLVE_AAAA;

    /**
     * The list of the domain names to exclude from {@link #testQueryMx(DnsNameResolverChannelStrategy)}.
     */
    private static readonly ISet<string> EXCLUSIONS_QUERY_MX;

    private static readonly string WINDOWS_HOST_NAME;
    private static readonly bool WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS;
    private static readonly bool WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS;

    private static readonly TestDnsServer dnsServer;
    private static readonly IEventLoopGroup group;

    static DnsNameResolverTest()
    {
        logger = InternalLoggerFactory.GetInstance(typeof(DnsNameResolver));

        DOMAINS = ImmutableHashSet.Create(
                "google.com",
                "youtube.com",
                "facebook.com",
                "baidu.com",
                "wikipedia.org",
                "yahoo.com",
                "reddit.com",
                "google.co.in",
                "qq.com",
                "amazon.com",
                "taobao.com",
                "tmall.com",
                "twitter.com",
                "vk.com",
                "live.com",
                "sohu.com",
                "instagram.com",
                "google.co.jp",
                "sina.com.cn",
                "jd.com",
                "weibo.com",
                "360.cn",
                "google.de",
                "google.co.uk",
                "google.com.br",
                "list.tmall.com",
                "google.ru",
                "google.fr",
                "yandex.ru",
                "netflix.com",
                "google.it",
                "google.com.hk",
                "linkedin.com",
                "pornhub.com",
                "t.co",
                "google.es",
                "twitch.tv",
                "alipay.com",
                "xvideos.com",
                "ebay.com",
                "yahoo.co.jp",
                "google.ca",
                "google.com.mx",
                "bing.com",
                "ok.ru",
                "imgur.com",
                "microsoft.com",
                "mail.ru",
                "imdb.com",
                "aliexpress.com",
                "hao123.com",
                "msn.com",
                "tumblr.com",
                "csdn.net",
                "wikia.com",
                "wordpress.com",
                "office.com",
                "google.com.tr",
                "livejasmin.com",
                "amazon.co.jp",
                "deloton.com",
                "apple.com",
                "google.com.au",
                "paypal.com",
                "google.com.tw",
                "bongacams.com",
                "popads.net",
                "whatsapp.com",
                "blogspot.com",
                "detail.tmall.com",
                "google.pl",
                "microsoftonline.com",
                "xhamster.com",
                "google.co.id",
                "github.com",
                "stackoverflow.com",
                "pinterest.com",
                "amazon.de",
                "diply.com",
                "amazon.co.uk",
                "so.com",
                "google.com.ar",
                "coccoc.com",
                "soso.com",
                "espn.com",
                "adobe.com",
                "google.com.ua",
                "tianya.cn",
                "xnxx.com",
                "googleusercontent.com",
                "savefrom.net",
                "google.com.pk",
                "amazon.in",
                "nicovideo.jp",
                "google.co.th",
                "dropbox.com",
                "thepiratebay.org",
                "google.com.sa",
                "google.com.eg",
                "pixnet.net",
                "localhost");

        DOMAINS_PUNYCODE = new Dictionary<string, string>();
        DOMAINS_PUNYCODE["büchner.de"] = "xn--bchner-3ya.de";
        DOMAINS_PUNYCODE["müller.de"] = "xn--mller-kva.de";

        HashSet<string> all = new HashSet<string>(DOMAINS.Count + DOMAINS_PUNYCODE.Count);
        all.UnionWith(DOMAINS);
        all.UnionWith(DOMAINS_PUNYCODE.Values);
        DOMAINS_ALL = ImmutableHashSet.CreateRange(all);

        EXCLUSIONS_RESOLVE_A = new HashSet<string>
        {
                "akamaihd.net",
                "googleusercontent.com",
                StringUtil.EMPTY_STRING
        };

        EXCLUSIONS_RESOLVE_AAAA = new HashSet<string>();
        EXCLUSIONS_RESOLVE_AAAA.UnionWith(EXCLUSIONS_RESOLVE_A);
        EXCLUSIONS_RESOLVE_AAAA.UnionWith(DOMAINS);
        EXCLUSIONS_RESOLVE_AAAA.ExceptWith(new[]
        {
                "google.com",
                "facebook.com",
                "youtube.com",
                "wikipedia.org",
                "google.co.in",
                "blogspot.com",
                "vk.com",
                "google.de",
                "google.co.jp",
                "google.co.uk",
                "google.fr",
                "google.com.br",
                "google.ru",
                "google.it",
                "google.es",
                "google.com.mx",
                "xhamster.com",
                "google.ca",
                "google.co.id",
                "blogger.com",
                "flipkart.com",
                "google.com.tr",
                "google.com.au",
                "google.pl",
                "google.com.hk",
                "blogspot.in"
        });

        EXCLUSIONS_QUERY_MX = new HashSet<string>
        {
                "hao123.com",
                "blogspot.com",
                "t.co",
                "espn.go.com",
                "people.com.cn",
                "googleusercontent.com",
                "blogspot.in",
                "localhost",
                StringUtil.EMPTY_STRING
        };

        string windowsHostName;
        bool windowsHostsFileLocalhostEntryExists;
        bool windowsHostsFileHostNameEntryExists;

        try
        {
            if (PlatformDependent.IsWindows())
            {
                windowsHostName = System.Net.Dns.GetHostName();

                // CLR: Charset.defaultCharset() is Encoding.Default and CharsetUtil.UTF_16/UTF_8 are the
                // CharsetUtil.ForName("UTF-16") (big-endian, as Java's UTF-16 decoder without a BOM) and UTF8 encodings.
                HostsFileEntriesProvider provider =
                        HostsFileEntriesProvider.Parser()
                                .ParseSilently(Encoding.Default, CharsetUtil.ForName("UTF-16"), Encoding.UTF8);
                windowsHostsFileLocalhostEntryExists =
                        provider.Ipv4Entries().GetValueOrDefault("localhost") != null ||
                                provider.Ipv6Entries().GetValueOrDefault("localhost") != null;
                windowsHostsFileHostNameEntryExists =
                        provider.Ipv4Entries().GetValueOrDefault(windowsHostName) != null ||
                                provider.Ipv6Entries().GetValueOrDefault(windowsHostName) != null;
            }
            else
            {
                windowsHostName = null;
                windowsHostsFileLocalhostEntryExists = false;
                windowsHostsFileHostNameEntryExists = false;
            }
        }
        catch (Exception)
        {
            windowsHostName = null;
            windowsHostsFileLocalhostEntryExists = false;
            windowsHostsFileHostNameEntryExists = false;
        }

        WINDOWS_HOST_NAME = windowsHostName;
        WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS = windowsHostsFileLocalhostEntryExists;
        WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS = windowsHostsFileHostNameEntryExists;

        dnsServer = new TestDnsServer(DOMAINS_ALL);
        group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
    }

    // CLR: the values of @EnumSource(DnsNameResolverChannelStrategy.class).
    public static TheoryData<DnsNameResolverChannelStrategy> ChannelStrategies()
    {
        return new TheoryData<DnsNameResolverChannelStrategy>(Enum.GetValues<DnsNameResolverChannelStrategy>());
    }

    private static DnsNameResolverBuilder NewResolver(DnsNameResolverChannelStrategy strategy,
                                                      bool decodeToUnicode)
    {
        return NewResolver(strategy, decodeToUnicode, null);
    }

    private static DnsNameResolverBuilder NewResolver(DnsNameResolverChannelStrategy strategy, bool decodeToUnicode,
                                                      IDnsServerAddressStreamProvider dnsServerAddressStreamProvider)
    {
        return NewResolver(strategy, decodeToUnicode, dnsServerAddressStreamProvider, dnsServer);
    }

    private static DnsNameResolverBuilder NewResolver(DnsNameResolverChannelStrategy strategy, bool decodeToUnicode,
                                                      IDnsServerAddressStreamProvider dnsServerAddressStreamProvider,
                                                      TestDnsServer dnsServer)
    {
        return NewResolver(strategy, decodeToUnicode, dnsServerAddressStreamProvider, dnsServer, group.Next());
    }

    internal static DnsNameResolverBuilder NewResolver(DnsNameResolverChannelStrategy strategy, bool decodeToUnicode,
                                                      IDnsServerAddressStreamProvider dnsServerAddressStreamProvider,
                                                      TestDnsServer dnsServer, IEventLoop eventLoop)
    {
        DnsNameResolverBuilder builder = new DnsNameResolverBuilder(eventLoop)
                .DnsQueryLifecycleObserverFactory(new TestRecursiveCacheDnsQueryLifecycleObserverFactory())
                .DatagramChannelType(typeof(NioDatagramChannel))
                .MaxQueriesPerResolve(1)
                .DecodeIdn(decodeToUnicode)
                .OptResourceEnabled(false)
                .Ndots(1)
                .DatagramChannelStrategy(strategy);

        if (dnsServerAddressStreamProvider == null)
        {
            builder.NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer.LocalAddress()));
        }
        else
        {
            builder.NameServerProvider(new MultiDnsServerAddressStreamProvider(dnsServerAddressStreamProvider,
                    new SingletonDnsServerAddressStreamProvider(dnsServer.LocalAddress())));
        }

        return builder;
    }

    private static DnsNameResolverBuilder NewResolver(DnsNameResolverChannelStrategy strategy)
    {
        return NewResolver(strategy, true);
    }

    private static DnsNameResolverBuilder NewResolver(DnsNameResolverChannelStrategy strategy,
                                                      ResolvedAddressTypes resolvedAddressTypes)
    {
        return NewResolver(strategy)
                .ResolvedAddressTypes(resolvedAddressTypes);
    }

    private static DnsNameResolverBuilder NewNonCachedResolver(DnsNameResolverChannelStrategy strategy,
                                                               ResolvedAddressTypes resolvedAddressTypes)
    {
        return NewResolver(strategy)
                .ResolveCache(NoopDnsCache.INSTANCE)
                .ResolvedAddressTypes(resolvedAddressTypes);
    }

    private static void Init()
    {
        dnsServer.Start();
    }

    private static void Destroy()
    {
        dnsServer.Stop();
        _ = group.ShutdownGracefullyAsync();
    }

    // CLR: @BeforeAll init() and @AfterAll destroy() (see the class header).
    public sealed class BeforeAndAfterAll : IDisposable
    {
        public BeforeAndAfterAll()
        {
            Init();
        }

        public void Dispose()
        {
            Destroy();
        }
    }

    // CLR: Future.awaitUninterruptibly(): waits for the Task without observing its result or failure; the outcome is
    // then read from the Task (IsCompletedSuccessfully, DnsNameResolver.Cause).
    private static async Task AwaitUninterruptibly(Task future)
    {
        try
        {
            await future;
        }
        catch (Exception)
        {
            // The outcome is read from the Task by the caller.
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAorAAAA(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = NewResolver(strategy, ResolvedAddressTypes.Ipv4Preferred).Build();

        try
        {
            await TestResolve0(resolver, EXCLUSIONS_RESOLVE_A, AAAA);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAAAAorA(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = NewResolver(strategy, ResolvedAddressTypes.Ipv6Preferred).Build();

        try
        {
            await TestResolve0(resolver, EXCLUSIONS_RESOLVE_A, A);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    /**
     * This test will start an second DNS test server which returns fixed results that can be easily verified as
     * originating from the second DNS test server. The resolver will put {@link DnsServerAddressStreamProvider} under
     * test to ensure that some hostnames can be directed toward both the primary and secondary DNS test servers
     * simultaneously.
     */
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNameServerCache(DnsNameResolverChannelStrategy channelStrategy)
    {
        string overriddenIP = "12.34.12.34";
        TestDnsServer dnsServer2 = new TestDnsServer(new NameServerCacheRecordStore(overriddenIP));
        dnsServer2.Start();

        try
        {
            ISet<string> overriddenHostnames = new HashSet<string>();

            foreach (string name in DOMAINS)
            {
                if (EXCLUSIONS_RESOLVE_A.Contains(name))
                {
                    continue;
                }

                if (Random.Shared.Next(2) != 0)
                {
                    overriddenHostnames.Add(name);
                }
            }

            DnsNameResolver resolver = NewResolver(channelStrategy, false,
                    new OverriddenHostnamesProvider(overriddenHostnames, dnsServer2)).Build();

            try
            {
                IDictionary<string, IPAddress> resultA = await TestResolve0(resolver, EXCLUSIONS_RESOLVE_A, AAAA);

                foreach (KeyValuePair<string, IPAddress> resolvedEntry in resultA)
                {
                    if (IPAddress.IsLoopback(resolvedEntry.Value))
                    {
                        continue;
                    }

                    if (overriddenHostnames.Contains(resolvedEntry.Key))
                    {
                        Assert.Equal(overriddenIP, GetHostAddress(resolvedEntry.Value),
                                "failed to resolve " + resolvedEntry.Key);
                    }
                    else
                    {
                        Assert.NotEqual(overriddenIP, GetHostAddress(resolvedEntry.Value),
                                "failed to resolve " + resolvedEntry.Key);
                    }
                }
            }
            finally
            {
                resolver.Dispose();
            }
        }
        finally
        {
            dnsServer2.Stop();
        }
    }

    private sealed class NameServerCacheRecordStore : IRecordStore
    {
        private readonly string overriddenIP;

        internal NameServerCacheRecordStore(string overriddenIP)
        {
            this.overriddenIP = overriddenIP;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            switch (question.GetRecordType())
            {
                case var type when type == RecordType.A:
                    IDictionary<string, object> attr = new Dictionary<string, object>();
                    attr[DnsAttribute.IP_ADDRESS.ToLowerInvariant()] = overriddenIP;
                    return ImmutableHashSet.Create<IResourceRecord>(
                            new TestDnsServer.TestResourceRecord(
                                    question.GetDomainName(), question.GetRecordType(), attr));
                default:
                    return null;
            }
        }
    }

    private sealed class OverriddenHostnamesProvider : IDnsServerAddressStreamProvider
    {
        private readonly ISet<string> overriddenHostnames;
        private readonly TestDnsServer dnsServer2;

        internal OverriddenHostnamesProvider(ISet<string> overriddenHostnames, TestDnsServer dnsServer2)
        {
            this.overriddenHostnames = overriddenHostnames;
            this.dnsServer2 = dnsServer2;
        }

        public IDnsServerAddressStream NameServerAddressStream(string hostname)
        {
            return overriddenHostnames.Contains(hostname) ? Sequential(dnsServer2.LocalAddress()).Stream() :
                    null;
        }
    }

    // CLR: resolver.resolveAll(key).getNow() of the assertion message is GetNow over the Task, null while it runs.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveA(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = NewResolver(strategy, ResolvedAddressTypes.Ipv4Only)
                // Cache for eternity
                .Ttl(int.MaxValue, int.MaxValue)
                .Build();

        try
        {
            IDictionary<string, IPAddress> resultA = await TestResolve0(resolver, EXCLUSIONS_RESOLVE_A, null);

            // Now, try to resolve again to see if it's cached.
            // This test works because the DNS servers usually randomizes the order of the records in a response.
            // If cached, the resolved addresses must be always same, because we reuse the same response.

            IDictionary<string, IPAddress> resultB = await TestResolve0(resolver, EXCLUSIONS_RESOLVE_A, null);

            // Ensure the result from the cache is identical from the uncached one.
            Assert.Equal(resultB.Count, resultA.Count);

            foreach (KeyValuePair<string, IPAddress> e in resultA)
            {
                IPAddress expected = e.Value;
                resultB.TryGetValue(e.Key, out IPAddress actual);
                IReadOnlyList<IPAddress> now = GetNow(resolver.ResolveAllAsync(e.Key));
                Assert.Equal(expected, actual,
                        "Cache for " + e.Key + ": " + (now == null ? "null" : "[" + string.Join(", ", now) + "]"));
            }
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAAAA(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = NewResolver(strategy, ResolvedAddressTypes.Ipv6Only).Build();

        try
        {
            await TestResolve0(resolver, EXCLUSIONS_RESOLVE_AAAA, null);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNonCachedResolve(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = NewNonCachedResolver(strategy, ResolvedAddressTypes.Ipv4Only).Build();

        try
        {
            await TestResolve0(resolver, EXCLUSIONS_RESOLVE_A, null);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory(Timeout = (int)DEFAULT_TEST_TIMEOUT_MS)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNonCachedResolveEmptyHostName(DnsNameResolverChannelStrategy strategy)
    {
        await TestNonCachedResolveEmptyHostName_(strategy, "");
    }

    [Theory(Timeout = (int)DEFAULT_TEST_TIMEOUT_MS)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNonCachedResolveNullHostName(DnsNameResolverChannelStrategy strategy)
    {
        await TestNonCachedResolveEmptyHostName_(strategy, null);
    }

    // CLR: SocketUtils.addressByName(inetHost) is InetAddress.getByName, which Resolver's DefaultNameResolver keeps
    // with the JDK rules (null and "" are the loopback address; resolver-dns brief).
    private static async Task TestNonCachedResolveEmptyHostName_(DnsNameResolverChannelStrategy strategy,
                                                                 string inetHost)
    {
        DnsNameResolver resolver = NewNonCachedResolver(strategy, ResolvedAddressTypes.Ipv4Only).Build();

        try
        {
            IPAddress addr = await resolver.ResolveAsync(inetHost);
            using DefaultNameResolver jdkResolver = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE);
            Assert.Equal(await jdkResolver.ResolveAsync(inetHost), addr);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory(Timeout = (int)DEFAULT_TEST_TIMEOUT_MS)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNonCachedResolveAllEmptyHostName(DnsNameResolverChannelStrategy strategy)
    {
        await TestNonCachedResolveAllEmptyHostName_(strategy, "");
    }

    [Theory(Timeout = (int)DEFAULT_TEST_TIMEOUT_MS)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNonCachedResolveAllNullHostName(DnsNameResolverChannelStrategy strategy)
    {
        await TestNonCachedResolveAllEmptyHostName_(strategy, null);
    }

    // CLR: SocketUtils.allAddressesByName(inetHost) is InetAddress.getAllByName through DefaultNameResolver.
    private static async Task TestNonCachedResolveAllEmptyHostName_(DnsNameResolverChannelStrategy strategy,
                                                                    string inetHost)
    {
        DnsNameResolver resolver = NewNonCachedResolver(strategy, ResolvedAddressTypes.Ipv4Only).Build();

        try
        {
            IReadOnlyList<IPAddress> addrs = await resolver.ResolveAllAsync(inetHost);
            using DefaultNameResolver jdkResolver = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE);
            Assert.Equal(
                    await jdkResolver.ResolveAllAsync(inetHost), addrs);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    // CLR: assertEquals(unresolved, resolved.getHostName()) has no counterpart: IPAddress carries no host name (infra's
    // DnsAddressDecoder drops the record name; infra notes "Host names"), so the results are keyed by the queried name,
    // which the source asserts to be the host name. The type check compares IPAddress.AddressFamily with
    // DnsNameResolver.AddressType(f) (Class<? extends InetAddress> is AddressFamily?, core notes). getHostAddress() is
    // GetHostAddress.
    private static async Task<IDictionary<string, IPAddress>> TestResolve0(DnsNameResolver resolver,
                                                                            ISet<string> excludedDomains,
                                                                            DnsRecordType cancelledType)
    {

        Assert.True(resolver.IsRecursionDesired());

        IDictionary<string, IPAddress> results = new Dictionary<string, IPAddress>();
        IDictionary<string, Task<IPAddress>> futures =
                new LinkedHashMap<string, Task<IPAddress>>();

        foreach (string name in DOMAINS)
        {
            if (excludedDomains.Contains(name))
            {
                continue;
            }

            Resolve(resolver, futures, name);
        }

        foreach (KeyValuePair<string, Task<IPAddress>> e in futures)
        {
            string unresolved = e.Key;
            IPAddress resolved = await e.Value;

            logger.Info("{}: {}", unresolved, GetHostAddress(resolved));

            bool typeMatches = false;

            foreach (SocketProtocolFamily f in resolver.ResolvedInternetProtocolFamiliesUnsafe())
            {
                AddressFamily resolvedType = resolved.AddressFamily;
                AddressFamily? addressType = DnsNameResolver.AddressType(f);
                Assert.NotNull(addressType);

                if (addressType == resolvedType)
                {
                    typeMatches = true;
                }
            }

            Assert.True(typeMatches);

            results[unresolved] = resolved;
        }

        AssertQueryObserver(resolver, cancelledType);

        return results;
    }

    // CLR: awaitUninterruptibly().getNow() of a successful query is the awaited envelope; ((ByteBufHolder)
    // r).content() is the IByteBufHolder.Content of the record; StringBuilder.append(int) appends the decimal text
    // (invariant culture).
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestQueryMx(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = NewResolver(strategy).Build();

        try
        {
            Assert.True(resolver.IsRecursionDesired());

            IDictionary<string, Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>>> futures =
                    new LinkedHashMap<string, Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>>>();

            foreach (string name in DOMAINS)
            {
                if (EXCLUSIONS_QUERY_MX.Contains(name))
                {
                    continue;
                }

                QueryMx(resolver, futures, name);
            }

            foreach (KeyValuePair<string, Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>>> e in futures)
            {
                string hostname = e.Key;
                Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>> f = e.Value;

                IDnsResponse response = (await f).Content;
                Assert.Equal(DnsResponseCode.NOERROR, response.Code());

                int answerCount = response.Count(DnsSection.ANSWER);
                List<IDnsRecord> mxList = new List<IDnsRecord>(answerCount);

                for (int i = 0; i < answerCount; i++)
                {
                    IDnsRecord r = response.RecordAt<IDnsRecord>(DnsSection.ANSWER, i);
                    if (r.Type() == DnsRecordType.MX)
                    {
                        mxList.Add(r);
                    }
                }

                Assert.True(mxList.Count > 0);
                StringBuilder buf = new StringBuilder();

                foreach (IDnsRecord r in mxList)
                {
                    ByteBuf recordContent = ((IByteBufHolder)r).Content;

                    buf.Append(StringUtil.NEWLINE);
                    buf.Append('\t');
                    buf.Append(r.Name());
                    buf.Append(' ');
                    buf.Append(r.Type().Name());
                    buf.Append(' ');
                    buf.Append(recordContent.ReadUnsignedShort().ToString(CultureInfo.InvariantCulture));
                    buf.Append(' ');
                    buf.Append(DnsResolveContext.DecodeDomainName(recordContent));
                }

                logger.Info("{} has the following MX records:{}", hostname, buf);
                response.Release();

                // We only track query lifecycle if it is managed by the DnsNameResolverContext, and not direct calls
                // to query.
                AssertNoQueriesMade(resolver);
            }
        }
        finally
        {
            resolver.Dispose();
        }
    }

    // CLR: the anonymous Thread subclass is a Thread over a lambda (a C# anonymous type cannot override members, as
    // Transport's SocketHalfClosedSourceTest records). Thread.Interrupt "Interrupts a thread that is in the
    // WaitSleepJoin thread state" and "If the thread never blocks, the exception is never thrown"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.interrupt?view=net-10.0), so interrupt() /
    // isInterrupted() is a flag the loop reads. An exception escaping a Java thread only ends that thread (the size
    // assertion then fails), while an unhandled exception in a CLR thread "causes the application to terminate"
    // (https://learn.microsoft.com/en-us/dotnet/standard/threading/exceptions-in-managed-threads), so the loop records
    // it and the test rethrows it after the join.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public void TestNegativeTtl(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = NewResolver(strategy).NegativeTtl(10).Build();

        try
        {
            ResolveNonExistentDomain(resolver);

            const int size = 10000;
            List<UnknownHostException> exceptions = new List<UnknownHostException>();
            InterruptFlag interrupted = new InterruptFlag();
            Exception[] failure = new Exception[1];

            // If negative cache works, this thread should be done really quickly.
            Thread negativeLookupThread = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < size; i++)
                    {
                        exceptions.Add(ResolveNonExistentDomain(resolver));
                        if (interrupted.Get())
                        {
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    failure[0] = e;
                }
            });

            negativeLookupThread.Start();
            negativeLookupThread.Join(TimeSpan.FromMilliseconds(DEFAULT_TEST_TIMEOUT_MS));

            if (negativeLookupThread.IsAlive)
            {
                interrupted.Set();
                Assert.Fail("Cached negative lookups did not finish quickly.");
            }

            if (failure[0] != null)
            {
                throw failure[0];
            }

            Assert.Equal(size, exceptions.Count);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    // CLR: the interrupt flag of the negative lookup thread (see TestNegativeTtl).
    private sealed class InterruptFlag
    {
        private volatile bool value;

        internal bool Get()
        {
            return value;
        }

        internal void Set()
        {
            value = true;
        }
    }

    // CLR: assertThrows(Exception.class, () -> resolver.resolve(...).sync()) is Assert.ThrowsAny over the blocking
    // wait of the Task (GetAwaiter().GetResult() rethrows the cause, as sync() does); this helper also runs on the
    // negative lookup thread, which has no await. assertInstanceOf is Assert.IsAssignableFrom.
    private static UnknownHostException ResolveNonExistentDomain(DnsNameResolver resolver)
    {
        Exception e = Assert.ThrowsAny<Exception>(
                () => resolver.ResolveAsync("non-existent.netty.io").GetAwaiter().GetResult());
        Assert.IsAssignableFrom<UnknownHostException>(e);

        TestRecursiveCacheDnsQueryLifecycleObserverFactory lifecycleObserverFactory =
                (TestRecursiveCacheDnsQueryLifecycleObserverFactory)resolver.DnsQueryLifecycleObserverFactory();
        // Queries which are not answered from the cache (e.g. the AAAA query until its negative answer is cached)
        // complete asynchronously on the EventLoop, so the observer may only have seen some of its events yet.
        // Only verify that no unexpected event was recorded.
        TestDnsQueryLifecycleObserver observer;

        while (lifecycleObserverFactory.observers.TryDequeue(out observer))
        {
            object o;

            while (observer.events.TryDequeue(out o))
            {
                if (o is QueryCancelledEvent)
                {
                    Assert.True(observer.question.Type() == CNAME || observer.question.Type() == AAAA,
                            "unexpected type: " + observer.question);
                }
                else if (!(o is QueryWrittenEvent) && !(o is QueryFailedEvent))
                {
                    Assert.Fail("unexpected event type: " + o);
                }
            }
        }

        return (UnknownHostException)e;
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveIp(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = NewResolver(strategy).Build();

        try
        {
            IPAddress address = await resolver.ResolveAsync("10.0.0.1");

            Assert.Equal("10.0.0.1", GetHostAddress(address));

            // This address is already resolved, and so we shouldn't have to query for anything.
            AssertNoQueriesMade(resolver);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveEmptyIpv4(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolve0(strategy, ResolvedAddressTypes.Ipv4Only, NetUtil.LOCALHOST4, StringUtil.EMPTY_STRING);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveEmptyIpv6(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolve0(strategy, ResolvedAddressTypes.Ipv6Only, NetUtil.LOCALHOST6, StringUtil.EMPTY_STRING);
    }

    // CLR: AssertJ's assumeThat(x).isTrue()/isFalse()/isEqualTo/isNotEqualTo skip the test when the assumption does not
    // hold; xUnit v3 skips dynamically with Assert.SkipUnless/SkipWhen (as infra's ReadSystem and handler's
    // SniHandlerTest), with the assumption as the reason.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveLocalhostIpv4(DnsNameResolverChannelStrategy strategy)
    {
        Assert.SkipUnless(PlatformDependent.IsWindows(), "assumeThat(PlatformDependent.isWindows()).isTrue()");
        Assert.SkipWhen(WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS,
                "assumeThat(WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS).isFalse()");
        Assert.SkipWhen(DEFAULT_RESOLVE_ADDRESS_TYPES == ResolvedAddressTypes.Ipv6Preferred,
                "assumeThat(DEFAULT_RESOLVE_ADDRESS_TYPES).isNotEqualTo(ResolvedAddressTypes.IPV6_PREFERRED)");
        await TestResolve0(strategy, ResolvedAddressTypes.Ipv4Only, NetUtil.LOCALHOST4, "localhost");
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveLocalhostIpv6(DnsNameResolverChannelStrategy strategy)
    {
        Assert.SkipUnless(PlatformDependent.IsWindows(), "assumeThat(PlatformDependent.isWindows()).isTrue()");
        Assert.SkipWhen(WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS,
                "assumeThat(WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS).isFalse()");
        Assert.SkipUnless(DEFAULT_RESOLVE_ADDRESS_TYPES == ResolvedAddressTypes.Ipv6Preferred,
                "assumeThat(DEFAULT_RESOLVE_ADDRESS_TYPES).isEqualTo(ResolvedAddressTypes.IPV6_PREFERRED)");
        await TestResolve0(strategy, ResolvedAddressTypes.Ipv6Only, NetUtil.LOCALHOST6, "localhost");
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveHostNameIpv4(DnsNameResolverChannelStrategy strategy)
    {
        Assert.SkipUnless(PlatformDependent.IsWindows(), "assumeThat(PlatformDependent.isWindows()).isTrue()");
        Assert.SkipWhen(WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS,
                "assumeThat(WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS).isFalse()");
        Assert.SkipWhen(DEFAULT_RESOLVE_ADDRESS_TYPES == ResolvedAddressTypes.Ipv6Preferred,
                "assumeThat(DEFAULT_RESOLVE_ADDRESS_TYPES).isNotEqualTo(ResolvedAddressTypes.IPV6_PREFERRED)");
        await TestResolve0(strategy, ResolvedAddressTypes.Ipv4Only, NetUtil.LOCALHOST4, WINDOWS_HOST_NAME);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveHostNameIpv6(DnsNameResolverChannelStrategy strategy)
    {
        Assert.SkipUnless(PlatformDependent.IsWindows(), "assumeThat(PlatformDependent.isWindows()).isTrue()");
        Assert.SkipWhen(WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS,
                "assumeThat(WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS).isFalse()");
        Assert.SkipUnless(DEFAULT_RESOLVE_ADDRESS_TYPES == ResolvedAddressTypes.Ipv6Preferred,
                "assumeThat(DEFAULT_RESOLVE_ADDRESS_TYPES).isEqualTo(ResolvedAddressTypes.IPV6_PREFERRED)");
        await TestResolve0(strategy, ResolvedAddressTypes.Ipv6Only, NetUtil.LOCALHOST6, WINDOWS_HOST_NAME);
    }

    public static TheoryData<DnsNameResolverChannelStrategy, ResolvedAddressTypes, IPAddress, string>
            TestResolveLocalhostWithoutDNSArgs()
    {
        DnsNameResolverChannelStrategy[] strategies = Enum.GetValues<DnsNameResolverChannelStrategy>();
        IList<string> names = new[] { "localhost", "localhost.", "test.localhost", "TEsT.LOCalhost", "test.localhost." };

        TheoryData<DnsNameResolverChannelStrategy, ResolvedAddressTypes, IPAddress, string> output =
                new TheoryData<DnsNameResolverChannelStrategy, ResolvedAddressTypes, IPAddress, string>();

        foreach (DnsNameResolverChannelStrategy strategy in strategies)
        {
            foreach (string name in names)
            {
                output.Add(strategy, ResolvedAddressTypes.Ipv4Only, NetUtil.LOCALHOST4, name);
                output.Add(strategy, ResolvedAddressTypes.Ipv6Only, NetUtil.LOCALHOST6, name);
            }
        }

        return output;
    }

    // CLR: IPAddress is not an xUnit-serializable theory value, so the rows are enumerated when the theory runs
    // (DisableDiscoveryEnumeration), as codec-http's DefaultHttpRequestTest does for its rows.
    [Theory]
    [MemberData(nameof(TestResolveLocalhostWithoutDNSArgs), DisableDiscoveryEnumeration = true)]
    public async Task TestResolveLocalhostWithoutDNSOrHostsFile(DnsNameResolverChannelStrategy strategy,
                                                                ResolvedAddressTypes addressTypes,
                                                                IPAddress expectedAddr, string name)
    {
        DnsNameResolver resolver = NewResolver(strategy, addressTypes)
                .HostsFileEntriesResolver(new NoHostsFileEntriesResolver())
                .Build();

        try
        {
            IPAddress address = await resolver.ResolveAsync(name);
            Assert.Equal(expectedAddr, address);

            // We are resolving the local address, so we shouldn't make any queries.
            AssertNoQueriesMade(resolver);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    private sealed class NoHostsFileEntriesResolver : IHostsFileEntriesResolver
    {
        public IPAddress Address(string inetHost, ResolvedAddressTypes resolvedAddressTypes)
        {
            // The hosts file should not be required to resolve localhost addresses.
            return null;
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveNullIpv4(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolve0(strategy, ResolvedAddressTypes.Ipv4Only, NetUtil.LOCALHOST4, null);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveNullIpv6(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolve0(strategy, ResolvedAddressTypes.Ipv6Only, NetUtil.LOCALHOST6, null);
    }

    private static async Task TestResolve0(DnsNameResolverChannelStrategy strategy, ResolvedAddressTypes addressTypes,
                                           IPAddress expectedAddr, string name)
    {
        DnsNameResolver resolver = NewResolver(strategy, addressTypes).Build();

        try
        {
            IPAddress address = await resolver.ResolveAsync(name);
            Assert.Equal(expectedAddr, address);

            // We are resolving the local address, so we shouldn't make any queries.
            AssertNoQueriesMade(resolver);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllEmptyIpv4(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolveAll0(strategy, ResolvedAddressTypes.Ipv4Only, NetUtil.LOCALHOST4, StringUtil.EMPTY_STRING);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllEmptyIpv6(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolveAll0(strategy, ResolvedAddressTypes.Ipv6Only, NetUtil.LOCALHOST6, StringUtil.EMPTY_STRING);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllLocalhostIpv4(DnsNameResolverChannelStrategy strategy)
    {
        Assert.SkipUnless(PlatformDependent.IsWindows(), "assumeThat(PlatformDependent.isWindows()).isTrue()");
        Assert.SkipWhen(WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS,
                "assumeThat(WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS).isFalse()");
        Assert.SkipWhen(DEFAULT_RESOLVE_ADDRESS_TYPES == ResolvedAddressTypes.Ipv6Preferred,
                "assumeThat(DEFAULT_RESOLVE_ADDRESS_TYPES).isNotEqualTo(ResolvedAddressTypes.IPV6_PREFERRED)");
        await TestResolveAll0(strategy, ResolvedAddressTypes.Ipv4Only, NetUtil.LOCALHOST4, "localhost");
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllLocalhostIpv6(DnsNameResolverChannelStrategy strategy)
    {
        Assert.SkipUnless(PlatformDependent.IsWindows(), "assumeThat(PlatformDependent.isWindows()).isTrue()");
        Assert.SkipWhen(WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS,
                "assumeThat(WINDOWS_HOSTS_FILE_LOCALHOST_ENTRY_EXISTS).isFalse()");
        Assert.SkipUnless(DEFAULT_RESOLVE_ADDRESS_TYPES == ResolvedAddressTypes.Ipv6Preferred,
                "assumeThat(DEFAULT_RESOLVE_ADDRESS_TYPES).isEqualTo(ResolvedAddressTypes.IPV6_PREFERRED)");
        await TestResolveAll0(strategy, ResolvedAddressTypes.Ipv6Only, NetUtil.LOCALHOST6, "localhost");
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllHostNameIpv4(DnsNameResolverChannelStrategy strategy)
    {
        Assert.SkipUnless(PlatformDependent.IsWindows(), "assumeThat(PlatformDependent.isWindows()).isTrue()");
        Assert.SkipWhen(WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS,
                "assumeThat(WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS).isFalse()");
        Assert.SkipWhen(DEFAULT_RESOLVE_ADDRESS_TYPES == ResolvedAddressTypes.Ipv6Preferred,
                "assumeThat(DEFAULT_RESOLVE_ADDRESS_TYPES).isNotEqualTo(ResolvedAddressTypes.IPV6_PREFERRED)");
        await TestResolveAll0(strategy, ResolvedAddressTypes.Ipv4Only, NetUtil.LOCALHOST4, WINDOWS_HOST_NAME);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllHostNameIpv6(DnsNameResolverChannelStrategy strategy)
    {
        Assert.SkipUnless(PlatformDependent.IsWindows(), "assumeThat(PlatformDependent.isWindows()).isTrue()");
        Assert.SkipWhen(WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS,
                "assumeThat(WINDOWS_HOSTS_FILE_HOST_NAME_ENTRY_EXISTS).isFalse()");
        Assert.SkipUnless(DEFAULT_RESOLVE_ADDRESS_TYPES == ResolvedAddressTypes.Ipv6Preferred,
                "assumeThat(DEFAULT_RESOLVE_ADDRESS_TYPES).isEqualTo(ResolvedAddressTypes.IPV6_PREFERRED)");
        await TestResolveAll0(strategy, ResolvedAddressTypes.Ipv6Only, NetUtil.LOCALHOST6, WINDOWS_HOST_NAME);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNAMEResolveAllIpv4(DnsNameResolverChannelStrategy strategy)
    {
        await TestCNAMERecursiveResolve(strategy, true);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNAMEResolveAllIpv6(DnsNameResolverChannelStrategy strategy)
    {
        await TestCNAMERecursiveResolve(strategy, false);
    }

    // CLR: Collections.singleton is ImmutableHashSet.Create (as TestDnsServer). assertEquals(firstName,
    // resolvedAddress.getHostName()) has no counterpart (IPAddress carries no host name; infra's DnsAddressDecoder
    // drops the record name), so the CNAME chain is verified through the resolved address alone.
    private static async Task TestCNAMERecursiveResolve(DnsNameResolverChannelStrategy strategy, bool ipv4Preferred)
    {
        string firstName = "firstname.com";
        string secondName = "secondname.com";
        string lastName = "lastname.com";
        string ipv4Addr = "1.2.3.4";
        string ipv6Addr = "::1";
        TestDnsServer dnsServer2 = new TestDnsServer(
                new CnameChainRecordStore(firstName, secondName, lastName, ipv4Addr, ipv6Addr));
        dnsServer2.Start();
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(true)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));

            if (ipv4Preferred)
            {
                builder.ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Preferred);
            }
            else
            {
                builder.ResolvedAddressTypes(ResolvedAddressTypes.Ipv6Preferred);
            }

            resolver = builder.Build();
            IPAddress resolvedAddress = await resolver.ResolveAsync(firstName);

            if (ipv4Preferred)
            {
                Assert.Equal(ipv4Addr, GetHostAddress(resolvedAddress));
            }
            else
            {
                Assert.Equal(ipv6Addr, NetUtil.ToAddressString(resolvedAddress));
            }
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class CnameChainRecordStore : IRecordStore
    {
        private readonly string firstName;
        private readonly string secondName;
        private readonly string lastName;
        private readonly string ipv4Addr;
        private readonly string ipv6Addr;

        internal CnameChainRecordStore(string firstName, string secondName, string lastName, string ipv4Addr,
                                       string ipv6Addr)
        {
            this.firstName = firstName;
            this.secondName = secondName;
            this.lastName = lastName;
            this.ipv4Addr = ipv4Addr;
            this.ipv6Addr = ipv6Addr;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            ResourceRecordModifier rm = new ResourceRecordModifier();
            rm.SetDnsClass(RecordClass.IN);
            rm.SetDnsName(question.GetDomainName());
            rm.SetDnsTtl(100);
            rm.SetDnsType(RecordType.CNAME);

            if (question.GetDomainName().Equals(firstName))
            {
                rm.Put(DnsAttribute.DOMAIN_NAME, secondName);
            }
            else if (question.GetDomainName().Equals(secondName))
            {
                rm.Put(DnsAttribute.DOMAIN_NAME, lastName);
            }
            else if (question.GetDomainName().Equals(lastName))
            {
                rm.SetDnsType(question.GetRecordType());

                switch (question.GetRecordType())
                {
                    case var type when type == RecordType.A:
                        rm.Put(DnsAttribute.IP_ADDRESS, ipv4Addr);
                        break;
                    case var type when type == RecordType.AAAA:
                        rm.Put(DnsAttribute.IP_ADDRESS, ipv6Addr);
                        break;
                    default:
                        return null;
                }
            }
            else
            {
                return null;
            }

            return ImmutableHashSet.Create(rm.GetEntry());
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNAMERecursiveResolveMultipleNameServersIPv4(DnsNameResolverChannelStrategy strategy)
    {
        await TestCNAMERecursiveResolveMultipleNameServers(strategy, true);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNAMERecursiveResolveMultipleNameServersIPv6(DnsNameResolverChannelStrategy strategy)
    {
        await TestCNAMERecursiveResolveMultipleNameServers(strategy, false);
    }

    // CLR: AtomicBoolean is Common's. assertEquals(firstName, resolvedAddress.getHostName()) has no counterpart
    // (IPAddress carries no host name).
    private static async Task TestCNAMERecursiveResolveMultipleNameServers(DnsNameResolverChannelStrategy strategy,
                                                                           bool ipv4Preferred)
    {
        string firstName = "firstname.nettyfoo.com";
        string lastName = "lastname.nettybar.com";
        string ipv4Addr = "1.2.3.4";
        string ipv6Addr = "::1";
        AtomicBoolean hitServer2 = new AtomicBoolean();
        TestDnsServer dnsServer2 = new TestDnsServer(new FirstNameCnameRecordStore(hitServer2, firstName, lastName));
        TestDnsServer dnsServer3 = new TestDnsServer(new LastNameRecordStore(lastName, ipv4Addr, ipv6Addr));
        dnsServer2.Start();
        dnsServer3.Start();
        DnsNameResolver resolver = null;

        try
        {
            IAuthoritativeDnsServerCache nsCache = new DefaultAuthoritativeDnsServerCache();
            // What we want to test is the following:
            // 1. Do a DNS query.
            // 2. CNAME is returned, we want to lookup that CNAME on multiple DNS servers
            // 3. The first DNS server should fail
            // 4. The second DNS server should succeed
            // This verifies that we do in fact follow multiple DNS servers in the CNAME resolution.
            // The DnsCache is used for the name server cache, but doesn't provide a InetSocketAddress (only InetAddress
            // so no port), so we only specify the name server in the cache, and then specify both name servers in the
            // fallback name server provider.
            nsCache.Cache("nettyfoo.com.", dnsServer2.LocalAddress(), 10000, group.Next());
            SequentialDnsServerAddressStreamProvider provider =
                    new SequentialDnsServerAddressStreamProvider(dnsServer2.LocalAddress(), dnsServer3.LocalAddress());
            resolver = new MultipleNameServersDnsNameResolver(
                    group.Next(), nsCache,
                    ipv4Preferred ? ResolvedAddressTypes.Ipv4Only : ResolvedAddressTypes.Ipv6Only,
                    provider, strategy, hitServer2, dnsServer2, dnsServer3);
            IPAddress resolvedAddress = await resolver.ResolveAsync(firstName);

            if (ipv4Preferred)
            {
                Assert.Equal(ipv4Addr, GetHostAddress(resolvedAddress));
            }
            else
            {
                Assert.Equal(ipv6Addr, NetUtil.ToAddressString(resolvedAddress));
            }
        }
        finally
        {
            dnsServer2.Stop();
            dnsServer3.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class FirstNameCnameRecordStore : IRecordStore
    {
        private readonly AtomicBoolean hitServer2;
        private readonly string firstName;
        private readonly string lastName;

        internal FirstNameCnameRecordStore(AtomicBoolean hitServer2, string firstName, string lastName)
        {
            this.hitServer2 = hitServer2;
            this.firstName = firstName;
            this.lastName = lastName;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            hitServer2.Set(true);

            if (question.GetDomainName().Equals(firstName))
            {
                ResourceRecordModifier rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(question.GetDomainName());
                rm.SetDnsTtl(100);
                rm.SetDnsType(RecordType.CNAME);
                rm.Put(DnsAttribute.DOMAIN_NAME, lastName);

                return ImmutableHashSet.Create(rm.GetEntry());
            }
            else
            {
                throw new DnsException(ResponseCode.REFUSED);
            }
        }
    }

    private sealed class LastNameRecordStore : IRecordStore
    {
        private readonly string lastName;
        private readonly string ipv4Addr;
        private readonly string ipv6Addr;

        internal LastNameRecordStore(string lastName, string ipv4Addr, string ipv6Addr)
        {
            this.lastName = lastName;
            this.ipv4Addr = ipv4Addr;
            this.ipv6Addr = ipv6Addr;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            if (question.GetDomainName().Equals(lastName))
            {
                ResourceRecordModifier rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(question.GetDomainName());
                rm.SetDnsTtl(100);
                rm.SetDnsType(question.GetRecordType());

                switch (question.GetRecordType())
                {
                    case var type when type == RecordType.A:
                        rm.Put(DnsAttribute.IP_ADDRESS, ipv4Addr);
                        break;
                    case var type when type == RecordType.AAAA:
                        rm.Put(DnsAttribute.IP_ADDRESS, ipv6Addr);
                        break;
                    default:
                        return null;
                }

                return ImmutableHashSet.Create(rm.GetEntry());
            }
            else
            {
                throw new DnsException(ResponseCode.REFUSED);
            }
        }
    }

    private sealed class MultipleNameServersDnsNameResolver : DnsNameResolver
    {
        private readonly AtomicBoolean hitServer2;
        private readonly TestDnsServer dnsServer2;
        private readonly TestDnsServer dnsServer3;

        internal MultipleNameServersDnsNameResolver(IEventLoop eventLoop, IAuthoritativeDnsServerCache nsCache,
                                                    ResolvedAddressTypes resolvedAddressTypes,
                                                    SequentialDnsServerAddressStreamProvider provider,
                                                    DnsNameResolverChannelStrategy strategy, AtomicBoolean hitServer2,
                                                    TestDnsServer dnsServer2, TestDnsServer dnsServer3)
            : base(
                    eventLoop, new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel,
                    null, false, NoopDnsCache.INSTANCE, NoopDnsCnameCache.INSTANCE, nsCache, null,
                    NoopDnsQueryLifecycleObserverFactory.INSTANCE, 3000,
                    resolvedAddressTypes, true,
                    10, true, 4096, false, IHostsFileEntriesResolver.Default,
                    provider, new ThreadLocalNameServerAddressStream(provider),
                    DnsNameResolver.DEFAULT_SEARCH_DOMAINS, 0, true, false, 0, strategy)
        {
            this.hitServer2 = hitServer2;
            this.dnsServer2 = dnsServer2;
            this.dnsServer3 = dnsServer3;
        }

        internal override IPEndPoint NewRedirectServerAddress(IPAddress server)
        {
            int port = hitServer2.Get() ? dnsServer3.LocalAddress().Port :
                    dnsServer2.LocalAddress().Port;
            return new IPEndPoint(server, port);
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllNullIpv4(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolveAll0(strategy, ResolvedAddressTypes.Ipv4Only, NetUtil.LOCALHOST4, null);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllNullIpv6(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolveAll0(strategy, ResolvedAddressTypes.Ipv6Only, NetUtil.LOCALHOST6, null);
    }

    // CLR: assertEquals(1, addresses.size()) is Assert.Single (xUnit2013).
    private static async Task TestResolveAll0(DnsNameResolverChannelStrategy strategy,
                                              ResolvedAddressTypes addressTypes, IPAddress expectedAddr, string name)
    {
        DnsNameResolver resolver = NewResolver(strategy, addressTypes).Build();

        try
        {
            IReadOnlyList<IPAddress> addresses = await resolver.ResolveAllAsync(name);
            Assert.Single(addresses);
            Assert.Equal(expectedAddr, addresses[0]);

            // We are resolving the local address, so we shouldn't make any queries.
            AssertNoQueriesMade(resolver);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    // CLR: resolveAll(DnsQuestion) is ResolveAllAsync(IDnsQuestion) (Task<IReadOnlyList<IDnsRecord>>);
    // awaitUninterruptibly().getNow() of a successful resolve is the awaited list; isGreaterThan(0) is
    // Assert.True(count > 0) (infra's ResolvConfTest); ReferenceCountUtil.release is ReferenceCountUtil.Release.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllMx(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = NewResolver(strategy).Build();

        try
        {
            Assert.True(resolver.IsRecursionDesired());

            IDictionary<string, Task<IReadOnlyList<IDnsRecord>>> futures =
                    new LinkedHashMap<string, Task<IReadOnlyList<IDnsRecord>>>();

            foreach (string name in DOMAINS)
            {
                if (EXCLUSIONS_QUERY_MX.Contains(name))
                {
                    continue;
                }

                futures[name] = resolver.ResolveAllAsync(new DefaultDnsQuestion(name, DnsRecordType.MX));
            }

            foreach (KeyValuePair<string, Task<IReadOnlyList<IDnsRecord>>> e in futures)
            {
                string hostname = e.Key;
                Task<IReadOnlyList<IDnsRecord>> f = e.Value;

                IReadOnlyList<IDnsRecord> mxList = await f;
                Assert.True(mxList.Count > 0);
                StringBuilder buf = new StringBuilder();

                foreach (IDnsRecord r in mxList)
                {
                    ByteBuf recordContent = ((IByteBufHolder)r).Content;

                    buf.Append(StringUtil.NEWLINE);
                    buf.Append('\t');
                    buf.Append(r.Name());
                    buf.Append(' ');
                    buf.Append(r.Type().Name());
                    buf.Append(' ');
                    buf.Append(recordContent.ReadUnsignedShort().ToString(CultureInfo.InvariantCulture));
                    buf.Append(' ');
                    buf.Append(DnsResolveContext.DecodeDomainName(recordContent));

                    ReferenceCountUtil.Release(r);
                }

                logger.Info("{} has the following MX records:{}", hostname, buf);
            }
        }
        finally
        {
            resolver.Dispose();
        }
    }

    // CLR: InetAddress.getByAddress("foo.com", bytes) is new IPAddress(bytes) (the host name has no place in
    // IPAddress; infra notes "Host names"); the IPAddress constructor declares no UnknownHostException, so the
    // catch that wraps it in an Error has no counterpart. The resolver is not closed, as in the source.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveAllHostsFile(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = new DnsNameResolverBuilder(group.Next())
                .DatagramChannelType(typeof(NioDatagramChannel))
                .HostsFileEntriesResolver(new FooComHostsFileEntriesResolver())
                .DatagramChannelStrategy(strategy)
                .Build();

        IReadOnlyList<IDnsRecord> records = await resolver.ResolveAllAsync(new DefaultDnsQuestion("foo.com.", A));
        Assert.Single(records);
        Assert.IsAssignableFrom<IDnsRawRecord>(records[0]);

        IDnsRawRecord record = (IDnsRawRecord)records[0];
        ByteBuf content = record.Content;
        Assert.Equal("foo.com.", record.Name());
        Assert.Equal(IDnsRecord.CLASS_IN, record.DnsClass());
        Assert.Equal(A, record.Type());
        Assert.Equal(4, content.ReadableBytes);
        Assert.Equal(0x01020304, content.ReadInt());
        record.Release();
    }

    private sealed class FooComHostsFileEntriesResolver : IHostsFileEntriesResolver
    {
        public IPAddress Address(string inetHost, ResolvedAddressTypes resolvedAddressTypes)
        {
            if ("foo.com.".Equals(inetHost))
            {
                return new IPAddress(new byte[] { 1, 2, 3, 4 });
            }

            return null;
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveDecodeUnicode(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolveUnicode(strategy, true);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveNotDecodeUnicode(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolveUnicode(strategy, false);
    }

    // CLR: assertEquals(decode ? unicode : punycode, address.getHostName()) has no counterpart: decodeIdn only changes
    // the host name the JDK stores in the resolved InetAddress, and IPAddress carries none (infra's DnsAddressDecoder
    // drops the name and its IDN.toUnicode form). The resolution of the IDN names (queried in punycode) and the query
    // observers are still verified; the decoded name itself is not observable.
    private static async Task TestResolveUnicode(DnsNameResolverChannelStrategy strategy, bool decode)
    {
        DnsNameResolver resolver = NewResolver(strategy, decode).Build();

        try
        {
            foreach (KeyValuePair<string, string> entries in DOMAINS_PUNYCODE)
            {
                IPAddress address = await resolver.ResolveAsync(entries.Key);
                Assert.NotNull(address);
            }

            AssertQueryObserver(resolver, AAAA);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory(Timeout = (int)DEFAULT_TEST_TIMEOUT_MS)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task SecondDnsServerShouldBeUsedBeforeCNAMEFirstServerNotStarted(
            DnsNameResolverChannelStrategy strategy)
    {
        await SecondDnsServerShouldBeUsedBeforeCNAME(strategy, false);
    }

    [Theory(Timeout = (int)DEFAULT_TEST_TIMEOUT_MS)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task SecondDnsServerShouldBeUsedBeforeCNAMEFirstServerFailResolve(
            DnsNameResolverChannelStrategy strategy)
    {
        await SecondDnsServerShouldBeUsedBeforeCNAME(strategy, true);
    }

    // CLR: new InetSocketAddress("127.0.0.1", 22) of a literal is new IPEndPoint(IPAddress.Parse("127.0.0.1"), 22)
    // (resolver-dns brief); Collections.singleton(name) is ImmutableHashSet.Create; poll() is TryDequeue, size() Count,
    // and assertEquals(1, size()) is Assert.Single (xUnit2013).
    private static async Task SecondDnsServerShouldBeUsedBeforeCNAME(
            DnsNameResolverChannelStrategy strategy, bool startDnsServer1)
    {
        string knownHostName = "netty.io";
        TestDnsServer dnsServer1 = new TestDnsServer(ImmutableHashSet.Create("notnetty.com"));
        TestDnsServer dnsServer2 = new TestDnsServer(ImmutableHashSet.Create(knownHostName));
        DnsNameResolver resolver = null;

        try
        {
            IPEndPoint dnsServer1Address;

            if (startDnsServer1)
            {
                dnsServer1.Start();
                dnsServer1Address = dnsServer1.LocalAddress();
            }
            else
            {
                // Some address where a DNS server will not be running.
                dnsServer1Address = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 22);
            }

            dnsServer2.Start();

            TestRecursiveCacheDnsQueryLifecycleObserverFactory lifecycleObserverFactory =
                    new TestRecursiveCacheDnsQueryLifecycleObserverFactory();

            DnsNameResolverBuilder builder = new DnsNameResolverBuilder(group.Next())
                    .DnsQueryLifecycleObserverFactory(lifecycleObserverFactory)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .DatagramChannelType(typeof(NioDatagramChannel))
                    .QueryTimeoutMillis(1000) // We expect timeouts if startDnsServer1 is false
                    .OptResourceEnabled(false)
                    .Ndots(1)
                    .DatagramChannelStrategy(strategy);

            builder.NameServerProvider(new SequentialDnsServerAddressStreamProvider(dnsServer1Address,
                    dnsServer2.LocalAddress()));
            resolver = builder.Build();
            Assert.NotNull(await resolver.ResolveAsync(knownHostName));

            lifecycleObserverFactory.observers.TryDequeue(out TestDnsQueryLifecycleObserver observer);
            Assert.NotNull(observer);
            Assert.Single(lifecycleObserverFactory.observers);
            Assert.Equal(2, observer.events.Count);
            observer.events.TryDequeue(out object written);
            QueryWrittenEvent writtenEvent = (QueryWrittenEvent)written;
            Assert.Equal(dnsServer1Address, writtenEvent.dnsServerAddress);
            observer.events.TryDequeue(out object failed);
            QueryFailedEvent failedEvent = (QueryFailedEvent)failed;

            lifecycleObserverFactory.observers.TryDequeue(out observer);
            Assert.Equal(2, observer.events.Count);
            observer.events.TryDequeue(out written);
            writtenEvent = (QueryWrittenEvent)written;
            Assert.Equal(dnsServer2.LocalAddress(), writtenEvent.dnsServerAddress);
            observer.events.TryDequeue(out object succeeded);
            QuerySucceededEvent succeededEvent = (QuerySucceededEvent)succeeded;
        }
        finally
        {
            if (resolver != null)
            {
                resolver.Dispose();
            }

            dnsServer1.Stop();
            dnsServer2.Stop();
        }
    }

    [Theory(Timeout = (int)DEFAULT_TEST_TIMEOUT_MS)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task AAndAAAAQueryShouldTryFirstDnsServerBeforeSecond(DnsNameResolverChannelStrategy strategy)
    {
        string knownHostName = "netty.io";
        TestDnsServer dnsServer1 = new TestDnsServer(ImmutableHashSet.Create("notnetty.com"));
        TestDnsServer dnsServer2 = new TestDnsServer(ImmutableHashSet.Create(knownHostName));
        DnsNameResolver resolver = null;

        try
        {
            dnsServer1.Start();
            dnsServer2.Start();

            TestRecursiveCacheDnsQueryLifecycleObserverFactory lifecycleObserverFactory =
                    new TestRecursiveCacheDnsQueryLifecycleObserverFactory();

            DnsNameResolverBuilder builder = new DnsNameResolverBuilder(group.Next())
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .DnsQueryLifecycleObserverFactory(lifecycleObserverFactory)
                    .DatagramChannelType(typeof(NioDatagramChannel))
                    .OptResourceEnabled(false)
                    .Ndots(1)
                    .DatagramChannelStrategy(strategy);

            builder.NameServerProvider(new SequentialDnsServerAddressStreamProvider(dnsServer1.LocalAddress(),
                    dnsServer2.LocalAddress()));
            resolver = builder.Build();
            Assert.NotNull(await resolver.ResolveAsync(knownHostName));

            lifecycleObserverFactory.observers.TryDequeue(out TestDnsQueryLifecycleObserver observer);
            Assert.NotNull(observer);
            Assert.Single(lifecycleObserverFactory.observers);
            Assert.Equal(2, observer.events.Count);
            observer.events.TryDequeue(out object written);
            QueryWrittenEvent writtenEvent = (QueryWrittenEvent)written;
            Assert.Equal(dnsServer1.LocalAddress(), writtenEvent.dnsServerAddress);
            observer.events.TryDequeue(out object failed);
            QueryFailedEvent failedEvent = (QueryFailedEvent)failed;

            lifecycleObserverFactory.observers.TryDequeue(out observer);
            Assert.Equal(2, observer.events.Count);
            observer.events.TryDequeue(out written);
            writtenEvent = (QueryWrittenEvent)written;
            Assert.Equal(dnsServer2.LocalAddress(), writtenEvent.dnsServerAddress);
            observer.events.TryDequeue(out object succeeded);
            QuerySucceededEvent succeededEvent = (QuerySucceededEvent)succeeded;
        }
        finally
        {
            if (resolver != null)
            {
                resolver.Dispose();
            }

            dnsServer1.Stop();
            dnsServer2.Stop();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestRecursiveResolveNoCache(DnsNameResolverChannelStrategy strategy)
    {
        await TestRecursiveResolveCache_(strategy, false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestRecursiveResolveCache(DnsNameResolverChannelStrategy strategy)
    {
        await TestRecursiveResolveCache_(strategy, true);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestIpv4PreferredWhenIpv6First(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolvesPreferredWhenNonPreferredFirst0(strategy, ResolvedAddressTypes.Ipv4Preferred);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestIpv6PreferredWhenIpv4First(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolvesPreferredWhenNonPreferredFirst0(strategy, ResolvedAddressTypes.Ipv6Preferred);
    }

    // CLR: The resolver is not closed, as in the source.
    private static async Task TestResolvesPreferredWhenNonPreferredFirst0(
            DnsNameResolverChannelStrategy strategy, ResolvedAddressTypes types)
    {
        string name = "netty.com";
        // This store is non-compliant, returning records of the wrong type for a query.
        // It works since we don't verify the type of the result when resolving to deal with
        // non-compliant servers in the wild.
        List<ISet<IResourceRecord>> records = new List<ISet<IResourceRecord>>();
        string ipv6Address = "0:0:0:0:0:0:1:1";
        string ipv4Address = "1.1.1.1";

        if (types == ResolvedAddressTypes.Ipv4Preferred)
        {
            records.Add(ImmutableHashSet.Create(TestDnsServer.NewAddressRecord(name, RecordType.AAAA, ipv6Address)));
            records.Add(ImmutableHashSet.Create(TestDnsServer.NewAddressRecord(name, RecordType.A, ipv4Address)));
        }
        else
        {
            records.Add(ImmutableHashSet.Create(TestDnsServer.NewAddressRecord(name, RecordType.A, ipv4Address)));
            records.Add(ImmutableHashSet.Create(TestDnsServer.NewAddressRecord(name, RecordType.AAAA, ipv6Address)));
        }

        IEnumerator<ISet<IResourceRecord>> recordsIterator = records.GetEnumerator();
        IRecordStore arbitrarilyOrderedStore = new ArbitrarilyOrderedRecordStore(recordsIterator);
        TestDnsServer nonCompliantDnsServer = new TestDnsServer(arbitrarilyOrderedStore);
        nonCompliantDnsServer.Start();

        try
        {
            DnsNameResolver resolver = NewResolver(strategy, types)
                    .MaxQueriesPerResolve(2)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(
                            nonCompliantDnsServer.LocalAddress()))
                    .Build();
            IPAddress resolved = await resolver.ResolveAsync("netty.com");

            if (types == ResolvedAddressTypes.Ipv4Preferred)
            {
                Assert.Equal(ipv4Address, GetHostAddress(resolved));
            }
            else
            {
                Assert.Equal(ipv6Address, GetHostAddress(resolved));
            }

            IPAddress ipv4InetAddress = IPAddress.Parse(ipv4Address);
            IPAddress ipv6InetAddress = IPAddress.Parse(ipv6Address);

            IReadOnlyList<IPAddress> resolvedAll = await resolver.ResolveAllAsync("netty.com");
            IList<IPAddress> expected = types == ResolvedAddressTypes.Ipv4Preferred ?
                    new[] { ipv4InetAddress, ipv6InetAddress } : new[] { ipv6InetAddress, ipv4InetAddress };
            Assert.Equal(expected, resolvedAll);
        }
        finally
        {
            nonCompliantDnsServer.Stop();
        }
    }

    private sealed class ArbitrarilyOrderedRecordStore : IRecordStore
    {
        private readonly IEnumerator<ISet<IResourceRecord>> recordsIterator;

        internal ArbitrarilyOrderedRecordStore(IEnumerator<ISet<IResourceRecord>> recordsIterator)
        {
            this.recordsIterator = recordsIterator;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord questionRecord)
        {
            // CLR: NoSuchElementException is InvalidOperationException.
            if (!recordsIterator.MoveNext())
            {
                throw new InvalidOperationException("no more records");
            }

            return recordsIterator.Current;
        }
    }

    // CLR: the anonymous DnsNameResolver subclass is the nested RedirectToAuthorityDnsNameResolver (see
    // MultipleNameServersDnsNameResolver); the local EventLoopGroup is shut down with ShutdownGracefullyAsync(0, 0)
    // without waiting, as core's DnsNameResolverClientSubnetTest; getAddress().getHostAddress() is the IPEndPoint
    // address text. assertEquals(expectedDnsName, redirectedEvent.nameServers.get(0).getHostName()) has no counterpart:
    // the redirected name server is the IPEndPoint built from the glue A record of "dns4.some.record.netty.io." and
    // IPAddress carries no host name (core notes, line 1557), so only the address of that name server is asserted, as
    // the next source line does, and the local expectedDnsName ("dns4.some.record.netty.io.") that only that assertion
    // reads is not declared. await().cause() is AwaitUninterruptibly then DnsNameResolver.Cause; cacheHits.get is
    // GetValueOrDefault.
    private static async Task TestRecursiveResolveCache_(DnsNameResolverChannelStrategy strategy, bool cache)
    {
        string hostname = "some.record.netty.io";
        string hostname2 = "some2.record.netty.io";

        TestDnsServer dnsServerAuthority = new TestDnsServer(new HashSet<string>(
                new[] { hostname, hostname2 }));
        dnsServerAuthority.Start();

        TestDnsServer dnsServer = new RedirectingTestDnsServer(hostname,
                dnsServerAuthority.LocalAddress().Address.ToString());
        dnsServer.Start();

        TestAuthoritativeDnsServerCache nsCache = new TestAuthoritativeDnsServerCache(
                cache ? new DefaultAuthoritativeDnsServerCache() : NoopAuthoritativeDnsServerCache.INSTANCE);
        TestRecursiveCacheDnsQueryLifecycleObserverFactory lifecycleObserverFactory =
                new TestRecursiveCacheDnsQueryLifecycleObserverFactory();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        SingletonDnsServerAddressStreamProvider provider =
                new SingletonDnsServerAddressStreamProvider(dnsServer.LocalAddress());
        DnsNameResolver resolver = new RedirectToAuthorityDnsNameResolver(
                group.Next(), nsCache, lifecycleObserverFactory, provider, strategy, dnsServerAuthority);

        try
        {
            await resolver.ResolveAllAsync(hostname);

            lifecycleObserverFactory.observers.TryDequeue(out TestDnsQueryLifecycleObserver observer);
            Assert.NotNull(observer);
            Assert.True(lifecycleObserverFactory.observers.IsEmpty);
            Assert.Equal(4, observer.events.Count);
            observer.events.TryDequeue(out object written1);
            QueryWrittenEvent writtenEvent1 = (QueryWrittenEvent)written1;
            Assert.Equal(dnsServer.LocalAddress(), writtenEvent1.dnsServerAddress);
            observer.events.TryDequeue(out object redirected);
            QueryRedirectedEvent redirectedEvent = (QueryRedirectedEvent)redirected;

            Assert.Equal(dnsServerAuthority.LocalAddress(), redirectedEvent.nameServers[0]);
            observer.events.TryDequeue(out object written2);
            QueryWrittenEvent writtenEvent2 = (QueryWrittenEvent)written2;
            Assert.Equal(dnsServerAuthority.LocalAddress(), writtenEvent2.dnsServerAddress);
            observer.events.TryDequeue(out object succeeded);
            QuerySucceededEvent succeededEvent = (QuerySucceededEvent)succeeded;

            if (cache)
            {
                Assert.Null(nsCache.cache.Get("io."));
                Assert.Null(nsCache.cache.Get("netty.io."));
                IDnsServerAddressStream entries = nsCache.cache.Get("record.netty.io.");

                // Should be null because of bailiwick check.
                Assert.Null(entries);

                Assert.Null(nsCache.cache.Get(hostname));

                // Test again via cache.
                await resolver.ResolveAllAsync(hostname);

                lifecycleObserverFactory.observers.TryDequeue(out observer);
                Assert.NotNull(observer);
                Assert.True(lifecycleObserverFactory.observers.IsEmpty);
                Assert.Equal(4, observer.events.Count);
                observer.events.TryDequeue(out written1);
                writtenEvent1 = (QueryWrittenEvent)written1;
                observer.events.TryDequeue(out redirected);
                QueryRedirectedEvent ev = (QueryRedirectedEvent)redirected;

                Task<IReadOnlyList<IPAddress>> resolveAll2 = resolver.ResolveAllAsync(hostname2);
                await AwaitUninterruptibly(resolveAll2);
                Assert.IsAssignableFrom<UnknownHostException>(DnsNameResolver.Cause(resolveAll2));

                Assert.Null(nsCache.cacheHits.GetValueOrDefault("io."));
                Assert.Null(nsCache.cacheHits.GetValueOrDefault("netty.io."));
                Assert.Null(nsCache.cacheHits.GetValueOrDefault("record.netty.io."));
                Assert.Null(nsCache.cacheHits.GetValueOrDefault("some.record.netty.io."));
            }
        }
        finally
        {
            resolver.Dispose();
            _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            dnsServer.Stop();
            dnsServerAuthority.Stop();
        }
    }

    // CLR: inside a DnsNameResolver subclass the simple name ResolvedAddressTypes binds to the inherited
    // ResolvedAddressTypes() method, so the enum is written with its namespace.
    private sealed class RedirectToAuthorityDnsNameResolver : DnsNameResolver
    {
        private readonly TestDnsServer dnsServerAuthority;

        internal RedirectToAuthorityDnsNameResolver(IEventLoop eventLoop, IAuthoritativeDnsServerCache nsCache,
                                                    IDnsQueryLifecycleObserverFactory lifecycleObserverFactory,
                                                    SingletonDnsServerAddressStreamProvider provider,
                                                    DnsNameResolverChannelStrategy strategy,
                                                    TestDnsServer dnsServerAuthority)
            : base(
                    eventLoop, new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel,
                    null, false, NoopDnsCache.INSTANCE, NoopDnsCnameCache.INSTANCE, nsCache, null,
                    lifecycleObserverFactory, 3000, Netty.NET.Resolver.ResolvedAddressTypes.Ipv4Only, true,
                    10, true, 4096, false, IHostsFileEntriesResolver.Default,
                    provider, new ThreadLocalNameServerAddressStream(provider), DnsNameResolver.DEFAULT_SEARCH_DOMAINS,
                    0, true, false, 0, strategy)
        {
            this.dnsServerAuthority = dnsServerAuthority;
        }

        internal override IPEndPoint NewRedirectServerAddress(IPAddress server)
        {
            if (server.Equals(dnsServerAuthority.LocalAddress().Address))
            {
                return new IPEndPoint(server, dnsServerAuthority.LocalAddress().Port);
            }

            return base.NewRedirectServerAddress(server);
        }
    }

    // CLR: InetAddress.getHostAddress(): an Inet4Address prints the dotted quad, as IPAddress.ToString() does; an
    // Inet6Address prints the eight 16-bit groups as Integer.toHexString, separated by ':' and not compressed, followed
    // by '%' and the scope id when one was set, while IPAddress.ToString() compresses with "::". Built as handler's
    // IpSubnetFilterRule.GetHostAddress (src/Netty.NET.Handler/IpFilter/IpSubnetFilterRule.cs, with the OpenJDK and
    // Microsoft Learn sources) and handler-proxy's ProxyHandler.GetHostAddress.
    private static string GetHostAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        byte[] src = address.GetAddressBytes();
        StringBuilder sb = new StringBuilder(39);

        for (int i = 0; i < 16 / 2; i++)
        {
            sb.Append((((src[i << 1] << 8) & 0xff00) | (src[(i << 1) + 1] & 0xff))
                    .ToString("x", CultureInfo.InvariantCulture));

            if (i < 16 / 2 - 1)
            {
                sb.Append(':');
            }
        }

        if (address.ScopeId != 0)
        {
            sb.Append('%').Append(address.ScopeId.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestFollowNsRedirectsNoopCaches(DnsNameResolverChannelStrategy strategy)
    {
        await TestFollowNsRedirects(strategy, NoopDnsCache.INSTANCE, NoopAuthoritativeDnsServerCache.INSTANCE, false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestFollowNsRedirectsNoopDnsCache(DnsNameResolverChannelStrategy strategy)
    {
        await TestFollowNsRedirects(strategy, NoopDnsCache.INSTANCE, new DefaultAuthoritativeDnsServerCache(), false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestFollowNsRedirectsNoopAuthoritativeDnsServerCache(DnsNameResolverChannelStrategy strategy)
    {
        await TestFollowNsRedirects(strategy, new DefaultDnsCache(), NoopAuthoritativeDnsServerCache.INSTANCE, false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestFollowNsRedirectsDefaultCaches(DnsNameResolverChannelStrategy strategy)
    {
        await TestFollowNsRedirects(strategy, new DefaultDnsCache(), new DefaultAuthoritativeDnsServerCache(), false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestFollowNsRedirectAndTrySecondNsOnTimeout(DnsNameResolverChannelStrategy strategy)
    {
        await TestFollowNsRedirects(strategy, NoopDnsCache.INSTANCE, NoopAuthoritativeDnsServerCache.INSTANCE, true);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestFollowNsRedirectAndTrySecondNsOnTimeoutDefaultCaches(DnsNameResolverChannelStrategy strategy)
    {
        await TestFollowNsRedirects(strategy, new DefaultDnsCache(), new DefaultAuthoritativeDnsServerCache(), true);
    }

    // CLR: InetAddress.getByAddress("some.record." + domain, bytes) keeps the host name inside the address; IPAddress
    // has none (infra notes, Host names), so the name is the local expectedHostName and expected the IPAddress
    // (Java's InetAddress.equals compares the address only). newRedirectServerAddress(InetAddress server) reads
    // server.getHostName(), the name the resolver gave the address it resolved for the unresolved name server
    // (DnsAddressDecoder); the C# IPAddress of the resolved ns1/ns2 address carries no name, so the redirect server
    // records, for each A answer it sends, the answered address with its question name (nsHostNames), and the
    // override looks the name up there (the address text, as a reverse lookup of an unknown address gives,
    // otherwise). The anonymous RecordStore, TestDnsServer and DnsNameResolver subclasses are the nested
    // FollowNsRedirectsAuthorityStore, FollowNsRedirectsServer and FollowNsRedirectsResolver; the resolver uses the
    // package-private constructor, which is internal (core notes), with new
    // ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)) .NewChannel and
    // IHostsFileEntriesResolver.Default. new DatagramSocket(new InetSocketAddress(0)) is a BCL Socket bound to the
    // wildcard address with an ephemeral port: Socket(SocketType, ProtocolType) "creates a dual-mode socket" when the
    // OS supports IPv6 (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.-ctor?view=net-10.0),
    // like the JDK's dual-stack wildcard socket; nothing reads it, so a query to it times out. The try/catch of
    // UnknownHostException around InetAddress.getByAddress has no counterpart (an IPEndPoint is built without a
    // lookup). shutdownGracefully(0, 0, SECONDS) is ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero) without
    // waiting. assertEquals(1, list.size()) is Assert.Single (xUnit2013).
    private async Task TestFollowNsRedirects(DnsNameResolverChannelStrategy strategy, IDnsCache cache,
                                             IAuthoritativeDnsServerCache authoritativeDnsServerCache,
                                             bool invalidNsFirst)
    {
        string domain = "netty.io";
        string ns1Name = "ns1." + domain;
        string ns2Name = "ns2." + domain;
        string expectedHostName = "some.record." + domain;
        IPAddress expected = new IPAddress(new byte[] { 10, 10, 10, 10 });

        // This is used to simulate a query timeout...
        Socket socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(
                socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));

        TestDnsServer dnsServerAuthority =
                new TestDnsServer(new FollowNsRedirectsAuthorityStore(expectedHostName, expected));
        dnsServerAuthority.Start();

        ConcurrentDictionary<IPAddress, string> nsHostNames = new ConcurrentDictionary<IPAddress, string>();
        TestDnsServer redirectServer = new FollowNsRedirectsServer(
                new HashSet<string> { expectedHostName, ns1Name, ns2Name },
                expectedHostName, domain, ns1Name, ns2Name, invalidNsFirst, nsHostNames);
        redirectServer.Start();
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        SingletonDnsServerAddressStreamProvider provider =
                new SingletonDnsServerAddressStreamProvider(redirectServer.LocalAddress());
        DnsNameResolver resolver = new FollowNsRedirectsResolver(
                group.Next(), new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel,
                null, false,
                cache, NoopDnsCnameCache.INSTANCE, authoritativeDnsServerCache, null,
                NoopDnsQueryLifecycleObserverFactory.INSTANCE, 2000, ResolvedAddressTypes.Ipv4Only, true,
                10, true, 4096, false, IHostsFileEntriesResolver.Default,
                provider, new ThreadLocalNameServerAddressStream(provider), DnsNameResolver.DEFAULT_SEARCH_DOMAINS,
                0, true, false, 0, strategy,
                ns1Name, ns2Name, dnsServerAuthority, socket, nsHostNames);

        try
        {
            IReadOnlyList<IPAddress> resolved = await resolver.ResolveAllAsync(expectedHostName);
            Assert.Single(resolved);
            Assert.Equal(expected, resolved[0]);

            IReadOnlyList<IPAddress> resolved2 = await resolver.ResolveAllAsync(expectedHostName);
            Assert.Single(resolved2);
            Assert.Equal(expected, resolved2[0]);

            if (authoritativeDnsServerCache != NoopAuthoritativeDnsServerCache.INSTANCE)
            {
                IDnsServerAddressStream cached = authoritativeDnsServerCache.Get(domain + '.');
                // We should not cache anything because of bailiwick check
                Assert.Null(cached);
            }

            if (cache != NoopDnsCache.INSTANCE)
            {
                IReadOnlyList<IDnsCacheEntry> ns1Cached = cache.Get(ns1Name + '.', null);
                Assert.Single(ns1Cached);
                IDnsCacheEntry nsEntry = ns1Cached[0];
                Assert.NotNull(nsEntry.Address());
                Assert.Null(nsEntry.Cause());

                IReadOnlyList<IDnsCacheEntry> ns2Cached = cache.Get(ns2Name + '.', null);

                if (invalidNsFirst)
                {
                    Assert.Single(ns2Cached);
                    IDnsCacheEntry ns2Entry = ns2Cached[0];
                    Assert.NotNull(ns2Entry.Address());
                    Assert.Null(ns2Entry.Cause());
                }
                else
                {
                    // We should not even have tried to resolve the DNS name so this should be null.
                    Assert.Null(ns2Cached);
                }

                IReadOnlyList<IDnsCacheEntry> expectedCached = cache.Get(expectedHostName, null);
                Assert.Single(expectedCached);
                IDnsCacheEntry expectedEntry = expectedCached[0];
                Assert.Equal(expected, expectedEntry.Address());
                Assert.Null(expectedEntry.Cause());
            }
        }
        finally
        {
            resolver.Dispose();
            _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            redirectServer.Stop();
            dnsServerAuthority.Stop();
            socket.Dispose();
        }
    }

    private sealed class FollowNsRedirectsAuthorityStore : IRecordStore
    {
        private readonly string expectedHostName;
        private readonly IPAddress expected;

        internal FollowNsRedirectsAuthorityStore(string expectedHostName, IPAddress expected)
        {
            this.expectedHostName = expectedHostName;
            this.expected = expected;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            if (question.GetDomainName().Equals(expectedHostName))
            {
                return ImmutableHashSet.Create(NewARecord(
                        expectedHostName, expected.ToString()));
            }

            return ImmutableHashSet<IResourceRecord>.Empty;
        }
    }

    private sealed class FollowNsRedirectsServer : TestDnsServer
    {
        private readonly string expectedHostName;
        private readonly string domain;
        private readonly string ns1Name;
        private readonly string ns2Name;
        private readonly bool invalidNsFirst;
        private readonly ConcurrentDictionary<IPAddress, string> nsHostNames;

        internal FollowNsRedirectsServer(ISet<string> domains, string expectedHostName, string domain, string ns1Name,
                                         string ns2Name, bool invalidNsFirst,
                                         ConcurrentDictionary<IPAddress, string> nsHostNames) : base(domains)
        {
            this.expectedHostName = expectedHostName;
            this.domain = domain;
            this.ns1Name = ns1Name;
            this.ns2Name = ns2Name;
            this.invalidNsFirst = invalidNsFirst;
            this.nsHostNames = nsHostNames;
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            foreach (QuestionRecord record in message.GetQuestionRecords())
            {
                if (record.GetDomainName().Equals(expectedHostName))
                {
                    message.GetAdditionalRecords().Clear();
                    message.GetAnswerRecords().Clear();

                    if (invalidNsFirst)
                    {
                        message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns2Name));
                        message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns1Name));
                    }
                    else
                    {
                        message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns1Name));
                        message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns2Name));
                    }

                    return message;
                }
            }

            // CLR: remembers the name of every answered address (see TestFollowNsRedirects).
            foreach (IResourceRecord answer in message.GetAnswerRecords())
            {
                string ipAddress = answer.Get(DnsAttribute.IP_ADDRESS);
                if (ipAddress != null)
                {
                    nsHostNames[IPAddress.Parse(ipAddress)] = answer.GetDomainName();
                }
            }

            return message;
        }
    }

    private sealed class FollowNsRedirectsResolver : DnsNameResolver
    {
        private readonly string ns1Name;
        private readonly string ns2Name;
        private readonly TestDnsServer dnsServerAuthority;
        private readonly Socket socket;
        private readonly ConcurrentDictionary<IPAddress, string> nsHostNames;

        internal FollowNsRedirectsResolver(
                IEventLoop eventLoop, Func<IDatagramChannel> channelFactory, Func<ISocketChannel> socketChannelFactory,
                bool retryWithTcpOnTimeout, IDnsCache resolveCache, IDnsCnameCache cnameCache,
                IAuthoritativeDnsServerCache authoritativeDnsServerCache, EndPoint localAddress,
                IDnsQueryLifecycleObserverFactory dnsQueryLifecycleObserverFactory, long queryTimeoutMillis,
                ResolvedAddressTypes resolvedAddressTypes, bool recursionDesired, int maxQueriesPerResolve,
                bool traceEnabled, int maxPayloadSize, bool optResourceEnabled,
                IHostsFileEntriesResolver hostsFileEntriesResolver,
                IDnsServerAddressStreamProvider dnsServerAddressStreamProvider,
                IDnsServerAddressStream queryDnsServerAddressStream, string[] searchDomains, int ndots,
                bool decodeIdn, bool completeOncePreferredResolved, int maxNumConsolidation,
                DnsNameResolverChannelStrategy datagramChannelStrategy,
                string ns1Name, string ns2Name, TestDnsServer dnsServerAuthority, Socket socket,
                ConcurrentDictionary<IPAddress, string> nsHostNames)
            : base(eventLoop, channelFactory, socketChannelFactory, retryWithTcpOnTimeout, resolveCache, cnameCache,
                   authoritativeDnsServerCache, localAddress, dnsQueryLifecycleObserverFactory, queryTimeoutMillis,
                   resolvedAddressTypes, recursionDesired, maxQueriesPerResolve, traceEnabled, maxPayloadSize,
                   optResourceEnabled, hostsFileEntriesResolver, dnsServerAddressStreamProvider,
                   queryDnsServerAddressStream, searchDomains, ndots, decodeIdn, completeOncePreferredResolved,
                   maxNumConsolidation, datagramChannelStrategy)
        {
            this.ns1Name = ns1Name;
            this.ns2Name = ns2Name;
            this.dnsServerAuthority = dnsServerAuthority;
            this.socket = socket;
            this.nsHostNames = nsHostNames;
        }

        internal override IPEndPoint NewRedirectServerAddress(IPAddress server)
        {
            // CLR: server.getHostName() (see TestFollowNsRedirects).
            string serverHostName = nsHostNames.TryGetValue(server, out string name) ? name : server.ToString();
            if (serverHostName.StartsWith(ns1Name, StringComparison.Ordinal))
            {
                return new IPEndPoint(dnsServerAuthority.LocalAddress().Address,
                        dnsServerAuthority.LocalAddress().Port);
            }
            if (serverHostName.StartsWith(ns2Name, StringComparison.Ordinal))
            {
                return new IPEndPoint(NetUtil.LOCALHOST, ((IPEndPoint)socket.LocalEndPoint).Port);
            }

            return base.NewRedirectServerAddress(server);
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestMultipleAdditionalRecordsForSameNSRecord(DnsNameResolverChannelStrategy strategy)
    {
        await TestMultipleAdditionalRecordsForSameNSRecord_(strategy, false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestMultipleAdditionalRecordsForSameNSRecordReordered(DnsNameResolverChannelStrategy strategy)
    {
        await TestMultipleAdditionalRecordsForSameNSRecord_(strategy, true);
    }

    // CLR: new InetSocketAddress(InetAddress.getByAddress(ns1Name, bytes), DNS_PORT) is an IPEndPoint, which keeps
    // no host name (infra notes, Host names), so the names the source gives the addresses are kept beside them in
    // hostNames for the anonymous server's newARecord(InetSocketAddress), which reads address.getHostName();
    // InetSocketAddress.equals compares the resolved address and port, as IPEndPoint.Equals does.
    // CopyOnWriteArrayList is a ConcurrentQueue (only add and size are used), AtomicReference is Common's,
    // Collections.reverse(list) is List<T>.Reverse() (in place), and resolveAll(...).await().cause() is
    // AwaitUninterruptibly with DnsNameResolver.Cause. assertEquals(0, cached.size()) is Assert.Empty (xUnit2013).
    private static async Task TestMultipleAdditionalRecordsForSameNSRecord_(
            DnsNameResolverChannelStrategy strategy, bool reversed)
    {
        string domain = "netty.io";
        string hostname = "test.netty.io";
        string ns1Name = "ns1." + domain;
        IPEndPoint ns1Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 1 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        IPEndPoint ns2Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 2 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        IPEndPoint ns3Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 3 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        IPEndPoint ns4Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 4 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        Dictionary<IPEndPoint, string> hostNames = new Dictionary<IPEndPoint, string>
        {
            [ns1Address] = ns1Name, [ns2Address] = ns1Name, [ns3Address] = ns1Name, [ns4Address] = ns1Name
        };

        TestDnsServer redirectServer = new MultipleAdditionalRecordsServer(
                new HashSet<string> { hostname, ns1Name }, hostname, domain, ns1Name,
                ns1Address, ns2Address, ns3Address, ns4Address, hostNames);
        redirectServer.Start();
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        ConcurrentQueue<EndPoint> cached = new ConcurrentQueue<EndPoint>();
        IAuthoritativeDnsServerCache authoritativeDnsServerCache =
                new CachedAddressesAuthoritativeDnsServerCache(cached);

        AtomicReference<IDnsServerAddressStream> redirectedRef = new AtomicReference<IDnsServerAddressStream>();
        SingletonDnsServerAddressStreamProvider provider =
                new SingletonDnsServerAddressStreamProvider(redirectServer.LocalAddress());
        DnsNameResolver resolver = new RedirectStreamRecordingResolver(
                group.Next(), new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel,
                null, false,
                NoopDnsCache.INSTANCE, NoopDnsCnameCache.INSTANCE, authoritativeDnsServerCache, null,
                NoopDnsQueryLifecycleObserverFactory.INSTANCE, 2000, ResolvedAddressTypes.Ipv4Only,
                true, 10, true, 4096,
                false, IHostsFileEntriesResolver.Default,
                provider, new ThreadLocalNameServerAddressStream(provider), DnsNameResolver.DEFAULT_SEARCH_DOMAINS, 0,
                true, false, 0, strategy,
                reversed, redirectedRef);

        try
        {
            Task<IReadOnlyList<IPAddress>> future = resolver.ResolveAllAsync(hostname);
            await AwaitUninterruptibly(future);
            Exception cause = DnsNameResolver.Cause(future);
            Assert.IsAssignableFrom<UnknownHostException>(cause);
            IDnsServerAddressStream redirected = redirectedRef.Get();
            Assert.NotNull(redirected);
            Assert.Equal(4, redirected.Size());

            // We should not cache anything because of bailiwick check
            Assert.Empty(cached);

            if (reversed)
            {
                Assert.Equal<EndPoint>(ns4Address, redirected.Next());
                Assert.Equal<EndPoint>(ns3Address, redirected.Next());
                Assert.Equal<EndPoint>(ns2Address, redirected.Next());
                Assert.Equal<EndPoint>(ns1Address, redirected.Next());
            }
            else
            {
                Assert.Equal<EndPoint>(ns1Address, redirected.Next());
                Assert.Equal<EndPoint>(ns2Address, redirected.Next());
                Assert.Equal<EndPoint>(ns3Address, redirected.Next());
                Assert.Equal<EndPoint>(ns4Address, redirected.Next());
            }
        }
        finally
        {
            resolver.Dispose();
            _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            redirectServer.Stop();
        }
    }

    private sealed class MultipleAdditionalRecordsServer : TestDnsServer
    {
        private readonly string hostname;
        private readonly string domain;
        private readonly string ns1Name;
        private readonly IPEndPoint ns1Address;
        private readonly IPEndPoint ns2Address;
        private readonly IPEndPoint ns3Address;
        private readonly IPEndPoint ns4Address;
        private readonly IReadOnlyDictionary<IPEndPoint, string> hostNames;

        internal MultipleAdditionalRecordsServer(ISet<string> domains, string hostname, string domain, string ns1Name,
                                                 IPEndPoint ns1Address, IPEndPoint ns2Address, IPEndPoint ns3Address,
                                                 IPEndPoint ns4Address,
                                                 IReadOnlyDictionary<IPEndPoint, string> hostNames)
            : base(domains)
        {
            this.hostname = hostname;
            this.domain = domain;
            this.ns1Name = ns1Name;
            this.ns1Address = ns1Address;
            this.ns2Address = ns2Address;
            this.ns3Address = ns3Address;
            this.ns4Address = ns4Address;
            this.hostNames = hostNames;
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            foreach (QuestionRecord record in message.GetQuestionRecords())
            {
                if (record.GetDomainName().Equals(hostname))
                {
                    message.GetAdditionalRecords().Clear();
                    message.GetAnswerRecords().Clear();
                    message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns1Name));
                    message.GetAdditionalRecords().Add(NewARecord(ns1Address));
                    message.GetAdditionalRecords().Add(NewARecord(ns2Address));
                    message.GetAdditionalRecords().Add(NewARecord(ns3Address));
                    message.GetAdditionalRecords().Add(NewARecord(ns4Address));
                    return message;
                }
            }

            return message;
        }

        private IResourceRecord NewARecord(IPEndPoint address)
        {
            return NewARecord(hostNames[address], address.Address.ToString());
        }
    }

    private sealed class CachedAddressesAuthoritativeDnsServerCache : IAuthoritativeDnsServerCache
    {
        private readonly ConcurrentQueue<EndPoint> cached;

        internal CachedAddressesAuthoritativeDnsServerCache(ConcurrentQueue<EndPoint> cached)
        {
            this.cached = cached;
        }

        public IDnsServerAddressStream Get(string hostname)
        {
            return null;
        }

        public void Cache(string hostname, EndPoint address, long originalTtl, IEventLoop loop)
        {
            cached.Enqueue(address);
        }

        public void Clear()
        {
            // NOOP
        }

        public bool Clear(string hostname)
        {
            return false;
        }
    }

    // CLR: the anonymous DnsNameResolver subclasses of testMultipleAdditionalRecordsForSameNSRecord and
    // testNSRecordsFromCache (the latter does not reverse, so it passes reversed = false); newRedirectDnsServerStream
    // is protected internal (core notes).
    private sealed class RedirectStreamRecordingResolver : DnsNameResolver
    {
        private readonly bool reversed;
        private readonly AtomicReference<IDnsServerAddressStream> redirectedRef;

        internal RedirectStreamRecordingResolver(
                IEventLoop eventLoop, Func<IDatagramChannel> channelFactory, Func<ISocketChannel> socketChannelFactory,
                bool retryWithTcpOnTimeout, IDnsCache resolveCache, IDnsCnameCache cnameCache,
                IAuthoritativeDnsServerCache authoritativeDnsServerCache, EndPoint localAddress,
                IDnsQueryLifecycleObserverFactory dnsQueryLifecycleObserverFactory, long queryTimeoutMillis,
                ResolvedAddressTypes resolvedAddressTypes, bool recursionDesired, int maxQueriesPerResolve,
                bool traceEnabled, int maxPayloadSize, bool optResourceEnabled,
                IHostsFileEntriesResolver hostsFileEntriesResolver,
                IDnsServerAddressStreamProvider dnsServerAddressStreamProvider,
                IDnsServerAddressStream queryDnsServerAddressStream, string[] searchDomains, int ndots,
                bool decodeIdn, bool completeOncePreferredResolved, int maxNumConsolidation,
                DnsNameResolverChannelStrategy datagramChannelStrategy,
                bool reversed, AtomicReference<IDnsServerAddressStream> redirectedRef)
            : base(eventLoop, channelFactory, socketChannelFactory, retryWithTcpOnTimeout, resolveCache, cnameCache,
                   authoritativeDnsServerCache, localAddress, dnsQueryLifecycleObserverFactory, queryTimeoutMillis,
                   resolvedAddressTypes, recursionDesired, maxQueriesPerResolve, traceEnabled, maxPayloadSize,
                   optResourceEnabled, hostsFileEntriesResolver, dnsServerAddressStreamProvider,
                   queryDnsServerAddressStream, searchDomains, ndots, decodeIdn, completeOncePreferredResolved,
                   maxNumConsolidation, datagramChannelStrategy)
        {
            this.reversed = reversed;
            this.redirectedRef = redirectedRef;
        }

        protected internal override IDnsServerAddressStream NewRedirectDnsServerStream(
                string hostname, List<EndPoint> nameservers)
        {
            if (reversed)
            {
                nameservers.Reverse();
            }

            IDnsServerAddressStream stream = new SequentialDnsServerAddressStream(nameservers, 0);
            redirectedRef.Set(stream);
            return stream;
        }
    }

    // CLR: as testMultipleAdditionalRecordsForSameNSRecord: the host names of the InetSocketAddress values are kept
    // in hostNames, and the anonymous classes are the nested NSRecordsFromCacheServer,
    // CachedAddressesAuthoritativeDnsServerCache and RedirectStreamRecordingResolver.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNSRecordsFromCache(DnsNameResolverChannelStrategy strategy)
    {
        string domain = "netty.io";
        string hostname = "test.netty.io";
        string ns0Name = "ns0." + domain + '.';
        string ns1Name = "ns1." + domain + '.';
        string ns2Name = "ns2." + domain + '.';

        IPEndPoint ns0Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 1, 0, 1 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        IPEndPoint ns1Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 1 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        IPEndPoint ns2Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 2 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        IPEndPoint ns3Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 3 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        IPEndPoint ns4Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 4 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        IPEndPoint ns5Address = new IPEndPoint(
                new IPAddress(new byte[] { 10, 0, 0, 5 }),
                DefaultDnsServerAddressStreamProvider.DNS_PORT);
        Dictionary<IPEndPoint, string> hostNames = new Dictionary<IPEndPoint, string>
        {
            [ns0Address] = ns0Name, [ns1Address] = ns1Name, [ns2Address] = ns1Name, [ns3Address] = ns1Name,
            [ns4Address] = ns1Name, [ns5Address] = ns2Name
        };
        TestDnsServer redirectServer = new NSRecordsFromCacheServer(
                new HashSet<string> { hostname, ns1Name }, hostname, domain, ns0Name, ns1Name, ns2Name,
                ns0Address, ns5Address, hostNames);
        redirectServer.Start();
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        ConcurrentQueue<EndPoint> cached = new ConcurrentQueue<EndPoint>();
        IAuthoritativeDnsServerCache authoritativeDnsServerCache =
                new CachedAddressesAuthoritativeDnsServerCache(cached);

        IEventLoop loop = group.Next();
        DefaultDnsCache cache = new DefaultDnsCache();
        cache.Cache(ns1Name, null, ns1Address.Address, 10000, loop);
        cache.Cache(ns1Name, null, ns2Address.Address, 10000, loop);
        cache.Cache(ns1Name, null, ns3Address.Address, 10000, loop);
        cache.Cache(ns1Name, null, ns4Address.Address, 10000, loop);

        AtomicReference<IDnsServerAddressStream> redirectedRef = new AtomicReference<IDnsServerAddressStream>();
        SingletonDnsServerAddressStreamProvider provider =
                new SingletonDnsServerAddressStreamProvider(redirectServer.LocalAddress());
        DnsNameResolver resolver = new RedirectStreamRecordingResolver(
                loop, new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel,
                null, false,
                cache, NoopDnsCnameCache.INSTANCE, authoritativeDnsServerCache, null,
                NoopDnsQueryLifecycleObserverFactory.INSTANCE, 2000, ResolvedAddressTypes.Ipv4Only,
                true, 10, true, 4096,
                false, IHostsFileEntriesResolver.Default,
                provider, new ThreadLocalNameServerAddressStream(provider),
                DnsNameResolver.DEFAULT_SEARCH_DOMAINS, 0, true, false, 0, strategy,
                false, redirectedRef);

        try
        {
            Task<IReadOnlyList<IPAddress>> future = resolver.ResolveAllAsync(hostname);
            await AwaitUninterruptibly(future);
            Exception cause = DnsNameResolver.Cause(future);
            Assert.IsAssignableFrom<UnknownHostException>(cause);
            IDnsServerAddressStream redirected = redirectedRef.Get();
            Assert.NotNull(redirected);
            Assert.Equal(6, redirected.Size());
            // We should not cache because of bailiwick check.
            Assert.Empty(cached);

            // The redirected addresses should have been retrieven from the DnsCache if not resolved, so these are
            // fully resolved.
            Assert.Equal<EndPoint>(ns0Address, redirected.Next());
            Assert.Equal<EndPoint>(ns1Address, redirected.Next());
            Assert.Equal<EndPoint>(ns2Address, redirected.Next());
            Assert.Equal<EndPoint>(ns3Address, redirected.Next());
            Assert.Equal<EndPoint>(ns4Address, redirected.Next());
            Assert.Equal<EndPoint>(ns5Address, redirected.Next());
        }
        finally
        {
            resolver.Dispose();
            _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            redirectServer.Stop();
        }
    }

    private sealed class NSRecordsFromCacheServer : TestDnsServer
    {
        private readonly string hostname;
        private readonly string domain;
        private readonly string ns0Name;
        private readonly string ns1Name;
        private readonly string ns2Name;
        private readonly IPEndPoint ns0Address;
        private readonly IPEndPoint ns5Address;
        private readonly IReadOnlyDictionary<IPEndPoint, string> hostNames;

        internal NSRecordsFromCacheServer(ISet<string> domains, string hostname, string domain, string ns0Name,
                                          string ns1Name, string ns2Name, IPEndPoint ns0Address,
                                          IPEndPoint ns5Address, IReadOnlyDictionary<IPEndPoint, string> hostNames)
            : base(domains)
        {
            this.hostname = hostname;
            this.domain = domain;
            this.ns0Name = ns0Name;
            this.ns1Name = ns1Name;
            this.ns2Name = ns2Name;
            this.ns0Address = ns0Address;
            this.ns5Address = ns5Address;
            this.hostNames = hostNames;
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            foreach (QuestionRecord record in message.GetQuestionRecords())
            {
                if (record.GetDomainName().Equals(hostname))
                {
                    message.GetAdditionalRecords().Clear();
                    message.GetAnswerRecords().Clear();
                    message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns0Name));
                    message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns1Name));
                    message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns2Name));

                    message.GetAdditionalRecords().Add(NewARecord(ns0Address));
                    message.GetAdditionalRecords().Add(NewARecord(ns5Address));

                    return message;
                }
            }

            return message;
        }

        private IResourceRecord NewARecord(IPEndPoint address)
        {
            return NewARecord(hostNames[address], address.Address.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNsLoopFailsResolveWithAuthoritativeDnsServerCache(DnsNameResolverChannelStrategy strategy)
    {
        await TestNsLoopFailsResolve(strategy, new DefaultAuthoritativeDnsServerCache());
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNsLoopFailsResolveWithoutAuthoritativeDnsServerCache(DnsNameResolverChannelStrategy strategy)
    {
        await TestNsLoopFailsResolve(strategy, NoopAuthoritativeDnsServerCache.INSTANCE);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestRRNameContainsDifferentSearchDomainNoDomains(DnsNameResolverChannelStrategy strategy)
    {
        await Assert.ThrowsAnyAsync<UnknownHostException>(
                () => TestRRNameContainsDifferentSearchDomain(strategy, new List<string>(), "netty"));
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestRRNameContainsDifferentSearchDomainEmptyExtraDomain(DnsNameResolverChannelStrategy strategy)
    {
        await TestRRNameContainsDifferentSearchDomain(strategy, new List<string> { "io", "" }, "netty");
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestRRNameContainsDifferentSearchDomainSingleExtraDomain(DnsNameResolverChannelStrategy strategy)
    {
        await TestRRNameContainsDifferentSearchDomain(strategy, new List<string> { "io", "foo.dom" }, "netty");
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestRRNameContainsDifferentSearchDomainMultiExtraDomains(DnsNameResolverChannelStrategy strategy)
    {
        await TestRRNameContainsDifferentSearchDomain(strategy, new List<string> { "com", "foo.dom", "bar.dom" },
                "google");
    }

    // CLR: the lambda RecordStore is the nested SearchDomainRecordStore (a C# lambda does not convert to an
    // interface); ThreadLocalRandom.current().nextInt(1, 10) is Random.Shared.Next(1, 10) (resolver-dns mapping; both
    // exclude the upper bound). assertThat(address.getHostName()).startsWith(unresolved) has no counterpart: the JDK
    // InetAddress carries the host name the resolver decoded it for, IPAddress carries none (infra's
    // DnsAddressDecoder drops the name, infra notes "Host names"), so only the address prefix is asserted.
    // assertThat(addresses.size()).isGreaterThan(0) is Assert.True(addresses.Count > 0) (as infra's ResolvConfTest).
    private static async Task TestRRNameContainsDifferentSearchDomain(DnsNameResolverChannelStrategy strategy,
                                                                IReadOnlyList<string> searchDomains,
                                                                string unresolved)
    {
        string ipAddrPrefix = "1.2.3.";
        TestDnsServer searchDomainServer = new TestDnsServer(new SearchDomainRecordStore(searchDomains, ipAddrPrefix));
        searchDomainServer.Start();

        DnsNameResolver resolver = NewResolver(strategy, false, null, searchDomainServer)
                .SearchDomains(searchDomains)
                .Build();

        try
        {
            IReadOnlyList<IPAddress> addresses = await resolver.ResolveAllAsync(unresolved);
            Assert.True(addresses.Count > 0);

            foreach (IPAddress address in addresses)
            {
                Assert.StartsWith(ipAddrPrefix, address.ToString());
            }
        }
        finally
        {
            resolver.Dispose();
            searchDomainServer.Stop();
        }
    }

    private sealed class SearchDomainRecordStore : IRecordStore
    {
        private readonly IReadOnlyList<string> searchDomains;
        private readonly string ipAddrPrefix;

        internal SearchDomainRecordStore(IReadOnlyList<string> searchDomains, string ipAddrPrefix)
        {
            this.searchDomains = searchDomains;
            this.ipAddrPrefix = ipAddrPrefix;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord questionRecord)
        {
            ISet<IResourceRecord> records = new HashSet<IResourceRecord>(searchDomains.Count);
            string qName = questionRecord.GetDomainName();

            foreach (string searchDomain in searchDomains)
            {
                if (qName.EndsWith(searchDomain, StringComparison.Ordinal))
                {
                    continue;
                }

                IResourceRecord rr = NewARecord(qName + '.' + searchDomain,
                        ipAddrPrefix + Random.Shared.Next(1, 10).ToString(CultureInfo.InvariantCulture));
                logger.Info("Adding A record: " + rr);
                records.Add(rr);
            }

            return records;
        }
    }

    private async Task TestNsLoopFailsResolve(DnsNameResolverChannelStrategy strategy,
                                              IAuthoritativeDnsServerCache authoritativeDnsServerCache)
    {
        string domain = "netty.io";
        string ns1Name = "ns1." + domain;
        string ns2Name = "ns2." + domain;

        TestDnsServer testDnsServer = new NsLoopServer(new HashSet<string> { domain }, domain, ns1Name, ns2Name);
        testDnsServer.Start();
        DnsNameResolverBuilder builder = NewResolver(strategy);

        DnsNameResolver resolver = builder.ResolveCache(NoopDnsCache.INSTANCE)
                .AuthoritativeDnsServerCache(authoritativeDnsServerCache)
                .NameServerProvider(new SingletonDnsServerAddressStreamProvider(testDnsServer.LocalAddress())).Build();

        try
        {
            Task<IPAddress> resolveFuture = resolver.ResolveAsync(domain);
            await AwaitUninterruptibly(resolveFuture);
            Assert.IsAssignableFrom<UnknownHostException>(DnsNameResolver.Cause(resolveFuture));
            Task<IReadOnlyList<IPAddress>> resolveAllFuture = resolver.ResolveAllAsync(domain);
            await AwaitUninterruptibly(resolveAllFuture);
            Assert.IsAssignableFrom<UnknownHostException>(DnsNameResolver.Cause(resolveAllFuture));
        }
        finally
        {
            resolver.Dispose();
            testDnsServer.Stop();
        }
    }

    private sealed class NsLoopServer : TestDnsServer
    {
        private readonly string domain;
        private readonly string ns1Name;
        private readonly string ns2Name;

        internal NsLoopServer(ISet<string> domains, string domain, string ns1Name, string ns2Name) : base(domains)
        {
            this.domain = domain;
            this.ns1Name = ns1Name;
            this.ns2Name = ns2Name;
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            // Just always return NS records only without any additional records (glue records).
            // Because of this the resolver will never be able to resolve and so fail eventually at some
            // point.
            foreach (QuestionRecord record in message.GetQuestionRecords())
            {
                if (record.GetDomainName().Equals(domain))
                {
                    message.GetAdditionalRecords().Clear();
                    message.GetAnswerRecords().Clear();
                    message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns1Name));
                    message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(domain, ns2Name));
                }
            }

            return message;
        }
    }

    // CLR: the anonymous DnsCache is the nested NotCachingDnsCache (fail(...) is Assert.Fail and
    // Collections.emptyList() ImmutableList.Empty). authoritativeDnsServerCache(DnsCache) is @Deprecated and not
    // ported (core notes, Not-ported rows: "use.AuthoritativeDnsServerCache(new
    // AuthoritativeDnsServerCacheAdapter(cache)), the deprecated method's own body"), so the adapter is built
    // here. new InetSocketAddress(NetUtil.LOCALHOST, 12345) is an IPEndPoint. resolve(...).awaitUninterruptibly()
    // is AwaitUninterruptibly, cause() DnsNameResolver.Cause and getCause() InnerException. The wait observes
    // TestContext.Current.CancellationToken (WaitAsync), so the @Timeout ends it promptly (xUnit1069).
    [Theory(Timeout = 3000)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTimeoutNotCached(DnsNameResolverChannelStrategy strategy)
    {
        IDnsCache cache = new NotCachingDnsCache();
        DnsNameResolverBuilder builder = NewResolver(strategy);
        builder.QueryTimeoutMillis(100)
                .AuthoritativeDnsServerCache(new AuthoritativeDnsServerCacheAdapter(cache))
                .ResolveCache(cache)
                .NameServerProvider(new SingletonDnsServerAddressStreamProvider(
                        new IPEndPoint(NetUtil.LOCALHOST, 12345)));
        DnsNameResolver resolver = builder.Build();
        Task<IPAddress> result = resolver.ResolveAsync("doesnotexist.netty.io");
        await AwaitUninterruptibly(result).WaitAsync(TestContext.Current.CancellationToken);
        Exception cause = DnsNameResolver.Cause(result);
        Assert.IsAssignableFrom<UnknownHostException>(cause);
        Assert.IsAssignableFrom<DnsNameResolverTimeoutException>(cause.InnerException);
        Assert.True(DnsNameResolver.IsTimeoutError(cause));
        Assert.True(DnsNameResolver.IsTransportOrTimeoutError(cause));
        resolver.Dispose();
    }

    private sealed class NotCachingDnsCache : IDnsCache
    {
        public void Clear()
        {
            // NOOP
        }

        public bool Clear(string hostname)
        {
            return false;
        }

        public IReadOnlyList<IDnsCacheEntry> Get(string hostname, IDnsRecord[] additionals)
        {
            return ImmutableList<IDnsCacheEntry>.Empty;
        }

        public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additionals, IPAddress address,
                                    long originalTtl, IEventLoop loop)
        {
            Assert.Fail("Should not be cached");
            return null;
        }

        public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additionals, Exception cause, IEventLoop loop)
        {
            Assert.Fail("Should not be cached");
            return null;
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTimeoutIpv4PreferredA(DnsNameResolverChannelStrategy strategy)
    {
        await TestTimeoutOneQuery(strategy, ResolvedAddressTypes.Ipv4Preferred, RecordType.A, RecordType.AAAA);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTimeoutIpv4PreferredAAAA(DnsNameResolverChannelStrategy strategy)
    {
        await TestTimeoutOneQuery(strategy, ResolvedAddressTypes.Ipv4Preferred, RecordType.AAAA, RecordType.A);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTimeoutIpv6PreferredA(DnsNameResolverChannelStrategy strategy)
    {
        await TestTimeoutOneQuery(strategy, ResolvedAddressTypes.Ipv6Preferred, RecordType.A, RecordType.AAAA);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTimeoutIpv6PreferredAAAA(DnsNameResolverChannelStrategy strategy)
    {
        await TestTimeoutOneQuery(strategy, ResolvedAddressTypes.Ipv6Preferred, RecordType.AAAA, RecordType.A);
    }

    // CLR: the anonymous RecordStore is the nested TimeoutOneQueryStore; LinkedHashSet is Common's, toLowerCase()
    // (the default locale) is ToLower(CultureInfo.CurrentCulture) as in TestDnsServer;
    // assertTrue(list.contains(x)) is Assert.Contains (xUnit2017) and assertEquals(1, size()) is Assert.Single
    // (xUnit2013).
    private static async Task TestTimeoutOneQuery(DnsNameResolverChannelStrategy strategy, ResolvedAddressTypes type,
                                                  RecordType recordType, RecordType dropType)
    {

        TestDnsServer dnsServer2 = new TestDnsServer(new TimeoutOneQueryStore(recordType));
        dnsServer2.Start(dropType);
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(true)
                    .QueryTimeoutMillis(500)
                    .ResolvedAddressTypes(type)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));

            resolver = builder.Build();
            IReadOnlyList<IPAddress> resolvedAddresses =
                    await resolver.ResolveAllAsync("somehost.netty.io");
            Assert.Single(resolvedAddresses);

            if (recordType == RecordType.A)
            {
                Assert.Contains(new IPAddress(new byte[] { 10, 0, 0, 2 }), resolvedAddresses);
            }
            else
            {
                Assert.Contains(new IPAddress(
                        new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }), resolvedAddresses);
            }
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class TimeoutOneQueryStore : IRecordStore
    {
        private readonly RecordType recordType;

        internal TimeoutOneQueryStore(RecordType recordType)
        {
            this.recordType = recordType;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            ISet<IResourceRecord> records = new LinkedHashSet<IResourceRecord>(2);
            IDictionary<string, object> map1 = new Dictionary<string, object>();

            if (question.GetRecordType() == RecordType.A)
            {
                map1[DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = "10.0.0.2";
            }
            else
            {
                map1[DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = "::1";
            }

            records.Add(new TestDnsServer.TestResourceRecord(
                    question.GetDomainName(), recordType, map1));
            return records;
        }
    }

    // CLR: ChannelFactory<DatagramChannel> is Func<IDatagramChannel> and new ReflectiveChannelFactory<>(class) its
    // NewChannel method group (core notes, Channels); two delegates are equal when they bind the same method of the
    // same target (Delegate.Equals), so the assertions still tell the two factories apart. datagramChannelFactory()
    // is the internal DatagramChannelFactory() getter.
    [Fact]
    public void TestDnsNameResolverBuilderCopy()
    {
        Func<IDatagramChannel> channelFactory =
                new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel;
        DnsNameResolverBuilder builder = new DnsNameResolverBuilder(group.Next())
                .DatagramChannelFactory(channelFactory);
        DnsNameResolverBuilder copiedBuilder = builder.Copy();

        // change channel factory does not propagate to previously made copy
        Func<IDatagramChannel> newChannelFactory =
                new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel;
        builder.DatagramChannelFactory(newChannelFactory);
        Assert.Equal(channelFactory, copiedBuilder.DatagramChannelFactory());
        Assert.Equal(newChannelFactory, builder.DatagramChannelFactory());
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestFollowCNAMEEvenIfARecordIsPresent(DnsNameResolverChannelStrategy strategy)
    {
        TestDnsServer dnsServer2 = new TestDnsServer(new FollowCNAMEEvenIfARecordIsPresentStore());
        dnsServer2.Start();
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(true)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));

            resolver = builder.Build();
            IReadOnlyList<IPAddress> resolvedAddresses =
                    await resolver.ResolveAllAsync("somehost.netty.io");
            Assert.Equal(2, resolvedAddresses.Count);
            Assert.Contains(new IPAddress(new byte[] { 10, 0, 0, 99 }), resolvedAddresses);
            Assert.Contains(new IPAddress(new byte[] { 10, 0, 0, 2 }), resolvedAddresses);
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class FollowCNAMEEvenIfARecordIsPresentStore : IRecordStore
    {
        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            if (question.GetDomainName().Equals("cname.netty.io"))
            {
                IDictionary<string, object> map1 = new Dictionary<string, object>();
                map1[DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = "10.0.0.99";
                return ImmutableHashSet.Create<IResourceRecord>(
                        new TestDnsServer.TestResourceRecord(question.GetDomainName(), RecordType.A, map1));
            }
            else
            {
                ISet<IResourceRecord> records = new LinkedHashSet<IResourceRecord>(2);
                IDictionary<string, object> map = new Dictionary<string, object>();
                map[DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture)] = "cname.netty.io";
                records.Add(new TestDnsServer.TestResourceRecord(
                        question.GetDomainName(), RecordType.CNAME, map));

                IDictionary<string, object> map1 = new Dictionary<string, object>();
                map1[DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = "10.0.0.2";
                records.Add(new TestDnsServer.TestResourceRecord(
                        question.GetDomainName(), RecordType.A, map1));
                return records;
            }
        }
    }

    //
    // This should only result in one query.
    // ;; ANSWER SECTION:
    // somehost.netty.io.     594    IN    CNAME    cname.netty.io.
    // cname.netty.io.        9042   IN    CNAME    cname2.netty.io.
    // cname2.netty.io.       1312   IN    CNAME    cname3.netty.io.io.
    // cname3.netty.io.       20     IN    A        10.0.0.2
    // CLR: the anonymous RecordStore is the nested CNAMEFollowInResponseStore and AtomicInteger is Common's.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNAMEFollowInResponseWithoutExtraQuery(DnsNameResolverChannelStrategy strategy)
    {
        AtomicInteger queryCount = new AtomicInteger();
        TestDnsServer dnsServer2 = new TestDnsServer(new CNAMEFollowInResponseStore(queryCount));
        dnsServer2.Start();
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(true)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));

            resolver = builder.Build();
            IReadOnlyList<IPAddress> resolvedAddresses =
                    await resolver.ResolveAllAsync("somehost.netty.io");
            Assert.Single(resolvedAddresses);
            Assert.Contains(new IPAddress(new byte[] { 10, 0, 0, 2 }), resolvedAddresses);
            Assert.Equal(1, queryCount.Get());
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class CNAMEFollowInResponseStore : IRecordStore
    {
        private readonly AtomicInteger queryCount;

        internal CNAMEFollowInResponseStore(AtomicInteger queryCount)
        {
            this.queryCount = queryCount;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            queryCount.IncrementAndGet();

            if (question.GetDomainName().Equals("somehost.netty.io"))
            {
                ISet<IResourceRecord> records = new LinkedHashSet<IResourceRecord>(2);
                IDictionary<string, object> map = new Dictionary<string, object>();
                map[DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture)] = "cname.netty.io";
                records.Add(new TestDnsServer.TestResourceRecord(
                        question.GetDomainName(), RecordType.CNAME, map));

                map = new Dictionary<string, object>();
                map[DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture)] = "cname2.netty.io";
                records.Add(new TestDnsServer.TestResourceRecord(
                        "cname.netty.io", RecordType.CNAME, map));

                map = new Dictionary<string, object>();
                map[DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture)] = "cname3.netty.io";
                records.Add(new TestDnsServer.TestResourceRecord(
                        "cname2.netty.io", RecordType.CNAME, map));

                IDictionary<string, object> map1 = new Dictionary<string, object>();
                map1[DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = "10.0.0.2";
                records.Add(new TestDnsServer.TestResourceRecord(
                       "cname3.netty.io", RecordType.A, map1));
                return records;
            }

            return null;
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestFollowCNAMELoop(DnsNameResolverChannelStrategy strategy)
    {
        TestDnsServer dnsServer2 = new TestDnsServer(new FollowCNAMELoopStore());
        dnsServer2.Start();
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(false)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));

            resolver = builder.Build();
            DnsNameResolver finalResolver = resolver;
            await Assert.ThrowsAnyAsync<UnknownHostException>(
                    () => finalResolver.ResolveAllAsync("somehost.netty.io"));
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class FollowCNAMELoopStore : IRecordStore
    {
        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            ISet<IResourceRecord> records = new LinkedHashSet<IResourceRecord>(4);

            records.Add(new TestDnsServer.TestResourceRecord("x." + question.GetDomainName(),
                    RecordType.A, ImmutableDictionary<string, object>.Empty.Add(
                            DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture), "10.0.0.99")));
            records.Add(new TestDnsServer.TestResourceRecord(
                    "cname2.netty.io", RecordType.CNAME,
                    ImmutableDictionary<string, object>.Empty.Add(
                            DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture), "cname.netty.io")));
            records.Add(new TestDnsServer.TestResourceRecord(
                    "cname.netty.io", RecordType.CNAME,
                    ImmutableDictionary<string, object>.Empty.Add(
                            DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture), "cname2.netty.io")));
            records.Add(new TestDnsServer.TestResourceRecord(
                    question.GetDomainName(), RecordType.CNAME,
                    ImmutableDictionary<string, object>.Empty.Add(
                            DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture), "cname.netty.io")));
            return records;
        }
    }

    // CLR: resolver.cnameCache().cache(..., resolver.executor()) is CnameCache().Cache(..., resolver.Executor) and
    // Long.MAX_VALUE long.MaxValue; assertThrows is Assert.ThrowsAnyAsync (as above).
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNAMELoopInCache(DnsNameResolverChannelStrategy strategy)
    {
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(false)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer.LocalAddress()));

            resolver = builder.Build();
            // Add a CNAME loop into the cache
            string name = "somehost.netty.io.";
            string name2 = "cname.netty.io.";

            resolver.CnameCache().Cache(name, name2, long.MaxValue, resolver.Executor);
            resolver.CnameCache().Cache(name2, name, long.MaxValue, resolver.Executor);
            DnsNameResolver finalResolver = resolver;
            await Assert.ThrowsAnyAsync<UnknownHostException>(() => finalResolver.ResolveAsync(name));
        }
        finally
        {
            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestSearchDomainQueryFailureForSingleAddressTypeCompletes(
            DnsNameResolverChannelStrategy strategy)
    {
        await Assert.ThrowsAnyAsync<UnknownHostException>(
                () => TestSearchDomainQueryFailureCompletes(strategy, ResolvedAddressTypes.Ipv4Only));
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestSearchDomainQueryFailureForMultipleAddressTypeCompletes(
            DnsNameResolverChannelStrategy strategy)
    {
        await Assert.ThrowsAnyAsync<UnknownHostException>(
                () => TestSearchDomainQueryFailureCompletes(strategy, ResolvedAddressTypes.Ipv4Preferred));
    }

    // CLR: singletonList(".") is a one-element list; resolve(...).syncUninterruptibly() is the awaited Task, so the
    // UnknownHostException reaches the caller's Assert.ThrowsAnyAsync.
    private async Task TestSearchDomainQueryFailureCompletes(
            DnsNameResolverChannelStrategy strategy, ResolvedAddressTypes types)
    {
        DnsNameResolver resolver = NewResolver(strategy)
                .ResolvedAddressTypes(types)
                .Ndots(1)
                .SearchDomains(new List<string> { "." }).Build();

        try
        {
            await resolver.ResolveAsync("invalid.com");
        }
        finally
        {
            resolver.Dispose();
        }
    }

    // CLR: each CountDownLatch(1) is a TaskCompletionSource completed by the cache's clear() (countDown) and awaited
    // (await, through WaitAsync(TestContext.Current.CancellationToken) so the @Timeout ends it promptly, xUnit1069);
    // the two anonymous DnsCache classes are the nested ClearLatchDnsCache. authoritativeDnsServerCache(DnsCache) is
    // @Deprecated and not ported, so the adapter is built here (core notes, as testTimeoutNotCached).
    [Theory(Timeout = 2000)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCachesClearedOnClose(DnsNameResolverChannelStrategy strategy)
    {
        TaskCompletionSource resolveLatch =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource authoritativeLatch =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        DnsNameResolver resolver = NewResolver(strategy).ResolveCache(new ClearLatchDnsCache(resolveLatch))
                .AuthoritativeDnsServerCache(
                        new AuthoritativeDnsServerCacheAdapter(new ClearLatchDnsCache(authoritativeLatch))).Build();

        resolver.Dispose();
        await resolveLatch.Task.WaitAsync(TestContext.Current.CancellationToken);
        await authoritativeLatch.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    private sealed class ClearLatchDnsCache : IDnsCache
    {
        private readonly TaskCompletionSource latch;

        internal ClearLatchDnsCache(TaskCompletionSource latch)
        {
            this.latch = latch;
        }

        public void Clear()
        {
            latch.TrySetResult();
        }

        public bool Clear(string hostname)
        {
            return false;
        }

        public IReadOnlyList<IDnsCacheEntry> Get(string hostname, IDnsRecord[] additionals)
        {
            return null;
        }

        public IDnsCacheEntry Cache(
                string hostname, IDnsRecord[] additionals, IPAddress address, long originalTtl, IEventLoop loop)
        {
            return null;
        }

        public IDnsCacheEntry Cache(
                string hostname, IDnsRecord[] additionals, Exception cause, IEventLoop loop)
        {
            return null;
        }
    }

    // CLR: DOMAINS.iterator().next() takes the first element of a HashSet: with Java's fixed String.hashCode that is
    // always "google.co.in", while the BCL string hash "may" differ between "two subsequent runs of the same program"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0), so the first element of
    // the ImmutableHashSet changes per run and could be "localhost", which the resolver answers from the hosts file
    // and never caches; the test takes the first element that is not "localhost". assertEquals(1, cached.size()) is
    // Assert.Single (xUnit2013) and List.equals the element-wise Assert.Equal.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveACachedWithDot(DnsNameResolverChannelStrategy strategy)
    {
        IDnsCache cache = new DefaultDnsCache();
        DnsNameResolver resolver = NewResolver(strategy, ResolvedAddressTypes.Ipv4Only)
                .ResolveCache(cache).Build();

        try
        {
            string domain = DOMAINS.First(d => d != "localhost");
            string domainWithDot = domain + '.';

            await resolver.ResolveAsync(domain);
            IReadOnlyList<IDnsCacheEntry> cached = cache.Get(domain, null);
            IReadOnlyList<IDnsCacheEntry> cached2 = cache.Get(domainWithDot, null);

            Assert.Single(cached);
            Assert.Equal(cached, cached2);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    // CLR: Collections.singleton/singletonList are ImmutableHashSet.Create and a one-element list;
    // doResolveAllCached and the searchDomains()/ndots()/ resolvedInternetProtocolFamiliesUnsafe() accessors are
    // internal. HashMap.get is GetValueOrDefault; assertFalse(entries.isEmpty()) is Assert.NotEmpty and
    // assertEquals(1, size()) Assert.Single (xUnit2013).
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveACachedWithDotSearchDomain(DnsNameResolverChannelStrategy strategy)
    {
        TestDnsCache cache = new TestDnsCache(new DefaultDnsCache());
        TestDnsServer server = new TestDnsServer(ImmutableHashSet.Create("test.netty.io"));
        server.Start();
        DnsNameResolver resolver = NewResolver(strategy, ResolvedAddressTypes.Ipv4Only)
                .SearchDomains(new List<string> { "netty.io" })
                .NameServerProvider(new SingletonDnsServerAddressStreamProvider(server.LocalAddress()))
                .ResolveCache(cache).Build();

        try
        {
            await resolver.ResolveAsync("test");

            Assert.Null(cache.cacheHits.GetValueOrDefault("test.netty.io"));

            IReadOnlyList<IDnsCacheEntry> cached = cache.cache.Get("test.netty.io", null);
            IReadOnlyList<IDnsCacheEntry> cached2 = cache.cache.Get("test.netty.io.", null);
            Assert.Single(cached);
            Assert.Equal(cached, cached2);

            TaskCompletionSource<IReadOnlyList<IPAddress>> promise =
                    new TaskCompletionSource<IReadOnlyList<IPAddress>>();
            bool isCached = DnsNameResolver.DoResolveAllCached("test", null, promise, cache,
                    resolver.SearchDomains(), resolver.Ndots(), resolver.ResolvedInternetProtocolFamiliesUnsafe());
            Assert.True(isCached);
            await promise.Task;

            IReadOnlyList<IDnsCacheEntry> entries = cache.cacheHits.GetValueOrDefault("test.netty.io");
            Assert.NotEmpty(entries);
        }
        finally
        {
            resolver.Dispose();
            server.Stop();
        }
    }

    // CLR: ConcurrentHashMap is ConcurrentDictionary (get is GetValueOrDefault, put the indexer), AtomicInteger is
    // Common's, the anonymous RecordStore and DnsCnameCache are the nested CNameCachedStore and AssertingCnameCache;
    // assertTrue(cond, message) keeps its message (XunitAssertMessages) and endsWith(".") is EndsWith('.') (ordinal).
    // assertTrue(list.contains(x)) is Assert.Contains (xUnit2017) and assertEquals(1, size()) Assert.Single
    // (xUnit2013).
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNameCached(DnsNameResolverChannelStrategy strategy)
    {
        ConcurrentDictionary<string, string> cache = new ConcurrentDictionary<string, string>();
        AtomicInteger cnameQueries = new AtomicInteger();
        AtomicInteger aQueries = new AtomicInteger();

        TestDnsServer dnsServer2 = new TestDnsServer(new CNameCachedStore(cnameQueries, aQueries));
        dnsServer2.Start();
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(true)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()))
                    .ResolveCache(NoopDnsCache.INSTANCE)
                    .CnameCache(new AssertingCnameCache(cache));
            resolver = builder.Build();
            IReadOnlyList<IPAddress> resolvedAddresses =
                    await resolver.ResolveAllAsync("x.netty.io");
            Assert.Single(resolvedAddresses);
            Assert.Contains(new IPAddress(new byte[] { 10, 0, 0, 99 }), resolvedAddresses);

            Assert.Equal("cname.netty.io.", cache.GetValueOrDefault("x.netty.io."));
            Assert.Equal(1, cnameQueries.Get());
            Assert.Equal(1, aQueries.Get());

            resolvedAddresses =
                    await resolver.ResolveAllAsync("x.netty.io");
            Assert.Single(resolvedAddresses);
            Assert.Contains(new IPAddress(new byte[] { 10, 0, 0, 99 }), resolvedAddresses);

            // Should not have queried for the CNAME again.
            Assert.Equal(1, cnameQueries.Get());
            Assert.Equal(2, aQueries.Get());

            resolvedAddresses =
                    await resolver.ResolveAllAsync("y.netty.io");
            Assert.Single(resolvedAddresses);
            Assert.Contains(new IPAddress(new byte[] { 10, 0, 0, 99 }), resolvedAddresses);

            Assert.Equal("x.netty.io.", cache.GetValueOrDefault("y.netty.io."));

            // Will only query for one CNAME
            Assert.Equal(2, cnameQueries.Get());
            Assert.Equal(3, aQueries.Get());

            resolvedAddresses =
                    await resolver.ResolveAllAsync("y.netty.io");
            Assert.Single(resolvedAddresses);
            Assert.Contains(new IPAddress(new byte[] { 10, 0, 0, 99 }), resolvedAddresses);

            // Should not have queried for the CNAME again.
            Assert.Equal(2, cnameQueries.Get());
            Assert.Equal(4, aQueries.Get());
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class CNameCachedStore : IRecordStore
    {
        private readonly AtomicInteger cnameQueries;
        private readonly AtomicInteger aQueries;

        internal CNameCachedStore(AtomicInteger cnameQueries, AtomicInteger aQueries)
        {
            this.cnameQueries = cnameQueries;
            this.aQueries = aQueries;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            if ("cname.netty.io".Equals(question.GetDomainName()))
            {
                aQueries.IncrementAndGet();

                return ImmutableHashSet.Create<IResourceRecord>(new TestDnsServer.TestResourceRecord(
                        question.GetDomainName(), RecordType.A,
                        ImmutableDictionary<string, object>.Empty.Add(
                                DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture), "10.0.0.99")));
            }

            if ("x.netty.io".Equals(question.GetDomainName()))
            {
                cnameQueries.IncrementAndGet();

                return ImmutableHashSet.Create<IResourceRecord>(new TestDnsServer.TestResourceRecord(
                        question.GetDomainName(), RecordType.CNAME,
                        ImmutableDictionary<string, object>.Empty.Add(
                                DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture), "cname.netty.io")));
            }

            if ("y.netty.io".Equals(question.GetDomainName()))
            {
                cnameQueries.IncrementAndGet();

                return ImmutableHashSet.Create<IResourceRecord>(new TestDnsServer.TestResourceRecord(
                        question.GetDomainName(), RecordType.CNAME,
                        ImmutableDictionary<string, object>.Empty.Add(
                                DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture), "x.netty.io")));
            }

            return ImmutableHashSet<IResourceRecord>.Empty;
        }
    }

    private sealed class AssertingCnameCache : IDnsCnameCache
    {
        private readonly ConcurrentDictionary<string, string> cache;

        internal AssertingCnameCache(ConcurrentDictionary<string, string> cache)
        {
            this.cache = cache;
        }

        public string Get(string hostname)
        {
            Assert.True(hostname.EndsWith('.'), hostname);

            return cache.GetValueOrDefault(hostname);
        }

        public void Cache(string hostname, string cname, long originalTtl, IEventLoop loop)
        {
            Assert.True(hostname.EndsWith('.'), hostname);
            cache[hostname] = cname;
        }

        public void Clear()
        {
            // NOOP
        }

        public bool Clear(string hostname)
        {
            return false;
        }
    }

    // CLR: the lambda RecordStore and the anonymous DnsCnameCache are the nested CnameCacheBailiwickStore and
    // MapCnameCache; new HashSet<>() is a HashSet, Collections.singletonMap(k, v) ImmutableDictionary.Empty.Add(k,
    // v), ConcurrentHashMap ConcurrentDictionary and the local dnsServer hides the static field, as in the source.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCnameCacheBailiwick(DnsNameResolverChannelStrategy strategy)
    {
        ConcurrentDictionary<string, string> cache = new ConcurrentDictionary<string, string>();

        TestDnsServer dnsServer = new TestDnsServer(new CnameCacheBailiwickStore());
        dnsServer.Start();
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(true)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer.LocalAddress()))
                    .ResolveCache(NoopDnsCache.INSTANCE)
                    .CnameCache(new MapCnameCache(cache));
            resolver = builder.Build();
            await resolver.ResolveAllAsync("x.netty.io");

            // The CNAME for x.netty.io should be cached because it was the queried name
            Assert.Equal("cname.netty.io.", cache.GetValueOrDefault("x.netty.io."));
            // The CNAME for cname.netty.io should NOT be cached because it is out of bailiwick for x.netty.io
            Assert.Null(cache.GetValueOrDefault("cname.netty.io."));
        }
        finally
        {
            dnsServer.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class CnameCacheBailiwickStore : IRecordStore
    {
        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            if ("x.netty.io".Equals(question.GetDomainName()))
            {
                ISet<IResourceRecord> records = new HashSet<IResourceRecord>();
                // Valid CNAME (in bailiwick of query)
                records.Add(new TestDnsServer.TestResourceRecord(
                        "x.netty.io", RecordType.CNAME,
                        ImmutableDictionary<string, object>.Empty.Add(
                                DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture), "cname.netty.io")));
                // Invalid CNAME (out of bailiwick of query)
                records.Add(new TestDnsServer.TestResourceRecord(
                        "cname.netty.io", RecordType.CNAME,
                        ImmutableDictionary<string, object>.Empty.Add(
                                DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture), "evil.com")));
                // Provide an A record to satisfy the resolution
                records.Add(new TestDnsServer.TestResourceRecord(
                        "evil.com", RecordType.A,
                        ImmutableDictionary<string, object>.Empty.Add(
                                DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture), "10.0.0.99")));
                return records;
            }

            return ImmutableHashSet<IResourceRecord>.Empty;
        }
    }

    private sealed class MapCnameCache : IDnsCnameCache
    {
        private readonly ConcurrentDictionary<string, string> cache;

        internal MapCnameCache(ConcurrentDictionary<string, string> cache)
        {
            this.cache = cache;
        }

        public string Get(string hostname)
        {
            return cache.GetValueOrDefault(hostname);
        }

        public void Cache(string hostname, string cname, long originalTtl, IEventLoop loop)
        {
            cache[hostname] = cname;
        }

        public void Clear()
        {
        }

        public bool Clear(string hostname)
        {
            return false;
        }
    }

    // CLR: CLR-inapplicable (owner rule 2026-11-16: a test that only exercises a not-ported @Deprecated member). The
    // test only constructs a DnsNameResolver through the public 17-argument constructor, which is @Deprecated ("Use
    // {@link DnsNameResolverBuilder}") and not ported (core notes, Not-ported rows), with a null resolvedAddressTypes
    // (netty PR 8445); ResolvedAddressTypes is a C# enum, which cannot be null, so the constructor's
    // "resolvedAddressTypes != null ? resolvedAddressTypes : DEFAULT_RESOLVE_ADDRESS_TYPES" has no null branch
    // (DnsNameResolver.cs). The body is kept, compiled and skipped, with the source's argument comments beside the values
    // the deprecated constructor forwards to the package-private one (a null socket channel factory, no TCP retry,
    // NoopDnsCnameCache.INSTANCE, no local address, a ThreadLocalNameServerAddressStream over the provider,
    // completeOncePreferredResolved false, maxNumConsolidation 0 and ChannelPerResolver); close() is Dispose().
    [Fact(Skip = "CLR-inapplicable: the @Deprecated DnsNameResolver constructor is not ported and ResolvedAddressTypes " +
                 "cannot be null")]
    public void TestInstanceWithNullPreferredAddressType()
    {
        IDnsServerAddressStreamProvider dnsServerAddressStreamProvider =
                DnsServerAddressStreamProviders.PlatformDefault();
        new DnsNameResolver(
                group.Next(), // eventLoop
                new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel, // channelFactory
                null, false,
                NoopDnsCache.INSTANCE, // resolveCache
                NoopDnsCnameCache.INSTANCE,
                NoopAuthoritativeDnsServerCache.INSTANCE, // authoritativeDnsServerCache
                null,
                NoopDnsQueryLifecycleObserverFactory.INSTANCE, // dnsQueryLifecycleObserverFactory
                100, // queryTimeoutMillis
                DnsNameResolver.DEFAULT_RESOLVE_ADDRESS_TYPES, // resolvedAddressTypes, see https://github.com/netty/netty/pull/8445
                true, // recursionDesired
                1, // maxQueriesPerResolve
                false, // traceEnabled
                4096, // maxPayloadSize
                true, // optResourceEnabled
                IHostsFileEntriesResolver.Default, // hostsFileEntriesResolver
                dnsServerAddressStreamProvider, // dnsServerAddressStreamProvider
                new ThreadLocalNameServerAddressStream(dnsServerAddressStreamProvider),
                null, // searchDomains
                1, // ndots
                true, // decodeIdn
                false, 0, DnsNameResolverChannelStrategy.ChannelPerResolver
        ).Dispose();
    }

    // CLR: InetSocketAddress.createUnresolved(address.getHostString(), port) is a DnsEndPoint; getHostString() of an
    // IPEndPoint is its address text (infra's DefaultAuthoritativeDnsServerCache.GetHostString).
    private static DnsEndPoint Unresolved(IPEndPoint address)
    {
        return new DnsEndPoint(address.Address.ToString(), address.Port);
    }

    private static void Resolve(DnsNameResolver resolver, IDictionary<string, Task<IPAddress>> futures, string hostname)
    {
        futures[hostname] = resolver.ResolveAsync(hostname);
    }

    private static void QueryMx(
            DnsNameResolver resolver,
            IDictionary<string, Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>>> futures,
            string hostname)
    {
        futures[hostname] = resolver.QueryAsync(new DefaultDnsQuestion(hostname, DnsRecordType.MX));
    }

    private static void AssertNoQueriesMade(DnsNameResolver resolver)
    {
        TestRecursiveCacheDnsQueryLifecycleObserverFactory lifecycleObserverFactory =
                (TestRecursiveCacheDnsQueryLifecycleObserverFactory)resolver.DnsQueryLifecycleObserverFactory();
        Assert.True(lifecycleObserverFactory.observers.IsEmpty);
    }

    private static void AssertQueryObserver(DnsNameResolver resolver, DnsRecordType cancelledType)
    {
        TestRecursiveCacheDnsQueryLifecycleObserverFactory lifecycleObserverFactory =
                (TestRecursiveCacheDnsQueryLifecycleObserverFactory)resolver.DnsQueryLifecycleObserverFactory();
        TestDnsQueryLifecycleObserver observer;

        while (lifecycleObserverFactory.observers.TryDequeue(out observer))
        {
            observer.events.TryDequeue(out object o);

            if (o is QueryCancelledEvent)
            {
                Assert.Equal(cancelledType, observer.question.Type());
            }
            else if (o is QueryWrittenEvent)
            {
                observer.events.TryDequeue(out object succeeded);
                QuerySucceededEvent succeededEvent = (QuerySucceededEvent)succeeded;
            }
            else
            {
                Assert.Fail("unexpected event type: " + (o == null ? "null" : o.ToString()));
            }

            Assert.True(observer.events.IsEmpty);
        }
    }

    // CLR: the package-private class is internal (IsolatedDnsNameResolverTest casts to it); its observer type is
    // internal too, because an internal field cannot expose a private type (CS0052).
    internal sealed class TestRecursiveCacheDnsQueryLifecycleObserverFactory
            : IDnsQueryLifecycleObserverFactory
    {
        internal readonly ConcurrentQueue<TestDnsQueryLifecycleObserver> observers =
                new ConcurrentQueue<TestDnsQueryLifecycleObserver>();

        public IDnsQueryLifecycleObserver NewDnsQueryLifecycleObserver(IDnsQuestion question)
        {
            TestDnsQueryLifecycleObserver observer = new TestDnsQueryLifecycleObserver(question);
            observers.Enqueue(observer);
            return observer;
        }
    }

    private sealed class QueryWrittenEvent
    {
        internal readonly EndPoint dnsServerAddress;

        internal QueryWrittenEvent(EndPoint dnsServerAddress)
        {
            this.dnsServerAddress = dnsServerAddress;
        }
    }

    private sealed class QueryCancelledEvent
    {
        internal readonly int queriesRemaining;

        internal QueryCancelledEvent(int queriesRemaining)
        {
            this.queriesRemaining = queriesRemaining;
        }
    }

    private sealed class QueryRedirectedEvent
    {
        internal readonly IReadOnlyList<EndPoint> nameServers;

        internal QueryRedirectedEvent(IReadOnlyList<EndPoint> nameServers)
        {
            this.nameServers = nameServers;
        }
    }

    private sealed class QueryCnamedEvent
    {
        internal readonly IDnsQuestion question;

        internal QueryCnamedEvent(IDnsQuestion question)
        {
            this.question = question;
        }
    }

    private sealed class QueryNoAnswerEvent
    {
        internal readonly DnsResponseCode code;

        internal QueryNoAnswerEvent(DnsResponseCode code)
        {
            this.code = code;
        }
    }

    private sealed class QueryFailedEvent
    {
        internal readonly Exception cause;

        internal QueryFailedEvent(Exception cause)
        {
            this.cause = cause;
        }
    }

    private sealed class QuerySucceededEvent
    {
    }

    internal sealed class TestDnsQueryLifecycleObserver : IDnsQueryLifecycleObserver
    {
        internal readonly ConcurrentQueue<object> events = new ConcurrentQueue<object>();
        internal readonly IDnsQuestion question;

        internal TestDnsQueryLifecycleObserver(IDnsQuestion question)
        {
            this.question = question;
        }

        public void QueryWritten(EndPoint dnsServerAddress, Task future)
        {
            events.Enqueue(new QueryWrittenEvent(dnsServerAddress));
        }

        public void QueryCancelled(int queriesRemaining)
        {
            events.Enqueue(new QueryCancelledEvent(queriesRemaining));
        }

        public IDnsQueryLifecycleObserver QueryRedirected(IReadOnlyList<EndPoint> nameServers)
        {
            events.Enqueue(new QueryRedirectedEvent(nameServers));
            return this;
        }

        public IDnsQueryLifecycleObserver QueryCNAMEd(IDnsQuestion cnameQuestion)
        {
            events.Enqueue(new QueryCnamedEvent(cnameQuestion));
            return this;
        }

        public IDnsQueryLifecycleObserver QueryNoAnswer(DnsResponseCode code)
        {
            events.Enqueue(new QueryNoAnswerEvent(code));
            return this;
        }

        public void QueryFailed(Exception cause)
        {
            events.Enqueue(new QueryFailedEvent(cause));
        }

        public void QuerySucceed()
        {
            events.Enqueue(new QuerySucceededEvent());
        }
    }

    private sealed class TestAuthoritativeDnsServerCache : IAuthoritativeDnsServerCache
    {
        internal readonly IAuthoritativeDnsServerCache cache;
        internal readonly Dictionary<string, IDnsServerAddressStream> cacheHits =
                new Dictionary<string, IDnsServerAddressStream>();

        internal TestAuthoritativeDnsServerCache(IAuthoritativeDnsServerCache cache)
        {
            this.cache = cache;
        }

        public void Clear()
        {
            cache.Clear();
        }

        public bool Clear(string hostname)
        {
            return cache.Clear(hostname);
        }

        public IDnsServerAddressStream Get(string hostname)
        {
            IDnsServerAddressStream cached = cache.Get(hostname);
            if (cached != null)
            {
                cacheHits[hostname] = cached.Duplicate();
            }

            return cached;
        }

        public void Cache(string hostname, EndPoint address, long originalTtl, IEventLoop loop)
        {
            cache.Cache(hostname, address, originalTtl, loop);
        }
    }

    private sealed class TestDnsCache : IDnsCache
    {
        internal readonly IDnsCache cache;
        internal readonly Dictionary<string, IReadOnlyList<IDnsCacheEntry>> cacheHits =
                new Dictionary<string, IReadOnlyList<IDnsCacheEntry>>();

        internal TestDnsCache(IDnsCache cache)
        {
            this.cache = cache;
        }

        public void Clear()
        {
            cache.Clear();
        }

        public bool Clear(string hostname)
        {
            return cache.Clear(hostname);
        }

        public IReadOnlyList<IDnsCacheEntry> Get(string hostname, IDnsRecord[] additionals)
        {
            IReadOnlyList<IDnsCacheEntry> cached = cache.Get(hostname, additionals);
            cacheHits[hostname] = cached;
            return cached;
        }

        public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additionals, IPAddress address,
                                    long originalTtl, IEventLoop loop)
        {
            return cache.Cache(hostname, additionals, address, originalTtl, loop);
        }

        public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additionals, Exception cause, IEventLoop loop)
        {
            return cache.Cache(hostname, additionals, cause, loop);
        }
    }

    private class RedirectingTestDnsServer : TestDnsServer
    {

        private readonly string dnsAddress;
        private readonly ISet<string> domains;

        internal RedirectingTestDnsServer(ISet<string> domains, string dnsAddress) : base(domains)
        {
            this.domains = domains;
            this.dnsAddress = dnsAddress;
        }

        internal RedirectingTestDnsServer(string domain, string dnsAddress)
            : this(ImmutableHashSet.Create(domain), dnsAddress)
        {
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            // Clear the answers as we want to add our own stuff to test dns redirects.
            message.GetAnswerRecords().Clear();
            message.GetAuthorityRecords().Clear();
            message.GetAdditionalRecords().Clear();

            foreach (string domain in domains)
            {
                string name = domain;

                for (int i = 0 ;; i++)
                {
                    int idx = name.IndexOf('.');
                    if (idx <= 0)
                    {
                        break;
                    }

                    name = name.Substring(idx + 1); // skip the '.' as well.
                    string dnsName = "dns" + idx + '.' + domain;
                    message.GetAuthorityRecords().Add(NewNsRecord(name, dnsName));
                    message.GetAdditionalRecords().Add(NewARecord(dnsName, i == 0 ? dnsAddress : "1.2.3." + idx));

                    // Add an unresolved NS record (with no additionals as well)
                    message.GetAuthorityRecords().Add(NewNsRecord(name, "unresolved." + dnsName));
                }
            }

            return message;
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestQueryTxt(DnsNameResolverChannelStrategy strategy)
    {
        const string hostname = "txt.netty.io";
        const string txt1 = "some text";
        const string txt2 = "some more text";

        TestDnsServer server = new TestDnsServer(new QueryTxtRecordStore(hostname, txt1, txt2));
        server.Start();
        DnsNameResolver resolver = NewResolver(strategy, ResolvedAddressTypes.Ipv4Only)
                .NameServerProvider(new SingletonDnsServerAddressStreamProvider(server.LocalAddress()))
                .Build();

        try
        {
            IAddressedEnvelope<IDnsResponse, IPEndPoint> envelope = await resolver.QueryAsync(
                    new DefaultDnsQuestion(hostname, DnsRecordType.TXT));
            Assert.NotNull(envelope.Sender);

            IDnsResponse response = envelope.Content;
            Assert.NotNull(response);

            Assert.Equal(DnsResponseCode.NOERROR, response.Code());
            int count = response.Count(DnsSection.ANSWER);

            Assert.Equal(2, count);
            List<string> txts = new List<string>();

            for (int i = 0; i < 2; i++)
            {
                txts.AddRange(DecodeTxt(response.RecordAt<IDnsRecord>(DnsSection.ANSWER, i)));
            }

            Assert.Contains(txt1, txts);
            Assert.Contains(txt2, txts);
            envelope.Release();
        }
        finally
        {
            resolver.Dispose();
            server.Stop();
        }
    }

    private sealed class QueryTxtRecordStore : IRecordStore
    {
        private readonly string hostname;
        private readonly string txt1;
        private readonly string txt2;

        internal QueryTxtRecordStore(string hostname, string txt1, string txt2)
        {
            this.hostname = hostname;
            this.txt1 = txt1;
            this.txt2 = txt2;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            if (question.GetDomainName() == hostname)
            {
                IDictionary<string, object> map1 = new Dictionary<string, object>();
                map1[DnsAttribute.CHARACTER_STRING.ToLower(CultureInfo.CurrentCulture)] = txt1;

                IDictionary<string, object> map2 = new Dictionary<string, object>();
                map2[DnsAttribute.CHARACTER_STRING.ToLower(CultureInfo.CurrentCulture)] = txt2;

                ISet<IResourceRecord> records = new HashSet<IResourceRecord>();
                records.Add(new TestDnsServer.TestResourceRecord(question.GetDomainName(), RecordType.TXT, map1));
                records.Add(new TestDnsServer.TestResourceRecord(question.GetDomainName(), RecordType.TXT, map2));
                return records;
            }

            return ImmutableHashSet<IResourceRecord>.Empty;
        }
    }

    // CLR: getUnsignedByte(index) is the unsigned C# byte of GetByte and toString(index, length, UTF_8)
    // ToString(index, length, Encoding.UTF8).
    private static IList<string> DecodeTxt(IDnsRecord record)
    {
        if (!(record is IDnsRawRecord))
        {
            return ImmutableList<string>.Empty;
        }

        List<string> list = new List<string>();
        ByteBuf data = ((IDnsRawRecord)record).Content;
        int idx = data.ReaderIndex;
        int wIdx = data.WriterIndex;

        while (idx < wIdx)
        {
            int len = data.GetByte(idx++);
            list.Add(data.ToString(idx, len, Encoding.UTF8));
            idx += len;
        }

        return list;
    }

    // CLR: Collections.singletonList(InetAddress.getByAddress(name, bytes)) is a one-element IPAddress array: the JDK
    // compares InetAddress instances by their address bytes only ("Two instances of InetAddress represent the same IP
    // address if the length of the byte arrays returned by getAddress is the same for both, and each of the array
    // components is the same", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/Inet6Address.html),
    // so the host name the expected address carries is not part of the comparison, and IPAddress has none.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestNotIncludeDuplicates(DnsNameResolverChannelStrategy strategy)
    {
        const string name = "netty.io";
        const string ipv4Addr = "1.2.3.4";
        TestDnsServer dnsServer2 = new TestDnsServer(new NotIncludeDuplicatesRecordStore(name, ipv4Addr));
        dnsServer2.Start();
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(true)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));
            builder.ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only);

            resolver = builder.Build();
            IReadOnlyList<IPAddress> resolvedAddresses = await resolver.ResolveAllAsync(name);
            Assert.Equal(new[] { new IPAddress(new byte[] { 1, 2, 3, 4 }) },
                    resolvedAddresses);
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class NotIncludeDuplicatesRecordStore : IRecordStore
    {
        private readonly string name;
        private readonly string ipv4Addr;

        internal NotIncludeDuplicatesRecordStore(string name, string ipv4Addr)
        {
            this.name = name;
            this.ipv4Addr = ipv4Addr;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            ISet<IResourceRecord> records = new LinkedHashSet<IResourceRecord>(4);
            string qName = question.GetDomainName().ToLower(CultureInfo.CurrentCulture);
            if (qName == name)
            {
                records.Add(new TestDnsServer.TestResourceRecord(
                        qName, RecordType.CNAME,
                        new Dictionary<string, object>
                        {
                                [DnsAttribute.DOMAIN_NAME.ToLower(CultureInfo.CurrentCulture)] = "cname.netty.io"
                        }));
                records.Add(new TestDnsServer.TestResourceRecord(qName,
                        RecordType.A, new Dictionary<string, object>
                        {
                                [DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = ipv4Addr
                        }));
            }
            else
            {
                records.Add(new TestDnsServer.TestResourceRecord(qName,
                        RecordType.A, new Dictionary<string, object>
                        {
                                [DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = ipv4Addr
                        }));
            }

            return records;
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestIncludeDuplicates(DnsNameResolverChannelStrategy strategy)
    {
        const string name = "netty.io";
        const string ipv4Addr = "1.2.3.4";
        TestDnsServer dnsServer2 = new TestDnsServer(new IncludeDuplicatesRecordStore(ipv4Addr));
        dnsServer2.Start();
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(true)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));
            builder.ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only);

            resolver = builder.Build();
            IReadOnlyList<IDnsRecord> resolvedAddresses = await resolver.ResolveAllAsync(new DefaultDnsQuestion(name, A));
            Assert.Equal(2, resolvedAddresses.Count);

            foreach (IDnsRecord record in resolvedAddresses)
            {
                ReferenceCountUtil.Release(record);
            }
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class IncludeDuplicatesRecordStore : IRecordStore
    {
        private readonly string ipv4Addr;

        internal IncludeDuplicatesRecordStore(string ipv4Addr)
        {
            this.ipv4Addr = ipv4Addr;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            ISet<IResourceRecord> records = new LinkedHashSet<IResourceRecord>(2);
            string qName = question.GetDomainName().ToLower(CultureInfo.CurrentCulture);
            records.Add(new TestDnsServer.TestResourceRecord(qName,
                    RecordType.A, new Dictionary<string, object>
                    {
                            [DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = ipv4Addr
                    }));
            records.Add(new TestDnsServer.TestResourceRecord(qName,
                    RecordType.A, new Dictionary<string, object>
                    {
                            [DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = ipv4Addr
                    }));
            return records;
        }
    }

    // CLR: assertEquals(host, address.getHostName()) has no counterpart: IPAddress carries no host name, and infra's
    // DnsAddressDecoder drops the record name (core notes, "host-name differences"); the address is asserted to be the
    // IPv4 address of the A answer instead (the AAAA query is dropped by the server).
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestDropAAAA(DnsNameResolverChannelStrategy strategy)
    {
        string host = "somehost.netty.io";
        TestDnsServer dnsServer2 = new TestDnsServer(ImmutableHashSet.Create(host));
        dnsServer2.Start(RecordType.AAAA);
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(false)
                    .QueryTimeoutMillis(500)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Preferred)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));

            resolver = builder.Build();
            IReadOnlyList<IPAddress> addressList = await resolver.ResolveAllAsync(host);
            Assert.Single(addressList);
            Assert.Equal(AddressFamily.InterNetwork, addressList[0].AddressFamily);
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    // CLR: getHostName() as in testDropAAAA.
    [Theory(Timeout = 2000)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestDropAAAAResolveFast(DnsNameResolverChannelStrategy strategy)
    {
        string host = "somehost.netty.io";
        TestDnsServer dnsServer2 = new TestDnsServer(ImmutableHashSet.Create(host));
        dnsServer2.Start(RecordType.AAAA);
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(false)
                    .QueryTimeoutMillis(10000)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Preferred)
                    .CompleteOncePreferredResolved(true)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));

            resolver = builder.Build();
            IPAddress address = await resolver.ResolveAsync(host);
            Assert.Equal(AddressFamily.InterNetwork, address.AddressFamily);
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    // CLR: The getHostName() assertion has no counterpart (see testDropAAAA).
    [Theory(Timeout = 2000)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestDropAAAAResolveAllFast(DnsNameResolverChannelStrategy strategy)
    {
        const string host = "somehost.netty.io";
        TestDnsServer dnsServer2 = new TestDnsServer(new DropAAAAResolveAllFastRecordStore(host));
        dnsServer2.Start(RecordType.AAAA);
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(false)
                    .QueryTimeoutMillis(10000)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Preferred)
                    .CompleteOncePreferredResolved(true)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));

            resolver = builder.Build();
            IReadOnlyList<IPAddress> addresses = await resolver.ResolveAllAsync(host);
            Assert.Equal(2, addresses.Count);

            foreach (IPAddress address in addresses)
            {
                Assert.Equal(AddressFamily.InterNetwork, address.AddressFamily);
            }
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class DropAAAAResolveAllFastRecordStore : IRecordStore
    {
        private readonly string host;

        internal DropAAAAResolveAllFastRecordStore(string host)
        {
            this.host = host;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            string name = question.GetDomainName();
            if (name == host)
            {
                ISet<IResourceRecord> records = new HashSet<IResourceRecord>(2);
                records.Add(new TestDnsServer.TestResourceRecord(name, RecordType.A,
                        new Dictionary<string, object>
                        {
                                [DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = "10.0.0.1"
                        }));
                records.Add(new TestDnsServer.TestResourceRecord(name, RecordType.A,
                        new Dictionary<string, object>
                        {
                                [DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = "10.0.0.2"
                        }));
                return records;
            }

            return null;
        }
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTruncatedWithoutTcpFallback(DnsNameResolverChannelStrategy strategy)
    {
        await TestTruncated0(strategy, false, false);
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTruncatedWithTcpFallback(DnsNameResolverChannelStrategy strategy)
    {
        await TestTruncated0(strategy, true, false);
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTruncatedWithTcpFallbackBecauseOfMtu(DnsNameResolverChannelStrategy strategy)
    {
        await TestTruncated0(strategy, true, true);
    }

    private static DnsMessageModifier ModifierFrom(DnsMessage message)
    {
        DnsMessageModifier modifier = new DnsMessageModifier();
        modifier.SetAcceptNonAuthenticatedData(message.IsAcceptNonAuthenticatedData());
        modifier.SetAdditionalRecords(message.GetAdditionalRecords());
        modifier.SetAnswerRecords(message.GetAnswerRecords());
        modifier.SetAuthoritativeAnswer(message.IsAuthoritativeAnswer());
        modifier.SetAuthorityRecords(message.GetAuthorityRecords());
        modifier.SetMessageType(message.GetMessageType());
        modifier.SetOpCode(message.GetOpCode());
        modifier.SetQuestionRecords(message.GetQuestionRecords());
        modifier.SetRecursionAvailable(message.IsRecursionAvailable());
        modifier.SetRecursionDesired(message.IsRecursionDesired());
        modifier.SetReserved(message.IsReserved());
        modifier.SetResponseCode(message.GetResponseCode());
        modifier.SetTransactionId(message.GetTransactionId());
        modifier.SetTruncated(message.IsTruncated());
        return modifier;
    }

    // CLR: ChannelFactory<DatagramChannel> is Func<IDatagramChannel> (resolver-dns mapping) returning the one
    // NioDatagramChannel; socketChannelType(NioSocketChannel.class) is SocketChannelType(typeof(NioSocketChannel)).
    // The ServerSocket is a BCL TCP Socket (see startDnsServerAndCreateServerSocket); accept() blocks as in the source.
    private static async Task TestTruncated0(DnsNameResolverChannelStrategy strategy,
                                             bool tcpFallback, bool truncatedBecauseOfMtu)
    {
        Socket serverSocket = null;
        const string host = "somehost.netty.io";
        const string txt = "this is a txt record";
        AtomicReference<DnsMessage> messageRef = new AtomicReference<DnsMessage>();

        TestDnsServer dnsServer2 = new TruncatedTestDnsServer(new TxtRecordStore(host, txt), messageRef,
                                                              truncatedBecauseOfMtu);
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy);
            IDatagramChannel datagramChannel = new NioDatagramChannel();
            Func<IDatagramChannel> channelFactory = () => datagramChannel;
            builder.DatagramChannelFactory(channelFactory);

            if (tcpFallback)
            {
                // If we are configured to use TCP as a fallback also bind a TCP socket
                serverSocket = StartDnsServerAndCreateServerSocket(dnsServer2);
                // If we are configured to use TCP as a fallback also bind a TCP socket
                builder.SocketChannelType(typeof(NioSocketChannel));
            }
            else
            {
                dnsServer2.Start();
            }

            builder.QueryTimeoutMillis(10000)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Preferred)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));
            resolver = builder.Build();

            if (truncatedBecauseOfMtu)
            {
                datagramChannel.Pipeline.AddFirst(new TruncatingPacketHandler());
            }

            Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>> envelopeFuture = resolver.QueryAsync(
                    new DefaultDnsQuestion(host, DnsRecordType.TXT));

            if (tcpFallback)
            {
                // If we are configured to use TCP as a fallback lets replay the dns message over TCP
                Socket socket = serverSocket.Accept();
                ResponseViaSocket(socket, messageRef.Get());

                // Let's wait until we received the envelope before closing the socket.
                await envelopeFuture;

                socket.Close();
                serverSocket.Close();
            }

            IAddressedEnvelope<IDnsResponse, IPEndPoint> envelope = await envelopeFuture;
            Assert.NotNull(envelope.Sender);

            IDnsResponse response = envelope.Content;
            Assert.NotNull(response);

            Assert.Equal(DnsResponseCode.NOERROR, response.Code());
            int count = response.Count(DnsSection.ANSWER);

            Assert.Equal(1, count);
            IList<string> texts = DecodeTxt(response.RecordAt<IDnsRecord>(DnsSection.ANSWER, 0));
            Assert.Single(texts);
            Assert.Equal(txt, texts[0]);

            if (tcpFallback)
            {
                Assert.False(envelope.Content.IsTruncated());
            }
            else
            {
                Assert.True(envelope.Content.IsTruncated());
            }

            Assert.True(envelope.Release());
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class TxtRecordStore : IRecordStore
    {
        private readonly string host;
        private readonly string txt;

        internal TxtRecordStore(string host, string txt)
        {
            this.host = host;
            this.txt = txt;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            string name = question.GetDomainName();
            if (name == host)
            {
                return ImmutableHashSet.Create<IResourceRecord>(
                        new TestDnsServer.TestResourceRecord(name, RecordType.TXT,
                                new Dictionary<string, object>
                                {
                                        [DnsAttribute.CHARACTER_STRING.ToLower(CultureInfo.CurrentCulture)] = txt
                                }));
            }

            return null;
        }
    }

    private sealed class TruncatedTestDnsServer : TestDnsServer
    {
        private readonly AtomicReference<DnsMessage> messageRef;
        private readonly bool truncatedBecauseOfMtu;

        internal TruncatedTestDnsServer(IRecordStore store, AtomicReference<DnsMessage> messageRef,
                                        bool truncatedBecauseOfMtu) : base(store)
        {
            this.messageRef = messageRef;
            this.truncatedBecauseOfMtu = truncatedBecauseOfMtu;
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            // Store a original message so we can replay it later on.
            messageRef.Set(message);

            if (!truncatedBecauseOfMtu)
            {
                // Create a copy of the message but set the truncated flag.
                DnsMessageModifier modifier = ModifierFrom(message);
                modifier.SetTruncated(true);

                return modifier.GetDnsMessage();
            }

            return message;
        }
    }

    private sealed class TruncatingPacketHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is DatagramPacket)
            {
                // Truncate the packet by 1 byte.
                DatagramPacket packet = (DatagramPacket)msg;
                packet.Content.WriterIndex = packet.Content.WriterIndex - 1;
            }

            ctx.FireChannelRead(msg);
        }
    }

    // CLR: socket.getInputStream()/getOutputStream() are one NetworkStream over the accepted socket that does not own
    // it (the caller closes the socket); InputStream.read() is Stream.ReadByte (the next byte or -1) and
    // OutputStream.write(int) WriteByte (the low eight bits). ByteBuffer.allocate(2) is IoBuffer.Allocate(2) (see the
    // part header).
    private static void ResponseViaSocket(Socket socket, DnsMessage message)
    {
        using NetworkStream stream = new NetworkStream(socket, false);
        Stream @in = stream;
        Assert.True((@in.ReadByte() << 8 | (@in.ReadByte() & 0xff)) > 2); // skip length field
        int txnId = @in.ReadByte() << 8 | (@in.ReadByte() & 0xff);

        IoBuffer ioBuffer = IoBuffer.Allocate(1024);
        // Must replace the transactionId with the one from the TCP request
        DnsMessageModifier modifier = ModifierFrom(message);
        modifier.SetTransactionId(txnId);
        new DnsMessageEncoder().Encode(ioBuffer, modifier.GetDnsMessage());
        ioBuffer.Flip();

        IoBuffer lenBuffer = IoBuffer.Allocate(2);
        lenBuffer.PutShort((short)ioBuffer.Remaining());
        lenBuffer.Flip();

        while (lenBuffer.HasRemaining())
        {
            stream.WriteByte(lenBuffer.Get());
        }

        while (ioBuffer.HasRemaining())
        {
            stream.WriteByte(ioBuffer.Get());
        }

        stream.Flush();
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTcpFallbackWhenTimeout(DnsNameResolverChannelStrategy strategy)
    {
        await TestTcpFallbackWhenTimeout_(strategy, true);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestTcpFallbackFailedWhenTimeout(DnsNameResolverChannelStrategy strategy)
    {
        await TestTcpFallbackWhenTimeout_(strategy, false);
    }

    // CLR: new ServerSocket() + setReuseAddress(true) + bind(address) is a BCL TCP Socket with
    // SocketOptionName.ReuseAddress, Bind and Listen (ServerSocket.bind also listens); getLocalSocketAddress() is
    // LocalEndPoint. TestDnsServer.start throws System.IO.IOException when the UDP bind fails, as MINA does (the
    // ApacheDs NioDatagramAcceptor). Assumptions.abort(message) is xUnit v3's Assert.Skip(message), as transport's
    // DatagramUnicastSourceTest maps TestAbortedException.
    private static Socket StartDnsServerAndCreateServerSocket(TestDnsServer dns)
    {
        for (int i = 0;; i++)
        {
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            serverSocket.Bind(new IPEndPoint(NetUtil.LOCALHOST4, 0));
            serverSocket.Listen();

            try
            {
                dns.Start(null, (IPEndPoint)serverSocket.LocalEndPoint);
                if (dns.LocalAddress() != null)
                {
                    return serverSocket;
                }

                // The underlying UDP acceptor sometimes fails to bind without throwing (observed on Windows),
                // leaving localAddress() null. Treat this the same as a failed bind and retry.
                dns.Stop();
                serverSocket.Close();

                if (i == 10)
                {
                    Assert.Skip(
                            "Unable to bind TestDnsServer and ServerSocket to the same address: localAddress is null");
                }
            }
            catch (IOException e)
            {
                serverSocket.Close();

                if (i == 10)
                {
                    // We tried 10 times without success
                    Assert.Skip("Unable to bind TestDnsServer and ServerSocket to the same address: " +
                            e.Message);
                }
                // We could not start the DnsServer which is most likely because the localAddress was already used,
                // let's retry
            }
        }
    }

    // CLR: socketChannelType(NioSocketChannel.class, true) is SocketChannelType(typeof(NioSocketChannel), true);
    // envelopeFuture.awaitUninterruptibly().cause() is AwaitUninterruptibly then DnsNameResolver.Cause, and
    // error.getSuppressed() Common's ThrowableUtil.GetSuppressed.
    private async Task TestTcpFallbackWhenTimeout_(DnsNameResolverChannelStrategy strategy, bool tcpSuccess)
    {
        const string host = "somehost.netty.io";
        const string txt = "this is a txt record";
        AtomicReference<DnsMessage> messageRef = new AtomicReference<DnsMessage>();

        TestDnsServer dnsServer2 = new TcpFallbackTestDnsServer(new TxtRecordStore(host, txt), messageRef);
        DnsNameResolver resolver = null;
        Socket serverSocket = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy);
            IDatagramChannel datagramChannel = new NioDatagramChannel();
            Func<IDatagramChannel> channelFactory = () => datagramChannel;
            builder.DatagramChannelFactory(channelFactory);
            serverSocket = StartDnsServerAndCreateServerSocket(dnsServer2);
            // If we are configured to use TCP as a fallback also bind a TCP socket
            builder.SocketChannelType(typeof(NioSocketChannel), true);

            builder.QueryTimeoutMillis(1000)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Preferred)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()))
                    .DatagramChannelStrategy(strategy);
            resolver = builder.Build();
            Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>> envelopeFuture = resolver.QueryAsync(
                    new DefaultDnsQuestion(host, DnsRecordType.TXT));

            // If we are configured to use TCP as a fallback lets replay the dns message over TCP
            Socket socket = serverSocket.Accept();

            if (tcpSuccess)
            {
                ResponseViaSocket(socket, messageRef.Get());

                // Let's wait until we received the envelope before closing the socket.
                await envelopeFuture;
                socket.Close();

                IAddressedEnvelope<IDnsResponse, IPEndPoint> envelope =
                        await envelopeFuture;
                Assert.NotNull(envelope.Sender);

                IDnsResponse response = envelope.Content;
                Assert.NotNull(response);

                Assert.Equal(DnsResponseCode.NOERROR, response.Code());
                int count = response.Count(DnsSection.ANSWER);

                Assert.Equal(1, count);
                IList<string> texts = DecodeTxt(response.RecordAt<IDnsRecord>(DnsSection.ANSWER, 0));
                Assert.Single(texts);
                Assert.Equal(txt, texts[0]);

                Assert.False(envelope.Content.IsTruncated());
                Assert.True(envelope.Release());
            }
            else
            {
                // Just close the socket. This should cause the original exception to be used.
                socket.Close();
                await AwaitUninterruptibly(envelopeFuture);
                Exception error = DnsNameResolver.Cause(envelopeFuture);
                Assert.IsAssignableFrom<DnsNameResolverTimeoutException>(error);
                Assert.True(ThrowableUtil.GetSuppressed(error).Length >= 1);
            }
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }

            if (serverSocket != null)
            {
                serverSocket.Close();
            }
        }
    }

    private sealed class TcpFallbackTestDnsServer : TestDnsServer
    {
        private readonly AtomicReference<DnsMessage> messageRef;

        internal TcpFallbackTestDnsServer(IRecordStore store, AtomicReference<DnsMessage> messageRef) : base(store)
        {
            this.messageRef = messageRef;
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            // Store a original message so we can replay it later on.
            messageRef.Set(message);
            return null;
        }
    }

    // CLR: the Promise from eventLoop.newPromise() that resolve(host, promise) completes is the CancellationToken
    // of a CancellationTokenSource passed to ResolveAsync, and promise.cancel(true) is Cancel(): the resolver
    // cancels its promise from the token (core notes, "Cancellation"), and the awaited Task is Canceled, so the
    // expected CancellationException is the OperationCanceledException (TaskCanceledException) that awaiting a
    // canceled Task throws (core notes, Future/Promise table). assertThrows(Exception.class,...) is
    // ThrowsAnyAsync<Exception>. As in the source, the two servers and the resolver are not closed.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCancelPromise(DnsNameResolverChannelStrategy strategy)
    {
        IEventLoop eventLoop = group.Next();
        CancellationTokenSource promise = new CancellationTokenSource();
        TestDnsServer dnsServer1 = new CancelPromiseTestDnsServer(promise);
        dnsServer1.Start();
        AtomicBoolean isQuerySentToSecondServer = new AtomicBoolean();
        TestDnsServer dnsServer2 = new QuerySentTestDnsServer(isQuerySentToSecondServer);
        dnsServer2.Start();
        IDnsServerAddressStreamProvider nameServerProvider =
                new SequentialDnsServerAddressStreamProvider(dnsServer1.LocalAddress(),
                                                             dnsServer2.LocalAddress());
        DnsNameResolver resolver = new DnsNameResolverBuilder(group.Next())
                .DnsQueryLifecycleObserverFactory(new TestRecursiveCacheDnsQueryLifecycleObserverFactory())
                .DatagramChannelType(typeof(NioDatagramChannel))
                .OptResourceEnabled(false)
                .NameServerProvider(nameServerProvider)
                .DatagramChannelStrategy(strategy)
                .Build();

        Exception e = await Assert.ThrowsAnyAsync<Exception>(
                () => resolver.ResolveAsync("non-existent.netty.io", promise.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(e);
        Assert.False(isQuerySentToSecondServer.Get());
        GC.KeepAlive(eventLoop);
    }

    // CLR: the first anonymous TestDnsServer subclass of testCancelPromise.
    private sealed class CancelPromiseTestDnsServer : TestDnsServer
    {
        private readonly CancellationTokenSource promise;

        internal CancelPromiseTestDnsServer(CancellationTokenSource promise)
            : base(ImmutableHashSet<string>.Empty)
        {
            this.promise = promise;
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            promise.Cancel();
            return message;
        }
    }

    // CLR: the second anonymous TestDnsServer subclass of testCancelPromise.
    private sealed class QuerySentTestDnsServer : TestDnsServer
    {
        private readonly AtomicBoolean isQuerySentToSecondServer;

        internal QuerySentTestDnsServer(AtomicBoolean isQuerySentToSecondServer)
            : base(ImmutableHashSet<string>.Empty)
        {
            this.isQuerySentToSecondServer = isQuerySentToSecondServer;
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            isQuerySentToSecondServer.Set(true);
            return message;
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNAMERecursiveResolveDifferentNameServersForDomains(DnsNameResolverChannelStrategy strategy)
    {
        const string firstName = "firstname.com";
        const string secondName = "secondname.com";
        const string lastName = "lastname.com";
        const string ipv4Addr = "1.2.3.4";
        TestDnsServer dnsServer2 = new TestDnsServer(
                new DifferentNameServersRecordStore2(firstName, secondName, lastName, ipv4Addr));
        dnsServer2.Start();
        TestDnsServer dnsServer3 = new TestDnsServer(new DifferentNameServersRecordStore3(secondName, lastName));
        dnsServer3.Start();
        DnsNameResolver resolver = null;

        try
        {
            resolver = NewResolver(strategy)
                    .ResolveCache(NoopDnsCache.INSTANCE)
                    .CnameCache(NoopDnsCnameCache.INSTANCE)
                    .RecursionDesired(true)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new DifferentNameServersProvider(secondName, dnsServer2, dnsServer3))
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Preferred).Build();

            AssertResolvedAddress(await resolver.ResolveAsync(firstName), ipv4Addr, firstName);
        }
        finally
        {
            dnsServer2.Stop();
            dnsServer3.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    // CLR: the first anonymous RecordStore of testCNAMERecursiveResolveDifferentNameServersForDomains.
    private sealed class DifferentNameServersRecordStore2 : IRecordStore
    {
        private readonly string firstName;
        private readonly string secondName;
        private readonly string lastName;
        private readonly string ipv4Addr;

        internal DifferentNameServersRecordStore2(string firstName, string secondName, string lastName,
                                                  string ipv4Addr)
        {
            this.firstName = firstName;
            this.secondName = secondName;
            this.lastName = lastName;
            this.ipv4Addr = ipv4Addr;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            ResourceRecordModifier rm = new ResourceRecordModifier();
            rm.SetDnsClass(RecordClass.IN);
            rm.SetDnsName(question.GetDomainName());
            rm.SetDnsTtl(100);

            if (question.GetDomainName() == firstName)
            {
                rm.SetDnsType(RecordType.CNAME);
                rm.Put(DnsAttribute.DOMAIN_NAME, secondName);
            }
            else if (question.GetDomainName() == lastName)
            {
                rm.SetDnsType(question.GetRecordType());
                rm.Put(DnsAttribute.IP_ADDRESS, ipv4Addr);
            }
            else
            {
                return null;
            }

            return ImmutableHashSet.Create(rm.GetEntry());
        }
    }

    // CLR: the second anonymous RecordStore of testCNAMERecursiveResolveDifferentNameServersForDomains.
    private sealed class DifferentNameServersRecordStore3 : IRecordStore
    {
        private readonly string secondName;
        private readonly string lastName;

        internal DifferentNameServersRecordStore3(string secondName, string lastName)
        {
            this.secondName = secondName;
            this.lastName = lastName;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            if (question.GetDomainName() == secondName)
            {
                ResourceRecordModifier rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(question.GetDomainName());
                rm.SetDnsTtl(100);
                rm.SetDnsType(RecordType.CNAME);
                rm.Put(DnsAttribute.DOMAIN_NAME, lastName);

                return ImmutableHashSet.Create(rm.GetEntry());
            }

            return null;
        }
    }

    private sealed class DifferentNameServersProvider : IDnsServerAddressStreamProvider
    {
        private readonly string secondName;
        private readonly TestDnsServer dnsServer2;
        private readonly TestDnsServer dnsServer3;

        internal DifferentNameServersProvider(string secondName, TestDnsServer dnsServer2, TestDnsServer dnsServer3)
        {
            this.secondName = secondName;
            this.dnsServer2 = dnsServer2;
            this.dnsServer3 = dnsServer3;
        }

        public IDnsServerAddressStream NameServerAddressStream(string hostname)
        {
            if (hostname == secondName + '.')
            {
                return DnsServerAddresses.Singleton(dnsServer3.LocalAddress()).Stream();
            }

            return DnsServerAddresses.Singleton(dnsServer2.LocalAddress()).Stream();
        }
    }

    // CLR: getHostAddress() of the IPv4 address is IPAddress.ToString(); assertEquals(hostname,
    // resolvedAddress.getHostName()) has no counterpart: IPAddress carries no host name and infra's DnsAddressDecoder
    // drops the record name (core notes, "host-name differences"), so only the address is asserted.
    private static void AssertResolvedAddress(IPAddress resolvedAddress, string ipAddr, string hostname)
    {
        Assert.Equal(ipAddr, resolvedAddress.ToString());
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestAllNameServers(DnsNameResolverChannelStrategy strategy)
    {
        const string domain = "netty.io";
        const string ipv4Addr = "1.2.3.4";
        AtomicInteger server2Counter = new AtomicInteger();
        TestDnsServer dnsServer2 = new TestDnsServer(new CountingRecordStore(server2Counter, ipv4Addr));
        dnsServer2.Start();

        AtomicInteger server3Counter = new AtomicInteger();
        TestDnsServer dnsServer3 = new TestDnsServer(new CountingRecordStore(server3Counter, ipv4Addr));
        dnsServer3.Start();
        DnsNameResolver resolver = null;

        try
        {
            resolver = NewResolver(strategy)
                    .ResolveCache(NoopDnsCache.INSTANCE)
                    .CnameCache(NoopDnsCnameCache.INSTANCE)
                    .RecursionDesired(true)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new RotationalProvider(dnsServer2, dnsServer3))
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only).Build();

            AssertResolvedAddress(await resolver.ResolveAsync(domain), ipv4Addr, domain);
            Assert.Equal(1, server2Counter.Get());
            Assert.Equal(0, server3Counter.Get());
            AssertResolvedAddress(await resolver.ResolveAsync(domain), ipv4Addr, domain);
            Assert.Equal(1, server2Counter.Get());
            Assert.Equal(1, server3Counter.Get());
            AssertResolvedAddress(await resolver.ResolveAsync(domain), ipv4Addr, domain);
            Assert.Equal(2, server2Counter.Get());
            Assert.Equal(1, server3Counter.Get());
            AssertResolvedAddress(await resolver.ResolveAsync(domain), ipv4Addr, domain);
            Assert.Equal(2, server2Counter.Get());
            Assert.Equal(2, server3Counter.Get());
        }
        finally
        {
            dnsServer2.Stop();
            dnsServer3.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    // CLR: the two anonymous RecordStores of testAllNameServers (the same body over their own counter).
    private sealed class CountingRecordStore : IRecordStore
    {
        private readonly AtomicInteger counter;
        private readonly string ipv4Addr;

        internal CountingRecordStore(AtomicInteger counter, string ipv4Addr)
        {
            this.counter = counter;
            this.ipv4Addr = ipv4Addr;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            counter.IncrementAndGet();
            ResourceRecordModifier rm = new ResourceRecordModifier();
            rm.SetDnsClass(RecordClass.IN);
            rm.SetDnsName(question.GetDomainName());
            rm.SetDnsTtl(100);

            rm.SetDnsType(question.GetRecordType());
            rm.Put(DnsAttribute.IP_ADDRESS, ipv4Addr);

            return ImmutableHashSet.Create(rm.GetEntry());
        }
    }

    // CLR: the anonymous DnsServerAddressStreamProvider of testAllNameServers; its field initializer runs in the
    // constructor.
    private sealed class RotationalProvider : IDnsServerAddressStreamProvider
    {
        private readonly DnsServerAddresses addresses;

        internal RotationalProvider(TestDnsServer dnsServer2, TestDnsServer dnsServer3)
        {
            addresses = DnsServerAddresses.Rotational(dnsServer2.LocalAddress(), dnsServer3.LocalAddress());
        }

        public IDnsServerAddressStream NameServerAddressStream(string hostname)
        {
            return addresses.Stream();
        }
    }

    [Theory(Timeout = 2000)]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestSrvWithCnameNotCached(DnsNameResolverChannelStrategy strategy)
    {
        AtomicBoolean alias = new AtomicBoolean();
        TestDnsServer dnsServer2 = new TestDnsServer(new SrvWithCnameRecordStore(alias));
        dnsServer2.Start();
        DnsNameResolver resolver = null;

        try
        {
            DnsNameResolverBuilder builder = NewResolver(strategy)
                    .RecursionDesired(false)
                    .QueryTimeoutMillis(10000)
                    .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Preferred)
                    .CompleteOncePreferredResolved(true)
                    .MaxQueriesPerResolve(16)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()));

            resolver = builder.Build();
            await AssertNotEmptyAndRelease(resolver.ResolveAllAsync(new DefaultDnsQuestion("service.netty.io", SRV)));
            alias.Set(true);
            await AssertNotEmptyAndRelease(resolver.ResolveAllAsync(new DefaultDnsQuestion("service.netty.io", SRV)));
            alias.Set(false);
            await AssertNotEmptyAndRelease(resolver.ResolveAllAsync(new DefaultDnsQuestion("service.netty.io", SRV)));
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class SrvWithCnameRecordStore : IRecordStore
    {
        private readonly AtomicBoolean alias;

        internal SrvWithCnameRecordStore(AtomicBoolean alias)
        {
            this.alias = alias;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            string name = question.GetDomainName();
            if (name == "service.netty.io")
            {
                ISet<IResourceRecord> records = new HashSet<IResourceRecord>(2);

                ResourceRecordModifier rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(name);
                rm.SetDnsTtl(10);
                rm.SetDnsType(RecordType.CNAME);
                rm.Put(DnsAttribute.DOMAIN_NAME, "alias.service.netty.io");
                records.Add(rm.GetEntry());

                rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(name);
                rm.SetDnsTtl(10);
                rm.SetDnsType(RecordType.SRV);
                rm.Put(DnsAttribute.DOMAIN_NAME, "foo.service.netty.io");
                rm.Put(DnsAttribute.SERVICE_PORT, "8080");
                rm.Put(DnsAttribute.SERVICE_PRIORITY, "10");
                rm.Put(DnsAttribute.SERVICE_WEIGHT, "1");
                records.Add(rm.GetEntry());
                return records;
            }

            if (name == "foo.service.netty.io")
            {
                ResourceRecordModifier rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(name);
                rm.SetDnsTtl(10);
                rm.SetDnsType(RecordType.A);
                rm.Put(DnsAttribute.IP_ADDRESS, "10.0.0.1");

                return ImmutableHashSet.Create(rm.GetEntry());
            }

            if (alias.Get())
            {
                ResourceRecordModifier rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(name);
                rm.SetDnsTtl(10);
                rm.SetDnsType(RecordType.SRV);
                rm.Put(DnsAttribute.DOMAIN_NAME, "foo.service.netty.io");
                rm.Put(DnsAttribute.SERVICE_PORT, "8080");
                rm.Put(DnsAttribute.SERVICE_PRIORITY, "10");
                rm.Put(DnsAttribute.SERVICE_WEIGHT, "1");

                return ImmutableHashSet.Create(rm.GetEntry());
            }

            return null;
        }
    }

    // CLR: TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS is core's internal static DnsResolveContext field (static import).
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNAMENotTriedOnAddressLookupsWhenDisabled(DnsNameResolverChannelStrategy strategy)
    {
        TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS = false;
        await TestFollowUpCNAME(strategy, false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCNAMEOnlyTriedOnAddressLookups(DnsNameResolverChannelStrategy strategy)
    {
        TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS = true;

        try
        {
            await TestFollowUpCNAME(strategy, true);
        }
        finally
        {
            TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS = false;
        }
    }

    // CLR: future.await().cause() is AwaitUninterruptibly then DnsNameResolver.Cause (the local function AwaitCause);
    // Collections.<String>emptyList() is ImmutableList<string>.Empty.
    private async Task TestFollowUpCNAME(DnsNameResolverChannelStrategy strategy, bool enabled)
    {
        AtomicInteger cnameQueries = new AtomicInteger();

        TestDnsServer dnsServer2 = new TestDnsServer(new FollowUpCnameRecordStore(cnameQueries));

        DnsNameResolver resolver = null;

        try
        {
            dnsServer2.Start();
            resolver = NewNonCachedResolver(strategy, ResolvedAddressTypes.Ipv4Preferred)
                    .MaxQueriesPerResolve(4)
                    .SearchDomains(ImmutableList<string>.Empty)
                    .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()))
                    .Build();

            // We expect these resolves to fail with UnknownHostException,
            // and then check that no unexpected CNAME queries were performed.
            Assert.IsAssignableFrom<UnknownHostException>(
                    await AwaitCause(resolver.ResolveAllAsync(new DefaultDnsQuestion("lookup-srv.netty.io", SRV))));
            Assert.Equal(0, cnameQueries.Get());

            Assert.IsAssignableFrom<UnknownHostException>(
                    await AwaitCause(resolver.ResolveAllAsync(new DefaultDnsQuestion("lookup-naptr.netty.io", NAPTR))));
            Assert.Equal(0, cnameQueries.Get());

            Assert.IsAssignableFrom<UnknownHostException>(
                    await AwaitCause(resolver.ResolveAllAsync(new DefaultDnsQuestion("lookup-cname.netty.io", CNAME))));
            Assert.Equal(1, cnameQueries.Set(0));

            Assert.IsAssignableFrom<UnknownHostException>(
                    await AwaitCause(resolver.ResolveAllAsync(new DefaultDnsQuestion("lookup-a.netty.io", A))));
            Assert.Equal(enabled ? 1 : 0, cnameQueries.Set(0));

            Assert.IsAssignableFrom<UnknownHostException>(
                    await AwaitCause(resolver.ResolveAllAsync(new DefaultDnsQuestion("lookup-aaaa.netty.io", AAAA))));
            Assert.Equal(enabled ? 1 : 0, cnameQueries.Set(0));

            Assert.IsAssignableFrom<UnknownHostException>(
                    await AwaitCause(resolver.ResolveAllAsync("lookup-address.netty.io")));
            Assert.Equal(enabled ? 1 : 0, cnameQueries.Set(0));
        }
        finally
        {
            dnsServer2.Stop();

            if (resolver != null)
            {
                resolver.Dispose();
            }
        }

        static async Task<Exception> AwaitCause(Task future)
        {
            await AwaitUninterruptibly(future);
            return DnsNameResolver.Cause(future);
        }
    }

    private sealed class FollowUpCnameRecordStore : IRecordStore
    {
        private readonly AtomicInteger cnameQueries;

        internal FollowUpCnameRecordStore(AtomicInteger cnameQueries)
        {
            this.cnameQueries = cnameQueries;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord questionRecord)
        {
            if (questionRecord.GetRecordType() == RecordType.CNAME)
            {
                cnameQueries.IncrementAndGet();
            }

            return ImmutableHashSet<IResourceRecord>.Empty;
        }
    }

    private static async Task AssertNotEmptyAndRelease(Task<IReadOnlyList<IDnsRecord>> recordsFuture)
    {
        IReadOnlyList<IDnsRecord> records = await recordsFuture;
        Assert.NotEmpty(records);

        foreach (IDnsRecord record in records)
        {
            ReferenceCountUtil.Release(record);
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public void TestResolveIpv6WithScopeId(DnsNameResolverChannelStrategy strategy)
    {
        TestResolveIpv6WithScopeId0(strategy, false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public void TestResolveAllIpv6WithScopeId(DnsNameResolverChannelStrategy strategy)
    {
        TestResolveIpv6WithScopeId0(strategy, true);
    }

    // An IP literal resolves immediately without a DNS query.
    private void TestResolveIpv6WithScopeId0(DnsNameResolverChannelStrategy strategy, bool resolveAll)
    {
        DnsNameResolver resolver = NewResolver(strategy).Build();
        string address = "fe80:0:0:0:1c31:d1d1:4824:72a9";
        int scopeId = 15;
        string addressString = address + '%' + scopeId;
        byte[] bytes = NetUtil.CreateByteArrayFromIpAddressString(address);
        IPAddress inet6Address = new IPAddress(bytes, scopeId);

        try
        {
            IPAddress addr;

            if (resolveAll)
            {
                IReadOnlyList<IPAddress> addressList = GetNow(resolver.ResolveAllAsync(addressString));
                Assert.Single(addressList);
                addr = addressList[0];
            }
            else
            {
                addr = GetNow(resolver.ResolveAsync(addressString));
            }

            Assert.Equal(inet6Address, addr);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    // CLR: Future.getNow(): the result of a successfully completed Task, otherwise the default value (null).
    private static T GetNow<T>(Task<T> future)
    {
        return future.IsCompletedSuccessfully ? future.Result : default;
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public void TestResolveIpv6WithoutScopeId(DnsNameResolverChannelStrategy strategy)
    {
        TestResolveIpv6WithoutScopeId0(strategy, false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public void TestResolveAllIpv6WithoutScopeId(DnsNameResolverChannelStrategy strategy)
    {
        TestResolveIpv6WithoutScopeId0(strategy, true);
    }

    // CLR: (Inet6Address) InetAddress.getByAddress(bytes) is new IPAddress(bytes) of the 16 bytes.
    private void TestResolveIpv6WithoutScopeId0(DnsNameResolverChannelStrategy strategy, bool resolveAll)
    {
        DnsNameResolver resolver = NewResolver(strategy).Build();
        string addressString = "fe80:0:0:0:1c31:d1d1:4824:72a9";
        byte[] bytes = NetUtil.CreateByteArrayFromIpAddressString(addressString);
        IPAddress inet6Address = new IPAddress(bytes);

        try
        {
            IPAddress addr;

            if (resolveAll)
            {
                IReadOnlyList<IPAddress> addressList = GetNow(resolver.ResolveAllAsync(addressString));
                Assert.Single(addressList);
                addr = addressList[0];
            }
            else
            {
                addr = GetNow(resolver.ResolveAsync(addressString));
            }

            Assert.Equal(inet6Address, addr);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public void TestResolveIp4(DnsNameResolverChannelStrategy strategy)
    {
        TestResolveIp4_(strategy, false);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public void TestResolveAllIp4(DnsNameResolverChannelStrategy strategy)
    {
        TestResolveIp4_(strategy, true);
    }

    private void TestResolveIp4_(DnsNameResolverChannelStrategy strategy, bool resolveAll)
    {
        DnsNameResolver resolver = NewResolver(strategy).Build();
        string addressString = "10.0.0.1";
        byte[] bytes = NetUtil.CreateByteArrayFromIpAddressString(addressString);
        IPAddress inetAddress = new IPAddress(bytes);

        try
        {
            IPAddress addr;

            if (resolveAll)
            {
                IReadOnlyList<IPAddress> addressList = GetNow(resolver.ResolveAllAsync(addressString));
                Assert.Single(addressList);
                addr = addressList[0];
            }
            else
            {
                addr = GetNow(resolver.ResolveAsync(addressString));
            }

            Assert.Equal(inetAddress, addr);
        }
        finally
        {
            resolver.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveSearchDomainStopOnFirstSuccess(DnsNameResolverChannelStrategy strategy)
    {
        const string addressString = "10.0.0.1";
        ConcurrentQueue<string> names = new ConcurrentQueue<string>();
        TestDnsServer dnsServer2 = new TestDnsServer(new SearchDomainStopOnFirstSuccessRecordStore(names, addressString));
        dnsServer2.Start();

        DnsNameResolver resolver = NewResolver(strategy).SearchDomains(
                new[] { "search1.netty.io", "search2.netty.io", "search3.netty.io" })
                .Ndots(2).NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()))
                .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                .Build();

        byte[] bytes = NetUtil.CreateByteArrayFromIpAddressString(addressString);
        IPAddress inetAddress = new IPAddress(bytes);

        try
        {
            IPAddress addr = await resolver.ResolveAsync("netty.io");
            Assert.Equal(inetAddress, addr);
        }
        finally
        {
            resolver.Dispose();
            dnsServer2.Stop();
            Assert.Equal("netty.io.search1.netty.io", Poll(names));
            Assert.Equal("netty.io.search2.netty.io", Poll(names));
            Assert.True(names.IsEmpty);
        }

        static string Poll(ConcurrentQueue<string> queue)
        {
            return queue.TryDequeue(out string name) ? name : null;
        }
    }

    private sealed class SearchDomainStopOnFirstSuccessRecordStore : IRecordStore
    {
        private readonly ConcurrentQueue<string> names;
        private readonly string addressString;
        private int called;

        internal SearchDomainStopOnFirstSuccessRecordStore(ConcurrentQueue<string> names, string addressString)
        {
            this.names = names;
            this.addressString = addressString;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            names.Enqueue(question.GetDomainName());

            if (++called == 2)
            {
                ResourceRecordModifier rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(question.GetDomainName());
                rm.SetDnsTtl(100);

                rm.SetDnsType(question.GetRecordType());
                rm.Put(DnsAttribute.IP_ADDRESS, addressString);

                return ImmutableHashSet.Create(rm.GetEntry());
            }

            return null;
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveTryWithoutSearchDomainFirst(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolveTryWithoutSearchDomainFirst_(strategy, true);
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResolveTryWithoutSearchDomainFirstButContinue(DnsNameResolverChannelStrategy strategy)
    {
        await TestResolveTryWithoutSearchDomainFirst_(strategy, false);
    }

    private static async Task TestResolveTryWithoutSearchDomainFirst_(
            DnsNameResolverChannelStrategy strategy, bool absoluteSuccess)
    {
        const string addressString = "10.0.0.1";
        ConcurrentQueue<string> names = new ConcurrentQueue<string>();
        TestDnsServer dnsServer2 = new TestDnsServer(
                new TryWithoutSearchDomainFirstRecordStore(names, addressString, absoluteSuccess));
        dnsServer2.Start();

        DnsNameResolver resolver = NewResolver(strategy).SearchDomains(
                        new[] { "search1.netty.io", "search2.netty.io", "search3.netty.io" })
                .Ndots(1).NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()))
                .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                .Build();

        byte[] bytes = NetUtil.CreateByteArrayFromIpAddressString(addressString);
        IPAddress inetAddress = new IPAddress(bytes);

        try
        {
            IPAddress addr = await resolver.ResolveAsync("netty.io");
            Assert.Equal(inetAddress, addr);
        }
        finally
        {
            resolver.Dispose();
            dnsServer2.Stop();
            Assert.Equal("netty.io", Poll(names));

            if (!absoluteSuccess)
            {
                Assert.Equal("netty.io.search1.netty.io", Poll(names));
                Assert.Equal("netty.io.search2.netty.io", Poll(names));
            }

            Assert.True(names.IsEmpty);
        }

        static string Poll(ConcurrentQueue<string> queue)
        {
            return queue.TryDequeue(out string name) ? name : null;
        }
    }

    private sealed class TryWithoutSearchDomainFirstRecordStore : IRecordStore
    {
        private readonly ConcurrentQueue<string> names;
        private readonly string addressString;
        private readonly bool absoluteSuccess;
        private int called;

        internal TryWithoutSearchDomainFirstRecordStore(ConcurrentQueue<string> names, string addressString,
                                                        bool absoluteSuccess)
        {
            this.names = names;
            this.addressString = addressString;
            this.absoluteSuccess = absoluteSuccess;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            names.Enqueue(question.GetDomainName());
            ++called;

            if ((absoluteSuccess && called == 1) || called == 3)
            {
                ResourceRecordModifier rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(question.GetDomainName());
                rm.SetDnsTtl(100);

                rm.SetDnsType(question.GetRecordType());
                rm.Put(DnsAttribute.IP_ADDRESS, addressString);

                return ImmutableHashSet.Create(rm.GetEntry());
            }

            return null;
        }
    }

    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestInflightQueries(DnsNameResolverChannelStrategy strategy)
    {
        const string addressString = "10.0.0.1";
        AtomicInteger called = new AtomicInteger();
        CountdownEvent latch = new CountdownEvent(1);
        TestDnsServer dnsServer2 = new TestDnsServer(new InflightQueriesRecordStore(called, latch, addressString));
        dnsServer2.Start();

        DnsNameResolver resolver = NewResolver(strategy)
                .NameServerProvider(new SingletonDnsServerAddressStreamProvider(dnsServer2.LocalAddress()))
                .ResolvedAddressTypes(ResolvedAddressTypes.Ipv4Only)
                .ConsolidateCacheSize(2)
                .Build();

        byte[] bytes = NetUtil.CreateByteArrayFromIpAddressString(addressString);
        IPAddress inetAddress = new IPAddress(bytes);

        try
        {
            Task<IPAddress> f = resolver.ResolveAsync("netty.io");
            Task<IPAddress> f2 = resolver.ResolveAsync("netty.io");
            Assert.False(f.IsCompleted);
            Assert.False(f2.IsCompleted);

            // Now unblock so we receive the response back for our query.
            latch.Signal();

            Assert.Equal(inetAddress, await f);
            Assert.Equal(inetAddress, await f2);
        }
        finally
        {
            resolver.Dispose();
            dnsServer2.Stop();
            Assert.Equal(1, called.Get());
        }
    }

    private sealed class InflightQueriesRecordStore : IRecordStore
    {
        private readonly AtomicInteger called;
        private readonly CountdownEvent latch;
        private readonly string addressString;

        internal InflightQueriesRecordStore(AtomicInteger called, CountdownEvent latch, string addressString)
        {
            this.called = called;
            this.latch = latch;
            this.addressString = addressString;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord question)
        {
            called.IncrementAndGet();

            try
            {
                latch.Wait();
                ResourceRecordModifier rm = new ResourceRecordModifier();
                rm.SetDnsClass(RecordClass.IN);
                rm.SetDnsName(question.GetDomainName());
                rm.SetDnsTtl(100);

                rm.SetDnsType(question.GetRecordType());
                rm.Put(DnsAttribute.IP_ADDRESS, addressString);

                return ImmutableHashSet.Create(rm.GetEntry());
            }
            catch (ThreadInterruptedException e)
            {
                throw new InvalidOperationException(ThrowableUtil.ThrowableToString(e), e);
            }
        }
    }

    // CLR: new DatagramSocket() binds the wildcard address with an ephemeral port on the JDK's default native socket,
    // "an IPv6 socket which lets applications connect to, and accept connections from, both IPv4 and IPv6 hosts" when
    // the OS has IPv6 (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/doc-files/net-properties.html):
    // the counterpart is Socket(SocketType.Dgram, ProtocolType.Udp), a dual-mode socket when the OS supports IPv6
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.dualmode?view=net-10.0), bound to the
    // wildcard of its family. Measured on Windows: an IPv4-only socket on 0.0.0.0:port does not keep the resolver's
    // dual-mode channel from binding [::]:port, so the source's bind failure only appears with the dual-stack socket.
    // isBound() is IsBound and getLocalSocketAddress() LocalEndPoint. java.net
    // BindException ("Signals that an error occurred while attempting to bind a socket to a local address and port.
    // Typically, the port is in use, or the requested local address could not be assigned",
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/BindException.html) is the SocketException
    // with SocketError.AddressAlreadyInUse or AddressNotAvailable, as transport's DatagramUnicastSourceTest maps it;
    // the UnknownHostException's getCause() is its InnerException (core notes: the transport-setup failure keeps the
    // cause). assertThrows matches subclasses (ThrowsAnyAsync) and IllegalStateException is InvalidOperationException.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestAddressAlreadyInUse(DnsNameResolverChannelStrategy strategy)
    {
        Socket datagramSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        datagramSocket.Bind(new IPEndPoint(
                datagramSocket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));

        try
        {
            Assert.True(datagramSocket.IsBound);

            try
            {
                DnsNameResolver resolver = NewResolver(strategy)
                        .LocalAddress(datagramSocket.LocalEndPoint).Build();

                try
                {
                    Exception cause = await Assert.ThrowsAnyAsync<UnknownHostException>(
                            () => resolver.ResolveAsync("netty.io"));
                    AssertBindException(cause.InnerException);
                }
                finally
                {
                    resolver.Dispose();
                }
            }
            catch (InvalidOperationException cause)
            {
                // We might also throw directly here... in this case let's verify that we use the correct exception.
                AssertBindException(cause.InnerException);
            }
        }
        finally
        {
            datagramSocket.Close();
        }

        static void AssertBindException(Exception e)
        {
            SocketException bindException = Assert.IsAssignableFrom<SocketException>(e);
            Assert.True(bindException.SocketErrorCode is SocketError.AddressAlreadyInUse
                        or SocketError.AddressNotAvailable, bindException.SocketErrorCode.ToString());
        }
    }

    // CLR: new InetSocketAddress(InetAddress.getByAddress("foo.com", bytes), 53) is new IPEndPoint(new IPAddress(bytes),
    // 53): the host name "foo.com" has no counterpart (IPAddress carries none) and is not asserted. getByAddress
    // declares the checked UnknownHostException (illegal length), which the source rethrows as Error; new
    // IPAddress(byte[]) of four bytes cannot fail, so that unreachable catch has no counterpart. The (byte) 169 and
    // (byte) 254 casts are the unsigned C# bytes 169 and 254 (the same bits). assertThat(x).isGreaterThanOrEqualTo(0L)
    // is Assert.True(x >= 0L).
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestResponseFeedbackStream(DnsNameResolverChannelStrategy strategy)
    {
        AtomicBoolean successCalled = new AtomicBoolean();
        AtomicBoolean failureCalled = new AtomicBoolean();
        AtomicBoolean returnSuccess = new AtomicBoolean(false);
        DnsNameResolver resolver = NewResolver(strategy, true,
                new ResponseFeedbackStreamProvider(successCalled, failureCalled, returnSuccess)).Build();

        try
        {
            // setup call to be successful and verify
            returnSuccess.Set(true);
            await resolver.ResolveAsync("google.com");
            Assert.True(successCalled.Get());
            Assert.False(failureCalled.Get());

            // reset state for next query
            successCalled.Set(false);
            failureCalled.Set(false);

            // setup call to fail and verify
            returnSuccess.Set(false);

            try
            {
                Exception e = await Assert.ThrowsAnyAsync<Exception>(() =>
                        resolver.ResolveAsync("yahoo.com"));
                // expected
                Assert.IsAssignableFrom<UnknownHostException>(e);
            }
            finally
            {
                Assert.False(successCalled.Get());
                Assert.True(failureCalled.Get());
            }
        }
        finally
        {
            if (resolver != null)
            {
                resolver.Dispose();
            }
        }
    }

    private sealed class ResponseFeedbackStreamProvider : IDnsServerAddressStreamProvider
    {
        private readonly AtomicBoolean successCalled;
        private readonly AtomicBoolean failureCalled;
        private readonly AtomicBoolean returnSuccess;

        internal ResponseFeedbackStreamProvider(AtomicBoolean successCalled, AtomicBoolean failureCalled,
                                                AtomicBoolean returnSuccess)
        {
            this.successCalled = successCalled;
            this.failureCalled = failureCalled;
            this.returnSuccess = returnSuccess;
        }

        public IDnsServerAddressStream NameServerAddressStream(string hostname)
        {
            return new ResponseFeedbackStream(successCalled, failureCalled, returnSuccess);
        }
    }

    private sealed class ResponseFeedbackStream : IDnsServerResponseFeedbackAddressStream
    {
        private readonly AtomicBoolean successCalled;
        private readonly AtomicBoolean failureCalled;
        private readonly AtomicBoolean returnSuccess;

        internal ResponseFeedbackStream(AtomicBoolean successCalled, AtomicBoolean failureCalled,
                                        AtomicBoolean returnSuccess)
        {
            this.successCalled = successCalled;
            this.failureCalled = failureCalled;
            this.returnSuccess = returnSuccess;
        }

        public void FeedbackSuccess(EndPoint address, long queryResponseTimeNanos)
        {
            Assert.True(queryResponseTimeNanos >= 0L);
            successCalled.Set(true);
        }

        public void FeedbackFailure(EndPoint address,
                                    Exception failureCause,
                                    long queryResponseTimeNanos)
        {
            Assert.True(queryResponseTimeNanos >= 0L);
            Assert.NotNull(failureCause);
            failureCalled.Set(true);
        }

        public EndPoint Next()
        {
            if (returnSuccess.Get())
            {
                return dnsServer.LocalAddress();
            }

            return new IPEndPoint(new IPAddress(new byte[] { 169, 254, 12, 34 }), 53);
        }

        public int Size()
        {
            return 1;
        }

        public IDnsServerAddressStream Duplicate()
        {
            return this;
        }
    }

    // CLR: the package-private 25-argument DnsNameResolver constructor is core's internal one (core notes, "Public C#
    // API"): new ReflectiveChannelFactory<DatagramChannel>(NioDatagramChannel.class) is the NewChannel method of
    // ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)) and HostsFileEntriesResolver.DEFAULT
    // IHostsFileEntriesResolver.Default; the local EventLoopGroup group hides the static field, as in the source, and
    // shutdownGracefully(0, 0, SECONDS) is ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero) without waiting.
    // address.getAddress() is GetAddressBytes() and assertArrayEquals Assert.Equal over the arrays.
    [Theory]
    [MemberData(nameof(ChannelStrategies))]
    public async Task TestCnameWithAAndAdditionalsAndAuthorities(DnsNameResolverChannelStrategy strategy)
    {
        const string hostname = "test.netty.io";
        const string cname = "cname.netty.io";

        List<string> nameServers = new List<string>();

        for (int i = 0; i < 13; i++)
        {
            nameServers.Add("ns" + i + ".foo.bar");
        }

        TestDnsServer server = new CnameWithAdditionalsTestDnsServer(
                new CnameWithAdditionalsRecordStore(hostname, cname), hostname, nameServers);
        server.Start();
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        SingletonDnsServerAddressStreamProvider provider =
                new SingletonDnsServerAddressStreamProvider(server.LocalAddress());
        DnsNameResolver resolver = new DnsNameResolver(
                group.Next(), new ReflectiveChannelFactory<IDatagramChannel>(typeof(NioDatagramChannel)).NewChannel,
                null, false,
                NoopDnsCache.INSTANCE, NoopDnsCnameCache.INSTANCE, NoopAuthoritativeDnsServerCache.INSTANCE, null,
                NoopDnsQueryLifecycleObserverFactory.INSTANCE, 2000, ResolvedAddressTypes.Ipv4Only,
                true, 8, true, 4096,
                false, IHostsFileEntriesResolver.Default,
                provider, new ThreadLocalNameServerAddressStream(provider),
                new string[] { "k8se-apps.svc.cluster.local, svc.cluster.local, cluster.local" }, 1,
                true, false, 0, strategy);

        try
        {
            IPAddress address = await resolver.ResolveAsync(hostname);
            Assert.Equal(new byte[] { 10, 0, 0, 2 }, address.GetAddressBytes());
        }
        finally
        {
            resolver.Dispose();
            _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            server.Stop();
        }
    }

    private sealed class CnameWithAdditionalsRecordStore : IRecordStore
    {
        private readonly string hostname;
        private readonly string cname;

        internal CnameWithAdditionalsRecordStore(string hostname, string cname)
        {
            this.hostname = hostname;
            this.cname = cname;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord questionRecord)
        {
            ResourceRecordModifier rm = new ResourceRecordModifier();
            rm.SetDnsClass(RecordClass.IN);
            rm.SetDnsName(hostname);
            rm.SetDnsTtl(10000);
            rm.SetDnsType(RecordType.CNAME);
            rm.Put(DnsAttribute.DOMAIN_NAME, cname);

            ISet<IResourceRecord> records = new LinkedHashSet<IResourceRecord>();
            records.Add(rm.GetEntry());
            records.Add(NewARecord(cname, "10.0.0.2"));
            return records;
        }
    }

    private sealed class CnameWithAdditionalsTestDnsServer : TestDnsServer
    {
        private readonly string hostname;
        private readonly List<string> nameServers;

        internal CnameWithAdditionalsTestDnsServer(IRecordStore store, string hostname, List<string> nameServers)
            : base(store)
        {
            this.hostname = hostname;
            this.nameServers = nameServers;
        }

        protected override DnsMessage FilterMessage(DnsMessage message)
        {
            foreach (QuestionRecord record in message.GetQuestionRecords())
            {
                if (record.GetDomainName() == hostname)
                {
                    // Let's add some extra records.
                    message.GetAuthorityRecords().Clear();
                    message.GetAdditionalRecords().Clear();

                    foreach (string nameserver in nameServers)
                    {
                        message.GetAuthorityRecords().Add(TestDnsServer.NewNsRecord(".", nameserver));
                        message.GetAdditionalRecords().Add(NewAddressRecord(nameserver, RecordType.A, "10.0.0.1"));
                        message.GetAdditionalRecords().Add(NewAddressRecord(nameserver, RecordType.AAAA, "::1"));
                    }

                    return message;
                }
            }

            return message;
        }
    }
}
