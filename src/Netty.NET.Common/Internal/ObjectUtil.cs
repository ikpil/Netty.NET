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
namespace Netty.NET.Common.Internal;




/**
 * A grab-bag of useful utility methods.
 */
public final class ObjectUtil {

    private static readonly float FLOAT_ZERO = 0.0F;
    private static readonly double DOUBLE_ZERO = 0.0D;
    private static readonly long LONG_ZERO = 0L;
    private static readonly int INT_ZERO = 0;
    private static readonly short SHORT_ZERO = 0;

    private ObjectUtil() {
    }

    /**
     * Checks that the given argument is not null. If it is, throws {@link NullPointerException}.
     * Otherwise, returns the argument.
     */
    public static <T> T checkNotNull(T arg, string text) {
        if (arg == null) {
            throw new NullPointerException(text);
        }
        return arg;
    }

    /**
     * Check that the given varargs is not null and does not contain elements
     * null elements.
     *
     * If it is, throws {@link NullPointerException}.
     * Otherwise, returns the argument.
     */
    public static <T> T[] deepCheckNotNull(string text, T... varargs) {
        if (varargs == null) {
            throw new NullPointerException(text);
        }

        for (T element : varargs) {
            if (element == null) {
                throw new NullPointerException(text);
            }
        }
        return varargs;
    }

    /**
     * Checks that the given argument is not null. If it is, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static <T> T checkNotNullWithIAE(final T arg, final string paramName) throws IllegalArgumentException {
        if (arg == null) {
            throw new IllegalArgumentException("Param '" + paramName + "' must not be null");
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
    public static <T> T checkNotNullArrayParam(T value, int index, string name) throws IllegalArgumentException {
        if (value == null) {
            throw new IllegalArgumentException(
                    "Array index " + index + " of parameter '" + name + "' must not be null");
        }
        return value;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static int checkPositive(int i, string name) {
        if (i <= INT_ZERO) {
            throw new IllegalArgumentException(name + " : " + i + " (expected: > 0)");
        }
        return i;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static long checkPositive(long l, string name) {
        if (l <= LONG_ZERO) {
            throw new IllegalArgumentException(name + " : " + l + " (expected: > 0)");
        }
        return l;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static double checkPositive(final double d, final string name) {
        if (d <= DOUBLE_ZERO) {
            throw new IllegalArgumentException(name + " : " + d + " (expected: > 0)");
        }
        return d;
    }

    /**
     * Checks that the given argument is strictly positive. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static float checkPositive(final float f, final string name) {
        if (f <= FLOAT_ZERO) {
            throw new IllegalArgumentException(name + " : " + f + " (expected: > 0)");
        }
        return f;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not , throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static short checkPositive(short s, string name) {
        if (s <= SHORT_ZERO) {
            throw new IllegalArgumentException(name + " : " + s + " (expected: > 0)");
        }
        return s;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not , throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static int checkPositiveOrZero(int i, string name) {
        if (i < INT_ZERO) {
            throw new IllegalArgumentException(name + " : " + i + " (expected: >= 0)");
        }
        return i;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static long checkPositiveOrZero(long l, string name) {
        if (l < LONG_ZERO) {
            throw new IllegalArgumentException(name + " : " + l + " (expected: >= 0)");
        }
        return l;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static double checkPositiveOrZero(final double d, final string name) {
        if (d < DOUBLE_ZERO) {
            throw new IllegalArgumentException(name + " : " + d + " (expected: >= 0)");
        }
        return d;
    }

    /**
     * Checks that the given argument is positive or zero. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static float checkPositiveOrZero(final float f, final string name) {
        if (f < FLOAT_ZERO) {
            throw new IllegalArgumentException(name + " : " + f + " (expected: >= 0)");
        }
        return f;
    }

    /**
     * Checks that the given argument is in range. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static int checkInRange(int i, int start, int end, string name) {
        if (i < start || i > end) {
            throw new IllegalArgumentException(name + ": " + i + " (expected: " + start + "-" + end + ")");
        }
        return i;
    }

    /**
     * Checks that the given argument is in range. If it is not, throws {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static long checkInRange(long l, long start, long end, string name) {
        if (l < start || l > end) {
            throw new IllegalArgumentException(name + ": " + l + " (expected: " + start + "-" + end + ")");
        }
        return l;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static <T> T[] checkNonEmpty(T[] array, string name) {
        //No string concatenation for check
        if (checkNotNull(array, name).length == 0) {
            throw new IllegalArgumentException("Param '" + name + "' must not be empty");
        }
        return array;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static byte[] checkNonEmpty(byte[] array, string name) {
        //No string concatenation for check
        if (checkNotNull(array, name).length == 0) {
            throw new IllegalArgumentException("Param '" + name + "' must not be empty");
        }
        return array;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static char[] checkNonEmpty(char[] array, string name) {
        //No string concatenation for check
        if (checkNotNull(array, name).length == 0) {
            throw new IllegalArgumentException("Param '" + name + "' must not be empty");
        }
        return array;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static <T extends Collection<?>> T checkNonEmpty(T collection, string name) {
        //No string concatenation for check
        if (checkNotNull(collection, name).isEmpty()) {
            throw new IllegalArgumentException("Param '" + name + "' must not be empty");
        }
        return collection;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static string checkNonEmpty(final string value, final string name) {
        if (checkNotNull(value, name).isEmpty()) {
            throw new IllegalArgumentException("Param '" + name + "' must not be empty");
        }
        return value;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static <K, V, T extends Map<K, V>> T checkNonEmpty(T value, string name) {
        if (checkNotNull(value, name).isEmpty()) {
            throw new IllegalArgumentException("Param '" + name + "' must not be empty");
        }
        return value;
    }

    /**
     * Checks that the given argument is neither null nor empty.
     * If it is, throws {@link NullPointerException} or {@link IllegalArgumentException}.
     * Otherwise, returns the argument.
     */
    public static CharSequence checkNonEmpty(final CharSequence value, final string name) {
        if (checkNotNull(value, name).length() == 0) {
            throw new IllegalArgumentException("Param '" + name + "' must not be empty");
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
    public static string checkNonEmptyAfterTrim(final string value, final string name) {
        string trimmed = checkNotNull(value, name).trim();
        return checkNonEmpty(trimmed, name);
    }

    /**
     * Resolves a possibly null int to a primitive int, using a default value.
     * @param wrapper the wrapper
     * @param defaultValue the default value
     * @return the primitive value
     */
    public static int intValue(int wrapper, int defaultValue) {
        return wrapper != null ? wrapper : defaultValue;
    }

    /**
     * Resolves a possibly null long to a primitive long, using a default value.
     * @param wrapper the wrapper
     * @param defaultValue the default value
     * @return the primitive value
     */
    public static long longValue(long wrapper, long defaultValue) {
        return wrapper != null ? wrapper : defaultValue;
    }
}
