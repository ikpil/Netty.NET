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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Socks.Tests;

// CLR: assertInstanceOf(C.class, e) is Assert.IsType<C>(e, exactMatch: false); NullPointerException from
// checkNotNull is ArgumentNullException and IllegalArgumentException is ArgumentException; the anonymous Executable is
// a lambda. java.net.IDN is the shared Netty.NET.Common.Internal.IDN; CharBuffer.wrap(s)
// is compared as the string and readCharSequence(...) through ToString(). CharsetUtil.US_ASCII is Encoding.ASCII.
// The short port widens to int for the ReadUnsignedShort comparison. encodeAsByteBuf is [Obsolete] (SocksMessage.cs),
// so its calls suppress CS0618.
public class SocksCmdResponseTest
{
    [Fact]
    public void TestConstructorParamsAreNotNull()
    {
        try
        {
            new SocksCmdResponse(null, SocksAddressType.UNKNOWN);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentNullException>(e, exactMatch: false);
        }
        try
        {
            new SocksCmdResponse(SocksCmdStatus.UNASSIGNED, null);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentNullException>(e, exactMatch: false);
        }
    }

    /**
     * Verifies content of the response when domain is not specified.
     */
    [Fact]
    public void TestEmptyDomain()
    {
        SocksCmdResponse socksCmdResponse = new SocksCmdResponse(SocksCmdStatus.SUCCESS, SocksAddressType.DOMAIN);
        Assert.Null(socksCmdResponse.Host());
        Assert.Equal(0, socksCmdResponse.Port());
        ByteBuf buffer = Unpooled.Buffer(20);
#pragma warning disable CS0618
        socksCmdResponse.EncodeAsByteBuf(buffer);
#pragma warning restore CS0618
        byte[] expected = {
                0x05, // version
                0x00, // success reply
                0x00, // reserved
                0x03, // address type domain
                0x01, // length of domain
                0x00, // domain value
                0x00, // port value
                0x00
        };
        AssertByteBufEquals(expected, buffer);
    }

    /**
     * Verifies content of the response when IPv4 address is specified.
     */
    [Fact]
    public void TestIPv4Host()
    {
        SocksCmdResponse socksCmdResponse = new SocksCmdResponse(SocksCmdStatus.SUCCESS, SocksAddressType.IPv4,
                "127.0.0.1", 80);
        Assert.Equal("127.0.0.1", socksCmdResponse.Host());
        Assert.Equal(80, socksCmdResponse.Port());
        ByteBuf buffer = Unpooled.Buffer(20);
#pragma warning disable CS0618
        socksCmdResponse.EncodeAsByteBuf(buffer);
#pragma warning restore CS0618
        byte[] expected = {
                0x05, // version
                0x00, // success reply
                0x00, // reserved
                0x01, // address type IPv4
                0x7F, // address 127.0.0.1
                0x00,
                0x00,
                0x01,
                0x00, // port
                0x50
                };
        AssertByteBufEquals(expected, buffer);
    }

    /**
     * Verifies that empty domain is allowed Response.
     */
    [Fact]
    public void TestEmptyBoundAddress()
    {
        SocksCmdResponse socksCmdResponse = new SocksCmdResponse(SocksCmdStatus.SUCCESS, SocksAddressType.DOMAIN,
                "", 80);
        Assert.Equal("", socksCmdResponse.Host());
        Assert.Equal(80, socksCmdResponse.Port());
        ByteBuf buffer = Unpooled.Buffer(20);
#pragma warning disable CS0618
        socksCmdResponse.EncodeAsByteBuf(buffer);
#pragma warning restore CS0618
        byte[] expected = {
                0x05, // version
                0x00, // success reply
                0x00, // reserved
                0x03, // address type domain
                0x00, // domain length
                0x00, // port
                0x50
        };
        AssertByteBufEquals(expected, buffer);
    }

    [Fact]
    public void TestHostNotEncodedForUnknown()
    {
        string asciiHost = "xn--e1aybc.xn--p1ai";
        short port = 10000;

        SocksCmdResponse rs = new SocksCmdResponse(SocksCmdStatus.SUCCESS, SocksAddressType.UNKNOWN, asciiHost, port);
        Assert.Equal(asciiHost, rs.Host());

        ByteBuf buffer = Unpooled.Buffer(16);
#pragma warning disable CS0618
        rs.EncodeAsByteBuf(buffer);
#pragma warning restore CS0618

        buffer.ResetReaderIndex();
        Assert.Equal(SocksProtocolVersion.SOCKS5.ByteValue(), buffer.ReadByte());
        Assert.Equal(SocksCmdStatus.SUCCESS.ByteValue(), buffer.ReadByte());
        Assert.Equal((byte) 0x00, buffer.ReadByte());
        Assert.Equal(SocksAddressType.UNKNOWN.ByteValue(), buffer.ReadByte());
        Assert.False(buffer.IsReadable());

        buffer.Release();
    }

    [Fact]
    public void TestIDNEncodeToAsciiForDomain()
    {
        string host = "тест.рф";
        string asciiHost = IDN.ToASCII(host);
        short port = 10000;

        SocksCmdResponse rs = new SocksCmdResponse(SocksCmdStatus.SUCCESS, SocksAddressType.DOMAIN, host, port);
        Assert.Equal(host, rs.Host());

        ByteBuf buffer = Unpooled.Buffer(24);
#pragma warning disable CS0618
        rs.EncodeAsByteBuf(buffer);
#pragma warning restore CS0618

        buffer.ResetReaderIndex();
        Assert.Equal(SocksProtocolVersion.SOCKS5.ByteValue(), buffer.ReadByte());
        Assert.Equal(SocksCmdStatus.SUCCESS.ByteValue(), buffer.ReadByte());
        Assert.Equal((byte) 0x00, buffer.ReadByte());
        Assert.Equal(SocksAddressType.DOMAIN.ByteValue(), buffer.ReadByte());
        Assert.Equal((byte) asciiHost.Length, buffer.ReadByte());
        Assert.Equal(asciiHost,
            buffer.ReadCharSequence(asciiHost.Length, Encoding.ASCII).ToString());
        Assert.Equal<int>(port, buffer.ReadUnsignedShort());

        buffer.Release();
    }

    /**
     * Verifies that Response cannot be constructed with invalid IP.
     */
    [Fact]
    public void TestInvalidBoundAddress()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            new SocksCmdResponse(SocksCmdStatus.SUCCESS, SocksAddressType.IPv4, "127.0.0", 1000);
        });
    }

    private static void AssertByteBufEquals(byte[] expected, ByteBuf actual)
    {
        byte[] actualBytes = new byte[actual.ReadableBytes];
        actual.ReadBytes(actualBytes);
        Assert.Equal(expected.Length, actualBytes.Length, "Generated response has incorrect length");
        Assert.Equal(expected, actualBytes, "Generated response differs from expected");
    }

    [Fact]
    public void TestValidPortRange()
    {
        try
        {
            new SocksCmdResponse(SocksCmdStatus.SUCCESS, SocksAddressType.IPv4, "127.0.0", 0);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }

        try
        {
            new SocksCmdResponse(SocksCmdStatus.SUCCESS, SocksAddressType.IPv4, "127.0.0", 65536);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }
    }
}
