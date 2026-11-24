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

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Handler.Address;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.Address;

// CLR: SocketAddress is EndPoint and the anonymous SocketAddress, handler and DynamicAddressConnectHandler classes are
// named nested classes; the captured localThrows/exception are constructor arguments. promise.setSuccess()/setFailure()
// are the returned completed or faulted Task, syncUninterruptibly() is an await of the connect Task (which rethrows a
// failure) and future.cause() is the exception that awaiting the completed Task rethrows. pipeline().get(Class) is
// Pipeline.Get<T>(). The channel operations receive TestContext.Current.CancellationToken (xUnit1051).
public class DynamicAddressConnectHandlerTest
{
    private static readonly EndPoint LOCAL = new TestSocketAddress();
    private static readonly EndPoint LOCAL_NEW = new TestSocketAddress();
    private static readonly EndPoint REMOTE = new TestSocketAddress();
    private static readonly EndPoint REMOTE_NEW = new TestSocketAddress();

    private sealed class TestSocketAddress : EndPoint
    {
    }

    [Fact]
    public async Task TestReplaceAddresses()
    {

        EmbeddedChannel channel = new EmbeddedChannel(new AssertingConnectHandler(), new ReplacingConnectHandler());
        await channel.ConnectAsync(REMOTE, LOCAL, TestContext.Current.CancellationToken);
        Assert.Null(channel.Pipeline.Get<DynamicAddressConnectHandler>());
        Assert.False(channel.Finish());
    }

    private sealed class AssertingConnectHandler : ChannelOutboundHandlerAdapter
    {
        public override Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress,
                                          EndPoint localAddress, CancellationToken cancellationToken)
        {
            try
            {
                Assert.Same(REMOTE_NEW, remoteAddress);
                Assert.Same(LOCAL_NEW, localAddress);
                return Task.CompletedTask;
            }
            catch (Exception cause)
            {
                return Task.FromException(cause);
            }
        }
    }

    private sealed class ReplacingConnectHandler : DynamicAddressConnectHandler
    {
        protected override EndPoint LocalAddress(EndPoint remoteAddress, EndPoint localAddress)
        {
            Assert.Same(REMOTE, remoteAddress);
            Assert.Same(LOCAL, localAddress);
            return LOCAL_NEW;
        }

        protected override EndPoint RemoteAddress(EndPoint remoteAddress, EndPoint localAddress)
        {
            Assert.Same(REMOTE, remoteAddress);
            Assert.Same(LOCAL, localAddress);
            return REMOTE_NEW;
        }
    }

    [Fact]
    public async Task TestLocalAddressThrows()
    {
        await TestThrows0(true);
    }

    [Fact]
    public async Task TestRemoteAddressThrows()
    {
        await TestThrows0(false);
    }

    // CLR: IllegalStateException is InvalidOperationException.
    private static async Task TestThrows0(bool localThrows)
    {
        InvalidOperationException exception = new InvalidOperationException();

        EmbeddedChannel channel = new EmbeddedChannel(new ThrowingConnectHandler(localThrows, exception));
        Assert.Same(exception, await Record.ExceptionAsync(
                () => channel.ConnectAsync(REMOTE, LOCAL, TestContext.Current.CancellationToken)));
        Assert.NotNull(channel.Pipeline.Get<DynamicAddressConnectHandler>());
        Assert.False(channel.Finish());
    }

    private sealed class ThrowingConnectHandler : DynamicAddressConnectHandler
    {
        private readonly bool localThrows;
        private readonly InvalidOperationException exception;

        internal ThrowingConnectHandler(bool localThrows, InvalidOperationException exception)
        {
            this.localThrows = localThrows;
            this.exception = exception;
        }

        protected override EndPoint LocalAddress(
                EndPoint remoteAddress, EndPoint localAddress)
        {
            if (localThrows)
            {
                throw exception;
            }
            return base.LocalAddress(remoteAddress, localAddress);
        }

        protected override EndPoint RemoteAddress(EndPoint remoteAddress, EndPoint localAddress)
        {
            if (!localThrows)
            {
                throw exception;
            }
            return base.RemoteAddress(remoteAddress, localAddress);
        }
    }
}
