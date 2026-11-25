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

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpObjectDecoder;

namespace Netty.NET.Handler.Codec.Http;

/**
 * A combination of {@link HttpRequestDecoder} and {@link HttpResponseEncoder}
 * which enables easier server side HTTP implementation.
 *
 * <h3>Header Validation</h3>
 *
 * It is recommended to always enable header validation.
 * <p>
 * Without header validation, your system can become vulnerable to
 * <a href="https://cwe.mitre.org/data/definitions/113.html">
 *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
 * </a>.
 * <p>
 * This recommendation stands even when both peers in the HTTP exchange are trusted,
 * as it helps with defence-in-depth.
 *
 * @see HttpClientCodec
 */
// CLR: CombinedChannelDuplexHandler's protected init(...) is Init(...) (docs/transport-porting.md). The inner classes
// read and write the codec's fields through the codec they receive. Queue<Byte> (ArrayDeque) is Queue<byte>, whose
// TryDequeue replaces poll()'s null for an empty queue; Long.SIZE is 64. write(ctx, msg, promise) is WriteAsync: Java
// adds ChannelFutureListener.CLOSE to the caller's promise before encoding, so the channel closes when the write
// completes or fails, also when encoding throws (the pipeline then fails the promise); the C# encoder closes the channel
// from a continuation of the returned Task, or before rethrowing an encoding exception (docs/codec-base-porting.md,
// Outbound operations; promise.unvoid() has no counterpart because there is no void promise). Java notifies that
// listener at once when the promise completes on the event loop (DefaultPromise.notifyListeners()), while the
// Transport's write Tasks run their continuations asynchronously (RunContinuationsAsynchronously), so the continuation
// runs on Common's EventExecutorTaskScheduler over ctx.Executor, the notifyListeners() policy that handler's
// IdleStateHandler and WriteTimeoutHandler use too (docs/handler-porting.md, Listener timing;
// src/Netty.NET.Common/Concurrent/EventExecutorTaskScheduler.cs): it runs the continuation on the completing thread
// when that is the event loop and otherwise hands it to the executor, so the channel is closed when the flush
// completes the write. The four @Deprecated constructors whose @deprecated texts name
// HttpServerCodec(HttpDecoderConfig) are not ported (owner decision 2026-11-16). IllegalStateException is
// InvalidOperationException; the pipeline depth prints with the invariant culture; the package-private
// DEFAULT_MAX_PIPELINE_DEPTH is internal.
public sealed class HttpServerCodec : CombinedChannelDuplexHandler<HttpRequestDecoder, HttpResponseEncoder>,
        HttpServerUpgradeHandler.ISourceCodec
{

    /**
     * The maximum number of pipelined requests we allow to be awaiting a response by default, before
     * decoding of further requests is rejected. This bounds the memory a single connection can force us
     * to hold onto if the peer pipelines requests without reading the corresponding responses.
     */
    internal const int DEFAULT_MAX_PIPELINE_DEPTH = 128;

    private const byte METHOD_FLAG_HEAD = 1;
    private const byte METHOD_FLAG_CONNECT = 2;
    private const byte METHOD_FLAG_OTHER = 3;

    // We only need 2 bits per request because we distinguish:
    // 01 = HEAD, 10 = CONNECT, 11 = other
    private const int METHOD_FLAG_BITS = 2;
    private const int INLINE_QUEUE_CAPACITY = 64 / METHOD_FLAG_BITS; // 32

    /**
     * FIFO of request method flags.
     *
     * The oldest entry is stored in the least-significant bits so poll is just a mask + unsigned shift.
     * This avoids allocation for the common case of <= 32 outstanding requests.
     *
     * Once more than {@link #INLINE_QUEUE_CAPACITY} requests are queued, additional entries are appended
     * to {@link #methodOverflowQueue}. Order is preserved by always draining the inline queue first.
     */
    private long methodQueue;
    private int methodQueueSize;
    private Queue<byte> methodOverflowQueue;
    private readonly int maxPipelineDepth;

    /**
     * When set, the connection will be closed after the next response is written.
     */
    private bool mustCloseAfterResponse;

    /**
     * Creates a new instance with the default decoder options
     * ({@code maxInitialLineLength (4096)}, {@code maxHeaderSize (8192)}, and
     * {@code maxChunkSize (8192)}).
     */
    public HttpServerCodec() : this(DEFAULT_MAX_INITIAL_LINE_LENGTH, DEFAULT_MAX_HEADER_SIZE, DEFAULT_MAX_CHUNK_SIZE)
    {
    }

    /**
     * Creates a new instance with the specified decoder options.
     */
    public HttpServerCodec(int maxInitialLineLength, int maxHeaderSize, int maxChunkSize)
        : this(new HttpDecoderConfig()
                .SetMaxInitialLineLength(maxInitialLineLength)
                .SetMaxHeaderSize(maxHeaderSize)
                .SetMaxChunkSize(maxChunkSize))
    {
    }

    /**
     * Creates a new instance with the specified decoder configuration.
     */
    public HttpServerCodec(HttpDecoderConfig config) : this(config, DEFAULT_MAX_PIPELINE_DEPTH)
    {
    }

    /**
     * Creates a new instance with the specified decoder configuration.
     *
     * @param config the decoder configuration.
     * @param maxPipelineDepth the maximum number of requests that may be decoded while awaiting the
     *                         corresponding responses to be written, before decoding of further requests
     *                         is rejected with an {@link IllegalStateException}.
     */
    public HttpServerCodec(HttpDecoderConfig config, int maxPipelineDepth)
    {
        this.maxPipelineDepth = ObjectUtil.CheckPositive(maxPipelineDepth, "maxPipelineDepth");
        Init(new HttpServerRequestDecoder(this, config), new HttpServerResponseEncoder(this));
    }

    /**
     * Upgrades to another protocol from HTTP. Removes the {@link HttpRequestDecoder} and
     * {@link HttpResponseEncoder} from the pipeline.
     */
    public void UpgradeFrom(IChannelHandlerContext ctx)
    {
        ctx.Pipeline.Remove(this);
    }

    private bool EnqueueMethod(HttpMethod method)
    {
        Queue<byte> overflowQueue = methodOverflowQueue;
        int currentDepth = methodQueueSize + (overflowQueue != null ? overflowQueue.Count : 0);
        if (currentDepth >= maxPipelineDepth)
        {
            return false;
        }

        byte flag;
        if (HttpMethod.HEAD.Equals(method))
        {
            flag = METHOD_FLAG_HEAD;
        }
        else if (HttpMethod.CONNECT.Equals(method))
        {
            flag = METHOD_FLAG_CONNECT;
        }
        else
        {
            flag = METHOD_FLAG_OTHER;
        }

        // Once we have overflow, always append there until it drains completely.
        if (overflowQueue != null)
        {
            overflowQueue.Enqueue(flag);
            return true;
        }

        if (methodQueueSize < INLINE_QUEUE_CAPACITY)
        {
            methodQueue |= (long) flag << (methodQueueSize << 1);
            methodQueueSize++;
        }
        else
        {
            overflowQueue = new Queue<byte>(4);
            overflowQueue.Enqueue(flag);
            methodOverflowQueue = overflowQueue;
        }
        return true;
    }

    private byte PollMethod()
    {
        if (methodQueueSize != 0)
        {
            //(methodQueue & ((1L << METHOD_FLAG_BITS) - 1))
            byte flag = (byte) (methodQueue & 0x3L);
            methodQueue >>>= METHOD_FLAG_BITS;
            methodQueueSize--;
            return flag;
        }

        Queue<byte> overflowQueue = methodOverflowQueue;
        if (overflowQueue != null)
        {
            bool polled = overflowQueue.TryDequeue(out byte flag);
            if (overflowQueue.Count == 0)
            {
                methodOverflowQueue = null;
            }
            return polled ? flag : METHOD_FLAG_OTHER;
        }

        return METHOD_FLAG_OTHER;
    }

    private sealed class HttpServerRequestDecoder : HttpRequestDecoder
    {
        private readonly HttpServerCodec codec;
        private bool discard;

        internal HttpServerRequestDecoder(HttpServerCodec codec, HttpDecoderConfig config) : base(config)
        {
            this.codec = codec;
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf buffer, IList<object> @out)
        {
            if (discard)
            {
                buffer.SkipBytes(buffer.ReadableBytes);
                return;
            }
            int oldSize = @out.Count;
            base.Decode(ctx, buffer, @out);
            int size = @out.Count;
            for (int i = oldSize; i < size; i++)
            {
                object obj = @out[i];
                if (obj is IHttpRequest)
                {
                    if (!codec.EnqueueMethod(((IHttpRequest) obj).Method()))
                    {
                        // We hit the limit, let's discard everything and release everything and also ensure
                        // we close the connection once the first response is written back.
                        codec.mustCloseAfterResponse = true;
                        discard = true;
                        ReferenceCountUtil.Release(obj);
                        while (++i < size)
                        {
                            ReferenceCountUtil.Release(@out[i]);
                        }
                        @out.Clear();
                        throw new System.InvalidOperationException("maxPipelineDepth exceeded: " +
                                codec.maxPipelineDepth.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        protected override void HandleTransferEncodingChunkedWithContentLength(IHttpMessage message)
        {
            base.HandleTransferEncodingChunkedWithContentLength(message);
            codec.mustCloseAfterResponse = true;
        }
    }

    private sealed class HttpServerResponseEncoder : HttpResponseEncoder
    {
        private readonly HttpServerCodec codec;

        private byte methodFlag;

        internal HttpServerResponseEncoder(HttpServerCodec codec)
        {
            this.codec = codec;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (codec.mustCloseAfterResponse && msg is ILastHttpContent)
            {
                codec.mustCloseAfterResponse = false;
                IChannel channel = ctx.Channel;
                Task future;
                try
                {
                    future = base.WriteAsync(ctx, msg, cancellationToken);
                }
                catch
                {
                    _ = channel.CloseAsync();
                    throw;
                }
                // CLR: the scheduler's lambda captures this branch's copy of ctx, so the other writes allocate no
                // closure (see EventExecutorTaskScheduler).
                IChannelHandlerContext listenerCtx = ctx;
                _ = future.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        new EventExecutorTaskScheduler(() => listenerCtx.Executor));
                return future;
            }
            return base.WriteAsync(ctx, msg, cancellationToken);
        }

        protected override void SanitizeHeadersBeforeEncode(IHttpResponse msg, bool isAlwaysEmpty)
        {
            if (!isAlwaysEmpty && methodFlag == METHOD_FLAG_CONNECT
                    && msg.Status().CodeClass() == HttpStatusClass.SUCCESS)
            {
                // Stripping Transfer-Encoding:
                // See https://tools.ietf.org/html/rfc7230#section-3.3.1
                msg.Headers().Remove(HttpHeaderNames.TRANSFER_ENCODING);
                return;
            }

            base.SanitizeHeadersBeforeEncode(msg, isAlwaysEmpty);
        }

        protected override bool IsContentAlwaysEmpty(IHttpResponse msg)
        {
            if (msg.Status().CodeClass() == HttpStatusClass.INFORMATIONAL)
            {
                // An informational response should be excluded from paired comparison. This covers 101 as well:
                // once the protocol is switched this handler is removed from the pipeline, so the entry that is
                // left behind goes away with it. Just delegate to super method which has all the needed handling.
                return base.IsContentAlwaysEmpty(msg);
            }
            methodFlag = codec.PollMethod();
            return methodFlag == METHOD_FLAG_HEAD || base.IsContentAlwaysEmpty(msg);
        }
    }
}
