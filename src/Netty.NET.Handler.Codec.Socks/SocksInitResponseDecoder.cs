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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * Decodes {@link ByteBuf}s into {@link SocksInitResponse}.
 * Before returning SocksResponse decoder removes itself from pipeline.
 */
// CLR: the new State enum with base.State(), goto case for the Java fall-through and InvalidOperationException for
// java.lang.Error, with the evidence given in SocksInitRequestDecoder.cs.
public class SocksInitResponseDecoder : ReplayingDecoder<SocksInitResponseDecoder.State>
{

    public SocksInitResponseDecoder() : base(State.CHECK_PROTOCOL_VERSION)
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
                    @out.Add(SocksCommonUtils.UNKNOWN_SOCKS_RESPONSE);
                    break;
                }

                Checkpoint(State.READ_PREFERRED_AUTH_TYPE);
                goto case State.READ_PREFERRED_AUTH_TYPE;
            }
            case State.READ_PREFERRED_AUTH_TYPE:
            {
                SocksAuthScheme authScheme = SocksAuthScheme.ValueOf(byteBuf.ReadByte());
                @out.Add(new SocksInitResponse(authScheme));
                break;
            }
            default:
            {
                throw new InvalidOperationException("Unexpected response decoder type: " + base.State());
            }
        }

        ctx.Pipeline.Remove(this);
    }

    [UnstableApi]
    public new enum State
    {
        CHECK_PROTOCOL_VERSION,
        READ_PREFERRED_AUTH_TYPE
    }
}
