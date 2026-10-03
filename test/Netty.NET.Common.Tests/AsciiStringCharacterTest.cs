/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Common.Internal;
using static Netty.NET.Common.AsciiString;

namespace Netty.NET.Common.Tests;

/**
 * Test character encoding and case insensitivity for the {@link AsciiString} class
 */
public class AsciiStringCharacterTest
{
    private static readonly Random r = new Random();

    // Preserve six encoding configurations with explicit CLR byte order/preamble policy.
    // A preamble-capable Encoding does not insert its preamble into AsciiString's raw bytes.
    private static readonly Encoding[] Encodings =
    {
        new UnicodeEncoding(true, true), new UnicodeEncoding(true, false),
        new UnicodeEncoding(false, false), new UTF8Encoding(false), Encoding.Latin1, Encoding.ASCII
    };

    [Fact]
    public void testContentEqualsIgnoreCase()
    {
        byte[] bytes = { 32, (byte)(byte)'a' };
        AsciiString asciiString = new AsciiString(bytes, 1, 1, false);
        // https://github.com/netty/netty/issues/9475
        Assert.False(asciiString.contentEqualsIgnoreCase(Seq("b")));
        Assert.False(asciiString.contentEqualsIgnoreCase(new AsciiString("b")));
    }

    [Fact]
    public void testGetBytesStringBuilder()
    {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < 1 << 16; ++i)
        {
            b.Append("eéaà");
        }

