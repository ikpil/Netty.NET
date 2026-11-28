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
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * Decodes {@link ByteBuf}s into {@link SocksAuthRequest}.
 * Before returning SocksRequest decoder removes itself from pipeline.
 */
// CLR: the new State enum with base.State(), goto case for the Java fall-through and InvalidOperationException for
// java.lang.Error, with the evidence given in SocksInitRequestDecoder.cs.
public class SocksAuthRequestDecoder : ReplayingDecoder<SocksAuthRequestDecoder.State>
{

    private string username;

    public SocksAuthRequestDecoder() : base(State.CHECK_PROTOCOL_VERSION)
    {
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf byteBuf, IList<object> @out)
    {
        switch (base.State())
        {
            case State.CHECK_PROTOCOL_VERSION:
            {
                if (byteBuf.ReadByte() != SocksSubnegotiationVersion.AUTH_PASSWORD.ByteValue())
                {
                    @out.Add(SocksCommonUtils.UNKNOWN_SOCKS_REQUEST);
                    break;
                }

                Checkpoint(State.READ_USERNAME);
                goto case State.READ_USERNAME;
            }
            case State.READ_USERNAME:
            {
                // CLR: the Java int fieldLength = readByte() sign-extends, so a length octet of 0x80-0xFF is negative
                // ("For byte, from -128 to 127, inclusive", https://docs.oracle.com/javase/specs/jls/se21/html/jls-4.html#jls-4.2.1)
                // and the readString call fails; ReadByte() is the unsigned System.Byte ("Represents an 8-bit
                // unsigned integer", https://learn.microsoft.com/en-us/dotnet/api/system.byte?view=net-10.0, owner
                // decision in docs/common-clr-design.md), so (sbyte) keeps the Java value.
                int fieldLength = (sbyte) byteBuf.ReadByte();
                username = byteBuf.ReadString(fieldLength, Encoding.ASCII);
                Checkpoint(State.READ_PASSWORD);
                goto case State.READ_PASSWORD;
            }
            case State.READ_PASSWORD:
            {
                // CLR: (sbyte) keeps the signed Java length (see READ_USERNAME).
                int fieldLength = (sbyte) byteBuf.ReadByte();
                string password = byteBuf.ReadString(fieldLength, Encoding.ASCII);
                @out.Add(new SocksAuthRequest(username, password));
                break;
            }
            default:
            {
                throw new InvalidOperationException("Unexpected request decoder state: " + base.State());
            }
        }

        ctx.Pipeline.Remove(this);
    }

    [UnstableApi]
    public new enum State
    {
        CHECK_PROTOCOL_VERSION,
        READ_USERNAME,
        READ_PASSWORD
    }
}
