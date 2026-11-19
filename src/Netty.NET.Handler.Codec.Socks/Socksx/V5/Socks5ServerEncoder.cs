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

using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * Encodes a server-side {@link Socks5Message} into a {@link ByteBuf}.
 */
// CLR: the Class argument is a Type (MessageToByteEncoder in codec-base), @Sharable is [Sharable], instanceof is the is
// operator and StringUtil.simpleClassName is StringUtil.SimpleClassName; numbers in messages use the invariant
// culture.
[Sharable]
public class Socks5ServerEncoder : MessageToByteEncoder<ISocks5Message>
{

    public static readonly Socks5ServerEncoder DEFAULT = new Socks5ServerEncoder(ISocks5AddressEncoder.DEFAULT);

    private readonly ISocks5AddressEncoder addressEncoder;

    /**
     * Creates a new instance with the default {@link Socks5AddressEncoder}.
     */
    protected Socks5ServerEncoder() : this(ISocks5AddressEncoder.DEFAULT)
    {
    }

    /**
     * Creates a new instance with the specified {@link Socks5AddressEncoder}.
     */
    public Socks5ServerEncoder(ISocks5AddressEncoder addressEncoder) : base(typeof(ISocks5Message))
    {
        this.addressEncoder = ObjectUtil.CheckNotNull(addressEncoder, "addressEncoder");
    }

    /**
     * Returns the {@link Socks5AddressEncoder} of this encoder.
     */
    protected ISocks5AddressEncoder AddressEncoder()
    {
        return addressEncoder;
    }

    protected override void Encode(IChannelHandlerContext ctx, ISocks5Message msg, ByteBuf @out)
    {
        if (msg is ISocks5InitialResponse)
        {
            EncodeAuthMethodResponse((ISocks5InitialResponse) msg, @out);
        }
        else if (msg is ISocks5PasswordAuthResponse)
        {
            EncodePasswordAuthResponse((ISocks5PasswordAuthResponse) msg, @out);
        }
        else if (msg is ISocks5CommandResponse)
        {
            EncodeCommandResponse((ISocks5CommandResponse) msg, @out);
        }
        else if (msg is ISocks5PrivateAuthResponse)
        {
            EncodePrivateAuthResponse((ISocks5PrivateAuthResponse) msg, @out);
        }
        else
        {
            throw new EncoderException("unsupported message type: " + StringUtil.SimpleClassName(msg));
        }
    }

    private static void EncodeAuthMethodResponse(ISocks5InitialResponse msg, ByteBuf @out)
    {
        @out.WriteByte(msg.Version().ByteValue());
        @out.WriteByte(msg.AuthMethod().ByteValue());
    }

    private static void EncodePasswordAuthResponse(ISocks5PasswordAuthResponse msg, ByteBuf @out)
    {
        @out.WriteByte(0x01);
        @out.WriteByte(msg.Status().ByteValue());
    }

    private static void EncodePrivateAuthResponse(ISocks5PrivateAuthResponse msg, ByteBuf @out)
    {
        @out.WriteByte(0x01);
        @out.WriteByte(msg.Status().ByteValue());
    }

    private void EncodeCommandResponse(ISocks5CommandResponse msg, ByteBuf @out)
    {
        @out.WriteByte(msg.Version().ByteValue());
        @out.WriteByte(msg.Status().ByteValue());
        @out.WriteByte(0x00);

        Socks5AddressType bndAddrType = msg.BndAddrType();
        @out.WriteByte(bndAddrType.ByteValue());
        string addrValue = msg.BndAddr();
        if (addrValue != null && bndAddrType == Socks5AddressType.DOMAIN)
        {
            CheckFieldLength(addrValue.Length);
        }
        addressEncoder.EncodeAddress(bndAddrType, addrValue, @out);

        ByteBufUtil.WriteShortBE(@out, msg.BndPort());
    }

    private static void CheckFieldLength(int length)
    {
        if (length > 255 || length < 0)
        {
            throw new EncoderException("Invalid field length value: " + length.ToString(CultureInfo.InvariantCulture));
        }
    }
}
