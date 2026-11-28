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
using System.Diagnostics;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec;

/**
 * An encoder that prepends the length of the message.  The length value is
 * prepended as a binary form.
 * <p>
 * For example, <tt>{@link LengthFieldPrepender}(2)</tt> will encode the
 * following 12-bytes string:
 * <pre>
 * +----------------+
 * | "HELLO, WORLD" |
 * +----------------+
 * </pre>
 * into the following:
 * <pre>
 * +--------+----------------+
 * + 0x000C | "HELLO, WORLD" |
 * +--------+----------------+
 * </pre>
 * If you turned on the {@code lengthIncludesLengthFieldLength} flag in the
 * constructor, the encoded data would look like the following
 * (12 (original data) + 2 (prepended data) = 14 (0xE)):
 * <pre>
 * +--------+----------------+
 * + 0x000E | "HELLO, WORLD" |
 * +--------+----------------+
 * </pre>
 */
// CLR: List<Object> out is IList<object> @out (a C# keyword); java.nio.ByteOrder is the Common port's
// Netty.NET.Common.Internal.ByteOrder. IllegalArgumentException is ArgumentException, and numbers in messages use the
// invariant culture (Int32.ToString() otherwise uses "the NumberFormatInfo object of the current culture",
// https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
[Sharable]
public class LengthFieldPrepender : MessageToMessageEncoder<ByteBuf>
{
    private readonly ByteOrder byteOrder;
    private readonly int lengthFieldLength;
    private readonly bool lengthIncludesLengthFieldLength;
    private readonly int lengthAdjustment;

    /**
     * Creates a new instance.
     *
     * @param lengthFieldLength the length of the prepended length field.
     *                          Only 1, 2, 3, 4, and 8 are allowed.
     *
     * @throws IllegalArgumentException
     *         if {@code lengthFieldLength} is not 1, 2, 3, 4, or 8
     */
    public LengthFieldPrepender(int lengthFieldLength) : this(lengthFieldLength, false)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param lengthFieldLength the length of the prepended length field.
     *                          Only 1, 2, 3, 4, and 8 are allowed.
     * @param lengthIncludesLengthFieldLength
     *                          if {@code true}, the length of the prepended
     *                          length field is added to the value of the
     *                          prepended length field.
     *
     * @throws IllegalArgumentException
     *         if {@code lengthFieldLength} is not 1, 2, 3, 4, or 8
     */
    public LengthFieldPrepender(int lengthFieldLength, bool lengthIncludesLengthFieldLength)
        : this(lengthFieldLength, 0, lengthIncludesLengthFieldLength)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param lengthFieldLength the length of the prepended length field.
     *                          Only 1, 2, 3, 4, and 8 are allowed.
     * @param lengthAdjustment  the compensation value to add to the value
     *                          of the length field
     *
     * @throws IllegalArgumentException
     *         if {@code lengthFieldLength} is not 1, 2, 3, 4, or 8
     */
    public LengthFieldPrepender(int lengthFieldLength, int lengthAdjustment)
        : this(lengthFieldLength, lengthAdjustment, false)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param lengthFieldLength the length of the prepended length field.
     *                          Only 1, 2, 3, 4, and 8 are allowed.
     * @param lengthAdjustment  the compensation value to add to the value
     *                          of the length field
     * @param lengthIncludesLengthFieldLength
     *                          if {@code true}, the length of the prepended
     *                          length field is added to the value of the
     *                          prepended length field.
     *
     * @throws IllegalArgumentException
     *         if {@code lengthFieldLength} is not 1, 2, 3, 4, or 8
     */
    public LengthFieldPrepender(int lengthFieldLength, int lengthAdjustment, bool lengthIncludesLengthFieldLength)
        : this(ByteOrder.BIG_ENDIAN, lengthFieldLength, lengthAdjustment, lengthIncludesLengthFieldLength)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param byteOrder         the {@link ByteOrder} of the length field
     * @param lengthFieldLength the length of the prepended length field.
     *                          Only 1, 2, 3, 4, and 8 are allowed.
     * @param lengthAdjustment  the compensation value to add to the value
     *                          of the length field
     * @param lengthIncludesLengthFieldLength
     *                          if {@code true}, the length of the prepended
     *                          length field is added to the value of the
     *                          prepended length field.
     *
     * @throws IllegalArgumentException
     *         if {@code lengthFieldLength} is not 1, 2, 3, 4, or 8
     */
    public LengthFieldPrepender(
            ByteOrder byteOrder, int lengthFieldLength,
            int lengthAdjustment, bool lengthIncludesLengthFieldLength)
        : base(typeof(ByteBuf))
    {
        if (lengthFieldLength != 1 && lengthFieldLength != 2 &&
            lengthFieldLength != 3 && lengthFieldLength != 4 &&
            lengthFieldLength != 8)
        {
            throw new ArgumentException(
                    "lengthFieldLength must be either 1, 2, 3, 4, or 8: " +
                    lengthFieldLength.ToString(CultureInfo.InvariantCulture));
        }

        this.byteOrder = ObjectUtil.CheckNotNull(byteOrder, "byteOrder");
        this.lengthFieldLength = lengthFieldLength;
        this.lengthIncludesLengthFieldLength = lengthIncludesLengthFieldLength;
        this.lengthAdjustment = lengthAdjustment;
    }