        string bString = b.ToString();
        Encoding[] charsets = Encodings;
        for (int i = 0; i < charsets.Length; ++i)
        {
            Encoding charset = charsets[i];
            byte[] expected = charset.GetBytes(bString);
            byte[] actual = new AsciiString(b.ToString(), charset).toByteArray();
            Assert.Equal(expected, actual, "failure for " + charset);
        }
    }

    [Fact]
    public void testGetBytesString()
    {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < 1 << 16; ++i)
        {
            b.Append("eéaà");
        }

        string bString = b.ToString();
        Encoding[] charsets = Encodings;
        for (int i = 0; i < charsets.Length; ++i)
        {
            Encoding charset = charsets[i];
            byte[] expected = charset.GetBytes(bString);
            byte[] actual = new AsciiString(bString, charset).toByteArray();
            Assert.Equal(expected, actual, "failure for " + charset);
        }
    }

    [Fact]
    public void testGetBytesAsciiString()
    {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < 1 << 16; ++i)
        {
            b.Append("eéaà");
        }

        string bString = b.ToString();
        // The AsciiString class actually limits the Charset to ISO_8859_1
        byte[] expected = Encoding.Latin1.GetBytes(bString);
        byte[] actual = new AsciiString(bString).toByteArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void testComparisonWithString()
    {
        string str = "shouldn't fail";
        AsciiString ascii = new AsciiString(str.ToCharArray());
        Assert.Equal(str, ascii.ToString());
    }

    [Fact]
    public void subSequenceTest()
    {
        byte[] init = { (byte)'t', (byte)'h', (byte)'i', (byte)'s', (byte)' ', (byte)'i', (byte)'s', (byte)' ', (byte)'a', (byte)' ', (byte)'t', (byte)'e', (byte)'s', (byte)'t' };
        AsciiString ascii = new AsciiString(init);
        int start = 2;
        int end = init.Length;
        AsciiString sub1 = ascii.subSequence(start, end, false);
        AsciiString sub2 = ascii.subSequence(start, end, true);
        Assert.Equal(sub1.GetHashCode(), sub2.GetHashCode());
        Assert.Equal(sub1, sub2);
        for (int i = start; i < end; ++i)
        {
            Assert.Equal(init[i], sub1.byteAt(i - start));
        }
    }

    [Fact]
    public void testContains()
    {
        string[] falseLhs = { null, "a", "aa", "aaa" };
        string[] falseRhs = { null, "b", "ba", "baa" };
        for (int i = 0; i < falseLhs.Length; ++i)
        {
            for (int j = 0; j < falseRhs.Length; ++j)
            {
                assertContains(falseLhs[i], falseRhs[i], false, false);
            }
        }

        assertContains("", "", true, true);
        assertContains("AsfdsF", "", true, true);
        assertContains("", "b", false, false);
        assertContains("a", "a", true, true);
        assertContains("a", "b", false, false);
        assertContains("a", "A", false, true);
        string b = "xyz";
        string a = b;
        assertContains(a, b, true, true);

        a = "a" + b;
        assertContains(a, b, true, true);

        a = b + "a";
        assertContains(a, b, true, true);

        a = "a" + b + "a";
        assertContains(a, b, true, true);

        b = "xYz";
        a = "xyz";
        assertContains(a, b, false, true);

        b = "xYz";
        a = "xyzxxxXyZ" + b + "aaa";
        assertContains(a, b, true, true);

        b = "foOo";
        a = "fooofoO";
        assertContains(a, b, false, true);

        b = "Content-Equals: 10000";
        a = "content-equals: 1000";
        assertContains(a, b, false, false);
        a += "0";
        assertContains(a, b, false, true);
    }

    private static void assertContains(string a, string b, bool caseSensitiveEquals, bool caseInsenstaiveEquals)
    {
        Assert.Equal(caseSensitiveEquals, contains(Seq(a), Seq(b)));
        Assert.Equal(caseInsenstaiveEquals, containsIgnoreCase(Seq(a), Seq(b)));
    }

    [Fact]
    public void testCaseSensitivity()
    {
        int i = 0;
        for (; i < 32; i++)
        {
            doCaseSensitivity(i);
        }

        int min = i;
        int max = 4000;
        int len = r.Next((max - min) + 1) + min;
        doCaseSensitivity(len);
    }

    private static void doCaseSensitivity(int len)
    {
        // Build an upper case and lower case string
        int upperA = 'A';
        int upperZ = 'Z';
        int upperToLower = (int)'a' - upperA;
        byte[] lowerCaseBytes = new byte[len];
        StringBuilder upperCaseBuilder = new StringBuilder(len);
        for (int i = 0; i < len; ++i)
        {
            char upper = (char)(r.Next((upperZ - upperA) + 1) + upperA);
            upperCaseBuilder.Append(upper);
            lowerCaseBytes[i] = (byte)(upper + upperToLower);
        }

        string upperCaseString = upperCaseBuilder.ToString();
        string lowerCaseString = Encoding.Latin1.GetString(lowerCaseBytes);
        AsciiString lowerCaseAscii = new AsciiString(lowerCaseBytes, false);
        AsciiString upperCaseAscii = new AsciiString(upperCaseString);
        string errorString = "len: " + len;
        // Test upper case hash codes are equal
        int upperCaseExpected = upperCaseAscii.GetHashCode();
        Assert.Equal(upperCaseExpected, AsciiString.hashCode( Seq(upperCaseBuilder)), errorString);
        Assert.Equal(upperCaseExpected, AsciiString.hashCode( Seq(upperCaseString)), errorString);
        Assert.Equal(upperCaseExpected, upperCaseAscii.GetHashCode(), errorString);

        // Test lower case hash codes are equal
        int lowerCaseExpected = lowerCaseAscii.GetHashCode();
        Assert.Equal(lowerCaseExpected, AsciiString.hashCode( Seq(lowerCaseAscii)), errorString);
        Assert.Equal(lowerCaseExpected, AsciiString.hashCode( Seq(lowerCaseString)), errorString);
        Assert.Equal(lowerCaseExpected, lowerCaseAscii.GetHashCode(), errorString);

        // Test case insensitive hash codes are equal
        int expectedCaseInsensitive = lowerCaseAscii.GetHashCode();
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode( Seq(upperCaseBuilder)), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode( Seq(upperCaseString)), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode( Seq(lowerCaseString)), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode( Seq(lowerCaseAscii)), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode( Seq(upperCaseAscii)), errorString);
        Assert.Equal(expectedCaseInsensitive, lowerCaseAscii.GetHashCode(), errorString);
        Assert.Equal(expectedCaseInsensitive, upperCaseAscii.GetHashCode(), errorString);

        // Test that opposite cases are equal
        Assert.Equal(lowerCaseAscii.GetHashCode(), AsciiString.hashCode( Seq(upperCaseString)), errorString);
        Assert.Equal(upperCaseAscii.GetHashCode(), AsciiString.hashCode( Seq(lowerCaseString)), errorString);
    }

    [Fact]
    public void caseInsensitiveHasherCharBuffer()
    {
        string s1 = "TRANSFER-ENCODING";
        char[] array = new char[128];
        int offset = 100;
        for (int i = 0; i < s1.Length; ++i)
        {
            array[offset + i] = s1[i];
        }

        // CLR adaptation: CharBuffer contributes a sliced character sequence, not a buffer API.
        ICharSequence buffer = new StringCharSequence(new string(array), offset, s1.Length);
        Assert.Equal(AsciiString.hashCode( Seq(s1)), AsciiString.hashCode( Seq(buffer)));
    }

    [Fact]
    public void testBooleanUtilityMethods()
    {
        Assert.True(new AsciiString(new byte[] { 1 }).parseBoolean());
        Assert.False(AsciiString.EMPTY_STRING.parseBoolean());
        Assert.False(new AsciiString(new byte[] { 0 }).parseBoolean());
        Assert.True(new AsciiString(new byte[] { 5 }).parseBoolean());
        Assert.True(new AsciiString(new byte[] { 2, 0 }).parseBoolean());
    }

    [Fact]
    public void testEqualsIgnoreCase()
    {
        Assert.True(AsciiString.contentEqualsIgnoreCase( Seq(null), Seq(null)));
        Assert.False(AsciiString.contentEqualsIgnoreCase( Seq(null), Seq("foo")));
        Assert.False(AsciiString.contentEqualsIgnoreCase( Seq("bar"), Seq(null)));
        Assert.True(AsciiString.contentEqualsIgnoreCase( Seq("FoO"), Seq("fOo")));
        Assert.False(AsciiString.contentEqualsIgnoreCase( Seq("FoO"), Seq("bar")));
        Assert.False(AsciiString.contentEqualsIgnoreCase( Seq("Foo"), Seq("foobar")));
        Assert.False(AsciiString.contentEqualsIgnoreCase( Seq("foobar"), Seq("Foo")));

        // Test variations (Ascii + String, Ascii + Ascii, String + Ascii)
        Assert.True(AsciiString.contentEqualsIgnoreCase( Seq(new AsciiString("FoO")), Seq("fOo")));
        Assert.True(AsciiString.contentEqualsIgnoreCase( Seq(new AsciiString("FoO")), Seq(new AsciiString("fOo"))));
        Assert.True(AsciiString.contentEqualsIgnoreCase( Seq("FoO"), Seq(new AsciiString("fOo"))));

        // Test variations (Ascii + String, Ascii + Ascii, String + Ascii)
        Assert.False(AsciiString.contentEqualsIgnoreCase( Seq(new AsciiString("FoO")), Seq("bAr")));
        Assert.False(AsciiString.contentEqualsIgnoreCase( Seq(new AsciiString("FoO")), Seq(new AsciiString("bAr"))));
        Assert.False(AsciiString.contentEqualsIgnoreCase( Seq("FoO"), Seq(new AsciiString("bAr"))));
    }

    [Fact]
    public void testIndexOfIgnoreCase()
    {
        Assert.Equal(-1, AsciiString.indexOfIgnoreCase( Seq(null), Seq("abc"), 1));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCase( Seq("abc"), Seq(null), 1));
        Assert.Equal(0, AsciiString.indexOfIgnoreCase( Seq(""), Seq(""), 0));
        Assert.Equal(0, AsciiString.indexOfIgnoreCase( Seq("aabaabaa"), Seq("A"), 0));
        Assert.Equal(2, AsciiString.indexOfIgnoreCase( Seq("aabaabaa"), Seq("B"), 0));
        Assert.Equal(1, AsciiString.indexOfIgnoreCase( Seq("aabaabaa"), Seq("AB"), 0));
        Assert.Equal(5, AsciiString.indexOfIgnoreCase( Seq("aabaabaa"), Seq("B"), 3));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCase( Seq("aabaabaa"), Seq("B"), 9));
        Assert.Equal(2, AsciiString.indexOfIgnoreCase( Seq("aabaabaa"), Seq("B"), -1));
        Assert.Equal(2, AsciiString.indexOfIgnoreCase( Seq("aabaabaa"), Seq(""), 2));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCase( Seq("abc"), Seq(""), 9));
        Assert.Equal(0, AsciiString.indexOfIgnoreCase( Seq("ãabaabaa"), Seq("Ã"), 0));
    }

    [Fact]
    public void testIndexOfIgnoreCaseAscii()
    {
        Assert.Equal(-1, AsciiString.indexOfIgnoreCaseAscii( Seq(null), Seq("abc"), 1));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCaseAscii( Seq("abc"), Seq(null), 1));
        Assert.Equal(0, AsciiString.indexOfIgnoreCaseAscii( Seq(""), Seq(""), 0));
        Assert.Equal(0, AsciiString.indexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("A"), 0));
        Assert.Equal(2, AsciiString.indexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("B"), 0));
        Assert.Equal(1, AsciiString.indexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("AB"), 0));
        Assert.Equal(5, AsciiString.indexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("B"), 3));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("B"), 9));
        Assert.Equal(2, AsciiString.indexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("B"), -1));
        Assert.Equal(2, AsciiString.indexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq(""), 2));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCaseAscii( Seq("abc"), Seq(""), 9));
    }

    [Fact]
    public void testTrim()
    {
        Assert.Equal("", AsciiString.EMPTY_STRING.trim().ToString());
        Assert.Equal("abc", new AsciiString("  abc").trim().ToString());
        Assert.Equal("abc", new AsciiString("abc  ").trim().ToString());
        Assert.Equal("abc", new AsciiString("  abc  ").trim().ToString());
    }

    [Fact]
    public void testIndexOfChar()
    {
        Assert.Equal(-1, AsciiString.indexOf( Seq(null), 'a', 0));
        Assert.Equal(-1, new AsciiString("").indexOf('a', 0));
        Assert.Equal(-1, new AsciiString("abc").indexOf('d', 0));
        Assert.Equal(-1, new AsciiString("aabaabaa").indexOf('A', 0));
        Assert.Equal(0, new AsciiString("aabaabaa").indexOf('a', 0));
        Assert.Equal(1, new AsciiString("aabaabaa").indexOf('a', 1));
        Assert.Equal(3, new AsciiString("aabaabaa").indexOf('a', 2));
        Assert.Equal(3, new AsciiString("aabdabaa").indexOf('d', 1));
        Assert.Equal(1, new AsciiString("abcd", 1, 2).indexOf('c', 0));
        Assert.Equal(2, new AsciiString("abcd", 1, 3).indexOf('d', 2));
        Assert.Equal(0, new AsciiString("abcd", 1, 2).indexOf('b', 0));
        Assert.Equal(-1, new AsciiString("abcd", 0, 2).indexOf('c', 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 3).indexOf('a', 0));
    }

    [Fact]
    public void testIndexOfCharSequence()
    {
        Assert.Equal(0, new AsciiString("abcd").indexOf(Seq("abcd"), 0));
        Assert.Equal(0, new AsciiString("abcd").indexOf(Seq("abc"), 0));
        Assert.Equal(1, new AsciiString("abcd").indexOf(Seq("bcd"), 0));
        Assert.Equal(1, new AsciiString("abcd").indexOf(Seq("bc"), 0));
        Assert.Equal(1, new AsciiString("abcdabcd").indexOf(Seq("bcd"), 0));
        Assert.Equal(0, new AsciiString("abcd", 1, 2).indexOf(Seq("bc"), 0));
        Assert.Equal(0, new AsciiString("abcd", 1, 3).indexOf(Seq("bcd"), 0));
        Assert.Equal(1, new AsciiString("abcdabcd", 4, 4).indexOf(Seq("bcd"), 0));
        Assert.Equal(3, new AsciiString("012345").indexOf(Seq("345"), 3));
        Assert.Equal(3, new AsciiString("012345").indexOf(Seq("345"), 0));

        // Test with empty string
        Assert.Equal(0, new AsciiString("abcd").indexOf(Seq(""), 0));
        Assert.Equal(1, new AsciiString("abcd").indexOf(Seq(""), 1));
        Assert.Equal(3, new AsciiString("abcd", 1, 3).indexOf(Seq(""), 4));

        // Test not found
        Assert.Equal(-1, new AsciiString("abcd").indexOf(Seq("abcde"), 0));
        Assert.Equal(-1, new AsciiString("abcdbc").indexOf(Seq("bce"), 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 3).indexOf(Seq("abc"), 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 2).indexOf(Seq("bd"), 0));
        Assert.Equal(-1, new AsciiString("012345").indexOf(Seq("345"), 4));
        Assert.Equal(-1, new AsciiString("012345").indexOf(Seq("abc"), 3));
        Assert.Equal(-1, new AsciiString("012345").indexOf(Seq("abc"), 0));
        Assert.Equal(-1, new AsciiString("012345").indexOf(Seq("abcdefghi"), 0));
        Assert.Equal(-1, new AsciiString("012345").indexOf(Seq("abcdefghi"), 4));
    }

    [Fact]
    public void testStaticIndexOfChar()
    {
        Assert.Equal(-1, AsciiString.indexOf( Seq(null), 'a', 0));
        Assert.Equal(-1, AsciiString.indexOf( Seq(""), 'a', 0));
        Assert.Equal(-1, AsciiString.indexOf( Seq("abc"), 'd', 0));
        Assert.Equal(-1, AsciiString.indexOf( Seq("aabaabaa"), 'A', 0));
        Assert.Equal(0, AsciiString.indexOf( Seq("aabaabaa"), 'a', 0));
        Assert.Equal(1, AsciiString.indexOf( Seq("aabaabaa"), 'a', 1));
        Assert.Equal(3, AsciiString.indexOf( Seq("aabaabaa"), 'a', 2));
        Assert.Equal(3, AsciiString.indexOf( Seq("aabdabaa"), 'd', 1));
    }

    [Fact]
    public void testLastIndexOfCharSequence()
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e' };
        AsciiString ascii = new AsciiString(bytes, 2, 3, false);

        Assert.Equal(0, new AsciiString("abcd").lastIndexOf(Seq("abcd"), 0));
        Assert.Equal(0, new AsciiString("abcd").lastIndexOf(Seq("abc"), 4));
        Assert.Equal(1, new AsciiString("abcd").lastIndexOf(Seq("bcd"), 4));
        Assert.Equal(1, new AsciiString("abcd").lastIndexOf(Seq("bc"), 4));
        Assert.Equal(5, new AsciiString("abcdabcd").lastIndexOf(Seq("bcd"), 10));
        Assert.Equal(0, new AsciiString("abcd", 1, 2).lastIndexOf(Seq("bc"), 2));
        Assert.Equal(0, new AsciiString("abcd", 1, 3).lastIndexOf(Seq("bcd"), 3));
        Assert.Equal(1, new AsciiString("abcdabcd", 4, 4).lastIndexOf(Seq("bcd"), 4));
        Assert.Equal(3, new AsciiString("012345").lastIndexOf(Seq("345"), 3));
        Assert.Equal(3, new AsciiString("012345").lastIndexOf(Seq("345"), 6));
        Assert.Equal(1, ascii.lastIndexOf(Seq("de"), 3));
        Assert.Equal(0, ascii.lastIndexOf(Seq("cde"), 3));

        // Test with empty string
        Assert.Equal(0, new AsciiString("abcd").lastIndexOf(Seq(""), 0));
        Assert.Equal(1, new AsciiString("abcd").lastIndexOf(Seq(""), 1));
        Assert.Equal(3, new AsciiString("abcd", 1, 3).lastIndexOf(Seq(""), 4));
        Assert.Equal(3, ascii.lastIndexOf(Seq(""), 3));

        // Test not found
        Assert.Equal(-1, new AsciiString("abcd").lastIndexOf(Seq("abcde"), 0));
        Assert.Equal(-1, new AsciiString("abcdbc").lastIndexOf(Seq("bce"), 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 3).lastIndexOf(Seq("abc"), 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 2).lastIndexOf(Seq("bd"), 0));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf(Seq("345"), 2));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf(Seq("abc"), 3));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf(Seq("abc"), 0));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf(Seq("abcdefghi"), 0));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf(Seq("abcdefghi"), 4));
        Assert.Equal(-1, ascii.lastIndexOf(Seq("a"), 3));
        Assert.Equal(-1, ascii.lastIndexOf(Seq("abc"), 3));
        Assert.Equal(-1, ascii.lastIndexOf(Seq("ce"), 3));
    }

    [Fact]
    public void testReplace()
    {
        AsciiString abcd = new AsciiString("abcd");
        Assert.Equal(new AsciiString("adcd"), abcd.replace('b', 'd'));
        Assert.Equal(new AsciiString("dbcd"), abcd.replace('a', 'd'));
        Assert.Equal(new AsciiString("abca"), abcd.replace('d', 'a'));
        Assert.Same(abcd, abcd.replace('x', 'a'));
        Assert.Equal(new AsciiString("cc"), new AsciiString("abcd", 1, 2).replace('b', 'c'));
        Assert.Equal(new AsciiString("bb"), new AsciiString("abcd", 1, 2).replace('c', 'b'));
        Assert.Equal(new AsciiString("bddd"), new AsciiString("abcdc", 1, 4).replace('c', 'd'));
        Assert.Equal(new AsciiString("xbcxd"), new AsciiString("abcada", 0, 5).replace('a', 'x'));
    }

    [Fact]
    public void testSubStringHashCode()
    {
        //two "123"s
        Assert.Equal(AsciiString.hashCode( Seq("123")), AsciiString.hashCode( Seq("a123".Substring(1))));
    }

    [Fact]
    public void testIndexOf()
    {
        AsciiString foo = new AsciiString("This is a test");
        int i1 = foo.indexOf(' ', 0);
        Assert.Equal(4, i1);
        int i2 = foo.indexOf(' ', i1 + 1);
        Assert.Equal(7, i2);
        int i3 = foo.indexOf(' ', i2 + 1);
        Assert.Equal(9, i3);
        Assert.True(i3 + 1 < foo.length());
        int i4 = foo.indexOf(' ', i3 + 1);
        Assert.Equal(i4, -1);
    }

    [Fact]
    public void testToLowerCase()
    {
        AsciiString foo = new AsciiString("This is a tesT");
        Assert.Equal("this is a test", foo.toLowerCase().ToString());
    }

    [Fact]
    public void testToLowerCaseForOddLengths()
    {
        AsciiString foo = new AsciiString("This is a test!");
        Assert.Equal("this is a test!", foo.toLowerCase().ToString());
    }

    [Fact]
    public void testToLowerCaseLong()
    {
        AsciiString foo = new AsciiString("This is a test for longer sequences");
        Assert.Equal("this is a test for longer sequences", foo.toLowerCase().ToString());
    }

    [Fact]
    public void testToUpperCase()
    {
        AsciiString foo = new AsciiString("This is a tesT");
        Assert.Equal("THIS IS A TEST", foo.toUpperCase().ToString());
    }

    [Fact]
    public void testToUpperCaseLong()
    {
        AsciiString foo = new AsciiString("This is a test for longer sequences");
        Assert.Equal("THIS IS A TEST FOR LONGER SEQUENCES", foo.toUpperCase().ToString());
    }

    [Fact]
    public void testRegionMatchesReturnsTrueForEqualRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        AsciiString world = new AsciiString("World");
        Assert.True(AsciiString.regionMatches(str, false, 0, hello, 0, 5));
        Assert.True(AsciiString.regionMatches(str, false, 7, world, 0, 5));
    }

    [Fact]
    public void testRegionMatchesReturnsFalseForDifferentRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.regionMatches(str, false, 0, world, 0, 5));
        Assert.False(AsciiString.regionMatches(str, false, 7, hello, 0, 5));
    }

    [Fact]
    public void testRegionMatchesIgnoreCaseReturnsTrueForEqualRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("hello");
        AsciiString world = new AsciiString("world");
        Assert.True(AsciiString.regionMatches(str, true, 0, hello, 0, 5));
        Assert.True(AsciiString.regionMatches(str, true, 7, world, 0, 5));
    }

    [Fact]
    public void testRegionMatchesIgnoreCaseReturnsFalseForDifferentRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.regionMatches(str, true, 0, world, 0, 5));
        Assert.False(AsciiString.regionMatches(str, true, 7, hello, 0, 5));
    }

    [Fact]
    public void testRegionMatchesAsciiReturnsTrueForEqualRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        AsciiString world = new AsciiString("World");
        Assert.True(AsciiString.regionMatchesAscii(str, false, 0, hello, 0, 5));
        Assert.True(AsciiString.regionMatchesAscii(str, false, 7, world, 0, 5));
    }

    [Fact]
    public void testRegionMatchesAsciiReturnsFalseForDifferentRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.regionMatchesAscii(str, false, 0, world, 0, 5));
        Assert.False(AsciiString.regionMatchesAscii(str, false, 7, hello, 0, 5));
    }

    [Fact]
    public void testRegionMatchesAsciiIgnoreCaseReturnsTrueForEqualRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("hello");
        AsciiString world = new AsciiString("world");
        Assert.True(AsciiString.regionMatchesAscii(str, true, 0, hello, 0, 5));
        Assert.True(AsciiString.regionMatchesAscii(str, true, 7, world, 0, 5));
    }

    [Fact]
    public void testRegionMatchesAsciiIgnoreCaseReturnsFalseForDifferentRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.regionMatchesAscii(str, true, 0, world, 0, 5));
        Assert.False(AsciiString.regionMatchesAscii(str, true, 7, hello, 0, 5));
    }

    [Fact]
    public void testRegionMatchesHandlesOutOfBounds()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        Assert.False(AsciiString.regionMatches(str, false, -1, hello, 0, 5));
        Assert.False(AsciiString.regionMatches(str, false, 0, hello, -1, 5));
        Assert.False(AsciiString.regionMatches(str, false, 0, hello, 0, 20));
    }

    [Fact]
    public void testRegionMatchesAsciiHandlesOutOfBounds()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        Assert.False(AsciiString.regionMatchesAscii(str, false, -1, hello, 0, 5));
        Assert.False(AsciiString.regionMatchesAscii(str, false, 0, hello, -1, 5));
    }

    // Existing heterogeneous header-sequence APIs are tested through a local
    // adapter. Native construction uses string/span; this helper is not public API.
    private static ICharSequence Seq(object value) => value switch
    {
        null => null,
        ICharSequence sequence => sequence,
        string text => new StringCharSequence(text),
        StringBuilder builder => new StringCharSequence(builder.ToString()),
        _ => throw new ArgumentException("Unsupported test sequence", nameof(value))
    };
    [Fact]
    public void testCachedWithAsciiString() {
        // Pure ASCII strings should reuse the original string to preserve identity
        string ascii = "hello";
        AsciiString cached = AsciiString.Cached(ascii);
        Assert.Equal(ascii, cached.ToString());
        Assert.Same(ascii, cached.ToString());
        Assert.Equal(ascii.Length, cached.length());
        Assert.True(cached.contentEquals(Seq(ascii)));
    }

    [Fact]
    public void testCachedWithAsciiLatin1String() {
        // Latin-1 strings (chars 128-255) should reuse the original string to preserve identity
        string latin1 = "h" + (char) 233 + "llo"; // héllo
        AsciiString cached = AsciiString.Cached(latin1);
        Assert.Equal(latin1, cached.ToString());
        Assert.Same(latin1, cached.ToString());
        Assert.Equal(latin1.Length, cached.length());
        Assert.True(cached.contentEquals(Seq(latin1)));
    }

    [Fact]
    public void testCachedSanitizesNonLatin1String() {
        // Chars > 255 should be sanitized to '?' in the cached string to match the byte content
        string nonLatin1 = "test" + (char) 0x1234 + "ing";
        AsciiString cached = AsciiString.Cached(nonLatin1);
        // The char 0x1234 gets converted to '?' by c2b, so toString should reflect that
        Assert.Equal("test?ing", cached.ToString());
    }

    [Fact]
    public void testCachedEmptyString() {
        AsciiString cached = AsciiString.Cached("");
        Assert.Equal("", cached.ToString());
        Assert.True(cached.isEmpty());
    }

    [Fact]
    public void testCachedStringMatchesByteContent() {
        // The cached string should always match the byte content round-trip
        string nonLatin1 = "a" + (char) 0x4321 + "b";
        AsciiString cached = AsciiString.Cached(nonLatin1);
        // Manually compute the expected sanitized string from the byte array
        StringBuilder expected = new StringBuilder();
        foreach (byte b in cached.toByteArray()) {
            expected.Append((char) (b & 0xFF));
        }
        Assert.Equal(expected.ToString(), cached.ToString());
    }

    [Fact]
    public void testCachedWithAllAsciiConstants() {
        // Constants used in the codebase should be unaffected
        AsciiString host = AsciiString.Cached("host");
        Assert.Equal("host", host.ToString());
        AsciiString method = AsciiString.Cached(":method");
        Assert.Equal(":method", method.ToString());
        AsciiString status = AsciiString.Cached(":status");
        Assert.Equal(":status", status.ToString());
    }
}
