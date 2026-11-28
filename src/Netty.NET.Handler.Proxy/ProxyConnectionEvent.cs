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

using System.Net;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Proxy;

// CLR: SocketAddress is EndPoint. The generic accessors <T extends SocketAddress> T proxyAddress() /
// destinationAddress() are generic methods constrained to EndPoint; Java infers T from the assignment target, a C#
// caller names it (ProxyAddress<IPEndPoint>()), and the unchecked cast (T) is a checked C# cast that throws
// InvalidCastException where the Java caller's implicit checkcast throws ClassCastException. An address prints as
// EndPoint.ToString().
public sealed class ProxyConnectionEvent
{
    private readonly string protocol;
    private readonly string authScheme;
    private readonly EndPoint proxyAddress;
    private readonly EndPoint destinationAddress;
    private string strVal;

    /**
     * Creates a new event that indicates a successful connection attempt to the destination address.
     */
    public ProxyConnectionEvent(
            string protocol, string authScheme, EndPoint proxyAddress, EndPoint destinationAddress)
    {
        this.protocol = ObjectUtil.CheckNotNull(protocol, "protocol");
        this.authScheme = ObjectUtil.CheckNotNull(authScheme, "authScheme");
        this.proxyAddress = ObjectUtil.CheckNotNull(proxyAddress, "proxyAddress");
        this.destinationAddress = ObjectUtil.CheckNotNull(destinationAddress, "destinationAddress");
    }

    /**
     * Returns the name of the proxy protocol in use.
     */
    public string Protocol()
    {
        return protocol;
    }

    /**
     * Returns the name of the authentication scheme in use.
     */
    public string AuthScheme()
    {
        return authScheme;
    }

    /**
     * Returns the address of the proxy server.
     */
    public T ProxyAddress<T>() where T : EndPoint
    {
        return (T) proxyAddress;
    }

    /**
     * Returns the address of the destination.
     */
    public T DestinationAddress<T>() where T : EndPoint
    {
        return (T) destinationAddress;
    }

    public override string ToString()
    {
        if (strVal != null)
        {
            return strVal;
        }

        StringBuilder buf = new StringBuilder(128)
            .Append(StringUtil.SimpleClassName(this))
            .Append('(')
            .Append(protocol)
            .Append(", ")
            .Append(authScheme)
            .Append(", ")
            .Append(proxyAddress)
            .Append(" => ")
            .Append(destinationAddress)
            .Append(')');

        return strVal = buf.ToString();
    }
}
