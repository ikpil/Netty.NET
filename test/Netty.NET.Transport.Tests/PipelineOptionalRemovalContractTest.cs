using System;
using System.Threading;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

public class PipelineOptionalRemovalContractTest
{
    [Fact]
    public void ConcurrentOptionalRemovalReturnsTheHandlerOnceAndNullOnce()
    {
        // Source DefaultChannelPipeline.removeIfExists uses tryRemove: losing an unlink race is "not found",
        // whereas remove must throw. Use the original concurrent-mutation fixture's 2000-round rendezvous.
        for (int i = 0; i < 2000; i++)
        {
            var handler = new RemovalCounter();
            var channel = new EmbeddedChannel(handler);
            string name = channel.Pipeline.Context(handler).Name;
            IChannelHandler first = null, second = null;
            Exception firstError = null, secondError = null;
            int ready = 0;
            var thread = new Thread(() =>
            {
                Rendezvous(ref ready);
                try
                { first = channel.Pipeline.RemoveIfExists(name); }
                catch (Exception error) { firstError = error; }
            });
            thread.Start();
            Rendezvous(ref ready);
            try
            { second = channel.Pipeline.RemoveIfExists(name); }
            catch (Exception error) { secondError = error; }
            thread.Join();
            try
            {
                Assert.Null(firstError);
                Assert.Null(secondError);
                Assert.True(first == null ^ second == null);
                Assert.Same(handler, first ?? second);
                Assert.Equal(1, handler.Removals);
                Assert.Null(channel.Pipeline.Context(handler));
            }
            finally { channel.FinishAndReleaseAll(); }
        }
    }
    private static void Rendezvous(ref int ready)
    {
        Interlocked.Increment(ref ready);
        while (Volatile.Read(ref ready) < 2)
            Thread.Yield();
    }
    private sealed class RemovalCounter : ChannelInboundHandlerAdapter
    {
        internal int Removals;
        public override void HandlerRemoved(ChannelHandlerContext context) => Interlocked.Increment(ref Removals);
    }
}
