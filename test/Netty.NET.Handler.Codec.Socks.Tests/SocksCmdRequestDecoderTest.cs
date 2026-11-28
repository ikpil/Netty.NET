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

using System.Net;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests;

// CLR: SocketUtils.addressByName of a literal is IPAddress.Parse and InetAddress.getAddress() is
// GetAddressBytes(); the UnknownHostException clause has no counterpart.
public class SocksCmdRequestDecoderTest
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SocksCmdRequestDecoderTest));

    private static void TestSocksCmdRequestDecoderWithDifferentParams(SocksCmdType cmdType,
                                                                      SocksAddressType addressType,
                                                                      string host,
                                                                      int port)
    {
        logger.Debug("Testing cmdType: " + cmdType + " addressType: " + addressType + " host: " + host +
                " port: " + port);
        SocksCmdRequest msg = new SocksCmdRequest(cmdType, addressType, host, port);
        SocksCmdRequestDecoder decoder = new SocksCmdRequestDecoder();
        EmbeddedChannel embedder = new EmbeddedChannel(decoder);
        SocksCommonTestUtils.WriteMessageIntoEmbedder(embedder, msg);

        if (msg.AddressType() == SocksAddressType.UNKNOWN)
        {
            Assert.IsType<UnknownSocksRequest>(embedder.ReadInbound(), exactMatch: false);
        }
        else
        {
            msg = embedder.ReadInbound<SocksCmdRequest>();
            Assert.Same(msg.CmdType(), cmdType);
            Assert.Same(msg.AddressType(), addressType);
            Assert.Equal(msg.Host(), host);
            Assert.Equal(msg.Port(), port);
        }

        Assert.Null(embedder.ReadInbound());
    }

    [Fact]
    public void TestCmdRequestDecoderIPv4()
    {
        string[] hosts = { "127.0.0.1", };
        int[] ports = { 1, 32769, 65535 };

        foreach (SocksCmdType cmdType in SocksCmdType.Values())
        {
            foreach (string host in hosts)
            {
                foreach (int port in ports)
                {
                    TestSocksCmdRequestDecoderWithDifferentParams(cmdType, SocksAddressType.IPv4, host, port);
                }
            }
        }
    }

    [Fact]
    public void TestCmdRequestDecoderIPv6()
    {
        string[] hosts = { SocksCommonUtils.Ipv6toStr(IPAddress.Parse("::1").GetAddressBytes()) };
        int[] ports = { 1, 32769, 65535 };

        foreach (SocksCmdType cmdType in SocksCmdType.Values())
        {
            foreach (string host in hosts)
            {
                foreach (int port in ports)
                {
                    TestSocksCmdRequestDecoderWithDifferentParams(cmdType, SocksAddressType.IPv6, host, port);
                }
            }
        }
    }

    [Fact]
    public void TestCmdRequestDecoderDomain()
    {
        string[] hosts = { "google.com" ,
                          "مثال.إختبار",
                          "παράδειγμα.δοκιμή",
                          "مثال.آزمایشی",
                          "пример.испытание",
                          "בײַשפּיל.טעסט",
                          "例子.测试",
                          "例子.測試",
                          "उदाहरण.परीक्षा",
                          "例え.テスト",
                          "실례.테스트",
                          "உதாரணம்.பரிட்சை" };
        int[] ports = { 1, 32769, 65535 };

        foreach (SocksCmdType cmdType in SocksCmdType.Values())
        {
            foreach (string host in hosts)
            {
                foreach (int port in ports)
                {
                    TestSocksCmdRequestDecoderWithDifferentParams(cmdType, SocksAddressType.DOMAIN, host, port);
                }
            }
        }
    }

    [Fact]
    public void TestCmdRequestDecoderUnknown()
    {
        string host = "google.com";
        int port = 80;

        foreach (SocksCmdType cmdType in SocksCmdType.Values())
        {
            TestSocksCmdRequestDecoderWithDifferentParams(cmdType, SocksAddressType.UNKNOWN, host, port);
        }
    }
}
