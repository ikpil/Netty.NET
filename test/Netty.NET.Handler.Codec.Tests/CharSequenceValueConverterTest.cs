/*
 * Copyright 2018 The Netty Project
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

using System;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: AsciiString.of takes an ICharSequence, so the Java String literal is wrapped as a StringCharSequence. Java
// byte is sbyte (see IValueConverter) and NumberFormatException is FormatException.
public class CharSequenceValueConverterTest
{

    private readonly CharSequenceValueConverter converter = CharSequenceValueConverter.INSTANCE;

    [Fact]
    public void TestBoolean()
    {
        Assert.True(converter.ConvertToBoolean(converter.ConvertBoolean(true)));
        Assert.False(converter.ConvertToBoolean(converter.ConvertBoolean(false)));
    }

    [Fact]
    public void TestByteFromAsciiString()
    {
        Assert.Equal(127, converter.ConvertToByte(AsciiString.Of(new StringCharSequence("127"))));
    }

    [Fact]
    public void TestByteFromEmptyAsciiString()
    {
        Assert.Throws<FormatException>(() => converter.ConvertToByte(AsciiString.EMPTY_STRING));
    }

    [Fact]
    public void TestByte()
    {
        Assert.Equal(sbyte.MaxValue, converter.ConvertToByte(converter.ConvertByte(sbyte.MaxValue)));
    }

    [Fact]
    public void TestChar()
    {
        Assert.Equal(char.MaxValue, converter.ConvertToChar(converter.ConvertChar(char.MaxValue)));
    }

    [Fact]
    public void TestDouble()
    {
        Assert.Equal(double.MaxValue, converter.ConvertToDouble(converter.ConvertDouble(double.MaxValue)), 0d);
    }

    [Fact]
    public void TestFloat()
    {
        Assert.Equal(float.MaxValue, converter.ConvertToFloat(converter.ConvertFloat(float.MaxValue)), 0f);
    }

    [Fact]
    public void TestInt()
    {
        Assert.Equal(int.MaxValue, converter.ConvertToInt(converter.ConvertInt(int.MaxValue)));
    }

    [Fact]
    public void TestShort()
    {
        Assert.Equal(short.MaxValue, converter.ConvertToShort(converter.ConvertShort(short.MaxValue)));
    }

    [Fact]
    public void TestLong()
    {
        Assert.Equal(long.MaxValue, converter.ConvertToLong(converter.ConvertLong(long.MaxValue)));
    }

    [Fact]
    public void TestTimeMillis()
    {
        // Zero out the millis as this is what the convert is doing as well.
        // CLR: System.currentTimeMillis() is DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().
        long millis = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000;
        Assert.Equal(millis, converter.ConvertToTimeMillis(converter.ConvertTimeMillis(millis)));
    }
}
