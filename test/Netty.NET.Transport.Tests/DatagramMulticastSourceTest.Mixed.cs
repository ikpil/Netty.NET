/*
 * Copyright 2021 The Netty Project
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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Tests;

public partial class DatagramMulticastSourceTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TestMixedFamilyMulticast(bool adaptive)
    {
        if (!OperatingSystem.IsLinux() || !Socket.OSSupportsIPv6)
            Assert.Skip("Requires a Linux dual-mode socket.");
        return RunMulticastAsync(adaptive, AddressFamily.InterNetworkV6, mixed: true);
    }

    private static SocketError? BoundSenderRouteError(NetworkInterface iface, IPAddress source, IPAddress multicast)
    {
        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
        socket.Bind(new IPEndPoint(source, 0));
        SetIPv4MulticastInterface(socket, iface);
        try { socket.SendTo(new byte[4], new IPEndPoint(multicast, 12345)); }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.NetworkUnreachable)
        { return error.SocketErrorCode; }
        return null;
    }

    private static void SetIPv4MulticastInterface(Socket socket, NetworkInterface iface) =>
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
            InterfaceAddress(iface, AddressFamily.InterNetwork).GetAddressBytes());
}
