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
namespace Netty.Common.Tests.Internal;


public class StringUtilTest {

    [Fact]
    public void ensureNewlineExists() {
        Assert.NotNull(NEWLINE);
    }

    [Fact]
    public void testToHexString() {
        Assert.Equal("0", toHexString(new byte[] { 0 }));
        Assert.Equal("1", toHexString(new byte[] { 1 }));
        Assert.Equal("0", toHexString(new byte[] { 0, 0 }));
        Assert.Equal("100", toHexString(new byte[] { 1, 0 }));
        Assert.Equal("", toHexString(EmptyArrays.EMPTY_BYTES));
    }

    [Fact]
    public void testToHexStringPadded() {
        Assert.Equal("00", toHexStringPadded(new byte[]{0}));
        Assert.Equal("01", toHexStringPadded(new byte[]{1}));
        Assert.Equal("0000", toHexStringPadded(new byte[]{0, 0}));
        Assert.Equal("0100", toHexStringPadded(new byte[]{1, 0}));
        Assert.Equal("", toHexStringPadded(EmptyArrays.EMPTY_BYTES));
    }

    [Fact]
    public void splitSimple() {
        Assert.Equal(new string[] { "foo", "bar" }, "foo:bar".split(":"));
    }

    [Fact]
    public void splitWithTrailingDelimiter() {
        Assert.Equal(new string[] { "foo", "bar" }, "foo,bar,".split(","));
    }

    [Fact]
    public void splitWithTrailingDelimiters() {
        Assert.Equal(new string[] { "foo", "bar" }, "foo!bar!!".split("!"));
    }

    [Fact]
    public void splitWithTrailingDelimitersDot() {
        Assert.Equal(new string[] { "foo", "bar" }, "foo.bar..".split("\\."));
    }

    [Fact]
    public void splitWithTrailingDelimitersEq() {
        Assert.Equal(new string[] { "foo", "bar" }, "foo=bar==".split("="));
    }

    [Fact]
    public void splitWithTrailingDelimitersSpace() {
        Assert.Equal(new string[] { "foo", "bar" }, "foo bar  ".split(" "));
    }

    [Fact]
    public void splitWithConsecutiveDelimiters() {
        Assert.Equal(new string[] { "foo", "", "bar" }, "foo$$bar".split("\\$"));
    }

    [Fact]
    public void splitWithDelimiterAtBeginning() {
        Assert.Equal(new string[] { "", "foo", "bar" }, "#foo#bar".split("#"));
    }

    [Fact]
    public void splitMaxPart() {
        Assert.Equal(new string[] { "foo", "bar:bar2" }, "foo:bar:bar2".split(":", 2));
        Assert.Equal(new string[] { "foo", "bar", "bar2" }, "foo:bar:bar2".split(":", 3));
    }

    [Fact]
    public void substringAfterTest() {
        Assert.Equal("bar:bar2", substringAfter("foo:bar:bar2", ':'));
    }

