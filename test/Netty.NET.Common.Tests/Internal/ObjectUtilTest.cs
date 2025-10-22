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
    public void testCheckNotNull()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkNotNull(NON_NULL_OBJECT, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNotNull(NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is NullReferenceException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckNotNullWithIAE()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkNotNullWithIAE(NON_NULL_OBJECT, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNotNullWithIAE(NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckNotNullArrayParam()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkNotNullArrayParam(NON_NULL_OBJECT, 1, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNotNullArrayParam(NULL_OBJECT, 1, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckPositiveIntString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkPositive(POS_ONE_INT, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositive(ZERO_INT, NUM_ZERO_NAME);
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
            ObjectUtil.checkPositive(NEG_ONE_INT, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckPositiveLongString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkPositive(POS_ONE_LONG, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositive(ZERO_LONG, NUM_ZERO_NAME);
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
            ObjectUtil.checkPositive(NEG_ONE_LONG, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckPositiveDoubleString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkPositive(POS_ONE_DOUBLE, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositive(ZERO_DOUBLE, NUM_ZERO_NAME);
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
            ObjectUtil.checkPositive(NEG_ONE_DOUBLE, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckPositiveFloatString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkPositive(POS_ONE_FLOAT, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositive(ZERO_FLOAT, NUM_ZERO_NAME);
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
            ObjectUtil.checkPositive(NEG_ONE_FLOAT, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckPositiveOrZeroIntString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(POS_ONE_INT, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(ZERO_INT, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(NEG_ONE_INT, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckPositiveOrZeroLongString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(POS_ONE_LONG, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(ZERO_LONG, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(NEG_ONE_LONG, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckPositiveOrZeroDoubleString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(POS_ONE_DOUBLE, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(ZERO_DOUBLE, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(NEG_ONE_DOUBLE, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckPositiveOrZeroFloatString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(POS_ONE_FLOAT, NUM_POS_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(ZERO_FLOAT, NUM_ZERO_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkPositiveOrZero(NEG_ONE_FLOAT, NUM_NEG_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckNonEmptyTArrayString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.checkNonEmpty((object[])NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is NullReferenceException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((object[])NON_NULL_FILLED_OBJECT_ARRAY, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((object[])NON_NULL_EMPTY_OBJECT_ARRAY, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckNonEmptyByteArrayString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.checkNonEmpty((byte[])NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is NullReferenceException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((byte[])NON_NULL_FILLED_BYTE_ARRAY, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((byte[])NON_NULL_EMPTY_BYTE_ARRAY, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckNonEmptyCharArrayString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.checkNonEmpty((char[])NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is NullReferenceException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((char[])NON_NULL_FILLED_CHAR_ARRAY, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((char[])NON_NULL_EMPTY_CHAR_ARRAY, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckNonEmptyTString()
    {
        Exception actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((object[])NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is NullReferenceException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((object[])NON_NULL_FILLED_OBJECT_ARRAY, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((object[])NON_NULL_EMPTY_OBJECT_ARRAY, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }

    [Fact]
    public void testCheckNonEmptyStringString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.checkNonEmpty((string)NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is NullReferenceException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((string)NON_NULL_OBJECT, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((string)NON_NULL_EMPTY_STRING, NON_NULL_EMPTY_NAME);
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
            ObjectUtil.checkNonEmpty((string)NON_NULL_WHITESPACE_STRING, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);
    }

    [Fact]
    public void testCheckNonEmptyCharSequenceString()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.checkNonEmpty((ICharSequence)NULL_CHARSEQUENCE, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is NullReferenceException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((ICharSequence)NON_NULL_CHARSEQUENCE, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmpty((ICharSequence)NON_NULL_EMPTY_CHARSEQUENCE, NON_NULL_EMPTY_NAME);
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
            ObjectUtil.checkNonEmpty(new StringCharSequence(NON_NULL_WHITESPACE_STRING), NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);
    }

    [Fact]
    public void testCheckNonEmptyAfterTrim()
    {
        Exception actualEx = null;

        try
        {
            ObjectUtil.checkNonEmptyAfterTrim((string)NULL_OBJECT, NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is NullReferenceException, TEST_RESULT_EXTYPE_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmptyAfterTrim((string)NON_NULL_OBJECT, NON_NULL_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.Null(actualEx, TEST_RESULT_NULLEX_NOK);

        actualEx = null;
        try
        {
            ObjectUtil.checkNonEmptyAfterTrim(NON_NULL_EMPTY_STRING, NON_NULL_EMPTY_NAME);
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
            ObjectUtil.checkNonEmptyAfterTrim(NON_NULL_WHITESPACE_STRING, NON_NULL_EMPTY_NAME);
        }
        catch (Exception e)
        {
            actualEx = e;
        }

        Assert.NotNull(actualEx, TEST_RESULT_NULLEX_OK);
        Assert.True(actualEx is ArgumentException, TEST_RESULT_EXTYPE_NOK);
    }
}