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
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests;

public class SocksAuthResponseDecoderTest
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SocksAuthResponseDecoderTest));

    private static void TestSocksAuthResponseDecoderWithDifferentParams(SocksAuthStatus authStatus)
    {
        logger.Debug("Testing SocksAuthResponseDecoder with authStatus: " + authStatus);
        SocksAuthResponse msg = new SocksAuthResponse(authStatus);
        SocksAuthResponseDecoder decoder = new SocksAuthResponseDecoder();
        EmbeddedChannel embedder = new EmbeddedChannel(decoder);
        SocksCommonTestUtils.WriteMessageIntoEmbedder(embedder, msg);
        msg = embedder.ReadInbound<SocksAuthResponse>();
        Assert.Same(msg.AuthStatus(), authStatus);
        Assert.Null(embedder.ReadInbound());
    }

    [Fact]
    public void TestSocksCmdResponseDecoder()
    {
        foreach (SocksAuthStatus authStatus in SocksAuthStatus.Values())
        {
            TestSocksAuthResponseDecoderWithDifferentParams(authStatus);
        }
    }
}
