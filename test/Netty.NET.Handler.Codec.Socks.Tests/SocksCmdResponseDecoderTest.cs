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
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests;

// CLR: assertInstanceOf(C.class, o) is Assert.IsType<C>(o, exactMatch: false); SocksCmdStatus.values() and
// SocksAddressType.values() are Values() (see SocksAddressType.cs); the anonymous Executable is a lambda and
// IllegalArgumentException is ArgumentException.
public class SocksCmdResponseDecoderTest
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SocksCmdResponseDecoderTest));

    private static void TestSocksCmdResponseDecoderWithDifferentParams(
            SocksCmdStatus cmdStatus, SocksAddressType addressType, string host, int port)
    {
        logger.Debug("Testing cmdStatus: " + cmdStatus + " addressType: " + addressType);
        SocksResponse msg = new SocksCmdResponse(cmdStatus, addressType, host, port);
        SocksCmdResponseDecoder decoder = new SocksCmdResponseDecoder();
        EmbeddedChannel embedder = new EmbeddedChannel(decoder);
        SocksCommonTestUtils.WriteMessageIntoEmbedder(embedder, msg);
        if (addressType == SocksAddressType.UNKNOWN)
        {
            Assert.IsType<UnknownSocksResponse>(embedder.ReadInbound(), exactMatch: false);
        }
        else
        {
            msg = embedder.ReadInbound<SocksResponse>();
            Assert.Equal(((SocksCmdResponse) msg).CmdStatus(), cmdStatus);
            if (host != null)
            {
                Assert.Equal(((SocksCmdResponse) msg).Host(), host);
            }
            Assert.Equal(((SocksCmdResponse) msg).Port(), port);
        }
        Assert.Null(embedder.ReadInbound());
    }

    /**
     * Verifies that sent socks messages are decoded correctly.
     */
    [Fact]
    public void TestSocksCmdResponseDecoder()
    {
        foreach (SocksCmdStatus cmdStatus in SocksCmdStatus.Values())
        {
            foreach (SocksAddressType addressType in SocksAddressType.Values())
            {
                TestSocksCmdResponseDecoderWithDifferentParams(cmdStatus, addressType, null, 0);
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
            TestSocksCmdResponseDecoderWithDifferentParams(
                    SocksCmdStatus.SUCCESS, SocksAddressType.IPv4, "1", 80);
        });
    }

    /**
     * Verifies that send socks messages are decoded correctly when bound host and port are set.
     */
    [Fact]
    public void TestSocksCmdResponseDecoderIncludingHost()
    {
        foreach (SocksCmdStatus cmdStatus in SocksCmdStatus.Values())
        {
            TestSocksCmdResponseDecoderWithDifferentParams(cmdStatus, SocksAddressType.IPv4,
                    "127.0.0.1", 80);
            TestSocksCmdResponseDecoderWithDifferentParams(cmdStatus, SocksAddressType.DOMAIN,
                    "testDomain.com", 80);
            TestSocksCmdResponseDecoderWithDifferentParams(cmdStatus, SocksAddressType.IPv6,
                    "2001:db8:85a3:42:1000:8a2e:370:7334", 80);
            TestSocksCmdResponseDecoderWithDifferentParams(cmdStatus, SocksAddressType.IPv6,
                    "1111:111:11:1:0:0:0:1", 80);
        }
    }
}
