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

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.HAProxy.HAProxyConstants;
using static Netty.NET.Handler.Codec.HAProxy.HAProxyProxiedProtocol;

namespace Netty.NET.Handler.Codec.HAProxy;

/**
 * Encodes an HAProxy proxy protocol message
 *
 * @see <a href="https://www.haproxy.org/download/1.8/doc/proxy-protocol.txt">Proxy Protocol Specification</a>
 */
// CLR: @Sharable is [Sharable] and the MessageToByteEncoder<HAProxyMessage> constructor takes
// typeof(HAProxyMessage), as in codec-base; 'out' is @out. A switch over the HAProxyProtocolVersion/AddressFamily
// constants uses case guards, because a C# case label is a pattern
// (https://learn.microsoft.com/dotnet/csharp/language-reference/statements/selection-statements).
// writeCharSequence(CharSequence, Charset) is WriteCharSequence(ReadOnlySpan<char>, Encoding) with Encoding.ASCII,
// readerIndex() is the ReaderIndex property and content() the Content property; numbers use the invariant culture.
[Sharable]
public sealed class HAProxyMessageEncoder : MessageToByteEncoder<HAProxyMessage>
{

    private const int V2_VERSION_BITMASK = 0x02 << 4;

    // Length for source/destination addresses for the UNIX family must be 108 bytes each.
    internal const int UNIX_ADDRESS_BYTES_LENGTH = 108;
    internal const int TOTAL_UNIX_ADDRESS_BYTES_LENGTH = UNIX_ADDRESS_BYTES_LENGTH * 2;

    public static readonly HAProxyMessageEncoder INSTANCE = new HAProxyMessageEncoder();

    private HAProxyMessageEncoder() : base(typeof(HAProxyMessage))
    {
    }

    protected override void Encode(IChannelHandlerContext ctx, HAProxyMessage msg, ByteBuf @out)
    {
        HAProxyProtocolVersion protocolVersion = msg.ProtocolVersion();

        switch (protocolVersion)
        {
            case var _ when protocolVersion == HAProxyProtocolVersion.V1:
                EncodeV1(msg, @out);
                break;
            case var _ when protocolVersion == HAProxyProtocolVersion.V2:
                EncodeV2(msg, @out);
                break;
            default:
                throw new HAProxyProtocolException("Unsupported version: " + msg.ProtocolVersion());
        }
    }

    // CLR: a null string converts to an empty ReadOnlySpan<char>, while the Java writeCharSequence(null, charset) throws a
    // NullPointerException (an UNKNOWN v1 message has null addresses), so the addresses are checked first with the
    // ArgumentNullException that ObjectUtil.CheckNotNull throws where checkNotNull throws NullPointerException.
    private static void EncodeV1(HAProxyMessage msg, ByteBuf @out)
    {
        @out.WriteBytes(TEXT_PREFIX);
        @out.WriteByte((byte) ' ');
        @out.WriteCharSequence(msg.ProxiedProtocol().Name(), Encoding.ASCII);
        @out.WriteByte((byte) ' ');
        @out.WriteCharSequence(ObjectUtil.CheckNotNull(msg.SourceAddress(), "sequence"), Encoding.ASCII);
        @out.WriteByte((byte) ' ');
        @out.WriteCharSequence(ObjectUtil.CheckNotNull(msg.DestinationAddress(), "sequence"), Encoding.ASCII);
        @out.WriteByte((byte) ' ');
        @out.WriteCharSequence(msg.SourcePort().ToString(CultureInfo.InvariantCulture), Encoding.ASCII);
        @out.WriteByte((byte) ' ');
        @out.WriteCharSequence(msg.DestinationPort().ToString(CultureInfo.InvariantCulture), Encoding.ASCII);
        @out.WriteByte((byte) '\r');
        @out.WriteByte((byte) '\n');
    }

