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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

internal sealed class SourceTestChannel : AbstractChannel
{
    internal SourceTestChannel() : base(null, new ChannelMetadata(false)) => SetActive(true);
    public override EndPoint LocalAddress => null;
    public override EndPoint RemoteAddress => null;
    protected override IChannelConfig NewConfig() => new TestConfig(this);
    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken token) => Task.CompletedTask;
    protected override Task DoBindAsync(EndPoint address, CancellationToken token)
    {
        // NOOP
        return Task.CompletedTask;
    }
    protected override void DoClose() { }
    protected override void DoBeginRead()
    {
        // NOOP
    }
    protected override Task DoWriteAsync(object message)
    {
        // NOOP
        return Task.CompletedTask;
    }
    private sealed class TestConfig(IChannel channel) : DefaultChannelConfig(channel)
    {
        protected override bool SetValue(ChannelOption option, object value) => throw new NotSupportedException("Unsupported channel option: " + option);
    }
}
