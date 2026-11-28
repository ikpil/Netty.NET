/*
 * Copyright 2015 The Netty Project
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

using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * Decodes a SOCKS5 address field into its string representation.
 *
 * @see Socks5CommandRequestDecoder
 * @see Socks5CommandResponseDecoder
 */
// CLR: Java interfaces take the I prefix (see Netty.NET.Handler.Codec.Socksx.ISocksMessage). The interface constant
// DEFAULT is a static readonly field and its anonymous class the nested DefaultSocks5AddressDecoder ("An interface
// declaration can contain ... Nested types ... Static fields",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface). throws Exception has no
// counterpart.
public interface ISocks5AddressDecoder
{

    public static readonly ISocks5AddressDecoder DEFAULT = new DefaultSocks5AddressDecoder();

    // CLR: the anonymous DEFAULT class.
    private sealed class DefaultSocks5AddressDecoder : ISocks5AddressDecoder
    {

        private const int IPv6_LEN = 16;

        public string DecodeAddress(Socks5AddressType addrType, ByteBuf @in)
        {
            if (addrType == Socks5AddressType.IPv4)
            {
                return NetUtil.IntToIpAddress(ByteBufUtil.ReadIntBE(@in));
            }

            if (addrType == Socks5AddressType.DOMAIN)
            {
                // CLR: readUnsignedByte() is ReadByte() (unsigned System.Byte, docs/common-clr-design.md);
                // CharsetUtil.US_ASCII is Encoding.ASCII.
                int length = @in.ReadByte();
                string domain = @in.ToString(@in.ReaderIndex, length, Encoding.ASCII);
                @in.SkipBytes(length);
                return domain;
            }

            if (addrType == Socks5AddressType.IPv6)
            {
                // CLR: the Buffer port has no hasArray()/array()/arrayOffset(); Memory borrowing replaces them and no
                // Java array facade is added (docs/buffer-porting.md, Array extraction). The source's other branch,
                // which reads the 16 bytes into a temporary array, is the only path here; it is also the path the
                // source takes for every ReplayingDecoder input, whose ReplayingDecoderByteBuf.hasArray() is false.
                byte[] tmp = new byte[IPv6_LEN];
                @in.ReadBytes(tmp);

                return NetUtil.BytesToIpAddress(tmp);
            }
            else
            {
                throw new DecoderException("unsupported address type: " +
                        (addrType.ByteValue() & 0xFF).ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /**
     * Decodes a SOCKS5 address field into its string representation.
     *
     * @param addrType the type of the address
     * @param in the input buffer which contains the SOCKS5 address field at its reader index
     */
    string DecodeAddress(Socks5AddressType addrType, ByteBuf @in);
}
