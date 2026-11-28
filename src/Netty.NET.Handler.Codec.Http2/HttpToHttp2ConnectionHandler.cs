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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Translates HTTP/1.x object writes into HTTP/2 frames.
 * <p>
 * See {@link InboundHttp2ToHttpAdapter} to get translation from HTTP/2 frames to HTTP/1.x objects.
 */
// CLR: the protected constructors are protected internal, because HttpToHttp2ConnectionHandlerBuilder creates the
// handler through Java's package access (as Http2ConnectionHandler's constructors). write(ctx, msg, promise) is
// WriteAsync(ctx, msg, cancellationToken), whose returned Task replaces the promise : the handler creates the
// promise (Http2CodecUtil.NewPromise, a TaskCompletionSource) that SimpleChannelPromiseAggregator completes and
// returns its Task, and a message that is not HTTP is passed on with ctx.WriteAsync(msg, cancellationToken). The
// token is not used otherwise (Java has none). The Write*Async Tasks of the encoder are discarded, as Java ignores
// the returned futures. HttpMessage, HttpContent, LastHttpContent and FullHttpMessage are the codec-http
// I-prefixed interfaces whose content() is the Content property.
public class HttpToHttp2ConnectionHandler : Http2ConnectionHandler
{

    private readonly bool validateHeaders;
    private int currentStreamId;
    private HttpScheme httpScheme;

    protected internal HttpToHttp2ConnectionHandler(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                           Http2Settings initialSettings, bool validateHeaders)
        : base(decoder, encoder, initialSettings)
    {
        this.validateHeaders = validateHeaders;
    }

    protected internal HttpToHttp2ConnectionHandler(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                           Http2Settings initialSettings, bool validateHeaders,
                                           bool decoupleCloseAndGoAway)
        : this(decoder, encoder, initialSettings, validateHeaders, decoupleCloseAndGoAway, null)
    {
    }

    protected internal HttpToHttp2ConnectionHandler(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                           Http2Settings initialSettings, bool validateHeaders,
                                           bool decoupleCloseAndGoAway, HttpScheme httpScheme)
        : base(decoder, encoder, initialSettings, decoupleCloseAndGoAway)
    {
        this.validateHeaders = validateHeaders;
        this.httpScheme = httpScheme;
    }

    protected internal HttpToHttp2ConnectionHandler(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                           Http2Settings initialSettings, bool validateHeaders,
                                           bool decoupleCloseAndGoAway, bool flushPreface,
                                           HttpScheme httpScheme)
        : base(decoder, encoder, initialSettings, decoupleCloseAndGoAway, flushPreface)
    {
        this.validateHeaders = validateHeaders;
        this.httpScheme = httpScheme;
    }

    /**
     * Get the next stream id either from the {@link HttpHeaders} object or HTTP/2 codec
     *
     * @param httpHeaders The HTTP/1.x headers object to look for the stream id
     * @return The stream id to use with this {@link HttpHeaders} object
     * @throws Exception If the {@code httpHeaders} object specifies an invalid stream id
     */
    private int GetStreamId(HttpHeaders httpHeaders)
    {
        return httpHeaders.GetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(),
                                  Connection().Local().IncrementAndGetNextStreamId());
    }

    /**
     * Handles conversion of {@link HttpMessage} and {@link HttpContent} to HTTP/2 frames.
     */
    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {

        if (!(msg is IHttpMessage || msg is IHttpContent))
        {
            return ctx.WriteAsync(msg, cancellationToken);
        }

        TaskCompletionSource promise = NewPromise();
        bool release = true;
        SimpleChannelPromiseAggregator promiseAggregator =
                new SimpleChannelPromiseAggregator(promise, ctx.Channel, ctx.Executor);

        try
        {
            IHttp2ConnectionEncoder encoder = Encoder();
            bool endStream = false;

            if (msg is IHttpMessage)
            {
                IHttpMessage httpMsg = (IHttpMessage) msg;

                // Provide the user the opportunity to specify the streamId
                currentStreamId = GetStreamId(httpMsg.Headers());

                // Add HttpScheme if it's defined in constructor and header does not contain it.
                if (httpScheme != null &&
                        !httpMsg.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text()))
                {
                    httpMsg.Headers().Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), httpScheme.Name());
                }

                // Convert and write the headers.
                IHttp2Headers http2Headers = HttpConversionUtil.ToHttp2Headers(httpMsg, validateHeaders);
                endStream = msg is IFullHttpMessage && !((IFullHttpMessage) msg).Content.IsReadable();
                WriteHeaders(ctx, encoder, currentStreamId, httpMsg.Headers(), http2Headers,
                        endStream, promiseAggregator);
            }

            if (!endStream && msg is IHttpContent)
            {
                bool isLastContent = false;
                HttpHeaders trailers = EmptyHttpHeaders.INSTANCE;
                IHttp2Headers http2Trailers = EmptyHttp2Headers.INSTANCE;

                if (msg is ILastHttpContent)
                {
                    isLastContent = true;

                    // Convert any trailing headers.
                    ILastHttpContent lastContent = (ILastHttpContent) msg;
                    trailers = lastContent.TrailingHeaders();
                    http2Trailers = HttpConversionUtil.ToHttp2Headers(trailers, validateHeaders);
                }

                // Write the data
                ByteBuf content = ((IHttpContent) msg).Content;
                endStream = isLastContent && trailers.IsEmpty();
                _ = encoder.WriteDataAsync(ctx, currentStreamId, content, 0, endStream, promiseAggregator.NewPromise());
                release = false;

                if (!trailers.IsEmpty())
                {
                    // Write trailing headers.
                    WriteHeaders(ctx, encoder, currentStreamId, trailers, http2Trailers, true, promiseAggregator);
                }
            }
        }
        catch (Exception t)
        {
            OnError(ctx, true, t);
            promiseAggregator.SetFailure(t);
        }
        finally
        {
            if (release)
            {
                ReferenceCountUtil.Release(msg);
            }

            promiseAggregator.DoneAllocatingPromises();
        }

        return promise.Task;
    }

    private static void WriteHeaders(IChannelHandlerContext ctx, IHttp2ConnectionEncoder encoder, int streamId,
                                     HttpHeaders headers, IHttp2Headers http2Headers, bool endStream,
                                     SimpleChannelPromiseAggregator promiseAggregator)
    {
        int dependencyId = headers.GetInt(
                HttpConversionUtil.ExtensionHeaderNames.STREAM_DEPENDENCY_ID.Text(), 0);
        short weight = headers.GetShort(
                HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), Http2CodecUtil.DEFAULT_PRIORITY_WEIGHT);
        _ = encoder.WriteHeadersAsync(ctx, streamId, http2Headers, dependencyId, weight, false,
                0, endStream, promiseAggregator.NewPromise());
    }
}
