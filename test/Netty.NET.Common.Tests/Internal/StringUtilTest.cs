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
    public void EnsureNewlineExists()
    {
        Assert.NotNull(NEWLINE);
    }

    [Fact]
    public void TestToHexString()
    {
        Assert.Equal("0", ToHexString(new byte[] { 0 }));
        Assert.Equal("1", ToHexString(new byte[] { 1 }));
        Assert.Equal("0", ToHexString(new byte[] { 0, 0 }));
        Assert.Equal("100", ToHexString(new byte[] { 1, 0 }));
        Assert.Equal("", ToHexString(EmptyArrays.EMPTY_BYTES));
    }

    [Fact]
    public void TestToHexStringPadded()
    {
        Assert.Equal("00", ToHexStringPadded(new byte[] { 0 }));
        Assert.Equal("01", ToHexStringPadded(new byte[] { 1 }));
        Assert.Equal("0000", ToHexStringPadded(new byte[] { 0, 0 }));
        Assert.Equal("0100", ToHexStringPadded(new byte[] { 1, 0 }));
        Assert.Equal("", ToHexStringPadded(EmptyArrays.EMPTY_BYTES));
    }

    [Fact]
    public void SplitSimple()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo:bar".Split(':'));
    }

    [Fact]
    public void SplitWithTrailingDelimiter()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo,bar,".TrimEnd(',').Split(','));
    }

    [Fact]
    public void SplitWithTrailingDelimiters()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo!bar!!".TrimEnd('!').Split('!'));
    }

    [Fact]
    public void SplitWithTrailingDelimitersDot()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo.bar..".TrimEnd('.').Split('.'));
    }

    [Fact]
    public void SplitWithTrailingDelimitersEq()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo=bar==".TrimEnd('=').Split('='));
    }

    [Fact]
    public void SplitWithTrailingDelimitersSpace()
    {
        Assert.Equal(new string[] { "foo", "bar" }, "foo bar  ".TrimEnd(' ').Split(' '));
    }

    [Fact]
    public void SplitWithConsecutiveDelimiters()
    {
        Assert.Equal(new string[] { "foo", "", "bar" }, "foo$$bar".Split('$'));
    }

    [Fact]
    public void SplitWithDelimiterAtBeginning()
    {
        Assert.Equal(new string[] { "", "foo", "bar" }, "#foo#bar".Split('#'));
    }

    [Fact]
    public void SplitMaxPart()
    {
        Assert.Equal(new string[] { "foo", "bar:bar2" }, "foo:bar:bar2".Split(':', 2, StringSplitOptions.None));
        Assert.Equal(new string[] { "foo", "bar", "bar2" }, "foo:bar:bar2".Split(':', 3, StringSplitOptions.None));
    }

    [Fact]
    public void SubstringAfterTest()
    {
        Assert.Equal("bar:bar2", SubstringAfter("foo:bar:bar2", ':'));
    }

    [Fact]
    public void CommonSuffixOfLengthTest()
    {
        // negative length suffixes are never common
        CheckNotCommonSuffix("abc", "abc", -1);

        // null has no suffix
        CheckNotCommonSuffix("abc", null, 0);
        CheckNotCommonSuffix(null, null, 0);

        // any non-null string has 0-length suffix
        CheckCommonSuffix("abc", "xx", 0);

        CheckCommonSuffix("abc", "abc", 0);
        CheckCommonSuffix("abc", "abc", 1);
        CheckCommonSuffix("abc", "abc", 2);
        CheckCommonSuffix("abc", "abc", 3);
        CheckNotCommonSuffix("abc", "abc", 4);

        CheckCommonSuffix("abcd", "cd", 1);
        CheckCommonSuffix("abcd", "cd", 2);
        CheckNotCommonSuffix("abcd", "cd", 3);

        CheckCommonSuffix("abcd", "axcd", 1);
        CheckCommonSuffix("abcd", "axcd", 2);
        CheckNotCommonSuffix("abcd", "axcd", 3);

        CheckNotCommonSuffix("abcx", "abcy", 1);
    }

    private static void CheckNotCommonSuffix(string s, string p, int len)
    {
        Assert.False(CheckCommonSuffixSymmetric(s, p, len));
    }

    private static void CheckCommonSuffix(string s, string p, int len)
    {
        Assert.True(CheckCommonSuffixSymmetric(s, p, len));
    }

    private static bool CheckCommonSuffixSymmetric(string s, string p, int len)
    {
        bool sp = CommonSuffixOfLength(s, p, len);
        bool ps = CommonSuffixOfLength(p, s, len);
        Assert.Equal(sp, ps);
        return sp;
    }

    [Fact]
    public void EscapeCsvNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            StringUtil.EscapeCsv(null);
        });
    }

    [Fact]
    public void EscapeCsvEmpty()
    {
        string value = "";
        EscapeCsv(value, value);
    }

    [Fact]
    public void EscapeCsvUnquoted()
    {
        string value = "something";
        EscapeCsv(value, value);
    }

    [Fact]
    public void EscapeCsvAlreadyQuoted()
    {
        string value = "\"something\"";
        string expected = "\"something\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithQuote()
    {
        string value = "s\"";
        string expected = "\"s\"\"\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithQuoteInMiddle()
    {
        string value = "some text\"and more text";
        string expected = "\"some text\"\"and more text\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithQuoteInMiddleAlreadyQuoted()
    {
        string value = "\"some text\"and more text\"";
        string expected = "\"some text\"\"and more text\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithQuotedWords()
    {
        string value = "\"foo\"\"goo\"";
        string expected = "\"foo\"\"goo\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithAlreadyEscapedQuote()
    {
        string value = "foo\"\"goo";
        string expected = "foo\"\"goo";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvEndingWithQuote()
    {
        string value = "some\"";
        string expected = "\"some\"\"\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithSingleQuote()
    {
        string value = "\"";
        string expected = "\"\"\"\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithSingleQuoteAndCharacter()
    {
        string value = "\"f";
        string expected = "\"\"\"f\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvAlreadyEscapedQuote()
    {
        string value = "\"some\"\"";
        string expected = "\"some\"\"\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvQuoted()
    {
        string value = "\"foo,goo\"";
        EscapeCsv(value, value);
    }

    [Fact]
    public void EscapeCsvWithLineFeed()
    {
        string value = "some text\n more text";
        string expected = "\"some text\n more text\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithSingleLineFeedCharacter()
    {
        string value = "\n";
        string expected = "\"\n\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithMultipleLineFeedCharacter()
    {
        string value = "\n\n";
        string expected = "\"\n\n\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithQuotedAndLineFeedCharacter()
    {
        string value = " \" \n ";
        string expected = "\" \"\" \n \"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithLineFeedAtEnd()
    {
        string value = "testing\n";
        string expected = "\"testing\n\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithComma()
    {
        string value = "test,ing";
        string expected = "\"test,ing\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithSingleComma()
    {
        string value = ",";
        string expected = "\",\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithSingleCarriageReturn()
    {
        string value = "\r";
        string expected = "\"\r\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithMultipleCarriageReturn()
    {
        string value = "\r\r";
        string expected = "\"\r\r\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithCarriageReturn()
    {
        string value = "some text\r more text";
        string expected = "\"some text\r more text\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithQuotedAndCarriageReturnCharacter()
    {
        string value = "\"\r";
        string expected = "\"\"\"\r\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithCarriageReturnAtEnd()
    {
        string value = "testing\r";
        string expected = "\"testing\r\"";
        EscapeCsv(value, expected);
    }

    [Fact]
    public void EscapeCsvWithCRLFCharacter()
    {
        string value = "\r\n";
        string expected = "\"\r\n\"";
        EscapeCsv(value, expected);
    }

    private static void EscapeCsv(string value, string expected)
    {
        EscapeCsv(value, expected, false);
    }

    private static void EscapeCsvWithTrimming(string value, string expected)
    {
        EscapeCsv(value, expected, true);
    }

    private static void EscapeCsv(string value, string expected, bool trimOws)
    {
        string escapedValue = value;
        for (int i = 0; i < 10; ++i)
        {
            escapedValue = StringUtil.EscapeCsv(escapedValue, trimOws);
            Assert.Equal(expected, escapedValue.ToString());
        }
    }

    [Fact]
    public void TestEscapeCsvWithTrimming()
    {
        Assert.Same("", StringUtil.EscapeCsv("", true));
        Assert.Same("ab", StringUtil.EscapeCsv("ab", true));

        EscapeCsvWithTrimming("", "");
        EscapeCsvWithTrimming(" \t ", "");
        EscapeCsvWithTrimming("ab", "ab");
        EscapeCsvWithTrimming("a b", "a b");
        EscapeCsvWithTrimming(" \ta \tb", "a \tb");
        EscapeCsvWithTrimming("a \tb \t", "a \tb");
        EscapeCsvWithTrimming("\t a \tb \t", "a \tb");
        EscapeCsvWithTrimming("\"\t a b \"", "\"\t a b \"");
        EscapeCsvWithTrimming(" \"\t a b \"\t", "\"\t a b \"");
        EscapeCsvWithTrimming(" testing\t\n ", "\"testing\t\n\"");
        EscapeCsvWithTrimming("\ttest,ing ", "\"test,ing\"");
    }

    [Fact]
    public void TestEscapeCsvGarbageFree()
    {
        // 'StringUtil#escapeCsv()' should return same string object if string didn't changing.
        Assert.Same("1", StringUtil.EscapeCsv("1", true));
        Assert.Same(" 123 ", StringUtil.EscapeCsv(" 123 ", false));
        Assert.Same("\" 123 \"", StringUtil.EscapeCsv("\" 123 \"", true));
        Assert.Same("\"\"", StringUtil.EscapeCsv("\"\"", true));
        Assert.Same("123 \"\"", StringUtil.EscapeCsv("123 \"\"", true));
        Assert.Same("123\"\"321", StringUtil.EscapeCsv("123\"\"321", true));
        Assert.Same("\"123\"\"321\"", StringUtil.EscapeCsv("\"123\"\"321\"", true));
    }

    [Fact]
    public void TestUnescapeCsv()
    {
        Assert.Equal("", UnescapeCsv(""));
        Assert.Equal("\"", UnescapeCsv("\"\"\"\""));
        Assert.Equal("\"\"", UnescapeCsv("\"\"\"\"\"\""));
        Assert.Equal("\"\"\"", UnescapeCsv("\"\"\"\"\"\"\"\""));
        Assert.Equal("\"netty\"", UnescapeCsv("\"\"\"netty\"\"\""));
        Assert.Equal("netty", UnescapeCsv("netty"));
        Assert.Equal("netty", UnescapeCsv("\"netty\""));
        Assert.Equal("\r", UnescapeCsv("\"\r\""));
        Assert.Equal("\n", UnescapeCsv("\"\n\""));
        Assert.Equal("hello,netty", UnescapeCsv("\"hello,netty\""));
    }

    [Fact]
    public void UnescapeCsvWithSingleQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsv("\"");
        });
    }

    [Fact]
    public void UnescapeCsvWithOddQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsv("\"\"\"");
        });
    }

    [Fact]
    public void UnescapeCsvWithCRAndWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsv("\r");
        });
    }

    [Fact]
    public void UnescapeCsvWithLFAndWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsv("\n");
        });
    }

    [Fact]
    public void UnescapeCsvWithCommaAndWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsv(",");
        });
    }

    [Fact]
    public void EscapeCsvAndUnEscapeCsv()
    {
        AssertEscapeCsvAndUnEscapeCsv("");
        AssertEscapeCsvAndUnEscapeCsv("netty");
        AssertEscapeCsvAndUnEscapeCsv("hello,netty");
        AssertEscapeCsvAndUnEscapeCsv("hello,\"netty\"");
        AssertEscapeCsvAndUnEscapeCsv("\"");
        AssertEscapeCsvAndUnEscapeCsv(",");
        AssertEscapeCsvAndUnEscapeCsv("\r");
        AssertEscapeCsvAndUnEscapeCsv("\n");
    }

    private static void AssertEscapeCsvAndUnEscapeCsv(string value)
    {
        Assert.Equal(value, UnescapeCsv(StringUtil.EscapeCsv(value)));
    }

    [Fact]
    public void TestUnescapeCsvFields()
    {
        Assert.Equal(Collectives.SingletonList(""), UnescapeCsvFields(""));
        Assert.Equal(Collectives.AsList("", ""), UnescapeCsvFields(","));
        Assert.Equal(Collectives.AsList("a", ""), UnescapeCsvFields("a,"));
        Assert.Equal(Collectives.AsList("", "a"), UnescapeCsvFields(",a"));
        Assert.Equal(Collectives.SingletonList("\""), UnescapeCsvFields("\"\"\"\""));
        Assert.Equal(Collectives.AsList("\"", "\""), UnescapeCsvFields("\"\"\"\",\"\"\"\""));
        Assert.Equal(Collectives.SingletonList("netty"), UnescapeCsvFields("netty"));
        Assert.Equal(Collectives.AsList("hello", "netty"), UnescapeCsvFields("hello,netty"));
        Assert.Equal(Collectives.SingletonList("hello,netty"), UnescapeCsvFields("\"hello,netty\""));
        Assert.Equal(Collectives.AsList("hello", "netty"), UnescapeCsvFields("\"hello\",\"netty\""));
        Assert.Equal(Collectives.AsList("a\"b", "c\"d"), UnescapeCsvFields("\"a\"\"b\",\"c\"\"d\""));
        Assert.Equal(Collectives.AsList("a\rb", "c\nd"), UnescapeCsvFields("\"a\rb\",\"c\nd\""));
    }

    [Fact]
    public void UnescapeCsvFieldsWithCRWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsvFields("a,\r");
        });
    }

    [Fact]
    public void UnescapeCsvFieldsWithLFWithoutQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsvFields("a,\r");
        });
    }

    [Fact]
    public void UnescapeCsvFieldsWithQuote()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsvFields("a,\"");
        });
    }

    [Fact]
    public void UnescapeCsvFieldsWithQuote2()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsvFields("\",a");
        });
    }

    [Fact]
    public void UnescapeCsvFieldsWithQuote3()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            UnescapeCsvFields("a\"b,a");
        });
    }

    [Fact]
    public void TestSimpleClassName()
    {
        TestSimpleClassName0(typeof(string));
    }

    [Fact]
    public void TestSimpleInnerClassName()
    {
        TestSimpleClassName0(typeof(TestClass));
    }

    private static void TestSimpleClassName0(Type clazz)
    {
        var pkg = clazz.Namespace;
        string name;
        if (pkg != null)
        {
            name = clazz.FullName[(pkg.Length + 1)..];
        }
        else
        {
            name = clazz.Name;
        }

        Assert.Equal(name, SimpleClassName(clazz));
    }


    [Fact]
    public void TestEndsWith()
    {
        Assert.False(StringUtil.EndsWith("", 'u'));
        Assert.True(StringUtil.EndsWith("u", 'u'));
        Assert.True(StringUtil.EndsWith("-u", 'u'));
        Assert.False(StringUtil.EndsWith("-", 'u'));
        Assert.False(StringUtil.EndsWith("u-", 'u'));
    }

    [Fact]
    public void TrimOws()
    {
        Assert.Same("", StringUtil.TrimOws(""));
        Assert.Equal("", StringUtil.TrimOws(" \t "));
        Assert.Same("a", StringUtil.TrimOws("a"));
        Assert.Equal("a", StringUtil.TrimOws(" a"));
        Assert.Equal("a", StringUtil.TrimOws("a "));
        Assert.Equal("a", StringUtil.TrimOws(" a "));
        Assert.Same("abc", StringUtil.TrimOws("abc"));
        Assert.Equal("abc", StringUtil.TrimOws("\tabc"));
        Assert.Equal("abc", StringUtil.TrimOws("abc\t"));
        Assert.Equal("abc", StringUtil.TrimOws("\tabc\t"));
        Assert.Same("a\t b", StringUtil.TrimOws("a\t b"));
        Assert.Equal("", StringUtil.TrimOws("\t ").ToString());
        Assert.Equal("a b", StringUtil.TrimOws("\ta b \t").ToString());
    }

    [Fact]
    public void TestJoin()
    {
        Assert.Equal("",
            StringUtil.Join(",", Collectives.EmptyList<string>()).ToString());
        Assert.Equal("a",
            StringUtil.Join(",", Collectives.SingletonList("a")).ToString());
        Assert.Equal("a,b",
            StringUtil.Join(",", Collectives.AsList("a", "b")).ToString());
        Assert.Equal("a,b,c",
            StringUtil.Join(",", Collectives.AsList("a", "b", "c")).ToString());
        Assert.Equal("a,b,c,null,d",
            StringUtil.Join(",", Collectives.AsList("a", "b", "c", null, "d")).ToString());
    }

    [Fact]
    public void TestIsNullOrEmpty()
    {
        Assert.True(IsNullOrEmpty(null));
        Assert.True(IsNullOrEmpty(""));
        Assert.True(IsNullOrEmpty(string.Empty));
        Assert.False(IsNullOrEmpty(" "));
        Assert.False(IsNullOrEmpty("\t"));
        Assert.False(IsNullOrEmpty("\n"));
        Assert.False(IsNullOrEmpty("foo"));
        Assert.False(IsNullOrEmpty(NEWLINE));
    }

    [Fact]
    public void TestIndexOfWhiteSpace()
    {
        Assert.Equal(-1, IndexOfWhiteSpace("", 0));
        Assert.Equal(0, IndexOfWhiteSpace(" ", 0));
        Assert.Equal(-1, IndexOfWhiteSpace(" ", 1));
        Assert.Equal(0, IndexOfWhiteSpace("\n", 0));
        Assert.Equal(-1, IndexOfWhiteSpace("\n", 1));
        Assert.Equal(0, IndexOfWhiteSpace("\t", 0));
        Assert.Equal(-1, IndexOfWhiteSpace("\t", 1));
        Assert.Equal(3, IndexOfWhiteSpace("foo\r\nbar", 1));
        Assert.Equal(-1, IndexOfWhiteSpace("foo\r\nbar", 10));
        Assert.Equal(7, IndexOfWhiteSpace("foo\tbar\r\n", 6));
        Assert.Equal(-1, IndexOfWhiteSpace("foo\tbar\r\n", int.MaxValue));
    }

    [Fact]
    public void TestIndexOfNonWhiteSpace()
    {
        Assert.Equal(-1, IndexOfNonWhiteSpace("", 0));
        Assert.Equal(-1, IndexOfNonWhiteSpace(" ", 0));
        Assert.Equal(-1, IndexOfNonWhiteSpace(" \t", 0));
        Assert.Equal(-1, IndexOfNonWhiteSpace(" \t\r\n", 0));
        Assert.Equal(2, IndexOfNonWhiteSpace(" \tfoo\r\n", 0));
        Assert.Equal(2, IndexOfNonWhiteSpace(" \tfoo\r\n", 1));
        Assert.Equal(4, IndexOfNonWhiteSpace(" \tfoo\r\n", 4));
        Assert.Equal(-1, IndexOfNonWhiteSpace(" \tfoo\r\n", 10));
        Assert.Equal(-1, IndexOfNonWhiteSpace(" \tfoo\r\n", int.MaxValue));
    }
}
