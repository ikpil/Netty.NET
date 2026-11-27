/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */
using System;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Http2.HelloWorld.Client;

/**
 * Reads the first {@link Http2Settings} object and notifies a {@link io.netty.channel.ChannelPromise}
 */
// CLR: ChannelPromise is TaskCompletionSource (docs/transport-porting.md); promise.setSuccess() is SetResult().
// awaitSettings(long, TimeUnit) blocks with promise.awaitUninterruptibly(timeout, unit); it is AwaitSettingsAsync(TimeSpan)
// (a (long, TimeUnit) pair is a TimeSpan, docs/common-clr-design.md), which awaits Task.WhenAny of the promise's Task and a
// delay of the timeout, as the ported HAProxyMessageDecoderTest maps awaitUninterruptibly. isSuccess() is
// IsCompletedSuccessfully and cause() the InnerException of the faulted Task (a canceled Task is a
// TaskCanceledException, as Java's cause() is a CancellationException). IllegalStateException is
// InvalidOperationException, and new RuntimeException(cause) is an Exception whose message is cause.toString()
// (Common's ThrowableUtil.ThrowableToString), as codec-http2's Http2MultiplexHandlerTest maps it.
public class Http2SettingsHandler : SimpleChannelInboundHandler<Http2Settings>
{
    private readonly TaskCompletionSource promise;

    /**
     * Create new instance
     *
     * @param promise Promise object used to notify when first settings are received
     */
    public Http2SettingsHandler(TaskCompletionSource promise)
    {
        this.promise = promise;
    }

    /**
     * Wait for this handler to be added after the upgrade to HTTP/2, and for initial preface
     * handshake to complete.
     *
     * @param timeout Time to wait
     * @param unit {@link java.util.concurrent.TimeUnit} for {@code timeout}
     * @throws Exception if timeout or other failure occurs
     */
    public async Task AwaitSettingsAsync(TimeSpan timeout)
    {
        if (await Task.WhenAny(promise.Task, Task.Delay(timeout)) != promise.Task)
        {
            throw new InvalidOperationException("Timed out waiting for settings");
        }
        if (!promise.Task.IsCompletedSuccessfully)
        {
            Exception cause = promise.Task.IsCanceled ? new TaskCanceledException(promise.Task)
                    : promise.Task.Exception.InnerException;
            throw new Exception(ThrowableUtil.ThrowableToString(cause), cause);
        }
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, Http2Settings msg)
    {
        promise.SetResult();

        // Only care about the first settings message
        ctx.Pipeline.Remove(this);
    }
}
