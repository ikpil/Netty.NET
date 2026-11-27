using System;
using System.IO;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental tests of DefaultFileRegion transfers to a non-blocking socket, the CLR counterpart of
// FileChannel.transferTo used by DefaultFileRegion.transferTo(WritableByteChannel, long).
#pragma warning disable xUnit1031 // Drive the manual event loop on its owning thread.
public class SocketNativeFileRegionContractTest
{
    [Fact]
    public void RangesPreserveTheSuppliedFilePositionAndReportFileProgress()
    {
        string path = Path.GetTempFileName();
        var bytes = new byte[300_000];
        Random.Shared.NextBytes(bytes);
        try
        {
            File.WriteAllBytes(path, bytes);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            file.Position = 3;
            using var fixture = new StreamFixture<NioSocketChannel>(socket => new NioSocketChannel(socket));
            var region = new DefaultFileRegion(file, 7, bytes.Length - 7);
            Task write = fixture.Channel.WriteAndFlushAsync(region);
            // The positional transfer never moves the supplied FileStream; the region closes it once released.
            if (!write.IsCompleted)
            {
                Assert.Equal(3, file.Position);
            }
            var received = new byte[bytes.Length - 7];
            int offset = 0;
            while (offset < received.Length)
            {
                fixture.Loop.RunNow();
                int chunk = Math.Min(65536, received.Length - offset);
                fixture.Receive(chunk).CopyTo(received, offset);
                offset += chunk;
            }
            fixture.PumpUntil(() => write.IsCompleted);
            Assert.True(write.IsCompletedSuccessfully);
            Assert.Equal(bytes.AsSpan(7).ToArray(), received);
            Assert.Equal(bytes.Length - 7, region.Transferred());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheOpenedHandleIsUsedAfterPathReplacement()
    {
        string path = Path.GetTempFileName();
        string moved = path + ".moved";
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var region = new DefaultFileRegion(new FileInfo(path), 0, 3);
            region.Open();
            File.Move(path, moved);
            File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
            using var fixture = new StreamFixture<NioSocketChannel>(socket => new NioSocketChannel(socket));
            Task write = fixture.Channel.WriteAndFlushAsync(region);
            fixture.PumpUntil(() => write.IsCompleted);
            Assert.True(write.IsCompletedSuccessfully);
            Assert.Equal(new byte[] { 1, 2, 3 }, fixture.Receive(3));
        }
        finally
        {
            File.Delete(path);
            File.Delete(moved);
        }
    }

    [Fact]
    public void AnEmptyRegionNeverOpensTheFile()
    {
        var region = new DefaultFileRegion(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), 0, 0);
        using var fixture = new StreamFixture<NioSocketChannel>(socket => new NioSocketChannel(socket));
        Task write = fixture.Channel.WriteAndFlushAsync(region);
        fixture.PumpUntil(() => write.IsCompleted);
        Assert.True(write.IsCompletedSuccessfully);
        Assert.False(region.IsOpen());
    }

    [Fact]
    public void ACountBeyondTheFileFailsAsIOException()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2 });
            using var fixture = new StreamFixture<NioSocketChannel>(socket => new NioSocketChannel(socket));
            var region = new DefaultFileRegion(new FileInfo(path), 0, 5);
            Task write = fixture.Channel.WriteAndFlushAsync(region);
            fixture.PumpUntil(() => write.IsCompleted);
            Assert.ThrowsAny<IOException>(() => write.GetAwaiter().GetResult());
            Assert.Equal(0, region.RefCnt);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
