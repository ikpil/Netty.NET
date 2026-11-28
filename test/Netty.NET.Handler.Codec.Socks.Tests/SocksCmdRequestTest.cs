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

namespace Netty.NET.Handler.Codec.Socks.Tests;

// CLR: NullPointerException from checkNotNull is ArgumentNullException and IllegalArgumentException is
// ArgumentException. java.net.IDN is the shared Netty.NET.Common.Internal.IDN; CharBuffer.wrap(s) is a
// CharSequence view whose equals compares the characters, so the strings are compared directly and
// readCharSequence(...) is compared through ToString(). The short port widens to int for the ReadUnsignedShortBE
// comparison, as assertEquals(int, int) does. encodeAsByteBuf is [Obsolete] (SocksMessage.cs), so its calls
// suppress CS0618.
public class SocksCmdRequestTest
{
    [Fact]
    public void TestConstructorParamsAreNotNull()
    {
        try
        {
            new SocksCmdRequest(null, SocksAddressType.UNKNOWN, "", 1);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentNullException>(e, exactMatch: false);
        }

        try
        {
            new SocksCmdRequest(SocksCmdType.UNKNOWN, null, "", 1);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentNullException>(e, exactMatch: false);
        }

        try
        {
            new SocksCmdRequest(SocksCmdType.UNKNOWN, SocksAddressType.UNKNOWN, null, 1);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentNullException>(e, exactMatch: false);
        }
    }

    [Fact]
    public void TestIPv4CorrectAddress()
    {
        try
        {
            new SocksCmdRequest(SocksCmdType.BIND, SocksAddressType.IPv4, "54.54.1111.253", 1);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }
    }

    [Fact]
    public void TestIPv6CorrectAddress()
    {
        try
        {
            new SocksCmdRequest(SocksCmdType.BIND, SocksAddressType.IPv6, "xxx:xxx:xxx", 1);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }
    }

    [Fact]
    public void TestIDNNotExceeds255CharsLimit()
    {
        try
        {
            new SocksCmdRequest(SocksCmdType.BIND, SocksAddressType.DOMAIN,
                    "παράδειγμα.δοκιμήπαράδειγμα.δοκιμήπαράδειγμα.δοκιμήπαράδειγμα.δοκιμή" +
                    "παράδειγμα.δοκιμήπαράδειγμα.δοκιμήπαράδειγμα.δοκιμήπαράδειγμα.δοκιμή" +
                    "παράδειγμα.δοκιμήπαράδειγμα.δοκιμήπαράδειγμα.δοκιμήπαράδειγμα.δοκιμή" +
                    "παράδειγμα.δοκιμήπαράδειγμα.δοκιμήπαράδειγμα.δοκιμήπαράδειγμα.δοκιμή", 1);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }
    }

    [Fact]
    public void TestHostNotEncodedForUnknown()
    {
        string asciiHost = "xn--e1aybc.xn--p1ai";
        short port = 10000;

        SocksCmdRequest rq = new SocksCmdRequest(SocksCmdType.BIND, SocksAddressType.UNKNOWN, asciiHost, port);
        Assert.Equal(asciiHost, rq.Host());

        ByteBuf buffer = Unpooled.Buffer(16);
#pragma warning disable CS0618
        rq.EncodeAsByteBuf(buffer);
#pragma warning restore CS0618

        buffer.ResetReaderIndex();
        Assert.Equal(SocksProtocolVersion.SOCKS5.ByteValue(), buffer.ReadByte());
        Assert.Equal(SocksCmdType.BIND.ByteValue(), buffer.ReadByte());
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

        SocksCmdRequest rq = new SocksCmdRequest(SocksCmdType.BIND, SocksAddressType.DOMAIN, host, port);
        Assert.Equal(host, rq.Host());

        ByteBuf buffer = Unpooled.Buffer(24);
#pragma warning disable CS0618
        rq.EncodeAsByteBuf(buffer);
#pragma warning restore CS0618

        buffer.ResetReaderIndex();
        Assert.Equal(SocksProtocolVersion.SOCKS5.ByteValue(), buffer.ReadByte());
        Assert.Equal(SocksCmdType.BIND.ByteValue(), buffer.ReadByte());
        Assert.Equal((byte) 0x00, buffer.ReadByte());
        Assert.Equal(SocksAddressType.DOMAIN.ByteValue(), buffer.ReadByte());
        Assert.Equal((byte) asciiHost.Length, buffer.ReadByte());
        Assert.Equal(asciiHost,
            buffer.ReadCharSequence(asciiHost.Length, Encoding.ASCII).ToString());
        Assert.Equal<int>(port, ByteBufUtil.ReadUnsignedShortBE(buffer));

        buffer.Release();
    }

    // CLR: ByteBuf.order(ByteOrder) is @deprecated ("use the Little Endian accessors, e.g. getShortLE, getIntLE
    // instead of creating a buffer with swapped endianness") and the Buffer port has no swapped-order view, only
    // explicit BE/LE accessors. The buffer is created without order(LITTLE_ENDIAN); the test still checks the
    // encoded DOMAIN request and that ByteBufUtil.writeShortBE wrote the port big-endian.
    [Fact]
    public void TestEndianessForPort()
    {
        string host = "localhost";
        string asciiHost = host;
        short port = 10000;

        SocksCmdRequest rq = new SocksCmdRequest(SocksCmdType.BIND, SocksAddressType.DOMAIN, host, port);
        Assert.Equal(host, rq.Host());

        ByteBuf buffer = Unpooled.Buffer(24);
#pragma warning disable CS0618
        rq.EncodeAsByteBuf(buffer);
#pragma warning restore CS0618

        Assert.Equal(SocksProtocolVersion.SOCKS5.ByteValue(), buffer.ReadByte());
        Assert.Equal(SocksCmdType.BIND.ByteValue(), buffer.ReadByte());
        Assert.Equal((byte) 0x00, buffer.ReadByte());
        Assert.Equal(SocksAddressType.DOMAIN.ByteValue(), buffer.ReadByte());
        Assert.Equal((byte) asciiHost.Length, buffer.ReadByte());
        Assert.Equal(asciiHost,
            buffer.ReadCharSequence(asciiHost.Length, Encoding.ASCII).ToString());
        Assert.Equal<int>(port, ByteBufUtil.ReadUnsignedShortBE(buffer));

        buffer.Release();
    }

    [Fact]
    public void TestValidPortRange()
    {
        try
        {
            new SocksCmdRequest(SocksCmdType.BIND, SocksAddressType.DOMAIN,
                    "παράδειγμα.δοκιμήπαράδει", 0);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }

        try
        {
            new SocksCmdRequest(SocksCmdType.BIND, SocksAddressType.DOMAIN,
                    "παράδειγμα.δοκιμήπαράδει", 65536);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }
    }
}
