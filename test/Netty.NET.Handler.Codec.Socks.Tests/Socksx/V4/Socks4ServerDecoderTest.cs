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

// CLR: Arrays.asList(...) is an array; the private helper keeps its name test (Test).
public class Socks4ServerDecoderTest
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(Socks4ServerDecoderTest));

    private static void Test(string userId, Socks4CommandType type, string dstAddr, int dstPort)
    {
        logger.Debug(
                "Testing type: " + type + " dstAddr: " + dstAddr + " dstPort: " + dstPort +
                " userId: " + userId);

        ISocks4CommandRequest msg = new DefaultSocks4CommandRequest(type, dstAddr, dstPort, userId);
        EmbeddedChannel embedder = new EmbeddedChannel(new Socks4ServerDecoder());
        Socks4CommonTestUtils.WriteMessageIntoEmbedder(embedder, msg);
        msg = embedder.ReadInbound<ISocks4CommandRequest>();
        Assert.Same(msg.Type(), type);
        Assert.Equal(msg.DstAddr(), dstAddr);
        Assert.Equal(msg.DstPort(), dstPort);
        Assert.Equal(msg.UserId(), userId);
        Assert.Null(embedder.ReadInbound());
    }

    [Fact]
    public void TestCmdRequestDecoder()
    {
        string[] hosts = { "127.0.0.1", };
        string[] userIds = { "test", };
        int[] ports = { 1, 32769, 65535 };

        foreach (Socks4CommandType cmdType in new[] { Socks4CommandType.BIND,
                                                       Socks4CommandType.CONNECT })
        {
            foreach (string userId in userIds)
            {
                foreach (string host in hosts)
                {
                    foreach (int port in ports)
                    {
                        Test(userId, cmdType, host, port);
                    }
                }
            }
        }
    }
}
