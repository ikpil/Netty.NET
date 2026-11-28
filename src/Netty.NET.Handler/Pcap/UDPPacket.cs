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

internal sealed class UDPPacket
{

    private const short UDP_HEADER_SIZE = 8;

    private UDPPacket()
    {
        // Prevent outside initialization
    }

    /**
     * Write UDP Packet
     *
     * @param byteBuf ByteBuf where Packet data will be set
     * @param payload Payload of this Packet
     * @param srcPort Source Port
     * @param dstPort Destination Port
     */
    internal static void WritePacket(ByteBuf byteBuf, ByteBuf payload, int srcPort, int dstPort)
    {
        byteBuf.WriteShort(srcPort); // Source Port
        byteBuf.WriteShort(dstPort); // Destination Port
        byteBuf.WriteShort(UDP_HEADER_SIZE + payload.ReadableBytes); // UDP Header Length + Payload Length
        byteBuf.WriteShort(0x0001);  // Checksum
        byteBuf.WriteBytes(payload); //  Payload of Data
    }
}
