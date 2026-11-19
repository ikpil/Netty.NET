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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Socksx.V4;

/**
 * Decodes a single {@link Socks4CommandRequest} from the inbound {@link ByteBuf}s.
 * On successful decode, this decoder will forward the received data to the next handler, so that
 * other handler can remove this decoder later.  On failed decode, this decoder will discard the
 * received data, so that other handler closes the connection later.
 */
// CLR: List<Object> out is IList<object> @out and 'in' is @in; the new State enum, goto case and the
// catch (Exception) when (e is not Signal) filter follow Socks4ClientDecoder (evidence there). readUnsignedByte() is
// ReadByte() (unsigned System.Byte) and the comparison with SocksVersion.SOCKS4a.byteValue() (4) does not depend on
// the sign. String.startsWith is ordinal ("Tests if this string starts with the specified prefix",
// https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/String.html), while String.StartsWith(string)
// "performs a word (case-sensitive and culture-sensitive) comparison using the current culture"
// (https://learn.microsoft.com/en-us/dotnet/api/system.string.startswith?view=net-10.0), so StringComparison.Ordinal is
// passed. CharsetUtil.US_ASCII is Encoding.ASCII; numbers in messages use the invariant culture.
public class Socks4ServerDecoder : ReplayingDecoder<Socks4ServerDecoder.State>
{

    private const int MAX_FIELD_LENGTH = 255;

    [UnstableApi]
    public new enum State
    {
        START,
        READ_USERID,
        READ_DOMAIN,
        SUCCESS,
        FAILURE
    }

    private Socks4CommandType type;
    private string dstAddr;
    private int dstPort;
    private string userId;

    public Socks4ServerDecoder() : base(State.START)
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
                if (version != SocksVersion.SOCKS4a.ByteValue())
                {
                    throw new DecoderException("unsupported protocol version: " + version.ToString(CultureInfo.InvariantCulture));
                }

                type = Socks4CommandType.ValueOf(@in.ReadByte());
                dstPort = ByteBufUtil.ReadUnsignedShortBE(@in);
                dstAddr = NetUtil.IntToIpAddress(ByteBufUtil.ReadIntBE(@in));
                Checkpoint(State.READ_USERID);
                goto case State.READ_USERID;
            }
            case State.READ_USERID:
            {
                userId = ReadString("userid", @in);
                Checkpoint(State.READ_DOMAIN);
                goto case State.READ_DOMAIN;
            }
            case State.READ_DOMAIN:
            {
                // Check for Socks4a protocol marker 0.0.0.x
                if (!"0.0.0.0".Equals(dstAddr) && dstAddr.StartsWith("0.0.0.", StringComparison.Ordinal))
                {
                    dstAddr = ReadString("dstAddr", @in);
                }
                @out.Add(new DefaultSocks4CommandRequest(type, dstAddr, dstPort, userId));
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

        ISocks4CommandRequest m = new DefaultSocks4CommandRequest(
                type != null? type : Socks4CommandType.CONNECT,
                dstAddr != null? dstAddr : "",
                dstPort != 0? dstPort : 65535,
                userId != null? userId : "");

        m.SetDecoderResult(DecoderResult.Failure(cause));
        @out.Add(m);

        Checkpoint(State.FAILURE);
    }

    /**
     * Reads a variable-length NUL-terminated string as defined in SOCKS4.
     */
    private static string ReadString(string fieldName, ByteBuf @in)
    {
        int length = @in.BytesBefore(MAX_FIELD_LENGTH + 1, (byte) 0);
        if (length < 0)
        {
            throw new DecoderException("field '" + fieldName + "' longer than " +
                    MAX_FIELD_LENGTH.ToString(CultureInfo.InvariantCulture) + " chars");
        }

        string value = @in.ReadSlice(length).ToString(Encoding.ASCII);
        @in.SkipBytes(1); // Skip the NUL.

        return value;
    }
}
