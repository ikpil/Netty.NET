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

    public static T requireNonNull<T>(T obj, string message) where T : class
    {
        if (obj == null)
            throw new NullReferenceException(message);
        
        return obj;
    }

    /**
     * Checks that the given argument is not null. If it is, throws {@link ArgumentNullException}.
     * Otherwise, returns the argument.
     */
    public static T checkNotNull<T>(T arg, string text) where T : class
    {
        if (arg == null)
        {
            throw new ArgumentNullException(text);
        }

        return arg;
    }
    
    public static IEnumerable<T> checkNotNull<T>(IEnumerable<T> args, string text)
    {
        if (args == null)
        {
            throw new ArgumentNullException(text);
        }
        
        if (args is IList<T> list && list.Any(x => x == null))
        {
            throw new ArgumentNullException(text);
        }
        else if (args is ICollection<T> collection && collection.Any(x => x == null))
        {
            throw new ArgumentNullException(text);
        }
        else if (args is IReadOnlyCollection<T> readOnlyCollection && readOnlyCollection.Any(x => x == null))
        {
            throw new ArgumentNullException(text);
        }
        else
        {
            if (args.Any(x => x == null))
            {
                throw new ArgumentNullException(text);
            }
        }

        return args;
    }

    /**
     * Check that the given varargs is not null and does not contain elements
     * null elements.
     *
     * If it is, throws {@link ArgumentNullException}.
     * Otherwise, returns the argument.
     */
    public static T[] deepCheckNotNull<T>(string text, params T[] varargs)
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
     * Checks that the given argument is not null. If it is, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static T checkNotNullWithIAE<T>(T arg, string paramName)
    {
        if (arg == null)
        {
            throw new ArgumentException("Param '" + paramName + "' must not be null");
        }

        return arg;
    }

    /**
     * Checks that the given argument is not null. If it is, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     *
     * @param <T> type of the given argument value.
     * @param name of the parameter, belongs to the exception message.
     * @param index of the array, belongs to the exception message.
     * @param value to check.
     * @return the given argument value.
     * @throws ArgumentException if value is null.
     */
    public static T checkNotNullArrayParam<T>(T value, int index, string name)
    {
        if (value == null)
        {
            throw new ArgumentException(
                "Array index " + index + " of parameter '" + name + "' must not be null");
        }

        return value;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static int checkPositive(int i, string name)
    {
        if (i <= INT_ZERO)
        {
            throw new ArgumentException(name + " : " + i + " (expected: > 0)");
        }

        return i;
    }
    
    public static TimeSpan checkPositive(TimeSpan span, string name)
    {
        if (span.Ticks <= INT_ZERO)
        {
            throw new ArgumentException(name + " : " + span.Ticks + " (expected: > 0)");
        }

        return span;
    }


    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static long checkPositive(long l, string name)
    {
        if (l <= LONG_ZERO)
        {
            throw new ArgumentException(name + " : " + l + " (expected: > 0)");
        }

        return l;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static double checkPositive(double d, string name)
    {
        if (d <= DOUBLE_ZERO)
        {
            throw new ArgumentException(name + " : " + d + " (expected: > 0)");
        }

        return d;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static float checkPositive(float f, string name)
    {
        if (f <= FLOAT_ZERO)
        {
            throw new ArgumentException(name + " : " + f + " (expected: > 0)");
        }

        return f;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not , throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static short checkPositive(short s, string name)
    {
        if (s <= SHORT_ZERO)
        {
            throw new ArgumentException(name + " : " + s + " (expected: > 0)");
        }

        return s;
    }

    public static TimeSpan checkPositiveOrZero(TimeSpan span, string name)
    {
        if (span.Ticks < LONG_ZERO)
        {
            throw new ArgumentException(name + " : " + span + " (expected: >= 0)");
        }

        return span;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not , throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static int checkPositiveOrZero(int i, string name)
    {
        if (i < INT_ZERO)
        {
            throw new ArgumentException(name + " : " + i + " (expected: >= 0)");
        }

        return i;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static long checkPositiveOrZero(long l, string name)
    {
        if (l < LONG_ZERO)
        {
            throw new ArgumentException(name + " : " + l + " (expected: >= 0)");
        }

        return l;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static double checkPositiveOrZero(double d, string name)
    {
        if (d < DOUBLE_ZERO)
        {
            throw new ArgumentException(name + " : " + d + " (expected: >= 0)");
        }

        return d;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static float checkPositiveOrZero(float f, string name)
    {
        if (f < FLOAT_ZERO)
        {
            throw new ArgumentException(name + " : " + f + " (expected: >= 0)");
        }

        return f;
    }

    /**
     * Checks that the given argument is in range. If it is not, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static int checkInRange(int i, int start, int end, string name)
    {
        if (i < start || i > end)
        {
            throw new ArgumentException(name + ": " + i + " (expected: " + start + "-" + end + ")");
        }

        return i;
    }

    /**
     * Checks that the given argument is in range. If it is not, throws {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static long checkInRange(long l, long start, long end, string name)
    {
        if (l < start || l > end)
        {
            throw new ArgumentException(name + ": " + l + " (expected: " + start + "-" + end + ")");
        }

        return l;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link ArgumentNullException} or {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static T[] checkNonEmpty<T>(T[] array, string name)
    {
        //No string concatenation for check
        if (checkNotNull(array, name).Length == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return array;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link ArgumentNullException} or {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static byte[] checkNonEmpty(byte[] array, string name)
    {
        //No string concatenation for check
        if (checkNotNull(array, name).Length == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return array;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link ArgumentNullException} or {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static char[] checkNonEmpty(char[] array, string name)
    {
        //No string concatenation for check
        if (checkNotNull(array, name).Length == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return array;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link ArgumentNullException} or {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static T checkNonEmpty<T>(T collection, string name) where T : ICollection<T>
    {
        //No string concatenation for check
        if (!checkNotNull(collection, name).Any())
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return collection;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link ArgumentNullException} or {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static string checkNonEmpty(string value, string name)
    {
        if (string.IsNullOrEmpty(checkNotNull(value, name)))
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return value;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link ArgumentNullException} or {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static IDictionary<K, V> checkNonEmpty<K, V>(IDictionary<K, V> value, string name)
    {
        if (checkNotNull(value, name).Count == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return value;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link ArgumentNullException} or {@link ArgumentException}.
     * Otherwise, returns the argument.
     */
    public static ICharSequence checkNonEmpty(ICharSequence value, string name)
    {
        if (checkNotNull(value, name).Count == 0)
        {
            throw new ArgumentException("Param '" + name + "' must not be empty");
        }

        return value;
    }

    /**
     * Trims the given argument and checks whether it is neither null nor empty.
     * If it is, throws {@link ArgumentNullException} or {@link ArgumentException}.
     * Otherwise, returns the trimmed argument.
     *
     * @param value to trim and check.
     * @param name of the parameter.
     * @return the trimmed (not the original) value.
     * @throws ArgumentNullException if value is null.
     * @throws ArgumentException if the trimmed value is empty.
     */
    public static string checkNonEmptyAfterTrim(string value, string name)
    {
        string trimmed = checkNotNull(value, name).Trim();
        return checkNonEmpty(trimmed, name);
    }

    /**
     * Resolves a possibly null int to a primitive int, using a default value.
     * @param wrapper the wrapper
     * @param defaultValue the default value
     * @return the primitive value
     */
    public static int intValue(int wrapper, int defaultValue)
    {
        return wrapper != null ? wrapper : defaultValue;
    }

    /**
     * Resolves a possibly null long to a primitive long, using a default value.
     * @param wrapper the wrapper
     * @param defaultValue the default value
     * @return the primitive value
     */
    public static long longValue(long wrapper, long defaultValue)
    {
        return wrapper != null ? wrapper : defaultValue;
    }

    public static T Null<T>() where T : class
    {
        return null;
    }
    
}