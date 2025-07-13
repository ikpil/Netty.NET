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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

internal static class NetUtilInitializations
{
    /**
     * The logger being used by this class
     */
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(NetUtilInitializations));

    public static IPAddress createLocalhost4()
    {
        byte[] LOCALHOST4_BYTES = { 127, 0, 0, 1 };

        IPAddress localhost4 = new IPAddress(LOCALHOST4_BYTES);

        return localhost4;
    }

    public static IPAddress createLocalhost6()
    {
        byte[] LOCALHOST6_BYTES = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 };

        IPAddress localhost6 = new IPAddress(LOCALHOST6_BYTES);

        return localhost6;
    }

    public static List<NetworkInterface> networkInterfaces()
    {
        List<NetworkInterface> networkInterfaces = new List<NetworkInterface>();
        try
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            if (0 < interfaces.Length)
            {
                foreach (var iface in interfaces)
                    networkInterfaces.Add(iface);
            }
        }
        catch (Exception e)
        {
            logger.warn("Failed to retrieve the list of available network interfaces", e);
            throw;
        }

        return networkInterfaces;
    }

    public static NetworkIfaceAndInetAddress determineLoopback(
        ICollection<NetworkInterface> networkInterfaces, IPAddress localhost4, IPAddress localhost6)
    {
        // Retrieve the list of available network interfaces.
        List<NetworkInterface> ifaces = new List<NetworkInterface>();
        foreach (NetworkInterface iface in networkInterfaces)
        {
            // Use the interface with proper INET addresses only.
            if (0 < SocketUtils.addressesFromNetworkInterface(iface).Count)
            {
                ifaces.Add(iface);
            }
        }

        // Find the first loopback interface available from its INET address (127.0.0.1 or ::1)
        // Note that we do not use NetworkInterface.isLoopback() in the first place because it takes long time
        // on a certain environment. (e.g. Windows with -Djava.net.preferIPv4Stack=true)
        NetworkInterface loopbackIface = null;
        IPAddress loopbackAddr = null;
        foreach (NetworkInterface iface in ifaces)
        {
            var addrs = SocketUtils.addressesFromNetworkInterface(iface);
            foreach (IPAddress addr in addrs)
            {
                if (IPAddress.IsLoopback(addr))
                {
                    // Found
                    loopbackIface = iface;
                    loopbackAddr = addr;
                    break;
                }
            }

            if (null != loopbackAddr)
                break;
        }

        // If failed to find the loopback interface from its INET address, fall back to isLoopback().
        if (loopbackIface == null)
        {
            try
            {
                foreach (NetworkInterface iface in ifaces)
                {
                    if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        var addrs = SocketUtils.addressesFromNetworkInterface(iface);
                        foreach (IPAddress addr in addrs)
                        {
                            // Found the one with INET address.
                            loopbackIface = iface;
                            loopbackAddr = addr;
                            break;
                        }

                        if (null != loopbackAddr)
                            break;
                    }
                }

                if (loopbackIface == null)
                {
                    logger.warn("Failed to find the loopback interface");
                }
            }
            catch (SocketException e)
            {
                logger.warn("Failed to find the loopback interface", e);
            }
        }

        if (loopbackIface != null)
        {
            // Found the loopback interface with an INET address.
            logger.debug($"Loopback interface: {loopbackIface.Name} ({loopbackIface.Description}, {loopbackAddr})");
        }
        else
        {
            // Could not find the loopback interface, but we can't leave LOCALHOST as null.
            // Use LOCALHOST6 or LOCALHOST4, preferably the IPv6 one.
            if (loopbackAddr == null)
            {
                try
                {
                    bool same = ifaces.Select(x => x.GetIPProperties())
                        .SelectMany(x => x.UnicastAddresses)
                        .Any(x => x.Address.Equals(localhost6));
                    if (same)
                    {
                        logger.debug($"Using hard-coded IPv6 localhost address: {localhost6}");
                        loopbackAddr = localhost6;
                    }
                }
                catch (Exception e)
                {
                    // Ignore
                }
                finally
                {
                    if (loopbackAddr == null)
                    {
                        logger.debug($"Using hard-coded IPv4 localhost address: {localhost4}");
                        loopbackAddr = localhost4;
                    }
                }
            }
        }

        return new NetworkIfaceAndInetAddress(loopbackIface, loopbackAddr);
    }
}