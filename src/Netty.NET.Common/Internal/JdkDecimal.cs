/*
 * Copyright 2026 The Netty Project
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
using System.Globalization;
using System.Text;

namespace Netty.NET.Common.Internal;

// CLR: renders a double or float the way Java string concatenation does (Double.toString(double) and
// Float.toString(float) as specified since JDK 19), so source messages built as "name : " + d are identical to the
// original and independent of the current culture.
//
// - .NET Core 3.0+ "R" formatting already yields the shortest decimal that round-trips, choosing the closest one.
//   On .NET 10.0.8 it does not round-trip for 2^-25 and 2^-958 (whose rounding interval is asymmetric); then the
//   shortest precision whose correctly rounded decimal, or its neighbour across the value, round-trips is used.
// - When that shortest decimal has a single digit, Java instead takes the closest decimal of length one or two that
//   still rounds to the value (e.g. Double.MIN_VALUE is "4.9E-324"); "E1" formatting is correctly rounded, so it is
//   that candidate whenever it round-trips.
// - Magnitudes in [10^-3, 10^7) use plain notation with at least one fractional digit; others use computerized
//   scientific notation "d.dddE[-]n".
internal static class JdkDecimal
{
    public static string DoubleToString(double v)
    {
        if (double.IsNaN(v))
        {
            return "NaN";
        }
        if (double.IsInfinity(v))
        {
            return v > 0 ? "Infinity" : "-Infinity";
        }
        if (v == 0)
        {
            return double.IsNegative(v) ? "-0.0" : "0.0";
        }

        double magnitude = Math.Abs(v);
        Shortest(magnitude.ToString("R", CultureInfo.InvariantCulture),
            text => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).CompareTo(magnitude),
            precision => magnitude.ToString("E" + ((precision - 1).ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture),
            out string digits, out int exponent);

        // No double lies in [10^-3, 0.001d), so comparing with the nearest doubles is exact.
        return Layout(v < 0, digits, exponent, magnitude >= 1e-3 && magnitude < 1e7);
    }

    public static string FloatToString(float v)
    {
        if (float.IsNaN(v))
        {
            return "NaN";
        }
        if (float.IsInfinity(v))
        {
            return v > 0 ? "Infinity" : "-Infinity";
        }
        if (v == 0)
        {
            return float.IsNegative(v) ? "-0.0" : "0.0";
        }

        float magnitude = Math.Abs(v);
        Shortest(magnitude.ToString("R", CultureInfo.InvariantCulture),
            text => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture).CompareTo(magnitude),
            precision => magnitude.ToString("E" + ((precision - 1).ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture),
            out string digits, out int exponent);

        // No float lies in [10^-3, 0.001f), so comparing with the nearest floats is exact.
        return Layout(v < 0, digits, exponent, magnitude >= 1e-3f && magnitude < 1e7f);
    }

    // compare(text) is the sign of the parsed text against the value, so 0 means the decimal rounds to the value;
    // scientific(p) is the value correctly rounded to p significant digits.
    private static void Shortest(string shortest, Func<string, int> compare, Func<int, string> scientific,
        out string digits, out int exponent)
    {
        if (compare(shortest) == 0)
        {
            Decompose(shortest, out digits, out exponent);
        }
        else
        {
            digits = null;
            exponent = 0;
            for (int precision = 1; digits == null; precision++)
            {
                string candidate = scientific(precision);
                int side = compare(candidate);
                if (side == 0)
                {
                    Decompose(candidate, out digits, out exponent);
                }
                else
                {
                    // The closest decimal missed the interval on one side; the next one across the value may not.
                    string neighbor = Neighbor(candidate, side < 0 ? 1 : -1);
                    if (neighbor != null && compare(neighbor) == 0)
                    {
                        Decompose(neighbor, out digits, out exponent);
                    }
                }
            }
        }

        if (digits.Length == 1)
        {
            string twoDigits = scientific(2);
            if (compare(twoDigits) == 0)
            {
                Decompose(twoDigits, out digits, out exponent);
            }
        }
    }

    // Adds delta to the last significant digit of an invariant "d.dddE[+-]n" string.
    private static string Neighbor(string scientific, int delta)
    {
        int e = scientific.IndexOf('E');
        string mantissa = scientific.Substring(0, e).Replace(".", "");
        int exp = int.Parse(scientific.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        long unscaled = long.Parse(mantissa, CultureInfo.InvariantCulture) + delta;
        if (unscaled <= 0)
        {
            return null;
        }
        return unscaled.ToString(CultureInfo.InvariantCulture) + "E"
            + (exp - (mantissa.Length - 1)).ToString(CultureInfo.InvariantCulture);
    }

    // Splits an invariant decimal string into significant digits (no leading or trailing zeros) and the decimal
    // exponent of the first digit, so the value is d1.d2d3... * 10^exponent.
    private static void Decompose(string text, out string digits, out int exponent)
    {
        int e = text.IndexOfAny(['E', 'e']);
        string mantissa = e < 0 ? text : text.Substring(0, e);
        int exp = e < 0 ? 0 : int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        int point = mantissa.IndexOf('.');
        string integerPart = point < 0 ? mantissa : mantissa.Substring(0, point);
        string all = point < 0 ? mantissa : integerPart + mantissa.Substring(point + 1);
        exp += integerPart.Length - 1;

        int start = 0;
        while (start < all.Length - 1 && all[start] == '0')
        {
            start++;
            exp--;
        }
        int end = all.Length;
        while (end > start + 1 && all[end - 1] == '0')
        {
            end--;
        }

        digits = all.Substring(start, end - start);
        exponent = exp;
    }

    private static string Layout(bool negative, string digits, int exponent, bool plain)
    {
        StringBuilder sb = new StringBuilder(digits.Length + 8);
        if (negative)
        {
            sb.Append('-');
        }

        if (plain)
        {
            if (exponent >= 0)
            {
                int integerDigits = exponent + 1;
                if (digits.Length <= integerDigits)
                {
                    sb.Append(digits).Append('0', integerDigits - digits.Length).Append(".0");
                }
                else
                {
                    sb.Append(digits, 0, integerDigits).Append('.').Append(digits, integerDigits, digits.Length - integerDigits);
                }
            }
            else
            {
                sb.Append("0.").Append('0', -exponent - 1).Append(digits);
            }
        }
        else
        {
            sb.Append(digits[0]).Append('.');
            if (digits.Length == 1)
            {
                sb.Append('0');
            }
            else
            {
                sb.Append(digits, 1, digits.Length - 1);
            }
            sb.Append('E').Append(exponent.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
