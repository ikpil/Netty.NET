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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * This handler converts from {@link Http3RequestStreamFrame} to {@link HttpObject},
 * and back. It can be used as an adapter in conjunction with {@link
 * Http3ServerConnectionHandler} or {@link Http3ClientConnectionHandler} to make http/3 connections
 * backward-compatible with {@link ChannelHandler}s expecting {@link HttpObject}.
 * <p>
 * For simplicity, it converts to chunked encoding unless the entire stream
 * is a single header.
 */
// CLR: the codec-http interfaces take the I prefix and their getters are methods, ByteBufHolder.content() is the
// Content property (docs/codec-http-porting.md, Shared CLR adaptations), as in codec-http2's
// Http2StreamFrameToHttpObjectCodec. The @Deprecated DefaultLastHttpContent(ByteBuf, boolean), whose @deprecated text
// names DefaultLastHttpContent(ByteBuf), is not ported in codec-http, so channelRead calls the HttpHeadersFactory
// constructor with trailersFactory().withValidation(validateHeaders), which is what the deprecated constructor does in
// Java (as codec-http2's port). ChannelOutboundHandler is IChannelOutboundHandler: write(ctx, msg, promise) is
// WriteAsync(ctx, msg, CancellationToken), which creates the promise with Http3CodecUtils.NewPromise() and returns its
// Task, and the other outbound operations return the context's Task (docs/codec-http2-porting.md, Shared CLR
// adaptations). There is no void promise (docs/transport-porting.md), so promise.isVoid() is false: the
// !promise.isVoid() conditions are left out and unvoid() is the promise itself. PromiseCombiner is a List<Task> that
// codec-base's ExecutorPromiseCombiner.Combine completes on the event loop (combiner.finish(promise)); ctx.write(msg,
// promise) is Http3CodecUtils.Write. The ChannelFutureListener QuicStreamChannel.SHUTDOWN_OUTPUT, which codec-quic does
// not port, is the listener that calls ShutdownOutputAsync() on the stream (the future's channel) when the promise
// completes, added with Http3CodecUtils.AddListener (docs/codec-classes-quic-porting.md). SocketAddress is EndPoint.
// isSharable() overrides the IsSharable property. "throws Exception"/"throws Http3Exception" have no counterpart; the
// @Nullable (org.jetbrains.annotations) annotations have no counterpart.
public sealed class Http3FrameToHttpObjectCodec : Http3RequestStreamInboundHandler, IChannelOutboundHandler
{

    private readonly bool isServer;
    private readonly bool validateHeaders;
    private bool inboundTranslationInProgress;

    public Http3FrameToHttpObjectCodec(bool isServer,
                                       bool validateHeaders)
    {
        this.isServer = isServer;
        this.validateHeaders = validateHeaders;
    }

    public Http3FrameToHttpObjectCodec(bool isServer) : this(isServer, true)
    {
    }

    public override bool IsSharable => false;

    protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3HeadersFrame frame)
    {
        IHttp3Headers headers = frame.Headers();
        long id = ((IQuicStreamChannel) ctx.Channel).StreamId();

        ICharSequence status = headers.Status();

        // 100-continue response is a special case where we should not send a fin,
        // but we need to decode it as a FullHttpResponse to play nice with HttpObjectAggregator.
        if (null != status && HttpResponseStatus.CONTINUE.CodeAsText().ContentEquals(status))
        {
            IFullHttpMessage fullMsg = NewFullMessage(id, headers, ctx.Alloc());
            ctx.FireChannelRead(fullMsg);
            return;
        }

        if (headers.Method() == null && status == null)
        {
            // Must be trailers!
            // CLR: new DefaultLastHttpContent(EMPTY_BUFFER, validateHeaders) (deprecated, see the class comment).
            ILastHttpContent last = new DefaultLastHttpContent(Unpooled.EmptyBuffer,
                    DefaultHttpHeadersFactory.TrailersFactory().WithValidation(validateHeaders));
            HttpConversionUtil.AddHttp3ToHttpHeaders(id, headers, last.TrailingHeaders(),
                    HttpVersion.HTTP_1_1, true, true);
            inboundTranslationInProgress = false;
            ctx.FireChannelRead(last);
        }
        else
        {
            IHttpMessage req = NewMessage(id, headers);
            if (!HttpUtil.IsContentLengthSet(req))
            {
                req.Headers().Add(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
            }

            inboundTranslationInProgress = true;
            ctx.FireChannelRead(req);
        }
    }

    protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3DataFrame frame)
    {
        inboundTranslationInProgress = true;
        ctx.FireChannelRead(new DefaultHttpContent(frame.Content));
    }

    protected override void ChannelInputClosed(IChannelHandlerContext ctx)
    {
        if (inboundTranslationInProgress)
        {
            ctx.FireChannelRead(ILastHttpContent.EMPTY_LAST_CONTENT);
        }
    }

    /**
     * Encode from an {@link HttpObject} to an {@link Http3RequestStreamFrame}. This method will
     * be called for each written message that can be handled by this encoder.
     *
     * NOTE: 100-Continue responses that are NOT {@link FullHttpResponse} will be rejected.
     *
     * @param ctx           the {@link ChannelHandlerContext} which this handler belongs to
     * @param msg           the {@link HttpObject} message to encode
     * @throws Exception    is thrown if an error occurs
     */
    public Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (!(msg is IHttpObject))
        {
            throw new UnsupportedMessageTypeException(msg, typeof(IHttpObject));
        }

        TaskCompletionSource promise = Http3CodecUtils.NewPromise();

        // 100-continue is typically a FullHttpResponse, but the decoded
        // Http3HeadersFrame should not handles as a end of stream.
        if (msg is IHttpResponse)
        {
            IHttpResponse res = (IHttpResponse) msg;
            if (res.Status().Equals(HttpResponseStatus.CONTINUE))
            {
                if (res is IFullHttpResponse)
                {
                    IHttp3Headers headers = ToHttp3Headers(res);
                    Http3CodecUtils.Write(ctx, new DefaultHttp3HeadersFrame(headers), promise);
                    ((IFullHttpResponse) res).Release();
                    return promise.Task;
                }
                else
                {
                    throw new EncoderException(
                            HttpResponseStatus.CONTINUE + " must be a FullHttpResponse");
                }
            }
        }

        // this combiner is created lazily if we need multiple write calls
        List<Task> combiner = null;
        // With the last content, *if* we write anything here, we need to wait for that write to complete before
        // closing. To do that, we need to unvoid the promise. So if we write anything *and* this is the last message
        // we will unvoid.
        bool isLast = msg is ILastHttpContent;

        if (msg is IHttpMessage)
        {
            IHttp3Headers headers = ToHttp3Headers((IHttpMessage) msg);
            DefaultHttp3HeadersFrame frame = new DefaultHttp3HeadersFrame(headers);

            // CLR: (!promise.isVoid() || isLast) is always true (see the class comment).
            if (msg is IHttpContent)
            {
                combiner = new List<Task>();
            }

            promise = WriteWithOptionalCombiner(ctx, frame, promise, combiner, isLast);
        }

        if (isLast)
        {
            ILastHttpContent last = (ILastHttpContent) msg;

            try
            {
                bool readable = last.Content.IsReadable();
                bool hasTrailers = !last.TrailingHeaders().IsEmpty();

                // CLR: !promise.isVoid() is always true (see the class comment).
                if (combiner == null && readable && hasTrailers)
                {
                    combiner = new List<Task>();
                }

                if (readable)
                {
                    promise = WriteWithOptionalCombiner(
                            ctx, new DefaultHttp3DataFrame(last.Content.Retain()), promise, combiner, true);
                }

                if (hasTrailers)
                {
                    IHttp3Headers headers = HttpConversionUtil.ToHttp3Headers(last.TrailingHeaders(), validateHeaders);
                    promise = WriteWithOptionalCombiner(ctx,
                            new DefaultHttp3HeadersFrame(headers), promise, combiner, true);
                }
                else if (!readable)
                {
                    if (combiner == null)
                    {
                        // We only need to write something if there was no write before.
                        promise = WriteWithOptionalCombiner(
                                ctx, new DefaultHttp3DataFrame(last.Content.Retain()), promise, combiner, true);
                    }
                }

                // The shutdown is always done via the listener to ensure previous written data is correctly drained
                // before QuicStreamChannel.shutdownOutput() is called. Missing to do so might cause previous queued
                // data to be failed with a ClosedChannelException.
                Http3CodecUtils.AddListener(ctx, promise.Task,
                        _ => ((IQuicStreamChannel) ctx.Channel).ShutdownOutputAsync());
            }
            finally
            {
                // Release LastHttpContent, we retain the content if we need it.
                last.Release();
            }
        }
        else if (msg is IHttpContent)
        {
            promise = WriteWithOptionalCombiner(ctx,
                    new DefaultHttp3DataFrame(((IHttpContent) msg).Content), promise, combiner, false);
        }

        if (combiner != null)
        {
            ExecutorPromiseCombiner.Combine(ctx, combiner, promise);
        }

        return promise.Task;
    }

    /**
     * Write a message. If there is a combiner, add a new write promise to that combiner. If there is no combiner
     * ({@code null}), use the {@code outerPromise} directly as the write promise.
     */
    // CLR: unvoid() is the promise itself (see the class comment), so unvoidPromise has no effect.
    private static TaskCompletionSource WriteWithOptionalCombiner(
            IChannelHandlerContext ctx,
            object msg,
            TaskCompletionSource outerPromise,
            List<Task> combiner,
            bool unvoidPromise
    )
    {
        if (combiner == null)
        {
            Http3CodecUtils.Write(ctx, msg, outerPromise);
        }
        else
        {
            combiner.Add(ctx.WriteAsync(msg));
        }

        return outerPromise;
    }

    private IHttp3Headers ToHttp3Headers(IHttpMessage msg)
    {
        if (msg is IHttpRequest)
        {
            msg.Headers().Set(
                    HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), HttpScheme.HTTPS);
        }

        return HttpConversionUtil.ToHttp3Headers(msg, validateHeaders);
    }

    private IHttpMessage NewMessage(long id,
                                    IHttp3Headers headers)
    {
        return isServer ?
                HttpConversionUtil.ToHttpRequest(id, headers, validateHeaders) :
                HttpConversionUtil.ToHttpResponse(id, headers, validateHeaders);
    }

    private IFullHttpMessage NewFullMessage(long id,
                                            IHttp3Headers headers,
                                            IByteBufAllocator alloc)
    {
        return isServer ?
                HttpConversionUtil.ToFullHttpRequest(id, headers, alloc, validateHeaders) :
                HttpConversionUtil.ToFullHttpResponse(id, headers, alloc, validateHeaders);
    }

    public void Flush(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    public Task BindAsync(IChannelHandlerContext ctx, System.Net.EndPoint localAddress,
                          CancellationToken cancellationToken)
    {
        return ctx.BindAsync(localAddress, cancellationToken);
    }

    public Task ConnectAsync(IChannelHandlerContext ctx, System.Net.EndPoint remoteAddress,
                             System.Net.EndPoint localAddress, CancellationToken cancellationToken)
    {
        return ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DisconnectAsync(cancellationToken);
    }

    public Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.CloseAsync(cancellationToken);
    }

    public Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DeregisterAsync(cancellationToken);
    }

    public void Read(IChannelHandlerContext ctx)
    {
        ctx.Read();
    }
}
