/*
 * Copyright 2014 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.File;

// CLR: new RandomAccessFile(msg, "r") is a read-only FileStream and raf.length() its Length; raf.getChannel() is the
// FileStream itself, which DefaultFileRegion takes as Java's takes the FileChannel. getClass().getSimpleName() is
// GetType().Name and getMessage() Message (.NET messages are worded by the BCL). ctx.pipeline().get(SslHandler.class) is
// Pipeline.Get<SslHandler>(). ctx.write/writeAndFlush are WriteAsync/WriteAndFlushAsync, whose Tasks are discarded;
// ChannelFutureListener.CLOSE on the write future is a continuation of the write Task that closes ctx.Channel (a Task has
// no channel), as codec-base's MessageAggregator maps it. cause.printStackTrace() writes the exception to Console.Error.
// The public channelRead0 override widens the protected SimpleChannelInboundHandler.channelRead0; a C# override cannot
// change the accessibility of the virtual method, so it stays protected.
public class FileServerHandler : SimpleChannelInboundHandler<string>
{

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        _ = ctx.WriteAndFlushAsync("HELLO: Type the path of the file to retrieve.\n");
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, string msg)
    {
        FileStream raf = null;
        long length = -1;

        try
        {
            raf = new FileStream(msg, FileMode.Open, FileAccess.Read, FileShare.Read);
            length = raf.Length;
        }
        catch (Exception e)
        {
            _ = ctx.WriteAndFlushAsync("ERR: " + e.GetType().Name + ": " + e.Message + '\n');
            return;
        }
        finally
        {
            if (length < 0 && raf != null)
            {
                raf.Close();
            }
        }

        _ = ctx.WriteAsync("OK: " + raf.Length + '\n');

        if (ctx.Pipeline.Get<SslHandler>() == null)
        {
            // SSL not enabled - can use zero-copy file transfer.
            _ = ctx.WriteAsync(new DefaultFileRegion(raf, 0, length));
        }
        else
        {
            // SSL enabled - cannot use zero-copy file transfer.
            _ = ctx.WriteAsync(new ChunkedFile(raf));
        }

        _ = ctx.WriteAndFlushAsync("\n");
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);

        if (ctx.Channel.IsActive)
        {
            _ = ctx.WriteAndFlushAsync("ERR: " +
                    cause.GetType().Name + ": " +
                    cause.Message + '\n').ContinueWith(_ => ctx.Channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
