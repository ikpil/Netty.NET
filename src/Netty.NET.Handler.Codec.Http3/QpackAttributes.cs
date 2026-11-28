/*
 * Copyright 2021 The Netty Project
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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Quic;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private final class is internal sealed and its package-private members are internal. QuicChannel and
// QuicStreamChannel are codec-quic's IQuicChannel and IQuicStreamChannel. Promise<QuicStreamChannel> is
// TaskCompletionSource<IQuicStreamChannel>, created without RunContinuationsAsynchronously (as
// Http3CodecUtils.NewPromise); setSuccess is SetResult and tryFailure TrySetException. The
// GenericFutureListener<Future<? super QuicStreamChannel>> listeners are Action<Task<IQuicStreamChannel>>, added as
// continuations on Common's EventExecutorTaskScheduler over the channel's event loop, so they run on the event loop
// as listeners of channel.eventLoop().newPromise() are notified (at once when the promise completes there, otherwise
// through the executor). requireNonNull is ArgumentNullException.ThrowIfNull; assert is Debug.Assert; Throwable is
// Exception.
internal sealed class QpackAttributes
{
    private readonly IQuicChannel channel;
    private readonly bool dynamicTableDisabled;
    private readonly TaskCompletionSource<IQuicStreamChannel> encoderStreamPromise;
    private readonly TaskCompletionSource<IQuicStreamChannel> decoderStreamPromise;

    private IQuicStreamChannel encoderStream;
    private IQuicStreamChannel decoderStream;

    internal QpackAttributes(IQuicChannel channel, bool disableDynamicTable)
    {
        this.channel = channel;
        dynamicTableDisabled = disableDynamicTable;
        encoderStreamPromise = dynamicTableDisabled ? null : new TaskCompletionSource<IQuicStreamChannel>();
        decoderStreamPromise = dynamicTableDisabled ? null : new TaskCompletionSource<IQuicStreamChannel>();
    }

    internal bool DynamicTableDisabled()
    {
        return dynamicTableDisabled;
    }

    internal bool DecoderStreamAvailable()
    {
        return !dynamicTableDisabled && decoderStream != null;
    }

    internal bool EncoderStreamAvailable()
    {
        return !dynamicTableDisabled && encoderStream != null;
    }

    internal void WhenEncoderStreamAvailable(Action<Task<IQuicStreamChannel>> listener)
    {
        Debug.Assert(!dynamicTableDisabled);
        Debug.Assert(encoderStreamPromise != null);
        AddListener(encoderStreamPromise.Task, listener);
    }

    internal void WhenDecoderStreamAvailable(Action<Task<IQuicStreamChannel>> listener)
    {
        Debug.Assert(!dynamicTableDisabled);
        Debug.Assert(decoderStreamPromise != null);
        AddListener(decoderStreamPromise.Task, listener);
    }

    internal IQuicStreamChannel DecoderStream()
    {
        Debug.Assert(DecoderStreamAvailable());
        return decoderStream;
    }

    internal IQuicStreamChannel EncoderStream()
    {
        Debug.Assert(EncoderStreamAvailable());
        return encoderStream;
    }

    internal void DecoderStream(IQuicStreamChannel decoderStream)
    {
        Debug.Assert(channel.EventLoop.InEventLoop());
        Debug.Assert(!dynamicTableDisabled);
        Debug.Assert(decoderStreamPromise != null);
        Debug.Assert(this.decoderStream == null);
        ArgumentNullException.ThrowIfNull(decoderStream);

        this.decoderStream = decoderStream;
        decoderStreamPromise.SetResult(decoderStream);
    }

    internal void EncoderStream(IQuicStreamChannel encoderStream)
    {
        Debug.Assert(channel.EventLoop.InEventLoop());
        Debug.Assert(!dynamicTableDisabled);
        Debug.Assert(encoderStreamPromise != null);
        Debug.Assert(this.encoderStream == null);
        ArgumentNullException.ThrowIfNull(encoderStream);

        this.encoderStream = encoderStream;
        encoderStreamPromise.SetResult(encoderStream);
    }

    internal void EncoderStreamInactive(Exception cause)
    {
        Debug.Assert(channel.EventLoop.InEventLoop());
        Debug.Assert(!dynamicTableDisabled);
        Debug.Assert(encoderStreamPromise != null);
        encoderStreamPromise.TrySetException(cause);
    }

    internal void DecoderStreamInactive(Exception cause)
    {
        Debug.Assert(channel.EventLoop.InEventLoop());
        Debug.Assert(!dynamicTableDisabled);
        Debug.Assert(decoderStreamPromise != null);
        decoderStreamPromise.TrySetException(cause);
    }

    // CLR: promise.addListener(listener) of a promise of channel.eventLoop() (see the type comment).
    private void AddListener(Task<IQuicStreamChannel> future, Action<Task<IQuicStreamChannel>> listener)
    {
        IQuicChannel ch = channel;
        _ = future.ContinueWith(listener, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ch.EventLoop));
    }
}
