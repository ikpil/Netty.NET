/*
 * Copyright 2015 The Netty Project
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

namespace Netty.NET.Handler.Codec;

/**
 * {@link UnsupportedOperationException} will be thrown from all {@link ValueConverter} methods.
 */
// CLR: UnsupportedOperationException is NotSupportedException.
public sealed class UnsupportedValueConverter<V> : IValueConverter<V>
{
    // CLR: the raw-typed INSTANCE and the unchecked cast in instance() become one instance per closed generic type;
    // the converter has no state, so every instance behaves the same.
    private static readonly UnsupportedValueConverter<V> INSTANCE = new UnsupportedValueConverter<V>();
    private UnsupportedValueConverter()
    {
    }

    public static UnsupportedValueConverter<V> Instance()
    {
        return INSTANCE;
    }

    public V ConvertObject(object value)
    {
        throw new NotSupportedException();
    }

    public V ConvertBoolean(bool value)
    {
        throw new NotSupportedException();
    }

    public bool ConvertToBoolean(V value)
    {
        throw new NotSupportedException();
    }

    public V ConvertByte(sbyte value)
    {
        throw new NotSupportedException();
    }

    public sbyte ConvertToByte(V value)
    {
        throw new NotSupportedException();
    }

    public V ConvertChar(char value)
    {
        throw new NotSupportedException();
    }

    public char ConvertToChar(V value)
    {
        throw new NotSupportedException();
    }

    public V ConvertShort(short value)
    {
        throw new NotSupportedException();
    }

    public short ConvertToShort(V value)
    {
        throw new NotSupportedException();
    }

    public V ConvertInt(int value)
    {
        throw new NotSupportedException();
    }

    public int ConvertToInt(V value)
    {
        throw new NotSupportedException();
    }

    public V ConvertLong(long value)
    {
        throw new NotSupportedException();
    }

    public long ConvertToLong(V value)
    {
        throw new NotSupportedException();
    }

    public V ConvertTimeMillis(long value)
    {
        throw new NotSupportedException();
    }

    public long ConvertToTimeMillis(V value)
    {
        throw new NotSupportedException();
    }

    public V ConvertFloat(float value)
    {
        throw new NotSupportedException();
    }

    public float ConvertToFloat(V value)
    {
        throw new NotSupportedException();
    }

    public V ConvertDouble(double value)
    {
        throw new NotSupportedException();
    }

    public double ConvertToDouble(V value)
    {
        throw new NotSupportedException();
    }
}
