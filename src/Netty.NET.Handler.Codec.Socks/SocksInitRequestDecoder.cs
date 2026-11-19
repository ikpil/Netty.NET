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
 * Decodes {@link ByteBuf}s into {@link SocksInitRequest}.
 * Before returning SocksRequest decoder removes itself from pipeline.
 */
// CLR: List<Object> out is IList<object> @out (codec-base). The nested State enum hides the inherited State() method
// ("a ... type that you introduce in a class or struct hides all base class members that share its name",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier), so it is declared with
// new and the decoder calls base.State(). "control can't fall through from one switch section to the next" in C#;
// "To imitate the fall-through behavior ... you can use the goto statement"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements), so each
// Java fall-through after checkpoint(...) is goto case. readUnsignedByte() is ReadByte(), which returns the unsigned
// System.Byte (owner decision, docs/common-clr-design.md). The unreachable default branch throws
// InvalidOperationException for java.lang.Error ("serious problems that a reasonable application should not try to
// catch", https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Error.html; "The exception that is
// thrown when a method call is invalid for the object's current state",
// https://learn.microsoft.com/en-us/dotnet/api/system.invalidoperationexception?view=net-10.0), as AsciiHeadersEncoder
// in codec-base; ByteToMessageDecoder wraps it in a DecoderException, where a java.lang.Error would propagate unwrapped.
public class SocksInitRequestDecoder : ReplayingDecoder<SocksInitRequestDecoder.State>
{

    public SocksInitRequestDecoder() : base(State.CHECK_PROTOCOL_VERSION)
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
                Checkpoint(State.READ_AUTH_SCHEMES);
                goto case State.READ_AUTH_SCHEMES;
            }
            case State.READ_AUTH_SCHEMES:
            {
                int authSchemeNum = byteBuf.ReadByte();
                if (authSchemeNum == 0)
                {
                    // RFC 1928 section 3 requires NMETHODS to be at least 1.
                    @out.Add(SocksCommonUtils.UNKNOWN_SOCKS_REQUEST);
                    break;
                }
                List<SocksAuthScheme> authSchemes = new List<SocksAuthScheme>(authSchemeNum);
                for (int i = 0; i < authSchemeNum; i++)
                {
                    authSchemes.Add(SocksAuthScheme.ValueOf(byteBuf.ReadByte()));
                }
                @out.Add(new SocksInitRequest(authSchemes));
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
        READ_AUTH_SCHEMES
    }
}