    private static void EncodeV2(HAProxyMessage msg, ByteBuf @out)
    {
        @out.WriteBytes(BINARY_PREFIX);
        @out.WriteByte(V2_VERSION_BITMASK | msg.Command().ByteValue());
        @out.WriteByte(msg.ProxiedProtocol().ByteValue());

        AddressFamily addressFamily = msg.ProxiedProtocol().AddressFamily_();

        switch (addressFamily)
        {
            case var _ when addressFamily == AddressFamily.AF_IPv4:
            case var _ when addressFamily == AddressFamily.AF_IPv6:
                byte[] srcAddrBytes = NetUtil.CreateByteArrayFromIpAddressString(msg.SourceAddress());
                byte[] dstAddrBytes = NetUtil.CreateByteArrayFromIpAddressString(msg.DestinationAddress());
                // srcAddrLen + dstAddrLen + 4 (srcPort + dstPort) + numTlvBytes
                @out.WriteShort(srcAddrBytes.Length + dstAddrBytes.Length + 4 + msg.TlvNumBytes());
                @out.WriteBytes(srcAddrBytes);
                @out.WriteBytes(dstAddrBytes);
                @out.WriteShort(msg.SourcePort());
                @out.WriteShort(msg.DestinationPort());
                EncodeTlvs(msg.Tlvs(), @out);
                break;
            case var _ when addressFamily == AddressFamily.AF_UNIX:
                @out.WriteShort(TOTAL_UNIX_ADDRESS_BYTES_LENGTH + msg.TlvNumBytes());
                int srcAddrBytesWritten = @out.WriteCharSequence(msg.SourceAddress(), Encoding.ASCII);
                @out.WriteZero(UNIX_ADDRESS_BYTES_LENGTH - srcAddrBytesWritten);
                int dstAddrBytesWritten = @out.WriteCharSequence(msg.DestinationAddress(), Encoding.ASCII);
                @out.WriteZero(UNIX_ADDRESS_BYTES_LENGTH - dstAddrBytesWritten);
                EncodeTlvs(msg.Tlvs(), @out);
                break;
            case var _ when addressFamily == AddressFamily.AF_UNSPEC:
                @out.WriteShort(0);
                break;
            default:
                throw new HAProxyProtocolException("unexpected addrFamily");
        }
    }

    private static void EncodeTlv(HAProxyTLV haProxyTLV, ByteBuf @out)
    {
        if (haProxyTLV is HAProxySSLTLV)
        {
            HAProxySSLTLV ssltlv = (HAProxySSLTLV) haProxyTLV;
            @out.WriteByte(haProxyTLV.TypeByteValue());
            @out.WriteShort(ssltlv.ContentNumBytes());
            @out.WriteByte(ssltlv.Client());
            @out.WriteInt(ssltlv.Verify());
            EncodeChildTlvs(ssltlv.EncapsulatedTLVs(), @out);
        }
        else
        {
            @out.WriteByte(haProxyTLV.TypeByteValue());
            ByteBuf value = haProxyTLV.Content;
            int readableBytes = value.ReadableBytes;
            @out.WriteShort(readableBytes);
            // Do not use readSlice(...) (or similar) here as it would mutate the reader index of the
            // content, which is observable by the caller (e.g. HAProxyMessage.tlvs() exposes the same
            // TLV instance both as a top-level entry and, if it is a direct child of a PP2_TYPE_SSL TLV,
            // flattened into the top-level list) and could result in the same TLV being encoded
            // correctly the first time and as empty content thereafter.
            @out.WriteBytes(value, value.ReaderIndex, readableBytes);
        }
    }

    /**
     * Encodes a genuine tree of encapsulated TLVs, e.g. the direct children of a {@link HAProxySSLTLV}.
     * Unlike {@link HAProxyMessage#tlvs()}, this list is not a flattened view, so every element must be
     * encoded.
     */
    private static void EncodeChildTlvs(IList<HAProxyTLV> haProxyTLVs, ByteBuf @out)
    {
        for (int i = 0; i < haProxyTLVs.Count; i++)
        {
            EncodeTlv(haProxyTLVs[i], @out);
        }
    }

    /**
     * Encodes the top-level TLV list as returned by {@link HAProxyMessage#tlvs()}. That list flattens the
     * direct children of any {@link HAProxySSLTLV} into it for convenience, so those flattened entries must
     * be skipped here: they are already encoded (recursively) as part of their parent
     * {@link HAProxySSLTLV}. Encoding them again would both duplicate their bytes on the wire and make the
     * declared TLV length (see {@link HAProxyMessage#tlvNumBytes()}) not match the number of bytes actually
     * written.
     */
    private static void EncodeTlvs(IList<HAProxyTLV> haProxyTLVs, ByteBuf @out)
    {
        for (int i = 0; i < haProxyTLVs.Count; i += 1 + HAProxyMessage.FlattenedChildren(haProxyTLVs, i))
        {
            EncodeTlv(haProxyTLVs[i], @out);
        }
    }
}
