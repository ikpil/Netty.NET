/*
 * Copyright 2020 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Pcap;

internal sealed class EthernetPacket
{

    /**
     * MAC Address: 00:00:5E:00:53:00
     */
    private static readonly byte[] DUMMY_SOURCE_MAC_ADDRESS = new byte[]{0, 0, 94, 0, 83, 0};

    /**
     * MAC Address: 00:00:5E:00:53:FF
     */
    // CLR: the signed Java byte -1 is the unsigned C# byte 0xFF (an unchecked cast keeps the bits Java writes).
    private static readonly byte[] DUMMY_DESTINATION_MAC_ADDRESS = unchecked(new byte[]{0, 0, 94, 0, 83, (byte) -1});

    /**
     * IPv4
     */
    private const int V4 = 0x0800;

    /**
     * IPv6
     */
    private const int V6 = 0x86dd;

    private EthernetPacket()
    {
        // Prevent outside initialization
    }

    /**
     * Write IPv4 Ethernet Packet. It uses a dummy MAC address for both source and destination.
     *
     * @param byteBuf ByteBuf where Ethernet Packet data will be set
     * @param payload Payload of IPv4
     */
    internal static void WriteIPv4(ByteBuf byteBuf, ByteBuf payload)
    {
        WritePacket(byteBuf, payload, DUMMY_SOURCE_MAC_ADDRESS, DUMMY_DESTINATION_MAC_ADDRESS, V4);
    }

    /**
     * Write IPv6 Ethernet Packet. It uses a dummy MAC address for both source and destination.
     *
     * @param byteBuf ByteBuf where Ethernet Packet data will be set
     * @param payload Payload of IPv6
     */
    internal static void WriteIPv6(ByteBuf byteBuf, ByteBuf payload)
    {
        WritePacket(byteBuf, payload, DUMMY_SOURCE_MAC_ADDRESS, DUMMY_DESTINATION_MAC_ADDRESS, V6);
    }

    /**
     * Write IPv6 Ethernet Packet
     *
     * @param byteBuf    ByteBuf where Ethernet Packet data will be set
     * @param payload    Payload of IPv6
     * @param srcAddress Source MAC Address
     * @param dstAddress Destination MAC Address
     * @param type       Type of Frame
     */
    private static void WritePacket(ByteBuf byteBuf, ByteBuf payload, byte[] srcAddress, byte[] dstAddress, int type)
    {
        byteBuf.WriteBytes(dstAddress); // Destination MAC Address
        byteBuf.WriteBytes(srcAddress); // Source MAC Address
        byteBuf.WriteShort(type);       // Frame Type (IPv4 or IPv6)
        byteBuf.WriteBytes(payload);    // Payload of L3
    }
}
