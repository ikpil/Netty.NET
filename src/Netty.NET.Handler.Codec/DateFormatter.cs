/*
 * Copyright 2016 The Netty Project
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
using System.Collections;
using System.Globalization;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec;

/**
 * A formatter for HTTP header dates, such as "Expires" and "Date" headers, or "expires" field in "Set-Cookie".
 *
 * On the parsing side, it honors RFC6265 (so it supports RFC1123).
 * Note that:
 * <ul>
 *     <li>Day of week is ignored and not validated</li>
 *     <li>Timezone is ignored, as RFC6265 assumes UTC</li>
 * </ul>
 * If you're looking for a date format that validates day of week, or supports other timezones, consider using
 * java.util.DateTimeFormatter.RFC_1123_DATE_TIME.
 *
 * On the formatting side, it uses a subset of RFC1123 (2 digit day-of-month and 4 digit year) as per RFC2616.
 * This subset supports RFC6265.
 *
 * @see <a href="https://tools.ietf.org/html/rfc6265#section-5.1.1">RFC6265</a> for the parsing side
 * @see <a href="https://tools.ietf.org/html/rfc1123#page-55">RFC1123</a> and
 * <a href="https://tools.ietf.org/html/rfc2616#section-3.3.1">RFC2616</a> for the encoding side.
 */
// CLR: java.util.Date, an instant with millisecond precision, is DateTimeOffset, which "always unambiguously
// identifies a single point in time" (https://learn.microsoft.com/dotnet/standard/datetime/choosing-between-datetime);
// parsed values carry Offset TimeSpan.Zero (UTC) and a null Date result is DateTimeOffset?. The UTC GregorianCalendar
// that converted between fields and instants is replaced by DateTimeOffset construction and its UtcDateTime fields;
// the RFC6265 token parser below is unchanged.
public sealed class DateFormatter
{
    // CLR: BitSet is BitArray sized to the ASCII range that holds every delimiter (see isDelim).
    private static readonly BitArray DELIMITERS = new BitArray(0x80);
    static DateFormatter()
    {
        DELIMITERS.Set(0x09, true);

        for (char c = (char) 0x20; c <= 0x2F; c++)
        {
            DELIMITERS.Set(c, true);
        }

        for (char c = (char) 0x3B; c <= 0x40; c++)
        {
            DELIMITERS.Set(c, true);
        }

        for (char c = (char) 0x5B; c <= 0x60; c++)
        {
            DELIMITERS.Set(c, true);
        }

        for (char c = (char) 0x7B; c <= 0x7E; c++)
        {
            DELIMITERS.Set(c, true);
        }
    }

    private static readonly string[] DAY_OF_WEEK_TO_SHORT_NAME =
            new string[]{"Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"};

    private static readonly string[] CALENDAR_MONTH_TO_SHORT_NAME =
            new string[]{"Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"};

    private static readonly FastThreadLocal<DateFormatter> INSTANCES = new DateFormatterThreadLocal();

    private sealed class DateFormatterThreadLocal : FastThreadLocal<DateFormatter>
    {
        protected override DateFormatter InitialValue()
        {
            return new DateFormatter();
        }
    }

    /**
     * Parse some text into a {@link Date}, according to RFC6265
     * @param txt text to parse
     * @return a {@link Date}, or null if text couldn't be parsed
     */
    public static DateTimeOffset? ParseHttpDate(ICharSequence txt)
    {
        return ParseHttpDate(txt, 0, txt.Length());
    }

    /**
     * Parse some text into a {@link Date}, according to RFC6265
     * @param txt text to parse
     * @param start the start index inside {@code txt}
     * @param end the end index inside {@code txt}
     * @return a {@link Date}, or null if text couldn't be parsed
     */
    public static DateTimeOffset? ParseHttpDate(ICharSequence txt, int start, int end)
    {
        int length = end - start;
        if (length == 0)
        {
            return null;
        }
        else if (length < 0)
        {
            throw new ArgumentException("Can't have end < start");
        }
        else if (length > 64)
        {
            throw new ArgumentException("Can't parse more than 64 chars, " +
                    "looks like a user error or a malformed header");
        }

        return Formatter().Parse0(ObjectUtil.CheckNotNull(txt, "txt"), start, end);
    }

