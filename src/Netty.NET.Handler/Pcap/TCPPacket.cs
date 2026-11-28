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

// CLR: The TCPFlag... varargs parameter is params TCPFlag[]. The (int) casts of the segment and acknowledgment
// numbers keep the low 32 bits (unchecked, as Java's narrowing conversion), which is the unsigned 32-bit number
// written on the wire.
internal sealed class TCPPacket
{

    /**
     * Data Offset + Reserved Bits.
     */
    private const short OFFSET = 0x5000;

    private TCPPacket()
    {
        // Prevent outside initialization
    }

    /**
     * Write TCP Packet
     *
     * @param byteBuf ByteBuf where Packet data will be set
     * @param payload Payload of this Packet
     * @param srcPort Source Port
     * @param dstPort Destination Port
     */
    internal static void WritePacket(ByteBuf byteBuf, ByteBuf payload, long segmentNumber, long ackNumber, int srcPort,
                                     int dstPort, params TCPFlag[] tcpFlags)
    {

        byteBuf.WriteShort(srcPort);     // Source Port
        byteBuf.WriteShort(dstPort);     // Destination Port
        byteBuf.WriteInt(unchecked((int) segmentNumber)); // Segment Number
        byteBuf.WriteInt(unchecked((int) ackNumber));     // Acknowledgment Number
        byteBuf.WriteShort(OFFSET | TCPFlag.GetFlag(tcpFlags)); // Flags
        byteBuf.WriteShort(65535);       // Window Size
        byteBuf.WriteShort(0x0001);      // Checksum
        byteBuf.WriteShort(0);           // Urgent Pointer

        if (payload != null)
        {
            byteBuf.WriteBytes(payload); //  Payload of Data
        }
    }

    // CLR: a C# enum cannot declare a constructor, field or method, so the Java enum with a value field and getFlag is
    // a sealed class with static readonly instances in declaration order, the mapping of codec-base Base64Dialect
    // (src/Netty.NET.Handler.Codec/Base64Codec/Base64Dialect.cs); ToString() returns the constant name as
    // Enum.toString() does.
    internal sealed class TCPFlag
    {
        internal static readonly TCPFlag FIN = new TCPFlag(nameof(FIN), 1);
        internal static readonly TCPFlag SYN = new TCPFlag(nameof(SYN), 1 << 1);
        internal static readonly TCPFlag RST = new TCPFlag(nameof(RST), 1 << 2);
        internal static readonly TCPFlag PSH = new TCPFlag(nameof(PSH), 1 << 3);
        internal static readonly TCPFlag ACK = new TCPFlag(nameof(ACK), 1 << 4);
        internal static readonly TCPFlag URG = new TCPFlag(nameof(URG), 1 << 5);
        internal static readonly TCPFlag ECE = new TCPFlag(nameof(ECE), 1 << 6);
        internal static readonly TCPFlag CWR = new TCPFlag(nameof(CWR), 1 << 7);

        private readonly string name;
        private readonly int value;

        private TCPFlag(string name, int value)
        {
            this.name = name;
            this.value = value;
        }

        internal static int GetFlag(params TCPFlag[] tcpFlags)
        {
            int flags = 0;

            foreach (TCPFlag tcpFlag in tcpFlags)
            {
                flags |= tcpFlag.value;
            }

            return  flags;
        }

        public override string ToString()
        {
            return name;
        }
    }
}
