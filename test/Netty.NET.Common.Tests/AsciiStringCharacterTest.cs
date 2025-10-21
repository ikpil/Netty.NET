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

using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Common.Tests;/**
 * Test character encoding and case insensitivity for the {@link AsciiString} class
 */
public class AsciiStringCharacterTest {
    private static readonly Random r = new Random();

    [Fact]
    public void testContentEqualsIgnoreCase() {
        byte[] bytes = { 32, 'a' };
        AsciiString asciiString = new AsciiString(bytes, 1, 1, false);
        // https://github.com/netty/netty/issues/9475
        Assert.False(asciiString.contentEqualsIgnoreCase("b"));
        Assert.False(asciiString.contentEqualsIgnoreCase(AsciiString.of("b")));
    }

    [Fact]
    public void testGetBytesStringBuilder() {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < 1 << 16; ++i) {
            b.Append("eéaà");
        }
        string bString = b.ToString();
        Encoding[] charsets = CharsetUtil.values();
        for (int i = 0; i < charsets.Length; ++i) {
            Encoding charset = charsets[i];
            byte[] expected = bString.getBytes(charset);
            byte[] actual = new AsciiString(b, charset).toByteArray();
            Assert.Equal(expected, actual, "failure for " + charset);
        }
    }

    [Fact]
    public void testGetBytesString() {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < 1 << 16; ++i) {
            b.Append("eéaà");
        }
        string bString = b.ToString();
        Encoding[] charsets = CharsetUtil.values();
        for (int i = 0; i < charsets.Length; ++i) {
            final Encoding charset = charsets[i];
            byte[] expected = bString.getBytes(charset);
            byte[] actual = new AsciiString(bString, charset).toByteArray();
            Assert.Equal(expected, actual, "failure for " + charset);
        }
    }

    [Fact]
    public void testGetBytesAsciiString() {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < 1 << 16; ++i) {
            b.Append("eéaà");
        }
        string bString = b.ToString();
        // The AsciiString class actually limits the Encoding to ISO_8859_1
        byte[] expected = bString.getBytes(CharsetUtil.ISO_8859_1);
        byte[] actual = new AsciiString(bString).toByteArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void testComparisonWithString() {
        string str = "shouldn't fail";
        AsciiString ascii = new AsciiString(str.toCharArray());
        Assert.Equal(str, ascii.ToString());
    }

    [Fact]
    public void subSequenceTest() {
        byte[] init = { 't', 'h', 'i', 's', ' ', 'i', 's', ' ', 'a', ' ', 't', 'e', 's', 't' };
        AsciiString ascii = new AsciiString(init);
        int start = 2;
        int end = init.Length;
        AsciiString sub1 = ascii.subSequence(start, end, false);
        AsciiString sub2 = ascii.subSequence(start, end, true);
        Assert.Equal(sub1.hashCode(), sub2.hashCode());
        Assert.Equal(sub1, sub2);
        for (int i = start; i < end; ++i) {
            Assert.Equal(init[i], sub1.byteAt(i - start));
        }
    }

    [Fact]
    public void testContains() {
        string[] falseLhs = { null, "a", "aa", "aaa" };
        string[] falseRhs = { null, "b", "ba", "baa" };
        for (int i = 0; i < falseLhs.length; ++i) {
            for (int j = 0; j < falseRhs.length; ++j) {
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

    private static void assertContains(string a, string b, bool caseSensitiveEquals, bool caseInsenstaiveEquals) {
        Assert.Equal(caseSensitiveEquals, contains(a, b));
        Assert.Equal(caseInsenstaiveEquals, containsIgnoreCase(a, b));
    }

    [Fact]
    public void testCaseSensitivity() {
        int i = 0;
        for (; i < 32; i++) {
            doCaseSensitivity(i);
        }
        int min = i;
        int max = 4000;
        int len = r.nextInt((max - min) + 1) + min;
        doCaseSensitivity(len);
    }

    private static void doCaseSensitivity(int len) {
        // Build an upper case and lower case string
        int upperA = 'A';
        int upperZ = 'Z';
        int upperToLower = (int) 'a' - upperA;
        byte[] lowerCaseBytes = new byte[len];
        StringBuilder upperCaseBuilder = new StringBuilder(len);
        for (int i = 0; i < len; ++i) {
            char upper = (char) (r.Next((upperZ - upperA) + 1) + upperA);
            upperCaseBuilder.Append(upper);
            lowerCaseBytes[i] = (byte) (upper + upperToLower);
        }
        string upperCaseString = upperCaseBuilder.ToString();
        string lowerCaseString = new string(lowerCaseBytes);
        AsciiString lowerCaseAscii = new AsciiString(lowerCaseBytes, false);
        AsciiString upperCaseAscii = new AsciiString(upperCaseString);
        final string errorString = "len: " + len;
        // Test upper case hash codes are equal
        final int upperCaseExpected = upperCaseAscii.hashCode();
        Assert.Equal(upperCaseExpected, AsciiString.hashCode(upperCaseBuilder), errorString);
        Assert.Equal(upperCaseExpected, AsciiString.hashCode(upperCaseString), errorString);
        Assert.Equal(upperCaseExpected, upperCaseAscii.hashCode(), errorString);

        // Test lower case hash codes are equal
        final int lowerCaseExpected = lowerCaseAscii.hashCode();
        Assert.Equal(lowerCaseExpected, AsciiString.hashCode(lowerCaseAscii), errorString);
        Assert.Equal(lowerCaseExpected, AsciiString.hashCode(lowerCaseString), errorString);
        Assert.Equal(lowerCaseExpected, lowerCaseAscii.hashCode(), errorString);

        // Test case insensitive hash codes are equal
        final int expectedCaseInsensitive = lowerCaseAscii.hashCode();
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode(upperCaseBuilder), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode(upperCaseString), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode(lowerCaseString), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode(lowerCaseAscii), errorString);
        Assert.Equal(expectedCaseInsensitive, AsciiString.hashCode(upperCaseAscii), errorString);
        Assert.Equal(expectedCaseInsensitive, lowerCaseAscii.hashCode(), errorString);
        Assert.Equal(expectedCaseInsensitive, upperCaseAscii.hashCode(), errorString);

        // Test that opposite cases are equal
        Assert.Equal(lowerCaseAscii.hashCode(), AsciiString.hashCode(upperCaseString), errorString);
        Assert.Equal(upperCaseAscii.hashCode(), AsciiString.hashCode(lowerCaseString), errorString);
    }

    [Fact]
    public void caseInsensitiveHasherCharBuffer() {
        string s1 = new string("TRANSFER-ENCODING");
        char[] array = new char[128];
        final int offset = 100;
        for (int i = 0; i < s1.length(); ++i) {
            array[offset + i] = s1.charAt(i);
        }
        CharBuffer buffer = CharBuffer.wrap(array, offset, s1.length());
        Assert.Equal(AsciiString.hashCode(s1), AsciiString.hashCode(buffer));
    }

    [Fact]
    public void testBooleanUtilityMethods() {
        Assert.True(new AsciiString(new byte[] { 1 }).parseBoolean());
        Assert.False(AsciiString.EMPTY_STRING.parseBoolean());
        Assert.False(new AsciiString(new byte[] { 0 }).parseBoolean());
        Assert.True(new AsciiString(new byte[] { 5 }).parseBoolean());
        Assert.True(new AsciiString(new byte[] { 2, 0 }).parseBoolean());
    }

    [Fact]
    public void testEqualsIgnoreCase() {
        Assert.True(AsciiString.contentEqualsIgnoreCase(null, null));
        Assert.False(AsciiString.contentEqualsIgnoreCase(null, "foo"));
        Assert.False(AsciiString.contentEqualsIgnoreCase("bar", null));
        Assert.True(AsciiString.contentEqualsIgnoreCase("FoO", "fOo"));
        Assert.False(AsciiString.contentEqualsIgnoreCase("FoO", "bar"));
        Assert.False(AsciiString.contentEqualsIgnoreCase("Foo", "foobar"));
        Assert.False(AsciiString.contentEqualsIgnoreCase("foobar", "Foo"));

        // Test variations (Ascii + string, Ascii + Ascii, string + Ascii)
        Assert.True(AsciiString.contentEqualsIgnoreCase(new AsciiString("FoO"), "fOo"));
        Assert.True(AsciiString.contentEqualsIgnoreCase(new AsciiString("FoO"), new AsciiString("fOo")));
        Assert.True(AsciiString.contentEqualsIgnoreCase("FoO", new AsciiString("fOo")));

        // Test variations (Ascii + string, Ascii + Ascii, string + Ascii)
        Assert.False(AsciiString.contentEqualsIgnoreCase(new AsciiString("FoO"), "bAr"));
        Assert.False(AsciiString.contentEqualsIgnoreCase(new AsciiString("FoO"), new AsciiString("bAr")));
        Assert.False(AsciiString.contentEqualsIgnoreCase("FoO", new AsciiString("bAr")));
    }

    [Fact]
    public void testIndexOfIgnoreCase() {
        Assert.Equal(-1, AsciiString.indexOfIgnoreCase(null, "abc", 1));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCase("abc", null, 1));
        Assert.Equal(0, AsciiString.indexOfIgnoreCase("", "", 0));
        Assert.Equal(0, AsciiString.indexOfIgnoreCase("aabaabaa", "A", 0));
        Assert.Equal(2, AsciiString.indexOfIgnoreCase("aabaabaa", "B", 0));
        Assert.Equal(1, AsciiString.indexOfIgnoreCase("aabaabaa", "AB", 0));
        Assert.Equal(5, AsciiString.indexOfIgnoreCase("aabaabaa", "B", 3));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCase("aabaabaa", "B", 9));
        Assert.Equal(2, AsciiString.indexOfIgnoreCase("aabaabaa", "B", -1));
        Assert.Equal(2, AsciiString.indexOfIgnoreCase("aabaabaa", "", 2));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCase("abc", "", 9));
        Assert.Equal(0, AsciiString.indexOfIgnoreCase("ãabaabaa", "Ã", 0));
    }

    [Fact]
    public void testIndexOfIgnoreCaseAscii() {
        Assert.Equal(-1, AsciiString.indexOfIgnoreCaseAscii(null, "abc", 1));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCaseAscii("abc", null, 1));
        Assert.Equal(0, AsciiString.indexOfIgnoreCaseAscii("", "", 0));
        Assert.Equal(0, AsciiString.indexOfIgnoreCaseAscii("aabaabaa", "A", 0));
        Assert.Equal(2, AsciiString.indexOfIgnoreCaseAscii("aabaabaa", "B", 0));
        Assert.Equal(1, AsciiString.indexOfIgnoreCaseAscii("aabaabaa", "AB", 0));
        Assert.Equal(5, AsciiString.indexOfIgnoreCaseAscii("aabaabaa", "B", 3));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCaseAscii("aabaabaa", "B", 9));
        Assert.Equal(2, AsciiString.indexOfIgnoreCaseAscii("aabaabaa", "B", -1));
        Assert.Equal(2, AsciiString.indexOfIgnoreCaseAscii("aabaabaa", "", 2));
        Assert.Equal(-1, AsciiString.indexOfIgnoreCaseAscii("abc", "", 9));
    }

    [Fact]
    public void testTrim() {
        Assert.Equal("", AsciiString.EMPTY_STRING.trim().toString());
        Assert.Equal("abc", new AsciiString("  abc").trim().toString());
        Assert.Equal("abc", new AsciiString("abc  ").trim().toString());
        Assert.Equal("abc", new AsciiString("  abc  ").trim().toString());
    }

    [Fact]
    public void testIndexOfChar() {
        Assert.Equal(-1, AsciiString.indexOf(null, 'a', 0));
        Assert.Equal(-1, AsciiString.of("").indexOf('a', 0));
        Assert.Equal(-1, AsciiString.of("abc").indexOf('d', 0));
        Assert.Equal(-1, AsciiString.of("aabaabaa").indexOf('A', 0));
        Assert.Equal(0, AsciiString.of("aabaabaa").indexOf('a', 0));
        Assert.Equal(1, AsciiString.of("aabaabaa").indexOf('a', 1));
        Assert.Equal(3, AsciiString.of("aabaabaa").indexOf('a', 2));
        Assert.Equal(3, AsciiString.of("aabdabaa").indexOf('d', 1));
        Assert.Equal(1, new AsciiString("abcd", 1, 2).indexOf('c', 0));
        Assert.Equal(2, new AsciiString("abcd", 1, 3).indexOf('d', 2));
        Assert.Equal(0, new AsciiString("abcd", 1, 2).indexOf('b', 0));
        Assert.Equal(-1, new AsciiString("abcd", 0, 2).indexOf('c', 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 3).indexOf('a', 0));
    }

    [Fact]
    public void testIndexOfCharSequence() {
        Assert.Equal(0, new AsciiString("abcd").indexOf("abcd", 0));
        Assert.Equal(0, new AsciiString("abcd").indexOf("abc", 0));
        Assert.Equal(1, new AsciiString("abcd").indexOf("bcd", 0));
        Assert.Equal(1, new AsciiString("abcd").indexOf("bc", 0));
        Assert.Equal(1, new AsciiString("abcdabcd").indexOf("bcd", 0));
        Assert.Equal(0, new AsciiString("abcd", 1, 2).indexOf("bc", 0));
        Assert.Equal(0, new AsciiString("abcd", 1, 3).indexOf("bcd", 0));
        Assert.Equal(1, new AsciiString("abcdabcd", 4, 4).indexOf("bcd", 0));
        Assert.Equal(3, new AsciiString("012345").indexOf("345", 3));
        Assert.Equal(3, new AsciiString("012345").indexOf("345", 0));

        // Test with empty string
        Assert.Equal(0, new AsciiString("abcd").indexOf("", 0));
        Assert.Equal(1, new AsciiString("abcd").indexOf("", 1));
        Assert.Equal(3, new AsciiString("abcd", 1, 3).indexOf("", 4));

        // Test not found
        Assert.Equal(-1, new AsciiString("abcd").indexOf("abcde", 0));
        Assert.Equal(-1, new AsciiString("abcdbc").indexOf("bce", 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 3).indexOf("abc", 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 2).indexOf("bd", 0));
        Assert.Equal(-1, new AsciiString("012345").indexOf("345", 4));
        Assert.Equal(-1, new AsciiString("012345").indexOf("abc", 3));
        Assert.Equal(-1, new AsciiString("012345").indexOf("abc", 0));
        Assert.Equal(-1, new AsciiString("012345").indexOf("abcdefghi", 0));
        Assert.Equal(-1, new AsciiString("012345").indexOf("abcdefghi", 4));
    }

    [Fact]
    public void testStaticIndexOfChar() {
        Assert.Equal(-1, AsciiString.indexOf(null, 'a', 0));
        Assert.Equal(-1, AsciiString.indexOf("", 'a', 0));
        Assert.Equal(-1, AsciiString.indexOf("abc", 'd', 0));
        Assert.Equal(-1, AsciiString.indexOf("aabaabaa", 'A', 0));
        Assert.Equal(0, AsciiString.indexOf("aabaabaa", 'a', 0));
        Assert.Equal(1, AsciiString.indexOf("aabaabaa", 'a', 1));
        Assert.Equal(3, AsciiString.indexOf("aabaabaa", 'a', 2));
        Assert.Equal(3, AsciiString.indexOf("aabdabaa", 'd', 1));
    }

    [Fact]
    public void testLastIndexOfCharSequence() {
        final byte[] bytes = { 'a', 'b', 'c', 'd', 'e' };
        final AsciiString ascii = new AsciiString(bytes, 2, 3, false);

        Assert.Equal(0, new AsciiString("abcd").lastIndexOf("abcd", 0));
        Assert.Equal(0, new AsciiString("abcd").lastIndexOf("abc", 4));
        Assert.Equal(1, new AsciiString("abcd").lastIndexOf("bcd", 4));
        Assert.Equal(1, new AsciiString("abcd").lastIndexOf("bc", 4));
        Assert.Equal(5, new AsciiString("abcdabcd").lastIndexOf("bcd", 10));
        Assert.Equal(0, new AsciiString("abcd", 1, 2).lastIndexOf("bc", 2));
        Assert.Equal(0, new AsciiString("abcd", 1, 3).lastIndexOf("bcd", 3));
        Assert.Equal(1, new AsciiString("abcdabcd", 4, 4).lastIndexOf("bcd", 4));
        Assert.Equal(3, new AsciiString("012345").lastIndexOf("345", 3));
        Assert.Equal(3, new AsciiString("012345").lastIndexOf("345", 6));
        Assert.Equal(1, ascii.lastIndexOf("de", 3));
        Assert.Equal(0, ascii.lastIndexOf("cde", 3));

        // Test with empty string
        Assert.Equal(0, new AsciiString("abcd").lastIndexOf("", 0));
        Assert.Equal(1, new AsciiString("abcd").lastIndexOf("", 1));
        Assert.Equal(3, new AsciiString("abcd", 1, 3).lastIndexOf("", 4));
        Assert.Equal(3, ascii.lastIndexOf("", 3));

        // Test not found
        Assert.Equal(-1, new AsciiString("abcd").lastIndexOf("abcde", 0));
        Assert.Equal(-1, new AsciiString("abcdbc").lastIndexOf("bce", 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 3).lastIndexOf("abc", 0));
        Assert.Equal(-1, new AsciiString("abcd", 1, 2).lastIndexOf("bd", 0));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf("345", 2));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf("abc", 3));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf("abc", 0));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf("abcdefghi", 0));
        Assert.Equal(-1, new AsciiString("012345").lastIndexOf("abcdefghi", 4));
        Assert.Equal(-1, ascii.lastIndexOf("a", 3));
        Assert.Equal(-1, ascii.lastIndexOf("abc", 3));
        Assert.Equal(-1, ascii.lastIndexOf("ce", 3));
    }

    [Fact]
    public void testReplace() {
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
    public void testSubStringHashCode() {
        //two "123"s
        Assert.Equal(AsciiString.hashCode("123"), AsciiString.hashCode("a123".substring(1)));
    }

    [Fact]
    public void testIndexOf() {
        AsciiString foo = AsciiString.of("This is a test");
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
    public void testToLowerCase() {
        AsciiString foo = AsciiString.of("This is a tesT");
        Assert.Equal("this is a test", foo.ToLower().toString());
    }

    [Fact]
    public void testToLowerCaseForOddLengths() {
        AsciiString foo = AsciiString.of("This is a test!");
        Assert.Equal("this is a test!", foo.ToLower().toString());
    }

    [Fact]
    public void testToLowerCaseLong() {
        AsciiString foo = AsciiString.of("This is a test for longer sequences");
        Assert.Equal("this is a test for longer sequences", foo.ToLower().toString());
    }

    [Fact]
    public void testToUpperCase() {
        AsciiString foo = AsciiString.of("This is a tesT");
        Assert.Equal("THIS IS A TEST", foo.toUpperCase().toString());
    }

    [Fact]
    public void testToUpperCaseLong() {
        AsciiString foo = AsciiString.of("This is a test for longer sequences");
        Assert.Equal("THIS IS A TEST FOR LONGER SEQUENCES", foo.toUpperCase().toString());
    }

    [Fact]
    public void testRegionMatchesReturnsTrueForEqualRegions() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        AsciiString world = new AsciiString("World");
        Assert.True(AsciiString.regionMatches(str, false, 0, hello, 0, 5));
        Assert.True(AsciiString.regionMatches(str, false, 7, world, 0, 5));
    }

    [Fact]
    public void testRegionMatchesReturnsFalseForDifferentRegions() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.regionMatches(str, false, 0, world, 0, 5));
        Assert.False(AsciiString.regionMatches(str, false, 7, hello, 0, 5));
    }

    [Fact]
    public void testRegionMatchesIgnoreCaseReturnsTrueForEqualRegions() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("hello");
        AsciiString world = new AsciiString("world");
        Assert.True(AsciiString.regionMatches(str, true, 0, hello, 0, 5));
        Assert.True(AsciiString.regionMatches(str, true, 7, world, 0, 5));
    }

    [Fact]
    public void testRegionMatchesIgnoreCaseReturnsFalseForDifferentRegions() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.regionMatches(str, true, 0, world, 0, 5));
        Assert.False(AsciiString.regionMatches(str, true, 7, hello, 0, 5));
    }

    [Fact]
    public void testRegionMatchesAsciiReturnsTrueForEqualRegions() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        AsciiString world = new AsciiString("World");
        Assert.True(AsciiString.regionMatchesAscii(str, false, 0, hello, 0, 5));
        Assert.True(AsciiString.regionMatchesAscii(str, false, 7, world, 0, 5));
    }

    [Fact]
    public void testRegionMatchesAsciiReturnsFalseForDifferentRegions() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.regionMatchesAscii(str, false, 0, world, 0, 5));
        Assert.False(AsciiString.regionMatchesAscii(str, false, 7, hello, 0, 5));
    }

    [Fact]
    public void testRegionMatchesAsciiIgnoreCaseReturnsTrueForEqualRegions() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("hello");
        AsciiString world = new AsciiString("world");
        Assert.True(AsciiString.regionMatchesAscii(str, true, 0, hello, 0, 5));
        Assert.True(AsciiString.regionMatchesAscii(str, true, 7, world, 0, 5));
    }

    [Fact]
    public void testRegionMatchesAsciiIgnoreCaseReturnsFalseForDifferentRegions() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString world = new AsciiString("world");
        AsciiString hello = new AsciiString("hello");
        Assert.False(AsciiString.regionMatchesAscii(str, true, 0, world, 0, 5));
        Assert.False(AsciiString.regionMatchesAscii(str, true, 7, hello, 0, 5));
    }

    [Fact]
    public void testRegionMatchesHandlesOutOfBounds() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        Assert.False(AsciiString.regionMatches(str, false, -1, hello, 0, 5));
        Assert.False(AsciiString.regionMatches(str, false, 0, hello, -1, 5));
        Assert.False(AsciiString.regionMatches(str, false, 0, hello, 0, 20));
    }

    [Fact]
    public void testRegionMatchesAsciiHandlesOutOfBounds() {
        AsciiString str = new AsciiString("Hello, World!");
        AsciiString hello = new AsciiString("Hello");
        Assert.False(AsciiString.regionMatchesAscii(str, false, -1, hello, 0, 5));
        Assert.False(AsciiString.regionMatchesAscii(str, false, 0, hello, -1, 5));
    }
}
