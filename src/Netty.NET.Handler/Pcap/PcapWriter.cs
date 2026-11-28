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

using System;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Handler.Pcap;

// CLR: the package-private final class is an internal sealed class. java.io.OutputStream is System.IO.Stream and
// packet.readBytes(outputStream, length) is ByteBuf.ReadBytes(Stream, int). synchronized (outputStream) is lock
// (outputStream), the statement the Transport port uses for synchronized blocks (docs/transport-porting.md).
// System.currentTimeMillis() is DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "the number of milliseconds that have
// elapsed since 1970-01-01T00:00:00.000Z"
// (https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.tounixtimemilliseconds?view=net-10.0). Closeable
// keeps close() as Close() and IDisposable.Dispose() calls it, as codec-compression's IDecompressor does for
// AutoCloseable (docs/codec-compression-porting.md); toString() appends the stream's ToString(). "throws IOException"
// has no C# counterpart.
internal sealed class PcapWriter : IDisposable
{

    /**
     * Logger
     */
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(PcapWriter));

    private readonly PcapWriteHandler pcapWriteHandler;

    /**
     * Reference declared so that we can use this as mutex in clean way.
     */
    private readonly System.IO.Stream outputStream;

    /**
     * This uses {@link OutputStream} for writing Pcap data.
     *
     * @throws IOException If {@link OutputStream#write(byte[])} throws an exception
     */
    internal PcapWriter(PcapWriteHandler pcapWriteHandler)
    {
        this.pcapWriteHandler = pcapWriteHandler;
        outputStream = pcapWriteHandler.OutputStream();

        // If OutputStream is not shared then we have to write Global Header.
        if (pcapWriteHandler.WritePcapGlobalHeader() && !pcapWriteHandler.SharedOutputStream())
        {
            PcapHeaders.WriteGlobalHeader(pcapWriteHandler.OutputStream());
        }
    }

    /**
     * Write Packet in Pcap OutputStream.
     *
     * @param packetHeaderBuf Packer Header {@link ByteBuf}
     * @param packet          Packet
     * @throws IOException If {@link OutputStream#write(byte[])} throws an exception
     */
    internal void WritePacket(ByteBuf packetHeaderBuf, ByteBuf packet)
    {
        if (pcapWriteHandler.State() == State.CLOSED)
        {
            logger.Debug("Pcap Write attempted on closed PcapWriter");
        }

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        PcapHeaders.WritePacketHeader(
                packetHeaderBuf,
                (int) (timestamp / 1000L),
                (int) (timestamp % 1000L * 1000L),
                packet.ReadableBytes,
                packet.ReadableBytes
        );

        if (pcapWriteHandler.SharedOutputStream())
        {
            lock (outputStream)
            {
                packetHeaderBuf.ReadBytes(outputStream, packetHeaderBuf.ReadableBytes);
                packet.ReadBytes(outputStream, packet.ReadableBytes);
            }
        }
        else
        {
            packetHeaderBuf.ReadBytes(outputStream, packetHeaderBuf.ReadableBytes);
            packet.ReadBytes(outputStream, packet.ReadableBytes);
        }
    }

    public override string ToString()
    {
        return "PcapWriter{" +
                "outputStream=" + outputStream +
                '}';
    }

    public void Close()
    {
        if (pcapWriteHandler.State() == State.CLOSED)
        {
            logger.Debug("PcapWriter is already closed");
        }
        else
        {
            if (pcapWriteHandler.SharedOutputStream())
            {
                lock (outputStream)
                {
                    outputStream.Flush();
                }
            }
            else
            {
                outputStream.Flush();
                outputStream.Close();
            }

            pcapWriteHandler.MarkClosed();
            logger.Debug("PcapWriter is now closed");
        }
    }

    void IDisposable.Dispose()
    {
        Close();
    }
}
