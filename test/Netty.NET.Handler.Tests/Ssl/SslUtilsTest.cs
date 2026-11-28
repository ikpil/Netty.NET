/*
 * Copyright 2017 The Netty Project
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
using System.Net.Security;
using System.Security.Authentication;
using Netty.NET.Buffer;
using Netty.NET.Handler.Ssl;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: SslUtils members are internal (package-private in Java) and visible through InternalsVisibleTo; the static imports
// are SslUtils-qualified names. @ParameterizedTest with @ValueSource is [Theory] with InlineData.
public class SslUtilsTest
{

    // CLR: the JDK SSLEngine that wraps a ClientHello is a client SslStream (ssl design decision 2,
    // docs/handler-ssl-design.md): NewEngine starts its handshake over a stream that keeps what SslStream writes, the
    // ClientHello record, and ends the input, so the handshake then fails, as the source's engine is never answered.
    // ByteBuf and ReadOnlyMemory<byte> have no mutable byte order ("Java's mutable ByteOrder facade is replaced by
    // explicit BE/LE access", docs/buffer-porting.md), so the little-endian ByteBuf and ByteBuffer views have no
    // counterpart: both "LE" buffers hold the same record, the ByteBuf and the ReadOnlyMemory<byte>[] overloads must agree,
    // and the length is also checked against the record header.
    [Fact]
    public void TestPacketLength()
    {
        CaptureStream cTOsLE = new CaptureStream();
        CaptureStream cTOsBE = new CaptureStream();
        using SslStream engineLE = NewEngine(cTOsLE);
        using SslStream engineBE = NewEngine(cTOsBE);

        Assert.True(cTOsLE.Length > 0);

        Assert.True(cTOsBE.Length > 0);

        byte[] clientHello = cTOsBE.ToArray();
        ByteBuf bufferLE = Unpooled.Buffer().WriteBytes(clientHello);
        ByteBuf bufferBE = Unpooled.Buffer().WriteBytes(clientHello);

        // Test that the packet-length for BE and LE is the same
        Assert.Equal(SslUtils.GetEncryptedPacketLength(bufferBE, 0, true),
                SslUtils.GetEncryptedPacketLength(bufferLE, 0, true));
        Assert.Equal(SslUtils.GetEncryptedPacketLength(new ReadOnlyMemory<byte>[] { bufferBE.ReadableMemory }, 0),
                SslUtils.GetEncryptedPacketLength(new ReadOnlyMemory<byte>[] { bufferLE.ReadableMemory }, 0));
        Assert.Equal(SslUtils.GetEncryptedPacketLength(bufferBE, 0, true),
                SslUtils.GetEncryptedPacketLength(new ReadOnlyMemory<byte>[] { bufferBE.ReadableMemory }, 0));
        Assert.Equal(SslUtils.SSL_RECORD_HEADER_LENGTH + ((clientHello[3] << 8) | clientHello[4]),
                SslUtils.GetEncryptedPacketLength(bufferBE, 0, true));
        bufferLE.Release();
        bufferBE.Release();
    }

    private static SslStream NewEngine(CaptureStream cTOs)
    {
        SslStream engine = new SslStream(cTOs, false);

        try
        {
            engine.AuthenticateAsClient(new SslClientAuthenticationOptions { TargetHost = "netty.io" });
        }
        catch (Exception e) when (e is AuthenticationException || e is IOException)
        {
            // CLR: the capture stream ends after the ClientHello was written.
        }

        return engine;
    }

    [Fact]
    public void TestIsTLSv13Cipher()
    {
        Assert.True(SslUtils.IsTLSv13Cipher("TLS_AES_128_GCM_SHA256"));
        Assert.True(SslUtils.IsTLSv13Cipher("TLS_AES_256_GCM_SHA384"));
        Assert.True(SslUtils.IsTLSv13Cipher("TLS_CHACHA20_POLY1305_SHA256"));
        Assert.True(SslUtils.IsTLSv13Cipher("TLS_AES_128_CCM_SHA256"));
        Assert.True(SslUtils.IsTLSv13Cipher("TLS_AES_128_CCM_8_SHA256"));
        Assert.False(SslUtils.IsTLSv13Cipher("TLS_DHE_RSA_WITH_AES_128_GCM_SHA256"));
    }

    [Fact]
    public void ShouldGetPacketLengthOfGmsslProtocolFromByteBuf()
    {
        int bodyLength = 65;
        ByteBuf buf = Unpooled.Buffer()
                              .WriteByte(SslUtils.SSL_CONTENT_TYPE_HANDSHAKE)
                              .WriteShort(SslUtils.GMSSL_PROTOCOL_VERSION)
                              .WriteShort(bodyLength);

        int packetLength = SslUtils.GetEncryptedPacketLength(buf, 0, true);
        Assert.Equal(bodyLength + SslUtils.SSL_RECORD_HEADER_LENGTH, packetLength);
        buf.Release();
    }

    // CLR: buf.nioBuffer() exposes the readable bytes, which is ByteBuf.ReadableMemory.
    [Fact]
    public void ShouldGetPacketLengthOfGmsslProtocolFromByteBuffer()
    {
        int bodyLength = 65;
        ByteBuf buf = Unpooled.Buffer()
                              .WriteByte(SslUtils.SSL_CONTENT_TYPE_HANDSHAKE)
                              .WriteShort(SslUtils.GMSSL_PROTOCOL_VERSION)
                              .WriteShort(bodyLength);

        int packetLength = SslUtils.GetEncryptedPacketLength(new ReadOnlyMemory<byte>[] { buf.ReadableMemory }, 0);
        Assert.Equal(bodyLength + SslUtils.SSL_RECORD_HEADER_LENGTH, packetLength);
        buf.Release();
    }

    // CLR: the DTLS versions are negative short constants; their int values are the InlineData, and WriteShort writes the
    // low 16 bits as writeShort does.
    [Theory]
    [InlineData((int) SslUtils.DTLS_1_0)]
    [InlineData((int) SslUtils.DTLS_1_2)]
    [InlineData((int) SslUtils.DTLS_1_3)] // six numbers
    public void ShouldGetPacketLengthOfDtlsRecordFromByteBuf(int dtlsVersion)
    {
        int bodyLength = 65;
        ByteBuf buf = Unpooled.Buffer()
                .WriteByte(SslUtils.SSL_CONTENT_TYPE_HANDSHAKE)
                .WriteShort(dtlsVersion)
                .WriteShort(0) // epoch
                .WriteBytes(new byte[6]) // sequence number
                .WriteShort(bodyLength);

        int packetLength = SslUtils.GetEncryptedPacketLength(buf, 0, true);
        // bodyLength + DTLS_RECORD_HEADER_LENGTH = 65 + 13 = 78
        Assert.Equal(78, packetLength);
        buf.Release();
    }

    [Theory]
    [InlineData((int) SslUtils.DTLS_1_0)]
    [InlineData((int) SslUtils.DTLS_1_2)]
    [InlineData((int) SslUtils.DTLS_1_3)] // six numbers
    public void ShouldGetPacketLengthOfFirstDtlsRecordFromByteBuf(int dtlsVersion)
    {
        int bodyLength = 65;
        ByteBuf buf = Unpooled.Buffer()
                .WriteByte(SslUtils.SSL_CONTENT_TYPE_HANDSHAKE)
                .WriteShort(dtlsVersion)
                .WriteShort(0) // epoch
                .WriteBytes(new byte[6]) // sequence number
                .WriteShort(bodyLength)
                .WriteBytes(new byte[65])
                .WriteByte(SslUtils.SSL_CONTENT_TYPE_HANDSHAKE)
                .WriteShort(dtlsVersion)
                .WriteShort(0) // epoch
                .WriteBytes(new byte[6]) // sequence number
                .WriteShort(bodyLength)
                .WriteBytes(new byte[65]);

        int packetLength = SslUtils.GetEncryptedPacketLength(buf, 0, true);
        Assert.Equal(78, packetLength);
        buf.Release();
    }

    [Theory]
    [InlineData((int) SslUtils.DTLS_1_0)]
    [InlineData((int) SslUtils.DTLS_1_2)]
    [InlineData((int) SslUtils.DTLS_1_3)] // six numbers
    public void ShouldSupportIncompletePackets(int dtlsVersion)
    {
        ByteBuf buf = Unpooled.Buffer()
                .WriteByte(SslUtils.SSL_CONTENT_TYPE_HANDSHAKE)
                .WriteShort(dtlsVersion)
                .WriteShort(0) // epoch
                .WriteBytes(new byte[6]) // sequence number
                .WriteByte(0);
                // Left off the last byte of the length on purpose

        int packetLength = SslUtils.GetEncryptedPacketLength(buf, 0, true);
        Assert.Equal(SslUtils.NOT_ENOUGH_DATA, packetLength);
        buf.Release();
    }

    // CLR: (byte) 0x82 is the unsigned System.Byte 0x82 (owner decision: Java byte values are C# byte); the buffer holds the
    // same octets.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProbeForSSLv2(bool probeSSLv2)
    {
        ByteBuf buf = Unpooled.WrappedBuffer(new byte[] { 0x30, 0x82, 0x0c, 0x48, 0x02, 0x01, 0x01, 0x01 });
        int packetLength = SslUtils.GetEncryptedPacketLength(buf, 0, probeSSLv2);

        if (probeSSLv2)
        {
            Assert.NotEqual(SslUtils.NOT_ENCRYPTED, packetLength);
        }
        else
        {
            Assert.Equal(SslUtils.NOT_ENCRYPTED, packetLength);
        }

        buf.Release();
    }

    [Fact]
    public void TestValidHostNameForSni()
    {
        Assert.False(SslUtils.IsValidHostNameForSNI("/test.de"), "SNI domain can't start with /");
        Assert.False(SslUtils.IsValidHostNameForSNI("test.de."), "SNI domain can't end with a dot/");
        Assert.True(SslUtils.IsValidHostNameForSNI("test.de"));
        // see https://datatracker.ietf.org/doc/html/rfc6066#section-3
        // it has to be test.local to qualify as SNI
        Assert.False(SslUtils.IsValidHostNameForSNI("test"), "SNI has to be FQDN");
    }

    // CLR: the ByteBuffer that SSLEngine.wrap fills: Write keeps the ciphertext SslStream writes, Read reports the end of
    // the stream.
    private sealed class CaptureStream : System.IO.Stream
    {
        private readonly MemoryStream written = new MemoryStream();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => written.Length;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] ToArray()
        {
            return written.ToArray();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            written.Write(buffer, offset, count);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