    /**
     * Format a {@link Date} into RFC1123 format
     * @param date the date to format
     * @return a RFC1123 string
     */
    // CLR: DateTimeOffset is a value type and cannot be null, so checkNotNull(date, "date") has no counterpart.
    public static string Format(DateTimeOffset date)
    {
        return Formatter().Format0(date);
    }

    /**
     * Append a {@link Date} to a {@link StringBuilder} into RFC1123 format
     * @param date the date to format
     * @param sb the StringBuilder
     * @return the same StringBuilder
     */
    // CLR: as in format(Date), the value-type date needs no null check.
    public static StringBuilder Append(DateTimeOffset date, StringBuilder sb)
    {
        return Formatter().Append0(date, ObjectUtil.CheckNotNull(sb, "sb"));
    }

    private static DateFormatter Formatter()
    {
        DateFormatter formatter = INSTANCES.Get();
        formatter.Reset();
        return formatter;
    }

    // delimiter = %x09 / %x20-2F / %x3B-40 / %x5B-60 / %x7B-7E
    private static bool IsDelim(char c)
    {
        // CLR: BitSet.get(int) is false for an index past the set's bits
        // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/BitSet.html#get(int)), whereas
        // BitArray.Get(int) throws ArgumentOutOfRangeException when the index is not less than its Count
        // (https://learn.microsoft.com/dotnet/api/system.collections.bitarray.get), so the length is checked first.
        return c < DELIMITERS.Length && DELIMITERS.Get(c);
    }

    private static bool IsDigit(char c)
    {
        return c >= 48 && c <= 57;
    }

    private static int GetNumericalValue(char c)
    {
        return c - 48;
    }

    // CLR: the GregorianCalendar cal field has no counterpart; computeDate() and append0() use DateTimeOffset.
    private readonly StringBuilder sb = new StringBuilder(29); // Sun, 27 Nov 2016 19:37:15 GMT
    private bool timeFound;
    private int hours;
    private int minutes;
    private int seconds;
    private bool dayOfMonthFound;
    private int dayOfMonth;
    private bool monthFound;
    private int month;
    private bool yearFound;
    private int year;

    private DateFormatter()
    {
        Reset();
    }

    public void Reset()
    {
        timeFound = false;
        hours = -1;
        minutes = -1;
        seconds = -1;
        dayOfMonthFound = false;
        dayOfMonth = -1;
        monthFound = false;
        month = -1;
        yearFound = false;
        year = -1;
        // CLR: cal.clear() has no counterpart; computeDate() builds a new value whose milliseconds are 0.
        sb.Length = 0;
    }

    private bool TryParseTime(ICharSequence txt, int tokenStart, int tokenEnd)
    {
        int len = tokenEnd - tokenStart;

        // h:m:s to hh:mm:ss
        if (len < 5 || len > 8)
        {
            return false;
        }

        int localHours = -1;
        int localMinutes = -1;
        int localSeconds = -1;
        int currentPartNumber = 0;
        int currentPartValue = 0;
        int numDigits = 0;

        for (int i = tokenStart; i < tokenEnd; i++)
        {
            char c = txt.CharAt(i);
            if (IsDigit(c))
            {
                currentPartValue = currentPartValue * 10 + GetNumericalValue(c);
                if (++numDigits > 2)
                {
                  return false; // too many digits in this part
                }
            }
            else if (c == ':')
            {
                if (numDigits == 0)
                {
                    // no digits between separators
                    return false;
                }

                switch (currentPartNumber)
                {
                    case 0:
                        // flushing hours
                        localHours = currentPartValue;
                        break;
                    case 1:
                        // flushing minutes
                        localMinutes = currentPartValue;
                        break;
                    default:
                        // invalid, too many :
                        return false;
                }

                currentPartValue = 0;
                currentPartNumber++;
                numDigits = 0;
            }
            else
            {
                // invalid char
                return false;
            }
        }

        if (numDigits > 0)
        {
            // pending seconds
            localSeconds = currentPartValue;
        }

        if (localHours >= 0 && localMinutes >= 0 && localSeconds >= 0)
        {
            hours = localHours;
            minutes = localMinutes;
            seconds = localSeconds;
            return true;
        }

        return false;
    }

