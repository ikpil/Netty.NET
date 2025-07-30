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

using System.Collections.Generic;
using System.IO;
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
    private static IEnumerable<T> empty<T>()
    {
        return Enumerable.Empty<T>();
    }

    public static void connect(Socket socket, SocketAddress remoteAddress, int timeout)
    {
        try
        {
            socket.Connect(remoteAddress, timeout);
        }
        catch (PrivilegedActionException e)
        {
            throw (IOException)e.getCause();
        }
    }

    public static void bind(Socket socket, SocketAddress bindpoint)
    {
        try
        {
            socket.bind(bindpoint);
        }
        catch (PrivilegedActionException e)
        {
            throw (IOException)e.getCause();
        }
    }

    public static bool connect(SocketChannel socketChannel, SocketAddress remoteAddress)
    {
        try
        {
            return socketChannel.connect(remoteAddress);
        }
        catch (PrivilegedActionException e)
        {
            throw (IOException)e.getCause();
        }
    }

    public static void bind(SocketChannel socketChannel, SocketAddress address)
    {
        try
        {
            socketChannel.bind(address);
        }
        catch (PrivilegedActionException e)
        {
            throw (IOException)e.getCause();
        }
    }

    public static SocketChannel accept(ServerSocketChannel serverSocketChannel)
    {
        try
        {
            return serverSocketChannel.accept();
        }
        catch (PrivilegedActionException e)
        {
            throw (IOException)e.getCause();
        }
    }

    public static void bind(DatagramChannel networkChannel, final SocketAddress address) {
        try
        {
            networkChannel.bind(address);
        }
        catch (PrivilegedActionException e)
        {
            throw (IOException)e.getCause();
        }
    }

    public static SocketAddress localSocketAddress(ServerSocket socket)
    {
        return socket.getLocalSocketAddress();
    }

    public static IPAddress addressByName(string hostname)
    {
        try
        {
            return IPAddress.getByName(hostname);
        }
        catch (PrivilegedActionException e)
        {
            throw (UnknownHostException)e.getCause();
        }
    }

    public static IPAddress[] allAddressesByName(string hostname)
    {
        try
        {
            return IPAddress.getAllByName(hostname);
        }
        catch (PrivilegedActionException e)
        {
            throw (UnknownHostException)e.getCause();
        }
    }

    public static InetSocketAddress socketAddress(string hostname, final int port) {
        return new InetSocketAddress(hostname, port);
    }

    public static List<IPAddress> addressesFromNetworkInterface(NetworkInterface intf)
    {
        var list = new List<IPAddress>();
        var ipProps = intf.GetIPProperties();

        // Android seems to sometimes return null even if this is not a valid return value by the api docs.
        // Just return an empty Enumeration in this case.
        // See https://github.com/netty/netty/issues/10045
        if (ipProps == null || ipProps.UnicastAddresses == null)
            return list;

        foreach (var unicast in ipProps.UnicastAddresses)
        {
            if (unicast?.Address != null)
            {
                list.Add(unicast.Address);
            }
        }

        return list;
    }

    public static IPAddress loopbackAddress()
    {
        return IPAddress.Loopback;
    }

    public static byte[] hardwareAddressFromNetworkInterface(NetworkInterface intf)
    {
        try
        {
            return intf.getHardwareAddress();
        }
        catch (PrivilegedActionException e)
        {
            throw (SocketException)e.getCause();
        }
    }
}