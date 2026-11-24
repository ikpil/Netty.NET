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

// CLR: the package-private final class is an internal sealed class with the private constructor. The Java byte
// constants are C# byte constants: MAX_TTL ((byte) 255, -1 in Java) is 255 and both write the octet 0xFF through
// writeByte(int); TCP and UDP are positive, so their values are unchanged.
internal sealed class IPPacket
{

    private const byte MAX_TTL = (byte) 255;
    private const short V4_HEADER_SIZE = 20;
    private const byte TCP = 6 & 0xff;
    private const byte UDP = 17 & 0xff;

    /**
     * Version + Traffic class + Flow label
     */
    private const int IPV6_VERSION_TRAFFIC_FLOW = 60000000;

    private IPPacket()
    {
        // Prevent outside initialization
    }

    /**
     * Write IPv4 Packet for UDP Packet
     *
     * @param byteBuf    ByteBuf where IP Packet data will be set
     * @param payload    Payload of UDP
     * @param srcAddress Source IPv4 Address
     * @param dstAddress Destination IPv4 Address
     */
    internal static void WriteUDPv4(ByteBuf byteBuf, ByteBuf payload, int srcAddress, int dstAddress)
    {
        WritePacketv4(byteBuf, payload, UDP, srcAddress, dstAddress);
    }

    /**
     * Write IPv6 Packet for UDP Packet
     *
     * @param byteBuf    ByteBuf where IP Packet data will be set
     * @param payload    Payload of UDP
     * @param srcAddress Source IPv6 Address
     * @param dstAddress Destination IPv6 Address
     */
    internal static void WriteUDPv6(ByteBuf byteBuf, ByteBuf payload, byte[] srcAddress, byte[] dstAddress)
    {
        WritePacketv6(byteBuf, payload, UDP, srcAddress, dstAddress);
    }

    /**
     * Write IPv4 Packet for TCP Packet
     *
     * @param byteBuf    ByteBuf where IP Packet data will be set
     * @param payload    Payload of TCP
     * @param srcAddress Source IPv4 Address
     * @param dstAddress Destination IPv4 Address
     */
    internal static void WriteTCPv4(ByteBuf byteBuf, ByteBuf payload, int srcAddress, int dstAddress)
    {
        WritePacketv4(byteBuf, payload, TCP, srcAddress, dstAddress);
    }

    /**
     * Write IPv6 Packet for TCP Packet
     *
     * @param byteBuf    ByteBuf where IP Packet data will be set
     * @param payload    Payload of TCP
     * @param srcAddress Source IPv6 Address
     * @param dstAddress Destination IPv6 Address
     */
    internal static void WriteTCPv6(ByteBuf byteBuf, ByteBuf payload, byte[] srcAddress, byte[] dstAddress)
    {
        WritePacketv6(byteBuf, payload, TCP, srcAddress, dstAddress);
    }

    private static void WritePacketv4(ByteBuf byteBuf, ByteBuf payload, int protocol, int srcAddress,
                                      int dstAddress)
    {

        byteBuf.WriteByte(0x45);      //  Version + IHL
        byteBuf.WriteByte(0x00);      //  DSCP
        byteBuf.WriteShort(V4_HEADER_SIZE + payload.ReadableBytes); // Length
        byteBuf.WriteShort(0x0000);   // Identification
        byteBuf.WriteShort(0x0000);   // Fragment
        byteBuf.WriteByte(MAX_TTL);   // TTL
        byteBuf.WriteByte(protocol);  // Protocol
        byteBuf.WriteShort(0);        // Checksum
        byteBuf.WriteInt(srcAddress); // Source IPv4 Address
        byteBuf.WriteInt(dstAddress); // Destination IPv4 Address
        byteBuf.WriteBytes(payload);  // Payload of L4
    }

    private static void WritePacketv6(ByteBuf byteBuf, ByteBuf payload, int protocol, byte[] srcAddress,
                                      byte[] dstAddress)
    {

        byteBuf.WriteInt(IPV6_VERSION_TRAFFIC_FLOW); // Version  + Traffic class + Flow label
        byteBuf.WriteShort(payload.ReadableBytes); // Payload length
        byteBuf.WriteByte(protocol & 0xff); // Next header
        byteBuf.WriteByte(MAX_TTL);         // Hop limit
        byteBuf.WriteBytes(srcAddress);     // Source IPv6 Address
        byteBuf.WriteBytes(dstAddress);     // Destination IPv6 Address
        byteBuf.WriteBytes(payload);        // Payload of L4
    }
}
