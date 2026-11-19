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

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Base64Codec;
using static Netty.NET.Buffer.Unpooled;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Tests.Base64Codec;

// CLR: CharsetUtil.US_ASCII/ISO_8859_1/UTF_8 are Encoding.ASCII/Latin1/UTF8 (docs/common-clr-design.md, "Native encoding
// and codec ownership").
public class Base64Test
{
    [Fact]
    public void TestNotAddNewLineWhenEndOnLimit()
    {
        ByteBuf src = CopiedBuffer("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcde",
                Encoding.ASCII);
        ByteBuf expectedEncoded =
                CopiedBuffer("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXphYmNkZWZnaGlqa2xtbm9wcXJzdHV2d3h5emFiY2Rl",
                        Encoding.ASCII);
        TestEncode(src, expectedEncoded);
    }

    [Fact]
    public void TestAddNewLine()
    {
        ByteBuf src = CopiedBuffer("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz12345678",
                Encoding.ASCII);
        ByteBuf expectedEncoded =
                CopiedBuffer("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXphYmNkZWZnaGlqa2xtbm9wcXJzdHV2d3h5ejEyMzQ1\nNjc4",
                        Encoding.ASCII);
        TestEncode(src, expectedEncoded);
    }

    [Fact]
    public void TestEncodeEmpty()
    {
        ByteBuf src = Unpooled.EmptyBuffer;
        ByteBuf expectedEncoded = Unpooled.EmptyBuffer;
        TestEncode(src, expectedEncoded);
    }

    [Fact]
    public void TestPaddingNewline()
    {
        string cert = "-----BEGIN CERTIFICATE-----\n" +
                "MIICqjCCAjGgAwIBAgICI1YwCQYHKoZIzj0EATAmMSQwIgYDVQQDDBtUcnVzdGVk\n" +
                "IFRoaW4gQ2xpZW50IFJvb3QgQ0EwIhcRMTYwMTI0MTU0OTQ1LTA2MDAXDTE2MDQy\n" +
                "NTIyNDk0NVowYzEwMC4GA1UEAwwnREMgMGRlYzI0MGYtOTI2OS00MDY5LWE2MTYt\n" +
                "YjJmNTI0ZjA2ZGE0MREwDwYDVQQLDAhEQyBJUFNFQzEcMBoGA1UECgwTVHJ1c3Rl\n" +
                "ZCBUaGluIENsaWVudDB2MBAGByqGSM49AgEGBSuBBAAiA2IABOB7pZYC24sF5gJm\n" +
                "OHXhasxmrNYebdtSAiQRgz0M0pIsogsFeTU/W0HTlTOqwDDckphHESAKHVxa6EBL\n" +
                "d+/8HYZ1AaCmXtG73XpaOyaRr3TipJl2IaJzwuehgDHs0L+qcqOB8TCB7jAwBgYr\n" +
                "BgEBEAQEJgwkMGRlYzI0MGYtOTI2OS00MDY5LWE2MTYtYjJmNTI0ZjA2ZGE0MCMG\n" +
                "CisGAQQBjCHbZwEEFQwTNDkwNzUyMjc1NjM3MTE3Mjg5NjAUBgorBgEEAYwh22cC\n" +
                "BAYMBDIwNTkwCwYDVR0PBAQDAgXgMAkGA1UdEwQCMAAwHQYDVR0OBBYEFGWljaKj\n" +
                "wiGqW61PgLL/zLxj4iirMB8GA1UdIwQYMBaAFA2FRBtG/dGnl0iXP2uKFwJHmEQI\n" +
                "MCcGA1UdJQQgMB4GCCsGAQUFBwMCBggrBgEFBQcDAQYIKwYBBQUHAwkwCQYHKoZI\n" +
                "zj0EAQNoADBlAjAQFP8rMLUxl36u8610LsSCiRG8pP3gjuLaaJMm3tjbVue/TI4C\n" +
                "z3iL8i96YWK0VxcCMQC7pf6Wk3RhUU2Sg6S9e6CiirFLDyzLkaWxuCnXcOwTvuXT\n" +
                "HUQSeUCp2Q6ygS5qKyc=\n" +
                "-----END CERTIFICATE-----";

        string expected = "MIICqjCCAjGgAwIBAgICI1YwCQYHKoZIzj0EATAmMSQwIgYDVQQDDBtUcnVzdGVkIFRoaW4gQ2xp\n" +
                "ZW50IFJvb3QgQ0EwIhcRMTYwMTI0MTU0OTQ1LTA2MDAXDTE2MDQyNTIyNDk0NVowYzEwMC4GA1UE\n" +
                "AwwnREMgMGRlYzI0MGYtOTI2OS00MDY5LWE2MTYtYjJmNTI0ZjA2ZGE0MREwDwYDVQQLDAhEQyBJ\n" +
                "UFNFQzEcMBoGA1UECgwTVHJ1c3RlZCBUaGluIENsaWVudDB2MBAGByqGSM49AgEGBSuBBAAiA2IA\n" +
                "BOB7pZYC24sF5gJmOHXhasxmrNYebdtSAiQRgz0M0pIsogsFeTU/W0HTlTOqwDDckphHESAKHVxa\n" +
                "6EBLd+/8HYZ1AaCmXtG73XpaOyaRr3TipJl2IaJzwuehgDHs0L+qcqOB8TCB7jAwBgYrBgEBEAQE\n" +
                "JgwkMGRlYzI0MGYtOTI2OS00MDY5LWE2MTYtYjJmNTI0ZjA2ZGE0MCMGCisGAQQBjCHbZwEEFQwT\n" +
                "NDkwNzUyMjc1NjM3MTE3Mjg5NjAUBgorBgEEAYwh22cCBAYMBDIwNTkwCwYDVR0PBAQDAgXgMAkG\n" +
                "A1UdEwQCMAAwHQYDVR0OBBYEFGWljaKjwiGqW61PgLL/zLxj4iirMB8GA1UdIwQYMBaAFA2FRBtG\n" +
                "/dGnl0iXP2uKFwJHmEQIMCcGA1UdJQQgMB4GCCsGAQUFBwMCBggrBgEFBQcDAQYIKwYBBQUHAwkw\n" +
                "CQYHKoZIzj0EAQNoADBlAjAQFP8rMLUxl36u8610LsSCiRG8pP3gjuLaaJMm3tjbVue/TI4Cz3iL\n" +
                "8i96YWK0VxcCMQC7pf6Wk3RhUU2Sg6S9e6CiirFLDyzLkaWxuCnXcOwTvuXTHUQSeUCp2Q6ygS5q\n" +
                "Kyc=";

        // CLR: X509Certificate.getEncoded() is GetRawCertData(), the raw data of the whole certificate
        // (https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificate.getrawcertdata).
        ByteBuf src = Unpooled.WrappedBuffer(CertFromString(cert).GetRawCertData());
        ByteBuf expectedEncoded = CopiedBuffer(expected, Encoding.ASCII);
        TestEncode(src, expectedEncoded);
    }

