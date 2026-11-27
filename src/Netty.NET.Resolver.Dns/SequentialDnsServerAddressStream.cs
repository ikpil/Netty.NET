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

using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace Netty.NET.Resolver.Dns;

// CLR: List<? extends InetSocketAddress> is the covariant IReadOnlyList<EndPoint> and Collection<? extends
// InetSocketAddress> IReadOnlyCollection<EndPoint>. The covariant duplicate() return is the public
// SequentialDnsServerAddressStream Duplicate() with an explicit IDnsServerAddressStream.Duplicate(), because C# covariant
// returns cover class overrides only (docs/transport-porting.md). The method-local i shadows the field as in the
// source (this.i). An address prints as EndPoint.ToString() and the index with the invariant culture.
internal sealed class SequentialDnsServerAddressStream : IDnsServerAddressStream
{

    private readonly IReadOnlyList<EndPoint> addresses;
    private int i;

    internal SequentialDnsServerAddressStream(IReadOnlyList<EndPoint> addresses, int startIdx)
    {
        this.addresses = addresses;
        i = startIdx;
    }

    public EndPoint Next()
    {
        int i = this.i;
        EndPoint next = addresses[i];
        if (++ i < addresses.Count)
        {
            this.i = i;
        }
        else
        {
            this.i = 0;
        }
        return next;
    }

    public int Size()
    {
        return addresses.Count;
    }

    public SequentialDnsServerAddressStream Duplicate()
    {
        return new SequentialDnsServerAddressStream(addresses, i);
    }

    IDnsServerAddressStream IDnsServerAddressStream.Duplicate()
    {
        return Duplicate();
    }

    public override string ToString()
    {
        return ToString("sequential", i, addresses);
    }

    internal static string ToString(string type, int index, IReadOnlyCollection<EndPoint> addresses)
    {
        StringBuilder buf = new StringBuilder(type.Length + 2 + addresses.Count * 16);
        buf.Append(type).Append("(index: ").Append(index.ToString(CultureInfo.InvariantCulture));
        buf.Append(", addrs: (");
        foreach (EndPoint a in addresses)
        {
            buf.Append(a).Append(", ");
        }

        buf.Length -= 2;
        buf.Append("))");

        return buf.ToString();
    }
}
