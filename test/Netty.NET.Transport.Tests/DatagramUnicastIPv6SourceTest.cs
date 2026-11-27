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

using System.Net.Sockets;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class DatagramUnicastIPv6SourceTest : DatagramUnicastInetSourceTest
{
    // CLR: JUnit runs the static @BeforeAll assumeIpv6Supported() once before the class's tests and aborts them all
    // when it throws TestAbortedException. "xUnit.net creates a new instance of the test class for every test that is
    // run" (https://xunit.net/docs/shared-context), so the constructor runs the same probe before each inherited test,
    // and Assert.Skip (https://xunit.net/docs/getting-started/v3/whats-new) there reports the test as skipped.
    public DatagramUnicastIPv6SourceTest()
    {
        AssumeIpv6Supported();
    }

    public static void AssumeIpv6Supported()
    {
        // CLR: SelectorProvider.provider().openDatagramChannel(StandardProtocolFamily.INET6) opens a JDK UDP channel;
        // the BCL counterpart is an IPv6 UDP Socket. Where the JDK throws UnsupportedOperationException, the BCL fails
        // with SocketError.AddressFamilyNotSupported, "returned if the IPv6 address family was specified and the IPv6
        // stack is not installed on the local machine"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror); any other SocketException
        // is the IOException the source ignores.
        try
        {
            Socket channel = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            channel.Close();
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressFamilyNotSupported)
        {
           Assert.Skip("IPv6 not supported");
        }
        catch (SocketException)
        {
            // Ignore
        }
    }

    protected override SocketProtocolFamily SocketProtocolFamily()
    {
        return Channels.Sockets.SocketProtocolFamily.Inet6;
    }
}
