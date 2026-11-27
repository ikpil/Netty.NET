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
    public void TestContentEqualsIgnoreCase()
    {
        byte[] bytes = { 32, (byte)(byte)'a' };
        AsciiString asciiString = new AsciiString(bytes, 1, 1, false);
        // https://github.com/netty/netty/issues/9475
        Assert.False(asciiString.ContentEqualsIgnoreCase(Seq("b")));
        Assert.False(asciiString.ContentEqualsIgnoreCase(new AsciiString("b")));
    }

    [Fact]
    public void TestGetBytesStringBuilder()
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
            byte[] actual = new AsciiString(b.ToString(), charset).ToByteArray();
            Assert.Equal(expected, actual, "failure for " + charset);
        }
    }

    [Fact]
    public void TestGetBytesString()
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
            byte[] actual = new AsciiString(bString, charset).ToByteArray();
            Assert.Equal(expected, actual, "failure for " + charset);
        }
    }

    [Fact]
    public void TestGetBytesAsciiString()
    {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < 1 << 16; ++i)
        {
            b.Append("eéaà");
        }

        string bString = b.ToString();
        // The AsciiString class actually limits the Charset to ISO_8859_1
        byte[] expected = Encoding.Latin1.GetBytes(bString);
        byte[] actual = new AsciiString(bString).ToByteArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TestComparisonWithString()
    {
        string str = "shouldn't fail";
        AsciiString ascii = new AsciiString(str.ToCharArray());
        Assert.Equal(str, ascii.ToString());
    }

    [Fact]
    public void SubSequenceTest()
    {
        byte[] init = { (byte)'t', (byte)'h', (byte)'i', (byte)'s', (byte)' ', (byte)'i', (byte)'s', (byte)' ', (byte)'a', (byte)' ', (byte)'t', (byte)'e', (byte)'s', (byte)'t' };
        AsciiString ascii = new AsciiString(init);
        int start = 2;
        int end = init.Length;
        AsciiString sub1 = ascii.SubSequence(start, end, false);
        AsciiString sub2 = ascii.SubSequence(start, end, true);
        Assert.Equal(sub1.GetHashCode(), sub2.GetHashCode());
        Assert.Equal(sub1, sub2);
        for (int i = start; i < end; ++i)
        {
            Assert.Equal(init[i], sub1.ByteAt(i - start));
        }
    }

    [Fact]
    public void TestContains()
    {
        string[] falseLhs = { null, "a", "aa", "aaa" };
        string[] falseRhs = { null, "b", "ba", "baa" };
        for (int i = 0; i < falseLhs.Length; ++i)
        {
            for (int j = 0; j < falseRhs.Length; ++j)
            {
                AssertContains(falseLhs[i], falseRhs[i], false, false);
            }
        }

        AssertContains("", "", true, true);
        AssertContains("AsfdsF", "", true, true);
        AssertContains("", "b", false, false);
        AssertContains("a", "a", true, true);
        AssertContains("a", "b", false, false);
        AssertContains("a", "A", false, true);
        string b = "xyz";
        string a = b;
        AssertContains(a, b, true, true);

        a = "a" + b;
        AssertContains(a, b, true, true);

        a = b + "a";
        AssertContains(a, b, true, true);

        a = "a" + b + "a";
        AssertContains(a, b, true, true);

        b = "xYz";
        a = "xyz";
        AssertContains(a, b, false, true);

        b = "xYz";
        a = "xyzxxxXyZ" + b + "aaa";
        AssertContains(a, b, true, true);

        b = "foOo";
        a = "fooofoO";
        AssertContains(a, b, false, true);

        b = "Content-Equals: 10000";
        a = "content-equals: 1000";
        AssertContains(a, b, false, false);
        a += "0";
        AssertContains(a, b, false, true);
    }

    private static void AssertContains(string a, string b, bool caseSensitiveEquals, bool caseInsenstaiveEquals)
    {
        Assert.Equal(caseSensitiveEquals, Contains(Seq(a), Seq(b)));
        Assert.Equal(caseInsenstaiveEquals, ContainsIgnoreCase(Seq(a), Seq(b)));
    }

    [Fact]
    public void TestCaseSensitivity()
    {
        int i = 0;
        for (; i < 32; i++)
        {
            DoCaseSensitivity(i);
        }

        int min = i;
        int max = 4000;
        int len = r.Next((max - min) + 1) + min;
        DoCaseSensitivity(len);
    }

    private static void DoCaseSensitivity(int len)
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
        Assert.Equal(upperCaseExpected, AsciiString.HashCode( Seq(upperCaseBuilder)), errorString);
        Assert.Equal(upperCaseExpected, AsciiString.HashCode( Seq(upperCaseString)), errorString);
        Assert.Equal(upperCaseExpected, upperCaseAscii.GetHashCode(), errorString);

        // Test lower case hash codes are equal
        int lowerCaseExpected = lowerCaseAscii.GetHashCode();
        Assert.Equal(lowerCaseExpected, AsciiString.HashCode( Seq(lowerCaseAscii)), errorString);
        Assert.Equal(lowerCaseExpected, AsciiString.HashCode( Seq(lowerCaseString)), errorString);
        Assert.Equal(lowerCaseExpected, lowerCaseAscii.GetHashCode(), errorString);

        // Test case insensitive hash codes are equal
        int expectedCaseInsensitive = lowerCaseAscii.GetHashCode();
        Assert.Equal(expectedCaseInsensitive, AsciiString.HashCode( Seq(upperCaseBuilder)), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.HashCode( Seq(upperCaseString)), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.HashCode( Seq(lowerCaseString)), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.HashCode( Seq(lowerCaseAscii)), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.HashCode( Seq(upperCaseAscii)), errorString);
        Assert.Equal(expectedCaseInsensitive, lowerCaseAscii.GetHashCode(), errorString);
        Assert.Equal(expectedCaseInsensitive, upperCaseAscii.GetHashCode(), errorString);

        // Test that opposite cases are equal
        Assert.Equal(lowerCaseAscii.GetHashCode(), AsciiString.HashCode( Seq(upperCaseString)), errorString);
        Assert.Equal(upperCaseAscii.GetHashCode(), AsciiString.HashCode( Seq(lowerCaseString)), errorString);
    }

    [Fact]
    public void CaseInsensitiveHasherCharBuffer()
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
        Assert.Equal(AsciiString.HashCode( Seq(s1)), AsciiString.HashCode( Seq(buffer)));
    }

    [Fact]
    public void TestBooleanUtilityMethods()
    {
        Assert.True(new AsciiString(new byte[] { 1 }).ParseBoolean());
        Assert.False(AsciiString.EMPTY_STRING.ParseBoolean());
        Assert.False(new AsciiString(new byte[] { 0 }).ParseBoolean());
        Assert.True(new AsciiString(new byte[] { 5 }).ParseBoolean());
        Assert.True(new AsciiString(new byte[] { 2, 0 }).ParseBoolean());
    }

    [Fact]
    public void TestEqualsIgnoreCase()
    {
        Assert.True(AsciiString.ContentEqualsIgnoreCase( Seq(null), Seq(null)));
        Assert.False(AsciiString.ContentEqualsIgnoreCase( Seq(null), Seq("foo")));
        Assert.False(AsciiString.ContentEqualsIgnoreCase( Seq("bar"), Seq(null)));
        Assert.True(AsciiString.ContentEqualsIgnoreCase( Seq("FoO"), Seq("fOo")));
        Assert.False(AsciiString.ContentEqualsIgnoreCase( Seq("FoO"), Seq("bar")));
        Assert.False(AsciiString.ContentEqualsIgnoreCase( Seq("Foo"), Seq("foobar")));
        Assert.False(AsciiString.ContentEqualsIgnoreCase( Seq("foobar"), Seq("Foo")));

        // Test variations (Ascii + String, Ascii + Ascii, String + Ascii)
        Assert.True(AsciiString.ContentEqualsIgnoreCase( Seq(new AsciiString("FoO")), Seq("fOo")));
        Assert.True(AsciiString.ContentEqualsIgnoreCase( Seq(new AsciiString("FoO")), Seq(new AsciiString("fOo"))));
        Assert.True(AsciiString.ContentEqualsIgnoreCase( Seq("FoO"), Seq(new AsciiString("fOo"))));

        // Test variations (Ascii + String, Ascii + Ascii, String + Ascii)
        Assert.False(AsciiString.ContentEqualsIgnoreCase( Seq(new AsciiString("FoO")), Seq("bAr")));
        Assert.False(AsciiString.ContentEqualsIgnoreCase( Seq(new AsciiString("FoO")), Seq(new AsciiString("bAr"))));
        Assert.False(AsciiString.ContentEqualsIgnoreCase( Seq("FoO"), Seq(new AsciiString("bAr"))));
    }

    [Fact]
    public void TestIndexOfIgnoreCase()
    {
        Assert.Equal(-1, AsciiString.IndexOfIgnoreCase( Seq(null), Seq("abc"), 1));
        Assert.Equal(-1, AsciiString.IndexOfIgnoreCase( Seq("abc"), Seq(null), 1));
        Assert.Equal(0, AsciiString.IndexOfIgnoreCase( Seq(""), Seq(""), 0));
        Assert.Equal(0, AsciiString.IndexOfIgnoreCase( Seq("aabaabaa"), Seq("A"), 0));
        Assert.Equal(2, AsciiString.IndexOfIgnoreCase( Seq("aabaabaa"), Seq("B"), 0));
        Assert.Equal(1, AsciiString.IndexOfIgnoreCase( Seq("aabaabaa"), Seq("AB"), 0));
        Assert.Equal(5, AsciiString.IndexOfIgnoreCase( Seq("aabaabaa"), Seq("B"), 3));
        Assert.Equal(-1, AsciiString.IndexOfIgnoreCase( Seq("aabaabaa"), Seq("B"), 9));
        Assert.Equal(2, AsciiString.IndexOfIgnoreCase( Seq("aabaabaa"), Seq("B"), -1));
        Assert.Equal(2, AsciiString.IndexOfIgnoreCase( Seq("aabaabaa"), Seq(""), 2));
        Assert.Equal(-1, AsciiString.IndexOfIgnoreCase( Seq("abc"), Seq(""), 9));
        Assert.Equal(0, AsciiString.IndexOfIgnoreCase( Seq("ãabaabaa"), Seq("Ã"), 0));
    }

    [Fact]
    public void TestIndexOfIgnoreCaseAscii()
    {
        Assert.Equal(-1, AsciiString.IndexOfIgnoreCaseAscii( Seq(null), Seq("abc"), 1));
        Assert.Equal(-1, AsciiString.IndexOfIgnoreCaseAscii( Seq("abc"), Seq(null), 1));
        Assert.Equal(0, AsciiString.IndexOfIgnoreCaseAscii( Seq(""), Seq(""), 0));
        Assert.Equal(0, AsciiString.IndexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("A"), 0));
        Assert.Equal(2, AsciiString.IndexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("B"), 0));
        Assert.Equal(1, AsciiString.IndexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("AB"), 0));
        Assert.Equal(5, AsciiString.IndexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("B"), 3));
        Assert.Equal(-1, AsciiString.IndexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("B"), 9));
        Assert.Equal(2, AsciiString.IndexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq("B"), -1));
        Assert.Equal(2, AsciiString.IndexOfIgnoreCaseAscii( Seq("aabaabaa"), Seq(""), 2));
        Assert.Equal(-1, AsciiString.IndexOfIgnoreCaseAscii( Seq("abc"), Seq(""), 9));
    }

    [Fact]
    public void TestTrim()
    {
        Assert.Equal("", AsciiString.EMPTY_STRING.Trim().ToString());
        Assert.Equal("abc", new AsciiString("  abc").Trim().ToString());
        Assert.Equal("abc", new AsciiString("abc  ").Trim().ToString());
        Assert.Equal("abc", new AsciiString("  abc  ").Trim().ToString());
    }

    [Fact]
    public void TestIndexOfChar()
    {
        Assert.Equal(-1, AsciiString.IndexOf( Seq(null), 'a', 0));
        Assert.Equal(-1, new AsciiString("").IndexOf('a', 0));
        Assert.Equal(-1, new AsciiString("abc").IndexOf('d', 0));
        Assert.Equal(-1, new AsciiString("aabaabaa").IndexOf('A', 0));
        Assert.Equal(0, new AsciiString("aabaabaa").IndexOf('a', 0));
        Assert.Equal(1, new AsciiString("aabaabaa").IndexOf('a', 1));
        Assert.Equal(3, new AsciiString("aabaabaa").IndexOf('a', 2));
        Assert.Equal(3, new AsciiString("aabdabaa").IndexOf('d', 1));
        Assert.Equal(1, new AsciiString("abcd", 1, 2).IndexOf('c', 0));
        Assert.Equal(2, new AsciiString("abcd", 1, 3).IndexOf('d', 2));
        Assert.Equal(0, new AsciiString("abcd", 1, 2).IndexOf('b', 0));
        Assert.Equal(-1, new AsciiString("abcd", 0, 2).IndexOf('c', 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 3).IndexOf('a', 0));
    }

    [Fact]
    public void TestIndexOfCharSequence()
    {
        Assert.Equal(0, new AsciiString("abcd").IndexOf(Seq("abcd"), 0));
        Assert.Equal(0, new AsciiString("abcd").IndexOf(Seq("abc"), 0));
        Assert.Equal(1, new AsciiString("abcd").IndexOf(Seq("bcd"), 0));
        Assert.Equal(1, new AsciiString("abcd").IndexOf(Seq("bc"), 0));
        Assert.Equal(1, new AsciiString("abcdabcd").IndexOf(Seq("bcd"), 0));
        Assert.Equal(0, new AsciiString("abcd", 1, 2).IndexOf(Seq("bc"), 0));
        Assert.Equal(0, new AsciiString("abcd", 1, 3).IndexOf(Seq("bcd"), 0));
        Assert.Equal(1, new AsciiString("abcdabcd", 4, 4).IndexOf(Seq("bcd"), 0));
        Assert.Equal(3, new AsciiString("012345").IndexOf(Seq("345"), 3));
        Assert.Equal(3, new AsciiString("012345").IndexOf(Seq("345"), 0));

        // Test with empty string
        Assert.Equal(0, new AsciiString("abcd").IndexOf(Seq(""), 0));
        Assert.Equal(1, new AsciiString("abcd").IndexOf(Seq(""), 1));
        Assert.Equal(3, new AsciiString("abcd", 1, 3).IndexOf(Seq(""), 4));

        // Test not found
        Assert.Equal(-1, new AsciiString("abcd").IndexOf(Seq("abcde"), 0));
        Assert.Equal(-1, new AsciiString("abcdbc").IndexOf(Seq("bce"), 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 3).IndexOf(Seq("abc"), 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 2).IndexOf(Seq("bd"), 0));
        Assert.Equal(-1, new AsciiString("012345").IndexOf(Seq("345"), 4));
        Assert.Equal(-1, new AsciiString("012345").IndexOf(Seq("abc"), 3));
        Assert.Equal(-1, new AsciiString("012345").IndexOf(Seq("abc"), 0));
        Assert.Equal(-1, new AsciiString("012345").IndexOf(Seq("abcdefghi"), 0));
        Assert.Equal(-1, new AsciiString("012345").IndexOf(Seq("abcdefghi"), 4));
    }

    [Fact]
    public void TestStaticIndexOfChar()
    {
        Assert.Equal(-1, AsciiString.IndexOf( Seq(null), 'a', 0));
        Assert.Equal(-1, AsciiString.IndexOf( Seq(""), 'a', 0));
        Assert.Equal(-1, AsciiString.IndexOf( Seq("abc"), 'd', 0));
        Assert.Equal(-1, AsciiString.IndexOf( Seq("aabaabaa"), 'A', 0));
        Assert.Equal(0, AsciiString.IndexOf( Seq("aabaabaa"), 'a', 0));
        Assert.Equal(1, AsciiString.IndexOf( Seq("aabaabaa"), 'a', 1));
        Assert.Equal(3, AsciiString.IndexOf( Seq("aabaabaa"), 'a', 2));
        Assert.Equal(3, AsciiString.IndexOf( Seq("aabdabaa"), 'd', 1));
    }

    [Fact]
    public void TestLastIndexOfCharSequence()
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e' };
        AsciiString ascii = new AsciiString(bytes, 2, 3, false);

        Assert.Equal(0, new AsciiString("abcd").LastIndexOf(Seq("abcd"), 0));
        Assert.Equal(0, new AsciiString("abcd").LastIndexOf(Seq("abc"), 4));
        Assert.Equal(1, new AsciiString("abcd").LastIndexOf(Seq("bcd"), 4));
        Assert.Equal(1, new AsciiString("abcd").LastIndexOf(Seq("bc"), 4));
        Assert.Equal(5, new AsciiString("abcdabcd").LastIndexOf(Seq("bcd"), 10));
        Assert.Equal(0, new AsciiString("abcd", 1, 2).LastIndexOf(Seq("bc"), 2));
        Assert.Equal(0, new AsciiString("abcd", 1, 3).LastIndexOf(Seq("bcd"), 3));
        Assert.Equal(1, new AsciiString("abcdabcd", 4, 4).LastIndexOf(Seq("bcd"), 4));
        Assert.Equal(3, new AsciiString("012345").LastIndexOf(Seq("345"), 3));
        Assert.Equal(3, new AsciiString("012345").LastIndexOf(Seq("345"), 6));
        Assert.Equal(1, ascii.LastIndexOf(Seq("de"), 3));
        Assert.Equal(0, ascii.LastIndexOf(Seq("cde"), 3));

        // Test with empty string
        Assert.Equal(0, new AsciiString("abcd").LastIndexOf(Seq(""), 0));
        Assert.Equal(1, new AsciiString("abcd").LastIndexOf(Seq(""), 1));
        Assert.Equal(3, new AsciiString("abcd", 1, 3).LastIndexOf(Seq(""), 4));
        Assert.Equal(3, ascii.LastIndexOf(Seq(""), 3));

        // Test not found
        Assert.Equal(-1, new AsciiString("abcd").LastIndexOf(Seq("abcde"), 0));
        Assert.Equal(-1, new AsciiString("abcdbc").LastIndexOf(Seq("bce"), 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 3).LastIndexOf(Seq("abc"), 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 2).LastIndexOf(Seq("bd"), 0));
        Assert.Equal(-1, new AsciiString("012345").LastIndexOf(Seq("345"), 2));
        Assert.Equal(-1, new AsciiString("012345").LastIndexOf(Seq("abc"), 3));
        Assert.Equal(-1, new AsciiString("012345").LastIndexOf(Seq("abc"), 0));
        Assert.Equal(-1, new AsciiString("012345").LastIndexOf(Seq("abcdefghi"), 0));
        Assert.Equal(-1, new AsciiString("012345").LastIndexOf(Seq("abcdefghi"), 4));
        Assert.Equal(-1, ascii.LastIndexOf(Seq("a"), 3));
        Assert.Equal(-1, ascii.LastIndexOf(Seq("abc"), 3));
        Assert.Equal(-1, ascii.LastIndexOf(Seq("ce"), 3));
    }

    [Fact]
    public void TestReplace()
    {
        AsciiString abcd = new AsciiString("abcd");
        Assert.Equal(new AsciiString("adcd"), abcd.Replace('b', 'd'));
        Assert.Equal(new AsciiString("dbcd"), abcd.Replace('a', 'd'));
        Assert.Equal(new AsciiString("abca"), abcd.Replace('d', 'a'));
        Assert.Same(abcd, abcd.Replace('x', 'a'));
        Assert.Equal(new AsciiString("cc"), new AsciiString("abcd", 1, 2).Replace('b', 'c'));
        Assert.Equal(new AsciiString("bb"), new AsciiString("abcd", 1, 2).Replace('c', 'b'));
        Assert.Equal(new AsciiString("bddd"), new AsciiString("abcdc", 1, 4).Replace('c', 'd'));
        Assert.Equal(new AsciiString("xbcxd"), new AsciiString("abcada", 0, 5).Replace('a', 'x'));
    }

    [Fact]
    public void TestSubStringHashCode()
    {
        //two "123"s
        Assert.Equal(AsciiString.HashCode( Seq("123")), AsciiString.HashCode( Seq("a123".Substring(1))));
    }

    [Fact]
    public void TestIndexOf()
    {
        AsciiString foo = new AsciiString("This is a test");
        int i1 = foo.IndexOf(' ', 0);
        Assert.Equal(4, i1);
        int i2 = foo.IndexOf(' ', i1 + 1);
        Assert.Equal(7, i2);
        int i3 = foo.IndexOf(' ', i2 + 1);
        Assert.Equal(9, i3);
        Assert.True(i3 + 1 < foo.Length());
        int i4 = foo.IndexOf(' ', i3 + 1);
        Assert.Equal(i4, -1);
    }

    [Fact]
    public void TestToLowerCase()
    {
        AsciiString foo = new AsciiString("This is a tesT");
        Assert.Equal("this is a test", foo.ToLowerCase().ToString());
    }

    [Fact]
    public void TestToLowerCaseForOddLengths()
    {
        AsciiString foo = new AsciiString("This is a test!");
        Assert.Equal("this is a test!", foo.ToLowerCase().ToString());
    }

    [Fact]
    public void TestToLowerCaseLong()
    {
        AsciiString foo = new AsciiString("This is a test for longer sequences");
        Assert.Equal("this is a test for longer sequences", foo.ToLowerCase().ToString());
    }

    [Fact]
    public void TestToUpperCase()
    {
        AsciiString foo = new AsciiString("This is a tesT");
        Assert.Equal("THIS IS A TEST", foo.ToUpperCase().ToString());
    }

    [Fact]
    public void TestToUpperCaseLong()
    {
        AsciiString foo = new AsciiString("This is a test for longer sequences");
        Assert.Equal("THIS IS A TEST FOR LONGER SEQUENCES", foo.ToUpperCase().ToString());
    }

    [Fact]
    public void TestRegionMatchesReturnsTrueForEqualRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        AsciiString world = new AsciiString("World");
        Assert.True(AsciiString.RegionMatches(str, false, 0, hello, 0, 5));
        Assert.True(AsciiString.RegionMatches(str, false, 7, world, 0, 5));
    }

    [Fact]
    public void TestRegionMatchesReturnsFalseForDifferentRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.RegionMatches(str, false, 0, world, 0, 5));
        Assert.False(AsciiString.RegionMatches(str, false, 7, hello, 0, 5));
    }

    [Fact]
    public void TestRegionMatchesIgnoreCaseReturnsTrueForEqualRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("hello");
        AsciiString world = new AsciiString("world");
        Assert.True(AsciiString.RegionMatches(str, true, 0, hello, 0, 5));
        Assert.True(AsciiString.RegionMatches(str, true, 7, world, 0, 5));
    }

    [Fact]
    public void TestRegionMatchesIgnoreCaseReturnsFalseForDifferentRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.RegionMatches(str, true, 0, world, 0, 5));
        Assert.False(AsciiString.RegionMatches(str, true, 7, hello, 0, 5));
    }

    [Fact]
    public void TestRegionMatchesAsciiReturnsTrueForEqualRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        AsciiString world = new AsciiString("World");
        Assert.True(AsciiString.RegionMatchesAscii(str, false, 0, hello, 0, 5));
        Assert.True(AsciiString.RegionMatchesAscii(str, false, 7, world, 0, 5));
    }

    [Fact]
    public void TestRegionMatchesAsciiReturnsFalseForDifferentRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.RegionMatchesAscii(str, false, 0, world, 0, 5));
        Assert.False(AsciiString.RegionMatchesAscii(str, false, 7, hello, 0, 5));
    }

    [Fact]
    public void TestRegionMatchesAsciiIgnoreCaseReturnsTrueForEqualRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("hello");
        AsciiString world = new AsciiString("world");
        Assert.True(AsciiString.RegionMatchesAscii(str, true, 0, hello, 0, 5));
        Assert.True(AsciiString.RegionMatchesAscii(str, true, 7, world, 0, 5));
    }

    [Fact]
    public void TestRegionMatchesAsciiIgnoreCaseReturnsFalseForDifferentRegions()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.RegionMatchesAscii(str, true, 0, world, 0, 5));
        Assert.False(AsciiString.RegionMatchesAscii(str, true, 7, hello, 0, 5));
    }

    [Fact]
    public void TestRegionMatchesHandlesOutOfBounds()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        Assert.False(AsciiString.RegionMatches(str, false, -1, hello, 0, 5));
        Assert.False(AsciiString.RegionMatches(str, false, 0, hello, -1, 5));
        Assert.False(AsciiString.RegionMatches(str, false, 0, hello, 0, 20));
    }

    [Fact]
    public void TestRegionMatchesAsciiHandlesOutOfBounds()
    {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        Assert.False(AsciiString.RegionMatchesAscii(str, false, -1, hello, 0, 5));
        Assert.False(AsciiString.RegionMatchesAscii(str, false, 0, hello, -1, 5));
    }

    // Existing heterogeneous header-sequence APIs are tested through a local
    // adapter. Native construction uses string/span; this helper is not public API.
    private static ICharSequence Seq(object value)
    {
        return value switch
        {
            null => null,
            ICharSequence sequence => sequence,
            string text => new StringCharSequence(text),
            StringBuilder builder => new StringCharSequence(builder.ToString()),
            _ => throw new ArgumentException("Unsupported test sequence", nameof(value))
        };
    }

    [Fact]
    public void TestCachedWithAsciiString()
    {
        // Pure ASCII strings should reuse the original string to preserve identity
        string ascii = "hello";
        AsciiString cached = AsciiString.Cached(ascii);
        Assert.Equal(ascii, cached.ToString());
        Assert.Same(ascii, cached.ToString());
        Assert.Equal(ascii.Length, cached.Length());
        Assert.True(cached.ContentEquals(Seq(ascii)));
    }

    [Fact]
    public void TestCachedWithAsciiLatin1String()
    {
        // Latin-1 strings (chars 128-255) should reuse the original string to preserve identity
        string latin1 = "h" + (char) 233 + "llo"; // héllo
        AsciiString cached = AsciiString.Cached(latin1);
        Assert.Equal(latin1, cached.ToString());
        Assert.Same(latin1, cached.ToString());
        Assert.Equal(latin1.Length, cached.Length());
        Assert.True(cached.ContentEquals(Seq(latin1)));
    }

    [Fact]
    public void TestCachedSanitizesNonLatin1String()
    {
        // Chars > 255 should be sanitized to '?' in the cached string to match the byte content
        string nonLatin1 = "test" + (char) 0x1234 + "ing";
        AsciiString cached = AsciiString.Cached(nonLatin1);
        // The char 0x1234 gets converted to '?' by c2b, so toString should reflect that
        Assert.Equal("test?ing", cached.ToString());
    }

    [Fact]
    public void TestCachedEmptyString()
    {
        AsciiString cached = AsciiString.Cached("");
        Assert.Equal("", cached.ToString());
        Assert.True(cached.IsEmpty());
    }

    [Fact]
    public void TestCachedStringMatchesByteContent()
    {
        // The cached string should always match the byte content round-trip
        string nonLatin1 = "a" + (char) 0x4321 + "b";
        AsciiString cached = AsciiString.Cached(nonLatin1);
        // Manually compute the expected sanitized string from the byte array
        StringBuilder expected = new StringBuilder();
        foreach (byte b in cached.ToByteArray())
        {
            expected.Append((char) (b & 0xFF));
        }
        Assert.Equal(expected.ToString(), cached.ToString());
    }

    [Fact]
    public void TestCachedWithAllAsciiConstants()
    {
        // Constants used in the codebase should be unaffected
        AsciiString host = AsciiString.Cached("host");
        Assert.Equal("host", host.ToString());
        AsciiString method = AsciiString.Cached(":method");
        Assert.Equal(":method", method.ToString());
        AsciiString status = AsciiString.Cached(":status");
        Assert.Equal(":status", status.ToString());
    }
}