    // CLR: CertificateFactory.getInstance("X.509").generateCertificate(InputStream) is
    // X509CertificateLoader.LoadCertificate, which loads a single PEM or DER certificate from its data
    // (https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificateloader.loadcertificate)
    // and takes the bytes themselves, so no ByteArrayInputStream (MemoryStream) is wrapped around them.
    private static X509Certificate2 CertFromString(string @string)
    {
        byte[] bin = Encoding.ASCII.GetBytes(@string);
        return X509CertificateLoader.LoadCertificate(bin);
    }

    private static void TestEncode(ByteBuf src, ByteBuf expectedEncoded)
    {
        ByteBuf encoded = Base64.Encode(src, true, Base64Dialect.STANDARD);
        try
        {
            Assert.Equal(expectedEncoded, encoded);
        }
        finally
        {
            src.Release();
            expectedEncoded.Release();
            encoded.Release();
        }
    }

    [Fact]
    public void TestEncodeDecodeBE()
    {
        TestEncodeDecode(ByteOrder.BIG_ENDIAN);
    }

    // CLR: testEncodeDecodeLE is not ported: it only differs from testEncodeDecodeBE by the deprecated
    // ByteBuf.order(ByteOrder.LITTLE_ENDIAN) view ("use the Little Endian accessors, e.g. getShortLE, getIntLE instead of
    // creating a buffer with swapped endianness"), which Netty.NET.Buffer does not port, so no little-endian buffer
    // exists to encode or decode.

    private static void TestEncodeDecode(ByteOrder order)
    {
        TestEncodeDecode(order, Base64Dialect.STANDARD);
        TestEncodeDecode(order, Base64Dialect.URL_SAFE);
    }

