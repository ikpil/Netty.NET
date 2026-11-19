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

using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V5;

public class Socks5PasswordAuthRequestDecoderTest
{

    [Fact]
    public void TestAuthRequestDecoder()
    {
        string username = "testUsername";
        string password = "testPassword";
        ISocks5PasswordAuthRequest msg = new DefaultSocks5PasswordAuthRequest(username, password);
        EmbeddedChannel embedder = new EmbeddedChannel(new Socks5PasswordAuthRequestDecoder());
        Socks5CommonTestUtils.WriteFromClientToServer(embedder, msg);
        msg = embedder.ReadInbound<ISocks5PasswordAuthRequest>();
        Assert.Equal(username, msg.Username());
        Assert.Equal(password, msg.Password());
        Assert.Null(embedder.ReadInbound());
    }
}
