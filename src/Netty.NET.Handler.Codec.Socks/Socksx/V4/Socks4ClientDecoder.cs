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
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socksx.V4;

/**
 * Decodes a single {@link Socks4CommandResponse} from the inbound {@link ByteBuf}s.
 * On successful decode, this decoder will forward the received data to the next handler, so that
 * other handler can remove this decoder later.  On failed decode, this decoder will discard the
 * received data, so that other handler closes the connection later.
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in (C# keywords). The new State enum with base.State() and
// goto case for the Java fall-through follow Netty.NET.Handler.Codec.Socks.SocksInitRequestDecoder (evidence there).
// Signal is an Exception here but a java.lang.Error in the source, which catch (Exception) does not catch, so the
// catch filters when (e is not Signal) and the REPLAY signal still reaches ReplayingDecoder (docs/codec-base-porting.md,
// Signals). readUnsignedByte() is ReadByte() (unsigned System.Byte, docs/common-clr-design.md); numbers in messages use
// the invariant culture (see Netty.NET.Handler.Codec.Socks.SocksCmdRequest).
public class Socks4ClientDecoder : ReplayingDecoder<Socks4ClientDecoder.State>
{

    [UnstableApi]
    public new enum State
    {
        START,
        SUCCESS,
        FAILURE
    }

    public Socks4ClientDecoder() : base(State.START)
    {
        SetSingleDecode(true);
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        try
        {
            switch (base.State())
            {
            case State.START:
            {
                int version = @in.ReadByte();
                if (version != 0)
                {
                    throw new DecoderException(
                            "unsupported reply version: " + version.ToString(CultureInfo.InvariantCulture) + " (expected: 0)");
                }

                Socks4CommandStatus status = Socks4CommandStatus.ValueOf(@in.ReadByte());
                int dstPort = ByteBufUtil.ReadUnsignedShortBE(@in);
                string dstAddr = NetUtil.IntToIpAddress(ByteBufUtil.ReadIntBE(@in));

                @out.Add(new DefaultSocks4CommandResponse(status, dstAddr, dstPort));
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

        ISocks4CommandResponse m = new DefaultSocks4CommandResponse(Socks4CommandStatus.REJECTED_OR_FAILED);
        m.SetDecoderResult(DecoderResult.Failure(cause));
        @out.Add(m);

        Checkpoint(State.FAILURE);
    }
}
