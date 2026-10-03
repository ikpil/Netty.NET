/*
 * Copyright 2021 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;

/**
 * Testcases for io.netty.util.internal.ObjectUtil.
 *
 * The tests for exceptions do not use a fail mimic. The tests evaluate the
 * presence and type, to have really regression character.
 *
 */
public class ObjectUtilTest
{
    [Fact]
    public void TestCheckInRangeDouble()
    {
        Assert.Equal(0.5, ObjectUtil.CheckInRange(0.5, 0.0, 1.0, "in range"));
        Assert.Equal(0.0, ObjectUtil.CheckInRange(0.0, 0.0, 1.0, "start of range"));
        Assert.Equal(1.0, ObjectUtil.CheckInRange(1.0, 0.0, 1.0, "end of range"));
        Assert.Throws<ArgumentException>(() => ObjectUtil.CheckInRange(-0.1, 0.0, 1.0, "below range"));
        Assert.Throws<ArgumentException>(() => ObjectUtil.CheckInRange(1.1, 0.0, 1.0, "above range"));
    }

    [Fact]
    public void NullableNumberWrappersPreserveDefaultValues()
    {
        Assert.Equal(7, ObjectUtil.IntValue(null, 7));
        Assert.Equal(0, ObjectUtil.IntValue(0, 7));
        Assert.Equal(9L, ObjectUtil.LongValue(null, 9L));
        Assert.Equal(0L, ObjectUtil.LongValue(0L, 9L));
    }

    private static readonly object NULL_OBJECT = null;

    private static readonly string NON_NULL_OBJECT = "object is not null";
    private static readonly string NON_NULL_EMPTY_STRING = "";
    private static readonly string NON_NULL_WHITESPACE_STRING = "  ";
    private static readonly object[] NON_NULL_EMPTY_OBJECT_ARRAY = { };
    private static readonly object[] NON_NULL_FILLED_OBJECT_ARRAY = { NON_NULL_OBJECT };
    private static readonly ICharSequence NULL_CHARSEQUENCE = (ICharSequence)NULL_OBJECT;
    private static readonly ICharSequence NON_NULL_CHARSEQUENCE = new StringCharSequence(NON_NULL_OBJECT);
    private static readonly ICharSequence NON_NULL_EMPTY_CHARSEQUENCE = new StringCharSequence(NON_NULL_EMPTY_STRING);
    private static readonly byte[] NON_NULL_EMPTY_BYTE_ARRAY = { };
    private static readonly byte[] NON_NULL_FILLED_BYTE_ARRAY = { (byte)0xa };
    private static readonly char[] NON_NULL_EMPTY_CHAR_ARRAY = { };
    private static readonly char[] NON_NULL_FILLED_CHAR_ARRAY = { 'A' };

    private static readonly string NULL_NAME = "IS_NULL";
    private static readonly string NON_NULL_NAME = "NOT_NULL";
    private static readonly string NON_NULL_EMPTY_NAME = "NOT_NULL_BUT_EMPTY";

    private static readonly string TEST_RESULT_NULLEX_OK = "Expected a NPE/IAE";
    private static readonly string TEST_RESULT_NULLEX_NOK = "Expected no exception";
    private static readonly string TEST_RESULT_EXTYPE_NOK = "Expected type not found";

    private static readonly int ZERO_INT = 0;
    private static readonly long ZERO_LONG = 0;
    private static readonly double ZERO_DOUBLE = 0.0d;
    private static readonly float ZERO_FLOAT = 0.0f;

    private static readonly int POS_ONE_INT = 1;
    private static readonly long POS_ONE_LONG = 1;
    private static readonly double POS_ONE_DOUBLE = 1.0d;
    private static readonly float POS_ONE_FLOAT = 1.0f;

    private static readonly int NEG_ONE_INT = -1;
    private static readonly long NEG_ONE_LONG = -1;
    private static readonly double NEG_ONE_DOUBLE = -1.0d;
    private static readonly float NEG_ONE_FLOAT = -1.0f;

    private static readonly string NUM_POS_NAME = "NUMBER_POSITIVE";
    private static readonly string NUM_ZERO_NAME = "NUMBER_ZERO";
    private static readonly string NUM_NEG_NAME = "NUMBER_NEGATIVE";

    [Fact]
    public void TestCheckNotNull()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckNotNull(NON_NULL_OBJECT, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNotNull(NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentNullException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckNotNullWithIAE()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckNotNullWithIAE(NON_NULL_OBJECT, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNotNullWithIAE(NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckNotNullArrayParam()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckNotNullArrayParam(NON_NULL_OBJECT, 1, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNotNullArrayParam(NULL_OBJECT, 1, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckPositiveIntString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(POS_ONE_INT, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(ZERO_INT, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(NEG_ONE_INT, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckPositiveLongString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(POS_ONE_LONG, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(ZERO_LONG, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(NEG_ONE_LONG, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckPositiveDoubleString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(POS_ONE_DOUBLE, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(ZERO_DOUBLE, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(NEG_ONE_DOUBLE, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckPositiveFloatString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(POS_ONE_FLOAT, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(ZERO_FLOAT, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositive(NEG_ONE_FLOAT, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckPositiveOrZeroIntString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(POS_ONE_INT, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(ZERO_INT, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(NEG_ONE_INT, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckPositiveOrZeroLongString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(POS_ONE_LONG, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(ZERO_LONG, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(NEG_ONE_LONG, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckPositiveOrZeroDoubleString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(POS_ONE_DOUBLE, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(ZERO_DOUBLE, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(NEG_ONE_DOUBLE, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckPositiveOrZeroFloatString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(POS_ONE_FLOAT, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(ZERO_FLOAT, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckPositiveOrZero(NEG_ONE_FLOAT, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckNonEmptyTArrayString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.CheckNonEmpty((object[])NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentNullException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((object[])NON_NULL_FILLED_OBJECT_ARRAY, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((object[])NON_NULL_EMPTY_OBJECT_ARRAY, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckNonEmptyByteArrayString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.CheckNonEmpty((byte[])NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentNullException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((byte[])NON_NULL_FILLED_BYTE_ARRAY, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((byte[])NON_NULL_EMPTY_BYTE_ARRAY, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckNonEmptyCharArrayString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.CheckNonEmpty((char[])NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentNullException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((char[])NON_NULL_FILLED_CHAR_ARRAY, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((char[])NON_NULL_EMPTY_CHAR_ARRAY, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckNonEmptyTString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((object[])NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentNullException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((object[])NON_NULL_FILLED_OBJECT_ARRAY, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((object[])NON_NULL_EMPTY_OBJECT_ARRAY, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void TestCheckNonEmptyStringString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.CheckNonEmpty((string)NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentNullException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((string)NON_NULL_OBJECT, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((string)NON_NULL_EMPTY_STRING, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((string)NON_NULL_WHITESPACE_STRING, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);
    }

    [Fact]
    public void TestCheckNonEmptyCharSequenceString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.CheckNonEmpty((ICharSequence)NULL_CHARSEQUENCE, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentNullException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((ICharSequence)NON_NULL_CHARSEQUENCE, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty((ICharSequence)NON_NULL_EMPTY_CHARSEQUENCE, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmpty(new StringCharSequence(NON_NULL_WHITESPACE_STRING), NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);
    }

    [Fact]
    public void TestCheckNonEmptyAfterTrim()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.CheckNonEmptyAfterTrim((string)NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentNullException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmptyAfterTrim((string)NON_NULL_OBJECT, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmptyAfterTrim(NON_NULL_EMPTY_STRING, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.CheckNonEmptyAfterTrim(NON_NULL_WHITESPACE_STRING, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }
}