    private bool TryParseDayOfMonth(ICharSequence txt, int tokenStart, int tokenEnd)
    {
        int len = tokenEnd - tokenStart;

        if (len == 1)
        {
            char c0 = txt.CharAt(tokenStart);
            if (IsDigit(c0))
            {
                dayOfMonth = GetNumericalValue(c0);
                return true;
            }

        }
        else if (len == 2)
        {
            char c0 = txt.CharAt(tokenStart);
            char c1 = txt.CharAt(tokenStart + 1);
            if (IsDigit(c0) && IsDigit(c1))
            {
                dayOfMonth = GetNumericalValue(c0) * 10 + GetNumericalValue(c1);
                return true;
            }
        }

        return false;
    }

    private bool TryParseMonth(ICharSequence txt, int tokenStart, int tokenEnd)
    {
        int len = tokenEnd - tokenStart;

        if (len != 3)
        {
            return false;
        }

        char monthChar1 = AsciiString.ToLowerCase(txt.CharAt(tokenStart));
        char monthChar2 = AsciiString.ToLowerCase(txt.CharAt(tokenStart + 1));
        char monthChar3 = AsciiString.ToLowerCase(txt.CharAt(tokenStart + 2));

        // CLR: Calendar.JANUARY to Calendar.DECEMBER are 0 to 11 ("The first month of the year in the Gregorian and
        // Julian calendars is JANUARY which is 0",
        // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Calendar.html); the month is kept
        // as the DateTimeOffset month 1 to 12 instead.
        if (monthChar1 == 'j' && monthChar2 == 'a' && monthChar3 == 'n')
        {
            month = 1;
        }
        else if (monthChar1 == 'f' && monthChar2 == 'e' && monthChar3 == 'b')
        {
            month = 2;
        }
        else if (monthChar1 == 'm' && monthChar2 == 'a' && monthChar3 == 'r')
        {
            month = 3;
        }
        else if (monthChar1 == 'a' && monthChar2 == 'p' && monthChar3 == 'r')
        {
            month = 4;
        }
        else if (monthChar1 == 'm' && monthChar2 == 'a' && monthChar3 == 'y')
        {
            month = 5;
        }
        else if (monthChar1 == 'j' && monthChar2 == 'u' && monthChar3 == 'n')
        {
            month = 6;
        }
        else if (monthChar1 == 'j' && monthChar2 == 'u' && monthChar3 == 'l')
        {
            month = 7;
        }
        else if (monthChar1 == 'a' && monthChar2 == 'u' && monthChar3 == 'g')
        {
            month = 8;
        }
        else if (monthChar1 == 's' && monthChar2 == 'e' && monthChar3 == 'p')
        {
            month = 9;
        }
        else if (monthChar1 == 'o' && monthChar2 == 'c' && monthChar3 == 't')
        {
            month = 10;
        }
        else if (monthChar1 == 'n' && monthChar2 == 'o' && monthChar3 == 'v')
        {
            month = 11;
        }
        else if (monthChar1 == 'd' && monthChar2 == 'e' && monthChar3 == 'c')
        {
            month = 12;
        }
        else
        {
            return false;
        }

        return true;
    }

    private bool TryParseYear(ICharSequence txt, int tokenStart, int tokenEnd)
    {
        int len = tokenEnd - tokenStart;

        if (len == 2)
        {
            char c0 = txt.CharAt(tokenStart);
            char c1 = txt.CharAt(tokenStart + 1);
            if (IsDigit(c0) && IsDigit(c1))
            {
                year = GetNumericalValue(c0) * 10 + GetNumericalValue(c1);
                return true;
            }

        }
        else if (len == 4)
        {
            char c0 = txt.CharAt(tokenStart);
            char c1 = txt.CharAt(tokenStart + 1);
            char c2 = txt.CharAt(tokenStart + 2);
            char c3 = txt.CharAt(tokenStart + 3);
            if (IsDigit(c0) && IsDigit(c1) && IsDigit(c2) && IsDigit(c3))
            {
                year = GetNumericalValue(c0) * 1000 +
                        GetNumericalValue(c1) * 100 +
                        GetNumericalValue(c2) * 10 +
                        GetNumericalValue(c3);
                return true;
            }
        }

        return false;
    }

