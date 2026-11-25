/*
 * Copyright 2016 The Netty Project
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
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

/**
 * Default implementation of {@link DnsCache}, backed by a {@link ConcurrentMap}.
 * If any additional {@link DnsRecord} is used, no caching takes place.
 */
// CLR: InetAddress is IPAddress, Throwable is Exception, EventLoop the Transport IEventLoop and List<? extends
// DnsCacheEntry> IReadOnlyList<IDnsCacheEntry>; checkNotNull/checkPositiveOrZero are Common's ObjectUtil and
// IllegalArgumentException is ArgumentException. Numbers in toString() and messages use the invariant culture.
public class DefaultDnsCache : IDnsCache
{

    // CLR: the anonymous Cache<DefaultDnsCacheEntry> subclass is the named nested DnsCacheEntryCache.
    private readonly Cache<DefaultDnsCacheEntry> resolveCache = new DnsCacheEntryCache();

    private sealed class DnsCacheEntryCache : Cache<DefaultDnsCacheEntry>
    {

        protected override bool ShouldReplaceAll(DefaultDnsCacheEntry entry)
        {
            return entry.Cause() != null;
        }

        protected override bool Equals(DefaultDnsCacheEntry entry, DefaultDnsCacheEntry otherEntry)
        {
            if (entry.Address() != null)
            {
                return entry.Address().Equals(otherEntry.Address());
            }
            if (otherEntry.Address() != null)
            {
                return false;
            }
            return entry.Cause().Equals(otherEntry.Cause());
        }
    }

    private readonly int minTtl;
    private readonly int maxTtl;
    private readonly int negativeTtl;

    /**
     * Create a cache that respects the TTL returned by the DNS server
     * and doesn't cache negative responses.
     */
    public DefaultDnsCache() : this(0, Cache<DefaultDnsCacheEntry>.MAX_SUPPORTED_TTL_SECS, 0)
    {
    }

    /**
     * Create a cache.
     * @param minTtl the minimum TTL
     * @param maxTtl the maximum TTL
     * @param negativeTtl the TTL for failed queries
     */
    public DefaultDnsCache(int minTtl, int maxTtl, int negativeTtl)
    {
        this.minTtl = Math.Min(Cache<DefaultDnsCacheEntry>.MAX_SUPPORTED_TTL_SECS, ObjectUtil.CheckPositiveOrZero(minTtl, "minTtl"));
        this.maxTtl = Math.Min(Cache<DefaultDnsCacheEntry>.MAX_SUPPORTED_TTL_SECS, ObjectUtil.CheckPositiveOrZero(maxTtl, "maxTtl"));
        if (minTtl > maxTtl)
        {
            throw new ArgumentException(
                    "minTtl: " + minTtl.ToString(CultureInfo.InvariantCulture) + ", maxTtl: " +
                    maxTtl.ToString(CultureInfo.InvariantCulture) + " (expected: 0 <= minTtl <= maxTtl)");
        }
        this.negativeTtl = Math.Min(Cache<DefaultDnsCacheEntry>.MAX_SUPPORTED_TTL_SECS, ObjectUtil.CheckPositiveOrZero(negativeTtl, "negativeTtl"));
    }

    /**
     * Returns the minimum TTL of the cached DNS resource records (in seconds).
     *
     * @see #maxTtl()
     */
    public int MinTtl()
    {
        return minTtl;
    }

    /**
     * Returns the maximum TTL of the cached DNS resource records (in seconds).
     *
     * @see #minTtl()
     */
    public int MaxTtl()
    {
        return maxTtl;
    }

    /**
     * Returns the TTL of the cache for the failed DNS queries (in seconds). The default value is {@code 0}, which
     * disables the cache for negative results.
     */
    public int NegativeTtl()
    {
        return negativeTtl;
    }

    public void Clear()
    {
        resolveCache.Clear();
    }

    public bool Clear(string hostname)
    {
        ObjectUtil.CheckNotNull(hostname, "hostname");
        return resolveCache.Clear(AppendDot(hostname));
    }

    private static bool EmptyAdditionals(IDnsRecord[] additionals)
    {
        return additionals == null || additionals.Length == 0;
    }

    public IReadOnlyList<IDnsCacheEntry> Get(string hostname, IDnsRecord[] additionals)
    {
        ObjectUtil.CheckNotNull(hostname, "hostname");
        if (!EmptyAdditionals(additionals))
        {
            return Array.Empty<IDnsCacheEntry>();
        }

        IReadOnlyList<IDnsCacheEntry> entries = resolveCache.Get(AppendDot(hostname));
        if (entries == null || entries.Count == 0)
        {
            return entries;
        }
        return new DnsCacheEntryList(entries);
    }

