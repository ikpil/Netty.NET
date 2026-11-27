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
using System.Globalization;
using System.Numerics;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Factorial;

/**
 * Encodes a {@link Number} into the binary representation prepended with
 * a magic number ('F' or 0x46) and a 32-bit length prefix.  For example, 42
 * will be encoded to { 'F', 0, 0, 0, 1, 42 }.
 */
// CLR: java.lang.Number has no CLR counterpart: the boxed numeric types share no base class, so the encoder is a
// MessageToByteEncoder<object> whose AcceptOutboundMessage accepts the boxed numeric types (the subclasses of
// java.lang.Number: BigInteger and the integral and floating-point primitives). String.valueOf(msg) is the invariant
// string form and new BigInteger(String) is BigInteger.Parse, which throws FormatException where Java throws
// NumberFormatException (a fractional value). toByteArray() is ToByteArray(isUnsigned: false, isBigEndian: true), the
// minimal big-endian two's-complement array
// (https://learn.microsoft.com/en-us/dotnet/api/system.numerics.biginteger.tobytearray?view=net-10.0).
public class NumberEncoder : MessageToByteEncoder<object>
{
    // CLR: the TypeParameterMatcher of MessageToByteEncoder<Number> accepts every Number.
    public override bool AcceptOutboundMessage(object msg)
    {
        return msg is BigInteger || msg is sbyte || msg is byte || msg is short || msg is ushort || msg is int ||
               msg is uint || msg is long || msg is ulong || msg is float || msg is double || msg is decimal;
    }

    protected override void Encode(IChannelHandlerContext ctx, object msg, ByteBuf @out)
    {
        // Convert to a BigInteger first for easier implementation.
        BigInteger v;
        if (msg is BigInteger)
        {
            v = (BigInteger) msg;
        }
        else
        {
            v = BigInteger.Parse(Convert.ToString(msg, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        // Convert the number into a byte array.
        byte[] data = v.ToByteArray(isUnsigned: false, isBigEndian: true);
        int dataLength = data.Length;

        // Write a message.
        @out.WriteByte((byte) 'F'); // magic number
        @out.WriteInt(dataLength);  // data length
        @out.WriteBytes(data);      // data
    }
}
