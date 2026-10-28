using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051
public class LocalTransportContractTest
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LocalEchoRespectsCrossLoopOwnershipAndBufferLifetime(int threads)
    {
        var group = new DefaultEventLoopGroup(threads);
        IChannel server = null, client = null;
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            server = await new ServerBootstrap().Group(group).Channel<LocalServerChannel>()
                .ChildHandler(new ActionChannelInitializer<IChannel>(channel => channel.Pipeline.AddLast(new Echo())))
                .BindAsync(LocalAddress.ANY);
            client = await new Bootstrap().Group(group).Channel<LocalChannel>().Handler(new Receive(received))
                .ConnectAsync(server.LocalAddress).WaitAsync(TimeSpan.FromSeconds(10));
            var buffer = Unpooled.Buffer(3).WriteBytes(new byte[] { 1, 2, 3 });
            await client.WriteAndFlushAsync(buffer).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(new byte[] { 1, 2, 3 }, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            await client.EventLoop.SubmitAsync(() => { });
        }
        finally
        {
            if (client != null)
                await client.CloseAsync();
            if (server != null)
                await server.CloseAsync();
            await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    private sealed class Echo : SimpleChannelInboundHandler<ByteBuf>
    { protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message) => _ = context.WriteAndFlushAsync(message.Retain()); }
    private sealed class Receive(TaskCompletionSource<byte[]> received) : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, ByteBuf message) => received.TrySetResult(message.ReadableMemory.ToArray());
        public override void ExceptionCaught(ChannelHandlerContext context, Exception error) => received.TrySetException(error);
    }
}
