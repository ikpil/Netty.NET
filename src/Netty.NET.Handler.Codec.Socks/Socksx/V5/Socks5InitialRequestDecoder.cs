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
 * Decodes a single {@link Socks5InitialRequest} from the inbound {@link ByteBuf}s.
 * On successful decode, this decoder will forward the received data to the next handler, so that
 * other handler can remove or replace this decoder later.  On failed decode, this decoder will
 * discard the received data, so that other handler closes the connection later.
 */
// CLR: as Socks5CommandRequestDecoder: the new State enum, goto case, the when (e is not Signal) filter and the
// version printed as the signed Java byte ((sbyte) version, invariant culture). readUnsignedByte() is ReadByte().
public class Socks5InitialRequestDecoder : ReplayingDecoder<Socks5InitialRequestDecoder.State>
{

    [UnstableApi]
    public new enum State
    {
        INIT,
        SUCCESS,
        FAILURE
    }

    public Socks5InitialRequestDecoder() : base(State.INIT)
    {
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

                int authMethodCnt = @in.ReadByte();

                Socks5AuthMethod[] authMethods = new Socks5AuthMethod[authMethodCnt];
                for (int i = 0; i < authMethodCnt; i++)
                {
                    authMethods[i] = Socks5AuthMethod.ValueOf(@in.ReadByte());
                }

                @out.Add(new DefaultSocks5InitialRequest(authMethods));
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

        ISocks5Message m = new DefaultSocks5InitialRequest(Socks5AuthMethod.NO_AUTH);
        m.SetDecoderResult(DecoderResult.Failure(cause));
        @out.Add(m);
    }
}
