/*
 * Copyright 2019 The Netty Project
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
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * {@link DecoratingHttp2ConnectionEncoder} which guards against a remote peer that will trigger a massive amount
 * of control frames but will not consume our responses to these.
 * This encoder will tear-down the connection once we reached the configured limit to reduce the risk of DDOS.
 */
// CLR: The overridden write methods are the Async members of DecoratingHttp2FrameWriter (conventions of this port,
// docs/codec-http-porting.md Shared CLR adaptations). The ChannelFutureListener lambda is an Action<Task> assigned
// in the constructor (a field initializer cannot reference the instance, CS0236), and
// promise.unvoid().addListener(listener) attaches it with Http2CodecUtil.AddListener (the
// EventExecutorTaskScheduler timing of DefaultPromise listeners) and returns the promise; there is no void
// promise. ctx.close() discards the returned Task.
internal sealed class Http2ControlFrameLimitEncoder : DecoratingHttp2ConnectionEncoder
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(Http2ControlFrameLimitEncoder));
    private int outstandingControlFrames;

    private readonly int maxOutstandingControlFrames;
    private readonly Action<Task> outstandingControlFramesListener;
    private IHttp2LifecycleManager lifecycleManager;
    private bool limitReached;

    internal Http2ControlFrameLimitEncoder(IHttp2ConnectionEncoder @delegate, int maxOutstandingControlFrames)
        : base(@delegate)
    {
        outstandingControlFramesListener = f => outstandingControlFrames--;
        this.maxOutstandingControlFrames = ObjectUtil.CheckPositive(maxOutstandingControlFrames,
                "maxOutstandingControlFrames");
    }

    public override void LifecycleManager(IHttp2LifecycleManager lifecycleManager)
    {
        this.lifecycleManager = lifecycleManager;
        base.LifecycleManager(lifecycleManager);
    }

    public override Task WriteSettingsAckAsync(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        TaskCompletionSource newPromise = HandleOutstandingControlFrames(ctx, promise);
        if (newPromise == null)
        {
            return promise.Task;
        }

        return base.WriteSettingsAckAsync(ctx, newPromise);
    }

    public override Task WritePingAsync(IChannelHandlerContext ctx, bool ack, long data, TaskCompletionSource promise)
    {
        // Only apply the limit to ping acks.
        if (ack)
        {
            TaskCompletionSource newPromise = HandleOutstandingControlFrames(ctx, promise);
            if (newPromise == null)
            {
                return promise.Task;
            }

            return base.WritePingAsync(ctx, ack, data, newPromise);
        }

        return base.WritePingAsync(ctx, ack, data, promise);
    }

    public override Task WriteRstStreamAsync(
            IChannelHandlerContext ctx, int streamId, long errorCode, TaskCompletionSource promise)
    {
        TaskCompletionSource newPromise = HandleOutstandingControlFrames(ctx, promise);
        if (newPromise == null)
        {
            return promise.Task;
        }

        return base.WriteRstStreamAsync(ctx, streamId, errorCode, newPromise);
    }

    private TaskCompletionSource HandleOutstandingControlFrames(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        if (!limitReached)
        {
            if (outstandingControlFrames == maxOutstandingControlFrames)
            {
                // Let's try to flush once as we may be able to flush some of the control frames.
                ctx.Flush();
            }

            if (outstandingControlFrames == maxOutstandingControlFrames)
            {
                limitReached = true;
                Http2Exception exception = Http2Exception.ConnectionError(Http2Error.ENHANCE_YOUR_CALM,
                        "Maximum number %d of outstanding control frames reached", maxOutstandingControlFrames);
                logger.Info("{} Maximum number {} of outstanding control frames reached, closing channel.",
                        ctx.Channel, maxOutstandingControlFrames, exception);

                // First notify the Http2LifecycleManager and then close the connection.
                lifecycleManager.OnError(ctx, true, exception);
                _ = ctx.CloseAsync();
            }

            outstandingControlFrames++;

            // We did not reach the limit yet, add the listener to decrement the number of outstanding control frames
            // once the promise was completed
            Http2CodecUtil.AddListener(ctx, promise.Task, outstandingControlFramesListener);
            return promise;
        }

        return promise;
    }
}
