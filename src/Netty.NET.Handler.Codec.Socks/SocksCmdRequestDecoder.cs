/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Generic;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * Decodes {@link ByteBuf}s into {@link SocksCmdRequest}.
 * Before returning SocksRequest decoder removes itself from pipeline.
 */
// CLR: the new State enum with base.State(), goto case for the Java fall-through and InvalidOperationException for
// java.lang.Error, with the evidence given in SocksInitRequestDecoder.cs; the switch over SocksAddressType is an
// if/else chain (see SocksAddressType.cs); CharsetUtil.US_ASCII is Encoding.ASCII.
public class SocksCmdRequestDecoder : ReplayingDecoder<SocksCmdRequestDecoder.State>
{

    private SocksCmdType cmdType;
    private SocksAddressType addressType;

    public SocksCmdRequestDecoder() : base(State.CHECK_PROTOCOL_VERSION)
    {
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf byteBuf, IList<object> @out)
    {
        switch (base.State())
        {
            case State.CHECK_PROTOCOL_VERSION:
            {
                if (byteBuf.ReadByte() != SocksProtocolVersion.SOCKS5.ByteValue())
                {
                    @out.Add(SocksCommonUtils.UNKNOWN_SOCKS_REQUEST);
                    break;
                }

                Checkpoint(State.READ_CMD_HEADER);
                goto case State.READ_CMD_HEADER;
            }
            case State.READ_CMD_HEADER:
            {
                cmdType = SocksCmdType.ValueOf(byteBuf.ReadByte());
                byteBuf.SkipBytes(1); // reserved
                addressType = SocksAddressType.ValueOf(byteBuf.ReadByte());
                Checkpoint(State.READ_CMD_ADDRESS);
                goto case State.READ_CMD_ADDRESS;
            }
            case State.READ_CMD_ADDRESS:
            {
                if (addressType == SocksAddressType.IPv4)
                {
                    string host = NetUtil.IntToIpAddress(ByteBufUtil.ReadIntBE(byteBuf));
                    int port = ByteBufUtil.ReadUnsignedShortBE(byteBuf);
                    @out.Add(new SocksCmdRequest(cmdType, addressType, host, port));
                }
                else if (addressType == SocksAddressType.DOMAIN)
                {
                    // CLR: the Java int fieldLength = readByte() is signed, so a length octet of 0x80-0xFF is negative
                    // and the readString call fails; (sbyte) keeps the Java value over the unsigned ReadByte() (see
                    // SocksAuthRequestDecoder).
                    int fieldLength = (sbyte) byteBuf.ReadByte();
                    string host = byteBuf.ReadString(fieldLength, Encoding.ASCII);
                    int port = ByteBufUtil.ReadUnsignedShortBE(byteBuf);
                    @out.Add(new SocksCmdRequest(cmdType, addressType, host, port));
                }
                else if (addressType == SocksAddressType.IPv6)
                {
                    byte[] bytes = new byte[16];
                    byteBuf.ReadBytes(bytes);
                    string host = SocksCommonUtils.Ipv6toStr(bytes);
                    int port = ByteBufUtil.ReadUnsignedShortBE(byteBuf);
                    @out.Add(new SocksCmdRequest(cmdType, addressType, host, port));
                }
                else if (addressType == SocksAddressType.UNKNOWN)
                {
                    @out.Add(SocksCommonUtils.UNKNOWN_SOCKS_REQUEST);
                }
                else
                {
                    throw new InvalidOperationException("Unexpected address type: " + addressType);
                }

                break;
            }
            default:
            {
                throw new InvalidOperationException("Unexpected request decoder type: " + base.State());
            }
        }

        ctx.Pipeline.Remove(this);
    }

    [UnstableApi]
    public new enum State
    {
        CHECK_PROTOCOL_VERSION,
        READ_CMD_HEADER,
        READ_CMD_ADDRESS
    }
}