    private static void TestEncodeDecode(ByteOrder order, Base64Dialect dialect)
    {
        TestEncodeDecode(order, dialect, true, true);
        TestEncodeDecode(order, dialect, true, false);
        TestEncodeDecode(order, dialect, false, true);
        TestEncodeDecode(order, dialect, false, false);
    }

    private static void TestEncodeDecode(ByteOrder order, Base64Dialect dialect, bool breakLines, bool padded)
    {
        TestEncodeDecode(64, order, dialect, breakLines, padded);
        TestEncodeDecode(128, order, dialect, breakLines, padded);
        TestEncodeDecode(512, order, dialect, breakLines, padded);
        TestEncodeDecode(1024, order, dialect, breakLines, padded);
        TestEncodeDecode(4096, order, dialect, breakLines, padded);
        TestEncodeDecode(8192, order, dialect, breakLines, padded);
        TestEncodeDecode(16384, order, dialect, breakLines, padded);
    }

    private static void TestEncodeDecode(
            int size, ByteOrder order, Base64Dialect dialect, bool breakLines, bool padded)
    {
        byte[] bytes = new byte[size];
        // CLR: ThreadLocalRandom.current() is Random.Shared, the thread-safe instance usable from any thread
        // (https://learn.microsoft.com/dotnet/api/system.random.shared).
        Random.Shared.NextBytes(bytes);

        // JDK encoder / decoder
        // CLR: java.util.Base64 is replaced by the BCL codecs of GetEncoder()/GetUrlEncoder()/GetDecoder()/GetUrlDecoder().
        Func<byte[], byte[]> jdkEncoder =
                dialect == Base64Dialect.STANDARD ? GetEncoder() : GetUrlEncoder();
        jdkEncoder = padded ? jdkEncoder : WithoutPadding(jdkEncoder);
        Func<byte[], byte[]> jdkDecoder =
                dialect == Base64Dialect.STANDARD ? GetDecoder() : GetUrlDecoder();

        ByteBuf src = null;
        ByteBuf encoded = null;
        ByteBuf decoded = null;
        ByteBuf expectedBuf = null;
        byte[] jdKEncoded;
        byte[] jdkDecoded;
        try
        {
            jdKEncoded = breakLines ? InsertNewLines(jdkEncoder(bytes)) : jdkEncoder(bytes);
            // CLR: ByteBuf has no order(ByteOrder) (deprecated in the source and not ported); every buffer is
            // BIG_ENDIAN, the only order passed here, so .order(order) is the wrapped buffer itself.
            src = Unpooled.WrappedBuffer(bytes);
            encoded = Base64.Encode(src, breakLines, dialect, padded);

            // Assert JDK encoded equals netty encoded
            Assert.Equal(Unpooled.WrappedBuffer(jdKEncoded), encoded,
                    StringUtil.NEWLINE + "expected: " + ByteBufUtil.HexDump(jdKEncoded) +
                    StringUtil.NEWLINE + "actual--: " +  ByteBufUtil.HexDump(encoded));

            jdkDecoded = jdkDecoder(StripNewLines(ByteBufUtil.GetBytes(encoded)));
            decoded = Base64.Decode(encoded, dialect);
            expectedBuf = Unpooled.WrappedBuffer(bytes);

            // Assert JDK decoded equals netty decoded
            Assert.Equal(Unpooled.WrappedBuffer(jdkDecoded) , decoded,
                    StringUtil.NEWLINE + "expected: " + ByteBufUtil.HexDump(jdkDecoded) +
                    StringUtil.NEWLINE + "actual--: " + ByteBufUtil.HexDump(decoded));

            // Assert netty decoded equals expected
            Assert.Equal(expectedBuf, decoded,
                    StringUtil.NEWLINE + "expected: " + ByteBufUtil.HexDump(expectedBuf) +
                    StringUtil.NEWLINE + "actual--: " + ByteBufUtil.HexDump(decoded));
        }
        finally
        {
            ReferenceCountUtil.SafeRelease(src);
            ReferenceCountUtil.SafeRelease(encoded);
            ReferenceCountUtil.SafeRelease(decoded);
            ReferenceCountUtil.SafeRelease(expectedBuf);
        }
    }