    // CLR: the Buffer port has no order(ByteOrder) view: ByteBuf.order is @deprecated "use the Little Endian accessors,
    // e.g. getShortLE, getIntLE instead of creating a buffer with swapped endianness", and the port keeps explicit
    // BE/LE accessors (docs/buffer-porting.md, Explicit endian replacements for swapped buffers). The byte order picks
    // the BE or LE write, and 'out' gets the allocated buffer itself instead of its swapped view (a single byte has no
    // order). The short cast is unchecked, as Java's narrowing (length < 65536 may exceed short.MaxValue). The default
    // branch throws UnreachableException for java.lang.Error: "thrown when the program executes an instruction that
    // was thought to be unreachable" (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception).
    protected override void Encode(IChannelHandlerContext ctx, ByteBuf msg, IList<object> @out)
    {
        int length = msg.ReadableBytes + lengthAdjustment;

        if (lengthIncludesLengthFieldLength)
        {
            length += lengthFieldLength;
        }

        CheckPositiveOrZero(length, "length");

        bool littleEndian = byteOrder == ByteOrder.LITTLE_ENDIAN;

        switch (lengthFieldLength)
        {
        case 1:
            if (length >= 256)
            {
                throw new ArgumentException(
                        "length does not fit into a byte: " + length.ToString(CultureInfo.InvariantCulture));
            }

            @out.Add(ctx.Alloc().Buffer(1).WriteByte((byte) length));
            break;
        case 2:
            if (length >= 65536)
            {
                throw new ArgumentException(
                        "length does not fit into a short integer: " + length.ToString(CultureInfo.InvariantCulture));
            }

            @out.Add(littleEndian ? ctx.Alloc().Buffer(2).WriteShortLE(unchecked((short) length))
                    : ctx.Alloc().Buffer(2).WriteShort(unchecked((short) length)));
            break;
        case 3:
            if (length >= 16777216)
            {
                throw new ArgumentException(
                        "length does not fit into a medium integer: " + length.ToString(CultureInfo.InvariantCulture));
            }

            @out.Add(littleEndian ? ctx.Alloc().Buffer(3).WriteMediumLE(length)
                    : ctx.Alloc().Buffer(3).WriteMedium(length));
            break;
        case 4:
            @out.Add(littleEndian ? ctx.Alloc().Buffer(4).WriteIntLE(length)
                    : ctx.Alloc().Buffer(4).WriteInt(length));
            break;
        case 8:
            @out.Add(littleEndian ? ctx.Alloc().Buffer(8).WriteLongLE(length)
                    : ctx.Alloc().Buffer(8).WriteLong(length));
            break;
        default:
            throw new UnreachableException(
                    "Unexpected length field length: " + lengthFieldLength.ToString(CultureInfo.InvariantCulture));
        }

        @out.Add(msg.Retain());
    }
}
