/*
 * Copyright 2015 The Netty Project
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

using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Decorator around another {@link Http2FrameWriter} instance.
 */
// CLR: delegate is a C# keyword, so the field and parameter are @delegate. The Task-returning methods take the Async
// suffix, ChannelPromise is TaskCompletionSource and ChannelFuture is Task (docs/codec-http-porting.md, Shared CLR
// adaptations); the overridable Java methods are virtual. Closeable.close() is Close(); IDisposable.Dispose() comes
// from IHttp2FrameWriter.
public class DecoratingHttp2FrameWriter : IHttp2FrameWriter
{
    private readonly IHttp2FrameWriter @delegate;

    public DecoratingHttp2FrameWriter(IHttp2FrameWriter @delegate)
    {
        this.@delegate = CheckNotNull(@delegate, "delegate");
    }

    public virtual Task WriteDataAsync(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding,
                                   bool endStream, TaskCompletionSource promise)
    {
        return @delegate.WriteDataAsync(ctx, streamId, data, padding, endStream, promise);
    }

    public virtual Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding,
                                      bool endStream, TaskCompletionSource promise)
    {
        return @delegate.WriteHeadersAsync(ctx, streamId, headers, padding, endStream, promise);
    }

    public virtual Task WriteHeadersAsync(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
                                      int streamDependency, short weight, bool exclusive, int padding,
                                      bool endStream, TaskCompletionSource promise)
    {
        return @delegate
                .WriteHeadersAsync(ctx, streamId, headers, streamDependency, weight, exclusive, padding, endStream,
                        promise);
    }

    public virtual Task WritePriorityAsync(IChannelHandlerContext ctx, int streamId, int streamDependency, short weight,
                                       bool exclusive, TaskCompletionSource promise)
    {
        return @delegate.WritePriorityAsync(ctx, streamId, streamDependency, weight, exclusive, promise);
    }

    public virtual Task WriteRstStreamAsync(IChannelHandlerContext ctx, int streamId, long errorCode,
                                        TaskCompletionSource promise)
    {
        return @delegate.WriteRstStreamAsync(ctx, streamId, errorCode, promise);
    }

    public virtual Task WriteSettingsAsync(IChannelHandlerContext ctx, Http2Settings settings,
                                   TaskCompletionSource promise)
    {
        return @delegate.WriteSettingsAsync(ctx, settings, promise);
    }

    public virtual Task WriteSettingsAckAsync(IChannelHandlerContext ctx, TaskCompletionSource promise)
    {
        return @delegate.WriteSettingsAckAsync(ctx, promise);
    }

    public virtual Task WritePingAsync(IChannelHandlerContext ctx, bool ack, long data, TaskCompletionSource promise)
    {
        return @delegate.WritePingAsync(ctx, ack, data, promise);
    }

    public virtual Task WritePushPromiseAsync(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
                                          IHttp2Headers headers, int padding, TaskCompletionSource promise)
    {
        return @delegate.WritePushPromiseAsync(ctx, streamId, promisedStreamId, headers, padding, promise);
    }

    public virtual Task WriteGoAwayAsync(IChannelHandlerContext ctx, int lastStreamId, long errorCode,
                                     ByteBuf debugData,
                                     TaskCompletionSource promise)
    {
        return @delegate.WriteGoAwayAsync(ctx, lastStreamId, errorCode, debugData, promise);
    }

    public virtual Task WriteWindowUpdateAsync(IChannelHandlerContext ctx, int streamId, int windowSizeIncrement,
                                           TaskCompletionSource promise)
    {
        return @delegate.WriteWindowUpdateAsync(ctx, streamId, windowSizeIncrement, promise);
    }

    public virtual Task WriteFrameAsync(IChannelHandlerContext ctx, byte frameType, int streamId, Http2Flags flags,
                                    ByteBuf payload, TaskCompletionSource promise)
    {
        return @delegate.WriteFrameAsync(ctx, frameType, streamId, flags, payload, promise);
    }

    public virtual IHttp2FrameWriter.IConfiguration Configuration()
    {
        return @delegate.Configuration();
    }

    public virtual void Close()
    {
        @delegate.Close();
    }
}