    // CLR: java.util.Base64.getEncoder() uses the Base64 alphabet, pads and adds no line separator
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Base64.html), as Convert.ToBase64String
    // does with "=" as trailing padding (https://learn.microsoft.com/dotnet/api/system.convert.tobase64string).
    private static Func<byte[], byte[]> GetEncoder()
    {
        return src => Encoding.ASCII.GetBytes(Convert.ToBase64String(src));
    }

    // CLR: java.util.Base64.getUrlEncoder() uses the URL and Filename safe alphabet and pads. Base64Url uses '-' and '_'
    // and omits the optional padding (https://learn.microsoft.com/dotnet/api/system.buffers.text.base64url.encodetoutf8),
    // so the padding is appended to a multiple of four characters.
    private static Func<byte[], byte[]> GetUrlEncoder()
    {
        return src =>
        {
            byte[] unpadded = System.Buffers.Text.Base64Url.EncodeToUtf8(src);
            byte[] padded = new byte[(unpadded.Length + 3) / 4 * 4];
            unpadded.CopyTo(padded, 0);
            padded.AsSpan(unpadded.Length).Fill((byte) '=');
            return padded;
        };
    }

    // CLR: Encoder.withoutPadding() encodes "without adding any padding character at the end of the encoded byte data"
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Base64.Encoder.html#withoutPadding()).
    private static Func<byte[], byte[]> WithoutPadding(Func<byte[], byte[]> encoder)
    {
        return src =>
        {
            byte[] encoded = encoder(src);
            int length = encoded.Length;
            while (length > 0 && encoded[length - 1] == '=')
            {
                length--;
            }
            return encoded.AsSpan(0, length).ToArray();
        };
    }

    // CLR: the JDK decoder accepts '=' padding "but is not required"
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Base64.Decoder.html), whereas
    // Convert.FromBase64String rejects a length that is not a multiple of 4
    // (https://learn.microsoft.com/dotnet/api/system.convert.frombase64string), so unpadded input is padded first.
    private static Func<byte[], byte[]> GetDecoder()
    {
        return src =>
        {
            string text = Encoding.ASCII.GetString(src);
            return Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '='));
        };
    }

    // CLR: Base64Url decoding treats padding as optional
    // (https://learn.microsoft.com/dotnet/api/system.buffers.text.base64url.decodefromutf8), as the JDK URL decoder does.
    private static Func<byte[], byte[]> GetUrlDecoder()
    {
        return src => System.Buffers.Text.Base64Url.DecodeFromUtf8(src);
    }

    // CLR: MemoryStream replaces ByteArrayOutputStream and the using statement replaces try-with-resources.
    private static byte[] InsertNewLines(byte[] src)
    {
        if (src == null || src.Length == 0)
        {
            return src;
        }

        using (MemoryStream os = new MemoryStream())
        {
            for (int i = 0; i < src.Length; i++)
            {
                os.WriteByte(src[i]);
                if (i + 1 < src.Length && (i + 1) % 76 == 0)
                {
                    os.WriteByte((byte) '\n');
                }
            }
            return os.ToArray();
        }
    }

    private static byte[] StripNewLines(byte[] src)
    {
        if (src == null || src.Length == 0)
        {
            return src;
        }

        using (MemoryStream os = new MemoryStream())
        {
            foreach (byte b in src)
            {
                if (b != '\n')
                {
                    os.WriteByte(b);
                }
            }
            return os.ToArray();
        }
    }

    [Fact]
    public void TestOverflowEncodedBufferSize()
    {
        Assert.Equal(int.MaxValue, Base64.EncodedBufferSize(int.MaxValue, true));
        Assert.Equal(int.MaxValue, Base64.EncodedBufferSize(int.MaxValue, false));
    }

    [Fact]
    public void TestOverflowDecodedBufferSize()
    {
        Assert.Equal(1610612736, Base64.DecodedBufferSize(int.MaxValue));
    }

    [Fact]
    public void DecodingFailsOnInvalidInputByte()
    {
        char[] invalidChars = {'\u007F', '\u0080', '½', 'ÿ'};
        foreach (char invalidChar in invalidChars)
        {
            ByteBuf buf = CopiedBuffer("eHh4" + invalidChar, Encoding.Latin1);
            try
            {
                // CLR: IllegalArgumentException is ArgumentException.
                Assert.Throws<ArgumentException>(() => Base64.Decode(buf));
            }
            finally
            {
                Assert.True(buf.Release());
            }
        }
    }
}
