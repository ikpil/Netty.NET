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

public static partial class Unpooled
{
    // CLR: disambiguates the two params CopyShort overloads for zero-input calls.
    public static ByteBuf CopyShort() => EmptyBuffer;

    /**
     * Creates a new 4-byte big-endian buffer that holds the specified 32-bit integer.
     */
    public static ByteBuf CopyInt(int value) => Buffer(4).WriteInt(value);

    /**
     * Create a big-endian buffer that holds a sequence of the specified 32-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyInt(params int[] values) => CopyInt(values.AsSpan());

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyInt(ReadOnlySpan<int> values)
    {
        if (values.IsEmpty) return EmptyBuffer;
        ByteBuf buffer = Buffer(checked(values.Length * 4));
        foreach (int value in values) buffer.WriteInt(value);
        return buffer;
    }

    /**
     * Creates a new 2-byte big-endian buffer that holds the specified 16-bit integer.
     */
    public static ByteBuf CopyShort(int value) => Buffer(2).WriteShort(value);

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 16-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyShort(params short[] values) => CopyShort(values.AsSpan());

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyShort(ReadOnlySpan<short> values)
    {
        if (values.IsEmpty) return EmptyBuffer;
        ByteBuf buffer = Buffer(checked(values.Length * 2));
        foreach (short value in values) buffer.WriteShort(value);
        return buffer;
    }

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 16-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyShort(params int[] values) => CopyShort(values.AsSpan());

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyShort(ReadOnlySpan<int> values)
    {
        if (values.IsEmpty) return EmptyBuffer;
        ByteBuf buffer = Buffer(checked(values.Length * 2));
        foreach (int value in values) buffer.WriteShort(value);
        return buffer;
    }

    /**
     * Creates a new 3-byte big-endian buffer that holds the specified 24-bit integer.
     */
    public static ByteBuf CopyMedium(int value) => Buffer(3).WriteMedium(value);

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 24-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyMedium(params int[] values) => CopyMedium(values.AsSpan());

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyMedium(ReadOnlySpan<int> values)
    {
        if (values.IsEmpty) return EmptyBuffer;
        ByteBuf buffer = Buffer(checked(values.Length * 3));
        foreach (int value in values) buffer.WriteMedium(value);
        return buffer;
    }

    /**
     * Creates a new 8-byte big-endian buffer that holds the specified 64-bit integer.
     */
    public static ByteBuf CopyLong(long value) => Buffer(8).WriteLong(value);

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 64-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyLong(params long[] values) => CopyLong(values.AsSpan());

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyLong(ReadOnlySpan<long> values)
    {
        if (values.IsEmpty) return EmptyBuffer;
        ByteBuf buffer = Buffer(checked(values.Length * 8));
        foreach (long value in values) buffer.WriteLong(value);
        return buffer;
    }

    /**
     * Creates a new single-byte big-endian buffer that holds the specified boolean value.
     */
    public static ByteBuf CopyBoolean(bool value) => Buffer(1).WriteBoolean(value);

    /**
     * Create a new big-endian buffer that holds a sequence of the specified boolean values.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyBoolean(params bool[] values) => CopyBoolean(values.AsSpan());

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyBoolean(ReadOnlySpan<bool> values)
    {
        if (values.IsEmpty) return EmptyBuffer;
        ByteBuf buffer = Buffer(checked(values.Length * 1));
        foreach (bool value in values) buffer.WriteBoolean(value);
        return buffer;
    }

    /**
     * Creates a new 4-byte big-endian buffer that holds the specified 32-bit floating point number.
     */
    public static ByteBuf CopyFloat(float value) => Buffer(4).WriteFloat(value);

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 32-bit floating point numbers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyFloat(params float[] values) => CopyFloat(values.AsSpan());

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyFloat(ReadOnlySpan<float> values)
    {
        if (values.IsEmpty) return EmptyBuffer;
        ByteBuf buffer = Buffer(checked(values.Length * 4));
        foreach (float value in values) buffer.WriteFloat(value);
        return buffer;
    }

    /**
     * Creates a new 8-byte big-endian buffer that holds the specified 64-bit floating point number.
     */
    public static ByteBuf CopyDouble(double value) => Buffer(8).WriteDouble(value);

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 64-bit floating point numbers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyDouble(params double[] values) => CopyDouble(values.AsSpan());

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyDouble(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty) return EmptyBuffer;
        ByteBuf buffer = Buffer(checked(values.Length * 8));
        foreach (double value in values) buffer.WriteDouble(value);
        return buffer;
    }
}
