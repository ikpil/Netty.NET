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
 * Encodes a SOCKS5 address into binary representation.
 *
 * @see Socks5ClientEncoder
 * @see Socks5ServerEncoder
 */
// CLR: The interface constant DEFAULT is a static readonly field and its anonymous class the nested
// DefaultSocks5AddressEncoder (see ISocks5AddressDecoder). throws Exception has no counterpart.
public interface ISocks5AddressEncoder
{

    public static readonly ISocks5AddressEncoder DEFAULT = new DefaultSocks5AddressEncoder();

    private sealed class DefaultSocks5AddressEncoder : ISocks5AddressEncoder
    {
        public void EncodeAddress(Socks5AddressType addrType, string addrValue, ByteBuf @out)
        {
            byte typeVal = addrType.ByteValue();
            if (typeVal == Socks5AddressType.IPv4.ByteValue())
            {
                if (addrValue != null)
                {
                    @out.WriteBytes(NetUtil.CreateByteArrayFromIpAddressString(addrValue));
                }
                else
                {
                    @out.WriteInt(0);
                }
            }
            else if (typeVal == Socks5AddressType.DOMAIN.ByteValue())
            {
                if (addrValue != null)
                {
                    @out.WriteByte(addrValue.Length);
                    @out.WriteCharSequence(addrValue, Encoding.ASCII);
                }
                else
                {
                    @out.WriteByte(0);
                }
            }
            else if (typeVal == Socks5AddressType.IPv6.ByteValue())
            {
                if (addrValue != null)
                {
                    @out.WriteBytes(NetUtil.CreateByteArrayFromIpAddressString(addrValue));
                }
                else
                {
                    @out.WriteLong(0);
                    @out.WriteLong(0);
                }
            }
            else
            {
                throw new EncoderException("unsupported addrType: " +
                        (addrType.ByteValue() & 0xFF).ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /**
     * Encodes a SOCKS5 address.
     *
     * @param addrType the type of the address
     * @param addrValue the string representation of the address
     * @param out the output buffer where the encoded SOCKS5 address field will be written to
     */
    void EncodeAddress(Socks5AddressType addrType, string addrValue, ByteBuf @out);
}