    public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additionals,
                               IPAddress address, long originalTtl, IEventLoop loop)
    {
        ObjectUtil.CheckNotNull(hostname, "hostname");
        ObjectUtil.CheckNotNull(address, "address");
        ObjectUtil.CheckNotNull(loop, "loop");
        DefaultDnsCacheEntry e = new DefaultDnsCacheEntry(hostname, address);
        if (maxTtl == 0 || !EmptyAdditionals(additionals))
        {
            return e;
        }
        resolveCache.Cache_(AppendDot(hostname), e, Math.Max(minTtl, (int) Math.Min(maxTtl, originalTtl)), loop);
        return e;
    }

    public IDnsCacheEntry Cache(string hostname, IDnsRecord[] additionals, Exception cause, IEventLoop loop)
    {
        ObjectUtil.CheckNotNull(hostname, "hostname");
        ObjectUtil.CheckNotNull(cause, "cause");
        ObjectUtil.CheckNotNull(loop, "loop");

        DefaultDnsCacheEntry e = new DefaultDnsCacheEntry(hostname, cause);
        if (negativeTtl == 0 || !EmptyAdditionals(additionals))
        {
            return e;
        }

        resolveCache.Cache_(AppendDot(hostname), e, negativeTtl, loop);
        return e;
    }

    public override string ToString()
    {
        return new StringBuilder()
                .Append("DefaultDnsCache(minTtl=")
                .Append(minTtl.ToString(CultureInfo.InvariantCulture)).Append(", maxTtl=")
                .Append(maxTtl.ToString(CultureInfo.InvariantCulture)).Append(", negativeTtl=")
                .Append(negativeTtl.ToString(CultureInfo.InvariantCulture)).Append(", cached resolved hostname=")
                .Append(resolveCache.Size().ToString(CultureInfo.InvariantCulture)).Append(')')
                .ToString();
    }

    // CLR: System.identityHashCode is RuntimeHelpers.GetHashCode (docs/handler-porting.md, Channel internals);
    // cause.toString() is ThrowableUtil.ThrowableToString (owner decision 2026-11-24) and InetAddress.toString()
    // ("hostname/literal") is IPAddress.ToString(), the literal, because IPAddress carries no host name.
    private sealed class DefaultDnsCacheEntry : IDnsCacheEntry
    {
        private readonly string hostname;
        private readonly IPAddress address;
        private readonly Exception cause;
        private readonly int hash;

        internal DefaultDnsCacheEntry(string hostname, IPAddress address)
        {
            this.hostname = hostname;
            this.address = address;
            cause = null;
            hash = RuntimeHelpers.GetHashCode(this);
        }

        internal DefaultDnsCacheEntry(string hostname, Exception cause)
        {
            this.hostname = hostname;
            this.cause = cause;
            address = null;
            hash = RuntimeHelpers.GetHashCode(this);
        }

        private DefaultDnsCacheEntry(DefaultDnsCacheEntry entry)
        {
            this.hostname = entry.hostname;
            if (entry.cause == null)
            {
                this.address = entry.address;
                this.cause = null;
            }
            else
            {
                this.address = null;
                this.cause = CopyThrowable(entry.cause);
            }
            this.hash = entry.hash;
        }

        public IPAddress Address()
        {
            return address;
        }

        public Exception Cause()
        {
            return cause;
        }

        internal string Hostname()
        {
            return hostname;
        }

        public override string ToString()
        {
            if (cause != null)
            {
                return hostname + '/' + ThrowableUtil.ThrowableToString(cause);
            }
            else
            {
                return address.ToString();
            }
        }

        public override int GetHashCode()
        {
            return hash;
        }

        public override bool Equals(object obj)
        {
            return (obj is DefaultDnsCacheEntry) && ((DefaultDnsCacheEntry) obj).hash == hash;
        }

        internal IDnsCacheEntry CopyIfNeeded()
        {
            if (cause == null)
            {
                return this;
            }
            return new DefaultDnsCacheEntry(this);
        }
    }

    // CLR: StringUtil.endsWith(hostname, '.') is Common's StringUtil.EndsWith.
    private static string AppendDot(string hostname)
    {
        return StringUtil.EndsWith(hostname, '.') ? hostname : hostname + '.';
    }

    // CLR: MemberwiseClone of an Exception instance, invoked through reflection because it is protected on object.
    private static readonly MethodInfo MEMBERWISE_CLONE =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

    private static Exception CopyThrowable(Exception error)
    {
        // CLR: UnknownHostException is Common's UnknownHostException (owner decision 2026-11-25). The anonymous
        // UnknownHostException subclass only overrides fillInStackTrace(), which the JDK constructors call to record the
        // stack (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Throwable.html); a CLR exception
        // records its stack trace when it is thrown and a new one has none
        // (https://learn.microsoft.com/en-us/dotnet/api/system.exception.stacktrace?view=net-10.0), so the copy is a
        // plain UnknownHostException with the same message, and initCause(error.getCause()) is its (string, Exception)
        // constructor. setStackTrace(error.getStackTrace()) is ExceptionDispatchInfo.SetRemoteStackTrace with the
        // original's stack trace text when it was thrown
        // (https://learn.microsoft.com/en-us/dotnet/api/system.runtime.exceptionservices.exceptiondispatchinfo.setremotestacktrace?view=net-10.0).
        if (error.GetType() == typeof(UnknownHostException))
        {
            // Fast-path as this is the only type of Throwable that our implementation ever add to the cache.
            UnknownHostException copy = new UnknownHostException(error.Message, error.InnerException);
            string stackTrace = error.StackTrace;
            if (stackTrace != null)
            {
                ExceptionDispatchInfo.SetRemoteStackTrace(copy, stackTrace);
            }
            return copy;
        }

        try
        {
            // Throwable is Serializable so lets just do a deep copy.
            // CLR: Java serialization (ObjectOutputStream/ObjectInputStream) has no counterpart: from .NET 9 the in-box
            // BinaryFormatter "throws exceptions on use"
            // (https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-security-guide). The copy is
            // Object.MemberwiseClone, "a shallow copy by creating a new object, and then copying the nonstatic fields"
            // (https://learn.microsoft.com/en-us/dotnet/api/system.object.memberwiseclone?view=net-10.0): a new exception
            // of the same type with the same message, inner exception and stack trace. The purpose of the copy (a caller
            // that adds suppressed exceptions must not change the cached one) holds because Common's
            // ThrowableUtil.AddSuppressed keys the suppressed list by exception identity; the Exception.Data dictionary,
            // when the original has one, is shared by the shallow copy.
            return (Exception) MEMBERWISE_CLONE.Invoke(error, null);
        }
        catch (Exception e) when (e is TargetInvocationException || e is MemberAccessException)
        {
            // CLR: new IllegalStateException(e) is InvalidOperationException with the cause's Throwable.toString() text.
            throw new InvalidOperationException(ThrowableUtil.ThrowableToString(e), e);
        }
    }

    // CLR: AbstractList<DnsCacheEntry> is IReadOnlyList<IDnsCacheEntry>. AbstractList.hashCode() and equals(Object) (an
    // element-wise comparison with another List) are kept with an IReadOnlyList<IDnsCacheEntry> as the CLR list.
    private sealed class DnsCacheEntryList : IReadOnlyList<IDnsCacheEntry>
    {
        private readonly IReadOnlyList<IDnsCacheEntry> entries;

        internal DnsCacheEntryList(IReadOnlyList<IDnsCacheEntry> entries)
        {
            this.entries = entries;
        }

        public IDnsCacheEntry this[int index]
        {
            get
            {
                DefaultDnsCacheEntry entry = (DefaultDnsCacheEntry) entries[index];
                // As we dont know what exactly the user is doing with the returned exception (for example
                // using addSuppressed(...) and so hold up a lot of memory until the entry expires) we do
                // create a copy.
                return entry.CopyIfNeeded();
            }
        }

        public int Count => entries.Count;

        public IEnumerator<IDnsCacheEntry> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override int GetHashCode()
        {
            // Just delegate to super to make checkstyle happy
            // CLR: AbstractList.hashCode(): 31 * h + element hash over the elements.
            int hashCode = 1;
            foreach (IDnsCacheEntry e in this)
            {
                hashCode = unchecked(31 * hashCode + (e == null ? 0 : e.GetHashCode()));
            }
            return hashCode;
        }

        public override bool Equals(object o)
        {
            if (o is DnsCacheEntryList)
            {
                // Fast-path.
                return ListEquals(entries, ((DnsCacheEntryList) o).entries);
            }
            return o is IReadOnlyList<IDnsCacheEntry> other && ListEquals(this, other);
        }

        // CLR: List.equals(Object): the same size and pairwise equal elements in order.
        private static bool ListEquals(IReadOnlyList<IDnsCacheEntry> a, IReadOnlyList<IDnsCacheEntry> b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (!Equals(a[i], b[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
