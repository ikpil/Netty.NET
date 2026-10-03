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

namespace Netty.NET.Common.Internal;

/**
 * A grab-bag of useful utility methods.
 */
public static class ObjectUtil
{
    private static readonly float FLOAT_ZERO = 0.0F;
    private static readonly double DOUBLE_ZERO = 0.0D;
    private static readonly long LONG_ZERO = 0L;
    private static readonly int INT_ZERO = 0;
    private static readonly short SHORT_ZERO = 0;

    public static T RequireNonNull<T>(T obj, string message) where T : class
    {
        if (obj == null)
            throw new NullReferenceException(message);
        
        return obj;
    }

    /**
     * Checks that the given argument is not null. If it is, throws {@link NullPointerException}.
     * Otherwise, returns the argument.
     */
    public static T CheckNotNull<T>(T arg, string text) where T : class
    {
        if (arg == null)
        {
            throw new ArgumentNullException(text);
        }

        return arg;
    }
    
    public static IEnumerable<T> CheckNotNull<T>(IEnumerable<T> args, string text)
    {
        if (args == null)
        {
            throw new ArgumentNullException(text);
        }
        
        return args;
    }

    /**
     * Check that the given varargs is not null and does not contain elements
     * null elements.
     *
     * If it is, throws {@link NullPointerException}.
     * Otherwise, returns the argument.
     */
    public static T[] DeepCheckNotNull<T>(string text, params T[] varargs)
    {
        if (varargs == null)
        {
            throw new ArgumentNullException(text);
        }

        foreach (T element in varargs)
        {
            if (element == null)
            {
                throw new ArgumentNullException(text);
            }
        }

        return varargs;
    }

