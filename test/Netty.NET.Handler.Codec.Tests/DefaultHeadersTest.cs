/*
 * Copyright 2014 The Netty Project
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
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Tests;

#pragma warning disable xUnit2013 // assertEquals(n, size()) keeps the source assertions.
#pragma warning disable xUnit2017 // assertTrue(values.contains(x)) keeps the source assertions.

/**
 * Tests for {@link DefaultHeaders}.
 */
// CLR: a Java String is a CharSequence; the CLR string literals passed as header names and values are wrapped as
// StringCharSequence. Iterator.next() past the end (NoSuchElementException) and remove() before next()
// (IllegalStateException) throw InvalidOperationException (see IIterator), NullPointerException from checkNotNull is
// ArgumentNullException, and the anonymous Executables are lambdas. Java byte is sbyte; Double.MIN_VALUE and
// Float.MIN_VALUE, "the smallest positive nonzero value"
// (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Double.html#MIN_VALUE), are
// double.Epsilon and float.Epsilon ("the smallest positive Double value that is greater than zero",
// https://learn.microsoft.com/dotnet/api/system.double.epsilon), not double.MinValue.
public class DefaultHeadersTest
{

    private sealed class TestDefaultHeaders :
            DefaultHeaders<ICharSequence, ICharSequence, TestDefaultHeaders>
    {
        internal TestDefaultHeaders() : this(CharSequenceValueConverter.INSTANCE)
        {
        }

        internal TestDefaultHeaders(IValueConverter<ICharSequence> converter) : base(converter)
        {
        }

        internal TestDefaultHeaders(IEqualityComparer<ICharSequence> nameHashingStrategy)
            : base(nameHashingStrategy, CharSequenceValueConverter.INSTANCE)
        {
        }
    }

    private static TestDefaultHeaders NewInstance()
    {
        return new TestDefaultHeaders();
    }

    // CLR: the statically imported AsciiString.of(CharSequence) takes the String literal as a CharSequence; the CLR
    // string is wrapped as a StringCharSequence for it.
    private static AsciiString Of(string str)
    {
        return AsciiString.Of(new StringCharSequence(str));
    }

    [Fact]
    public void AddShouldIncreaseAndRemoveShouldDecreaseTheSize()
    {
        TestDefaultHeaders headers = NewInstance();
        Assert.Equal(0, headers.Size());
        headers.Add(Of("name1"), Of("value1"), Of("value2"));
        Assert.Equal(2, headers.Size());
        headers.Add(Of("name2"), Of("value3"), Of("value4"));
        Assert.Equal(4, headers.Size());
        headers.Add(Of("name3"), Of("value5"));
        Assert.Equal(5, headers.Size());

        headers.Remove(Of("name3"));
        Assert.Equal(4, headers.Size());
        headers.Remove(Of("name1"));
        Assert.Equal(2, headers.Size());
        headers.Remove(Of("name2"));
        Assert.Equal(0, headers.Size());
        Assert.True(headers.IsEmpty());
    }

