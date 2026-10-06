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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Net;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Internal;

public static class MacAddressUtil
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(MacAddressUtil));

    private static readonly int EUI64_MAC_ADDRESS_LENGTH = 8;
    private static readonly int EUI48_MAC_ADDRESS_LENGTH = 6;

    /**
     * Obtains the best MAC address found on local network interfaces.
     * Generally speaking, an active network interface used on public
     * networks is better than a local network interface.
     *
     * @return byte array containing a MAC. null if no MAC can be found.
     */
    public static byte[] BestAvailableMac()
    {
        // Find the best MAC address available.
        byte[] bestMacAddr = EmptyArrays.EMPTY_BYTES;
        IPAddress bestInetAddr = NetUtil.LOCALHOST4;

        // Retrieve the list of available network interfaces.
        List<KeyValuePair<NetworkInterface, IPAddress>> ifaces = new List<KeyValuePair<NetworkInterface, IPAddress>>();
        foreach (NetworkInterface iface in NetUtil.NETWORK_INTERFACES)
        {
            // Use the interface with proper INET addresses only.
            UnicastIPAddressInformationCollection addrs = iface.GetIPProperties().UnicastAddresses;
            if (0 < addrs.Count)
            {
                IPAddress a = addrs[0].Address;
                if (!IPAddress.IsLoopback(a))
                {
                    ifaces.Add(KeyValuePair.Create(iface, a));
                }
            }
        }

        foreach (var entry in ifaces)
        {
            NetworkInterface iface = entry.Key;
            IPAddress inetAddr = entry.Value;
            
            // Cannot reliably detect virtual interfaces in .NET; no built-in API exists.
            // if (iface.isVirtual()) {
            //     continue;
            // }
            
            byte[] macAddr;
            try
            {
                macAddr = iface.GetPhysicalAddress().GetAddressBytes();
            }
            catch (NetworkInformationException e)
            {
                logger.Debug("Failed to get the hardware address of a network interface: {}", iface, e);
                continue;
            }

            bool replace = false;
            int res = CompareAddresses(bestMacAddr, macAddr);
            if (res < 0)
            {
                // Found a better MAC address.
                replace = true;
            }
            else if (res == 0)
            {
                // Two MAC addresses are of pretty much same quality.
                res = CompareAddresses(bestInetAddr, inetAddr);
                if (res < 0)
                {
                    // Found a MAC address with better INET address.
                    replace = true;
                }
                else if (res == 0)
                {
                    // Cannot tell the difference.  Choose the longer one.
                    if (bestMacAddr.Length < macAddr.Length)
                    {
                        replace = true;
                    }
                }
            }

            if (replace)
            {
                bestMacAddr = macAddr;
                bestInetAddr = inetAddr;
            }
        }

        if (bestMacAddr == EmptyArrays.EMPTY_BYTES)
        {
            return null;
        }

        if (bestMacAddr.Length == EUI48_MAC_ADDRESS_LENGTH)
        {
            // EUI-48 - convert to EUI-64
            byte[] newAddr = new byte[EUI64_MAC_ADDRESS_LENGTH];
            Arrays.Arraycopy(bestMacAddr, 0, newAddr, 0, 3);
            newAddr[3] = (byte)0xFF;
            newAddr[4] = (byte)0xFE;
            Arrays.Arraycopy(bestMacAddr, 3, newAddr, 5, 3);
            bestMacAddr = newAddr;
        }
        else
        {
            // Unknown
            bestMacAddr = Arrays.CopyOf(bestMacAddr, EUI64_MAC_ADDRESS_LENGTH);
        }

        return bestMacAddr;
    }

    /**
     * Returns the result of {@link #bestAvailableMac()} if non-{@code null} otherwise returns a random EUI-64 MAC
     * address.
     */
    public static byte[] DefaultMachineId()
    {
        byte[] bestMacAddr = BestAvailableMac();
        if (bestMacAddr == null)
        {
            bestMacAddr = new byte[EUI64_MAC_ADDRESS_LENGTH];
            Random.Shared.NextBytes(bestMacAddr);
            logger.Warn(
                "Failed to find a usable hardware address from the network interfaces; using random bytes: {}",
                FormatAddress(bestMacAddr));
        }

        return bestMacAddr;
    }

    /**
     * Parse a EUI-48, MAC-48, or EUI-64 MAC address from a {@link String} and return it as a {@code byte[]}.
     * @param value The string representation of the MAC address.
     * @return The byte representation of the MAC address.
     */
    public static byte[] ParseMAC(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] machineId;
        char separator;
        switch (value.Length)
        {
            case 17:
                separator = value[2];
                ValidateMacSeparator(separator);
                machineId = new byte[EUI48_MAC_ADDRESS_LENGTH];
                break;
            case 23:
                separator = value[2];
                ValidateMacSeparator(separator);
                machineId = new byte[EUI64_MAC_ADDRESS_LENGTH];
                break;
            default:
                throw new ArgumentException("value is not supported [MAC-48, EUI-48, EUI-64]");
        }

        int end = machineId.Length - 1;
        int j = 0;
        for (int i = 0; i < end; ++i, j += 3)
        {
            int sIndex = j + 2;
            machineId[i] = StringUtil.DecodeHexByte(value, j);
            if (value[sIndex] != separator)
            {
                throw new ArgumentException("expected separator '" + separator + " but got '" +
                                            value[sIndex] + "' at index: " + sIndex);
            }
        }

        machineId[end] = StringUtil.DecodeHexByte(value, j);

        return machineId;
    }

    private static void ValidateMacSeparator(char separator)
    {
        if (separator != ':' && separator != '-')
        {
            throw new ArgumentException("unsupported separator: " + separator + " (expected: [:-])");
        }
    }

    /**
     * @param addr byte array of a MAC address.
     * @return hex formatted MAC address.
     */
    public static string FormatAddress(byte[] addr)
    {
        ArgumentNullException.ThrowIfNull(addr);
        if (addr.Length == 0) return string.Empty;

        return string.Create(checked(addr.Length * 3 - 1), addr, static (chars, bytes) =>
        {
            const string digits = "0123456789abcdef";
            int position = 0;
            foreach (byte value in bytes)
            {
                if (position != 0) chars[position++] = ':';
                chars[position++] = digits[value >> 4];
                chars[position++] = digits[value & 15];
            }
        });
    }

    /**
     * @return positive - current is better, 0 - cannot tell from MAC addr, negative - candidate is better.
     */
    // visible for testing
    public static int CompareAddresses(byte[] current, byte[] candidate)
    {
        if (candidate == null || candidate.Length < EUI48_MAC_ADDRESS_LENGTH)
        {
            return 1;
        }

        // Must not be filled with only 0 and 1.
        bool onlyZeroAndOne = true;
        foreach (byte b in candidate)
        {
            if (b != 0 && b != 1)
            {
                onlyZeroAndOne = false;
                break;
            }
        }

        if (onlyZeroAndOne)
        {
            return 1;
        }

        // Must not be a multicast address
        if ((candidate[0] & 1) != 0)
        {
            return 1;
        }

        // Prefer globally unique address.
        if ((candidate[0] & 2) == 0)
        {
            if (current.Length != 0 && (current[0] & 2) == 0)
            {
                // Both current and candidate are globally unique addresses.
                return 0;
            }
            else
            {
                // Only candidate is globally unique.
                return -1;
            }
        }
        else
        {
            if (current.Length != 0 && (current[0] & 2) == 0)
            {
                // Only current is globally unique.
                return 1;
            }
            else
            {
                // Both current and candidate are non-unique.
                return 0;
            }
        }
    }

    /**
     * @return positive - current is better, 0 - cannot tell, negative - candidate is better
     */
    private static int CompareAddresses(IPAddress current, IPAddress candidate)
    {
        return ScoreAddress(current) - ScoreAddress(candidate);
    }

    private static int ScoreAddress(IPAddress addr)
    {
        if (addr.IsAny() || IPAddress.IsLoopback(addr))
        {
            return 0;
        }

        if (addr.IsMulticast())
        {
            return 1;
        }

        if (addr.IsLinkLocal())
        {
            return 2;
        }

        if (addr.IsSiteLocal())
        {
            return 3;
        }

        return 4;
    }

    private static bool IsAny(this IPAddress addr)
    {
        return addr.Equals(IPAddress.Any) || addr.Equals(IPAddress.IPv6Any);
    }

    private static bool IsMulticast(this IPAddress addr)
    {
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            // IPv4 Multicast: 224.0.0.0 ~ 239.255.255.255
            byte[] bytes = addr.GetAddressBytes();
            return bytes[0] >= 224 && bytes[0] <= 239;
        }
        else if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return addr.IsIPv6Multicast;
        }

        return false;
    }

    private static bool IsLinkLocal(this IPAddress addr)
    {
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            // IPv4 Link-local: 169.254.0.0/16
            byte[] bytes = addr.GetAddressBytes();
            return bytes[0] == 169 && bytes[1] == 254;
        }
        else if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return addr.IsIPv6LinkLocal;
        }

        return false;
    }

    private static bool IsSiteLocal(this IPAddress addr)
    {
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = addr.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && (bytes[1] >= 16 && bytes[1] <= 31))
                return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
        }
        else if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return addr.IsIPv6SiteLocal; // obsolete, but available
        }

        return false;
    }
}
