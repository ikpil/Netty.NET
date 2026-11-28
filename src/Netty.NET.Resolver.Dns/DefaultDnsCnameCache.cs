/*
 * Copyright 2018 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Resolver.Dns;

/**
 * Default implementation of a {@link DnsCnameCache}.
 */
// CLR: EventLoop is the Transport IEventLoop; checkNotNull/checkPositive/checkPositiveOrZero are Common's ObjectUtil and
// IllegalArgumentException is ArgumentException (numbers in the message use the invariant culture).
public sealed class DefaultDnsCnameCache : IDnsCnameCache
{
    private readonly int minTtl;
    private readonly int maxTtl;

    private readonly Cache<string> cache = new CnameCache();

    private sealed class CnameCache : Cache<string>
    {
        protected override bool ShouldReplaceAll(string entry)
        {
            // Only one 1:1 mapping is supported as specified in the RFC.
            return true;
        }

        protected override bool Equals(string entry, string otherEntry)
        {
            return AsciiString.ContentEqualsIgnoreCase(
                    entry == null ? null : new StringCharSequence(entry),
                    otherEntry == null ? null : new StringCharSequence(otherEntry));
        }
    }

    /**
     * Create a cache that respects the TTL returned by the DNS server.
     */
    public DefaultDnsCnameCache() : this(0, Cache<string>.MAX_SUPPORTED_TTL_SECS)
    {
    }

    /**
     * Create a cache.
     *
     * @param minTtl the minimum TTL
     * @param maxTtl the maximum TTL
     */
    public DefaultDnsCnameCache(int minTtl, int maxTtl)
    {
        this.minTtl = Math.Min(Cache<string>.MAX_SUPPORTED_TTL_SECS, ObjectUtil.CheckPositiveOrZero(minTtl, "minTtl"));
        this.maxTtl = Math.Min(Cache<string>.MAX_SUPPORTED_TTL_SECS, ObjectUtil.CheckPositive(maxTtl, "maxTtl"));
        if (minTtl > maxTtl)
        {
            throw new ArgumentException(
                    "minTtl: " + minTtl.ToString(CultureInfo.InvariantCulture) + ", maxTtl: " +
                    maxTtl.ToString(CultureInfo.InvariantCulture) + " (expected: 0 <= minTtl <= maxTtl)");
        }
    }

    public string Get(string hostname)
    {
        IReadOnlyList<string> cached =  cache.Get(ObjectUtil.CheckNotNull(hostname, "hostname"));
        if (cached == null || cached.Count == 0)
        {
            return null;
        }

        // We can never have more then one record.
        return cached[0];
    }

    public void Cache(string hostname, string cname, long originalTtl, IEventLoop loop)
    {
        ObjectUtil.CheckNotNull(hostname, "hostname");
        ObjectUtil.CheckNotNull(cname, "cname");
        ObjectUtil.CheckNotNull(loop, "loop");

        cache.Cache_(hostname, cname, Math.Max(minTtl, (int) Math.Min(maxTtl, originalTtl)), loop);
    }

    public void Clear()
    {
        cache.Clear();
    }

    public bool Clear(string hostname)
    {
        return cache.Clear(ObjectUtil.CheckNotNull(hostname, "hostname"));
    }

    // Package visibility for testing purposes
    internal int MinTtl()
    {
        return minTtl;
    }

    // Package visibility for testing purposes
    internal int MaxTtl()
    {
        return maxTtl;
    }
}
