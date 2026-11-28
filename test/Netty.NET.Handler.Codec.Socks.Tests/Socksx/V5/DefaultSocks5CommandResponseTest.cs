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
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Socksx.V5;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V5;

// CLR: assertInstanceOf(C.class, e) is Assert.IsType<C>(e, exactMatch: false); NullPointerException from
// checkNotNull is ArgumentNullException and IllegalArgumentException is ArgumentException; the anonymous Executable is
// a lambda.
public class DefaultSocks5CommandResponseTest
{
    [Fact]
    public void TestConstructorParamsAreNotNull()
    {
        try
        {
            new DefaultSocks5CommandResponse(null, Socks5AddressType.DOMAIN);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentNullException>(e, exactMatch: false);
        }

        try
        {
            new DefaultSocks5CommandResponse(Socks5CommandStatus.FAILURE, null);
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
        ISocks5CommandResponse socks5CmdResponse = new DefaultSocks5CommandResponse(
                Socks5CommandStatus.SUCCESS, Socks5AddressType.DOMAIN);
        Assert.Null(socks5CmdResponse.BndAddr());
        Assert.Equal(0, socks5CmdResponse.BndPort());

        ByteBuf buffer = Socks5CommonTestUtils.EncodeServer(socks5CmdResponse);
        byte[] expected = {
                0x05, // version
                0x00, // success reply
                0x00, // reserved
                0x03, // address type domain
                0x00, // length of domain
                0x00, // port value
                0x00
        };
        AssertByteBufEquals(expected, buffer);
        buffer.Release();
    }

    /**
     * Verifies content of the response when IPv4 address is specified.
     */
    [Fact]
    public void TestIPv4Host()
    {
        ISocks5CommandResponse socks5CmdResponse = new DefaultSocks5CommandResponse(
                Socks5CommandStatus.SUCCESS, Socks5AddressType.IPv4, "127.0.0.1", 80);
        Assert.Equal("127.0.0.1", socks5CmdResponse.BndAddr());
        Assert.Equal(80, socks5CmdResponse.BndPort());

        ByteBuf buffer = Socks5CommonTestUtils.EncodeServer(socks5CmdResponse);
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
        buffer.Release();
    }

    /**
     * Verifies that empty domain is allowed Response.
     */
    [Fact]
    public void TestEmptyBoundAddress()
    {
        ISocks5CommandResponse socks5CmdResponse = new DefaultSocks5CommandResponse(
                Socks5CommandStatus.SUCCESS, Socks5AddressType.DOMAIN, "", 80);
        Assert.Equal("", socks5CmdResponse.BndAddr());
        Assert.Equal(80, socks5CmdResponse.BndPort());

        ByteBuf buffer = Socks5CommonTestUtils.EncodeServer(socks5CmdResponse);
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
            new DefaultSocks5CommandResponse(
                    Socks5CommandStatus.SUCCESS, Socks5AddressType.IPv4, "127.0.0", 1000);
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
            new DefaultSocks5CommandResponse(Socks5CommandStatus.SUCCESS, Socks5AddressType.IPv4, "127.0.0", 0);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }

        try
        {
            new DefaultSocks5CommandResponse(Socks5CommandStatus.SUCCESS, Socks5AddressType.IPv4, "127.0.0", 65536);
        }
        catch (Exception e)
        {
            Assert.IsType<ArgumentException>(e, exactMatch: false);
        }
    }
}