    [Fact]
    public void commonSuffixOfLengthTest() {
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

    private static void checkNotCommonSuffix(string s, string p, int len) {
        Assert.False(checkCommonSuffixSymmetric(s, p, len));
    }

    private static void checkCommonSuffix(string s, string p, int len) {
        Assert.True(checkCommonSuffixSymmetric(s, p, len));
    }

    private static bool checkCommonSuffixSymmetric(string s, string p, int len) {
        bool sp = commonSuffixOfLength(s, p, len);
        bool ps = commonSuffixOfLength(p, s, len);
        Assert.Equal(sp, ps);
        return sp;
    }

    [Fact]
    public void escapeCsvNull() {
        Assert.Throws<NullReferenceException>(new Executable() {
            @Override
            public void execute() {
                StringUtil.escapeCsv(null);
            }
        });
    }

    [Fact]
    public void escapeCsvEmpty() {
        CharSequence value = "";
        escapeCsv(value, value);
    }

    [Fact]
    public void escapeCsvUnquoted() {
        CharSequence value = "something";
        escapeCsv(value, value);
    }

    [Fact]
    public void escapeCsvAlreadyQuoted() {
        CharSequence value = "\"something\"";
        CharSequence expected = "\"something\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuote() {
        CharSequence value = "s\"";
        CharSequence expected = "\"s\"\"\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuoteInMiddle() {
        CharSequence value = "some text\"and more text";
        CharSequence expected = "\"some text\"\"and more text\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuoteInMiddleAlreadyQuoted() {
        CharSequence value = "\"some text\"and more text\"";
        CharSequence expected = "\"some text\"\"and more text\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuotedWords() {
        CharSequence value = "\"foo\"\"goo\"";
        CharSequence expected = "\"foo\"\"goo\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithAlreadyEscapedQuote() {
        CharSequence value = "foo\"\"goo";
        CharSequence expected = "foo\"\"goo";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvEndingWithQuote() {
        CharSequence value = "some\"";
        CharSequence expected = "\"some\"\"\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleQuote() {
        CharSequence value = "\"";
        CharSequence expected = "\"\"\"\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleQuoteAndCharacter() {
        CharSequence value = "\"f";
        CharSequence expected = "\"\"\"f\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvAlreadyEscapedQuote() {
        CharSequence value = "\"some\"\"";
        CharSequence expected = "\"some\"\"\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvQuoted() {
        CharSequence value = "\"foo,goo\"";
        escapeCsv(value, value);
    }

    [Fact]
    public void escapeCsvWithLineFeed() {
        CharSequence value = "some text\n more text";
        CharSequence expected = "\"some text\n more text\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleLineFeedCharacter() {
        CharSequence value = "\n";
        CharSequence expected = "\"\n\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithMultipleLineFeedCharacter() {
        CharSequence value = "\n\n";
        CharSequence expected = "\"\n\n\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuotedAndLineFeedCharacter() {
        CharSequence value = " \" \n ";
        CharSequence expected = "\" \"\" \n \"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithLineFeedAtEnd() {
        CharSequence value = "testing\n";
        CharSequence expected = "\"testing\n\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithComma() {
        CharSequence value = "test,ing";
        CharSequence expected = "\"test,ing\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleComma() {
        CharSequence value = ",";
        CharSequence expected = "\",\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithSingleCarriageReturn() {
        CharSequence value = "\r";
        CharSequence expected = "\"\r\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithMultipleCarriageReturn() {
        CharSequence value = "\r\r";
        CharSequence expected = "\"\r\r\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithCarriageReturn() {
        CharSequence value = "some text\r more text";
        CharSequence expected = "\"some text\r more text\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithQuotedAndCarriageReturnCharacter() {
        CharSequence value = "\"\r";
        CharSequence expected = "\"\"\"\r\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithCarriageReturnAtEnd() {
        CharSequence value = "testing\r";
        CharSequence expected = "\"testing\r\"";
        escapeCsv(value, expected);
    }

    [Fact]
    public void escapeCsvWithCRLFCharacter() {
        CharSequence value = "\r\n";
        CharSequence expected = "\"\r\n\"";
        escapeCsv(value, expected);
    }

    private static void escapeCsv(CharSequence value, CharSequence expected) {
        escapeCsv(value, expected, false);
    }

    private static void escapeCsvWithTrimming(CharSequence value, CharSequence expected) {
        escapeCsv(value, expected, true);
    }

    private static void escapeCsv(CharSequence value, CharSequence expected, bool trimOws) {
        CharSequence escapedValue = value;
        for (int i = 0; i < 10; ++i) {
            escapedValue = StringUtil.escapeCsv(escapedValue, trimOws);
            Assert.Equal(expected, escapedValue.toString());
        }
    }

    [Fact]
    public void escapeCsvWithTrimming() {
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
    public void escapeCsvGarbageFree() {
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
    public void testUnescapeCsv() {
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
    public void unescapeCsvWithSingleQuote() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsv("\"");
            }
        });
    }

    [Fact]
    public void unescapeCsvWithOddQuote() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsv("\"\"\"");
            }
        });
    }

    [Fact]
    public void unescapeCsvWithCRAndWithoutQuote() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsv("\r");
            }
        });
    }

    [Fact]
    public void unescapeCsvWithLFAndWithoutQuote() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsv("\n");
            }
        });
    }

    [Fact]
    public void unescapeCsvWithCommaAndWithoutQuote() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsv(",");
            }
        });
    }

    [Fact]
    public void escapeCsvAndUnEscapeCsv() {
        assertEscapeCsvAndUnEscapeCsv("");
        assertEscapeCsvAndUnEscapeCsv("netty");
        assertEscapeCsvAndUnEscapeCsv("hello,netty");
        assertEscapeCsvAndUnEscapeCsv("hello,\"netty\"");
        assertEscapeCsvAndUnEscapeCsv("\"");
        assertEscapeCsvAndUnEscapeCsv(",");
        assertEscapeCsvAndUnEscapeCsv("\r");
        assertEscapeCsvAndUnEscapeCsv("\n");
    }

    private static void assertEscapeCsvAndUnEscapeCsv(string value) {
        Assert.Equal(value, unescapeCsv(StringUtil.escapeCsv(value)));
    }

    [Fact]
    public void testUnescapeCsvFields() {
        Assert.Equal(Collections.singletonList(""), unescapeCsvFields(""));
        Assert.Equal(Arrays.asList("", ""), unescapeCsvFields(","));
        Assert.Equal(Arrays.asList("a", ""), unescapeCsvFields("a,"));
        Assert.Equal(Arrays.asList("", "a"), unescapeCsvFields(",a"));
        Assert.Equal(Collections.singletonList("\""), unescapeCsvFields("\"\"\"\""));
        Assert.Equal(Arrays.asList("\"", "\""), unescapeCsvFields("\"\"\"\",\"\"\"\""));
        Assert.Equal(Collections.singletonList("netty"), unescapeCsvFields("netty"));
        Assert.Equal(Arrays.asList("hello", "netty"), unescapeCsvFields("hello,netty"));
        Assert.Equal(Collections.singletonList("hello,netty"), unescapeCsvFields("\"hello,netty\""));
        Assert.Equal(Arrays.asList("hello", "netty"), unescapeCsvFields("\"hello\",\"netty\""));
        Assert.Equal(Arrays.asList("a\"b", "c\"d"), unescapeCsvFields("\"a\"\"b\",\"c\"\"d\""));
        Assert.Equal(Arrays.asList("a\rb", "c\nd"), unescapeCsvFields("\"a\rb\",\"c\nd\""));
    }

    [Fact]
    public void unescapeCsvFieldsWithCRWithoutQuote() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsvFields("a,\r");
            }
        });
    }

    [Fact]
    public void unescapeCsvFieldsWithLFWithoutQuote() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsvFields("a,\r");
            }
        });
    }

    [Fact]
    public void unescapeCsvFieldsWithQuote() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsvFields("a,\"");
            }
        });
    }

    [Fact]
    public void unescapeCsvFieldsWithQuote2() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsvFields("\",a");
            }
        });
    }

    [Fact]
    public void unescapeCsvFieldsWithQuote3() {
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                unescapeCsvFields("a\"b,a");
            }
        });
    }

    [Fact]
    public void testSimpleClassName() {
        testSimpleClassName(string.class);
    }

    [Fact]
    public void testSimpleInnerClassName() {
        testSimpleClassName(TestClass.class);
    }

    private static void testSimpleClassName(Class<?> clazz) {
        Package pkg = clazz.getPackage();
        string name;
        if (pkg != null) {
            name = clazz.getName().substring(pkg.getName().length() + 1);
        } else {
            name = clazz.getName();
        }
        Assert.Equal(name, simpleClassName(clazz));
    }

    private static final class TestClass { }

    [Fact]
    public void testEndsWith() {
        Assert.False(StringUtil.endsWith("", 'u'));
        Assert.True(StringUtil.endsWith("u", 'u'));
        Assert.True(StringUtil.endsWith("-u", 'u'));
        Assert.False(StringUtil.endsWith("-", 'u'));
        Assert.False(StringUtil.endsWith("u-", 'u'));
    }

    [Fact]
    public void trimOws() {
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
        Assert.Equal("", StringUtil.trimOws("\t ").toString());
        Assert.Equal("a b", StringUtil.trimOws("\ta b \t").toString());
    }

    [Fact]
    public void testJoin() {
        Assert.Equal("",
                     StringUtil.join(",", Collections.<CharSequence>emptyList()).toString());
        Assert.Equal("a",
                     StringUtil.join(",", Collections.singletonList("a")).toString());
        Assert.Equal("a,b",
                     StringUtil.join(",", Arrays.asList("a", "b")).toString());
        Assert.Equal("a,b,c",
                     StringUtil.join(",", Arrays.asList("a", "b", "c")).toString());
        Assert.Equal("a,b,c,null,d",
                     StringUtil.join(",", Arrays.asList("a", "b", "c", null, "d")).toString());
    }

    [Fact]
    public void testIsNullOrEmpty() {
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
    public void testIndexOfWhiteSpace() {
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
    public void testIndexOfNonWhiteSpace() {
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
