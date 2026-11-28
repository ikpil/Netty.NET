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

using System;
using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * Decodes a single {@link Socks5CommandRequest} from the inbound {@link ByteBuf}s.
 * On successful decode, this decoder will forward the received data to the next handler, so that
 * other handler can remove or replace this decoder later.  On failed decode, this decoder will
 * discard the received data, so that other handler closes the connection later.
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in; the new State enum, goto case and the
// catch (Exception) when (e is not Signal) filter follow Netty.NET.Handler.Codec.Socksx.V4.Socks4ClientDecoder
// (evidence there). The version byte is printed as the signed Java byte: readByte() is the unsigned System.Byte
// (owner decision, docs/common-clr-design.md) where "For byte, from -128 to 127, inclusive"
// (https://docs.oracle.com/javase/specs/jls/se21/html/jls-4.html#jls-4.2.1), so the message converts (sbyte) version
// with the invariant culture; the != comparison with SOCKS5 (5) does not depend on the sign.
public class Socks5CommandRequestDecoder : ReplayingDecoder<Socks5CommandRequestDecoder.State>
{

    [UnstableApi]
    public new enum State
    {
        INIT,
        SUCCESS,
        FAILURE
    }

    private readonly ISocks5AddressDecoder addressDecoder;

    public Socks5CommandRequestDecoder() : this(ISocks5AddressDecoder.DEFAULT)
    {
    }

    public Socks5CommandRequestDecoder(ISocks5AddressDecoder addressDecoder) : base(State.INIT)
    {
        this.addressDecoder = ObjectUtil.CheckNotNull(addressDecoder, "addressDecoder");
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        try
        {
            switch (base.State())
            {
            case State.INIT:
            {
                byte version = @in.ReadByte();
                if (version != SocksVersion.SOCKS5.ByteValue())
                {
                    throw new DecoderException(
                            "unsupported version: " + ((sbyte) version).ToString(CultureInfo.InvariantCulture) +
                            " (expected: " + SocksVersion.SOCKS5.ByteValue().ToString(CultureInfo.InvariantCulture) + ')');
                }

                Socks5CommandType type = Socks5CommandType.ValueOf(@in.ReadByte());
                @in.SkipBytes(1); // RSV
                Socks5AddressType dstAddrType = Socks5AddressType.ValueOf(@in.ReadByte());
                string dstAddr = addressDecoder.DecodeAddress(dstAddrType, @in);
                int dstPort = ByteBufUtil.ReadUnsignedShortBE(@in);

                @out.Add(new DefaultSocks5CommandRequest(type, dstAddrType, dstAddr, dstPort));
                Checkpoint(State.SUCCESS);
                goto case State.SUCCESS;
            }
            case State.SUCCESS:
            {
                int readableBytes = ActualReadableBytes();
                if (readableBytes > 0)
                {
                    @out.Add(@in.ReadRetainedSlice(readableBytes));
                }

                break;
            }
            case State.FAILURE:
            {
                @in.SkipBytes(ActualReadableBytes());
                break;
            }
            }
        }
        catch (Exception e) when (e is not Signal)
        {
            Fail(@out, e);
        }
    }

    private void Fail(IList<object> @out, Exception cause)
    {
        if (!(cause is DecoderException))
        {
            cause = new DecoderException(cause);
        }

        Checkpoint(State.FAILURE);

        ISocks5Message m = new DefaultSocks5CommandRequest(
                Socks5CommandType.CONNECT, Socks5AddressType.IPv4, "0.0.0.0", 1);
        m.SetDecoderResult(DecoderResult.Failure(cause));
        @out.Add(m);
    }
}
