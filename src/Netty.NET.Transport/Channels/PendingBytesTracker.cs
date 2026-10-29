/*
 * Copyright 2017 The Netty Project
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

namespace Netty.NET.Transport.Channels;

// CLR channels always expose the native DefaultChannelPipeline. Keep the estimator snapshot and
// route byte accounting through that pipeline's current owner; a detached/closed outbound queue is a no-op.
internal sealed class PendingBytesTracker
{
    private readonly IChannel _channel;
    private readonly IMessageSizeEstimator.IHandle _estimator;
    internal PendingBytesTracker(IChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _estimator = channel.Pipeline.EstimatorHandle ?? throw new InvalidOperationException("Message size estimator returned a null handle.");
    }
    internal int Size(object message) => _estimator.Size(message);
    private ChannelOutboundBuffer Buffer
    {
        get
        {
            // We need to guard against null as channel.unsafe().outboundBuffer() may returned null
            // if the channel was already closed when constructing the PendingBytesTracker.
            // See https://github.com/netty/netty/issues/3967
            var buffer = _channel.Pipeline.Owner.OutboundBuffer;
            if (_channel.IsOpen && !buffer.IsClosed)
                return buffer;
            // Noop
            return null;
        }
    }
    internal void IncrementPendingOutboundBytes(long bytes) => Buffer?.IncrementPendingOutboundBytes(bytes);
    internal void DecrementPendingOutboundBytes(long bytes) => Buffer?.DecrementPendingOutboundBytes(bytes);
}