    /**
     * Checks that the given argument is not null. If it is, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static T CheckNotNullWithIAE<T>(T arg, string paramName)
    {
        if (arg == null)
        {
            throw new ArgumentException("Param '" + paramName + "' must not be null");
        }

        return arg;
    }

    /**
     * Checks that the given argument is not null. If it is, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     *
     * @param <T> type of the given argument value.
     * @param name of the parameter, belongs to the exception message.
     * @param index of the array, belongs to the exception message.
     * @param value to check.
     * @return the given argument value.
     * @throws IllegalArgumentException if value is null.
     */
    public static T CheckNotNullArrayParam<T>(T value, int index, string name)
    {
        if (value == null)
        {
            throw new ArgumentException(
                "Array index " + index + " of parameter '" + name + "' must not be null");
        }

        return value;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static int CheckPositive(int i, string name)
    {
        if (i <= INT_ZERO)
        {
            throw new ArgumentException(name + " : " + i + " (expected: > 0)");
        }

        return i;
    }
    
    public static TimeSpan CheckPositive(TimeSpan span, string name)
    {
        if (span.Ticks <= INT_ZERO)
        {
            throw new ArgumentException(name + " : " + span.Ticks + " (expected: > 0)");
        }

        return span;
    }


    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static long CheckPositive(long l, string name)
    {
        if (l <= LONG_ZERO)
        {
            throw new ArgumentException(name + " : " + l + " (expected: > 0)");
        }

        return l;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static double CheckPositive(double d, string name)
    {
        if (d <= DOUBLE_ZERO)
        {
            throw new ArgumentException(name + " : " + d + " (expected: > 0)");
        }

        return d;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static float CheckPositive(float f, string name)
    {
        if (f <= FLOAT_ZERO)
        {
            throw new ArgumentException(name + " : " + f + " (expected: > 0)");
        }

        return f;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not , throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static short CheckPositive(short s, string name)
    {
        if (s <= SHORT_ZERO)
        {
            throw new ArgumentException(name + " : " + s + " (expected: > 0)");
        }

        return s;
    }

    public static TimeSpan CheckPositiveOrZero(TimeSpan span, string name)
    {
        if (span.Ticks < LONG_ZERO)
        {
            throw new ArgumentException(name + " : " + span + " (expected: >= 0)");
        }

        return span;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not , throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static int CheckPositiveOrZero(int i, string name)
    {
        if (i < INT_ZERO)
        {
            throw new ArgumentException(name + " : " + i + " (expected: >= 0)");
        }

        return i;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static long CheckPositiveOrZero(long l, string name)
    {
        if (l < LONG_ZERO)
        {
            throw new ArgumentException(name + " : " + l + " (expected: >= 0)");
        }

        return l;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static double CheckPositiveOrZero(double d, string name)
    {
        if (d < DOUBLE_ZERO)
        {
            throw new ArgumentException(name + " : " + d + " (expected: >= 0)");
        }

        return d;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static float CheckPositiveOrZero(float f, string name)
    {
        if (f < FLOAT_ZERO)
        {
            throw new ArgumentException(name + " : " + f + " (expected: >= 0)");
        }

        return f;
    }

    /**
     * Checks that the given argument is in range. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static int CheckInRange(int i, int start, int end, string name)
    {
        if (i < start || i > end)
        {
            throw new ArgumentException(name + ": " + i + " (expected: " + start + "-" + end + ")");
        }

        return i;
    }

    /**
     * Checks that the given argument is in range. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static long CheckInRange(long l, long start, long end, string name)
    {
        if (l < start || l > end)
        {
            throw new ArgumentException(name + ": " + l + " (expected: " + start + "-" + end + ")");
        }

        return l;
    }

    /**
     * Checks that the given argument is in range. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static double CheckInRange(double value, double start, double end, string name)
    {
        if (value < start || value > end)
            throw new ArgumentException(name + ": " + value + " (expected: " + start + "-" + end + ")");
        return value;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static T[] CheckNonEmpty<T>(T[] array, string name)
    {
        //No String concatenation for check
        if (CheckNotNull(array, name).Length == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return array;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static byte[] CheckNonEmpty(byte[] array, string name)
    {
        //No String concatenation for check
        if (CheckNotNull(array, name).Length == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return array;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static char[] CheckNonEmpty(char[] array, string name)
    {
        //No String concatenation for check
        if (CheckNotNull(array, name).Length == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return array;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static ICollection<T> CheckNonEmpty<T>(ICollection<T> collection, string name)
    {
        //No String concatenation for check
        if (CheckNotNull<ICollection<T>>(collection, name).Count == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return collection;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static string CheckNonEmpty(string value, string name)
    {
        if (string.IsNullOrEmpty(CheckNotNull(value, name)))
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return value;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static IDictionary<K, V> CheckNonEmpty<K, V>(IDictionary<K, V> value, string name)
    {
        if (CheckNotNull(value, name).Count == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return value;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static ICharSequence CheckNonEmpty(ICharSequence value, string name)
    {
        if (CheckNotNull(value, name).Count == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return value;
    }

    /**
     * Trims the given argument and checks whether it is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the trimmed argument.
     *
     * @param value to trim and check.
     * @param name of the parameter.
     * @return the trimmed (not the original) value.
     * @throws NullPointerException if value is null.
     * @throws IllegalArgumentException if the trimmed value is empty.
     */
    public static string CheckNonEmptyAfterTrim(string value, string name)
    {
        CheckNotNull(value, name);
        int start = 0, end = value.Length;
        while (start < end && value[start] <= ' ') start++;
        while (start < end && value[end - 1] <= ' ') end--;
        string trimmed = value.Substring(start, end - start);
        return CheckNonEmpty(trimmed, name);
    }

    /**
     * Resolves a possibly null Integer to a primitive int, using a default value.
     * @param wrapper the wrapper
     * @param defaultValue the default value
     * @return the primitive value
     */
    public static int IntValue(int? wrapper, int defaultValue)
    {
        return wrapper ?? defaultValue;
    }

    /**
     * Resolves a possibly null Long to a primitive long, using a default value.
     * @param wrapper the wrapper
     * @param defaultValue the default value
     * @return the primitive value
     */
    public static long LongValue(long? wrapper, long defaultValue)
    {
        return wrapper ?? defaultValue;
    }

    public static T Null<T>() where T : class
    {
        return null;
    }
    
}
