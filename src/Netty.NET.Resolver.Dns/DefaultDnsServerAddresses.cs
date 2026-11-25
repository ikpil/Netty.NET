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
using System.Net;
using System.Text;

namespace Netty.NET.Resolver.Dns;

// CLR: InetSocketAddress is EndPoint; an address prints as EndPoint.ToString() ("192.168.1.2:80",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.ipendpoint.tostring?view=net-10.0) where the JDK prints
// InetSocketAddress.toString() ("/192.168.1.2:80"), as recorded in docs/handler-porting.md (Addresses).
// buf.setLength(buf.length() - 2) is StringBuilder.Length -= 2.
internal abstract class DefaultDnsServerAddresses : DnsServerAddresses
{

    protected readonly List<EndPoint> addresses;
    private readonly string strVal;

    internal DefaultDnsServerAddresses(string type, List<EndPoint> addresses)
    {
        this.addresses = addresses;

        StringBuilder buf = new StringBuilder(type.Length + 2 + addresses.Count * 16);
        buf.Append(type).Append('(');

        foreach (EndPoint a in addresses)
        {
            buf.Append(a).Append(", ");
        }

        buf.Length -= 2;
        buf.Append(')');

        strVal = buf.ToString();
    }

    public override string ToString()
    {
        return strVal;
    }
}
