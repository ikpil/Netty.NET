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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * Encodes a client-side {@link Socks5Message} into a {@link ByteBuf}.
 */
// CLR: the Class argument is a Type (MessageToByteEncoder in codec-base), @Sharable is [Sharable], instanceof is the is
// operator and StringUtil.simpleClassName is StringUtil.SimpleClassName. List is IList; the RandomAccess marker
// ("used by List implementations to indicate that they support fast (generally constant time) random access",
// https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/RandomAccess.html) has no counterpart because
// IList<T> is "a collection of objects that can be individually accessed by index"
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.ilist-1?view=net-10.0), so the indexed loop
// of the RandomAccess branch is the only loop. Numbers in messages use the invariant culture.
[Sharable]
public class Socks5ClientEncoder : MessageToByteEncoder<ISocks5Message>
{

    public static readonly Socks5ClientEncoder DEFAULT = new Socks5ClientEncoder();

    private readonly ISocks5AddressEncoder addressEncoder;

    /**
     * Creates a new instance with the default {@link Socks5AddressEncoder}.
     */
    protected Socks5ClientEncoder() : this(ISocks5AddressEncoder.DEFAULT)
    {
    }

    /**
     * Creates a new instance with the specified {@link Socks5AddressEncoder}.
     */
    public Socks5ClientEncoder(ISocks5AddressEncoder addressEncoder) : base(typeof(ISocks5Message))
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
        if (msg is ISocks5InitialRequest)
        {
            EncodeAuthMethodRequest((ISocks5InitialRequest) msg, @out);
        }
        else if (msg is ISocks5PasswordAuthRequest)
        {
            EncodePasswordAuthRequest((ISocks5PasswordAuthRequest) msg, @out);
        }
        else if (msg is ISocks5PrivateAuthRequest)
        {
            EncodePrivateAuthRequest((ISocks5PrivateAuthRequest) msg, @out);
        }
        else if (msg is ISocks5CommandRequest)
        {
            EncodeCommandRequest((ISocks5CommandRequest) msg, @out);
        }
        else
        {
            throw new EncoderException("unsupported message type: " + StringUtil.SimpleClassName(msg));
        }
    }

    private static void EncodeAuthMethodRequest(ISocks5InitialRequest msg, ByteBuf @out)
    {
        @out.WriteByte(msg.Version().ByteValue());

        IList<Socks5AuthMethod> authMethods = msg.AuthMethods();
        int numAuthMethods = authMethods.Count;
        WriteFieldLength(@out, numAuthMethods);

        for (int i = 0; i < numAuthMethods; i ++)
        {
            @out.WriteByte(authMethods[i].ByteValue());
        }
    }

    private static void EncodePasswordAuthRequest(ISocks5PasswordAuthRequest msg, ByteBuf @out)
    {
        @out.WriteByte(0x01);

        string username = msg.Username();
        WriteFieldLength(@out, username.Length);
        ByteBufUtil.WriteAscii(@out, username);

        string password = msg.Password();
        WriteFieldLength(@out, password.Length);
        ByteBufUtil.WriteAscii(@out, password);
    }

    private static void EncodePrivateAuthRequest(ISocks5PrivateAuthRequest msg, ByteBuf @out)
    {
        byte[] bytes = msg.PrivateToken();
        @out.WriteByte(0x01);
        WriteFieldLength(@out, bytes.Length);
        @out.WriteBytes(bytes);
    }

    private void EncodeCommandRequest(ISocks5CommandRequest msg, ByteBuf @out)
    {
        @out.WriteByte(msg.Version().ByteValue());
        @out.WriteByte(msg.Type().ByteValue());
        @out.WriteByte(0x00);

        Socks5AddressType dstAddrType = msg.DstAddrType();
        @out.WriteByte(dstAddrType.ByteValue());
        string addrValue = msg.DstAddr();
        if (addrValue != null && dstAddrType == Socks5AddressType.DOMAIN)
        {
            CheckFieldLength(addrValue.Length);
        }

        addressEncoder.EncodeAddress(dstAddrType, addrValue, @out);
        ByteBufUtil.WriteShortBE(@out, msg.DstPort());
    }

    private static void WriteFieldLength(ByteBuf @out, int length)
    {
        CheckFieldLength(length);
        @out.WriteByte(length);
    }

    private static void CheckFieldLength(int length)
    {
        if (length > 255 || length < 0)
        {
            throw new EncoderException("Invalid field length value: " + length.ToString(CultureInfo.InvariantCulture));
        }
    }
}
