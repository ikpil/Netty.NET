/*
 * Copyright 2015 The Netty Project
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
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Pool;

namespace Netty.NET.Transport.Tests;

public class AbstractChannelPoolMapTest
{
    [Fact]
    public async Task TestMap()
    {
        var group = new DefaultEventLoopGroup(2);
        try
        {
            var bootstrap = new Bootstrap().RemoteAddress(new LocalAddress(ChannelPoolTestUtils.GetLocalAddrId()))
                .Group(group).Channel<LocalChannel>();
            await using var map = new PoolMap<SimpleChannelPool>(loop => new SimpleChannelPool(bootstrap.Clone(loop), new TestHandler()));
            var loop = group.Next();
            Assert.False(map.Any());
            Assert.Equal(0, map.Size);
            var pool = map.Get(loop);
            Assert.Equal(1, map.Size);
            Assert.True(map.Any());
            Assert.Same(pool, map.Get(loop));
            Assert.True(map.Remove(loop));
            Assert.False(map.Remove(loop));
            Assert.False(map.Any());
            Assert.Equal(0, map.Size);
            var error = await Assert.ThrowsAsync<SocketException>(() => pool.AcquireAsync(TestContext.Current.CancellationToken));
            Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
        }
        finally { await group.StopAsync(); }
    }

    [Fact]
    public async Task TestRemoveClosesChannelPool()
    {
        var group = new DefaultEventLoopGroup(2);
        try
        {
            var bootstrap = new Bootstrap().RemoteAddress(new LocalAddress(ChannelPoolTestUtils.GetLocalAddrId()))
                .Group(group).Channel<LocalChannel>();
            await using var map = new PoolMap<TestPool>(loop => new TestPool(bootstrap.Clone(loop)));
            var loop = group.Next();
            var pool = map.Get(loop);
            Assert.True(map.Remove(loop));
            // the pool should be closed eventually after remove
            await pool.Closed.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.True(pool.Closed.Task.IsCompleted);
        }
        finally { await group.StopAsync(); }
    }

    [Fact]
    public async Task TestCloseClosesPoolsImmediately()
    {
        var group = new DefaultEventLoopGroup(2);
        try
        {
            var bootstrap = new Bootstrap().RemoteAddress(new LocalAddress(ChannelPoolTestUtils.GetLocalAddrId()))
                .Group(group).Channel<LocalChannel>();
            await using var map = new PoolMap<TestPool>(loop => new TestPool(bootstrap.Clone(loop)));
            var pool = map.Get(group.Next());
            Assert.False(pool.Closed.Task.IsCompleted);
            // the pool should be closed immediately after remove
            map.Close();
            Assert.True(pool.Closed.Task.IsCompleted);
        }
        finally { await group.StopAsync(); }
    }

    private sealed class TestHandler : AbstractChannelPoolHandler
    {
        public override void ChannelCreated(IChannel channel)
        {
            // NOOP
        }
    }
    private sealed class TestPool(Bootstrap bootstrap) : SimpleChannelPool(bootstrap, new TestHandler())
    {
        internal readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task CloseAsync()
        {
            try
            { await base.CloseAsync(); Closed.TrySetResult(); }
            catch (Exception error) { Closed.TrySetException(error); throw; }
        }
    }
    private sealed class PoolMap<TPool>(Func<IEventLoop, TPool> create) : AbstractChannelPoolMap<IEventLoop, TPool>
        where TPool : class, IChannelPool
    {
        protected override TPool NewPool(IEventLoop key) => create(key);
    }
}
