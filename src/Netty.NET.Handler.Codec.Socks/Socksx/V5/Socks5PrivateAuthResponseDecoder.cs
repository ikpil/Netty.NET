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
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * Decodes a single {@link Socks5PrivateAuthResponse} from the inbound {@link ByteBuf}s.
 * On successful decode, this decoder will forward the received data to the next handler, so that
 * other handler can remove or replace this decoder later. On failed decode, this decoder will
 * discard the received data, so that other handler closes the connection later.
 * <p>
 * The default format follows a simple structure:
 * <ul>
 *   <li>1 byte: version (must be 1)</li>
 *   <li>1 byte: status (0x00 for success, 0xFF for failure)</li>
 * </ul>
 * </p>
 * <p>
 * For custom private authentication protocols, you can:
 * <ul>
 *   <li>Create a new decoder implementing {@link ByteToMessageDecoder} or similar</li>
 *   <li>Implement the {@link Socks5PrivateAuthResponse} interface
 *   or extend {@link DefaultSocks5PrivateAuthResponse}</li>
 *   <li>Create a custom handler chain to process the authentication responses</li>
 * </ul>
 * </p>
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in. A plain ByteToMessageDecoder throws no REPLAY signal,
// so catch (Exception) needs no Signal filter. The version is printed as the signed Java byte ((sbyte) version,
// invariant culture; see Socks5CommandRequestDecoder). The unreachable default branch throws InvalidOperationException
// for java.lang.Error (see Netty.NET.Handler.Codec.Socks.SocksInitRequestDecoder); unlike the Java Error it would be
// caught by catch (Exception) and reported through fail().
public sealed class Socks5PrivateAuthResponseDecoder : ByteToMessageDecoder
{

    /**
     * Decoder states for SOCKS5 private authentication responses.
     */
    private enum State
    {
        /**
         * Initial state.
         */
        INIT,
        /**
         * Authentication successful.
         */
        SUCCESS,
        /**
         * Authentication failed.
         */
        FAILURE
    }

    private State state = State.INIT;

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in,
                          IList<object> @out)
    {
        try
        {
            switch (state)
            {
                case State.INIT:
                    if (@in.ReadableBytes < 2)
                    {
                        return;
                    }

                    byte version = @in.ReadByte();
                    if (version != 1)
                    {
                        throw new DecoderException(
                            "unsupported subnegotiation version: " +
                            ((sbyte) version).ToString(CultureInfo.InvariantCulture) + " (expected: 1)");
                    }

                    @out.Add(new DefaultSocks5PrivateAuthResponse(
                        Socks5PrivateAuthStatus.ValueOf(@in.ReadByte())));
                    state = State.SUCCESS;
                    break;
                case State.SUCCESS:
                    int readableBytes = @in.ReadableBytes;
                    if (readableBytes > 0)
                    {
                        @out.Add(@in.ReadRetainedSlice(readableBytes));
                    }

                    break;
                case State.FAILURE:
                    @in.SkipBytes(@in.ReadableBytes);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected response decoder state: " + state);
            }
        }
        catch (Exception e)
        {
            Fail(@out, e);
        }
    }

    /**
     * Handles decoder failures by setting the appropriate error state.
     *
     * @param out   the output list to add the failure message to
     * @param cause the exception that caused the failure
     */
    private void Fail(IList<object> @out, Exception cause)
    {
        if (!(cause is DecoderException))
        {
            cause = new DecoderException(cause);
        }

        state = State.FAILURE;

        ISocks5Message m = new DefaultSocks5PrivateAuthResponse(Socks5PrivateAuthStatus.FAILURE);
        m.SetDecoderResult(DecoderResult.Failure(cause));
        @out.Add(m);
    }
}
