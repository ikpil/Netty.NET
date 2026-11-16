/*
 * Copyright 2012 The Netty Project
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
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public abstract class AbstractDatagramTest : AbstractComboTestsuiteTest<Bootstrap, Bootstrap>
{
    protected override List<TestsuitePermutation.IBootstrapComboFactory<Bootstrap, Bootstrap>> NewFactories()
    {
        return SocketTestPermutation.INSTANCE.Datagram(SocketInternetProtocalFamily());
    }

    protected override void Configure(Bootstrap bootstrap, Bootstrap bootstrap2, IByteBufAllocator allocator)
    {
        bootstrap.Option(ChannelOption.ALLOCATOR, allocator);
        bootstrap2.Option(ChannelOption.ALLOCATOR, allocator);
    }

    // CLR: java.net.SocketAddress/InetSocketAddress are System.Net.EndPoint/IPEndPoint.
    protected virtual EndPoint NewSocketAddress()
    {
        switch (SocketInternetProtocalFamily())
        {
            // CLR: inside this class the simple name SocketProtocolFamily binds to the SocketProtocolFamily() method
            // (the source's socketProtocolFamily() differs from the enum only by case), so enum members are qualified.
            case Channels.Sockets.SocketProtocolFamily.Inet:
                return new IPEndPoint(NetUtil.LOCALHOST4, 0);
            case Channels.Sockets.SocketProtocolFamily.Inet6:
                return new IPEndPoint(NetUtil.LOCALHOST6, 0);
            default:
                // CLR: java.lang.AssertionError has no BCL type; UnreachableException is "thrown when the program
                // executes an instruction that was thought to be unreachable"
                // (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception).
                throw new UnreachableException();
        }
    }

    // CLR: Inet4Address/Inet6Address are one IPAddress type; AddressFamily distinguishes them.
    protected virtual bool IsSupported(SocketProtocolFamily family, IPAddress address)
    {
        switch (family)
        {
            case Channels.Sockets.SocketProtocolFamily.Inet:
                return address.AddressFamily == AddressFamily.InterNetwork;
            case Channels.Sockets.SocketProtocolFamily.Inet6:
                return address.AddressFamily == AddressFamily.InterNetworkV6;
            default:
                return false;
        }
    }

    protected virtual SocketProtocolFamily SocketProtocolFamily()
    {
        return Channels.Sockets.SocketProtocolFamily.Inet;
    }

    protected virtual SocketProtocolFamily GroupInternetProtocalFamily()
    {
        return SocketProtocolFamily();
    }

    protected virtual SocketProtocolFamily SocketInternetProtocalFamily()
    {
        return SocketProtocolFamily();
    }
}
