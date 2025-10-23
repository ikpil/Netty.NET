/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.StringUtil;

namespace Netty.NET.Common.Tests.Internal;

public class StringUtilTest
{
    private class TestClass
    {
    }

    [Fact]
    public void ensureNewlineExists()
    {
        Assert.NotNull(NEWLINE);
    }

    [Fact]
    public void testToHexString()
    {
        Assert.Equal("0", toHexString(new byte[] { 0 }));
        Assert.Equal("1", toHexString(new byte[] { 1 }));
        Assert.Equal("0", toHexString(new byte[] { 0, 0 }));
        Assert.Equal("100", toHexString(new byte[] { 1, 0 }));
        Assert.Equal("", toHexString(EmptyArrays.EMPTY_BYTES));
    }

    [Fact]
    public void testToHexStringPadded()
    {
        Assert.Equal("00", toHexStringPadded(new byte[] { 0 }));
        Assert.Equal("01", toHexStringPadded(new byte[] { 1 }));
        Assert.Equal("0000", toHexStringPadded(new byte[] { 0, 0 }));
        Assert.Equal("0100", toHexStringPadded(new byte[] { 1, 0 }));
        Assert.Equal("", toHexStringPadded(EmptyArrays.EMPTY_BYTES));
    }

    [Fact]
    public void splitSimple()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo:bar".Split(":"));
    }

    [Fact]
    public void splitWithTrailingDelimiter()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo,bar,".Split(","));
    }

    [Fact]
    public void splitWithTrailingDelimiters()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo!bar!!".Split("!"));
    }

    [Fact]
    public void splitWithTrailingDelimitersDot()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo.bar..".Split("\\."));
    }

    [Fact]
    public void splitWithTrailingDelimitersEq()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo=bar==".Split("="));
    }

    [Fact]
    public void splitWithTrailingDelimitersSpace()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo bar  ".Split(" "));
    }

    [Fact]
    public void splitWithConsecutiveDelimiters()
    {
        Assert.Equal(new string[] { "foo", "", "bar" }, "foo$$bar".Split("\\$"));
    }

    [Fact]
    public void splitWithDelimiterAtBeginning()
    {
        Assert.Equal(new string[] { "", "foo", "bar" }, "#foo#bar".Split("#"));
    }

    [Fact]
    public void splitMaxPart()
    {
        Assert.Equal(new string[] { "foo", "bar:bar2" }, "foo:bar:bar2".Split(":", 2));
        Assert.Equal(new string[] { "foo", "bar", "bar2" }, "foo:bar:bar2".Split(":", 3));
    }

    [Fact]
    public void substringAfterTest()
    {
        Assert.Equal("bar:bar2", substringAfter("foo:bar:bar2", ':'));
    }

    [Fact]
    public void commonSuffixOfLengthTest()
    {
        // negative length suffixes are never common
        checkNotCommonSuffix("abc", "abc", -1);

        // null has no suffix
        checkNotCommonSuffix("abc", null, 0);
        checkNotCommonSuffix(null, null, 0);

        // any non-null string has 0-length suffix
        checkCommonSuffix("abc", "xx", 0);

        checkCommonSuffix("abc", "abc", 0);
        checkCommonSuffix("abc", "abc", 1);
        checkCommonSuffix("abc", "abc", 2);
        checkCommonSuffix("abc", "abc", 3);
        checkNotCommonSuffix("abc", "abc", 4);

        checkCommonSuffix("abcd", "cd", 1);
        checkCommonSuffix("abcd", "cd", 2);
        checkNotCommonSuffix("abcd", "cd", 3);

        checkCommonSuffix("abcd", "axcd", 1);
        checkCommonSuffix("abcd", "axcd", 2);
        checkNotCommonSuffix("abcd", "axcd", 3);

        checkNotCommonSuffix("abcx", "abcy", 1);
    }

    private static void checkNotCommonSuffix(string s, string p, int len)
    {
        Assert.False(checkCommonSuffixSymmetric(s, p, len));
    }

    private static void checkCommonSuffix(string s, string p, int len)
    {
        Assert.True(checkCommonSuffixSymmetric(s, p, len));
    }

    private static bool checkCommonSuffixSymmetric(string s, string p, int len)
    {
        bool sp = commonSuffixOfLength(s, p, len);
        bool ps = commonSuffixOfLength(p, s, len);
        Assert.Equal(sp, ps);
        return sp;
    }

    [Fact]
    public void escapeCsvNull()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            StringUtil.escapeCsv(null);
        });
    }

    [Fact]
    public void escapeCsvEmpty()
    {
        string value = "";
        escapeCsv(value, value);
    }

    [Fact]
    public void escapeCsvUnquoted()
    {
        string value = "something";
        escapeCsv(value, value);
    }

    [Fact]
    public void escapeCsvAlreadyQuoted()
    {
        string value = "\"something\"";
        string expected = "\"something\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuote()
    {
        string value = "s\"";
        string expected = "\"s\"\"\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuoteInMiddle()
    {
        string value = "some text\"and more text";
        string expected = "\"some text\"\"and more text\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuoteInMiddleAlreadyQuoted()
    {
        string value = "\"some text\"and more text\"";
        string expected = "\"some text\"\"and more text\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuotedWords()
    {
        string value = "\"foo\"\"goo\"";
        string expected = "\"foo\"\"goo\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithAlreadyEscapedQuote()
    {
        string value = "foo\"\"goo";
        string expected = "foo\"\"goo";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvEndingWithQuote()
    {
        string value = "some\"";
        string expected = "\"some\"\"\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleQuote()
    {
        string value = "\"";
        string expected = "\"\"\"\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleQuoteAndCharacter()
    {
        string value = "\"f";
        string expected = "\"\"\"f\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvAlreadyEscapedQuote()
    {
        string value = "\"some\"\"";
        string expected = "\"some\"\"\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvQuoted()
    {
        string value = "\"foo,goo\"";
        escapeCsv(value, value);
    }

    [Fact]
    public void escapeCsvWithLineFeed()
    {
        string value = "some text\n more text";
        string expected = "\"some text\n more text\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleLineFeedCharacter()
    {
        string value = "\n";
        string expected = "\"\n\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithMultipleLineFeedCharacter()
    {
        string value = "\n\n";
        string expected = "\"\n\n\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuotedAndLineFeedCharacter()
    {
        string value = " \" \n ";
        string expected = "\" \"\" \n \"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithLineFeedAtEnd()
    {
        string value = "testing\n";
        string expected = "\"testing\n\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithComma()
    {
        string value = "test,ing";
        string expected = "\"test,ing\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleComma()
    {
        string value = ",";
        string expected = "\",\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleCarriageReturn()
    {
        string value = "\r";
        string expected = "\"\r\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithMultipleCarriageReturn()
    {
        string value = "\r\r";
        string expected = "\"\r\r\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithCarriageReturn()
    {
        string value = "some text\r more text";
        string expected = "\"some text\r more text\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuotedAndCarriageReturnCharacter()
    {
        string value = "\"\r";
        string expected = "\"\"\"\r\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithCarriageReturnAtEnd()
    {
        string value = "testing\r";
        string expected = "\"testing\r\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithCRLFCharacter()
    {
        string value = "\r\n";
        string expected = "\"\r\n\"";
        escapeCsv(value, expected);
    }

    private static void escapeCsv(string value, string expected)
    {
        escapeCsv(value, expected, false);
    }

    private static void escapeCsvWithTrimming(string value, string expected)
    {
        escapeCsv(value, expected, true);
    }

    private static void escapeCsv(string value, string expected, bool trimOws)
    {
        string escapedValue = value;
        for (int i = 0; i < 10; ++i)
        {
            escapedValue = StringUtil.escapeCsv(escapedValue, trimOws);
            Assert.Equal(expected, escapedValue.ToString());
        }
    }

    [Fact]
    public void testEscapeCsvWithTrimming()
    {
        Assert.Same("", StringUtil.escapeCsv("", true));
        Assert.Same("ab", StringUtil.escapeCsv("ab", true));

        escapeCsvWithTrimming("", "");
        escapeCsvWithTrimming(" \t ", "");
        escapeCsvWithTrimming("ab", "ab");
        escapeCsvWithTrimming("a b", "a b");
        escapeCsvWithTrimming(" \ta \tb", "a \tb");
        escapeCsvWithTrimming("a \tb \t", "a \tb");
        escapeCsvWithTrimming("\t a \tb \t", "a \tb");
        escapeCsvWithTrimming("\"\t a b \"", "\"\t a b \"");
        escapeCsvWithTrimming(" \"\t a b \"\t", "\"\t a b \"");
        escapeCsvWithTrimming(" testing\t\n ", "\"testing\t\n\"");
        escapeCsvWithTrimming("\ttest,ing ", "\"test,ing\"");
    }

    [Fact]
    public void testEscapeCsvGarbageFree()
    {
        // 'StringUtil#escapeCsv()' should return same string object if string didn't changing.
        Assert.Same("1", StringUtil.escapeCsv("1", true));
        Assert.Same(" 123 ", StringUtil.escapeCsv(" 123 ", false));
        Assert.Same("\" 123 \"", StringUtil.escapeCsv("\" 123 \"", true));
        Assert.Same("\"\"", StringUtil.escapeCsv("\"\"", true));
        Assert.Same("123 \"\"", StringUtil.escapeCsv("123 \"\"", true));
        Assert.Same("123\"\"321", StringUtil.escapeCsv("123\"\"321", true));
        Assert.Same("\"123\"\"321\"", StringUtil.escapeCsv("\"123\"\"321\"", true));
    }

    [Fact]
    public void testUnescapeCsv()
    {
        Assert.Equal("", unescapeCsv(""));
        Assert.Equal("\"", unescapeCsv("\"\"\"\""));
        Assert.Equal("\"\"", unescapeCsv("\"\"\"\"\"\""));
        Assert.Equal("\"\"\"", unescapeCsv("\"\"\"\"\"\"\"\""));
        Assert.Equal("\"netty\"", unescapeCsv("\"\"\"netty\"\"\""));
        Assert.Equal("netty", unescapeCsv("netty"));
        Assert.Equal("netty", unescapeCsv("\"netty\""));
        Assert.Equal("\r", unescapeCsv("\"\r\""));
        Assert.Equal("\n", unescapeCsv("\"\n\""));
        Assert.Equal("hello,netty", unescapeCsv("\"hello,netty\""));
    }

    [Fact]
    public void unescapeCsvWithSingleQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsv("\"");
        });
    }

    [Fact]
    public void unescapeCsvWithOddQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsv("\"\"\"");
        });
    }

    [Fact]
    public void unescapeCsvWithCRAndWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsv("\r");
        });
    }

    [Fact]
    public void unescapeCsvWithLFAndWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsv("\n");
        });
    }

    [Fact]
    public void unescapeCsvWithCommaAndWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsv(",");
        });
    }

    [Fact]
    public void escapeCsvAndUnEscapeCsv()
    {
        assertEscapeCsvAndUnEscapeCsv("");
        assertEscapeCsvAndUnEscapeCsv("netty");
        assertEscapeCsvAndUnEscapeCsv("hello,netty");
        assertEscapeCsvAndUnEscapeCsv("hello,\"netty\"");
        assertEscapeCsvAndUnEscapeCsv("\"");
        assertEscapeCsvAndUnEscapeCsv(",");
        assertEscapeCsvAndUnEscapeCsv("\r");
        assertEscapeCsvAndUnEscapeCsv("\n");
    }

    private static void assertEscapeCsvAndUnEscapeCsv(string value)
    {
        Assert.Equal(value, unescapeCsv(StringUtil.escapeCsv(value)));
    }

    [Fact]
    public void testUnescapeCsvFields()
    {
        Assert.Equal(Collectives.singletonList(""), unescapeCsvFields(""));
        Assert.Equal(Collectives.asList("", ""), unescapeCsvFields(","));
        Assert.Equal(Collectives.asList("a", ""), unescapeCsvFields("a,"));
        Assert.Equal(Collectives.asList("", "a"), unescapeCsvFields(",a"));
        Assert.Equal(Collectives.singletonList("\""), unescapeCsvFields("\"\"\"\""));
        Assert.Equal(Collectives.asList("\"", "\""), unescapeCsvFields("\"\"\"\",\"\"\"\""));
        Assert.Equal(Collectives.singletonList("netty"), unescapeCsvFields("netty"));
        Assert.Equal(Collectives.asList("hello", "netty"), unescapeCsvFields("hello,netty"));
        Assert.Equal(Collectives.singletonList("hello,netty"), unescapeCsvFields("\"hello,netty\""));
        Assert.Equal(Collectives.asList("hello", "netty"), unescapeCsvFields("\"hello\",\"netty\""));
        Assert.Equal(Collectives.asList("a\"b", "c\"d"), unescapeCsvFields("\"a\"\"b\",\"c\"\"d\""));
        Assert.Equal(Collectives.asList("a\rb", "c\nd"), unescapeCsvFields("\"a\rb\",\"c\nd\""));
    }

    [Fact]
    public void unescapeCsvFieldsWithCRWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsvFields("a,\r");
        });
    }

    [Fact]
    public void unescapeCsvFieldsWithLFWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsvFields("a,\r");
        });
    }

    [Fact]
    public void unescapeCsvFieldsWithQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsvFields("a,\"");
        });
    }

    [Fact]
    public void unescapeCsvFieldsWithQuote2()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsvFields("\",a");
        });
    }

    [Fact]
    public void unescapeCsvFieldsWithQuote3()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            unescapeCsvFields("a\"b,a");
        });
    }

    [Fact]
    public void testSimpleClassName()
    {
        testSimpleClassName0(typeof(string));
    }

    [Fact]
    public void testSimpleInnerClassName()
    {
        testSimpleClassName0(typeof(TestClass));
    }

    private static void testSimpleClassName0(Type clazz)
    {
        var pkg = clazz.Namespace;
        string name;
        if (pkg != null)
        {
            name = clazz.FullName.substring(pkg.length() + 1);
        }
        else
        {
            name = clazz.Name;
        }

        Assert.Equal(name, simpleClassName(clazz));
    }


    [Fact]
    public void testEndsWith()
    {
        Assert.False(StringUtil.endsWith("", 'u'));
        Assert.True(StringUtil.endsWith("u", 'u'));
        Assert.True(StringUtil.endsWith("-u", 'u'));
        Assert.False(StringUtil.endsWith("-", 'u'));
        Assert.False(StringUtil.endsWith("u-", 'u'));
    }

    [Fact]
    public void trimOws()
    {
        Assert.Same("", StringUtil.trimOws(""));
        Assert.Equal("", StringUtil.trimOws(" \t "));
        Assert.Same("a", StringUtil.trimOws("a"));
        Assert.Equal("a", StringUtil.trimOws(" a"));
        Assert.Equal("a", StringUtil.trimOws("a "));
        Assert.Equal("a", StringUtil.trimOws(" a "));
        Assert.Same("abc", StringUtil.trimOws("abc"));
        Assert.Equal("abc", StringUtil.trimOws("\tabc"));
        Assert.Equal("abc", StringUtil.trimOws("abc\t"));
        Assert.Equal("abc", StringUtil.trimOws("\tabc\t"));
        Assert.Same("a\t b", StringUtil.trimOws("a\t b"));
        Assert.Equal("", StringUtil.trimOws("\t ").ToString());
        Assert.Equal("a b", StringUtil.trimOws("\ta b \t").ToString());
    }

    [Fact]
    public void testJoin()
    {
        Assert.Equal("",
            StringUtil.join(",", Collectives.emptyList<string>()).ToString());
        Assert.Equal("a",
            StringUtil.join(",", Collectives.singletonList("a")).ToString());
        Assert.Equal("a,b",
            StringUtil.join(",", Collectives.asList("a", "b")).ToString());
        Assert.Equal("a,b,c",
            StringUtil.join(",", Collectives.asList("a", "b", "c")).ToString());
        Assert.Equal("a,b,c,null,d",
            StringUtil.join(",", Collectives.asList("a", "b", "c", null, "d")).ToString());
    }

    [Fact]
    public void testIsNullOrEmpty()
    {
        Assert.True(isNullOrEmpty(null));
        Assert.True(isNullOrEmpty(""));
        Assert.True(isNullOrEmpty(string.Empty));
        Assert.False(isNullOrEmpty(" "));
        Assert.False(isNullOrEmpty("\t"));
        Assert.False(isNullOrEmpty("\n"));
        Assert.False(isNullOrEmpty("foo"));
        Assert.False(isNullOrEmpty(NEWLINE));
    }

    [Fact]
    public void testIndexOfWhiteSpace()
    {
        Assert.Equal(-1, indexOfWhiteSpace("", 0));
        Assert.Equal(0, indexOfWhiteSpace(" ", 0));
        Assert.Equal(-1, indexOfWhiteSpace(" ", 1));
        Assert.Equal(0, indexOfWhiteSpace("\n", 0));
        Assert.Equal(-1, indexOfWhiteSpace("\n", 1));
        Assert.Equal(0, indexOfWhiteSpace("\t", 0));
        Assert.Equal(-1, indexOfWhiteSpace("\t", 1));
        Assert.Equal(3, indexOfWhiteSpace("foo\r\nbar", 1));
        Assert.Equal(-1, indexOfWhiteSpace("foo\r\nbar", 10));
        Assert.Equal(7, indexOfWhiteSpace("foo\tbar\r\n", 6));
        Assert.Equal(-1, indexOfWhiteSpace("foo\tbar\r\n", int.MaxValue));
    }

    [Fact]
    public void testIndexOfNonWhiteSpace()
    {
        Assert.Equal(-1, indexOfNonWhiteSpace("", 0));
        Assert.Equal(-1, indexOfNonWhiteSpace(" ", 0));
        Assert.Equal(-1, indexOfNonWhiteSpace(" \t", 0));
        Assert.Equal(-1, indexOfNonWhiteSpace(" \t\r\n", 0));
        Assert.Equal(2, indexOfNonWhiteSpace(" \tfoo\r\n", 0));
        Assert.Equal(2, indexOfNonWhiteSpace(" \tfoo\r\n", 1));
        Assert.Equal(4, indexOfNonWhiteSpace(" \tfoo\r\n", 4));
        Assert.Equal(-1, indexOfNonWhiteSpace(" \tfoo\r\n", 10));
        Assert.Equal(-1, indexOfNonWhiteSpace(" \tfoo\r\n", int.MaxValue));
    }
}