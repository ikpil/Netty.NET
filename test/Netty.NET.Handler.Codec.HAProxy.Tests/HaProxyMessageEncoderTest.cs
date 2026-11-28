/*
 * Copyright 2020 The Netty Project
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
using System.Collections.Generic;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.HAProxy.HAProxyConstants;
using static Netty.NET.Handler.Codec.HAProxy.HAProxyMessageEncoder;
using Type = Netty.NET.Handler.Codec.HAProxy.HAProxyTLV.Type;

namespace Netty.NET.Handler.Codec.HAProxy.Tests;

// CLR: the static imports of HAProxyConstants and HAProxyMessageEncoder are using static directives over their internal
// members (InternalsVisibleTo), and the import of HAProxyTLV.Type is a using alias. Anonymous Executable classes are
// lambdas; assertThrows is Assert.Throws, whose exception type must match exactly (the thrown types are the asserted
// ones), and NullPointerException from checkNotNull is ArgumentNullException. ByteBuf.array().length of the copied
// ASCII buffers is their readable length, because the Buffer port has no array() accessor; new String(byte[]) decodes
// with Encoding.Default as codec-base maps Charset.defaultCharset(). Java byte literals below zero in byte[]
// initializers are unchecked((byte) ...), and Collections.singletonList is a one-element array.
public class HaProxyMessageEncoderTest
{

    private const int V2_HEADER_BYTES_LENGTH = 16;
    private const int IPv4_ADDRESS_BYTES_LENGTH = 12;
    private const int IPv6_ADDRESS_BYTES_LENGTH = 36;

    [Fact]
    public void TestIPV4EncodeProxyV1()
    {
        EmbeddedChannel ch = new EmbeddedChannel(INSTANCE);

        HAProxyMessage message = new HAProxyMessage(
                HAProxyProtocolVersion.V1, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP4,
                "192.168.0.1", "192.168.0.11", 56324, 443);
        Assert.True(ch.WriteOutbound(message));

        ByteBuf byteBuf = ch.ReadOutbound<ByteBuf>();

        Assert.Equal("PROXY TCP4 192.168.0.1 192.168.0.11 56324 443\r\n",
                     byteBuf.ToString(Encoding.ASCII));

        byteBuf.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestIPV6EncodeProxyV1()
    {
        EmbeddedChannel ch = new EmbeddedChannel(INSTANCE);

        HAProxyMessage message = new HAProxyMessage(
                HAProxyProtocolVersion.V1, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP6,
                "2001:0db8:85a3:0000:0000:8a2e:0370:7334", "1050:0:0:0:5:600:300c:326b", 56324, 443);
        Assert.True(ch.WriteOutbound(message));

        ByteBuf byteBuf = ch.ReadOutbound<ByteBuf>();

        Assert.Equal("PROXY TCP6 2001:0db8:85a3:0000:0000:8a2e:0370:7334 1050:0:0:0:5:600:300c:326b 56324 443\r\n",
                     byteBuf.ToString(Encoding.ASCII));

        byteBuf.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestIPv4EncodeProxyV2()
    {
        EmbeddedChannel ch = new EmbeddedChannel(INSTANCE);

        HAProxyMessage message = new HAProxyMessage(
                HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP4,
                "192.168.0.1", "192.168.0.11", 56324, 443);
        Assert.True(ch.WriteOutbound(message));

        ByteBuf byteBuf = ch.ReadOutbound<ByteBuf>();

        // header
        byte[] headerBytes = ByteBufUtil.GetBytes(byteBuf, 0, 12);
        Assert.Equal(BINARY_PREFIX, headerBytes);

        // command
        byte commandByte = byteBuf.GetByte(12);
        Assert.Equal(0x02, (commandByte & 0xf0) >> 4);
        Assert.Equal(0x01, commandByte & 0x0f);

        // transport protocol, address family
        byte transportByte = byteBuf.GetByte(13);
        Assert.Equal(0x01, (transportByte & 0xf0) >> 4);
        Assert.Equal(0x01, transportByte & 0x0f);

        // source address length
        int sourceAddrLength = byteBuf.GetUnsignedShort(14);
        Assert.Equal(12, sourceAddrLength);

        // source address
        byte[] sourceAddr = ByteBufUtil.GetBytes(byteBuf, 16, 4);
        Assert.Equal(new byte[] { (byte) 0xc0, (byte) 0xa8, 0x00, 0x01 }, sourceAddr);

        // destination address
        byte[] destAddr = ByteBufUtil.GetBytes(byteBuf, 20, 4);
        Assert.Equal(new byte[] { (byte) 0xc0, (byte) 0xa8, 0x00, 0x0b }, destAddr);

        // source port
        int sourcePort = byteBuf.GetUnsignedShort(24);
        Assert.Equal(56324, sourcePort);

        // destination port
        int destPort = byteBuf.GetUnsignedShort(26);
        Assert.Equal(443, destPort);

        byteBuf.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestIPv6EncodeProxyV2()
    {
        EmbeddedChannel ch = new EmbeddedChannel(INSTANCE);

        HAProxyMessage message = new HAProxyMessage(
                HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP6,
                "2001:0db8:85a3:0000:0000:8a2e:0370:7334", "1050:0:0:0:5:600:300c:326b", 56324, 443);
        Assert.True(ch.WriteOutbound(message));

        ByteBuf byteBuf = ch.ReadOutbound<ByteBuf>();

        // header
        byte[] headerBytes = ByteBufUtil.GetBytes(byteBuf, 0, 12);
        Assert.Equal(BINARY_PREFIX, headerBytes);

        // command
        byte commandByte = byteBuf.GetByte(12);
        Assert.Equal(0x02, (commandByte & 0xf0) >> 4);
        Assert.Equal(0x01, commandByte & 0x0f);

        // transport protocol, address family
        byte transportByte = byteBuf.GetByte(13);
        Assert.Equal(0x02, (transportByte & 0xf0) >> 4);
        Assert.Equal(0x01, transportByte & 0x0f);

        // source address length
        int sourceAddrLength = byteBuf.GetUnsignedShort(14);
        Assert.Equal(IPv6_ADDRESS_BYTES_LENGTH, sourceAddrLength);

        // source address
        byte[] sourceAddr = ByteBufUtil.GetBytes(byteBuf, 16, 16);
        Assert.Equal(new byte[] {
                (byte) 0x20, (byte) 0x01, 0x0d, (byte) 0xb8,
                (byte) 0x85, (byte) 0xa3, 0x00, 0x00, 0x00, 0x00, (byte) 0x8a, 0x2e,
                0x03, 0x70, 0x73, 0x34
        }, sourceAddr);

        // destination address
        byte[] destAddr = ByteBufUtil.GetBytes(byteBuf, 32, 16);
        Assert.Equal(new byte[] {
                (byte) 0x10, (byte) 0x50, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x05, 0x06, 0x00, 0x30, 0x0c, 0x32, 0x6b
        }, destAddr);

        // source port
        int sourcePort = byteBuf.GetUnsignedShort(48);
        Assert.Equal(56324, sourcePort);

        // destination port
        int destPort = byteBuf.GetUnsignedShort(50);
        Assert.Equal(443, destPort);

        byteBuf.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUnixEncodeProxyV2()
    {
        EmbeddedChannel ch = new EmbeddedChannel(INSTANCE);

        HAProxyMessage message = new HAProxyMessage(
                HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.UNIX_STREAM,
                "/var/run/src.sock", "/var/run/dst.sock", 0, 0);
        Assert.True(ch.WriteOutbound(message));

        ByteBuf byteBuf = ch.ReadOutbound<ByteBuf>();

        // header
        byte[] headerBytes = ByteBufUtil.GetBytes(byteBuf, 0, 12);
        Assert.Equal(BINARY_PREFIX, headerBytes);

        // command
        byte commandByte = byteBuf.GetByte(12);
        Assert.Equal(0x02, (commandByte & 0xf0) >> 4);
        Assert.Equal(0x01, commandByte & 0x0f);

        // transport protocol, address family
        byte transportByte = byteBuf.GetByte(13);
        Assert.Equal(0x03, (transportByte & 0xf0) >> 4);
        Assert.Equal(0x01, transportByte & 0x0f);

        // address length
        int addrLength = byteBuf.GetUnsignedShort(14);
        Assert.Equal(TOTAL_UNIX_ADDRESS_BYTES_LENGTH, addrLength);

        // source address
        int srcAddrEnd = byteBuf.ForEachByte(16, 108, ByteProcessor.FIND_NUL);
        Assert.Equal("/var/run/src.sock",
                     byteBuf.Slice(16, srcAddrEnd - 16).ToString(Encoding.ASCII));

        // destination address
        int dstAddrEnd = byteBuf.ForEachByte(124, 108, ByteProcessor.FIND_NUL);
        Assert.Equal("/var/run/dst.sock",
                     byteBuf.Slice(124, dstAddrEnd - 124).ToString(Encoding.ASCII));

        byteBuf.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestTLVEncodeProxy()
    {
        EmbeddedChannel ch = new EmbeddedChannel(INSTANCE);

        List<HAProxyTLV> tlvs = new List<HAProxyTLV>();

        ByteBuf helloWorld = Unpooled.CopiedBuffer("hello world", Encoding.ASCII);
        HAProxyTLV alpnTlv = new HAProxyTLV(Type.PP2_TYPE_ALPN, (byte) 0x01, helloWorld.Copy());
        tlvs.Add(alpnTlv);

        ByteBuf arbitrary = Unpooled.CopiedBuffer("an arbitrary string", Encoding.ASCII);
        HAProxyTLV authorityTlv = new HAProxyTLV(Type.PP2_TYPE_AUTHORITY, (byte) 0x01, arbitrary.Copy());
        tlvs.Add(authorityTlv);

        HAProxyMessage message = new HAProxyMessage(
                HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP4,
                "192.168.0.1", "192.168.0.11", 56324, 443, tlvs);
        Assert.True(ch.WriteOutbound(message));

        ByteBuf byteBuf = ch.ReadOutbound<ByteBuf>();

        // length
        Assert.Equal(byteBuf.GetUnsignedShort(14), byteBuf.ReadableBytes - V2_HEADER_BYTES_LENGTH);

        // skip to tlv section
        ByteBuf tlv = byteBuf.SkipBytes(V2_HEADER_BYTES_LENGTH + IPv4_ADDRESS_BYTES_LENGTH);

        // alpn tlv
        Assert.Equal(alpnTlv.TypeByteValue(), tlv.ReadByte());
        short bufLength = tlv.ReadShort();
        Assert.Equal(helloWorld.ReadableBytes, bufLength);
        Assert.Equal(helloWorld, tlv.ReadSlice(bufLength));

        // authority tlv
        Assert.Equal(authorityTlv.TypeByteValue(), tlv.ReadByte());
        bufLength = tlv.ReadShort();
        Assert.Equal(arbitrary.ReadableBytes, bufLength);
        Assert.Equal(arbitrary, tlv.ReadSlice(bufLength));

        byteBuf.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestSslTLVEncodeProxy()
    {
        EmbeddedChannel ch = new EmbeddedChannel(INSTANCE);

        List<HAProxyTLV> tlvs = new List<HAProxyTLV>();

        ByteBuf helloWorld = Unpooled.CopiedBuffer("hello world", Encoding.ASCII);
        HAProxyTLV alpnTlv = new HAProxyTLV(Type.PP2_TYPE_ALPN, (byte) 0x01, helloWorld.Copy());
        tlvs.Add(alpnTlv);

        ByteBuf arbitrary = Unpooled.CopiedBuffer("an arbitrary string", Encoding.ASCII);
        HAProxyTLV authorityTlv = new HAProxyTLV(Type.PP2_TYPE_AUTHORITY, (byte) 0x01, arbitrary.Copy());
        tlvs.Add(authorityTlv);

        ByteBuf sslContent = Unpooled.CopiedBuffer("some ssl content", Encoding.ASCII);
        HAProxySSLTLV haProxySSLTLV = new HAProxySSLTLV(1, (byte) 0x01, tlvs, sslContent.Copy());

        HAProxyMessage message = new HAProxyMessage(
                HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP4,
                "192.168.0.1", "192.168.0.11", 56324, 443,
                new HAProxyTLV[] { haProxySSLTLV });
        Assert.True(ch.WriteOutbound(message));

        ByteBuf byteBuf = ch.ReadOutbound<ByteBuf>();

        Assert.Equal(byteBuf.GetUnsignedShort(14), byteBuf.ReadableBytes - V2_HEADER_BYTES_LENGTH);
        ByteBuf tlv = byteBuf.SkipBytes(V2_HEADER_BYTES_LENGTH + IPv4_ADDRESS_BYTES_LENGTH);

        // ssl tlv type
        Assert.Equal(haProxySSLTLV.TypeByteValue(), tlv.ReadByte());

        // length
        int bufLength = tlv.ReadUnsignedShort();
        Assert.Equal(bufLength, tlv.ReadableBytes);

        // client, verify
        Assert.Equal(0x01, byteBuf.ReadByte());
        Assert.Equal(1, byteBuf.ReadInt());

        // alpn tlv
        Assert.Equal(alpnTlv.TypeByteValue(), tlv.ReadByte());
        bufLength = tlv.ReadShort();
        Assert.Equal(helloWorld.ReadableBytes, bufLength);
        Assert.Equal(helloWorld, tlv.ReadSlice(bufLength));

        // authority tlv
        Assert.Equal(authorityTlv.TypeByteValue(), tlv.ReadByte());
        bufLength = tlv.ReadShort();
        Assert.Equal(arbitrary.ReadableBytes, bufLength);
        Assert.Equal(arbitrary, tlv.ReadSlice(bufLength));

        byteBuf.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestReencodeDecodedNestedSslTlvsRoundTrips()
    {
        // PROXY v2 header with a top-level PP2_TYPE_SSL TLV that itself encapsulates a nested
        // PP2_TYPE_SSL TLV (which in turn encapsulates a PP2_TYPE_SSL_VERSION TLV) and a
        // PP2_TYPE_SSL_CN TLV. HAProxyMessage.readTlvs(...) flattens the two direct children of the
        // top-level SSL TLV into HAProxyMessage.tlvs() alongside the SSL TLV itself.
        byte[] bytes = {
                13, 10, 13, 10, 0, 13, 10, 81, 85, 73, 84, 10,
                33, 17,
                0, 43,
                127, 0, 0, 1, 127, 0, 0, 1, unchecked((byte) -55), unchecked((byte) -90), 7, 89,
                32, 0, 28,
                5, 0, 0, 0, 0,
                32, 0, 13,
                1, 0, 0, 0, 0,
                33, 0, 5, 84, 76, 83, 118, 49,
                34, 0, 4, 76, 69, 65, 70
        };
        int originalPayloadLength = ((bytes[14] & 0xff) << 8) | (bytes[15] & 0xff);

        ByteBuf original = Unpooled.WrappedBuffer(bytes);
        HAProxyMessage decoded = HAProxyMessage.DecodeHeader(original);
        EmbeddedChannel encoder = new EmbeddedChannel(INSTANCE);
        EmbeddedChannel downstream = new EmbeddedChannel(new HAProxyMessageDecoder());

        try
        {
            // 3 flattened entries: the top-level SSL TLV, its nested SSL TLV child, and the SSL_CN child.
            Assert.Equal(3, decoded.Tlvs().Count);

            Assert.True(encoder.WriteOutbound(decoded.Retain()));
            ByteBuf encoded = encoder.ReadOutbound<ByteBuf>();

            // The declared v2 payload length must equal the number of bytes actually written, and must
            // match the original (valid) input length -- re-encoding a decoded message must not inflate
            // or shrink the frame.
            int declaredLength = encoded.GetUnsignedShort(14);
            int actualPayloadBytes = encoded.ReadableBytes - V2_HEADER_BYTES_LENGTH;
            Assert.Equal(originalPayloadLength, declaredLength);
            Assert.Equal(originalPayloadLength, actualPayloadBytes);

            // Append trailing application data that must never be consumed as PROXY protocol bytes.
            ByteBuf input = encoder.Alloc().Buffer();
            input.WriteBytes(encoded, encoded.ReaderIndex, encoded.ReadableBytes);
            string appDataString = "GET /ok\r\n";
            input.WriteCharSequence(appDataString, Encoding.ASCII);
            encoded.Release();

            Assert.True(downstream.WriteInbound(input));
            HAProxyMessage downstreamMsg = downstream.ReadInbound<HAProxyMessage>();
            Assert.Equal(3, downstreamMsg.Tlvs().Count);
            downstreamMsg.Release();

            ByteBuf forwardedAppData = downstream.ReadInbound<ByteBuf>();
            Assert.Equal(appDataString, forwardedAppData.ToString(Encoding.ASCII));
            forwardedAppData.Release();
            Assert.Null(downstream.ReadInbound());
        }
        finally
        {
            decoded.Release();
            original.Release();
            encoder.FinishAndReleaseAll();
            downstream.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestEncodeLocalProxyV2()
    {
        EmbeddedChannel ch = new EmbeddedChannel(INSTANCE);

        HAProxyMessage message = new HAProxyMessage(
                HAProxyProtocolVersion.V2, HAProxyCommand.LOCAL, HAProxyProxiedProtocol.UNKNOWN,
                null, null, 0, 0);
        Assert.True(ch.WriteOutbound(message));

        ByteBuf byteBuf = ch.ReadOutbound<ByteBuf>();

        // header
        byte[] headerBytes = new byte[12];
        byteBuf.ReadBytes(headerBytes);
        Assert.Equal(BINARY_PREFIX, headerBytes);

        // command
        byte commandByte = byteBuf.ReadByte();
        Assert.Equal(0x02, (commandByte & 0xf0) >> 4);
        Assert.Equal(0x00, commandByte & 0x0f);

        // transport protocol, address family
        byte transportByte = byteBuf.ReadByte();
        Assert.Equal(0x00, transportByte);

        // source address length
        int sourceAddrLength = byteBuf.ReadUnsignedShort();
        Assert.Equal(0, sourceAddrLength);

        Assert.False(byteBuf.IsReadable());

        byteBuf.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestInvalidIpV4Address()
    {
        string invalidIpv4Address = "192.168.0.1234";
        Assert.Throws<ArgumentException>(() =>
        {
            new HAProxyMessage(
                    HAProxyProtocolVersion.V1, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP4,
                    invalidIpv4Address, "192.168.0.11", 56324, 443);
        });
    }

    [Fact]
    public void TestInvalidIpV6Address()
    {
        string invalidIpv6Address = "2001:0db8:85a3:0000:0000:8a2e:0370:73345";
        Assert.Throws<ArgumentException>(() =>
        {
            new HAProxyMessage(
                    HAProxyProtocolVersion.V1, HAProxyCommand.PROXY, HAProxyProxiedProtocol.TCP6,
                    invalidIpv6Address, "1050:0:0:0:5:600:300c:326b", 56324, 443);
        });
    }

    [Fact]
    public void TestInvalidUnixAddress()
    {
        string invalidUnixAddress = Encoding.Default.GetString(new byte[UNIX_ADDRESS_BYTES_LENGTH + 1]);
        Assert.Throws<ArgumentException>(() =>
        {
            new HAProxyMessage(
                    HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.UNIX_STREAM,
                    invalidUnixAddress, "/var/run/dst.sock", 0, 0);
        });
    }

    [Theory]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData(' ')]
    public void TestIllegalCharacterInV1UnixAddress(char illegal)
    {
        string invalidUnixAddress = "/var/run/dst" + illegal + ".sock";
        string fineUnixAddress = "/var/run/dst.sock";
        Assert.Throws<ArgumentException>(() =>
        {
            new HAProxyMessage(
                    HAProxyProtocolVersion.V1, HAProxyCommand.PROXY, HAProxyProxiedProtocol.UNIX_STREAM,
                    invalidUnixAddress, fineUnixAddress, 0, 0);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            new HAProxyMessage(
                    HAProxyProtocolVersion.V1, HAProxyCommand.PROXY, HAProxyProxiedProtocol.UNIX_STREAM,
                    fineUnixAddress, invalidUnixAddress, 0, 0);
        });
    }

    [Theory]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData(' ')]
    public void TestIllegalV1UnixCharactersAreFineInV2(char illegal)
    {
        string suspectUnixAddress = "/var/run/dst" + illegal + ".sock";
        string fineUnixAddress = "/var/run/dst.sock";
        new HAProxyMessage(
                HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.UNIX_STREAM,
                suspectUnixAddress, fineUnixAddress, 0, 0)
                .Release();
        new HAProxyMessage(
                HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.UNIX_STREAM,
                fineUnixAddress, suspectUnixAddress, 0, 0)
                .Release();
    }

    [Fact]
    public void TestNullUnixAddress()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new HAProxyMessage(
                    HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.UNIX_STREAM,
                    null, null, 0, 0);
        });
    }

    [Fact]
    public void TestLongUnixAddress()
    {
        string longUnixAddress = new string(new char[109]).Replace("\0", "a");
        Assert.Throws<ArgumentException>(() =>
        {
            new HAProxyMessage(
                    HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.UNIX_STREAM,
                    "source", longUnixAddress, 0, 0);
        });
    }

    [Fact]
    public void TestInvalidUnixPort()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            new HAProxyMessage(
                    HAProxyProtocolVersion.V2, HAProxyCommand.PROXY, HAProxyProxiedProtocol.UNIX_STREAM,
                    "/var/run/src.sock", "/var/run/dst.sock", 80, 443);
        });
    }
}
