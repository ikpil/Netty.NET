/*
 * Copyright 2025 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * Decodes a single {@link Socks5PrivateAuthRequest} from the inbound {@link ByteBuf}s.
 * On successful decode, this decoder will forward the received data to the next handler, so that
 * other handler can remove or replace this decoder later.
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in. A plain ByteToMessageDecoder throws no REPLAY signal,
// so catch (Exception) needs no Signal filter. The version is printed as the signed Java byte ((sbyte) version,
// invariant culture; see Socks5CommandRequestDecoder); getUnsignedByte is GetByte (unsigned System.Byte).
// EmptyArrays.EMPTY_BYTES is the Common EmptyArrays.EMPTY_BYTES.
public sealed class Socks5PrivateAuthRequestDecoder : ByteToMessageDecoder
{

    private bool decoded;

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        try
        {
            if (decoded)
            {
                int readableBytes = @in.ReadableBytes;
                if (readableBytes > 0)
                {
                    @out.Add(@in.ReadRetainedSlice(readableBytes));
                }

                return;
            }

            // Check if we have enough data to decode the message
            if (@in.ReadableBytes < 2)
            {
                return;
            }

            int startOffset = @in.ReaderIndex;
            byte version = @in.GetByte(startOffset);
            if (version != 1)
            {
                throw new DecoderException("unsupported subnegotiation version: " +
                        ((sbyte) version).ToString(CultureInfo.InvariantCulture) + " (expected: 1)");
            }

            int tokenLength = @in.GetByte(startOffset + 1);

            // Check if the full message is available
            if (@in.ReadableBytes < 2 + tokenLength)
            {
                return;
            }

            // Read the version and token length
            @in.SkipBytes(2);

            // Read the token
            byte[] token = new byte[tokenLength];
            @in.ReadBytes(token);

            // Add the decoded token to the output list
            @out.Add(new DefaultSocks5PrivateAuthRequest(token));

            // Mark as decoded to handle remaining bytes in future calls
            decoded = true;
        }
        catch (Exception e)
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

        decoded = true;

        ISocks5Message m = new
            DefaultSocks5PrivateAuthRequest(EmptyArrays.EMPTY_BYTES);
        m.SetDecoderResult(DecoderResult.Failure(cause));
        @out.Add(m);
    }
}
