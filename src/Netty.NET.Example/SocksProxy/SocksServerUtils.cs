/*
 * Copyright 2012 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.SocksProxy;

// CLR: ChannelFutureListener.CLOSE is a continuation of the write Task on the channel's event loop
// (EventExecutorTaskScheduler, the timing of DefaultPromise.notifyListeners()) that closes the channel whatever the
// outcome. Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer.
public sealed class SocksServerUtils
{

    /**
     * Closes the specified channel after all queued write requests are flushed.
     */
    public static void CloseOnFlush(IChannel ch)
    {
        if (ch.IsActive)
        {
            _ = ch.WriteAndFlushAsync(Unpooled.EmptyBuffer).ContinueWith(_ => ch.CloseAsync(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => ch.EventLoop));
        }
    }

    private SocksServerUtils()
    {
    }
}
