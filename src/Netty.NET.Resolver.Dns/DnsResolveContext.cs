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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Dns;
using DnsResponseCode = Netty.NET.Handler.Codec.Dns.DnsResponseCode;
using Netty.NET.Transport.Channels;
using static Netty.NET.Resolver.Dns.DnsResolveContext;

namespace Netty.NET.Resolver.Dns;

// CLR: the static members and static nested classes of the generic Java class DnsResolveContext<T> live in this
// non-generic static class of the same name. Java has one class and one set of statics for every T, while "Each
// distinct closed constructed type ... has its own set of static fields"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#1538-static-and-instance-members):
// the tests set TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS and call cnameResolveFromCache/decodeDomainName for every resolve
// context, and the RedirectAuthoritativeDnsServerCache wrapper must be recognised across DnsAddressResolveContext and
// DnsRecordResolveContext. DnsResolveContext<T> imports them with using static. Nested classes that are private in Java
// but used by the generic class (or whose private fields the sibling AuthoritativeNameServerList reads, which Java
// allows within one top-level class) are internal. RuntimeException is Exception; the
// DnsResolveContextException(message, null, false, true) constructor and its fillInStackTrace() override are mapped
// as in DnsErrorCauseException (suppression and the stack trace switch have no CLR counterpart; the comments are
// recorded in the manifest). String.toLowerCase(Locale.US) is ToLowerInvariant (docs/codec-http-porting.md),
// equalsIgnoreCase OrdinalIgnoreCase, equals/endsWith ordinal; Map/HashMap is Dictionary (owner decision 2026-11-19) and
// Collections.emptyMap() a new empty Dictionary (the callers check emptiness before remove). InetSocketAddress is
// EndPoint (an unresolved name server is a DnsEndPoint, createUnresolved(host, port) is new DnsEndPoint(host, port) and
// getHostString() its Host; docs/handler-porting.md, Addresses) and InetAddress IPAddress. ByteBufHolder.content() is
// IByteBufHolder.Content. UnknownHostException is Common's UnknownHostException (owner decision 2026-11-25).
internal static class DnsResolveContext
{
    internal static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DnsResolveContext));
    private const string PROP_TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS =
            "io.netty.resolver.dns.tryCnameOnAddressLookups";
    internal static bool TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS;

    // CLR: the static initializer is the static constructor (it runs after the static field initializers below,
    // which do not read TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS).
    static DnsResolveContext()
    {
        TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS =
                SystemPropertyUtil.GetBoolean(PROP_TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS, false);

        if (logger.IsDebugEnabled())
        {
            logger.Debug("-D{}: {}", PROP_TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS, TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS);
        }
    }

    internal static readonly Exception NXDOMAIN_QUERY_FAILED_EXCEPTION =
            DnsResolveContextException.NewStatic("No answer found and NXDOMAIN response code returned",
            typeof(DnsResolveContext), "onResponse(..)");
    internal static readonly Exception CNAME_NOT_FOUND_QUERY_FAILED_EXCEPTION =
            DnsResolveContextException.NewStatic("No matching CNAME record found",
            typeof(DnsResolveContext), "onResponseCNAME(..)");
    internal static readonly Exception NO_MATCHING_RECORD_QUERY_FAILED_EXCEPTION =
            DnsResolveContextException.NewStatic("No matching record type found",
            typeof(DnsResolveContext), "onResponseAorAAAA(..)");
    internal static readonly Exception UNRECOGNIZED_TYPE_QUERY_FAILED_EXCEPTION =
            DnsResolveContextException.NewStatic("Response type was unrecognized",
            typeof(DnsResolveContext), "onResponse(..)");
    internal static readonly Exception NAME_SERVERS_EXHAUSTED_EXCEPTION =
            DnsResolveContextException.NewStatic("No name servers returned an answer",
            typeof(DnsResolveContext), "tryToFinishResolve(..)");
    internal static readonly Exception SERVFAIL_QUERY_FAILED_EXCEPTION =
            DnsErrorCauseException.NewStatic("Query failed with SERVFAIL", DnsResponseCode.SERVFAIL,
                    typeof(DnsResolveContext), "onResponse(..)");
    internal static readonly Exception NXDOMAIN_CAUSE_QUERY_FAILED_EXCEPTION =
            DnsErrorCauseException.NewStatic("Query failed with NXDOMAIN", DnsResponseCode.NXDOMAIN,
                    typeof(DnsResolveContext), "onResponse(..)");

    internal sealed class DnsResolveContextException : Exception
    {

        private DnsResolveContextException(string message, bool shared) : base(message)
        {
            Debug.Assert(shared);
        }

        internal static DnsResolveContextException NewStatic(string message, Type clazz, string method)
        {
            DnsResolveContextException exception = new DnsResolveContextException(message, true);
            return ThrowableUtil.UnknownStackTrace(exception, clazz, method);
        }
    }

    // CLR: super(message) and initCause(cause.getCause()) are the (string, Exception) constructor of Common's
    // UnknownHostException; setStackTrace(cause.getStackTrace()) is ExceptionDispatchInfo.SetRemoteStackTrace with the
    // stack trace text of cause, when it has one (a CLR exception has a stack trace only once thrown,
    // https://learn.microsoft.com/en-us/dotnet/api/system.exception.stacktrace?view=net-10.0). serialVersionUID and the
    // fillInStackTrace() override have no counterpart (its comment is recorded in the manifest). Arrays.toString is
    // DnsNameResolver.ArraysToString.
    internal sealed class SearchDomainUnknownHostException : UnknownHostException
    {

        internal SearchDomainUnknownHostException(Exception cause, string originalHostname,
                DnsRecordType[] queryTypes, string[] searchDomains)
            : base("Failed to resolve '" + originalHostname + "' " + DnsNameResolver.ArraysToString(queryTypes) +
                    " and search domain query for configured domains failed as well: " +
                    DnsNameResolver.ArraysToString(searchDomains),
                    // Preserve the cause
                    cause.InnerException)
        {
            if (cause.StackTrace != null)
            {
                ExceptionDispatchInfo.SetRemoteStackTrace(this, cause.StackTrace);
            }
        }
    }

    internal static string HostnameWithDot(string name)
    {
        if (StringUtil.EndsWith(name, '.'))
        {
            return name;
        }

        return name + '.';
    }

    // Resolve the final name from the CNAME cache until there is nothing to follow anymore. This also
    // guards against loops in the cache but early return once a loop is detected.
    //
    // Visible for testing only
    internal static string CnameResolveFromCache(IDnsCnameCache cnameCache, string name)
    {
        string first = cnameCache.Get(HostnameWithDot(name));
        if (first == null)
        {
            // Nothing in the cache at all
            return name;
        }

        string second = cnameCache.Get(HostnameWithDot(first));
        if (second == null)
        {
            // Nothing else to follow, return first match.
            return first;
        }

        CheckCnameLoop(name, first, second);

        return CnameResolveFromCacheLoop(cnameCache, name, first, second);
    }

    private static string CnameResolveFromCacheLoop(
            IDnsCnameCache cnameCache, string hostname, string first, string mapping)
    {
        // Detect loops by advance only every other iteration.
        // See https://en.wikipedia.org/wiki/Cycle_detection#Floyd's_Tortoise_and_Hare
        bool advance = false;

        string name = mapping;

        // Resolve from cnameCache() until there is no more cname entry cached.
        while ((mapping = cnameCache.Get(HostnameWithDot(name))) != null)
        {
            CheckCnameLoop(hostname, first, mapping);
            name = mapping;

            if (advance)
            {
                first = cnameCache.Get(first);
            }

            advance = !advance;
        }

        return name;
    }

    private static void CheckCnameLoop(string hostname, string first, string second)
    {
        if (first.Equals(second))
        {
            // Follow CNAME from cache would loop. Lets throw and so fail the resolution.
            throw new UnknownHostException("CNAME loop detected for '" + hostname + '\'');
        }
    }

    // CLR: the static method redirectAuthoritativeDnsServerCache(...) is RedirectAuthoritativeDnsServerCache_ because
    // the nested class has the same name in this type (CS0102; trailing-underscore rule of docs/codec-http-porting.md).
    internal static IAuthoritativeDnsServerCache RedirectAuthoritativeDnsServerCache_(
            IAuthoritativeDnsServerCache authoritativeDnsServerCache)
    {
        // Don't wrap again to prevent the possibility of an StackOverflowError when wrapping another
        // RedirectAuthoritativeDnsServerCache.
        if (authoritativeDnsServerCache is RedirectAuthoritativeDnsServerCache)
        {
            return authoritativeDnsServerCache;
        }

        return new RedirectAuthoritativeDnsServerCache(authoritativeDnsServerCache);
    }

    private sealed class RedirectAuthoritativeDnsServerCache : IAuthoritativeDnsServerCache
    {
        private readonly IAuthoritativeDnsServerCache wrapped;

        internal RedirectAuthoritativeDnsServerCache(IAuthoritativeDnsServerCache authoritativeDnsServerCache)
        {
            wrapped = authoritativeDnsServerCache;
        }

        public IDnsServerAddressStream Get(string hostname)
        {
            // To not risk falling into any loop, we will not use the cache while following redirects but only
            // on the initial query.
            return null;
        }

        public void Cache(string hostname, EndPoint address, long originalTtl, IEventLoop loop)
        {
            wrapped.Cache(hostname, address, originalTtl, loop);
        }

        public void Clear()
        {
            wrapped.Clear();
        }

        public bool Clear(string hostname)
        {
            return wrapped.Clear(hostname);
        }
    }

    internal static Exception Cause(DnsResponseCode code)
    {
        Debug.Assert(code != null);

        if (DnsResponseCode.SERVFAIL.IntValue() == code.IntValue())
        {
            return SERVFAIL_QUERY_FAILED_EXCEPTION;
        }
        else if (DnsResponseCode.NXDOMAIN.IntValue() == code.IntValue())
        {
            return NXDOMAIN_CAUSE_QUERY_FAILED_EXCEPTION;
        }

        return null;
    }

    // CLR: AbstractList<InetSocketAddress> is IReadOnlyList<EndPoint>, the parameter type of
    // IDnsQueryLifecycleObserver.QueryRedirected; get(index) is the indexer, size() Count, and the anonymous Iterator an
    // iterator block over a duplicate of the stream (remove() has no IEnumerator counterpart). The equals/hashCode/
    // toString that AbstractList inherits are not reproduced.
    internal sealed class DnsAddressStreamList : IReadOnlyList<EndPoint>
    {

        private readonly IDnsServerAddressStream duplicate;
        private List<EndPoint> addresses;

        internal DnsAddressStreamList(IDnsServerAddressStream stream)
        {
            duplicate = stream.Duplicate();
        }

        public EndPoint this[int index]
        {
            get
            {
                if (addresses == null)
                {
                    IDnsServerAddressStream stream = duplicate.Duplicate();
                    addresses = new List<EndPoint>(Count);

                    for (int i = 0; i < stream.Size(); i++)
                    {
                        addresses.Add(stream.Next());
                    }
                }

                return addresses[index];
            }
        }

        public int Count => duplicate.Size();

        public IEnumerator<EndPoint> GetEnumerator()
        {
            IDnsServerAddressStream stream = duplicate.Duplicate();

            for (int i = 0; i < stream.Size(); i++)
            {
                yield return stream.Next();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /**
     * Returns the {@code {@link AuthoritativeNameServerList} which were included in {@link DnsSection#AUTHORITY}
     * or {@code null} if non are found.
     */
    internal static AuthoritativeNameServerList ExtractAuthoritativeNameServers(string questionName, IDnsResponse res)
    {
        int authorityCount = res.Count(DnsSection.AUTHORITY);
        if (authorityCount == 0)
        {
            return null;
        }

        AuthoritativeNameServerList serverNames = new AuthoritativeNameServerList(questionName);

        for (int i = 0; i < authorityCount; i++)
        {
            serverNames.Add(res.RecordAt<IDnsRecord>(DnsSection.AUTHORITY, i));
        }

        return serverNames.IsEmpty() ? null : serverNames;
    }

    internal static Dictionary<string, string> BuildAliasMap(
            string queryName, IDnsResponse response, IDnsCnameCache cache, IEventLoop loop)
    {
        int answerCount = response.Count(DnsSection.ANSWER);
        Dictionary<string, string> cnames = null;

        for (int i = 0; i < answerCount; i ++)
        {
            IDnsRecord r = response.RecordAt<IDnsRecord>(DnsSection.ANSWER, i);
            DnsRecordType type = r.Type();
            if (type != DnsRecordType.CNAME)
            {
                continue;
            }

            if (!(r is IDnsRawRecord))
            {
                continue;
            }

            ByteBuf recordContent = ((IByteBufHolder) r).Content;
            string domainName = DecodeDomainName(recordContent);
            if (domainName == null)
            {
                continue;
            }

            if (cnames == null)
            {
                cnames = new Dictionary<string, string>(Math.Min(8, answerCount));
            }

            string name = r.Name().ToLowerInvariant();
            string mapping = domainName.ToLowerInvariant();

            // Cache the CNAME as well.
            string nameWithDot = HostnameWithDot(name);
            string mappingWithDot = HostnameWithDot(mapping);
            if (!nameWithDot.Equals(mappingWithDot, StringComparison.OrdinalIgnoreCase))
            {
                string queryNameWithDot = HostnameWithDot(queryName.ToLowerInvariant());
                // Only cache the CNAME if the owner is in the bailiwick of the original query name.
                bool inBailiwick = nameWithDot.Equals(queryNameWithDot) ||
                        nameWithDot.EndsWith("." + queryNameWithDot, StringComparison.Ordinal);
                if (inBailiwick)
                {
                    cache.Cache(nameWithDot, mappingWithDot, r.TimeToLive(), loop);
                }

                cnames[name] = mapping;
            }
        }

        return cnames != null? cnames : new Dictionary<string, string>();
    }

    internal static string DecodeDomainName(ByteBuf @in)
    {
        @in.MarkReaderIndex();

        try
        {
            return DefaultDnsRecordDecoder.DecodeName(@in);
        }
        catch (CorruptedFrameException)
        {
            // In this case we just return null.
            return null;
        }
        finally
        {
            @in.ResetReaderIndex();
        }
    }

    /**
     * Holds the closed DNS Servers for a domain.
     */
    internal sealed class AuthoritativeNameServerList
    {

        private readonly string questionName;

        // We not expect the linked-list to be very long so a double-linked-list is overkill.
        private AuthoritativeNameServer head;

        private int nameServerCount;

        internal AuthoritativeNameServerList(string questionName)
        {
            this.questionName = questionName.ToLowerInvariant();
        }

        internal void Add(IDnsRecord r)
        {
            if (r.Type() != DnsRecordType.NS || !(r is IDnsRawRecord))
            {
                return;
            }

            // Only include servers that serve the correct domain.
            if (questionName.Length <  r.Name().Length)
            {
                return;
            }

            string recordName = r.Name().ToLowerInvariant();

            int dots = 0;

            for (int a = recordName.Length - 1, b = questionName.Length - 1; a >= 0; a--, b--)
            {
                char c = recordName[a];
                if (questionName[b] != c)
                {
                    return;
                }

                if (c == '.')
                {
                    dots++;
                }
            }

            if (head != null && head.dots > dots)
            {
                // We already have a closer match so ignore this one, no need to parse the domainName etc.
                return;
            }

            ByteBuf recordContent = ((IByteBufHolder) r).Content;
            string domainName = DecodeDomainName(recordContent);
            if (domainName == null)
            {
                // Could not be parsed, ignore.
                return;
            }

            // We are only interested in preserving the nameservers which are the closest to our qName, so ensure
            // we drop servers that have a smaller dots count.
            if (head == null || head.dots < dots)
            {
                nameServerCount = 1;
                head = new AuthoritativeNameServer(dots, r.TimeToLive(), recordName, domainName);
            }
            else if (head.dots == dots)
            {
                AuthoritativeNameServer serverName = head;

                while (serverName.next != null)
                {
                    serverName = serverName.next;
                }

                serverName.next = new AuthoritativeNameServer(dots, r.TimeToLive(), recordName, domainName);
                nameServerCount++;
            }
        }

        internal void HandleWithAdditional(
                DnsNameResolver parent, IDnsRecord r, IAuthoritativeDnsServerCache authoritativeCache)
        {
            // Just walk the linked-list and mark the entry as handled when matched.
            AuthoritativeNameServer serverName = head;

            string nsName = r.Name();
            IPAddress resolved = DnsAddressDecoder.DecodeAddress(r, nsName, parent.IsDecodeIdn());
            if (resolved == null)
            {
                // Could not parse the address, just ignore.
                return;
            }

            while (serverName != null)
            {
                if (serverName.nsName.Equals(nsName, StringComparison.OrdinalIgnoreCase))
                {
                    if (serverName.address != null)
                    {
                        // We received multiple ADDITIONAL records for the same name.
                        // Search for the last we insert before and then append a new one.
                        while (serverName.next != null && serverName.next.isCopy)
                        {
                            serverName = serverName.next;
                        }

                        AuthoritativeNameServer server = new AuthoritativeNameServer(serverName);
                        server.next = serverName.next;
                        serverName.next = server;
                        serverName = server;

                        nameServerCount++;
                    }

                    // We should replace the TTL if needed with the one of the ADDITIONAL record so we use
                    // the smallest for caching.
                    serverName.Update(parent.NewRedirectServerAddress(resolved), r.TimeToLive());

                    // Cache the server now.
                    Cache(serverName, authoritativeCache, parent.Executor);
                    return;
                }

                serverName = serverName.next;
            }
        }

        // Now handle all AuthoritativeNameServer for which we had no ADDITIONAL record
        internal void HandleWithoutAdditionals(
                DnsNameResolver parent, IDnsCache cache, IAuthoritativeDnsServerCache authoritativeCache)
        {
            AuthoritativeNameServer serverName = head;

            while (serverName != null)
            {
                if (serverName.address == null)
                {
                    // These will be resolved on the fly if needed.
                    CacheUnresolved(serverName, authoritativeCache, parent.Executor);

                    // Try to resolve via cache as we had no ADDITIONAL entry for the server.

                    IReadOnlyList<IDnsCacheEntry> entries = cache.Get(serverName.nsName, null);
                    if (entries != null && entries.Count != 0)
                    {
                        IPAddress address = entries[0].Address();

                        // If address is null we have a resolution failure cached so just use an unresolved address.
                        if (address != null)
                        {
                            serverName.Update(parent.NewRedirectServerAddress(address));

                            for (int i = 1; i < entries.Count; i++)
                            {
                                address = entries[i].Address();

                                Debug.Assert(address != null,
                                        "Cache returned a cached failure, should never return anything else");

                                AuthoritativeNameServer server = new AuthoritativeNameServer(serverName);
                                server.next = serverName.next;
                                serverName.next = server;
                                serverName = server;
                                serverName.Update(parent.NewRedirectServerAddress(address));

                                nameServerCount++;
                            }
                        }
                    }
                }

                serverName = serverName.next;
            }
        }

        private void CacheUnresolved(
                AuthoritativeNameServer server, IAuthoritativeDnsServerCache authoritativeCache, IEventLoop loop)
        {
            // We still want to cached the unresolved address
            server.address = new DnsEndPoint(
                    server.nsName, DefaultDnsServerAddressStreamProvider.DNS_PORT);

            // Cache the server now.
            Cache(server, authoritativeCache, loop);
        }

        private void Cache(AuthoritativeNameServer server, IAuthoritativeDnsServerCache cache, IEventLoop loop)
        {
            // Cache NS record if not for a root server as we should never cache for root servers.
            if (server.IsRootServer())
            {
                return;
            }
            // Bailiwick check (RFC 2181 §5.4.1): only cache a nameserver entry when its zone
            // equals the question name or is a subdomain of it. A server that is authoritative
            // for a child zone must not be trusted to supply authoritative NS records for a
            // parent zone, which would allow cache poisoning of the parent.
            if (!server.domainName.Equals(questionName) &&
                    !server.domainName.EndsWith("." + questionName, StringComparison.Ordinal))
            {
                return;
            }

            cache.Cache(server.domainName, server.address, server.ttl, loop);
        }

        /**
         * Returns {@code true} if empty, {@code false} otherwise.
         */
        internal bool IsEmpty()
        {
            return nameServerCount == 0;
        }

        /**
         * Creates a new {@link List} which holds the {@link InetSocketAddress}es.
         */
        internal List<EndPoint> AddressList()
        {
            List<EndPoint> addressList = new List<EndPoint>(nameServerCount);

            AuthoritativeNameServer server = head;

            while (server != null)
            {
                if (server.address != null)
                {
                    addressList.Add(server.address);
                }

                server = server.next;
            }

            return addressList;
        }
    }

    internal sealed class AuthoritativeNameServer
    {
        internal readonly int dots;
        internal readonly string domainName;
        internal readonly bool isCopy;
        internal readonly string nsName;

        internal long ttl;
        internal EndPoint address;

        internal AuthoritativeNameServer next;

        internal AuthoritativeNameServer(int dots, long ttl, string domainName, string nsName)
        {
            this.dots = dots;
            this.ttl = ttl;
            this.nsName = nsName;
            this.domainName = domainName;
            isCopy = false;
        }

        internal AuthoritativeNameServer(AuthoritativeNameServer server)
        {
            dots = server.dots;
            ttl = server.ttl;
            nsName = server.nsName;
            domainName = server.domainName;
            isCopy = true;
        }

        /**
         * Returns {@code true} if its a root server.
         */
        internal bool IsRootServer()
        {
            return dots == 1;
        }

        /**
         * Update the server with the given address and TTL if needed.
         */
        internal void Update(EndPoint address, long ttl)
        {
            Debug.Assert(this.address == null || this.address is DnsEndPoint);
            this.address = address;
            this.ttl = Math.Min(this.ttl, ttl);
        }

        internal void Update(EndPoint address)
        {
            Update(address, long.MaxValue);
        }
    }
}

// CLR: the package-private abstract class is internal; T is a reference type (IPAddress or IDnsRecord), so a
// converted record can be compared with null. Promise<List<T>> is TaskCompletionSource<IReadOnlyList<T>>
// (Future<List<T>> is Task<IReadOnlyList<T>>, SimpleNameResolver) and Promise<?> originalPromise, only read for
// isCancelled(), the Task of the original promise; isDone()/isCancelled() are Task.IsCompleted/IsCanceled,
// trySuccess/tryFailure TrySetResult/TrySetException, f.cancel(false) TrySetCanceled() and future.cause()
// DnsNameResolver.Cause. The query Future of DnsNameResolver.DoQuery is the TaskCompletionSource it returns, so
// queriesInProgress (an identity set, Collections.newSetFromMap(new IdentityHashMap())) is a HashSet with
// ReferenceEqualityComparer.Instance holding the promises, and newSucceededFuture(null) is a completed one. The
// listeners are continuations on the resolver's listener scheduler (DnsNameResolver.AddListener), and the anonymous
// search domain FutureListener, which re-adds itself, is the nested SearchDomainListener. EventLoop is IEventLoop and
// parent.executor() the resolver's Executor. Iterator<InetAddress> of CombinedDnsServerAddressStream is a position
// into the list. Removing from queriesInProgress while iterating it is the removal of each element of a snapshot.
// System.nanoTime() is SystemTimer.NanoTime() (docs/handler-porting.md). PlatformDependent.throwException(cause) inside
// the catch of that cause is throw; (docs/codec-http2-porting.md). IllegalArgumentException is ArgumentException,
// Throwable Exception, assert Debug.Assert, Math.min Math.Min; numbers in messages use the invariant culture.
internal abstract class DnsResolveContext<T> where T : class
{
    internal readonly DnsNameResolver parent;
    private readonly IChannel channel;
    private readonly Task originalPromise;
    private readonly IDnsServerAddressStream nameServerAddrs;
    private readonly string hostname;
    private readonly int dnsClass;
    private readonly DnsRecordType[] expectedTypes;
    internal readonly IDnsRecord[] additionals;

    private readonly HashSet<TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>>> queriesInProgress =
            new HashSet<TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>>>(
                    ReferenceEqualityComparer.Instance);

    private List<T> finalResult;
    private int allowedQueries;
    private bool triedCNAME;
    private bool completeEarly;

    internal DnsResolveContext(DnsNameResolver parent, IChannel channel,
                      Task originalPromise, string hostname, int dnsClass, DnsRecordType[] expectedTypes,
                      IDnsRecord[] additionals, IDnsServerAddressStream nameServerAddrs, int allowedQueries)
    {
        Debug.Assert(expectedTypes.Length > 0);

        this.parent = parent;
        this.channel = channel;
        this.originalPromise = originalPromise;
        this.hostname = hostname;
        this.dnsClass = dnsClass;
        this.expectedTypes = expectedTypes;
        this.additionals = additionals;

        this.nameServerAddrs = ObjectUtil.CheckNotNull(nameServerAddrs, "nameServerAddrs");
        this.allowedQueries = allowedQueries;
    }

    /**
     * The {@link Channel} used.
     */
    internal IChannel Channel()
    {
        return channel;
    }

    /**
     * The {@link DnsCache} to use while resolving.
     */
    internal virtual IDnsCache ResolveCache()
    {
        return parent.ResolveCache();
    }

    /**
     * The {@link DnsCnameCache} that is used for resolving.
     */
    internal virtual IDnsCnameCache CnameCache()
    {
        return parent.CnameCache();
    }

    /**
     * The {@link AuthoritativeDnsServerCache} to use while resolving.
     */
    internal virtual IAuthoritativeDnsServerCache AuthoritativeDnsServerCache()
    {
        return parent.AuthoritativeDnsServerCache();
    }

    /**
     * Creates a new context with the given parameters.
     */
    internal abstract DnsResolveContext<T> NewResolverContext(DnsNameResolver parent, IChannel channel,
                                                     Task originalPromise,
                                                     string hostname,
                                                     int dnsClass, DnsRecordType[] expectedTypes,
                                                     IDnsRecord[] additionals,
                                                     IDnsServerAddressStream nameServerAddrs, int allowedQueries);

    /**
     * Converts the given {@link DnsRecord} into {@code T}.
     */
    internal abstract T ConvertRecord(IDnsRecord record, string hostname, IDnsRecord[] additionals,
                                      IEventLoop eventLoop);

    /**
     * Returns a filtered list of results which should be the final result of DNS resolution. This must take into
     * account JDK semantics such as {@link NetUtil#isIpV6AddressesPreferred()}.
     */
    internal abstract List<T> FilterResults(List<T> unfiltered);

    internal abstract bool IsCompleteEarly(T resolved);

    /**
     * Returns {@code true} if we should allow duplicates in the result or {@code false} if no duplicates should
     * be included.
     */
    internal abstract bool IsDuplicateAllowed();

    /**
     * Caches a successful resolution.
     */
    internal abstract void Cache(string hostname, IDnsRecord[] additionals,
                        IDnsRecord result, T convertedResult);

    /**
     * Caches a failed resolution.
     */
    internal abstract void Cache(string hostname, IDnsRecord[] additionals,
                        UnknownHostException cause);

    internal void Resolve(TaskCompletionSource<IReadOnlyList<T>> promise)
    {
        string[] searchDomains = parent.SearchDomains();
        if (searchDomains.Length == 0 || parent.Ndots() == 0 || StringUtil.EndsWith(hostname, '.'))
        {
            InternalResolve(hostname, promise);
        }
        else
        {
            bool startWithoutSearchDomain = HasNDots();
            string initialHostname = startWithoutSearchDomain ? hostname : hostname + '.' + searchDomains[0];
            int initialSearchDomainIdx = startWithoutSearchDomain ? 0 : 1;

            TaskCompletionSource<IReadOnlyList<T>> searchDomainPromise =
                    DnsNameResolver.NewPromise<IReadOnlyList<T>>();
            parent.AddListener(searchDomainPromise.Task, new SearchDomainListener(this, promise, searchDomains,
                    startWithoutSearchDomain, initialSearchDomainIdx).OperationComplete);
            DoSearchDomainQuery(initialHostname, searchDomainPromise);
        }
    }

    // CLR: the anonymous FutureListener<List<T>> of resolve(...), which keeps searchDomainIdx and adds itself to the
    // next promise.
    private sealed class SearchDomainListener
    {
        private readonly DnsResolveContext<T> context;
        private readonly TaskCompletionSource<IReadOnlyList<T>> promise;
        private readonly string[] searchDomains;
        private readonly bool startWithoutSearchDomain;
        private int searchDomainIdx;

        internal SearchDomainListener(DnsResolveContext<T> context, TaskCompletionSource<IReadOnlyList<T>> promise,
                                      string[] searchDomains, bool startWithoutSearchDomain, int initialSearchDomainIdx)
        {
            this.context = context;
            this.promise = promise;
            this.searchDomains = searchDomains;
            this.startWithoutSearchDomain = startWithoutSearchDomain;
            searchDomainIdx = initialSearchDomainIdx;
        }

        internal void OperationComplete(Task<IReadOnlyList<T>> future)
        {
            Exception cause = DnsNameResolver.Cause(future);
            if (cause == null)
            {
                IReadOnlyList<T> result = future.Result;
                if (!promise.TrySetResult(result))
                {
                    foreach (T item in result)
                    {
                        ReferenceCountUtil.SafeRelease(item);
                    }
                }
            }
            else
            {
                if (DnsNameResolver.IsTransportOrTimeoutError(cause))
                {
                    promise.TrySetException(new SearchDomainUnknownHostException(cause, context.hostname,
                            context.expectedTypes, searchDomains));
                }
                else if (searchDomainIdx < searchDomains.Length)
                {
                    TaskCompletionSource<IReadOnlyList<T>> newPromise = DnsNameResolver.NewPromise<IReadOnlyList<T>>();
                    context.parent.AddListener(newPromise.Task, OperationComplete);
                    context.DoSearchDomainQuery(context.hostname + '.' + searchDomains[searchDomainIdx++], newPromise);
                }
                else if (!startWithoutSearchDomain)
                {
                    context.InternalResolve(context.hostname, promise);
                }
                else
                {
                    promise.TrySetException(new SearchDomainUnknownHostException(cause, context.hostname,
                            context.expectedTypes, searchDomains));
                }
            }
        }
    }

    private bool HasNDots()
    {
        for (int idx = hostname.Length - 1, dots = 0; idx >= 0; idx--)
        {
            if (hostname[idx] == '.' && ++dots >= parent.Ndots())
            {
                return true;
            }
        }

        return false;
    }

    internal virtual void DoSearchDomainQuery(string hostname, TaskCompletionSource<IReadOnlyList<T>> nextPromise)
    {
        DnsResolveContext<T> nextContext = NewResolverContext(parent, channel,
                originalPromise, hostname, dnsClass,
                expectedTypes, additionals, nameServerAddrs,
                parent.MaxQueriesPerResolve());
        nextContext.InternalResolve(hostname, nextPromise);
    }

    private void InternalResolve(string name, TaskCompletionSource<IReadOnlyList<T>> promise)
    {
        try
        {
            // Resolve from cnameCache() until there is no more cname entry cached.
            name = CnameResolveFromCache(CnameCache(), name);
        }
        catch (Exception cause)
        {
            promise.TrySetException(cause);
            return;
        }

        try
        {
            IDnsServerAddressStream nameServerAddressStream = GetNameServers(name);

            int end = expectedTypes.Length - 1;

            for (int i = 0; i < end; ++i)
            {
                if (!Query(name, expectedTypes[i], nameServerAddressStream.Duplicate(), false, promise))
                {
                    return;
                }
            }

            Query(name, expectedTypes[end], nameServerAddressStream, false, promise);
        }
        finally
        {
            // Now flush everything we submitted before for the Channel.
            channel.Flush();
        }
    }

    /**
     * Returns the {@link DnsServerAddressStream} that was cached for the given hostname or {@code null} if non
     *  could be found.
     */
    private IDnsServerAddressStream GetNameServersFromCache(string hostname)
    {
        int len = hostname.Length;

        if (len == 0)
        {
            // We never cache for root servers.
            return null;
        }

        // We always store in the cache with a trailing '.'.
        if (hostname[len - 1] != '.')
        {
            hostname += ".";
        }

        int idx = hostname.IndexOf('.');
        if (idx == hostname.Length - 1)
        {
            // We are not interested in handling '.' as we should never serve the root servers from cache.
            return null;
        }

        // We start from the closed match and then move down.
        for (;;)
        {
            // Skip '.' as well.
            hostname = hostname.Substring(idx + 1);

            int idx2 = hostname.IndexOf('.');
            if (idx2 <= 0 || idx2 == hostname.Length - 1)
            {
                // We are not interested in handling '.TLD.' as we should never serve the root servers from cache.
                return null;
            }

            idx = idx2;

            IDnsServerAddressStream entries = AuthoritativeDnsServerCache().Get(hostname);
            if (entries != null)
            {
                // The returned List may contain unresolved InetSocketAddress instances that will be
                // resolved on the fly in query(....).
                return entries;
            }
        }
    }

    private void Query(IDnsServerAddressStream nameServerAddrStream,
                       int nameServerAddrStreamIndex,
                       IDnsQuestion question,
                       IDnsQueryLifecycleObserver queryLifecycleObserver,
                       bool flush,
                       TaskCompletionSource<IReadOnlyList<T>> promise,
                       Exception cause)
    {
        if (completeEarly || nameServerAddrStreamIndex >= nameServerAddrStream.Size() ||
                allowedQueries == 0 || originalPromise.IsCanceled || promise.Task.IsCanceled)
        {
            TryToFinishResolve(nameServerAddrStream, nameServerAddrStreamIndex, question, queryLifecycleObserver,
                               promise, cause);
            return;
        }

        --allowedQueries;

        EndPoint nameServerAddr = nameServerAddrStream.Next();
        if (nameServerAddr is DnsEndPoint)
        {
            QueryUnresolvedNameServer(nameServerAddr, nameServerAddrStream, nameServerAddrStreamIndex, question,
                                      queryLifecycleObserver, promise, cause);
            return;
        }

        TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> queryPromise =
                DnsNameResolver.NewPromise<IAddressedEnvelope<IDnsResponse, IPEndPoint>>();

        long queryStartTimeNanos;
        bool isFeedbackAddressStream;

        if (nameServerAddrStream is IDnsServerResponseFeedbackAddressStream)
        {
            queryStartTimeNanos = SystemTimer.NanoTime();
            isFeedbackAddressStream = true;
        }
        else
        {
            queryStartTimeNanos = -1;
            isFeedbackAddressStream = false;
        }

        TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> f =
                parent.DoQuery(channel, (IPEndPoint) nameServerAddr, question,
                        queryLifecycleObserver, additionals, flush, queryPromise);

        queriesInProgress.Add(f);

        parent.AddListener(f.Task, future =>
        {
            queriesInProgress.Remove(f);

            if (promise.Task.IsCompleted || future.IsCanceled)
            {
                queryLifecycleObserver.QueryCancelled(allowedQueries);

                // Check if we need to release the envelope itself. If the query was cancelled the getNow() will
                // return null as well as the Future will be failed with a CancellationException.
                IAddressedEnvelope<IDnsResponse, IPEndPoint> result =
                        future.IsCompletedSuccessfully ? future.Result : null;
                if (result != null)
                {
                    result.Release();
                }

                return;
            }

            Exception queryCause = DnsNameResolver.Cause(future);

            try
            {
                if (queryCause == null)
                {
                    if (isFeedbackAddressStream)
                    {
                        IDnsServerResponseFeedbackAddressStream feedbackNameServerAddrStream =
                                (IDnsServerResponseFeedbackAddressStream) nameServerAddrStream;
                        feedbackNameServerAddrStream.FeedbackSuccess(nameServerAddr,
                                SystemTimer.NanoTime() - queryStartTimeNanos);
                    }

                    OnResponse(nameServerAddrStream, nameServerAddrStreamIndex, question, future.Result,
                               queryLifecycleObserver, promise);
                }
                else
                {
                    // Server did not respond or I/O error occurred; try again.
                    if (isFeedbackAddressStream)
                    {
                        IDnsServerResponseFeedbackAddressStream feedbackNameServerAddrStream =
                                (IDnsServerResponseFeedbackAddressStream) nameServerAddrStream;
                        feedbackNameServerAddrStream.FeedbackFailure(nameServerAddr, queryCause,
                                SystemTimer.NanoTime() - queryStartTimeNanos);
                    }

                    queryLifecycleObserver.QueryFailed(queryCause);
                    Query(nameServerAddrStream, nameServerAddrStreamIndex + 1, question,
                          NewDnsQueryLifecycleObserver(question), true, promise, queryCause);
                }
            }
            finally
            {
                TryToFinishResolve(nameServerAddrStream, nameServerAddrStreamIndex, question,
                                   // queryLifecycleObserver has already been terminated at this point so we must
                                   // not allow it to be terminated again by tryToFinishResolve.
                                   NoopDnsQueryLifecycleObserver.INSTANCE,
                                   promise, queryCause);
            }
        });
    }

    private void QueryUnresolvedNameServer(EndPoint nameServerAddr,
                                           IDnsServerAddressStream nameServerAddrStream,
                                           int nameServerAddrStreamIndex,
                                           IDnsQuestion question,
                                           IDnsQueryLifecycleObserver queryLifecycleObserver,
                                           TaskCompletionSource<IReadOnlyList<T>> promise,
                                           Exception cause)
    {
        string nameServerName = ((DnsEndPoint) nameServerAddr).Host;
        Debug.Assert(nameServerName != null);

        // Placeholder so we will not try to finish the original query yet.
        TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> resolveFuture =
                DnsNameResolver.NewPromise<IAddressedEnvelope<IDnsResponse, IPEndPoint>>();
        resolveFuture.SetResult(null);
        queriesInProgress.Add(resolveFuture);

        TaskCompletionSource<IReadOnlyList<IPAddress>> resolverPromise =
                DnsNameResolver.NewPromise<IReadOnlyList<IPAddress>>();
        parent.AddListener(resolverPromise.Task, future =>
        {
            // Remove placeholder.
            queriesInProgress.Remove(resolveFuture);

            if (future.IsCompletedSuccessfully)
            {
                IReadOnlyList<IPAddress> resolvedAddresses = future.Result;
                IDnsServerAddressStream addressStream = new CombinedDnsServerAddressStream(this,
                        nameServerAddr, resolvedAddresses, nameServerAddrStream);
                Query(addressStream, nameServerAddrStreamIndex, question,
                      queryLifecycleObserver, true, promise, cause);
            }
            else
            {
                // Ignore the server and try the next one...
                Query(nameServerAddrStream, nameServerAddrStreamIndex + 1,
                      question, queryLifecycleObserver, true, promise, cause);
            }
        });
        IDnsCache resolveCache = ResolveCache();
        if (!DnsNameResolver.DoResolveAllCached(nameServerName, additionals, resolverPromise, resolveCache,
                parent.SearchDomains(), parent.Ndots(), parent.ResolvedInternetProtocolFamiliesUnsafe()))
        {

            new DnsAddressResolveContext(parent, channel,
                    originalPromise, nameServerName, additionals, parent.NewNameServerAddressStream(nameServerName),
                    // Resolving the unresolved nameserver must be limited by allowedQueries
                    // so we eventually fail
                    allowedQueries,
                    resolveCache,
                    RedirectAuthoritativeDnsServerCache_(AuthoritativeDnsServerCache()), false)
                    .Resolve(resolverPromise);
        }
    }

    private void OnResponse(IDnsServerAddressStream nameServerAddrStream, int nameServerAddrStreamIndex,
                            IDnsQuestion question, IAddressedEnvelope<IDnsResponse, IPEndPoint> envelope,
                            IDnsQueryLifecycleObserver queryLifecycleObserver,
                            TaskCompletionSource<IReadOnlyList<T>> promise)
    {
        try
        {
            IDnsResponse res = envelope.Content;
            DnsResponseCode code = res.Code();
            if (code == DnsResponseCode.NOERROR)
            {
                if (HandleRedirect(question, envelope, queryLifecycleObserver, promise))
                {
                    // Was a redirect so return here as everything else is handled in handleRedirect(...)
                    return;
                }

                DnsRecordType type = question.Type();

                if (type == DnsRecordType.CNAME)
                {
                    OnResponseCNAME(question,
                            BuildAliasMap(question.Name(), envelope.Content, CnameCache(), parent.Executor),
                            queryLifecycleObserver, promise);
                    return;
                }

                foreach (DnsRecordType expectedType in expectedTypes)
                {
                    if (type == expectedType)
                    {
                        OnExpectedResponse(question, envelope, queryLifecycleObserver, promise);
                        return;
                    }
                }

                queryLifecycleObserver.QueryFailed(UNRECOGNIZED_TYPE_QUERY_FAILED_EXCEPTION);
                return;
            }

            // Retry with the next server if the server did not tell us that the domain does not exist.
            if (code != DnsResponseCode.NXDOMAIN)
            {
                Query(nameServerAddrStream, nameServerAddrStreamIndex + 1, question,
                      queryLifecycleObserver.QueryNoAnswer(code), true, promise, Cause(code));
            }
            else
            {
                queryLifecycleObserver.QueryFailed(NXDOMAIN_QUERY_FAILED_EXCEPTION);

                // Try with the next server if is not authoritative for the domain.
                //
                // From https://tools.ietf.org/html/rfc1035 :
                //
                //   RCODE        Response code - this 4 bit field is set as part of
                //                responses.  The values have the following
                //                interpretation:
                //
                //                ....
                //                ....
                //
                //                3               Name Error - Meaningful only for
                //                                responses from an authoritative name
                //                                server, this code signifies that the
                //                                domain name referenced in the query does
                //                                not exist.
                //                ....
                //                ....
                if (!res.IsAuthoritativeAnswer())
                {
                    Query(nameServerAddrStream, nameServerAddrStreamIndex + 1, question,
                            NewDnsQueryLifecycleObserver(question), true, promise, Cause(code));
                }
                else
                {
                    // Failed with NX cause - distinction between NXDOMAIN vs a timeout
                    TryToFinishResolve(nameServerAddrStream, nameServerAddrStreamIndex, question,
                            queryLifecycleObserver, promise, NXDOMAIN_CAUSE_QUERY_FAILED_EXCEPTION);
                }
            }
        }
        finally
        {
            ReferenceCountUtil.SafeRelease(envelope);
        }
    }

    /**
     * Handles a redirect answer if needed and returns {@code true} if a redirect query has been made.
     */
    private bool HandleRedirect(
            IDnsQuestion question, IAddressedEnvelope<IDnsResponse, IPEndPoint> envelope,
            IDnsQueryLifecycleObserver queryLifecycleObserver, TaskCompletionSource<IReadOnlyList<T>> promise)
    {
        IDnsResponse res = envelope.Content;

        // Check if we have answers, if not this may be an non authority NS and so redirects must be handled.
        if (res.Count(DnsSection.ANSWER) == 0)
        {
            AuthoritativeNameServerList serverNames = ExtractAuthoritativeNameServers(question.Name(), res);
            if (serverNames != null)
            {
                int additionalCount = res.Count(DnsSection.ADDITIONAL);

                IAuthoritativeDnsServerCache authoritativeDnsServerCache = AuthoritativeDnsServerCache();

                for (int i = 0; i < additionalCount; i++)
                {
                    IDnsRecord r = res.RecordAt<IDnsRecord>(DnsSection.ADDITIONAL, i);

                    if (r.Type() == DnsRecordType.A && !parent.SupportsARecords() ||
                        r.Type() == DnsRecordType.AAAA && !parent.SupportsAAAARecords())
                    {
                        continue;
                    }

                    // We may have multiple ADDITIONAL entries for the same nameserver name. For example one AAAA and
                    // one A record.
                    serverNames.HandleWithAdditional(parent, r, authoritativeDnsServerCache);
                }

                // Process all unresolved nameservers as well.
                serverNames.HandleWithoutAdditionals(parent, ResolveCache(), authoritativeDnsServerCache);

                List<EndPoint> addresses = serverNames.AddressList();

                // Give the user the chance to sort or filter the used servers for the query.
                IDnsServerAddressStream serverStream = parent.NewRedirectDnsServerStream(
                        question.Name(), addresses);

                if (serverStream != null)
                {
                    Query(serverStream, 0, question,
                          queryLifecycleObserver.QueryRedirected(new DnsAddressStreamList(serverStream)),
                          true, promise, null);
                    return true;
                }
            }
        }

        return false;
    }

    private void OnExpectedResponse(
            IDnsQuestion question, IAddressedEnvelope<IDnsResponse, IPEndPoint> envelope,
            IDnsQueryLifecycleObserver queryLifecycleObserver, TaskCompletionSource<IReadOnlyList<T>> promise)
    {

        // We often get a bunch of CNAMES as well when we asked for A/AAAA.
        IDnsResponse response = envelope.Content;
        Dictionary<string, string> cnames = BuildAliasMap(question.Name(), response, CnameCache(), parent.Executor);
        int answerCount = response.Count(DnsSection.ANSWER);

        bool found = false;
        bool completeEarly = this.completeEarly;
        bool cnameNeedsFollow = cnames.Count != 0;

        for (int i = 0; i < answerCount; i ++)
        {
            IDnsRecord r = response.RecordAt<IDnsRecord>(DnsSection.ANSWER, i);
            DnsRecordType type = r.Type();
            bool matches = false;

            foreach (DnsRecordType expectedType in expectedTypes)
            {
                if (type == expectedType)
                {
                    matches = true;
                    break;
                }
            }

            if (!matches)
            {
                continue;
            }

            string questionName = question.Name().ToLowerInvariant();
            string recordName = r.Name().ToLowerInvariant();

            // Make sure the record is for the questioned domain.
            if (!recordName.Equals(questionName))
            {
                Dictionary<string, string> cnamesCopy = new Dictionary<string, string>(cnames);
                // Even if the record's name is not exactly same, it might be an alias defined in the CNAME records.
                string resolved = questionName;

                do
                {
                    resolved = cnamesCopy.Remove(resolved, out string next) ? next : null;
                    if (recordName.Equals(resolved))
                    {
                        // We followed a CNAME chain that was part of the response without any extra queries.
                        cnameNeedsFollow = false;
                        break;
                    }
                } while (resolved != null);

                if (resolved == null)
                {
                    Debug.Assert(questionName.Length == 0 || questionName[questionName.Length - 1] == '.');

                    foreach (string searchDomain in parent.SearchDomains())
                    {
                        if (searchDomain.Length == 0)
                        {
                            continue;
                        }

                        string fqdn;

                        if (searchDomain[searchDomain.Length - 1] == '.')
                        {
                            fqdn = questionName + searchDomain;
                        }
                        else
                        {
                            fqdn = questionName + searchDomain + '.';
                        }

                        if (recordName.Equals(fqdn))
                        {
                            resolved = recordName;
                            break;
                        }
                    }

                    if (resolved == null)
                    {
                        if (logger.IsDebugEnabled())
                        {
                            logger.Debug("{} Ignoring record {} for [{}: {}] as it contains a different name than " +
                                            "the question name [{}]. Cnames: {}, Search domains: {}",
                                    channel, r.ToString(), response.Id(), envelope.Sender,
                                    questionName, cnames, parent.SearchDomains());
                        }

                        continue;
                    }
                }
            }

            T converted = ConvertRecord(r, hostname, additionals, parent.Executor);
            if (converted == null)
            {
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("{} Ignoring record {} for [{}: {}] as the converted record is null. "
                                    + "Hostname [{}], Additionals: {}",
                            channel, r.ToString(), response.Id(),
                            envelope.Sender, hostname, additionals);
                }

                continue;
            }

            bool shouldRelease = false;

            // Check if we did determine we wanted to complete early before. If this is the case we want to not
            // include the result
            if (!completeEarly)
            {
                completeEarly = IsCompleteEarly(converted);
            }

            // Check if the promise was done already, and only if not add things to the finalResult. Otherwise lets
            // just release things after we cached it.
            if (!promise.Task.IsCompleted)
            {
                // We want to ensure we do not have duplicates in finalResult as this may be unexpected.
                //
                // While using a LinkedHashSet or HashSet may sound like the perfect fit for this we will use an
                // ArrayList here as duplicates should be found quite unfrequently in the wild and we dont want to pay
                // for the extra memory copy and allocations in this cases later on.
                if (finalResult == null)
                {
                    finalResult = new List<T>(8);
                    finalResult.Add(converted);
                }
                else if (IsDuplicateAllowed() || !finalResult.Contains(converted))
                {
                    finalResult.Add(converted);
                }
                else
                {
                    shouldRelease = true;
                }
            }
            else
            {
                shouldRelease = true;
            }

            Cache(hostname, additionals, r, converted);
            found = true;

            if (shouldRelease)
            {
                ReferenceCountUtil.Release(converted);
            }
            // Note that we do not break from the loop here, so we decode/cache all A/AAAA records.
        }

        if (found && !cnameNeedsFollow)
        {
            // If we found the correct result we can just stop here without following any extra CNAME records in the
            // response.
            if (completeEarly)
            {
                this.completeEarly = true;
            }

            queryLifecycleObserver.QuerySucceed();
        }
        else if (cnames.Count == 0)
        {
            queryLifecycleObserver.QueryFailed(NO_MATCHING_RECORD_QUERY_FAILED_EXCEPTION);
        }
        else
        {
            queryLifecycleObserver.QuerySucceed();
            // We also got a CNAME so we need to ensure we also query it.
            OnResponseCNAME(question, cnames, NewDnsQueryLifecycleObserver(question), promise);
        }
    }

    private void OnResponseCNAME(
            IDnsQuestion question, Dictionary<string, string> cnames,
            IDnsQueryLifecycleObserver queryLifecycleObserver,
            TaskCompletionSource<IReadOnlyList<T>> promise)
    {

        // Resolve the host name in the question into the real host name.
        string resolved = question.Name().ToLowerInvariant();
        bool found = false;

        while (cnames.Count != 0)
        { // Do not attempt to call Map.remove() when the Map is empty
                                    // because it can be Collections.emptyMap()
                                    // whose remove() throws a UnsupportedOperationException.
            string next = cnames.Remove(resolved, out string mapped) ? mapped : null;
            if (next != null)
            {
                found = true;
                resolved = next;
            }
            else
            {
                break;
            }
        }

        if (found)
        {
            FollowCname(question, resolved, queryLifecycleObserver, promise);
        }
        else
        {
            queryLifecycleObserver.QueryFailed(CNAME_NOT_FOUND_QUERY_FAILED_EXCEPTION);
        }
    }

    private void TryToFinishResolve(IDnsServerAddressStream nameServerAddrStream,
                                    int nameServerAddrStreamIndex,
                                    IDnsQuestion question,
                                    IDnsQueryLifecycleObserver queryLifecycleObserver,
                                    TaskCompletionSource<IReadOnlyList<T>> promise,
                                    Exception cause)
    {

        // There are no queries left to try.
        if (!completeEarly && queriesInProgress.Count != 0)
        {
            queryLifecycleObserver.QueryCancelled(allowedQueries);

            // There are still some queries in process, we will try to notify once the next one finishes until
            // all are finished.
            return;
        }

        // There are no queries left to try.
        if (finalResult == null)
        {
            if (nameServerAddrStreamIndex < nameServerAddrStream.Size())
            {
                if (queryLifecycleObserver == NoopDnsQueryLifecycleObserver.INSTANCE)
                {
                    // If the queryLifecycleObserver has already been terminated we should create a new one for this
                    // fresh query.
                    Query(nameServerAddrStream, nameServerAddrStreamIndex + 1, question,
                          NewDnsQueryLifecycleObserver(question), true, promise, cause);
                }
                else
                {
                    Query(nameServerAddrStream, nameServerAddrStreamIndex + 1, question, queryLifecycleObserver,
                          true, promise, cause);
                }

                return;
            }

            queryLifecycleObserver.QueryFailed(NAME_SERVERS_EXHAUSTED_EXCEPTION);

            // .. and we could not find any expected records.

            // The following is of questionable benefit, but has been around for a while that
            // it may be risky to remove. Reference https://datatracker.ietf.org/doc/html/rfc8020
            // - If we receive NXDOMAIN we know the domain doesn't exist, any other lookup type is meaningless.
            // - If we receive SERVFAIL, and we attempt a CNAME that returns NOERROR with 0 answers, it may lead the
            //   call-site to invalidate previously advertised addresses.
            // Having said that, there is the case of DNS services that don't respect the protocol either
            // - An A lookup could result in NXDOMAIN but a CNAME may succeed with answers.
            // It's an imperfect world. Accept it.
            // Guarding it with a system property, as an opt-in functionality.
            if (TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS)
            {
                // If cause != null we know this was caused by a timeout / cancel / transport exception. In this case we
                // won't try to resolve the CNAME as we only should do this if we could not get the expected records
                // because they do not exist and the DNS server did probably signal it.
                bool isValidResponse =
                        cause == NXDOMAIN_CAUSE_QUERY_FAILED_EXCEPTION || cause == SERVFAIL_QUERY_FAILED_EXCEPTION;
                if ((cause == null || isValidResponse) && !triedCNAME &&
                        (question.Type() == DnsRecordType.A || question.Type() == DnsRecordType.AAAA))
                {
                    // As the last resort, try to query CNAME, just in case the name server has it.
                    triedCNAME = true;

                    Query(hostname, DnsRecordType.CNAME, GetNameServers(hostname), true, promise);
                    return;
                }
            }
        }
        else
        {
            queryLifecycleObserver.QueryCancelled(allowedQueries);
        }

        // We have at least one resolved record or tried CNAME as the last resort.
        FinishResolve(promise, cause);
    }

    private void FinishResolve(TaskCompletionSource<IReadOnlyList<T>> promise, Exception cause)
    {
        // If completeEarly was true we still want to continue processing the queries to ensure we still put everything
        // in the cache eventually.
        if (!completeEarly && queriesInProgress.Count != 0)
        {
            // If there are queries in progress, we should cancel it because we already finished the resolution.
            foreach (TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> f in
                     new List<TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>>>(queriesInProgress))
            {
                queriesInProgress.Remove(f);

                f.TrySetCanceled();
            }
        }

        if (finalResult != null)
        {
            if (!promise.Task.IsCompleted)
            {
                // Found at least one resolved record.
                List<T> result = FilterResults(finalResult);
                // Lets replace the previous stored result.
                finalResult = new List<T>(0);

                if (!DnsNameResolver.TrySuccess<IReadOnlyList<T>>(promise, result))
                {
                    foreach (T item in result)
                    {
                        ReferenceCountUtil.SafeRelease(item);
                    }
                }
            }
            else
            {
                // This should always be the case as we replaced the list once notify the promise with an empty one
                // and never add to it again.
                Debug.Assert(finalResult.Count == 0);
            }

            return;
        }

        // No resolved address found.
        int maxAllowedQueries = parent.MaxQueriesPerResolve();
        int tries = maxAllowedQueries - allowedQueries;
        StringBuilder buf = new StringBuilder(64);

        buf.Append("Failed to resolve '").Append(hostname).Append("' ").Append(DnsNameResolver.ArraysToString(expectedTypes));

        if (tries > 1)
        {
            if (tries < maxAllowedQueries)
            {
                buf.Append(" after ")
                   .Append(tries.ToString(CultureInfo.InvariantCulture))
                   .Append(" queries ");
            }
            else
            {
                buf.Append(". Exceeded max queries per resolve ")
                .Append(maxAllowedQueries.ToString(CultureInfo.InvariantCulture))
                .Append(' ');
            }
        }

        UnknownHostException unknownHostException;

        if (cause == null)
        {
            unknownHostException = new UnknownHostException(buf.ToString());
            // Only cache if the failure was not because of an IO error / timeout that was caused by the query
            // itself.
            Cache(hostname, additionals, unknownHostException);
        }
        else
        {
            // CLR: initCause(cause) is the (string, Exception) constructor.
            unknownHostException = new UnknownHostException(buf.ToString(), cause);
        }

        promise.TrySetException(unknownHostException);
    }

    private IDnsServerAddressStream GetNameServers(string name)
    {
        IDnsServerAddressStream stream = GetNameServersFromCache(name);
        if (stream == null)
        {
            // We need to obtain a new stream from the parent DnsNameResolver if the hostname is not the same as
            // for the original query (for example we may follow CNAMEs). Otherwise let's just duplicate the
            // original nameservers so we correctly update the internal index
            if (name.Equals(hostname))
            {
                return nameServerAddrs.Duplicate();
            }

            return parent.NewNameServerAddressStream(name);
        }

        return stream;
    }

    private void FollowCname(IDnsQuestion question, string cname, IDnsQueryLifecycleObserver queryLifecycleObserver,
                             TaskCompletionSource<IReadOnlyList<T>> promise)
    {
        IDnsQuestion cnameQuestion;
        IDnsServerAddressStream stream;

        try
        {
            cname = CnameResolveFromCache(CnameCache(), cname);
            stream = GetNameServers(cname);
            cnameQuestion = new DefaultDnsQuestion(cname, question.Type(), dnsClass);
        }
        catch (Exception cause)
        {
            queryLifecycleObserver.QueryFailed(cause);
            throw;
        }

        Query(stream, 0, cnameQuestion, queryLifecycleObserver.QueryCNAMEd(cnameQuestion),
              true, promise, null);
    }

    private bool Query(string hostname, DnsRecordType type, IDnsServerAddressStream dnsServerAddressStream,
                          bool flush, TaskCompletionSource<IReadOnlyList<T>> promise)
    {
        IDnsQuestion question;

        try
        {
            question = new DefaultDnsQuestion(hostname, type, dnsClass);
        }
        catch (Exception cause)
        {
            // Assume a single failure means that queries will succeed. If the hostname is invalid for one type
            // there is no case where it is known to be valid for another type.
            promise.TrySetException(new ArgumentException("Unable to create DNS Question for: [" + hostname + ", " +
                    type + ']', cause));
            return false;
        }

        Query(dnsServerAddressStream, 0, question, NewDnsQueryLifecycleObserver(question), flush, promise, null);
        return true;
    }

    private IDnsQueryLifecycleObserver NewDnsQueryLifecycleObserver(IDnsQuestion question)
    {
        return parent.DnsQueryLifecycleObserverFactory().NewDnsQueryLifecycleObserver(question);
    }

    private sealed class CombinedDnsServerAddressStream : IDnsServerAddressStream
    {
        private readonly DnsResolveContext<T> context;
        private readonly EndPoint replaced;
        private readonly IDnsServerAddressStream originalStream;
        private readonly IReadOnlyList<IPAddress> resolvedAddresses;
        private int resolved;

        internal CombinedDnsServerAddressStream(DnsResolveContext<T> context, EndPoint replaced,
                                       IReadOnlyList<IPAddress> resolvedAddresses,
                                       IDnsServerAddressStream originalStream)
        {
            this.context = context;
            this.replaced = replaced;
            this.resolvedAddresses = resolvedAddresses;
            this.originalStream = originalStream;
            resolved = 0;
        }

        public EndPoint Next()
        {
            if (resolved < resolvedAddresses.Count)
            {
                return NextResolved0();
            }

            EndPoint address = originalStream.Next();
            if (address.Equals(replaced))
            {
                resolved = 0;
                return NextResolved0();
            }

            return address;
        }

        private EndPoint NextResolved0()
        {
            return context.parent.NewRedirectServerAddress(resolvedAddresses[resolved++]);
        }

        public int Size()
        {
            return originalStream.Size() + resolvedAddresses.Count - 1;
        }

        public IDnsServerAddressStream Duplicate()
        {
            return new CombinedDnsServerAddressStream(context, replaced, resolvedAddresses, originalStream.Duplicate());
        }
    }
}
