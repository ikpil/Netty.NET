/*
 * Copyright 2014 The Netty Project
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

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.HAProxy.HAProxyConstants;

namespace Netty.NET.Handler.Codec.HAProxy;

/**
 * Decodes an HAProxy proxy protocol header
 *
 * @see <a href="https://haproxy.1wt.eu/download/1.5/doc/proxy-protocol.txt">Proxy Protocol Specification</a>
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in (docs/codec-base-porting.md); readerIndex()/
// writerIndex()/readableBytes() are the ReaderIndex/WriterIndex/ReadableBytes properties, getUnsignedByte() is GetByte()
// (unsigned byte, owner decision) and getUnsignedShort() returns ushort (docs/buffer-porting.md). The returned close
// Task of ctx.CloseAsync() is discarded as the source discards the ChannelFuture. CharsetUtil.US_ASCII is
// Encoding.ASCII, and numbers in messages use the invariant culture.
public class HAProxyMessageDecoder : ByteToMessageDecoder
{
    /**
     * Maximum possible length of a v1 proxy protocol header per spec
     */
    private const int V1_MAX_LENGTH = 108;

    /**
     * Maximum possible length of a v2 proxy protocol header (fixed 16 bytes + max unsigned short)
     */
    private const int V2_MAX_LENGTH = 16 + 65535;

    /**
     * Minimum possible length of a fully functioning v2 proxy protocol header (fixed 16 bytes + v2 address info space)
     */
    private const int V2_MIN_LENGTH = 16 + 216;

    /**
     * Maximum possible length for v2 additional TLV data (max unsigned short - max v2 address info space)
     */
    private const int V2_MAX_TLV = 65535 - 216;

    /**
     * Binary header prefix length
     */
    private static readonly int BINARY_PREFIX_LENGTH = BINARY_PREFIX.Length;

    /**
     * {@link ProtocolDetectionResult} for {@link HAProxyProtocolVersion#V1}.
     */
    private static readonly ProtocolDetectionResult<HAProxyProtocolVersion> DETECTION_RESULT_V1 =
            ProtocolDetectionResult<HAProxyProtocolVersion>.Detected(HAProxyProtocolVersion.V1);

    /**
     * {@link ProtocolDetectionResult} for {@link HAProxyProtocolVersion#V2}.
     */
    private static readonly ProtocolDetectionResult<HAProxyProtocolVersion> DETECTION_RESULT_V2 =
            ProtocolDetectionResult<HAProxyProtocolVersion>.Detected(HAProxyProtocolVersion.V2);

    /**
     * Used to extract a header frame out of the {@link ByteBuf} and return it.
     */
    private HeaderExtractor headerExtractor;

    /**
     * {@code true} if we're discarding input because we're already over maxLength
     */
    private bool discarding;

    /**
     * Number of discarded bytes
     */
    private int discardedBytes;

    /**
     * Whether or not to throw an exception as soon as we exceed maxLength.
     */
    private readonly bool failFast;

    /**
     * {@code true} if we're finished decoding the proxy protocol header
     */
    private bool finished;

    /**
     * Protocol specification version
     */
    private int version = -1;

    /**
     * The latest v2 spec (2014/05/18) allows for additional data to be sent in the proxy protocol header beyond the
     * address information block so now we need a configurable max header size
     */
    private readonly int v2MaxHeaderSize;

    /**
     * Creates a new decoder with no additional data (TLV) restrictions, and should throw an exception as soon as
     * we exceed maxLength.
     */
    public HAProxyMessageDecoder() : this(true)
    {
    }

    /**
     * Creates a new decoder with no additional data (TLV) restrictions, whether or not to throw an exception as soon
     * as we exceed maxLength.
     *
     * @param failFast Whether or not to throw an exception as soon as we exceed maxLength
     */
    public HAProxyMessageDecoder(bool failFast)
    {
        v2MaxHeaderSize = V2_MAX_LENGTH;
        this.failFast = failFast;
    }

    /**
     * Creates a new decoder with restricted additional data (TLV) size, and should throw an exception as soon as
     * we exceed maxLength.
     * <p>
     * <b>Note:</b> limiting TLV size only affects processing of v2, binary headers. Also, as allowed by the 1.5 spec
     * TLV data is currently ignored. For maximum performance it would be best to configure your upstream proxy host to
     * <b>NOT</b> send TLV data and instantiate with a max TLV size of {@code 0}.
     * </p>
     *
     * @param maxTlvSize maximum number of bytes allowed for additional data (Type-Length-Value vectors) in a v2 header
     */
    public HAProxyMessageDecoder(int maxTlvSize) : this(maxTlvSize, true)
    {
    }

    /**
     * Creates a new decoder with restricted additional data (TLV) size, whether or not to throw an exception as soon
     * as we exceed maxLength.
     *
     * @param maxTlvSize maximum number of bytes allowed for additional data (Type-Length-Value vectors) in a v2 header
     * @param failFast Whether or not to throw an exception as soon as we exceed maxLength
     */
    public HAProxyMessageDecoder(int maxTlvSize, bool failFast)
    {
        if (maxTlvSize < 1)
        {
            v2MaxHeaderSize = V2_MIN_LENGTH;
        }
        else if (maxTlvSize > V2_MAX_TLV)
        {
            v2MaxHeaderSize = V2_MAX_LENGTH;
        }
        else
        {
            int calcMax = maxTlvSize + V2_MIN_LENGTH;
            if (calcMax > V2_MAX_LENGTH)
            {
                v2MaxHeaderSize = V2_MAX_LENGTH;
            }
            else
            {
                v2MaxHeaderSize = calcMax;
            }
        }
        this.failFast = failFast;
    }

    /**
     * Returns the proxy protocol specification version in the buffer if the version is found.
     * Returns -1 if no version was found in the buffer.
     */
    private static int FindVersion(ByteBuf buffer)
    {
        int n = buffer.ReadableBytes;
        // per spec, the version number is found in the 13th byte
        if (n < 13)
        {
            return -1;
        }

        int idx = buffer.ReaderIndex;
        return Match(BINARY_PREFIX, buffer, idx) ? buffer.GetByte(idx + BINARY_PREFIX_LENGTH) : 1;
    }

    /**
     * Returns the index in the buffer of the end of header if found.
     * Returns -1 if no end of header was found in the buffer.
     */
    private static int FindEndOfHeader(ByteBuf buffer)
    {
        int n = buffer.ReadableBytes;

        // per spec, the 15th and 16th bytes contain the address length in bytes
        if (n < 16)
        {
            return -1;
        }

        int offset = buffer.ReaderIndex + 14;

        // the total header length will be a fixed 16 byte sequence + the dynamic address information block
        int totalHeaderBytes = 16 + buffer.GetUnsignedShort(offset);

        // ensure we actually have the full header available
        if (n >= totalHeaderBytes)
        {
            return totalHeaderBytes;
        }
        else
        {
            return -1;
        }
    }

    /**
     * Returns the index in the buffer of the end of line found.
     * Returns -1 if no end of line was found in the buffer.
     */
    private static int FindEndOfLine(ByteBuf buffer)
    {
        int n = buffer.WriterIndex;
        for (int i = buffer.ReaderIndex; i < n; i++)
        {
            byte b = buffer.GetByte(i);
            if (b == '\r' && i < n - 1 && buffer.GetByte(i + 1) == '\n')
            {
                return i;  // \r\n
            }
        }
        return -1;  // Not found.
    }

    public override bool IsSingleDecode()
    {
        // ByteToMessageDecoder uses this method to optionally break out of the decoding loop after each unit of work.
        // Since we only ever want to decode a single header we always return true to save a bit of work here.
        return true;
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        base.ChannelRead(ctx, msg);
        if (finished)
        {
            ctx.Pipeline.Remove(this);
        }
    }

    protected sealed override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        // determine the specification version
        if (version == -1)
        {
            if ((version = FindVersion(@in)) == -1)
            {
                return;
            }
        }

        ByteBuf decoded;

        if (version == 1)
        {
            decoded = DecodeLine(ctx, @in);
        }
        else
        {
            decoded = DecodeStruct(ctx, @in);
        }

        if (decoded != null)
        {
            finished = true;
            try
            {
                if (version == 1)
                {
                    @out.Add(HAProxyMessage.DecodeHeader(decoded.ToString(Encoding.ASCII)));
                }
                else
                {
                    @out.Add(HAProxyMessage.DecodeHeader(decoded));
                }
            }
            catch (HAProxyProtocolException e)
            {
                Fail(ctx, null, e);
            }
        }
    }

    /**
     * Create a frame out of the {@link ByteBuf} and return it.
     *
     * @param ctx     the {@link ChannelHandlerContext} which this {@link HAProxyMessageDecoder} belongs to
     * @param buffer  the {@link ByteBuf} from which to read data
     * @return frame  the {@link ByteBuf} which represent the frame or {@code null} if no frame could
     *                be created
     */
    private ByteBuf DecodeStruct(IChannelHandlerContext ctx, ByteBuf buffer)
    {
        if (headerExtractor == null)
        {
            headerExtractor = new StructHeaderExtractor(this, v2MaxHeaderSize);
        }
        return headerExtractor.Extract(ctx, buffer);
    }

    /**
     * Create a frame out of the {@link ByteBuf} and return it.
     *
     * @param ctx     the {@link ChannelHandlerContext} which this {@link HAProxyMessageDecoder} belongs to
     * @param buffer  the {@link ByteBuf} from which to read data
     * @return frame  the {@link ByteBuf} which represent the frame or {@code null} if no frame could
     *                be created
     */
    private ByteBuf DecodeLine(IChannelHandlerContext ctx, ByteBuf buffer)
    {
        if (headerExtractor == null)
        {
            headerExtractor = new LineHeaderExtractor(this, V1_MAX_LENGTH);
        }
        return headerExtractor.Extract(ctx, buffer);
    }

    private void FailOverLimit(IChannelHandlerContext ctx, int length)
    {
        FailOverLimit(ctx, length.ToString(CultureInfo.InvariantCulture));
    }

    private void FailOverLimit(IChannelHandlerContext ctx, string length)
    {
        int maxLength = version == 1 ? V1_MAX_LENGTH : v2MaxHeaderSize;
        Fail(ctx, "header length (" + length + ") exceeds the allowed maximum (" +
                maxLength.ToString(CultureInfo.InvariantCulture) + ')', null);
    }

    private void Fail(IChannelHandlerContext ctx, string errMsg, System.Exception e)
    {
        finished = true;
        _ = ctx.CloseAsync(); // drop connection immediately per spec
        HAProxyProtocolException ppex;
        if (errMsg != null && e != null)
        {
            ppex = new HAProxyProtocolException(errMsg, e);
        }
        else if (errMsg != null)
        {
            ppex = new HAProxyProtocolException(errMsg);
        }
        else if (e != null)
        {
            ppex = new HAProxyProtocolException(e);
        }
        else
        {
            ppex = new HAProxyProtocolException();
        }
        throw ppex;
    }

    /**
     * Returns the {@link ProtocolDetectionResult} for the given {@link ByteBuf}.
     */
    public static ProtocolDetectionResult<HAProxyProtocolVersion> DetectProtocol(ByteBuf buffer)
    {
        if (buffer.ReadableBytes < 12)
        {
            return ProtocolDetectionResult<HAProxyProtocolVersion>.NeedsMoreData();
        }

        int idx = buffer.ReaderIndex;

        if (Match(BINARY_PREFIX, buffer, idx))
        {
            return DETECTION_RESULT_V2;
        }
        if (Match(TEXT_PREFIX, buffer, idx))
        {
            return DETECTION_RESULT_V1;
        }
        return ProtocolDetectionResult<HAProxyProtocolVersion>.Invalid();
    }

    private static bool Match(byte[] prefix, ByteBuf buffer, int idx)
    {
        for (int i = 0; i < prefix.Length; i++)
        {
            byte b = buffer.GetByte(idx + i);
            if (b != prefix[i])
            {
                return false;
            }
        }
        return true;
    }

    /**
     * HeaderExtractor create a header frame out of the {@link ByteBuf}.
     */
    // CLR: a Java inner class reads the fields of its enclosing instance (discarding, discardedBytes, failFast) and calls
    // its methods implicitly; a C# nested class has no enclosing instance, so the decoder is passed to the constructor
    // and used explicitly (https://learn.microsoft.com/dotnet/csharp/programming-guide/classes-and-structs/nested-types).
    private abstract class HeaderExtractor
    {
        private readonly HAProxyMessageDecoder decoder;

        /** Header max size */
        private readonly int maxHeaderSize;

        protected HeaderExtractor(HAProxyMessageDecoder decoder, int maxHeaderSize)
        {
            this.decoder = decoder;
            this.maxHeaderSize = maxHeaderSize;
        }

        /**
         * Create a frame out of the {@link ByteBuf} and return it.
         *
         * @param ctx     the {@link ChannelHandlerContext} which this {@link HAProxyMessageDecoder} belongs to
         * @param buffer  the {@link ByteBuf} from which to read data
         * @return frame  the {@link ByteBuf} which represent the frame or {@code null} if no frame could
         *                be created
         * @throws Exception if exceed maxLength
         */
        public ByteBuf Extract(IChannelHandlerContext ctx, ByteBuf buffer)
        {
            int eoh = FindEndOfHeader(buffer);
            if (!decoder.discarding)
            {
                if (eoh >= 0)
                {
                    int length = eoh - buffer.ReaderIndex;
                    if (length > maxHeaderSize)
                    {
                        buffer.ReaderIndex = eoh + DelimiterLength(buffer, eoh);
                        decoder.FailOverLimit(ctx, length);
                        return null;
                    }
                    ByteBuf frame = buffer.ReadSlice(length);
                    buffer.SkipBytes(DelimiterLength(buffer, eoh));
                    return frame;
                }
                else
                {
                    int length = buffer.ReadableBytes;
                    if (length > maxHeaderSize)
                    {
                        decoder.discardedBytes = length;
                        buffer.SkipBytes(length);
                        decoder.discarding = true;
                        if (decoder.failFast)
                        {
                            decoder.FailOverLimit(ctx, "over " + decoder.discardedBytes.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                    return null;
                }
            }
            else
            {
                if (eoh >= 0)
                {
                    int length = decoder.discardedBytes + eoh - buffer.ReaderIndex;
                    buffer.ReaderIndex = eoh + DelimiterLength(buffer, eoh);
                    decoder.discardedBytes = 0;
                    decoder.discarding = false;
                    if (!decoder.failFast)
                    {
                        decoder.FailOverLimit(ctx, "over " + length.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    decoder.discardedBytes += buffer.ReadableBytes;
                    buffer.SkipBytes(buffer.ReadableBytes);
                }
                return null;
            }
        }

        /**
         * Find the end of the header from the given {@link ByteBuf}，the end may be a CRLF, or the length given by the
         * header.
         *
         * @param buffer the buffer to be searched
         * @return {@code -1} if can not find the end, otherwise return the buffer index of end
         */
        protected abstract int FindEndOfHeader(ByteBuf buffer);

        /**
         * Get the length of the header delimiter.
         *
         * @param buffer the buffer where delimiter is located
         * @param eoh index of delimiter
         * @return length of the delimiter
         */
        protected abstract int DelimiterLength(ByteBuf buffer, int eoh);
    }

    private sealed class LineHeaderExtractor : HeaderExtractor
    {

        internal LineHeaderExtractor(HAProxyMessageDecoder decoder, int maxHeaderSize) : base(decoder, maxHeaderSize)
        {
        }

        protected override int FindEndOfHeader(ByteBuf buffer)
        {
            return FindEndOfLine(buffer);
        }

        protected override int DelimiterLength(ByteBuf buffer, int eoh)
        {
            return buffer.GetByte(eoh) == '\r' ? 2 : 1;
        }
    }

    private sealed class StructHeaderExtractor : HeaderExtractor
    {

        internal StructHeaderExtractor(HAProxyMessageDecoder decoder, int maxHeaderSize) : base(decoder, maxHeaderSize)
        {
        }

        protected override int FindEndOfHeader(ByteBuf buffer)
        {
            return HAProxyMessageDecoder.FindEndOfHeader(buffer);
        }

        protected override int DelimiterLength(ByteBuf buffer, int eoh)
        {
            return 0;
        }
    }
}
