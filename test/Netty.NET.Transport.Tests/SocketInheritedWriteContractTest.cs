using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental tests: subclasses inherit the Java doWrite(ChannelOutboundBuffer) loop, and overridden
// doWriteBytes/doWriteFileRegion members are the ones that loop calls.
#pragma warning disable xUnit1031 // Drive the manual event loop on its owning thread.
public class SocketInheritedWriteContractTest
{
    [Fact]
    public void NativeSubclassesInheritTheJavaWriteLoop()
    {
        using var fixture = new StreamFixture<Inherited>(socket => new Inherited(socket));
        Assert.True(fixture.Channel.HasDoWriteLoop);
        Task a = fixture.Channel.WriteAsync(Unpooled.WrappedBuffer(new byte[] { 1, 2 }));
        Task b = fixture.Channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 3 }));
        fixture.PumpUntil(() => a.IsCompleted && b.IsCompleted);
        Assert.True(a.IsCompletedSuccessfully && b.IsCompletedSuccessfully);
        Assert.Equal(new byte[] { 1, 2, 3 }, fixture.Receive(3));
    }

    [Fact]
    public void OverriddenFileProducersAreCalledByTheInheritedLoop()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 7, 8, 9 });
            using var fixture = new StreamFixture<Overriding>(socket => new Overriding(socket));
            Task write = fixture.Channel.WriteAndFlushAsync(new DefaultFileRegion(new FileInfo(path), 0, 3));
            fixture.PumpUntil(() => write.IsCompleted);
            Assert.True(write.IsCompletedSuccessfully);
            Assert.True(fixture.Channel.FileCalls >= 1);
            Assert.Equal(new byte[] { 7, 8, 9 }, fixture.Receive(3));
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal sealed class Inherited(Socket socket) : NioSocketChannel(socket);

    internal sealed class Overriding(Socket socket) : NioSocketChannel(socket)
    {
        internal int FileCalls;

        protected override long DoWriteFileRegion(IFileRegion region)
        {
            FileCalls++;
            return base.DoWriteFileRegion(region);
        }
    }
}
