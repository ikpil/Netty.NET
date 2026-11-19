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
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V5;

// CLR: the private helper keeps its name test (Test).
public class Socks5PasswordAuthResponseDecoderTest
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(
            typeof(Socks5PasswordAuthResponseDecoderTest));

    private static void Test(Socks5PasswordAuthStatus status)
    {
        logger.Debug("Testing Socks5PasswordAuthResponseDecoder with status: " + status);
        ISocks5PasswordAuthResponse msg = new DefaultSocks5PasswordAuthResponse(status);
        EmbeddedChannel embedder = new EmbeddedChannel(new Socks5PasswordAuthResponseDecoder());
        Socks5CommonTestUtils.WriteFromServerToClient(embedder, msg);
        msg = embedder.ReadInbound<ISocks5PasswordAuthResponse>();
        Assert.Same(msg.Status(), status);
        Assert.Null(embedder.ReadInbound());
    }

    [Fact]
    public void TestSocksCmdResponseDecoder()
    {
        Test(Socks5PasswordAuthStatus.SUCCESS);
        Test(Socks5PasswordAuthStatus.FAILURE);
    }
}
