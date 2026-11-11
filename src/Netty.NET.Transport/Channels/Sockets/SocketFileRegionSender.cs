using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels.Sockets;

internal readonly record struct FileRegionSendResult(int BytesTransferred, SocketError Error);

// CLR file-handle/range submission. SendPackets uses TransmitPackets on Windows and sendfile on Linux.
// The caller retains the region until completion; disposing these descriptors never owns the supplied file.
internal static class SocketFileRegionSender
{
    internal static Task<FileRegionSendResult> SendAsync(Socket socket, FileStream file, long offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count); // SendPackets interprets zero as the entire file.
        var completion = new TaskCompletionSource<FileRegionSendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var args = new SocketAsyncEventArgs();
        args.Completed += (_, value) => Complete(value);
        try
        {
            args.SendPacketsElements = new[] { new SendPacketsElement(file, offset, count) };
            if (!socket.SendPacketsAsync(args)) Complete(args);
        }
        catch (Exception error) { args.Dispose(); completion.TrySetException(error); }
        return completion.Task;

        void Complete(SocketAsyncEventArgs value)
        {
            var result = new FileRegionSendResult(value.BytesTransferred, value.SocketError);
            value.Dispose();
            completion.TrySetResult(result);
        }
    }
}
