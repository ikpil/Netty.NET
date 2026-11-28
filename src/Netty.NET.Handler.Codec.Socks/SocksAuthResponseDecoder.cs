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
 * Decodes {@link ByteBuf}s into {@link SocksAuthResponse}.
 * Before returning SocksResponse decoder removes itself from pipeline.
 */
// CLR: the new State enum with base.State(), goto case for the Java fall-through and InvalidOperationException for
// java.lang.Error, with the evidence given in SocksInitRequestDecoder.cs.
public class SocksAuthResponseDecoder : ReplayingDecoder<SocksAuthResponseDecoder.State>
{

    public SocksAuthResponseDecoder() : base(State.CHECK_PROTOCOL_VERSION)
    {
    }

    protected override void Decode(IChannelHandlerContext channelHandlerContext, ByteBuf byteBuf, IList<object> @out)
    {
        switch (base.State())
        {
            case State.CHECK_PROTOCOL_VERSION:
            {
                if (byteBuf.ReadByte() != SocksSubnegotiationVersion.AUTH_PASSWORD.ByteValue())
                {
                    @out.Add(SocksCommonUtils.UNKNOWN_SOCKS_RESPONSE);
                    break;
                }

                Checkpoint(State.READ_AUTH_RESPONSE);
                goto case State.READ_AUTH_RESPONSE;
            }
            case State.READ_AUTH_RESPONSE:
            {
                SocksAuthStatus authStatus = SocksAuthStatus.ValueOf(byteBuf.ReadByte());
                @out.Add(new SocksAuthResponse(authStatus));
                break;
            }
            default:
            {
                throw new InvalidOperationException("Unexpected response decoder state: " + base.State());
            }
        }

        channelHandlerContext.Pipeline.Remove(this);
    }

    [UnstableApi]
    public new enum State
    {
        CHECK_PROTOCOL_VERSION,
        READ_AUTH_RESPONSE
    }
}
