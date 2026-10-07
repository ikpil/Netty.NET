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
using System.Net;
using System.Net.NetworkInformation;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

internal static class NetUtilInitializations
{
    /**
     * The logger being used by this class
     */
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(NetUtilInitializations));

    public static IReadOnlyList<NetworkInterface> NetworkInterfaces()
    {
        return NetworkInterfaces(NetworkInterface.GetAllNetworkInterfaces);
    }

    internal static IReadOnlyList<NetworkInterface> NetworkInterfaces(Func<IEnumerable<NetworkInterface>> getInterfaces)
    {
        ArgumentNullException.ThrowIfNull(getInterfaces);
        List<NetworkInterface> networkInterfaces = new List<NetworkInterface>();
        try
        {
            foreach (NetworkInterface iface in getInterfaces())
            {
                networkInterfaces.Add(iface);
            }
        }
        catch (NetworkInformationException e)
        {
            logger.Warn("Failed to retrieve the list of available network interfaces", e);
        }

        return networkInterfaces.AsReadOnly();
    }

    public static (NetworkInterface Iface, IPAddress Address) DetermineLoopback(
        IReadOnlyList<NetworkInterface> networkInterfaces, IPAddress localhost4, IPAddress localhost6)
    {
        return DetermineLoopback(networkInterfaces, localhost4, localhost6, IsAddressAssigned);
    }

    internal static (NetworkInterface Iface, IPAddress Address) DetermineLoopback(
        IReadOnlyList<NetworkInterface> networkInterfaces, IPAddress localhost4, IPAddress localhost6,
        Func<IPAddress, bool> isAddressAssigned)
    {
        ArgumentNullException.ThrowIfNull(networkInterfaces);
        ArgumentNullException.ThrowIfNull(localhost4);
        ArgumentNullException.ThrowIfNull(localhost6);
        ArgumentNullException.ThrowIfNull(isAddressAssigned);
        // Retrieve the list of available network interfaces.
        List<NetworkInterface> ifaces = new List<NetworkInterface>();
        foreach (NetworkInterface iface in networkInterfaces)
        {
            // Use the interface with proper INET addresses only.
            try
            {
                if (iface.GetIPProperties().UnicastAddresses.Count != 0)
                {
                    ifaces.Add(iface);
                }
            }
            catch (NetworkInformationException e)
            {
                logger.Warn("Failed to retrieve the addresses of a network interface: {}", iface, e);
            }
        }

        // Find the first loopback interface available from its INET address (127.0.0.1 or ::1)
        // Note that we do not use NetworkInterface.isLoopback() in the first place because it takes long time
        // on a certain environment. (e.g. Windows with -Djava.net.preferIPv4Stack=true)
        NetworkInterface loopbackIface = null;
        IPAddress loopbackAddr = null;
        foreach (NetworkInterface iface in ifaces)
        {
            try
            {
                var addrs = iface.GetIPProperties().UnicastAddresses;
                foreach (UnicastIPAddressInformation address in addrs)
                {
                    IPAddress addr = address.Address;
                    if (IsLoopbackAddress(addr))
                    {
                        // Found
                        loopbackIface = iface;
                        loopbackAddr = addr;
                        break;
                    }
                }
            }
            catch (NetworkInformationException e)
            {
                logger.Warn("Failed to retrieve the addresses of a network interface: {}", iface, e);
            }

            if (null != loopbackAddr)
                break;
        }

        // If failed to find the loopback interface from its INET address, fall back to isLoopback().
        if (loopbackIface == null)
        {
            foreach (NetworkInterface iface in ifaces)
            {
                try
                {
                    if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        var addrs = iface.GetIPProperties().UnicastAddresses;
                        foreach (UnicastIPAddressInformation address in addrs)
                        {
                            IPAddress addr = address.Address;
                            // Found the one with INET address.
                            loopbackIface = iface;
                            loopbackAddr = addr;
                            break;
                        }

                        if (null != loopbackAddr)
                            break;
                    }
                }
                catch (NetworkInformationException e)
                {
                    logger.Warn("Failed to inspect a possible loopback interface: {}", iface, e);
                }
            }

            if (loopbackIface == null)
            {
                logger.Warn("Failed to find the loopback interface");
            }
        }

        if (loopbackIface != null)
        {
            // Found the loopback interface with an INET address.
            logger.Debug($"Loopback interface: {loopbackIface.Name} ({loopbackIface.Description}, {loopbackAddr})");
        }
        else
        {
            // Could not find the loopback interface, but we can't leave LOCALHOST as null.
            // Use LOCALHOST6 or LOCALHOST4, preferably the IPv6 one.
            if (loopbackAddr == null)
            {
                try
                {
                    if (isAddressAssigned(localhost6))
                    {
                        logger.Debug($"Using hard-coded IPv6 localhost address: {localhost6}");
                        loopbackAddr = localhost6;
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // Ignore
                }
                finally
                {
                    if (loopbackAddr == null)
                    {
                        logger.Debug($"Using hard-coded IPv4 localhost address: {localhost4}");
                        loopbackAddr = localhost4;
                    }
                }
            }
        }

        return (loopbackIface, loopbackAddr);
    }

    internal static bool IsLoopbackAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (IPAddress.IsLoopback(address)) return true;
        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out int written);
        if (written != 16) return false;

        // Java treats the whole mapped 127/8 range as IPv4 loopback.
        if (address.IsIPv4MappedToIPv6) return bytes[12] == 127;
        return bytes[15] == 1 && bytes[..15].IndexOfAnyExcept((byte)0) < 0;
    }

    private static bool IsAddressAssigned(IPAddress address)
    {
        return IsAddressAssigned(address, NetworkInterface.GetAllNetworkInterfaces());
    }

    internal static bool IsAddressAssigned(IPAddress address, IReadOnlyList<NetworkInterface> interfaces)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(interfaces);
        Span<byte> requestedBytes = stackalloc byte[16];
        address.TryWriteBytes(requestedBytes, out int requestedLength);
        ReadOnlySpan<byte> requested = address.IsIPv4MappedToIPv6 ? requestedBytes[12..] : requestedBytes[..requestedLength];
        Span<byte> candidateBytes = stackalloc byte[16];
        // Java factories normalize mapped IPv4; address equality ignores IPv6 scope.
        foreach (NetworkInterface iface in interfaces)
        {
            foreach (UnicastIPAddressInformation candidate in iface.GetIPProperties().UnicastAddresses)
            {
                IPAddress observed = candidate.Address;
                observed.TryWriteBytes(candidateBytes, out int candidateLength);
                ReadOnlySpan<byte> bytes = observed.IsIPv4MappedToIPv6 ? candidateBytes[12..] : candidateBytes[..candidateLength];
                if (bytes.SequenceEqual(requested)) return true;
            }
        }

        return false;
    }
}
