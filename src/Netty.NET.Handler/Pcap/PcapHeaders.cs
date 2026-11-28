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

// CLR: the package-private final class is an internal sealed class with the private constructor. java.io.OutputStream
// is System.IO.Stream; outputStream.write(byte[]) writes the whole array with Write(array, 0, length), as
// OutputStream.write(byte[]) calls write(b, 0, b.length)
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/OutputStream.html). "throws IOException" has no
// C# counterpart.
internal sealed class PcapHeaders
{

    /**
     * Pcap Global Header built from:
     * <ol>
     *      <li> magic_number </li>
     *      <li> version_major </li>
     *      <li> version_minor </li>
     *      <li> thiszone </li>
     *      <li> sigfigs </li>
     *      <li> snaplen </li>
     *      <li> network </li>
     * </ol>
     */
    // CLR: the Java byte literals are signed; a C# byte is unsigned, so the negative values are converted with
    // unchecked casts, which keep the same bits (0xA1 0xB2 0xC3 0xD4... 0xFF 0xFF...) that Java writes.
    private static readonly byte[] GLOBAL_HEADER = unchecked(new byte[] {(byte) -95, (byte) -78, (byte) -61, (byte) -44,
            0, 2, 0, 4, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, (byte) -1, (byte) -1, 0, 0, 0, 1});

    private PcapHeaders()
    {
        // Prevent outside initialization
    }

    /**
     * Writes the Pcap Global Header to the provided {@code OutputStream}
     *
     * @param outputStream OutputStream where Pcap data will be written.
     * @throws IOException if there is an error writing to the {@code OutputStream}
     */
    internal static void WriteGlobalHeader(System.IO.Stream outputStream)
    {
        outputStream.Write(GLOBAL_HEADER, 0, GLOBAL_HEADER.Length);
    }

    /**
     * Write Pcap Packet Header
     *
     * @param byteBuf  ByteBuf where we'll write header data
     * @param ts_sec   timestamp seconds
     * @param ts_usec  timestamp microseconds
     * @param incl_len number of octets of packet saved in file
     * @param orig_len actual length of packet
     */
    internal static void WritePacketHeader(ByteBuf byteBuf, int ts_sec, int ts_usec, int incl_len, int orig_len)
    {
        byteBuf.WriteInt(ts_sec);
        byteBuf.WriteInt(ts_usec);
        byteBuf.WriteInt(incl_len);
        byteBuf.WriteInt(orig_len);
    }
}
