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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Netty.NET.Common.Internal;

/**
 * Provides socket operations with privileges enabled. This is necessary for applications that use the
 * {@link SecurityManager} to restrict {@link SocketPermission} to their application. By asserting that these
 * operations are privileged, the operations can proceed even if some code in the calling chain lacks the appropriate
 * {@link SocketPermission}.
 */
public static class SocketUtils
{
    public static void connect(Socket socket, IPAddress remoteAddress, int timeout)
    {
        socket.Connect(remoteAddress, timeout);
    }

    public static void bind(Socket socket, EndPoint bindpoint)
    {
        socket.Bind(bindpoint);
    }

    public static bool connect(TcpClient socketChannel, IPEndPoint remoteAddress)
    {
        try
        {
            socketChannel.Connect(remoteAddress);
            return true;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public static Socket accept(Socket serverSocketChannel)
    {
        return serverSocketChannel.Accept();
    }

    public static EndPoint localSocketAddress(Socket socket)
    {
        return socket.LocalEndPoint;
    }

    public static IPAddress addressByName(string hostname)
    {
        IPHostEntry hostEntry = Dns.GetHostEntry(hostname);

        foreach (var ipAddress in hostEntry.AddressList)
        {
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
                return ipAddress;
        }

        if (hostEntry.AddressList.Length > 0)
        {
            return hostEntry.AddressList[0];
        }

        return IPAddress.None;
    }

    public static IPAddress[] allAddressesByName(string hostname)
    {
        var hostEntry = Dns.GetHostEntry(hostname);
        return hostEntry.AddressList;
    }

    public static IPEndPoint socketAddress(string hostname, int port)
    {
        var ipAddress = addressByName(hostname);
        return new IPEndPoint(ipAddress, port);
    }

    public static IEnumerable<IPAddress> addressesFromNetworkInterface(NetworkInterface intf)
    {
        IPInterfaceProperties properties = intf.GetIPProperties();

        // Android seems to sometimes return null even if this is not a valid return value by the api docs.
        // Just return an empty Enumeration in this case.
        // See https://github.com/netty/netty/issues/10045
        return properties.UnicastAddresses
            .Select(addrInfo => addrInfo.Address)
            .ToList();
    }

    public static IPAddress loopbackAddress()
    {
        return IPAddress.Loopback;
    }

    public static byte[] hardwareAddressFromNetworkInterface(NetworkInterface intf)
    {
        PhysicalAddress macAddress = intf.GetPhysicalAddress();
        if (macAddress == PhysicalAddress.None)
        {
            return null;
        }

        return macAddress.GetAddressBytes();
    }
}