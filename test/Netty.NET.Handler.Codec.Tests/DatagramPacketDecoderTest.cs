/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Generic;
using System.Net;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.String;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: CharsetUtil.UTF_8 is Encoding.UTF8. SocketUtils.socketAddress(host, port) builds an InetSocketAddress; SocketUtils
// is replaced by native socket operations and a numeric host is an IPAddress in an IPEndPoint (docs/common-clr-design.md,
// "Native socket operations and network interface access").
public class DatagramPacketDecoderTest : IDisposable
{
    private EmbeddedChannel channel;

    // CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the
    // constructor and the @AfterEach tearDown() from Dispose() (https://xunit.net/docs/shared-context).
    public DatagramPacketDecoderTest()
    {
        SetUp();
    }

    public void Dispose()
    {
        TearDown();
    }

    private void SetUp()
    {
        channel = new EmbeddedChannel(
                new DatagramPacketDecoder(
                        new StringDecoder(Encoding.UTF8)));
    }

    private void TearDown()
    {
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestDecode()
    {
        IPEndPoint recipient = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 10000);
        IPEndPoint sender = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 20000);
        ByteBuf content = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("netty"));
        Assert.True(channel.WriteInbound(new DatagramPacket(content, recipient, sender)));
        Assert.Equal("netty", channel.ReadInbound());
    }

    [Fact]
    public void TestIsNotSharable()
    {
        TestIsSharable0(false);
    }

    [Fact]
    public void TestIsSharable()
    {
        TestIsSharable0(true);
    }

    // CLR: xUnit1024 forbids overloading the TestIsSharable test name.
    private static void TestIsSharable0(bool sharable)
    {
        MessageToMessageDecoder<ByteBuf> wrapped = new TestMessageToMessageDecoder(sharable);
        DatagramPacketDecoder decoder = new DatagramPacketDecoder(wrapped);
        Assert.Equal(wrapped.IsSharable, decoder.IsSharable);
    }

    private sealed class TestMessageToMessageDecoder : MessageToMessageDecoder<ByteBuf>
    {
        private readonly bool sharable;

        internal TestMessageToMessageDecoder(bool sharable)
        {
            this.sharable = sharable;
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf msg, IList<object> @out)
        {
            // NOOP
        }

        public override bool IsSharable => sharable;
    }
}
