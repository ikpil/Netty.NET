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
using System.Text;
using Netty.NET.Common;
using static Netty.NET.Handler.Codec.DateFormatter;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: java.util.Date is DateTimeOffset (new Date(long) is DateTimeOffset.FromUnixTimeMilliseconds) and a null Date
// is a null DateTimeOffset?. parseHttpDate takes an ICharSequence, so each Java String literal is wrapped as a
// StringCharSequence.
public class DateFormatterTest
{
    /**
     * This date is set at "06 Nov 1994 08:49:37 GMT", from
     * <a href="https://www.w3.org/Protocols/rfc2616/rfc2616-sec3.html">examples in RFC documentation</a>
     */
    private const long TIMESTAMP = 784111777000L;
    private static readonly DateTimeOffset DATE = DateTimeOffset.FromUnixTimeMilliseconds(TIMESTAMP);

    [Fact]
    public void TestParseWithSingleDigitDay()
    {
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence("Sun, 6 Nov 1994 08:49:37 GMT")));
    }

    [Fact]
    public void TestParseHttpDateSubstringDoesNotOverrunEnd()
    {
        // Set-Cookie header format with the date anywhere but last (RFC 6265 tokens are unordered).
        string dateText = "Sun 08:49:37 06 Nov 1994";
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence(dateText)));
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence(dateText + "; Path=/"), 0, dateText.Length));
    }

    [Fact]
    public void TestParseWithDoubleDigitDay()
    {
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 08:49:37 GMT")));
    }

    [Fact]
    public void TestParseWithDashSeparatorSingleDigitDay()
    {
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence("Sunday, 6-Nov-94 08:49:37 GMT")));
    }

    [Fact]
    public void TestParseWithDashSeparatorDoubleDigitDay()
    {
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence("Sunday, 06-Nov-94 08:49:37 GMT")));
    }

    [Fact]
    public void TestParseWithoutGMT()
    {
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence("Sun Nov 06 08:49:37 1994")));
    }

    [Fact]
    public void TestParseWithFunkyTimezone()
    {
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence("Sun Nov 06 08:49:37 1994 -0000")));
    }

    [Fact]
    public void TestParseWithSingleDigitHourMinutesAndSecond()
    {
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence("Sunday, 06-Nov-94 8:49:37 GMT")));
    }

    [Fact]
    public void TestParseWithSingleDigitTime()
    {
        Assert.Equal(DATE, ParseHttpDate(new StringCharSequence("Sunday, 06 Nov 1994 8:49:37 GMT")));

        DateTimeOffset _08_09_37 = DateTimeOffset.FromUnixTimeMilliseconds(TIMESTAMP - 40 * 60 * 1000);
        Assert.Equal(_08_09_37, ParseHttpDate(new StringCharSequence("Sunday, 06 Nov 1994 8:9:37 GMT")));
        Assert.Equal(_08_09_37, ParseHttpDate(new StringCharSequence("Sunday, 06 Nov 1994 8:09:37 GMT")));

        DateTimeOffset _08_09_07 = DateTimeOffset.FromUnixTimeMilliseconds(TIMESTAMP - (40 * 60 + 30) * 1000);
        Assert.Equal(_08_09_07, ParseHttpDate(new StringCharSequence("Sunday, 06 Nov 1994 8:9:7 GMT")));
        Assert.Equal(_08_09_07, ParseHttpDate(new StringCharSequence("Sunday, 06 Nov 1994 8:9:07 GMT")));
    }

    [Fact]
    public void TestParseMidnight()
    {
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(784080000000L),
                ParseHttpDate(new StringCharSequence("Sunday, 06 Nov 1994 00:00:00 GMT")));
    }

    [Fact]
    public void TestParseInvalidInput()
    {
        // missing field
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, Nov 1994 08:49:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 1994 08:49:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 08:49:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 :49:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 49:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 08::37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 08:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 08:49: GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 08:49 GMT")));
        //invalid value
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 FOO 1994 08:49:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 36 Nov 1994 08:49:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 28:49:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 08:69:37 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sun, 06 Nov 1994 08:49:67 GMT")));
        //wrong number of digits in timestamp
        Assert.Null(ParseHttpDate(new StringCharSequence("Sunday, 06 Nov 1994 0:0:000 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sunday, 06 Nov 1994 0:000:0 GMT")));
        Assert.Null(ParseHttpDate(new StringCharSequence("Sunday, 06 Nov 1994 000:0:0 GMT")));
    }

    [Fact]
    public void TestFormat()
    {
        Assert.Equal("Sun, 06 Nov 1994 08:49:37 GMT", Format(DATE));
    }

    [Fact]
    public void TestAppend()
    {
        StringBuilder sb = new StringBuilder();
        Append(DATE, sb);
        Assert.Equal("Sun, 06 Nov 1994 08:49:37 GMT", sb.ToString());
    }

    // CLR: Calendar.JANUARY to Calendar.DECEMBER are 0 to 11; getMonth returns the DateTimeOffset month, 1 to 12.
    [Fact]
    public void TestParseAllMonths()
    {
        Assert.Equal(1, GetMonth(ParseHttpDate(new StringCharSequence("Sun, 06 Jan 1994 08:49:37 GMT"))));
        Assert.Equal(2, GetMonth(ParseHttpDate(new StringCharSequence("Sun, 06 Feb 1994 08:49:37 GMT"))));
        Assert.Equal(3, GetMonth(ParseHttpDate(new StringCharSequence("Sun, 06 Mar 1994 08:49:37 GMT"))));
        Assert.Equal(4, GetMonth(ParseHttpDate(new StringCharSequence("Sun, 06 Apr 1994 08:49:37 GMT"))));
        Assert.Equal(5, GetMonth(ParseHttpDate(new StringCharSequence("Sun, 06 May 1994 08:49:37 GMT"))));
        Assert.Equal(6, GetMonth(ParseHttpDate(new StringCharSequence("Sun, 06 Jun 1994 08:49:37 GMT"))));
        Assert.Equal(7, GetMonth(ParseHttpDate(new StringCharSequence("Sun, 06 Jul 1994 08:49:37 GMT"))));
        Assert.Equal(8, GetMonth(ParseHttpDate(new StringCharSequence("Sun, 06 Aug 1994 08:49:37 GMT"))));
        Assert.Equal(9, GetMonth(ParseHttpDate(new StringCharSequence("Sun, 06 Sep 1994 08:49:37 GMT"))));
        Assert.Equal(10, GetMonth(ParseHttpDate(new StringCharSequence("Sun Oct 06 08:49:37 1994"))));
        Assert.Equal(11, GetMonth(ParseHttpDate(new StringCharSequence("Sun Nov 06 08:49:37 1994"))));
        Assert.Equal(12, GetMonth(ParseHttpDate(new StringCharSequence("Sun Dec 06 08:49:37 1994"))));
    }

    // CLR: Calendar.getInstance() uses the default time zone, which is TimeZoneInfo.Local.
    private static int GetMonth(DateTimeOffset? referenceDate)
    {
        DateTimeOffset cal = TimeZoneInfo.ConvertTime(referenceDate.Value, TimeZoneInfo.Local);
        return cal.Month;
    }
}
