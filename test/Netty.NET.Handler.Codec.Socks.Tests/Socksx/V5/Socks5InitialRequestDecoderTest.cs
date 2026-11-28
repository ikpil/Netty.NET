/*
 * Copyright 2019 The Netty Project
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

using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V5;

public class Socks5InitialRequestDecoderTest
{
    [Fact]
    public void TestUnpackingCausesDecodeFail()
    {
        EmbeddedChannel e = new EmbeddedChannel(new Socks5InitialRequestDecoder());
        Assert.False(e.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 5, 2, 0 })));
        Assert.True(e.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 1 })));
        object o = e.ReadInbound();

        Assert.IsType<DefaultSocks5InitialRequest>(o, exactMatch: false);
        DefaultSocks5InitialRequest req = (DefaultSocks5InitialRequest) o;
        Assert.Same(req.DecoderResult(), DecoderResult.SUCCESS);
        Assert.False(e.Finish());
    }
}
