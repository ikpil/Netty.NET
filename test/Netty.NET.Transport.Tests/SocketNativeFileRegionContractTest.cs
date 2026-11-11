using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

// Supplemental CLR contracts for the original positional FileChannel/sendfile path. These are not
// additional mappings of original Java methods: the inherited source fixture remains separate.
#pragma warning disable xUnit1051 // Native operation and reference-counted file have independent lifetimes.
#pragma warning disable xUnit1069 // The Cancellation property supplies the current test deadline to every asynchronous operation.
public class SocketNativeFileRegionContractTest
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void RequireNative()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) Assert.Skip("Native file-handle submission is implemented on Windows and Linux.");
    }

    [Theory(Timeout = 15000)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeRangesPreserveFilePositionProgressAndSocketMode(bool lazy, bool nonblocking)
    {
        RequireNative();
        await using var connection = await Connected.Open(nonblocking);
        string path = Path.GetTempFileName();
        var bytes = new byte[2 * 1024 * 1024 + 8192]; Random.Shared.NextBytes(bytes);
        DefaultFileRegion region = null;
        try
        {
            await File.WriteAllBytesAsync(path, bytes, Cancellation);
            using var file = Open(path);
            file.Position = 17;
            const int start = 4093, length = 2 * 1024 * 1024 + 31;
            region = lazy ? new DefaultFileRegion(new FileInfo(path), start, length) : new DefaultFileRegion(file, start, length);
            region.Retain(); // Inspect the file after the outbound queue releases its own reference.
            connection.Channel.Region = region;
            byte[] actual = await SendAndRead(connection, region, length);
            Assert.Equal(bytes.AsSpan(start, length).ToArray(), actual);
            Assert.Equal(length, region.Transferred());
            Assert.Equal(1, region.ReferenceCount);
            Assert.Equal(17, file.Position);
            long next = start;
            foreach (var call in connection.Channel.Calls)
            {
                Assert.Equal(next, call.Offset);
                Assert.Equal(next - start, call.Transferred);
                Assert.True(call.OnOwner);
                Assert.InRange(call.Count, 1, SocketStreamChannel.NativeFileChunkSize);
                next += call.Count;
            }
            Assert.Equal(start + length, next);
            Assert.Equal(!nonblocking, connection.Channel.NativeSocket.Blocking);
        }
        finally { region?.Release(); File.Delete(path); }
    }

    [Fact(Timeout = 10000)]
    public async Task NativeSendUsesTheOpenedHandleAfterPathReplacement()
    {
        RequireNative();
        await using var connection = await Connected.Open();
        string path = Path.GetTempFileName(), renamed = path + ".moved";
        try
        {
            byte[] bytes = { 1, 3, 5, 7, 9 };
            await File.WriteAllBytesAsync(path, bytes, Cancellation);
            using var file = Open(path);
            File.Move(path, renamed);
            await File.WriteAllBytesAsync(path, new byte[] { 2, 4, 6, 8, 0 }, Cancellation);
            var region = new DefaultFileRegion(file, 1, 3);
            Assert.Equal(new byte[] { 3, 5, 7 }, await SendAndRead(connection, region, 3));
            Assert.Single(connection.Channel.Calls);
            Assert.True(file.SafeFileHandle.IsClosed);
            Assert.Equal(0, region.ReferenceCount);
        }
        finally { File.Delete(path); File.Delete(renamed); }
    }

    [Fact(Timeout = 10000)]
    public async Task NativeSendResumesAfterAnEarlierPositionalTransfer()
    {
        RequireNative();
        await using var connection = await Connected.Open();
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0, 1, 2, 3, 4, 5 }, Cancellation);
            var region = new DefaultFileRegion(Open(path), 1, 5);
            // TransferTo is positional: two bytes were already accepted before this socket write.
            Assert.Equal(2, region.TransferTo(Stream.Null, 3));
            Assert.Equal(new byte[] { 3, 4, 5 }, await SendAndRead(connection, region, 3));
            Assert.Equal(3, Assert.Single(connection.Channel.Calls).Offset);
            Assert.Equal(5, region.Transferred());
        }
        finally { File.Delete(path); }
    }

    [Fact(Timeout = 10000)]
    public async Task NativeSendKeepsOffsetsBeyondInt32()
    {
        RequireNative();
        await using var connection = await Connected.Open();
        string path = Path.GetTempFileName();
        const long offset = (long)int.MaxValue + 1024;
        try
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                // On Windows explicitly mark the temporary file sparse before extending it.
                if (OperatingSystem.IsWindows()) MarkSparse(file);
                file.Position = offset; file.Write(new byte[] { 0x42, 0x43, 0x44 });
            }
            var region = new DefaultFileRegion(new FileInfo(path), offset, 3);
            Assert.Equal(new byte[] { 0x42, 0x43, 0x44 }, await SendAndRead(connection, region, 3));
            Assert.Equal(offset, Assert.Single(connection.Channel.Calls).Offset);
        }
        finally { File.Delete(path); }
    }

    [Fact(Timeout = 10000)]
    public async Task EmptyRegionNeverOpensOrSubmitsTheWholeFile()
    {
        await using var connection = await Connected.Open();
        var region = new DefaultFileRegion(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), 123, 0);
        await connection.Channel.WriteAndFlushAsync(region, Cancellation);
        Assert.Empty(connection.Channel.Calls);
        Assert.False(region.IsOpen());
        Assert.Equal(0, region.ReferenceCount);
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousFilesAndVirtualProducersKeepTheirStreamContract(bool derived)
    {
        await using var connection = await Connected.Open();
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 }, Cancellation);
            using var file = derived ? Open(path) : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            DefaultFileRegion region = derived ? new CustomRegion(file) : new DefaultFileRegion(file, 0, 3);
            Assert.Equal(new byte[] { 1, 2, 3 }, await SendAndRead(connection, region, 3));
            Assert.Empty(connection.Channel.Calls);
            if (derived) Assert.Equal(1, ((CustomRegion)region).Invocations);
            Assert.True(file.SafeFileHandle.IsClosed);
        }
        finally { File.Delete(path); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapabilityFallbackResumesWithoutDuplicatingAcceptedBytes(bool afterPartial)
    {
        RequireNative();
        await using var connection = await Connected.Open();
        string path = Path.GetTempFileName();
        try
        {
            byte[] bytes = new byte[1024]; Random.Shared.NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes, Cancellation);
            int calls = 0;
            connection.Channel.Send = async (file, offset, count) =>
            {
                if (afterPartial && calls++ == 0) return await connection.Channel.SendNative(file, offset, 23);
                return new FileRegionSendResult(0, SocketError.OperationNotSupported);
            };
            var region = new DefaultFileRegion(Open(path), 0, bytes.Length);
            Assert.Equal(bytes, await SendAndRead(connection, region, bytes.Length));
            Assert.Equal(bytes.Length, region.Transferred());
            Assert.Equal(afterPartial ? 2 : 1, connection.Channel.Calls.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact(Timeout = 10000)]
    public async Task PartialNativeErrorRecordsAcceptedBytesBeforeFailingAndReleasing()
    {
        RequireNative();
        await using var connection = await Connected.Open();
        string path = Path.GetTempFileName();
        try
        {
            byte[] bytes = new byte[1024]; Random.Shared.NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes, Cancellation);
            using var file = Open(path);
            connection.Channel.Send = async (input, offset, count) =>
            {
                var sent = await connection.Channel.SendNative(input, offset, 23);
                Assert.Equal(23, sent.BytesTransferred);
                return sent with { Error = SocketError.ConnectionReset };
            };
            var region = new DefaultFileRegion(file, 0, bytes.Length);
            var error = await Assert.ThrowsAsync<IOException>(() => connection.Channel.WriteAndFlushAsync(region, Cancellation));
            Assert.Equal(SocketError.ConnectionReset, Assert.IsType<SocketException>(error.InnerException).SocketErrorCode);
            Assert.Equal(23, region.Transferred());
            Assert.Equal(0, region.ReferenceCount);
            Assert.True(file.SafeFileHandle.IsClosed);
            var actual = new byte[23]; await connection.Peer.GetStream().ReadExactlyAsync(actual, Cancellation);
            Assert.Equal(bytes.AsSpan(0, 23).ToArray(), actual);
            Assert.Single(connection.Channel.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact(Timeout = 10000)]
    public async Task TruncationBetweenRangeAdmissionAndKernelSendFailsAsIOException()
    {
        RequireNative();
        await using var connection = await Connected.Open();
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[1024], Cancellation);
            using var file = Open(path);
            connection.Channel.Send = (input, offset, count) =>
            {
                using (var truncate = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) truncate.SetLength(0);
                return connection.Channel.SendNative(input, offset, count);
            };
            var region = new DefaultFileRegion(file, 0, 1024);
            await Assert.ThrowsAsync<IOException>(() => connection.Channel.WriteAndFlushAsync(region, Cancellation));
            Assert.Equal(0, region.Transferred());
            Assert.Equal(0, region.ReferenceCount);
            Assert.True(file.SafeFileHandle.IsClosed);
            Assert.Single(connection.Channel.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact(Timeout = 15000)]
    public async Task ClosingAPendingNativeSendReleasesTheFileOnlyAfterNativeCompletion()
    {
        RequireNative();
        await using var connection = await Connected.Open(true, true);
        string path = Path.GetTempFileName();
        try
        {
            using (var output = File.OpenWrite(path)) output.SetLength(16 * 1024 * 1024);
            using var file = Open(path);
            var region = new DefaultFileRegion(file, 0, file.Length);
            var nativeDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Channel.Send = async (input, offset, count) =>
            {
                try { return await connection.Channel.SendNative(input, offset, count); }
                finally { nativeDone.TrySetResult(); }
            };
            Task write = connection.Channel.WriteAndFlushAsync(region, Cancellation);
            await connection.Peer.GetStream().ReadExactlyAsync(new byte[1], Cancellation);
            Assert.False(write.IsCompleted);
            Assert.False(file.SafeFileHandle.IsClosed);
            await connection.Channel.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2), Cancellation);
            await Assert.ThrowsAsync<IOException>(() => write);
            await nativeDone.Task.WaitAsync(Cancellation);
            // Closing may complete the queue's Task before the native borrow's continuation has exited.
            while (!file.SafeFileHandle.IsClosed) await Task.Delay(1, Cancellation);
            Assert.Equal(0, region.ReferenceCount);
        }
        finally { File.Delete(path); }
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, options: FileOptions.Asynchronous);
    private static async Task<byte[]> SendAndRead(Connected connection, DefaultFileRegion region, int length)
    {
        Task write = connection.Channel.WriteAndFlushAsync(region, Cancellation);
        var actual = new byte[length];
        await connection.Peer.GetStream().ReadExactlyAsync(actual, Cancellation);
        await write.WaitAsync(Cancellation);
        return actual;
    }
    private sealed class CustomRegion(FileStream file) : DefaultFileRegion(file, 0, 3)
    {
        internal int Invocations;
        public override long TransferTo(Stream target, long position) { Invocations++; return base.TransferTo(target, position); }
    }
    private static void MarkSparse(FileStream file)
    {
        if (!DeviceIoControl(file.SafeFileHandle, 0x900c4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint control,
        IntPtr input, uint inputLength, IntPtr output, uint outputLength, out uint returned, IntPtr overlapped);

    private sealed class ProbeChannel : TcpSocketChannel
    {
        internal readonly ConcurrentQueue<(long Offset, int Count, long Transferred, bool OnOwner)> Calls = new();
        internal DefaultFileRegion Region;
        internal Func<FileStream, long, int, Task<FileRegionSendResult>> Send;
        internal Socket NativeSocket => Socket;
        internal ProbeChannel(Socket socket) : base(socket) { }
        internal override Task<FileRegionSendResult> SendFilePacketAsync(FileStream file, long offset, int count)
        {
            Calls.Enqueue((offset, count, Region?.Transferred() ?? 0, EventLoop.InEventLoop()));
            return Send != null ? Send(file, offset, count) : SendNative(file, offset, count);
        }
        internal Task<FileRegionSendResult> SendNative(FileStream file, long offset, int count) => base.SendFilePacketAsync(file, offset, count);
    }
    private sealed class Connected : IAsyncDisposable
    {
        private readonly ShutdownFixture _fixture = new(false);
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        internal ProbeChannel Channel;
        internal TcpClient Peer;
        internal static async Task<Connected> Open(bool nonblocking = false, bool limitedReceiveBuffer = false)
        {
            var result = new Connected();
            if (limitedReceiveBuffer) result._listener.Server.ReceiveBufferSize = 1024;
            result._listener.Start();
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                { Blocking = !nonblocking, SendBufferSize = 4096 };
            result.Channel = new ProbeChannel(socket);
            try
            {
                await new Bootstrap().Group(result._fixture.Client.Config.Group).ChannelFactory(() => result.Channel)
                    .Handler(new ChannelInboundHandlerAdapter()).ConnectAsync(result._listener.LocalEndpoint, cancellationToken: Cancellation);
                result.Peer = await result._listener.AcceptTcpClientAsync(Cancellation);
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            if (Channel != null) await Channel.CloseAsync();
            Peer?.Dispose(); _listener.Stop(); await _fixture.DisposeAsync();
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
