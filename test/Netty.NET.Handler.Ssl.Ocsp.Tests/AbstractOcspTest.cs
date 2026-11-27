/*
 * Copyright 2025 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Ssl.Ocsp.Tests;

/**
 * We can't use {@link IoTransport#DEFAULT} because that breaks leak detection. This base class creates a per-test
 * transport as a substitute.
 */
// CLR: @BeforeEach/@AfterEach are the constructor and IAsyncLifetime.DisposeAsync (xUnit creates a test class instance
// per test); shutdownGracefully(100, 1000, MILLISECONDS).syncUninterruptibly() awaits ShutdownGracefullyAsync with the
// same TimeSpans. The NioSocketChannel::new and NioDatagramChannel::new method references are lambdas.
public abstract class AbstractOcspTest : IAsyncLifetime
{
    private IEventLoopGroup group;

    protected AbstractOcspTest()
    {
        Start();
    }

    private void Start()
    {
        group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Stop();
    }

    private Task Stop()
    {
        return group.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(1000));
    }

    internal IoTransport CreateDefaultTransport()
    {
        return IoTransport.Create(group.Next(), () => new NioSocketChannel(), () => new NioDatagramChannel());
    }
}
