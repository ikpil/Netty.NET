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
using Netty.NET.Common.Collections;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: the anonymous Executables are lambdas and UnsupportedOperationException is NotSupportedException.
// Arrays.asList is Collectives.AsList, Collections.emptyList() is an empty array (List equality is element-wise in
// both), and Java byte is sbyte (see IValueConverter). assertEquals(double/float, ..., delta) is the tolerance
// overload of Assert.Equal.
public class EmptyHeadersTest
{

    private static readonly TestEmptyHeaders HEADERS = new TestEmptyHeaders();

    [Fact]
    public void TestAddStringValue()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.Add("name", "value"));
    }

    [Fact]
    public void TestAddStringValues()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.Add("name", "value1", "value2"));
    }

    [Fact]
    public void TestAddStringValuesIterable()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.Add("name", Collectives.AsList("value1", "value2")));
    }

    [Fact]
    public void TestAddBoolean()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.AddBoolean("name", true));
    }

    [Fact]
    public void TestAddByte()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.AddByte("name", (sbyte) 1));
    }

    [Fact]
    public void TestAddChar()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.AddChar("name", 'a'));
    }

    [Fact]
    public void TestAddDouble()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.AddDouble("name", 0));
    }

    [Fact]
    public void TestAddFloat()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.AddFloat("name", 0));
    }

    [Fact]
    public void TestAddInt()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.AddInt("name", 0));
    }

    [Fact]
    public void TestAddLong()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.AddLong("name", 0));
    }

    [Fact]
    public void TestAddShort()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.AddShort("name", (short) 0));
    }

    [Fact]
    public void TestAddTimeMillis()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.AddTimeMillis("name", 0));
    }

    [Fact]
    public void TestSetStringValue()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.Set("name", "value"));
    }

    [Fact]
    public void TestSetStringValues()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.Set("name", "value1", "value2"));
    }

    [Fact]
    public void TestSetStringValuesIterable()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.Set("name", Collectives.AsList("value1", "value2")));
    }

    [Fact]
    public void TestSetBoolean()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetBoolean("name", true));
    }

    [Fact]
    public void TestSetByte()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetByte("name", (sbyte) 1));
    }

    [Fact]
    public void TestSetChar()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetChar("name", 'a'));
    }

    [Fact]
    public void TestSetDouble()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetDouble("name", 0));
    }

    [Fact]
    public void TestSetFloat()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetFloat("name", 0));
    }

    [Fact]
    public void TestSetInt()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetInt("name", 0));
    }

    [Fact]
    public void TestSetLong()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetLong("name", 0));
    }

    [Fact]
    public void TestSetShort()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetShort("name", (short) 0));
    }

    [Fact]
    public void TestSetTimeMillis()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetTimeMillis("name", 0));
    }

    [Fact]
    public void TestSetAll()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.SetAll(new TestEmptyHeaders()));
    }

    [Fact]
    public void TestSet()
    {
        Assert.Throws<NotSupportedException>(() => HEADERS.Set(new TestEmptyHeaders()));
    }

    [Fact]
    public void TestGet()
    {
        Assert.Null(HEADERS.Get("name1"));
    }

    [Fact]
    public void TestGetDefault()
    {
        Assert.Equal("default", HEADERS.Get("name1", "default"));
    }

    [Fact]
    public void TestGetAndRemove()
    {
        Assert.Null(HEADERS.GetAndRemove("name1"));
    }

    [Fact]
    public void TestGetAndRemoveDefault()
    {
        Assert.Equal("default", HEADERS.GetAndRemove("name1", "default"));
    }

    [Fact]
    public void TestGetAll()
    {
        Assert.Equal(Array.Empty<string>(), HEADERS.GetAll("name1"));
    }

    [Fact]
    public void TestGetAllAndRemove()
    {
        Assert.Equal(Array.Empty<string>(), HEADERS.GetAllAndRemove("name1"));
    }

    [Fact]
    public void TestGetBoolean()
    {
        Assert.Null(HEADERS.GetBoolean("name1"));
    }

    [Fact]
    public void TestGetBooleanDefault()
    {
        Assert.True(HEADERS.GetBoolean("name1", true));
    }

    [Fact]
    public void TestGetBooleanAndRemove()
    {
        Assert.Null(HEADERS.GetBooleanAndRemove("name1"));
    }

    [Fact]
    public void TestGetBooleanAndRemoveDefault()
    {
        Assert.True(HEADERS.GetBooleanAndRemove("name1", true));
    }

    [Fact]
    public void TestGetByte()
    {
        Assert.Null(HEADERS.GetByte("name1"));
    }

    [Fact]
    public void TestGetByteDefault()
    {
        Assert.Equal((sbyte) 0, HEADERS.GetByte("name1", (sbyte) 0));
    }

    [Fact]
    public void TestGetByteAndRemove()
    {
        Assert.Null(HEADERS.GetByteAndRemove("name1"));
    }

    [Fact]
    public void TestGetByteAndRemoveDefault()
    {
        Assert.Equal((sbyte) 0, HEADERS.GetByteAndRemove("name1", (sbyte) 0));
    }

    [Fact]
    public void TestGetChar()
    {
        Assert.Null(HEADERS.GetChar("name1"));
    }

    [Fact]
    public void TestGetCharDefault()
    {
        Assert.Equal('x', HEADERS.GetChar("name1", 'x'));
    }

    [Fact]
    public void TestGetCharAndRemove()
    {
        Assert.Null(HEADERS.GetCharAndRemove("name1"));
    }

    [Fact]
    public void TestGetCharAndRemoveDefault()
    {
        Assert.Equal('x', HEADERS.GetCharAndRemove("name1", 'x'));
    }

    [Fact]
    public void TestGetDouble()
    {
        Assert.Null(HEADERS.GetDouble("name1"));
    }

    [Fact]
    public void TestGetDoubleDefault()
    {
        Assert.Equal(1, HEADERS.GetDouble("name1", 1), 0d);
    }

    [Fact]
    public void TestGetDoubleAndRemove()
    {
        Assert.Null(HEADERS.GetDoubleAndRemove("name1"));
    }

    [Fact]
    public void TestGetDoubleAndRemoveDefault()
    {
        Assert.Equal(1, HEADERS.GetDoubleAndRemove("name1", 1), 0d);
    }

    [Fact]
    public void TestGetFloat()
    {
        Assert.Null(HEADERS.GetFloat("name1"));
    }

    [Fact]
    public void TestGetFloatDefault()
    {
        Assert.Equal(1, HEADERS.GetFloat("name1", 1), 0f);
    }

    [Fact]
    public void TestGetFloatAndRemove()
    {
        Assert.Null(HEADERS.GetFloatAndRemove("name1"));
    }

    [Fact]
    public void TestGetFloatAndRemoveDefault()
    {
        Assert.Equal(1, HEADERS.GetFloatAndRemove("name1", 1), 0f);
    }

    [Fact]
    public void TestGetInt()
    {
        Assert.Null(HEADERS.GetInt("name1"));
    }

    [Fact]
    public void TestGetIntDefault()
    {
        Assert.Equal(1, HEADERS.GetInt("name1", 1));
    }

    [Fact]
    public void TestGetIntAndRemove()
    {
        Assert.Null(HEADERS.GetIntAndRemove("name1"));
    }

    [Fact]
    public void TestGetIntAndRemoveDefault()
    {
        Assert.Equal(1, HEADERS.GetIntAndRemove("name1", 1));
    }

    [Fact]
    public void TestGetLong()
    {
        Assert.Null(HEADERS.GetLong("name1"));
    }

    [Fact]
    public void TestGetLongDefault()
    {
        Assert.Equal(1, HEADERS.GetLong("name1", 1));
    }

    [Fact]
    public void TestGetLongAndRemove()
    {
        Assert.Null(HEADERS.GetLongAndRemove("name1"));
    }

    [Fact]
    public void TestGetLongAndRemoveDefault()
    {
        Assert.Equal(1, HEADERS.GetLongAndRemove("name1", 1));
    }

    [Fact]
    public void TestGetShort()
    {
        Assert.Null(HEADERS.GetShort("name1"));
    }

    [Fact]
    public void TestGetShortDefault()
    {
        Assert.Equal(1, HEADERS.GetShort("name1", (short) 1));
    }

    [Fact]
    public void TestGetShortAndRemove()
    {
        Assert.Null(HEADERS.GetShortAndRemove("name1"));
    }

    [Fact]
    public void TestGetShortAndRemoveDefault()
    {
        Assert.Equal(1, HEADERS.GetShortAndRemove("name1", (short) 1));
    }

    [Fact]
    public void TestGetTimeMillis()
    {
        Assert.Null(HEADERS.GetTimeMillis("name1"));
    }

    [Fact]
    public void TestGetTimeMillisDefault()
    {
        Assert.Equal(1, HEADERS.GetTimeMillis("name1", 1));
    }

    [Fact]
    public void TestGetTimeMillisAndRemove()
    {
        Assert.Null(HEADERS.GetTimeMillisAndRemove("name1"));
    }

    [Fact]
    public void TestGetTimeMillisAndRemoveDefault()
    {
        Assert.Equal(1, HEADERS.GetTimeMillisAndRemove("name1", 1));
    }

    [Fact]
    public void TestContains()
    {
        Assert.False(HEADERS.Contains("name1"));
    }

    [Fact]
    public void TestContainsWithValue()
    {
        Assert.False(HEADERS.Contains("name1", "value1"));
    }

    [Fact]
    public void TestContainsBoolean()
    {
        Assert.False(HEADERS.ContainsBoolean("name1", false));
    }

    [Fact]
    public void TestContainsByte()
    {
        Assert.False(HEADERS.ContainsByte("name1", (sbyte) 'x'));
    }

    [Fact]
    public void TestContainsChar()
    {
        Assert.False(HEADERS.ContainsChar("name1", 'x'));
    }

    [Fact]
    public void TestContainsDouble()
    {
        Assert.False(HEADERS.ContainsDouble("name1", 1));
    }

    [Fact]
    public void TestContainsFloat()
    {
        Assert.False(HEADERS.ContainsFloat("name1", 1));
    }

    [Fact]
    public void TestContainsInt()
    {
        Assert.False(HEADERS.ContainsInt("name1", 1));
    }

    [Fact]
    public void TestContainsLong()
    {
        Assert.False(HEADERS.ContainsLong("name1", 1));
    }

    [Fact]
    public void TestContainsShort()
    {
        Assert.False(HEADERS.ContainsShort("name1", (short) 1));
    }

    [Fact]
    public void TestContainsTimeMillis()
    {
        Assert.False(HEADERS.ContainsTimeMillis("name1", 1));
    }

    [Fact]
    public void TestContainsObject()
    {
        Assert.False(HEADERS.ContainsObject("name1", ""));
    }

    [Fact]
    public void TestIsEmpty()
    {
        Assert.True(HEADERS.IsEmpty());
    }

    [Fact]
    public void TestClear()
    {
        Assert.Same(HEADERS, HEADERS.Clear());
    }

    [Fact]
    public void TestSize()
    {
        Assert.Equal(0, HEADERS.Size());
    }

    [Fact]
    public void TestValueIterator()
    {
        Assert.False(HEADERS.ValueIterator("name1").HasNext());
    }
    private sealed class TestEmptyHeaders : EmptyHeaders<string, string, TestEmptyHeaders> { }
}
