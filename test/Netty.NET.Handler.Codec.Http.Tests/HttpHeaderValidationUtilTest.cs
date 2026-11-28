/*
 * Copyright 2022 The Netty Project
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
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValidationUtil;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: @ParameterizedTest @MethodSource is a [Theory] with MemberData whose rows are the object[] of JUnit's Arguments;
// JUnit's @Execution(CONCURRENT) has no counterpart (xUnit runs the tests of a class one after another and the classes
// in parallel). The deprecated HttpHeaderNames.KEEP_ALIVE and PROXY_CONNECTION are not ported (their @deprecated text
// names CONNECTION, owner decision 2026-11-16), so the rows use AsciiString constants with the same cached names, as
// HttpHeaderValidationUtil itself does. A Java String passed as a CharSequence is wrapped in StringCharSequence (a C#
// string is not an ICharSequence), and the anonymous CharSequence of asCharSequence is the private nested
// NonAsciiCharSequence, which forwards every ICharSequence member to the AsciiString but is not one. Java byte loops
// "for (byte i = 0; i < 0x21; i++)" count 0..0x20 in both languages; "value < 0" of validateHeaderNameElement tests the
// signed Java byte, so it reads the octet as sbyte (Java byte sign rule). ByteBuffer.wrap(array) with
// new AsciiString(buffer, false) is an AsciiString that shares the array (the ReadOnlyMemory constructor with
// copy=false), putInt/getInt are BinaryPrimitives big-endian writes/reads as ByteBuffer's default byte order. The old
// algorithms signal a rejection by throwing a preallocated stack-less IllegalArgumentException that the 2^32-iteration
// loops catch; a CLR throw unwinds the stack and "its performance can be orders of magnitude slower"
// (https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/exceptions-and-performance), so they return the
// rejection (false, or the state -1) instead of throwing VALIDATION_EXCEPTION, which therefore has no counterpart, and
// the failure message Supplier is built only for a failing assertion. @DisabledForJreRange(max = JAVA_17) ("much too
// slow on older Java versions") has no counterpart: both exhaustive tests run, and they walk the same 2^32 values in
// 256 slices of 2^24 values (one per high octet, each with its own array, AsciiString and CharSequence view) on
// Parallel.For, because the sequential walk did not finish within 10 minutes on this host (measured 2026-11-24); every
// value is still checked once, and the order of the checks is not observable. new String(bytes) decodes ASCII-only
// bytes, so Encoding.ASCII is used.
// We have a couple of fairly slow tests here. Better to run them in parallel.
public class HttpHeaderValidationUtilTest
{
    private static readonly AsciiString KEEP_ALIVE = AsciiString.Cached("keep-alive");
    private static readonly AsciiString PROXY_CONNECTION = AsciiString.Cached("proxy-connection");

    // We need to check for deprecated headers as well.
    public static IEnumerable<object[]> ConnectionRelatedHeaders()
    {
        List<object[]> list = new List<object[]>();

        list.Add(Header(false, HttpHeaderNames.ACCEPT));
        list.Add(Header(false, HttpHeaderNames.ACCEPT_CHARSET));
        list.Add(Header(false, HttpHeaderNames.ACCEPT_ENCODING));
        list.Add(Header(false, HttpHeaderNames.ACCEPT_LANGUAGE));
        list.Add(Header(false, HttpHeaderNames.ACCEPT_RANGES));
        list.Add(Header(false, HttpHeaderNames.ACCEPT_PATCH));
        list.Add(Header(false, HttpHeaderNames.ACCEPT_QUERY));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_ALLOW_CREDENTIALS));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_ALLOW_HEADERS));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_ALLOW_METHODS));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_ALLOW_ORIGIN));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_ALLOW_PRIVATE_NETWORK));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_EXPOSE_HEADERS));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_MAX_AGE));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_REQUEST_HEADERS));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_REQUEST_METHOD));
        list.Add(Header(false, HttpHeaderNames.ACCESS_CONTROL_REQUEST_PRIVATE_NETWORK));
        list.Add(Header(false, HttpHeaderNames.AGE));
        list.Add(Header(false, HttpHeaderNames.ALLOW));
        list.Add(Header(false, HttpHeaderNames.AUTHORIZATION));
        list.Add(Header(false, HttpHeaderNames.CACHE_CONTROL));
        list.Add(Header(true, HttpHeaderNames.CONNECTION));
        list.Add(Header(false, HttpHeaderNames.CONTENT_BASE));
        list.Add(Header(false, HttpHeaderNames.CONTENT_ENCODING));
        list.Add(Header(false, HttpHeaderNames.CONTENT_LANGUAGE));
        list.Add(Header(false, HttpHeaderNames.CONTENT_LENGTH));
        list.Add(Header(false, HttpHeaderNames.CONTENT_LOCATION));
        list.Add(Header(false, HttpHeaderNames.CONTENT_TRANSFER_ENCODING));
        list.Add(Header(false, HttpHeaderNames.CONTENT_DISPOSITION));
        list.Add(Header(false, HttpHeaderNames.CONTENT_MD5));
        list.Add(Header(false, HttpHeaderNames.CONTENT_RANGE));
        list.Add(Header(false, HttpHeaderNames.CONTENT_SECURITY_POLICY));
        list.Add(Header(false, HttpHeaderNames.CONTENT_TYPE));
        list.Add(Header(false, HttpHeaderNames.COOKIE));
        list.Add(Header(false, HttpHeaderNames.DATE));
        list.Add(Header(false, HttpHeaderNames.DNT));
        list.Add(Header(false, HttpHeaderNames.ETAG));
        list.Add(Header(false, HttpHeaderNames.EXPECT));
        list.Add(Header(false, HttpHeaderNames.EXPIRES));
        list.Add(Header(false, HttpHeaderNames.FROM));
        list.Add(Header(false, HttpHeaderNames.HOST));
        list.Add(Header(false, HttpHeaderNames.IF_MATCH));
        list.Add(Header(false, HttpHeaderNames.IF_MODIFIED_SINCE));
        list.Add(Header(false, HttpHeaderNames.IF_NONE_MATCH));
        list.Add(Header(false, HttpHeaderNames.IF_RANGE));
        list.Add(Header(false, HttpHeaderNames.IF_UNMODIFIED_SINCE));
        list.Add(Header(true, KEEP_ALIVE));
        list.Add(Header(false, HttpHeaderNames.LAST_MODIFIED));
        list.Add(Header(false, HttpHeaderNames.LOCATION));
        list.Add(Header(false, HttpHeaderNames.MAX_FORWARDS));
        list.Add(Header(false, HttpHeaderNames.ORIGIN));
        list.Add(Header(false, HttpHeaderNames.PRAGMA));
        list.Add(Header(false, HttpHeaderNames.PROXY_AUTHENTICATE));
        list.Add(Header(false, HttpHeaderNames.PROXY_AUTHORIZATION));
        list.Add(Header(true, PROXY_CONNECTION));
        list.Add(Header(false, HttpHeaderNames.RANGE));
        list.Add(Header(false, HttpHeaderNames.REFERER));
        list.Add(Header(false, HttpHeaderNames.RETRY_AFTER));
        list.Add(Header(false, HttpHeaderNames.SEC_WEBSOCKET_KEY1));
        list.Add(Header(false, HttpHeaderNames.SEC_WEBSOCKET_KEY2));
        list.Add(Header(false, HttpHeaderNames.SEC_WEBSOCKET_LOCATION));
        list.Add(Header(false, HttpHeaderNames.SEC_WEBSOCKET_ORIGIN));
        list.Add(Header(false, HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL));
        list.Add(Header(false, HttpHeaderNames.SEC_WEBSOCKET_VERSION));
        list.Add(Header(false, HttpHeaderNames.SEC_WEBSOCKET_KEY));
        list.Add(Header(false, HttpHeaderNames.SEC_WEBSOCKET_ACCEPT));
        list.Add(Header(false, HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));
        list.Add(Header(false, HttpHeaderNames.SERVER));
        list.Add(Header(false, HttpHeaderNames.SET_COOKIE));
        list.Add(Header(false, HttpHeaderNames.SET_COOKIE2));
        list.Add(Header(true, HttpHeaderNames.TE));
        list.Add(Header(false, HttpHeaderNames.TRAILER));
        list.Add(Header(true, HttpHeaderNames.TRANSFER_ENCODING));
        list.Add(Header(true, HttpHeaderNames.UPGRADE));
        list.Add(Header(false, HttpHeaderNames.UPGRADE_INSECURE_REQUESTS));
        list.Add(Header(false, HttpHeaderNames.USER_AGENT));
        list.Add(Header(false, HttpHeaderNames.VARY));
        list.Add(Header(false, HttpHeaderNames.VIA));
        list.Add(Header(false, HttpHeaderNames.WARNING));
        list.Add(Header(false, HttpHeaderNames.WEBSOCKET_LOCATION));
        list.Add(Header(false, HttpHeaderNames.WEBSOCKET_ORIGIN));
        list.Add(Header(false, HttpHeaderNames.WEBSOCKET_PROTOCOL));
        list.Add(Header(false, HttpHeaderNames.WWW_AUTHENTICATE));
        list.Add(Header(false, HttpHeaderNames.X_FRAME_OPTIONS));
        list.Add(Header(false, HttpHeaderNames.X_REQUESTED_WITH));

        return list;
    }

    private static object[] Header(bool isConnectionRelated, AsciiString headerName)
    {
        return new object[]{headerName, isConnectionRelated};
    }

    [Theory]
    [MemberData(nameof(ConnectionRelatedHeaders))]
    public void MustIdentifyConnectionRelatedHeadersAsciiString(AsciiString headerName, bool isConnectionRelated)
    {
        Assert.Equal(isConnectionRelated, HttpHeaderValidationUtil.IsConnectionHeader(headerName, false));
    }

    [Theory]
    [MemberData(nameof(ConnectionRelatedHeaders))]
    public void MustIdentifyConnectionRelatedHeadersString(AsciiString headerName, bool isConnectionRelated)
    {
        Assert.Equal(isConnectionRelated,
                HttpHeaderValidationUtil.IsConnectionHeader(new StringCharSequence(headerName.ToString()), false));
    }

    [Fact]
    public void TeHeaderIsNotConnectionRelatedWhenIgnoredAsciiString()
    {
        Assert.False(HttpHeaderValidationUtil.IsConnectionHeader(HttpHeaderNames.TE, true));
    }

    [Fact]
    public void TeHeaderIsNotConnectionRelatedWhenIgnoredString()
    {
        Assert.False(HttpHeaderValidationUtil.IsConnectionHeader(new StringCharSequence(HttpHeaderNames.TE.ToString()), true));
    }

    public static IEnumerable<object[]> TeIsTrailersTruthTable()
    {
        List<object[]> list = new List<object[]>();

        list.Add(TeIsTrailter(HttpHeaderNames.TE, HttpHeaderValues.TRAILERS, false));
        list.Add(TeIsTrailter(HttpHeaderNames.TE, HttpHeaderValues.CHUNKED, true));
        list.Add(TeIsTrailter(HttpHeaderNames.COOKIE, HttpHeaderValues.CHUNKED, false));
        list.Add(TeIsTrailter(HttpHeaderNames.COOKIE, HttpHeaderValues.TRAILERS, false));
        list.Add(TeIsTrailter(HttpHeaderNames.TRAILER, HttpHeaderValues.TRAILERS, false));
        list.Add(TeIsTrailter(HttpHeaderNames.TRAILER, HttpHeaderValues.CHUNKED, false));

        return list;
    }

    private static object[] TeIsTrailter(
            AsciiString headerName, AsciiString headerValue, bool result)
    {
        return new object[]{headerName, headerValue, result};
    }

    [Theory]
    [MemberData(nameof(TeIsTrailersTruthTable))]
    public void WhenTeIsNotTrailerOrNotWithNameAndValueAsciiString(
            AsciiString headerName, AsciiString headerValue, bool result)
    {
        Assert.Equal(result, HttpHeaderValidationUtil.IsTeNotTrailers(headerName, headerValue));
    }

    [Theory]
    [MemberData(nameof(TeIsTrailersTruthTable))]
    public void WhenTeIsNotTrailerOrNotSWithNameAndValueString(
            AsciiString headerName, AsciiString headerValue, bool result)
    {
        Assert.Equal(result, HttpHeaderValidationUtil.IsTeNotTrailers(new StringCharSequence(headerName.ToString()),
                new StringCharSequence(headerValue.ToString())));
    }

    [Theory]
    [MemberData(nameof(TeIsTrailersTruthTable))]
    public void WhenTeIsNotTrailerOrNotSWithNameAsciiStringAndValueString(
            AsciiString headerName, AsciiString headerValue, bool result)
    {
        Assert.Equal(result, HttpHeaderValidationUtil.IsTeNotTrailers(headerName,
                new StringCharSequence(headerValue.ToString())));
    }

    [Theory]
    [MemberData(nameof(TeIsTrailersTruthTable))]
    public void WhenTeIsNotTrailerOrNotSWithNametringAndValueAsciiString(
            AsciiString headerName, AsciiString headerValue, bool result)
    {
        Assert.Equal(result, HttpHeaderValidationUtil.IsTeNotTrailers(new StringCharSequence(headerName.ToString()),
                headerValue));
    }

    public static IEnumerable<object[]> IllegalFirstChar()
    {
        List<AsciiString> list = new List<AsciiString>();

        for (byte i = 0; i < 0x21; i++)
        {
            AsciiStrings(new byte[]{i, (byte) 'a'}, list);
        }

        AsciiStrings(new byte[]{0x7F, (byte) 'a'}, list);

        return Rows(list);
    }

    // CLR: the MethodSource methods return List<AsciiString>; MemberData needs one object[] row per argument set.
    private static IEnumerable<object[]> Rows(List<AsciiString> list)
    {
        foreach (AsciiString value in list)
        {
            yield return new object[] { value };
        }
    }

    private static void AsciiStrings(byte[] chars, List<AsciiString> @out)
    {
        @out.Add(new AsciiString(chars));
        @out.Add(new AsciiString(CopyOf(chars, chars.Length + 1), 0, chars.Length, false));

        byte[] cs = CopyOf(chars, chars.Length + 1);
        Array.Copy(cs, 0, cs, 1, chars.Length);
        @out.Add(new AsciiString(cs, 1, chars.Length, false));

        cs = CopyOf(chars, chars.Length + 2);
        Array.Copy(cs, 0, cs, 1, chars.Length);
        @out.Add(new AsciiString(cs, 1, chars.Length, false));
    }

    // CLR: java.util.Arrays.copyOf(byte[], int) (zero-padded copy).
    private static byte[] CopyOf(byte[] original, int newLength)
    {
        byte[] copy = new byte[newLength];
        Array.Copy(original, copy, Math.Min(original.Length, newLength));
        return copy;
    }

    [Theory]
    [MemberData(nameof(IllegalFirstChar))]
    public void DecodingInvalidHeaderValuesMustFailIfFirstCharIsIllegalAsciiString(AsciiString value)
    {
        Assert.Equal(0, ValidateValidHeaderValue(value));
    }

    [Theory]
    [MemberData(nameof(IllegalFirstChar))]
    public void DecodingInvalidHeaderValuesMustFailIfFirstCharIsIllegalCharSequence(AsciiString value)
    {
        Assert.Equal(0, ValidateValidHeaderValue(AsCharSequence(value)));
    }

    public static IEnumerable<object[]> LegalFirstChar()
    {
        List<AsciiString> list = new List<AsciiString>();

        for (int i = 0x21; i <= 0xFF; i++)
        {
            if (i == 0x7F)
            {
                continue;
            }

            AsciiStrings(new byte[]{(byte) i, (byte) 'a'}, list);
        }

        return Rows(list);
    }

    [Theory]
    [MemberData(nameof(LegalFirstChar))]
    public void AllOtherCharsAreLegalFirstCharsAsciiString(AsciiString value)
    {
        Assert.Equal(-1, ValidateValidHeaderValue(value));
    }

    [Theory]
    [MemberData(nameof(LegalFirstChar))]
    public void AllOtherCharsAreLegalFirstCharsCharSequence(AsciiString value)
    {
        Assert.Equal(-1, ValidateValidHeaderValue(value));
    }

    public static IEnumerable<object[]> IllegalNotFirstChar()
    {
        List<AsciiString> list = new List<AsciiString>();

        for (byte i = 0; i < 0x21; i++)
        {
            if (i == ' ' || i == '\t')
            {
                continue; // Space and horizontal tab are only illegal as first chars.
            }

            AsciiStrings(new byte[]{(byte) 'a', i}, list);
        }

        AsciiStrings(new byte[]{(byte) 'a', 0x7F}, list);

        return Rows(list);
    }

    [Theory]
    [MemberData(nameof(IllegalNotFirstChar))]
    public void DecodingInvalidHeaderValuesMustFailIfNotFirstCharIsIllegalAsciiString(AsciiString value)
    {
        Assert.Equal(1, ValidateValidHeaderValue(value));
    }

    [Theory]
    [MemberData(nameof(IllegalNotFirstChar))]
    public void DecodingInvalidHeaderValuesMustFailIfNotFirstCharIsIllegalCharSequence(AsciiString value)
    {
        Assert.Equal(1, ValidateValidHeaderValue(AsCharSequence(value)));
    }

    public static IEnumerable<object[]> LegalNotFirstChar()
    {
        List<AsciiString> list = new List<AsciiString>();

        for (int i = 0; i < 0xFF; i++)
        {
            if (i == 0x7F || i < 0x21 && (i != ' ' || i != '\t'))
            {
                continue;
            }

            AsciiStrings(new byte[] {(byte) 'a', (byte) i}, list);
        }

        return Rows(list);
    }

    [Theory]
    [MemberData(nameof(LegalNotFirstChar))]
    public void AllOtherCharsArgLegalNotFirstCharsAsciiString(AsciiString value)
    {
        Assert.Equal(-1, ValidateValidHeaderValue(value));
    }

    [Theory]
    [MemberData(nameof(LegalNotFirstChar))]
    public void AllOtherCharsArgLegalNotFirstCharsCharSequence(AsciiString value)
    {
        Assert.Equal(-1, ValidateValidHeaderValue(AsCharSequence(value)));
    }

    [Fact]
    public void EmptyValuesHaveNoIllegalCharsAsciiString()
    {
        Assert.Equal(-1, ValidateValidHeaderValue(AsciiString.EMPTY_STRING));
    }

    [Fact]
    public void EmptyValuesHaveNoIllegalCharsCharSequence()
    {
        Assert.Equal(-1, ValidateValidHeaderValue(AsCharSequence(AsciiString.EMPTY_STRING)));
    }

    [Fact]
    public void HeaderValuesCannotEndWithNewlinesAsciiString()
    {
        Assert.Equal(1, ValidateValidHeaderValue(AsciiString.Of(new StringCharSequence("a\n"))));
        Assert.Equal(1, ValidateValidHeaderValue(AsciiString.Of(new StringCharSequence("a\r"))));
    }

    [Fact]
    public void HeaderValuesCannotEndWithNewlinesCharSequence()
    {
        Assert.Equal(1, ValidateValidHeaderValue(new StringCharSequence("a\n")));
        Assert.Equal(1, ValidateValidHeaderValue(new StringCharSequence("a\r")));
    }

    /**
     * This method returns a {@link CharSequence} instance that has the same contents as the given {@link AsciiString},
     * but which is, critically, <em>not</em> itself an {@link AsciiString}.
     * <p>
     * Some methods specialise on {@link AsciiString}, while having a {@link CharSequence} based fallback.
     * <p>
     * This method exist to test those fallback methods.
     *
     * @param value The {@link AsciiString} instance to wrap.
     * @return A new {@link CharSequence} instance which backed by the given {@link AsciiString},
     * but which is itself not an {@link AsciiString}.
     */
    private static ICharSequence AsCharSequence(AsciiString value)
    {
        return new NonAsciiCharSequence(value);
    }

    private sealed class NonAsciiCharSequence : ICharSequence
    {
        private readonly AsciiString value;

        internal NonAsciiCharSequence(AsciiString value)
        {
            this.value = value;
        }

        public int Length()
        {
            return value.Length();
        }

        public char CharAt(int index)
        {
            return value.CharAt(index);
        }

        public ICharSequence SubSequence(int start, int end)
        {
            return AsCharSequence(value.SubSequence(start, end));
        }

        public ICharSequence SubSequence(int start)
        {
            return AsCharSequence(value.SubSequence(start));
        }

        public int IndexOf(char ch, int start = 0)
        {
            return value.IndexOf(ch, start);
        }

        public bool RegionMatches(int thisStart, ICharSequence seq, int start, int length)
        {
            return value.RegionMatches(thisStart, seq, start, length);
        }

        public bool RegionMatchesIgnoreCase(int thisStart, ICharSequence seq, int start, int length)
        {
            return value.RegionMatchesIgnoreCase(thisStart, seq, start, length);
        }

        public bool ContentEquals(ICharSequence other)
        {
            return value.ContentEquals(other);
        }

        public bool ContentEqualsIgnoreCase(ICharSequence other)
        {
            return value.ContentEqualsIgnoreCase(other);
        }

        public int HashCode(bool ignoreCase)
        {
            return value.HashCode(ignoreCase);
        }

        public string ToString(int start)
        {
            return value.ToString(start);
        }

        public override string ToString()
        {
            return value.ToString();
        }

        public int Count => value.Count;

        public char this[int index] => value[index];

        public IEnumerator<char> GetEnumerator()
        {
            return value.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    // This test is much too slow on older Java versions.
    [Fact]
    public void HeaderValueValidationMustRejectAllValuesRejectedByOldAlgorithm()
    {
        Parallel.For(0, 256, high =>
        {
            byte[] array = new byte[4];
            AsciiString asciiString = new AsciiString(array.AsMemory(), false);
            ICharSequence charSequence = AsCharSequence(asciiString);
            int i = high << 24;
            int end = i + (1 << 24);
            Func<string> failureMessageSupplier = () =>
                    "validation mismatch on string '" + asciiString + "', iteration " +
                    BinaryPrimitives.ReadInt32BigEndian(array);

            do
            {
                BinaryPrimitives.WriteInt32BigEndian(array, i);

                if (!OldHeaderValueValidationAlgorithm(asciiString))
                {
                    int r = ValidateValidHeaderValue(asciiString);
                    if (r == -1)
                    {
                        Assert.NotEqual(-1, r, failureMessageSupplier());
                    }

                    r = ValidateValidHeaderValue(charSequence);
                    if (r == -1)
                    {
                        Assert.NotEqual(-1, r, failureMessageSupplier());
                    }
                }

                i++;
            } while (i != end);
        });
    }

    private static bool OldHeaderValueValidationAlgorithm(ICharSequence seq)
    {
        int state = 0;

        // Start looping through each of the character
        for (int index = 0; index < seq.Length(); index++)
        {
            state = OldValidationAlgorithmValidateValueChar(state, seq.CharAt(index));
            if (state == -1)
            {
                return false;
            }
        }

        if (state != 0)
        {
            return false;
        }

        return true;
    }

    private static int OldValidationAlgorithmValidateValueChar(int state, char character)
    {
        /*
         * State:
         * 0: Previous character was neither CR nor LF
         * 1: The previous character was CR
         * 2: The previous character was LF
         */
        if ((character & ~15) == 0)
        {
            // Check the absolutely prohibited characters.
            switch (character)
            {
                case (char) 0x0: // NULL
                    return -1;
                case (char) 0x0b: // Vertical tab
                    return -1;
                case '\f':
                    return -1;
                default:
                    break;
            }
        }

        // Check the CRLF (HT | SP) pattern
        switch (state)
        {
            case 0:
                switch (character)
                {
                    case '\r':
                        return 1;
                    case '\n':
                        return 2;
                    default:
                        break;
                }

                break;
            case 1:
                if (character == '\n')
                {
                    return 2;
                }

                return -1;
            case 2:
                switch (character)
                {
                    case '\t':
                    case ' ':
                        return 0;
                    default:
                        return -1;
                }
            default:
                break;
        }

        return state;
    }

    // This test is much too slow on older Java versions.
    [Fact]
    public void HeaderNameValidationMustRejectAllNamesRejectedByOldAlgorithm()
    {
        Parallel.For(0, 256, high =>
        {
            byte[] array = new byte[4];
            AsciiString asciiString = new AsciiString(array.AsMemory(), false);
            ICharSequence charSequence = AsCharSequence(asciiString);
            int i = high << 24;
            int end = i + (1 << 24);
            Func<string> failureMessageSupplier = () =>
                    "validation mismatch on string '" + asciiString + "', iteration " +
                    BinaryPrimitives.ReadInt32BigEndian(array);

            do
            {
                BinaryPrimitives.WriteInt32BigEndian(array, i);

                if (!OldHeaderNameValidationAlgorithmAsciiString(asciiString))
                {
                    int r = ValidateToken(asciiString);
                    if (r == -1)
                    {
                        Assert.NotEqual(-1, r, failureMessageSupplier());
                    }

                    r = ValidateToken(charSequence);
                    if (r == -1)
                    {
                        Assert.NotEqual(-1, r, failureMessageSupplier());
                    }
                }

                i++;
            } while (i != end);
        });
    }

    private static bool OldHeaderNameValidationAlgorithmAsciiString(AsciiString name)
    {
        byte[] array = name.Array();

        for (int i = name.ArrayOffset(), len = name.ArrayOffset() + name.Length(); i < len; i++)
        {
            if (!ValidateHeaderNameElement(array[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateHeaderNameElement(byte value)
    {
        switch (value)
        {
            case 0x1c:
            case 0x1d:
            case 0x1e:
            case 0x1f:
            case 0x00:
            case (byte) '\t':
            case (byte) '\n':
            case 0x0b:
            case (byte) '\f':
            case (byte) '\r':
            case (byte) ' ':
            case (byte) ',':
            case (byte) ':':
            case (byte) ';':
            case (byte) '=':
                return false;
            default:
                // Check to see if the character is not an ASCII character, or invalid
                if (unchecked((sbyte) value) < 0)
                {
                    return false;
                }

                return true;
        }
    }

    public static IEnumerable<object[]> ValidTokenChars()
    {
        List<char> charList = new List<char>();

        for (char c = '0'; c <= '9'; c++)
        {
            charList.Add(c);
        }

        for (char c = 'a'; c <= 'z'; c++)
        {
            charList.Add(c);
        }

        for (char c = 'A'; c <= 'Z'; c++)
        {
            charList.Add(c);
        }

        // Unreserved characters:
        charList.Add('-');
        charList.Add('.');
        charList.Add('_');
        charList.Add('~');

        // Token special characters:
        charList.Add('!');
        charList.Add('#');
        charList.Add('$');
        charList.Add('%');
        charList.Add('&');
        charList.Add('\'');
        charList.Add('*');
        charList.Add('+');
        charList.Add('^');
        charList.Add('`');
        charList.Add('|');

        List<AsciiString> asciiStrings = new List<AsciiString>();
        List<object[]> list = new List<object[]>();

        foreach (char tokenChar in charList)
        {
            foreach (byte[] cs in new[] { new byte[] {(byte) tokenChar, (byte) 'a'}, new byte[] {(byte) 'a', (byte) tokenChar} })
            {
                asciiStrings.Clear();
                AsciiStrings(cs, asciiStrings);

                foreach (AsciiString asciiString in asciiStrings)
                {
                    list.Add(Args(asciiString, Encoding.ASCII.GetString(cs)));
                }

                foreach (AsciiString asciiString in asciiStrings)
                {
                    list.Add(Args(asciiString, asciiString.ToString()));
                }
            }
        }

        return list;
    }

    private static object[] Args(params object[] objs)
    {
        return objs;
    }

    [Theory]
    [MemberData(nameof(ValidTokenChars))]
    public void AllTokenCharsAreValidInHeaderName(AsciiString asciiString, string @string)
    {
        ICharSequence charSequence = AsCharSequence(asciiString);

        Assert.Equal(-1, ValidateToken(asciiString));
        Assert.Equal(-1, ValidateToken(charSequence));
        Assert.Equal(-1, ValidateToken(new StringCharSequence(@string)));
    }
}
