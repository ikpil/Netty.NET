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

using System;
using System.Globalization;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec;

/**
 * Converts to/from native types, general {@link Object}, and {@link CharSequence}s.
 */
// CLR: Numbers are formatted and parsed with the invariant culture: the Java digit grammar does not depend on a
// locale. Integer parsing allows only a leading sign, as Integer.parseInt does (it also accepts non-ASCII Unicode
// digits, which the BCL parsers reject). NumberFormatException is FormatException or OverflowException.
public class CharSequenceValueConverter : IValueConverter<ICharSequence>
{
    public static readonly CharSequenceValueConverter INSTANCE = new CharSequenceValueConverter();
    private static readonly AsciiString TRUE_ASCII = new AsciiString("true");

    public virtual ICharSequence ConvertObject(object value)
    {
        if (value is ICharSequence)
        {
            return (ICharSequence) value;
        }
        // CLR: Object.toString() of a boxed Java Boolean is "true"/"false" and of a boxed number is
        // locale-independent; Boolean.ToString() returns "True" or "False"
        // (https://learn.microsoft.com/dotnet/api/system.boolean.tostring) and IFormattable values use the current
        // culture unless a provider is given, so both are formatted explicitly.
        if (value is bool b)
        {
            return new StringCharSequence(b ? "true" : "false");
        }
        if (value is IFormattable formattable)
        {
            return new StringCharSequence(formattable.ToString(null, CultureInfo.InvariantCulture));
        }

        return new StringCharSequence(value.ToString());
    }

    public virtual ICharSequence ConvertInt(int value)
    {
        return new StringCharSequence(value.ToString(CultureInfo.InvariantCulture));
    }

    public virtual ICharSequence ConvertLong(long value)
    {
        return new StringCharSequence(value.ToString(CultureInfo.InvariantCulture));
    }

    // CLR: Double.toString(double) and Float.toString(float) are JdkDecimal.DoubleToString/FloatToString (Netty.NET.Common),
    // which reproduce the JDK text (1.0 is "1.0", Double.MAX_VALUE is "1.7976931348623157E308") independent of the
    // current culture; double.ToString would give "1" and "1.7976931348623157E+308".
    public virtual ICharSequence ConvertDouble(double value)
    {
        return new StringCharSequence(JdkDecimal.DoubleToString(value));
    }

    public virtual ICharSequence ConvertChar(char value)
    {
        return new StringCharSequence(value.ToString());
    }

    // CLR: String.valueOf(boolean) is "true" or "false"; Boolean.ToString() returns "True" or "False"
    // (https://learn.microsoft.com/dotnet/api/system.boolean.tostring).
    public virtual ICharSequence ConvertBoolean(bool value)
    {
        return new StringCharSequence(value ? "true" : "false");
    }

    public virtual ICharSequence ConvertFloat(float value)
    {
        return new StringCharSequence(JdkDecimal.FloatToString(value));
    }

    public virtual bool ConvertToBoolean(ICharSequence value)
    {
        return AsciiString.ContentEqualsIgnoreCase(value, TRUE_ASCII);
    }

    public virtual ICharSequence ConvertByte(sbyte value)
    {
        return new StringCharSequence(value.ToString(CultureInfo.InvariantCulture));
    }

    public virtual sbyte ConvertToByte(ICharSequence value)
    {
        if (value is AsciiString && value.Length() == 1)
        {
            // CLR: AsciiString.ByteAt returns the unsigned octet; the Java byte is that octet reinterpreted as signed.
            return unchecked((sbyte) ((AsciiString) value).ByteAt(0));
        }

        return sbyte.Parse(value.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    public virtual char ConvertToChar(ICharSequence value)
    {
        return value.CharAt(0);
    }

    public virtual ICharSequence ConvertShort(short value)
    {
        return new StringCharSequence(value.ToString(CultureInfo.InvariantCulture));
    }

    public virtual short ConvertToShort(ICharSequence value)
    {
        if (value is AsciiString)
        {
            return ((AsciiString) value).ParseShort();
        }

        return short.Parse(value.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    public virtual int ConvertToInt(ICharSequence value)
    {
        if (value is AsciiString)
        {
            return ((AsciiString) value).ParseInt();
        }

        return int.Parse(value.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    public virtual long ConvertToLong(ICharSequence value)
    {
        if (value is AsciiString)
        {
            return ((AsciiString) value).ParseLong();
        }

        return long.Parse(value.ToString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    // CLR: new Date(long) is DateTimeOffset.FromUnixTimeMilliseconds, which throws ArgumentOutOfRangeException
    // outside years 0001 to 9999 (https://learn.microsoft.com/dotnet/api/system.datetimeoffset.fromunixtimemilliseconds),
    // where a Java Date accepts any long.
    public virtual ICharSequence ConvertTimeMillis(long value)
    {
        return new StringCharSequence(DateFormatter.Format(DateTimeOffset.FromUnixTimeMilliseconds(value)));
    }

    public virtual long ConvertToTimeMillis(ICharSequence value)
    {
        DateTimeOffset? date = DateFormatter.ParseHttpDate(value);
        if (date == null)
        {
            // CLR: PlatformDependent.throwException rethrows the checked java.text.ParseException unchecked; it is
            // FormatException here (ParseException's error offset 0 has no counterpart), thrown directly, so the
            // unreachable "return 0;" after it is omitted.
            throw new FormatException("header can't be parsed into a Date: " + value);
        }

        return date.Value.ToUnixTimeMilliseconds();
    }

    // CLR: Float.parseFloat/Double.parseDouble map to the BCL float grammar with the invariant culture; the Java-only
    // forms (hexadecimal literals, f/F/d/D suffixes) are accepted only on the AsciiString path, whose parser keeps the
    // Java grammar.
    public virtual float ConvertToFloat(ICharSequence value)
    {
        if (value is AsciiString)
        {
            return ((AsciiString) value).ParseFloat();
        }

        return float.Parse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public virtual double ConvertToDouble(ICharSequence value)
    {
        if (value is AsciiString)
        {
            return ((AsciiString) value).ParseDouble();
        }

        return double.Parse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
