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

namespace Netty.NET.Buffer;

public partial class CompositeByteBuf
{
    // CompositeByteBuf._get*/_set* dispatch through the selected component's public
    // primitive operations. Direct memory access would skip observable overrides.
    internal override byte ReadByteCore(int index)
    { CheckIndex(index, 1); return (byte)ReadComponentWord(index, 1, false); }
    internal override void WriteByteCore(int index, int value)
    { EnsureCanWrite(); CheckIndex(index, 1); WriteComponentWord(index, unchecked((ulong)value), 1, false); }
    internal override ulong ReadWord(int index, int width, bool littleEndian)
    { CheckIndex(index, width); return ReadComponentWord(index, width, littleEndian); }
    internal override ByteBuf SetWord(int index, ulong value, int width, bool littleEndian)
    { EnsureCanWrite(); CheckIndex(index, width); WriteComponentWord(index, value, width, littleEndian); return this; }

    private ulong ReadComponentWord(int index, int width, bool littleEndian)
    {
        ComponentEntry component = _components[FindComponentIndex(index)];
        if (width <= component.EndOffset - index)
        {
            int local = component.SourceIndex + (index - component.Offset);
            ByteBuf source = component.Source;
            return width switch
            {
                1 => source.GetByte(local),
                2 => unchecked((ushort)(littleEndian ? source.GetShortLE(local) : source.GetShort(local))),
                3 => unchecked((uint)(littleEndian ? source.GetUnsignedMediumLE(local) : source.GetUnsignedMedium(local))),
                4 => unchecked((uint)(littleEndian ? source.GetIntLE(local) : source.GetInt(local))),
                8 => unchecked((ulong)(littleEndian ? source.GetLongLE(local) : source.GetLong(local))),
                _ => throw new ArgumentOutOfRangeException(nameof(width))
            };
        }
        int firstWidth = width == 3 ? 2 : width / 2;
        ulong first = ReadComponentWord(index, firstWidth, littleEndian);
        ulong second = ReadComponentWord(index + firstWidth, width - firstWidth, littleEndian);
        return littleEndian ? first | second << (firstWidth * 8) : first << ((width - firstWidth) * 8) | second;
    }

    private void WriteComponentWord(int index, ulong value, int width, bool littleEndian)
    {
        ComponentEntry component = _components[FindComponentIndex(index)];
        if (width <= component.EndOffset - index)
        {
            int local = component.SourceIndex + (index - component.Offset);
            ByteBuf source = component.Source;
            switch (width)
            {
                case 1: source.SetByte(local, unchecked((int)value)); break;
                case 2:
                    if (littleEndian) source.SetShortLE(local, unchecked((int)value));
                    else source.SetShort(local, unchecked((int)value));
                    break;
                case 3:
                    if (littleEndian) source.SetMediumLE(local, unchecked((int)value));
                    else source.SetMedium(local, unchecked((int)value));
                    break;
                case 4:
                    if (littleEndian) source.SetIntLE(local, unchecked((int)value));
                    else source.SetInt(local, unchecked((int)value));
                    break;
                case 8:
                    if (littleEndian) source.SetLongLE(local, unchecked((long)value));
                    else source.SetLong(local, unchecked((long)value));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(width));
            }
            return;
        }
        int firstWidth = width == 3 ? 2 : width / 2, secondWidth = width - firstWidth;
        // Match the original byte/short/int casts before each recursive setter call.
        WriteComponentWord(index, NarrowPart(littleEndian ? value : value >> (secondWidth * 8), firstWidth), firstWidth, littleEndian);
        WriteComponentWord(index + firstWidth, NarrowPart(littleEndian ? value >> (firstWidth * 8) : value, secondWidth), secondWidth, littleEndian);
    }

    private static ulong NarrowPart(ulong value, int width) => width switch
    {
        1 => unchecked((ulong)(sbyte)value),
        2 => unchecked((ulong)(short)value),
        4 => unchecked((ulong)(int)value),
        _ => throw new ArgumentOutOfRangeException(nameof(width))
    };
}
