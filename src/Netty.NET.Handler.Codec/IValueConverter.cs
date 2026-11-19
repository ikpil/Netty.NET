/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

namespace Netty.NET.Handler.Codec;

/**
 * Converts to/from a generic object to the type.
 */
// CLR: Java byte is a signed 8-bit integer, so it is sbyte here; header bytes are decimal text ("-128" to "127")
// rather than the octets ByteBuf exposes as System.Byte. The boxed results of Headers.getByte() and friends are the
// corresponding nullable value types. Java long millisecond values stay long.
public interface IValueConverter<T>
{
    T ConvertObject(object value);

    T ConvertBoolean(bool value);

    bool ConvertToBoolean(T value);

    T ConvertByte(sbyte value);

    sbyte ConvertToByte(T value);

    T ConvertChar(char value);

    char ConvertToChar(T value);

    T ConvertShort(short value);

    short ConvertToShort(T value);

    T ConvertInt(int value);

    int ConvertToInt(T value);

    T ConvertLong(long value);

    long ConvertToLong(T value);

    T ConvertTimeMillis(long value);

    long ConvertToTimeMillis(T value);

    T ConvertFloat(float value);

    float ConvertToFloat(T value);

    T ConvertDouble(double value);

    double ConvertToDouble(T value);
}