    private bool ParseToken(ICharSequence txt, int tokenStart, int tokenEnd)
    {
        // return true if all parts are found
        if (!timeFound)
        {
            timeFound = TryParseTime(txt, tokenStart, tokenEnd);
            if (timeFound)
            {
                return dayOfMonthFound && monthFound && yearFound;
            }
        }

        if (!dayOfMonthFound)
        {
            dayOfMonthFound = TryParseDayOfMonth(txt, tokenStart, tokenEnd);
            if (dayOfMonthFound)
            {
                return timeFound && monthFound && yearFound;
            }
        }

        if (!monthFound)
        {
            monthFound = TryParseMonth(txt, tokenStart, tokenEnd);
            if (monthFound)
            {
                return timeFound && dayOfMonthFound && yearFound;
            }
        }

        if (!yearFound)
        {
            yearFound = TryParseYear(txt, tokenStart, tokenEnd);
        }

        return timeFound && dayOfMonthFound && monthFound && yearFound;
    }

    private DateTimeOffset? Parse0(ICharSequence txt, int start, int end)
    {
        bool allPartsFound = Parse1(txt, start, end);
        return allPartsFound && NormalizeAndValidate() ? ComputeDate() : null;
    }

    private bool Parse1(ICharSequence txt, int start, int end)
    {
        // return true if all parts are found
        int tokenStart = -1;

        for (int i = start; i < end; i++)
        {
            char c = txt.CharAt(i);

            if (IsDelim(c))
            {
                if (tokenStart != -1)
                {
                    // terminate token
                    if (ParseToken(txt, tokenStart, i))
                    {
                        return true;
                    }

                    tokenStart = -1;
                }
            }
            else if (tokenStart == -1)
            {
                // start new token
                tokenStart = i;
            }
        }

        // terminate trailing token at end, not txt.length(), so a substring parse doesn't overrun
        return tokenStart != -1 && ParseToken(txt, tokenStart, end);
    }

    private bool NormalizeAndValidate()
    {
        if (dayOfMonth < 1
                || dayOfMonth > 31
                || hours > 23
                || minutes > 59
                || seconds > 59)
        {
            return false;
        }

        if (year >= 70 && year <= 99)
        {
            year += 1900;
        }
        else if (year >= 0 && year < 70)
        {
            year += 2000;
        }
        else if (year < 1601)
        {
            // invalid value
            return false;
        }

        return true;
    }

    private DateTimeOffset ComputeDate()
    {
        // CLR: the cleared UTC GregorianCalendar is lenient, so a day past the end of the month rolls into the next
        // month ("a lenient GregorianCalendar interprets MONTH == JANUARY, DAY_OF_MONTH == 32 as February 1",
        // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Calendar.html). The DateTimeOffset
        // constructor throws when "day is less than one or greater than the number of days in month"
        // (https://learn.microsoft.com/dotnet/api/system.datetimeoffset.-ctor), so the day is added to the first of
        // the month. Years are 1601 to 9999 here, inside both the Gregorian cutover and the DateTimeOffset range.
        return new DateTimeOffset(year, month, 1, hours, minutes, seconds, TimeSpan.Zero).AddDays(dayOfMonth - 1);
    }

    private string Format0(DateTimeOffset date)
    {
        Append0(date, sb);

        return sb.ToString();
    }

    private StringBuilder Append0(DateTimeOffset date, StringBuilder sb)
    {
        // CLR: cal.setTime(date) on the UTC calendar becomes the UTC DateTime of the instant. DayOfWeek is Sunday = 0
        // to Saturday = 6 (https://learn.microsoft.com/dotnet/api/system.dayofweek), where Calendar.DAY_OF_WEEK - 1
        // indexed the same table; DateTime.Month is 1 to 12 where Calendar.MONTH was 0 to 11. Numbers are appended
        // with the invariant culture.
        DateTime cal = date.UtcDateTime;

        sb.Append(DAY_OF_WEEK_TO_SHORT_NAME[(int) cal.DayOfWeek]).Append(", ");
        AppendZeroLeftPadded(cal.Day, sb).Append(' ');
        sb.Append(CALENDAR_MONTH_TO_SHORT_NAME[cal.Month - 1]).Append(' ');
        sb.Append(cal.Year.ToString(CultureInfo.InvariantCulture)).Append(' ');
        AppendZeroLeftPadded(cal.Hour, sb).Append(':');
        AppendZeroLeftPadded(cal.Minute, sb).Append(':');

        return AppendZeroLeftPadded(cal.Second, sb).Append(" GMT");
    }

    private static StringBuilder AppendZeroLeftPadded(int value, StringBuilder sb)
    {
        if (value < 10)
        {
            sb.Append('0');
        }

        return sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }
}