    [Fact]
    public void AfterClearHeadersShouldBeEmpty()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"));
        headers.Add(Of("name2"), Of("value2"));
        Assert.Equal(2, headers.Size());
        headers.Clear();
        Assert.Equal(0, headers.Size());
        Assert.True(headers.IsEmpty());
        Assert.False(headers.Contains(Of("name1")));
        Assert.False(headers.Contains(Of("name2")));
    }

    [Fact]
    public void RemovingANameForASecondTimeShouldReturnFalse()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"));
        headers.Add(Of("name2"), Of("value2"));
        Assert.True(headers.Remove(Of("name2")));
        Assert.False(headers.Remove(Of("name2")));
    }

    // CLR: Collection.containsAll(c) is c.All(values.Contains).
    [Fact]
    public void MultipleValuesPerNameShouldBeAllowed()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name"), Of("value1"));
        headers.Add(Of("name"), Of("value2"));
        headers.Add(Of("name"), Of("value3"));
        Assert.Equal(3, headers.Size());

        IList<ICharSequence> values = headers.GetAll(Of("name"));
        Assert.Equal(3, values.Count);
        Assert.True(Collectives.AsList(Of("value1"), Of("value2"), Of("value3")).All(values.Contains));
    }

    [Fact]
    public void MultipleValuesPerNameIteratorWithOtherNames()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"));
        headers.Add(Of("name1"), Of("value2"));
        headers.Add(Of("name2"), Of("value4"));
        headers.Add(Of("name1"), Of("value3"));
        Assert.Equal(4, headers.Size());

        List<ICharSequence> values = new List<ICharSequence>();
        IIterator<ICharSequence> itr = headers.ValueIterator(Of("name1"));

        while (itr.HasNext())
        {
            values.Add(itr.Next());
            itr.Remove();
        }

        Assert.Equal(3, values.Count);
        Assert.Equal(1, headers.Size());
        Assert.False(headers.IsEmpty());
        Assert.True(Collectives.AsList(Of("value1"), Of("value2"), Of("value3")).All(values.Contains));
        itr = headers.ValueIterator(Of("name1"));
        Assert.False(itr.HasNext());
        itr = headers.ValueIterator(Of("name2"));
        Assert.True(itr.HasNext());
        Assert.Equal(Of("value4"), itr.Next());
        Assert.False(itr.HasNext());
    }

    [Fact]
    public void MultipleValuesPerNameIterator()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"));
        headers.Add(Of("name1"), Of("value2"));
        Assert.Equal(2, headers.Size());

        List<ICharSequence> values = new List<ICharSequence>();
        IIterator<ICharSequence> itr = headers.ValueIterator(Of("name1"));

        while (itr.HasNext())
        {
            values.Add(itr.Next());
            itr.Remove();
        }

        Assert.Equal(2, values.Count);
        Assert.Equal(0, headers.Size());
        Assert.True(headers.IsEmpty());
        Assert.True(Collectives.AsList(Of("value1"), Of("value2")).All(values.Contains));
        itr = headers.ValueIterator(Of("name1"));
        Assert.False(itr.HasNext());
    }

    [Fact]
    public void ValuesItrRemoveThrowsWhenEmpty()
    {
        TestDefaultHeaders headers = NewInstance();
        Assert.Equal(0, headers.Size());
        Assert.True(headers.IsEmpty());
        IIterator<ICharSequence> itr = headers.ValueIterator(Of("name"));
        Assert.Throws<InvalidOperationException>(() => itr.Remove());
    }

    [Fact]
    public void ValuesItrRemoveThrowsAfterLastElement()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name"), Of("value1"));
        Assert.Equal(1, headers.Size());

        List<ICharSequence> values = new List<ICharSequence>();
        IIterator<ICharSequence> itr = headers.ValueIterator(Of("name"));

        while (itr.HasNext())
        {
            values.Add(itr.Next());
            itr.Remove();
        }

        Assert.Equal(1, values.Count);
        Assert.Equal(0, headers.Size());
        Assert.True(headers.IsEmpty());
        Assert.True(values.Contains(Of("value1")));
        Assert.Throws<InvalidOperationException>(() => itr.Remove());
    }

    [Fact]
    public void MultipleValuesPerNameIteratorEmpty()
    {
        TestDefaultHeaders headers = NewInstance();

        List<ICharSequence> values = new List<ICharSequence>();
        IIterator<ICharSequence> itr = headers.ValueIterator(Of("name"));

        while (itr.HasNext())
        {
            values.Add(itr.Next());
        }

        Assert.Equal(0, values.Count);
        Assert.Throws<InvalidOperationException>(() => itr.Next());
    }

    // CLR: System.currentTimeMillis() is DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().
    [Fact]
    public void TestContains()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.AddBoolean(Of("boolean"), true);
        Assert.True(headers.ContainsBoolean(Of("boolean"), true));
        Assert.False(headers.ContainsBoolean(Of("boolean"), false));

        headers.AddLong(Of("long"), long.MaxValue);
        Assert.True(headers.ContainsLong(Of("long"), long.MaxValue));
        Assert.False(headers.ContainsLong(Of("long"), long.MinValue));

        headers.AddInt(Of("int"), int.MinValue);
        Assert.True(headers.ContainsInt(Of("int"), int.MinValue));
        Assert.False(headers.ContainsInt(Of("int"), int.MaxValue));

        headers.AddShort(Of("short"), short.MaxValue);
        Assert.True(headers.ContainsShort(Of("short"), short.MaxValue));
        Assert.False(headers.ContainsShort(Of("short"), short.MinValue));

        headers.AddChar(Of("char"), char.MaxValue);
        Assert.True(headers.ContainsChar(Of("char"), char.MaxValue));
        Assert.False(headers.ContainsChar(Of("char"), char.MinValue));

        headers.AddByte(Of("byte"), sbyte.MaxValue);
        Assert.True(headers.ContainsByte(Of("byte"), sbyte.MaxValue));
        Assert.False(headers.ContainsLong(Of("byte"), sbyte.MinValue));

        headers.AddDouble(Of("double"), double.MaxValue);
        Assert.True(headers.ContainsDouble(Of("double"), double.MaxValue));
        Assert.False(headers.ContainsDouble(Of("double"), double.Epsilon));

        headers.AddFloat(Of("float"), float.MaxValue);
        Assert.True(headers.ContainsFloat(Of("float"), float.MaxValue));
        Assert.False(headers.ContainsFloat(Of("float"), float.Epsilon));

        long millis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        headers.AddTimeMillis(Of("millis"), millis);
        Assert.True(headers.ContainsTimeMillis(Of("millis"), millis));
        // This test doesn't work on midnight, January 1, 1970 UTC
        Assert.False(headers.ContainsTimeMillis(Of("millis"), 0));

        headers.AddObject(Of("object"), "Hello World");
        Assert.True(headers.ContainsObject(Of("object"), "Hello World"));
        Assert.False(headers.ContainsObject(Of("object"), ""));

        headers.Add(Of("name"), Of("value"));
        Assert.True(headers.Contains(Of("name"), Of("value")));
        Assert.False(headers.Contains(Of("name"), Of("value1")));
    }

    [Fact]
    public void TestCopy()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.AddBoolean(Of("boolean"), true);
        headers.AddLong(Of("long"), long.MaxValue);
        headers.AddInt(Of("int"), int.MinValue);
        headers.AddShort(Of("short"), short.MaxValue);
        headers.AddChar(Of("char"), char.MaxValue);
        headers.AddByte(Of("byte"), sbyte.MaxValue);
        headers.AddDouble(Of("double"), double.MaxValue);
        headers.AddFloat(Of("float"), float.MaxValue);
        long millis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        headers.AddTimeMillis(Of("millis"), millis);
        headers.AddObject(Of("object"), "Hello World");
        headers.Add(Of("name"), Of("value"));

        headers = NewInstance().Add(headers);

        Assert.True(headers.ContainsBoolean(Of("boolean"), true));
        Assert.False(headers.ContainsBoolean(Of("boolean"), false));

        Assert.True(headers.ContainsLong(Of("long"), long.MaxValue));
        Assert.False(headers.ContainsLong(Of("long"), long.MinValue));

        Assert.True(headers.ContainsInt(Of("int"), int.MinValue));
        Assert.False(headers.ContainsInt(Of("int"), int.MaxValue));

        Assert.True(headers.ContainsShort(Of("short"), short.MaxValue));
        Assert.False(headers.ContainsShort(Of("short"), short.MinValue));

        Assert.True(headers.ContainsChar(Of("char"), char.MaxValue));
        Assert.False(headers.ContainsChar(Of("char"), char.MinValue));

        Assert.True(headers.ContainsByte(Of("byte"), sbyte.MaxValue));
        Assert.False(headers.ContainsLong(Of("byte"), sbyte.MinValue));

        Assert.True(headers.ContainsDouble(Of("double"), double.MaxValue));
        Assert.False(headers.ContainsDouble(Of("double"), double.Epsilon));

        Assert.True(headers.ContainsFloat(Of("float"), float.MaxValue));
        Assert.False(headers.ContainsFloat(Of("float"), float.Epsilon));

        Assert.True(headers.ContainsTimeMillis(Of("millis"), millis));
        // This test doesn't work on midnight, January 1, 1970 UTC
        Assert.False(headers.ContainsTimeMillis(Of("millis"), 0));

        Assert.True(headers.ContainsObject(Of("object"), "Hello World"));
        Assert.False(headers.ContainsObject(Of("object"), ""));

        Assert.True(headers.Contains(Of("name"), Of("value")));
        Assert.False(headers.Contains(Of("name"), Of("value1")));
    }

    [Fact]
    public void CanMixConvertedAndNormalValues()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name"), Of("value"));
        headers.AddInt(Of("name"), 100);
        headers.AddBoolean(Of("name"), false);

        Assert.Equal(3, headers.Size());
        Assert.True(headers.Contains(Of("name")));
        Assert.True(headers.Contains(Of("name"), Of("value")));
        Assert.True(headers.ContainsInt(Of("name"), 100));
        Assert.True(headers.ContainsBoolean(Of("name"), false));
    }

    [Fact]
    public void TestGetAndRemove()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"));
        headers.Add(Of("name2"), Of("value2"), Of("value3"));
        headers.Add(Of("name3"), Of("value4"), Of("value5"), Of("value6"));

        Assert.Equal(Of("value1"), headers.GetAndRemove(Of("name1"), Of("defaultvalue")));
        Assert.Equal(Of("value2"), headers.GetAndRemove(Of("name2")));
        Assert.Null(headers.GetAndRemove(Of("name2")));
        Assert.Equal(Collectives.AsList<ICharSequence>(Of("value4"), Of("value5"), Of("value6")),
                headers.GetAllAndRemove(Of("name3")));
        Assert.Equal(0, headers.Size());
        Assert.Null(headers.GetAndRemove(Of("noname")));
        Assert.Equal(Of("defaultvalue"), headers.GetAndRemove(Of("noname"), Of("defaultvalue")));
    }

    [Fact]
    public void WhenNameContainsMultipleValuesGetShouldReturnTheFirst()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"), Of("value2"));
        Assert.Equal(Of("value1"), headers.Get(Of("name1")));
    }

    [Fact]
    public void GetWithDefaultValueWorks()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"));

        Assert.Equal(Of("value1"), headers.Get(Of("name1"), Of("defaultvalue")));
        Assert.Equal(Of("defaultvalue"), headers.Get(Of("noname"), Of("defaultvalue")));
    }

    [Fact]
    public void SetShouldOverWritePreviousValue()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Set(Of("name"), Of("value1"));
        headers.Set(Of("name"), Of("value2"));
        Assert.Equal(1, headers.Size());
        Assert.Equal(1, headers.GetAll(Of("name")).Count);
        Assert.Equal(Of("value2"), headers.GetAll(Of("name"))[0]);
        Assert.Equal(Of("value2"), headers.Get(Of("name")));
    }

    [Fact]
    public void SetAllShouldOverwriteSomeAndLeaveOthersUntouched()
    {
        TestDefaultHeaders h1 = NewInstance();

        h1.Add(Of("name1"), Of("value1"));
        h1.Add(Of("name2"), Of("value2"));
        h1.Add(Of("name2"), Of("value3"));
        h1.Add(Of("name3"), Of("value4"));

        TestDefaultHeaders h2 = NewInstance();
        h2.Add(Of("name1"), Of("value5"));
        h2.Add(Of("name2"), Of("value6"));
        h2.Add(Of("name1"), Of("value7"));

        TestDefaultHeaders expected = NewInstance();
        expected.Add(Of("name1"), Of("value5"));
        expected.Add(Of("name2"), Of("value6"));
        expected.Add(Of("name1"), Of("value7"));
        expected.Add(Of("name3"), Of("value4"));

        h1.SetAll(h2);

        Assert.Equal(expected, h1);
    }

    [Fact]
    public void HeadersWithSameNamesAndValuesShouldBeEquivalent()
    {
        TestDefaultHeaders headers1 = NewInstance();
        headers1.Add(Of("name1"), Of("value1"));
        headers1.Add(Of("name2"), Of("value2"));
        headers1.Add(Of("name2"), Of("value3"));

        TestDefaultHeaders headers2 = NewInstance();
        headers2.Add(Of("name1"), Of("value1"));
        headers2.Add(Of("name2"), Of("value2"));
        headers2.Add(Of("name2"), Of("value3"));

        Assert.Equal(headers1, headers2);
        Assert.Equal(headers2, headers1);
        Assert.Equal(headers1, headers1);
        Assert.Equal(headers2, headers2);
        Assert.Equal(headers1.GetHashCode(), headers2.GetHashCode());
        Assert.Equal(headers1.GetHashCode(), headers1.GetHashCode());
        Assert.Equal(headers2.GetHashCode(), headers2.GetHashCode());
    }

    [Fact]
    public void EmptyHeadersShouldBeEqual()
    {
        TestDefaultHeaders headers1 = NewInstance();
        TestDefaultHeaders headers2 = NewInstance();
        Assert.NotSame(headers1, headers2);
        Assert.Equal(headers1, headers2);
        Assert.Equal(headers1.GetHashCode(), headers2.GetHashCode());
    }

    [Fact]
    public void HeadersWithSameNamesButDifferentValuesShouldNotBeEquivalent()
    {
        TestDefaultHeaders headers1 = NewInstance();
        headers1.Add(Of("name1"), Of("value1"));
        TestDefaultHeaders headers2 = NewInstance();
        headers1.Add(Of("name1"), Of("value2"));
        Assert.NotEqual(headers1, headers2);
    }

    [Fact]
    public void SubsetOfHeadersShouldNotBeEquivalent()
    {
        TestDefaultHeaders headers1 = NewInstance();
        headers1.Add(Of("name1"), Of("value1"));
        headers1.Add(Of("name2"), Of("value2"));
        TestDefaultHeaders headers2 = NewInstance();
        headers1.Add(Of("name1"), Of("value1"));
        Assert.NotEqual(headers1, headers2);
    }

    [Fact]
    public void HeadersWithDifferentNamesAndValuesShouldNotBeEquivalent()
    {
        TestDefaultHeaders h1 = NewInstance();
        h1.Set(Of("name1"), Of("value1"));
        TestDefaultHeaders h2 = NewInstance();
        h2.Set(Of("name2"), Of("value2"));
        Assert.NotEqual(h1, h2);
        Assert.NotEqual(h2, h1);
        Assert.Equal(h1, h1);
        Assert.Equal(h2, h2);
    }

    [Fact]
    public void IterateEmptyHeadersShouldThrow()
    {
        IIterator<IEntry<ICharSequence, ICharSequence>> iterator = NewInstance().Iterator();
        Assert.False(iterator.HasNext());
        Assert.Throws<InvalidOperationException>(() => iterator.Next());
    }

    [Fact]
    public void IteratorShouldReturnAllNameValuePairs()
    {
        TestDefaultHeaders headers1 = NewInstance();
        headers1.Add(Of("name1"), Of("value1"), Of("value2"));
        headers1.Add(Of("name2"), Of("value3"));
        headers1.Add(Of("name3"), Of("value4"), Of("value5"), Of("value6"));
        headers1.Add(Of("name1"), Of("value7"), Of("value8"));
        Assert.Equal(8, headers1.Size());

        TestDefaultHeaders headers2 = NewInstance();

        foreach (IEntry<ICharSequence, ICharSequence> entry in headers1)
        {
            headers2.Add(entry.GetKey(), entry.GetValue());
        }

        Assert.Equal(headers1, headers2);
    }

    [Fact]
    public void IteratorSetValueShouldChangeHeaderValue()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"), Of("value2"), Of("value3"));
        headers.Add(Of("name2"), Of("value4"));
        Assert.Equal(4, headers.Size());

        IIterator<IEntry<ICharSequence, ICharSequence>> iter = headers.Iterator();

        while (iter.HasNext())
        {
            IEntry<ICharSequence, ICharSequence> header = iter.Next();
            if (Of("name1").Equals(header.GetKey()) && Of("value2").Equals(header.GetValue()))
            {
                header.SetValue(Of("updatedvalue2"));
                Assert.Equal(Of("updatedvalue2"), header.GetValue());
            }

            if (Of("name1").Equals(header.GetKey()) && Of("value3").Equals(header.GetValue()))
            {
                header.SetValue(Of("updatedvalue3"));
                Assert.Equal(Of("updatedvalue3"), header.GetValue());
            }
        }

        Assert.Equal(4, headers.Size());
        Assert.True(headers.Contains(Of("name1"), Of("updatedvalue2")));
        Assert.False(headers.Contains(Of("name1"), Of("value2")));
        Assert.True(headers.Contains(Of("name1"), Of("updatedvalue3")));
        Assert.False(headers.Contains(Of("name1"), Of("value3")));
    }

    [Fact]
    public void TestEntryEquals()
    {
        IEntry<ICharSequence, ICharSequence> same1 = NewInstance()
                .Add(new StringCharSequence("name"), new StringCharSequence("value")).Iterator().Next();
        IEntry<ICharSequence, ICharSequence> same2 = NewInstance()
                .Add(new StringCharSequence("name"), new StringCharSequence("value")).Iterator().Next();
        Assert.Equal(same1, same2);
        Assert.Equal(same1.GetHashCode(), same2.GetHashCode());

        IEntry<ICharSequence, ICharSequence> nameDifferent1 = NewInstance()
                .Add(new StringCharSequence("name1"), new StringCharSequence("value")).Iterator().Next();
        IEntry<ICharSequence, ICharSequence> nameDifferent2 = NewInstance()
                .Add(new StringCharSequence("name2"), new StringCharSequence("value")).Iterator().Next();
        Assert.NotEqual(nameDifferent1, nameDifferent2);
        Assert.NotEqual(nameDifferent1.GetHashCode(), nameDifferent2.GetHashCode());

        IEntry<ICharSequence, ICharSequence> valueDifferent1 = NewInstance()
                .Add(new StringCharSequence("name"), new StringCharSequence("value1")).Iterator().Next();
        IEntry<ICharSequence, ICharSequence> valueDifferent2 = NewInstance()
                .Add(new StringCharSequence("name"), new StringCharSequence("value2")).Iterator().Next();
        Assert.NotEqual(valueDifferent1, valueDifferent2);
        Assert.NotEqual(valueDifferent1.GetHashCode(), valueDifferent2.GetHashCode());
    }

    [Fact]
    public void GetAllReturnsEmptyListForUnknownName()
    {
        TestDefaultHeaders headers = NewInstance();
        Assert.Equal(0, headers.GetAll(Of("noname")).Count);
    }

    [Fact]
    public void SetHeadersShouldClearAndOverwrite()
    {
        TestDefaultHeaders headers1 = NewInstance();
        headers1.Add(Of("name"), Of("value"));

        TestDefaultHeaders headers2 = NewInstance();
        headers2.Add(Of("name"), Of("newvalue"));
        headers2.Add(Of("name1"), Of("value1"));

        headers1.Set(headers2);
        Assert.Equal(headers1, headers2);
    }

    [Fact]
    public void SetAllHeadersShouldOnlyOverwriteHeaders()
    {
        TestDefaultHeaders headers1 = NewInstance();
        headers1.Add(Of("name"), Of("value"));
        headers1.Add(Of("name1"), Of("value1"));

        TestDefaultHeaders headers2 = NewInstance();
        headers2.Add(Of("name"), Of("newvalue"));
        headers2.Add(Of("name2"), Of("value2"));

        TestDefaultHeaders expected = NewInstance();
        expected.Add(Of("name"), Of("newvalue"));
        expected.Add(Of("name1"), Of("value1"));
        expected.Add(Of("name2"), Of("value2"));

        headers1.SetAll(headers2);
        Assert.Equal(headers1, expected);
    }

    [Fact]
    public void TestAddSelf()
    {
        TestDefaultHeaders headers = NewInstance();
        Assert.Throws<ArgumentException>(() => headers.Add(headers));
    }

    [Fact]
    public void TestSetSelfIsNoOp()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(new StringCharSequence("name"), new StringCharSequence("value"));
        headers.Set(headers);
        Assert.Equal(1, headers.Size());
    }

    [Fact]
    public void TestToString()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"));
        headers.Add(Of("name1"), Of("value2"));
        headers.Add(Of("name2"), Of("value3"));
        Assert.Equal("TestDefaultHeaders[name1: value1, name1: value2, name2: value3]", headers.ToString());

        headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"));
        headers.Add(Of("name2"), Of("value2"));
        headers.Add(Of("name3"), Of("value3"));
        Assert.Equal("TestDefaultHeaders[name1: value1, name2: value2, name3: value3]", headers.ToString());

        headers = NewInstance();
        headers.Add(Of("name1"), Of("value1"));
        Assert.Equal("TestDefaultHeaders[name1: value1]", headers.ToString());

        headers = NewInstance();
        Assert.Equal("TestDefaultHeaders[]", headers.ToString());
    }

    [Fact]
    public void TestNotThrowWhenConvertFails()
    {
        TestDefaultHeaders headers = new TestDefaultHeaders(new ThrowingValueConverter());
        headers.Set(new StringCharSequence("name1"), new StringCharSequence(""));
        Assert.Null(headers.GetInt(new StringCharSequence("name1")));
        Assert.Equal(1, headers.GetInt(new StringCharSequence("name1"), 1));

        Assert.Null(headers.GetBoolean(new StringCharSequence("")));
        Assert.False(headers.GetBoolean(new StringCharSequence("name1"), false));

        Assert.Null(headers.GetByte(new StringCharSequence("name1")));
        Assert.Equal(1, headers.GetByte(new StringCharSequence("name1"), (sbyte) 1));

        Assert.Null(headers.GetChar(new StringCharSequence("name")));
        Assert.Equal('n', headers.GetChar(new StringCharSequence("name1"), 'n'));

        Assert.Null(headers.GetDouble(new StringCharSequence("name")));
        Assert.Equal(1, headers.GetDouble(new StringCharSequence("name1"), 1), 0d);

        Assert.Null(headers.GetFloat(new StringCharSequence("name")));
        Assert.Equal(float.MaxValue, headers.GetFloat(new StringCharSequence("name1"), float.MaxValue), 0f);

        Assert.Null(headers.GetLong(new StringCharSequence("name")));
        Assert.Equal(long.MaxValue, headers.GetLong(new StringCharSequence("name1"), long.MaxValue));

        Assert.Null(headers.GetShort(new StringCharSequence("name")));
        Assert.Equal(short.MaxValue, headers.GetShort(new StringCharSequence("name1"), short.MaxValue));

        Assert.Null(headers.GetTimeMillis(new StringCharSequence("name")));
        Assert.Equal(long.MaxValue, headers.GetTimeMillis(new StringCharSequence("name1"), long.MaxValue));
    }

    // CLR: the anonymous ValueConverter is a named nested class; IllegalArgumentException is ArgumentException.
    private sealed class ThrowingValueConverter : IValueConverter<ICharSequence>
    {
        public ICharSequence ConvertObject(object value)
        {
            throw new ArgumentException();
        }

        public ICharSequence ConvertBoolean(bool value)
        {
            throw new ArgumentException();
        }

        public bool ConvertToBoolean(ICharSequence value)
        {
            throw new ArgumentException();
        }

        public ICharSequence ConvertByte(sbyte value)
        {
            throw new ArgumentException();
        }

        public sbyte ConvertToByte(ICharSequence value)
        {
            throw new ArgumentException();
        }

        public ICharSequence ConvertChar(char value)
        {
            throw new ArgumentException();
        }

        public char ConvertToChar(ICharSequence value)
        {
            throw new ArgumentException();
        }

        public ICharSequence ConvertShort(short value)
        {
            throw new ArgumentException();
        }

        public short ConvertToShort(ICharSequence value)
        {
            throw new ArgumentException();
        }

        public ICharSequence ConvertInt(int value)
        {
            throw new ArgumentException();
        }

        public int ConvertToInt(ICharSequence value)
        {
            throw new ArgumentException();
        }

        public ICharSequence ConvertLong(long value)
        {
            throw new ArgumentException();
        }

        public long ConvertToLong(ICharSequence value)
        {
            throw new ArgumentException();
        }

        public ICharSequence ConvertTimeMillis(long value)
        {
            throw new ArgumentException();
        }

        public long ConvertToTimeMillis(ICharSequence value)
        {
            throw new ArgumentException();
        }

        public ICharSequence ConvertFloat(float value)
        {
            throw new ArgumentException();
        }

        public float ConvertToFloat(ICharSequence value)
        {
            throw new ArgumentException();
        }

        public ICharSequence ConvertDouble(double value)
        {
            throw new ArgumentException();
        }

        public double ConvertToDouble(ICharSequence value)
        {
            throw new ArgumentException();
        }
    }

    // CLR: new StringBuilder("invalid"), a mutable CharSequence that is neither a String nor an AsciiString, is
    // Netty's mutable AppendableCharSequence.
    [Fact]
    public void TestGetBooleanInvalidValue()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Set(new StringCharSequence("name1"), new StringCharSequence("invalid"));
        headers.Set(new StringCharSequence("name2"), new AsciiString("invalid"));
        headers.Set(new StringCharSequence("name3"), new AppendableCharSequence("invalid".Length).Append("invalid"));

        Assert.False(headers.GetBoolean(new StringCharSequence("name1"), false));
        Assert.False(headers.GetBoolean(new StringCharSequence("name2"), false));
        Assert.False(headers.GetBoolean(new StringCharSequence("name3"), false));
    }

    [Fact]
    public void TestGetBooleanFalseValue()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Set(new StringCharSequence("name1"), new StringCharSequence("false"));
        headers.Set(new StringCharSequence("name2"), new AsciiString("false"));
        headers.Set(new StringCharSequence("name3"), new AppendableCharSequence("false".Length).Append("false"));

        Assert.False(headers.GetBoolean(new StringCharSequence("name1"), true));
        Assert.False(headers.GetBoolean(new StringCharSequence("name2"), true));
        Assert.False(headers.GetBoolean(new StringCharSequence("name3"), true));
    }

    [Fact]
    public void TestGetBooleanTrueValue()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Set(new StringCharSequence("name1"), new StringCharSequence("true"));
        headers.Set(new StringCharSequence("name2"), new AsciiString("true"));
        headers.Set(new StringCharSequence("name3"), new AppendableCharSequence("true".Length).Append("true"));

        Assert.True(headers.GetBoolean(new StringCharSequence("name1"), false));
        Assert.True(headers.GetBoolean(new StringCharSequence("name2"), false));
        Assert.True(headers.GetBoolean(new StringCharSequence("name3"), false));
    }

    // CLR: assertThat(cookies).contains(values...) asserts that each value is present, in any order.
    [Fact]
    public void HandlingOfHeaderNameHashCollisions()
    {
        TestDefaultHeaders headers = new TestDefaultHeaders(new DegenerateHashingStrategy());

        headers.Add(new StringCharSequence("Cookie"), new StringCharSequence("a=b; c=d; e=f"));
        headers.Add(new StringCharSequence("other"), new StringCharSequence("text/plain"));  // Add another header which will be saved in the same entries[index]

        SimulateCookieSplitting(headers);
        IList<ICharSequence> cookies = headers.GetAll(new StringCharSequence("Cookie"));

        Assert.Equal(3, cookies.Count);
        Assert.Contains(new StringCharSequence("a=b"), cookies);
        Assert.Contains(new StringCharSequence("c=d"), cookies);
        Assert.Contains(new StringCharSequence("e=f"), cookies);
    }

    // CLR: the anonymous HashingStrategy is a named nested IEqualityComparer.
    private sealed class DegenerateHashingStrategy : IEqualityComparer<ICharSequence>
    {
        public int GetHashCode(ICharSequence obj)
        {
            return 0; // Degenerate hashing strategy to enforce collisions.
        }

        public bool Equals(ICharSequence a, ICharSequence b)
        {
            return a.Equals(b);
        }
    }

    /**
     * Split up cookies into individual cookie crumb headers.
     */
    // CLR: package-private, but private here because TestDefaultHeaders is private. String.split("; ") with this
    // literal pattern is string.Split("; ") (the input has no trailing empty strings for Java to drop), and the
    // String crumbs are wrapped as StringCharSequence.
    private static void SimulateCookieSplitting(TestDefaultHeaders headers)
    {
        IIterator<ICharSequence> cookieItr = headers.ValueIterator(new StringCharSequence("Cookie"));
        if (!cookieItr.HasNext())
        {
            return;
        }

        // We want to avoid "concurrent modifications" of the headers while we are iterating. So we insert crumbs
        // into an intermediate collection and insert them after the split process concludes.
        List<ICharSequence> cookiesToAdd = new List<ICharSequence>();

        while (cookieItr.HasNext())
        {
            //noinspection DynamicRegexReplaceableByCompiledPattern
            string[] cookies = cookieItr.Next().ToString().Split("; ");
            cookiesToAdd.AddRange(cookies.Select(cookie => new StringCharSequence(cookie)));
            cookieItr.Remove();
        }

        foreach (ICharSequence crumb in cookiesToAdd)
        {
            headers.Add(new StringCharSequence("Cookie"), crumb);
        }
    }

    // CLR: BiPredicate is Func<ICharSequence, ICharSequence, bool>; the unbound Object::equals reference is a
    // lambda calling the first argument's Equals.
    [Fact]
    public void TestContainsAnyEquality()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("name"), Of("value1"), Of("value2"));

        Func<ICharSequence, ICharSequence, bool> equalsPredicate = (stored, arg) => stored.Equals(arg);

        Assert.True(headers.ContainsAny(Of("name"), Of("value2"), equalsPredicate));
        Assert.False(headers.ContainsAny(Of("name"), Of("value3"), equalsPredicate));
        // Unknown name should return false
        Assert.False(headers.ContainsAny(Of("unknown"), Of("value1"), equalsPredicate));
    }

    // CLR: String.contains(CharSequence) reads the argument's toString().
    [Fact]
    public void TestContainsAnySubstringPredicate()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("h1"), Of("prefix-value"));
        headers.Add(Of("h1"), Of("other"));

        Func<ICharSequence, ICharSequence, bool> containsPredicate = (stored, arg) =>
                stored.ToString().Contains(arg.ToString());

        Assert.True(headers.ContainsAny(Of("h1"), Of("value"), containsPredicate));
        Assert.False(headers.ContainsAny(Of("h1"), Of("missing"), containsPredicate));
    }

    [Fact]
    public void ContainsAnyHandlesHashCollisions()
    {
        TestDefaultHeaders headers = new TestDefaultHeaders(new CollidingHashingStrategy());

        headers.Add(Of("name1"), Of("value1"));
        headers.Add(Of("name2"), Of("value2"));

        Func<ICharSequence, ICharSequence, bool> eq = (stored, arg) => stored.Equals(arg);

        Assert.True(headers.ContainsAny(Of("name1"), Of("value1"), eq));
        Assert.True(headers.ContainsAny(Of("name2"), Of("value2"), eq));
        Assert.False(headers.ContainsAny(Of("name1"), Of("no"), eq));
        Assert.False(headers.ContainsAny(Of("name1"), Of("value2"), eq));
        Assert.False(headers.ContainsAny(Of("name2"), Of("value1"), eq));
    }

    // CLR: the anonymous HashingStrategy is a named nested IEqualityComparer.
    private sealed class CollidingHashingStrategy : IEqualityComparer<ICharSequence>
    {
        public int GetHashCode(ICharSequence obj)
        {
            return 0; // force collisions
        }

        public bool Equals(ICharSequence a, ICharSequence b)
        {
            return a.Equals(b);
        }
    }

    [Fact]
    public void ContainsAnyShouldValidateArguments()
    {
        TestDefaultHeaders headers = NewInstance();
        headers.Add(Of("n"), Of("v"));

        Assert.Throws<ArgumentNullException>(() => headers.ContainsAny(null, Of("v"), (stored, arg) => false));

        Assert.Throws<ArgumentNullException>(() => headers.ContainsAny(Of("n"), Of("v"), null));
    }
}
