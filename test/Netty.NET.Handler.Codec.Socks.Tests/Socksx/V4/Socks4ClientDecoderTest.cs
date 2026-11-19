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

using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Socksx.V4;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V4;

// CLR: the SLF4J Logger is Netty.NET.Common's IInternalLogger from InternalLoggerFactory (SLF4J is one of the backends
// of Netty's InternalLoggerFactory, which Common ports); the private helper keeps its name test (Test).
public class Socks4ClientDecoderTest
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(Socks4ClientDecoderTest));

    private static void Test(Socks4CommandStatus cmdStatus, string dstAddr, int dstPort)
    {
        logger.Debug("Testing cmdStatus: " + cmdStatus);
        ISocks4CommandResponse msg = new DefaultSocks4CommandResponse(cmdStatus, dstAddr, dstPort);
        EmbeddedChannel embedder = new EmbeddedChannel(new Socks4ClientDecoder());
        Socks4CommonTestUtils.WriteMessageIntoEmbedder(embedder, msg);

        msg = embedder.ReadInbound<ISocks4CommandResponse>();
        Assert.Equal(msg.Status(), cmdStatus);
        if (dstAddr != null)
        {
            Assert.Equal(msg.DstAddr(), dstAddr);
        }
        Assert.Equal(msg.DstPort(), dstPort);
        Assert.Null(embedder.ReadInbound());
    }

    /**
     * Verifies that sent socks messages are decoded correctly.
     */
    [Fact]
    public void TestSocksCmdResponseDecoder()
    {
        Test(Socks4CommandStatus.IDENTD_AUTH_FAILURE, null, 0);
        Test(Socks4CommandStatus.IDENTD_UNREACHABLE, null, 0);
        Test(Socks4CommandStatus.REJECTED_OR_FAILED, null, 0);
        Test(Socks4CommandStatus.SUCCESS, null, 0);
    }
}
