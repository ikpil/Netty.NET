/*
 * Copyright 2020 The Netty Project
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

using System.Globalization;

namespace Netty.NET.Resolver.Dns;

/**
 * Represents options defined in a file of the format <a href=https://linux.die.net/man/5/resolver>etc/resolv.conf</a>.
 */
// CLR: package-private members are internal. Builder's private constructor is internal, because a containing C# type
// cannot reach the private members of a nested type, while Java's enclosing class can. getClass().getSimpleName() is
// GetType().Name and the numbers use the invariant culture.
internal sealed class UnixResolverOptions
{

    private readonly int ndots;
    private readonly int timeout;
    private readonly int attempts;

    internal UnixResolverOptions(int ndots, int timeout, int attempts)
    {
        this.ndots = ndots;
        this.timeout = timeout;
        this.attempts = attempts;
    }

    internal static UnixResolverOptions.Builder NewBuilder()
    {
        return new UnixResolverOptions.Builder();
    }

    /**
     * The number of dots which must appear in a name before an initial absolute query is made.
     * The default value is {@code 1}.
     */
    internal int Ndots()
    {
        return ndots;
    }

    /**
     * The timeout of each DNS query performed by this resolver (in seconds).
     * The default value is {@code 5}.
     */
    internal int Timeout()
    {
        return timeout;
    }

    /**
     * The maximum allowed number of DNS queries to send when resolving a host name.
     * The default value is {@code 16}.
     */
    internal int Attempts()
    {
        return attempts;
    }

    public override string ToString()
    {
        return GetType().Name +
                "{ndots=" + ndots.ToString(CultureInfo.InvariantCulture) +
                ", timeout=" + timeout.ToString(CultureInfo.InvariantCulture) +
                ", attempts=" + attempts.ToString(CultureInfo.InvariantCulture) +
                '}';
    }

    internal sealed class Builder
    {

        private int ndots = 1;
        private int timeout = 5;
        private int attempts = 16;

        internal Builder()
        {
        }

        internal void SetNdots(int ndots)
        {
            this.ndots = ndots;
        }

        internal void SetTimeout(int timeout)
        {
            this.timeout = timeout;
        }

        internal void SetAttempts(int attempts)
        {
            this.attempts = attempts;
        }

        internal UnixResolverOptions Build()
        {
            return new UnixResolverOptions(ndots, timeout, attempts);
        }
    }
}
