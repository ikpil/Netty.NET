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
using System.Globalization;
using System.Net;
using Netty.NET.Buffer;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Parses the QUIC packet header and notifies a callback once parsing was successful.
 * <p>
 * Once the parser is not needed anymore the user needs to call {@link #close()} to ensure all resources are
 * released. Failed to do so may lead to memory leaks.
 * <p>
 * This class can be used for advanced use-cases. Usually you want to just use {@link QuicClientCodecBuilder} or
 * {@link QuicServerCodecBuilder}.
 */
// CLR: AutoCloseable is IDisposable with close() kept as Close() and Dispose() calling it (docs/handler-porting.md:
// "Closeable keeps close() as Close() and implements IDisposable.Dispose() with it"). InetSocketAddress is IPEndPoint;
// the nested interface QuicHeaderProcessor is IQuicHeaderProcessor; the throws clauses have no counterpart.
// getSimpleName() is nameof. Byte.BYTES and Integer.BYTES are sizeof(byte) and sizeof(int). The Buffer port's
// GetByte is unsigned: hasShortHeader, typeOfLongHeader and numBytesForVariableLengthInteger test bits 0x80, 0x30 and
// the two bits of b >> 6, which the signed Java byte and the C# byte give alike, and getUnsignedByte is GetByte.
// getUnsignedShort/getUnsignedInt are GetUnsignedShort/GetUnsignedInt (ushort/uint). The switch over the
// QuicPacketType constants uses case guards (a sealed class, see QuicPacketType), as codec-haproxy HAProxyTLV does.
// IllegalStateException is InvalidOperationException; numbers in messages print with the invariant culture.
public sealed class QuicHeaderParser : IDisposable
{
    // See https://datatracker.ietf.org/doc/rfc7714/
    private const int AES_128_GCM_TAG_LENGTH = 16;

    private readonly int localConnectionIdLength;
    private bool closed;

    public QuicHeaderParser(int localConnectionIdLength)
    {
        this.localConnectionIdLength = CheckPositiveOrZero(localConnectionIdLength, "localConnectionIdLength");
    }

    public void Close()
    {
        if (!closed)
        {
            closed = true;
        }
    }

    void IDisposable.Dispose()
    {
        Close();
    }

    /**
     * Parses a QUIC packet and extract the header values out of it. This method takes no ownership of the packet itself
     * which means the caller of this method is expected to call {@link ByteBuf#release()} once the packet is not needed
     * anymore.
     *
     * @param sender        the sender of the packet. This is directly passed to the {@link QuicHeaderProcessor} once
     *                      parsing was successful.
     * @param recipient     the recipient of the packet.This is directly passed to the {@link QuicHeaderProcessor} once
     *                      parsing was successful.
     * @param packet        raw QUIC packet itself. The ownership of the packet is not transferred. This is directly
     *                      passed to the {@link QuicHeaderProcessor} once parsing was successful.
     * @param callback      the {@link QuicHeaderProcessor} that is called once a QUIC packet could be parsed and all
     *                      the header values be extracted.
     * @throws Exception    thrown if we couldn't parse the header or if the {@link QuicHeaderProcessor} throws an
     *                      exception.
     */
    public void Parse(IPEndPoint sender, IPEndPoint recipient, ByteBuf packet,
                      IQuicHeaderProcessor callback)
    {
        if (closed)
        {
            throw new InvalidOperationException(nameof(QuicHeaderParser) + " is already closed");
        }

        // See https://datatracker.ietf.org/doc/html/rfc9000#section-17
        int offset = 0;
        int readable = packet.ReadableBytes;

        CheckReadable(offset, readable, sizeof(byte));
        byte first = packet.GetByte(offset);
        offset += sizeof(byte);

        QuicPacketType type;
        long version;
        ByteBuf dcid;
        ByteBuf scid;
        ByteBuf token;

        if (HasShortHeader(first))
        {
            // See https://www.rfc-editor.org/rfc/rfc9000.html#section-17.3
            // 1-RTT Packet {
            //  Header Form (1) = 0,
            //  Fixed Bit (1) = 1,
            //  Spin Bit (1),
            //  Reserved Bits (2),
            //  Key Phase (1),
            //  Packet Number Length (2),
            //  Destination Connection ID (0..160),
            //  Packet Number (8..32),
            //  Packet Payload (8..),
            //}
            version = 0;
            type = QuicPacketType.SHORT;
            // Short packets have no source connection id and no token.
            scid = Unpooled.EmptyBuffer;
            token = Unpooled.EmptyBuffer;
            dcid = SliceCid(packet, offset, localConnectionIdLength);
        }
        else
        {
            // See https://www.rfc-editor.org/rfc/rfc9000.html#section-17.2
            // Long Header Packet {
            //  Header Form (1) = 1,
            //  Fixed Bit (1) = 1,
            //  Long Packet Type (2),
            //  Type-Specific Bits (4),
            //  Version (32),
            //  Destination Connection ID Length (8),
            //  Destination Connection ID (0..160),
            //  Source Connection ID Length (8),
            //  Source Connection ID (0..160),
            //  Type-Specific Payload (..),
            //}
            CheckReadable(offset, readable, sizeof(int));

            // Version uses an unsigned int:
            // https://www.rfc-editor.org/rfc/rfc9000.html#section-15
            version = packet.GetUnsignedInt(offset);
            offset += sizeof(int);
            type = TypeOfLongHeader(first, version);

            CheckReadable(offset, readable, sizeof(byte));
            int dcidLen = packet.GetByte(offset);
            CheckCidLength(dcidLen);
            offset += sizeof(byte);
            dcid = SliceCid(packet, offset, dcidLen);
            offset += dcidLen;

            CheckReadable(offset, readable, sizeof(byte));
            int scidLen = packet.GetByte(offset);
            CheckCidLength(scidLen);
            offset += sizeof(byte);
            scid = SliceCid(packet, offset, scidLen);
            offset += scidLen;
            token = SliceToken(type, packet, offset, readable);
        }
        callback.Process(sender, recipient, packet, type, version, scid, dcid, token);
    }

    // Check if the connection id is not longer then 20. This is what is the maximum for QUIC version 1.
    // See https://www.rfc-editor.org/rfc/rfc9000.html#section-17.2
    private static void CheckCidLength(int length)
    {
        if (length > Quic.MAX_CONN_ID_LEN)
        {
            throw new QuicException("connection id to large: "  + length.ToString(CultureInfo.InvariantCulture) +
                    " > " + Quic.MAX_CONN_ID_LEN.ToString(CultureInfo.InvariantCulture),
                    QuicTransportError.PROTOCOL_VIOLATION);
        }
    }

    private static ByteBuf SliceToken(QuicPacketType type, ByteBuf packet, int offset, int readable)
    {
        switch (type)
        {
            case var _ when type == QuicPacketType.INITIAL:
                // See
                CheckReadable(offset, readable, sizeof(byte));
                int numBytes = NumBytesForVariableLengthInteger(packet.GetByte(offset));
                int len = (int) GetVariableLengthInteger(packet, offset, numBytes);
                offset += numBytes;

                CheckReadable(offset, readable, len);
                return packet.Slice(offset, len);
            case var _ when type == QuicPacketType.RETRY:
                // Exclude the integrity tag from the token.
                // See https://www.rfc-editor.org/rfc/rfc9000.html#section-17.2.5
                CheckReadable(offset, readable, AES_128_GCM_TAG_LENGTH);
                int tokenLen = readable - offset - AES_128_GCM_TAG_LENGTH;
                return packet.Slice(offset, tokenLen);
            default:
                // No token included.
                return Unpooled.EmptyBuffer;
        }
    }

    private static QuicException NewProtocolViolationException(string message)
    {
        return new QuicException(message, QuicTransportError.PROTOCOL_VIOLATION);
    }

    internal static ByteBuf SliceCid(ByteBuf buffer, int offset, int len)
    {
        CheckReadable(offset, buffer.ReadableBytes, len);
        return buffer.Slice(offset, len);
    }

    private static void CheckReadable(int offset, int readable, int needed)
    {
        int r = readable - offset;
        if (r < needed)
        {
            throw NewProtocolViolationException("Not enough bytes to read, " + r.ToString(CultureInfo.InvariantCulture) +
                    " < " + needed.ToString(CultureInfo.InvariantCulture));
        }
    }

    /**
     * Read the variable length integer from the {@link ByteBuf}.
     * See <a href="https://tools.ietf.org/html/draft-ietf-quic-transport-32#section-16">
     *     Variable-Length Integer Encoding </a>
     */
    private static long GetVariableLengthInteger(ByteBuf @in, int offset, int len)
    {
        CheckReadable(offset, @in.ReadableBytes, len);
        switch (len)
        {
            case 1:
                return @in.GetByte(offset);
            case 2:
                return @in.GetUnsignedShort(offset) & 0x3fff;
            case 4:
                return @in.GetUnsignedInt(offset) & 0x3fffffff;
            case 8:
                return @in.GetLong(offset) & 0x3fffffffffffffffL;
            default:
                throw NewProtocolViolationException("Unsupported length:" + len.ToString(CultureInfo.InvariantCulture));
        }
    }

    /**
     * Returns the number of bytes that were encoded into the byte for a variable length integer to read.
     * See <a href="https://tools.ietf.org/html/draft-ietf-quic-transport-32#section-16">
     *     Variable-Length Integer Encoding </a>
     */
    private static int NumBytesForVariableLengthInteger(byte b)
    {
        byte val = (byte) (b >> 6);
        if ((val & 1) != 0)
        {
            if ((val & 2) != 0)
            {
                return 8;
            }
            return 2;
        }
        if ((val & 2) != 0)
        {
            return 4;
        }
        return 1;
    }

    internal static bool HasShortHeader(byte b)
    {
        return (b & 0x80) == 0;
    }

    private static QuicPacketType TypeOfLongHeader(byte first, long version)
    {
        if (version == 0)
        {
            // If we parsed a version of 0 we are sure it's a version negotiation packet:
            // https://www.rfc-editor.org/rfc/rfc9000.html#section-17.2.1
            //
            // This also means we should ignore everything that is left in 'first'.
            return QuicPacketType.VERSION_NEGOTIATION;
        }
        int packetType = (first & 0x30) >> 4;
        switch (packetType)
        {
            case 0x00:
                return QuicPacketType.INITIAL;
            case 0x01:
                return QuicPacketType.ZERO_RTT;
            case 0x02:
                return QuicPacketType.HANDSHAKE;
            case 0x03:
                return QuicPacketType.RETRY;
            default:
                throw NewProtocolViolationException("Unknown packet type: " +
                        packetType.ToString(CultureInfo.InvariantCulture));
        }
    }

    /**
     * Called when a QUIC packet and its header could be parsed.
     */
    public interface IQuicHeaderProcessor
    {

        /**
         * Called when a QUIC packet header was parsed.
         *
         * @param sender        the sender of the QUIC packet.
         * @param recipient     the recipient of the QUIC packet.
         * @param packet        the raw QUIC packet. The ownership is not transferred, which means you will need to call
         *                      {@link ByteBuf#retain()} on it if you want to keep a reference after this method
         *                      returns.
         * @param type          the type of the packet.
         * @param version       the version of the packet.
         * @param scid          the source connection id. The ownership is not transferred and its generally not allowed
         *                      to hold any references to this buffer outside of the method as it will be re-used.
         * @param dcid          the destination connection id. The ownership is not transferred and its generally not
         *                      allowed to hold any references to this buffer outside of the method as it will be
         *                      re-used.
         * @param token         the token.The ownership is not transferred and its generally not allowed
         *                      to hold any references to this buffer outside of the method as it will be re-used.
         * @throws Exception    throws if an error happens during processing.
         */
        void Process(IPEndPoint sender, IPEndPoint recipient, ByteBuf packet,
                     QuicPacketType type, long version, ByteBuf scid, ByteBuf dcid, ByteBuf token);
    }
}
