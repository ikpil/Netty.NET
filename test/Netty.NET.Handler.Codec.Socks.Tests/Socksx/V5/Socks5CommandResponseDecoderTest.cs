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
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V5;

// CLR: Arrays.asList(...) is an array, the anonymous Executable is a lambda and IllegalArgumentException is
// ArgumentException. The private helper keeps its name test (Test).
public class Socks5CommandResponseDecoderTest
{

    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(Socks5CommandResponseDecoderTest));

    private static readonly Socks5CommandStatus[] STATUSES = {
            Socks5CommandStatus.ADDRESS_UNSUPPORTED,
            Socks5CommandStatus.COMMAND_UNSUPPORTED,
            Socks5CommandStatus.CONNECTION_REFUSED,
            Socks5CommandStatus.FAILURE,
            Socks5CommandStatus.FORBIDDEN,
            Socks5CommandStatus.HOST_UNREACHABLE,
            Socks5CommandStatus.NETWORK_UNREACHABLE,
            Socks5CommandStatus.SUCCESS,
            Socks5CommandStatus.TTL_EXPIRED
    };

    private static void Test(
            Socks5CommandStatus status, Socks5AddressType bndAddrType, string bndAddr, int bndPort)
    {
        logger.Debug("Testing status: " + status + " bndAddrType: " + bndAddrType);
        ISocks5CommandResponse msg =
                new DefaultSocks5CommandResponse(status, bndAddrType, bndAddr, bndPort);
        EmbeddedChannel embedder = new EmbeddedChannel(new Socks5CommandResponseDecoder());
        Socks5CommonTestUtils.WriteFromServerToClient(embedder, msg);
        msg = embedder.ReadInbound<ISocks5CommandResponse>();
        Assert.Equal(msg.Status(), status);

        if (bndAddr != null)
        {
            Assert.Equal(msg.BndAddr(), bndAddr);
        }

        Assert.Equal(msg.BndPort(), bndPort);
        Assert.Null(embedder.ReadInbound());
    }

    /**
     * Verifies that sent socks messages are decoded correctly.
     */
    [Fact]
    public void TestSocksCmdResponseDecoder()
    {
        foreach (Socks5CommandStatus cmdStatus in STATUSES)
        {
            foreach (Socks5AddressType addressType in new[] { Socks5AddressType.DOMAIN,
                                                               Socks5AddressType.IPv4,
                                                               Socks5AddressType.IPv6 })
            {
                Test(cmdStatus, addressType, null, 0);
            }
        }
    }

    /**
     * Verifies that invalid bound host will fail with IllegalArgumentException during encoding.
     */
    [Fact]
    public void TestInvalidAddress()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Test(Socks5CommandStatus.SUCCESS, Socks5AddressType.IPv4, "1", 80);
        });
    }

    /**
     * Verifies that send socks messages are decoded correctly when bound host and port are set.
     */
    [Fact]
    public void TestSocksCmdResponseDecoderIncludingHost()
    {
        foreach (Socks5CommandStatus cmdStatus in STATUSES)
        {
            Test(cmdStatus, Socks5AddressType.IPv4,
                 "127.0.0.1", 80);
            Test(cmdStatus, Socks5AddressType.DOMAIN,
                 "testDomain.com", 80);
            Test(cmdStatus, Socks5AddressType.IPv6,
                 "2001:db8:85a3:42:1000:8a2e:370:7334", 80);
            Test(cmdStatus, Socks5AddressType.IPv6,
                 "1111:111:11:1::1", 80);
        }
    }
}
