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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V5;

// CLR: java.net.IDN is the shared Netty.NET.Common.Internal.IDN; SocketUtils.addressByName of a literal is IPAddress.Parse and InetAddress.getAddress() is
// GetAddressBytes() (docs/common-clr-design.md, as in the codec-dns tests); the UnknownHostException clause has no
// counterpart and Arrays.asList(...) is an array. The private helper keeps its name test (Test).
public class Socks5CommandRequestDecoderTest
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(Socks5CommandRequestDecoderTest));

    private static void Test(
            Socks5CommandType type, Socks5AddressType dstAddrType, string dstAddr, int dstPort)
    {
        logger.Debug(
                "Testing type: " + type + " dstAddrType: " + dstAddrType +
                " dstAddr: " + dstAddr + " dstPort: " + dstPort);

        ISocks5CommandRequest msg =
                new DefaultSocks5CommandRequest(type, dstAddrType, dstAddr, dstPort);
        EmbeddedChannel embedder = new EmbeddedChannel(new Socks5CommandRequestDecoder());
        Socks5CommonTestUtils.WriteFromClientToServer(embedder, msg);
        msg = embedder.ReadInbound<ISocks5CommandRequest>();
        Assert.Same(msg.Type(), type);
        Assert.Same(msg.DstAddrType(), dstAddrType);
        Assert.Equal(msg.DstAddr(), IDN.ToASCII(dstAddr));
        Assert.Equal(msg.DstPort(), dstPort);
        Assert.Null(embedder.ReadInbound());
    }

    [Fact]
    public void TestCmdRequestDecoderIPv4()
    {
        string[] hosts = { "127.0.0.1", };
        int[] ports = { 1, 32769, 65535 };
        foreach (Socks5CommandType cmdType in new[] { Socks5CommandType.BIND,
                                                      Socks5CommandType.CONNECT,
                                                      Socks5CommandType.UDP_ASSOCIATE })
        {
            foreach (string host in hosts)
            {
                foreach (int port in ports)
                {
                    Test(cmdType, Socks5AddressType.IPv4, host, port);
                }
            }
        }
    }

    [Fact]
    public void TestCmdRequestDecoderIPv6()
    {
        string[] hosts = {
                NetUtil.BytesToIpAddress(IPAddress.Parse("::1").GetAddressBytes()) };
        int[] ports = { 1, 32769, 65535 };
        foreach (Socks5CommandType cmdType in new[] { Socks5CommandType.BIND,
                                                      Socks5CommandType.CONNECT,
                                                      Socks5CommandType.UDP_ASSOCIATE })
        {
            foreach (string host in hosts)
            {
                foreach (int port in ports)
                {
                    Test(cmdType, Socks5AddressType.IPv6, host, port);
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
        foreach (Socks5CommandType cmdType in new[] { Socks5CommandType.BIND,
                                                      Socks5CommandType.CONNECT,
                                                      Socks5CommandType.UDP_ASSOCIATE })
        {
            foreach (string host in hosts)
            {
                foreach (int port in ports)
                {
                    Test(cmdType, Socks5AddressType.DOMAIN, host, port);
                }
            }
        }
    }
}
