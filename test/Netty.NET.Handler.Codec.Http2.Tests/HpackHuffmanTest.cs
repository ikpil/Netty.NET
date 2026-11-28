/*
 * Copyright 2015 The Netty Project
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

/*
 * Copyright 2014 Twitter, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the anonymous Executables are lambdas and assertThrows(Http2Exception.class,...) is
// Assert.ThrowsAny<Http2Exception>. java.util.Random(123456789L) is System.Random(123456789): the round trip of
// the 4096 random bytes does not depend on the exact sequence, which is not reproduced. String.getBytes() and new
// String(byte[]) use the platform default charset, which is Encoding.Default (UTF-8 on.NET;
// docs/codec-http-porting.md, Shared CLR adaptations); the strings are ASCII, so the bytes are the same. The
// String passed to HpackHuffmanEncoder.encode as a CharSequence is a StringCharSequence. Arrays.fill is
// Array.Fill; int... is params int[]. The assertion with a message uses XunitAssertMessages.AssertM.
public class HpackHuffmanTest
{

    [Fact]
    public void TestHuffman()
    {
        string s = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        for (int i = 0; i < s.Length; i++)
        {
            RoundTrip(s.Substring(0, i));
        }

        Random random = new Random(123456789);
        byte[] buf = new byte[4096];
        random.NextBytes(buf);
        RoundTrip(buf);
    }

    [Fact]
    public void TestDecodeEOS()
    {
        byte[] buf = new byte[4];

        for (int i = 0; i < 4; i++)
        {
            buf[i] = (byte) 0xFF;
        }

        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(buf);
        });
    }

    [Fact]
    public void TestDecodeIllegalPadding()
    {
        byte[] buf = new byte[1];
        buf[0] = 0x00; // '0', invalid padding
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(buf);
        });
    }

    [Fact]
    public void TestDecodeExtraPadding()
    {
        byte[] buf = MakeBuf(0x0f, 0xFF); // '1', 'EOS'
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(buf);
        });
    }

    [Fact]
    public void TestDecodeExtraPadding1byte()
    {
        byte[] buf = MakeBuf(0xFF);
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(buf);
        });
    }

    [Fact]
    public void TestDecodeExtraPadding2byte()
    {
        byte[] buf = MakeBuf(0x1F, 0xFF); // 'a'
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(buf);
        });
    }

    [Fact]
    public void TestDecodeExtraPadding3byte()
    {
        byte[] buf = MakeBuf(0x1F, 0xFF, 0xFF); // 'a'
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(buf);
        });
    }

    [Fact]
    public void TestDecodeExtraPadding4byte()
    {
        byte[] buf = MakeBuf(0x1F, 0xFF, 0xFF, 0xFF); // 'a'
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(buf);
        });
    }

    [Fact]
    public void TestDecodeExtraPadding29bit()
    {
        byte[] buf = MakeBuf(0xFF, 0x9F, 0xFF, 0xFF, 0xFF);  // '|'
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(buf);
        });
    }

    [Fact]
    public void TestDecodePartialSymbol()
    {
        byte[] buf =
                MakeBuf(0x52, 0xBC, 0x30, 0xFF, 0xFF, 0xFF, 0xFF); // " pFA\x00", 31 bits of padding, a.k.a. EOS
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            Decode(buf);
        });
    }

    [Fact]
    public void TestEncoderSanitizingMultiByteCharacters()
    {
        int inputLen = 500;
        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < inputLen; i++)
        {
            // Starts with 0x4E01 because certain suboptimal sanitization could cause some problem with this input.
            // For example, if a multibyte character C is sanitized by doing (C & OxFF), if C == 0x4E01, then
            // (0x4E01 & OxFF) is greater than zero which indicates insufficient sanitization.
            sb.Append((char) (0x4E01 + i));
        }

        HpackHuffmanEncoder encoder = new HpackHuffmanEncoder();
        string toBeEncoded = sb.ToString();
        ByteBuf buffer = Unpooled.Buffer();
        byte[] bytes;

        try
        {
            encoder.Encode(buffer, new StringCharSequence(toBeEncoded));
            bytes = new byte[buffer.ReadableBytes];
            buffer.ReadBytes(bytes);
        }
        finally
        {
            buffer.Release(); // Release as soon as possible.
        }

        byte[] actualBytes = Decode(bytes);
        string actualDecoded = Encoding.Default.GetString(actualBytes);
        char[] charArray = new char[inputLen];
        Array.Fill(charArray, '?');
        string expectedDecoded = new string(charArray);
        Assert.Equal(
                expectedDecoded,
                actualDecoded,
                "Expect the decoded string to be sanitized and contains only '?' characters."
        );
    }

    private static byte[] MakeBuf(params int[] bytes)
    {
        byte[] buf = new byte[bytes.Length];

        for (int i = 0; i < buf.Length; i++)
        {
            buf[i] = (byte) bytes[i];
        }

        return buf;
    }

    private static void RoundTrip(string s)
    {
        RoundTrip(new HpackHuffmanEncoder(), s);
    }

    private static void RoundTrip(HpackHuffmanEncoder encoder, string s)
    {
        RoundTrip(encoder, Encoding.Default.GetBytes(s));
    }

    private static void RoundTrip(byte[] buf)
    {
        RoundTrip(new HpackHuffmanEncoder(), buf);
    }

    private static void RoundTrip(HpackHuffmanEncoder encoder, byte[] buf)
    {
        ByteBuf buffer = Unpooled.Buffer();

        try
        {
            encoder.Encode(buffer, new AsciiString(buf, false));
            byte[] bytes = new byte[buffer.ReadableBytes];
            buffer.ReadBytes(bytes);

            byte[] actualBytes = Decode(bytes);

            Assert.Equal(buf, actualBytes);
        }
        finally
        {
            buffer.Release();
        }
    }

    private static byte[] Decode(byte[] bytes)
    {
        ByteBuf buffer = Unpooled.WrappedBuffer(bytes);

        try
        {
            AsciiString decoded = new HpackHuffmanDecoder().Decode(buffer, buffer.ReadableBytes);
            Assert.False(buffer.IsReadable());

            return decoded.ToByteArray();
        }
        finally
        {
            buffer.Release();
        }
    }
}
