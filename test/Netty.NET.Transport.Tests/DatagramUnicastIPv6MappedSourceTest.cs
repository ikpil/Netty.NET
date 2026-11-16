/*
 * Copyright 2019 The Netty Project
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
using System.Net;
using Netty.NET.Common;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class DatagramUnicastIPv6MappedSourceTest : DatagramUnicastIPv6SourceTest
{
    protected override EndPoint NewSocketAddress()
    {
        // Explicit use IPv4 wildcard for bind while the channel is using SocketProtocolFamily.INET6.
        // CLR: InetAddress.getByAddress(byte[]) throws UnknownHostException "if IP address is of illegal length"
        // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/net/InetAddress.html); IPAddress(byte[])
        // throws ArgumentException when "address contains a bad IP address"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.-ctor). IllegalStateException is
        // InvalidOperationException, "thrown when a method call is invalid for the object's current state"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.invalidoperationexception).
        try
        {
            return new IPEndPoint(new IPAddress(new byte[] {0, 0 , 0, 0}), 0);
        }
        catch (ArgumentException e)
        {
            throw new InvalidOperationException(e.Message, e);
        }
    }

    protected override IPEndPoint ConvertAnyAddress(IPEndPoint serverAddress)
    {
        IPAddress addr = serverAddress.Address;
        if (IsAnyLocalAddress(addr))
        {
            return new IPEndPoint(NetUtil.LOCALHOST4, serverAddress.Port);
        }
        return serverAddress;
    }

    protected override bool DisconnectMightFail(IDatagramChannel channel)
    {
        // See https://bugs.openjdk.org/browse/JDK-8285515
        // CLR: the source tolerates a failed disconnect only for NioDatagramChannel below Java 20, where OpenJDK's
        // native DatagramChannelImpl.disconnect0 failed with "Invalid argument" on macOS
        // (https://github.com/openjdk/jdk/commit/269eae61894b6bd0a7512045a369b53df747f6e5). This port's
        // NioDatagramChannel disconnects without that JDK code and PlatformDependent.javaVersion() has no CLR
        // counterpart, so only the inherited answer remains, as on Java 20 and later.
        return base.DisconnectMightFail(channel);
    }
}
