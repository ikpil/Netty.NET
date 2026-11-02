/*
 * Copyright 2013 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable CS0612, CS0618, xUnit1051, xUnit1031 // Original option aliases, caller-thread checks and independent teardown.
[Collection("Initializer extensions")]
public class BootstrapSourceTest
{
    [Fact]
    public async Task TestSetOptionsThrow()
    {
        var group = new DefaultEventLoopGroup(1);
        SourceTestChannel created = null;
        try
        {
            var future = new Bootstrap().Group(group).ChannelFactory(() => created = new SourceTestChannel())
                .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 4242).Handler(new ChannelInboundHandlerAdapter()).RegisterAsync();
            await Assert.ThrowsAsync<NotSupportedException>(() => future);
            Assert.False(created.IsActive);
        }
        finally { await group.StopAsync(); }
    }
    [Fact]
    public void TestOptionsCopied()
    {
        var bootstrap = new Bootstrap().Option(ChannelOption.AUTO_READ, true);
        var options = bootstrap.NewOptionsArray();
        bootstrap.Option(ChannelOption.AUTO_READ, false);
        Assert.Same(ChannelOption.AUTO_READ, options[0].Key);
        Assert.Equal(true, options[0].Value);
    }
    [Fact]
    public void TestAttributesCopied()
    {
        var key = AttributeKey<string>.ValueOf(Guid.NewGuid().ToString());
        const string value = "value";
        var bootstrap = new Bootstrap().Attr(key, value);
        var attributes = bootstrap.NewAttributesArray();
        bootstrap.Attr(key, "value2");
        Assert.Same(key, attributes[0].Key);
        Assert.Equal(value, attributes[0].Value);
    }
    [Fact]
    public async Task OptionsAndAttributesMustBeAvailableOnChannelInit()
    {
        var group = new DefaultEventLoopGroup(1);
        var key = AttributeKey<string>.ValueOf(Guid.NewGuid().ToString());
        try
        {
            await new Bootstrap().Group(group).Channel<LocalChannel>().Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 4242)
                .Attr(key, "value").Handler(new ActionChannelInitializer<LocalChannel>(channel =>
                {
                    Assert.True(channel.Config.TryGetOption(ChannelOption.CONNECT_TIMEOUT_MILLIS, out int value));
                    Assert.Equal(4242, value);
                    Assert.Equal("value", channel.Attr(key).Get());
                })).BindAsync(LocalAddress.ANY);
        }
        finally { await group.StopAsync(); }
    }
    [Fact(Timeout = 10000)]
    public Task TestBindDeadLock() => CrossOwners(false, TestContext.Current.CancellationToken);
    [Fact(Timeout = 10000)]
    public Task TestConnectDeadLock() => CrossOwners(true, TestContext.Current.CancellationToken);
    private static async Task CrossOwners(bool connect, CancellationToken timeout)
    {
        var groupA = new DefaultEventLoopGroup(1);
        var groupB = new DefaultEventLoopGroup(1);
        var handler = new DummyHandler();
        var bootstrapA = new Bootstrap().Group(groupA).Channel<LocalChannel>().Handler(handler);
        var bootstrapB = new Bootstrap().Group(groupB).Channel<LocalChannel>().Handler(handler);
        var operations = new ConcurrentBag<Task<IChannel>>();
        try
        {
            var submissions = new List<Task>();
            // Try to bind from each other.
            // Try to connect from each other.
            for (int i = 0; i < 1024; i++)
            {
                submissions.Add(groupA.Next().SubmitAsync(() => { operations.Add(connect ? bootstrapB.ConnectAsync(LocalAddress.ANY) : bootstrapB.BindAsync(LocalAddress.ANY)); }));
                submissions.Add(groupB.Next().SubmitAsync(() => { operations.Add(connect ? bootstrapA.ConnectAsync(LocalAddress.ANY) : bootstrapA.BindAsync(LocalAddress.ANY)); }));
            }
            foreach (var submission in submissions) await submission.WaitAsync(timeout);
        }
        finally
        {
            await groupA.StopAsync(); await groupB.StopAsync();
            // Original only waits for owner submissions. Observe detached native failures during teardown.
            foreach (var operation in operations)
            { try { await operation; } catch { } }
        }
    }
    [Fact]
    public async Task TestChannelOptionOrderPreserve()
    {
        var options = new ConcurrentQueue<ChannelOption>();
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            await new Bootstrap().Group(group).ChannelFactory(() => new OptionOrderChannel(options))
                .Handler(new ActionChannelInitializer<IChannel>(_ => initialized.TrySetResult()))
                .Option(ChannelOption.WRITE_BUFFER_LOW_WATER_MARK, 1).Option(ChannelOption.WRITE_BUFFER_HIGH_WATER_MARK, 2).RegisterAsync();
            await initialized.Task.WaitAsync(TestContext.Current.CancellationToken);
            // Check the order is the same as what we defined before.
            Assert.True(options.TryDequeue(out var low)); Assert.Same(ChannelOption.WRITE_BUFFER_LOW_WATER_MARK, low);
            Assert.True(options.TryDequeue(out var high)); Assert.Same(ChannelOption.WRITE_BUFFER_HIGH_WATER_MARK, high);
        }
        finally { await group.StopAsync(); }
    }
    [Fact]
    public void MustCallInitializerExtensions()
    {
        using var extension = new SourceInitializerExtension();
        using var registration = ChannelInitializerExtension.Register(extension);
        var group = new DefaultEventLoopGroup(1);
        try
        {
            extension.ClearThreadLocals();
            var expected = new Bootstrap().Group(group).Handler(new ChannelInboundHandlerAdapter()).Channel<LocalChannel>().RegisterAsync().GetAwaiter().GetResult();
            Assert.Same(expected, extension.LastSeenClient.Value);
            Assert.Null(extension.LastSeenChild.Value);
            Assert.Null(extension.LastSeenListener.Value);
            expected.CloseAsync().GetAwaiter().GetResult();
        }
        finally { group.StopAsync().GetAwaiter().GetResult(); }
    }
    [Sharable]
    private sealed class DummyHandler : ChannelInboundHandlerAdapter { }
    private sealed class OptionOrderChannel(ConcurrentQueue<ChannelOption> options) : LocalChannel
    {
        protected override IChannelConfig NewConfig() => new OrderConfig(this, options);
        private sealed class OrderConfig(IChannel channel, ConcurrentQueue<ChannelOption> options) : DefaultChannelConfig(channel)
        { protected override bool SetValue(ChannelOption option, object value) { options.Enqueue(option); return base.SetValue(option, value); } }
    }
}
