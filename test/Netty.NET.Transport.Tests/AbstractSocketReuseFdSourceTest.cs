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
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original peer closure and group cleanup are independently owned.
public abstract class AbstractSocketReuseFdSourceTest
{
    [Theory(Timeout = 60000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestReuseFd(bool adaptive)
    {
        DomainSocketSourceTest.RequireDomainSockets();
        var cancellation = TestContext.Current.CancellationToken;
        await using var fixture = new ShutdownFixture(adaptive, true);
        // Use a number which will typically not exceed /proc/sys/net/core/somaxconn (which is 128 on linux by default
        // often).
        const int numChannels = 100;
        var state = new SharedState();
        var serverRemaining = new Remaining(numChannels);
        var clientRemaining = new Remaining(numChannels);
        var serverDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Server.ChildOption(ChannelOption.AUTO_READ, true).ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
            channel.Pipeline.AddLast(new ReuseFdHandler(false, state, serverRemaining, serverDone))));
        fixture.Client.Option(ChannelOption.AUTO_READ, true).Handler(new ActionChannelInitializer<IChannel>(channel =>
            channel.Pipeline.AddLast(new ReuseFdHandler(true, state, clientRemaining, clientDone))));
        var server = await fixture.Server.BindAsync(DomainSocketSourceTest.NewAddress(), cancellation);
        try
        {
            for (int i = 0; i < numChannels; i++) _ = ObserveConnect(fixture.Client.ConnectAsync(server.LocalAddress, cancellationToken: cancellation));
            await clientDone.Task.WaitAsync(cancellation);
            await serverDone.Task.WaitAsync(cancellation);
            await server.CloseAsync();
            if (state.Error != null && state.Error is not IOException) ExceptionDispatchInfo.Capture(state.Error).Throw();
        }
        finally { await server.CloseAsync(); }

        async Task ObserveConnect(Task<IChannel> connect)
        {
            try { await connect; }
            catch (Exception error) { clientDone.TrySetException(error); }
        }
    }
    private sealed class SharedState { internal Exception Error; }
    private sealed class Remaining(int value) { internal int Value = value; }
    private sealed class ReuseFdHandler(bool client, SharedState global, Remaining remaining, TaskCompletionSource done) : ChannelInboundHandlerAdapter
    {
        private const string ExpectedPayload = "payload";
        private readonly StringBuilder _received = new();
        private Exception _error;
        public override void ChannelActive(ChannelHandlerContext context)
        {
            if (client) _ = ObserveWrite(context.WriteAndFlushAsync(Unpooled.CopiedBuffer(ExpectedPayload, Encoding.ASCII)));
        }
        public override void ChannelRead(ChannelHandlerContext context, object message)
        {
            if (message is ByteBuf buffer)
            {
                _received.Append(buffer.GetString(Encoding.ASCII));
                buffer.Release();
                if (_received.ToString() == ExpectedPayload)
                {
                    if (client) _ = context.CloseAsync();
                    else _ = ObserveWrite(context.WriteAndFlushAsync(Unpooled.CopiedBuffer(ExpectedPayload, Encoding.ASCII)));
                }
            }
        }
        private async Task ObserveWrite(Task write)
        {
            try { await write; }
            catch (Exception error) { done.TrySetException(error); }
        }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error)
        {
            if (Interlocked.CompareExchange(ref _error, error, null) == null)
            {
                done.TrySetException(new InvalidOperationException("exceptionCaught: " + context.Channel, error));
                _ = context.CloseAsync();
            }
            Interlocked.CompareExchange(ref global.Error, error, null);
        }
        public override void ChannelInactive(ChannelHandlerContext context)
        {
            if (Interlocked.Decrement(ref remaining.Value) == 0)
            {
                if (_received.ToString() == ExpectedPayload) done.TrySetResult();
                else done.TrySetException(new Exception("Unexpected payload:" + _received));
            }
        }
    }
}
#pragma warning restore xUnit1051
