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

using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * This adapter provides just header/data events from the HTTP message flow defined
 * in <a href="https://tools.ietf.org/html/rfc7540#section-8.1">[RFC 7540], Section 8.1</a>.
 * <p>
 * See {@link HttpToHttp2ConnectionHandler} to get translation from HTTP/1.x objects to HTTP/2 frames for writes.
 */
// CLR: the overridable Java methods are virtual and the protected final ones are non-virtual; the protected final
// fields are protected readonly. The protected constructor is protected internal, because
// InboundHttp2ToHttpAdapterBuilder creates the adapter through Java's package access. The private interface
// ImmediateSendDetector is the nested IImmediateSendDetector and its anonymous DEFAULT_SEND_DETECTOR
// implementation is the nested DefaultSendDetector. The generic stream property accessors are
// GetProperty<V>/SetProperty<V>/RemoveProperty<V> (IHttp2Stream). FullHttpMessage is IFullHttpMessage, whose
// content() is the Content property, and FullHttpRequest.replace(ByteBuf) returns IByteBufHolder (narrowing
// re-declarations are inherited, docs/codec-http-porting.md), so its result is cast to IFullHttpRequest. Every
// "throws Http2Exception" member throws Http2Exception (C# has no checked exceptions).
public class InboundHttp2ToHttpAdapter : Http2EventAdapter
{
    private static readonly IImmediateSendDetector DEFAULT_SEND_DETECTOR = new DefaultSendDetector();

    private sealed class DefaultSendDetector : IImmediateSendDetector
    {
        public bool MustSendImmediately(IFullHttpMessage msg)
        {
            if (msg is IFullHttpResponse)
            {
                return ((IFullHttpResponse) msg).Status().CodeClass() == HttpStatusClass.INFORMATIONAL;
            }
            if (msg is IFullHttpRequest)
            {
                return msg.Headers().Contains(HttpHeaderNames.EXPECT);
            }

            return false;
        }

        public IFullHttpMessage CopyIfNeeded(IByteBufAllocator allocator, IFullHttpMessage msg)
        {
            if (msg is IFullHttpRequest)
            {
                IFullHttpRequest copy = (IFullHttpRequest) ((IFullHttpRequest) msg).Replace(allocator.Buffer(0));
                copy.Headers().Remove(HttpHeaderNames.EXPECT);
                return copy;
            }

            return null;
        }
    }

    private readonly int maxContentLength;
    private readonly IImmediateSendDetector sendDetector;
    private readonly IHttp2Connection.IPropertyKey messageKey;
    private readonly bool propagateSettings;
    protected readonly IHttp2Connection connection;
    protected readonly bool validateHttpHeaders;

    protected internal InboundHttp2ToHttpAdapter(IHttp2Connection connection, int maxContentLength,
                                        bool validateHttpHeaders, bool propagateSettings)
    {
        this.connection = CheckNotNull(connection, "connection");
        this.maxContentLength = CheckPositive(maxContentLength, "maxContentLength");
        this.validateHttpHeaders = validateHttpHeaders;
        this.propagateSettings = propagateSettings;
        sendDetector = DEFAULT_SEND_DETECTOR;
        messageKey = connection.NewKey();
    }

    /**
     * The stream is out of scope for the HTTP message flow and will no longer be tracked
     * @param stream The stream to remove associated state with
     * @param release {@code true} to call release on the value if it is present. {@code false} to not call release.
     */
    protected void RemoveMessage(IHttp2Stream stream, bool release)
    {
        IFullHttpMessage msg = stream.RemoveProperty<IFullHttpMessage>(messageKey);
        if (release && msg != null)
        {
            msg.Release();
        }
    }

    /**
     * Get the {@link FullHttpMessage} associated with {@code stream}.
     * @param stream The stream to get the associated state from
     * @return The {@link FullHttpMessage} associated with {@code stream}.
     */
    protected IFullHttpMessage GetMessage(IHttp2Stream stream)
    {
        return stream.GetProperty<IFullHttpMessage>(messageKey);
    }

    /**
     * Make {@code message} be the state associated with {@code stream}.
     * @param stream The stream which {@code message} is associated with.
     * @param message The message which contains the HTTP semantics.
     */
    protected void PutMessage(IHttp2Stream stream, IFullHttpMessage message)
    {
        IFullHttpMessage previous = stream.SetProperty(messageKey, message);
        if (previous != message && previous != null)
        {
            previous.Release();
        }
    }

    public override void OnStreamRemoved(IHttp2Stream stream)
    {
        RemoveMessage(stream, true);
    }

    /**
     * Set final headers and fire a channel read event
     *
     * @param ctx The context to fire the event on
     * @param msg The message to send
     * @param release {@code true} to call release on the value if it is present. {@code false} to not call release.
     * @param stream the stream of the message which is being fired
     */
    protected virtual void FireChannelRead(IChannelHandlerContext ctx, IFullHttpMessage msg, bool release,
                                   IHttp2Stream stream)
    {
        RemoveMessage(stream, release);
        HttpUtil.SetContentLength(msg, msg.Content.ReadableBytes);
        ctx.FireChannelRead(msg);
    }

    /**
     * Create a new {@link FullHttpMessage} based upon the current connection parameters
     *
     * @param stream The stream to create a message for
     * @param headers The headers associated with {@code stream}
     * @param validateHttpHeaders
     * <ul>
     * <li>{@code true} to validate HTTP headers in the http-codec</li>
     * <li>{@code false} not to validate HTTP headers in the http-codec</li>
     * </ul>
     * @param alloc The {@link ByteBufAllocator} to use to generate the content of the message
     * @throws Http2Exception If there is an error when creating {@link FullHttpMessage} from
     *                        {@link Http2Stream} and {@link Http2Headers}
     */
    protected virtual IFullHttpMessage NewMessage(IHttp2Stream stream, IHttp2Headers headers, bool validateHttpHeaders,
                                         IByteBufAllocator alloc)
    {
        return connection.IsServer() ? HttpConversionUtil.ToFullHttpRequest(stream.Id(), headers, alloc,
                validateHttpHeaders) : HttpConversionUtil.ToFullHttpResponse(stream.Id(), headers, alloc,
                validateHttpHeaders);
    }

    /**
     * Provides translation between HTTP/2 and HTTP header objects while ensuring the stream
     * is in a valid state for additional headers.
     *
     * @param ctx The context for which this message has been received.
     * Used to send informational header if detected.
     * @param stream The stream the {@code headers} apply to
     * @param headers The headers to process
     * @param endOfStream {@code true} if the {@code stream} has received the end of stream flag
     * @param allowAppend
     * <ul>
     * <li>{@code true} if headers will be appended if the stream already exists.</li>
     * <li>if {@code false} and the stream already exists this method returns {@code null}.</li>
     * </ul>
     * @param appendToTrailer
     * <ul>
     * <li>{@code true} if a message {@code stream} already exists then the headers
     * should be added to the trailing headers.</li>
     * <li>{@code false} then appends will be done to the initial headers.</li>
     * </ul>
     * @return The object used to track the stream corresponding to {@code stream}. {@code null} if
     *         {@code allowAppend} is {@code false} and the stream already exists.
     * @throws Http2Exception If the stream id is not in the correct state to process the headers request
     */
    protected virtual IFullHttpMessage ProcessHeadersBegin(IChannelHandlerContext ctx, IHttp2Stream stream,
                                                  IHttp2Headers headers, bool endOfStream, bool allowAppend,
                                                  bool appendToTrailer)
    {
        IFullHttpMessage msg = GetMessage(stream);
        bool release = true;

        if (msg == null)
        {
            msg = NewMessage(stream, headers, validateHttpHeaders, ctx.Alloc());
        }
        else if (allowAppend)
        {
            release = false;
            HttpConversionUtil.AddHttp2ToHttpHeaders(stream.Id(), headers, msg, appendToTrailer);
        }
        else
        {
            release = false;
            msg = null;
        }

        if (sendDetector.MustSendImmediately(msg))
        {
            // Copy the message (if necessary) before sending. The content is not expected to be copied (or used) in
            // this operation but just in case it is used do the copy before sending and the resource may be released
            IFullHttpMessage copy = endOfStream ? null : sendDetector.CopyIfNeeded(ctx.Alloc(), msg);
            FireChannelRead(ctx, msg, release, stream);
            return copy;
        }

        return msg;
    }

    /**
     * After HTTP/2 headers have been processed by {@link #processHeadersBegin} this method either
     * sends the result up the pipeline or retains the message for future processing.
     *
     * @param ctx The context for which this message has been received
     * @param stream The stream the {@code objAccumulator} corresponds to
     * @param msg The object which represents all headers/data for corresponding to {@code stream}
     * @param endOfStream {@code true} if this is the last event for the stream
     */
    private void ProcessHeadersEnd(IChannelHandlerContext ctx, IHttp2Stream stream, IFullHttpMessage msg,
                                   bool endOfStream)
    {
        if (endOfStream)
        {
            // Release if the msg from the map is different from the object being forwarded up the pipeline.
            FireChannelRead(ctx, msg, GetMessage(stream) != msg, stream);
        }
        else
        {
            PutMessage(stream, msg);
        }
    }

    public override int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding,
                                   bool endOfStream)
    {
        IHttp2Stream stream = connection.Stream(streamId);
        IFullHttpMessage msg = GetMessage(stream);
        if (msg == null)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Data Frame received for unknown stream id %d", streamId);
        }

        ByteBuf content = msg.Content;
        int dataReadableBytes = data.ReadableBytes;
        if (content.ReadableBytes > maxContentLength - dataReadableBytes)
        {
            throw StreamError(streamId, ENHANCE_YOUR_CALM,
                    "Content length exceeded max of %d for stream id %d", maxContentLength, streamId);
        }

        content.WriteBytes(data, data.ReaderIndex, dataReadableBytes);

        if (endOfStream)
        {
            FireChannelRead(ctx, msg, false, stream);
        }

        // All bytes have been processed.
        return dataReadableBytes + padding;
    }

    public override void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding,
                              bool endOfStream)
    {
        IHttp2Stream stream = connection.Stream(streamId);
        IFullHttpMessage msg = ProcessHeadersBegin(ctx, stream, headers, endOfStream, true, true);
        if (msg != null)
        {
            ProcessHeadersEnd(ctx, stream, msg, endOfStream);
        }
    }

    public override void OnHeadersRead(IChannelHandlerContext ctx, int streamId, IHttp2Headers headers,
                              int streamDependency, short weight, bool exclusive, int padding, bool endOfStream)
    {
        IHttp2Stream stream = connection.Stream(streamId);
        IFullHttpMessage msg = ProcessHeadersBegin(ctx, stream, headers, endOfStream, true, true);
        if (msg != null)
        {
            // Add headers for dependency and weight.
            // See https://github.com/netty/netty/issues/5866
            if (streamDependency != Http2CodecUtil.CONNECTION_STREAM_ID)
            {
                msg.Headers().SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_DEPENDENCY_ID.Text(),
                        streamDependency);
            }

            msg.Headers().SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), weight);

            ProcessHeadersEnd(ctx, stream, msg, endOfStream);
        }
    }

    public override void OnRstStreamRead(IChannelHandlerContext ctx, int streamId, long errorCode)
    {
        IHttp2Stream stream = connection.Stream(streamId);
        IFullHttpMessage msg = GetMessage(stream);
        if (msg != null)
        {
            OnRstStreamRead(stream, msg);
        }

        ctx.FireExceptionCaught(Http2Exception.StreamError(streamId, Http2Error.ValueOf(errorCode),
                "HTTP/2 to HTTP layer caught stream reset"));
    }

    public override void OnPushPromiseRead(IChannelHandlerContext ctx, int streamId, int promisedStreamId,
                                  IHttp2Headers headers, int padding)
    {
        // A push promise should not be allowed to add headers to an existing stream
        IHttp2Stream promisedStream = connection.Stream(promisedStreamId);

        if (headers.Status() == null)
        {
            // A PUSH_PROMISE frame has no Http response status.
            // https://tools.ietf.org/html/rfc7540#section-8.2.1
            // Server push is semantically equivalent to a server responding to a
            // request; however, in this case, that request is also sent by the
            // server, as a PUSH_PROMISE frame.
            headers.Status(OK.CodeAsText());
        }

        IFullHttpMessage msg = ProcessHeadersBegin(ctx, promisedStream, headers, false, false, false);
        if (msg == null)
        {
            throw ConnectionError(PROTOCOL_ERROR, "Push Promise Frame received for pre-existing stream id %d",
                    promisedStreamId);
        }

        msg.Headers().SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_PROMISE_ID.Text(), streamId);
        msg.Headers().SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(),
                Http2CodecUtil.DEFAULT_PRIORITY_WEIGHT);

        ProcessHeadersEnd(ctx, promisedStream, msg, false);
    }

    public override void OnSettingsRead(IChannelHandlerContext ctx, Http2Settings settings)
    {
        if (propagateSettings)
        {
            // Provide an interface for non-listeners to capture settings
            ctx.FireChannelRead(settings);
        }
    }

    /**
     * Called if a {@code RST_STREAM} is received but we have some data for that stream.
     */
    protected virtual void OnRstStreamRead(IHttp2Stream stream, IFullHttpMessage msg)
    {
        RemoveMessage(stream, true);
    }

    /**
     * Allows messages to be sent up the pipeline before the next phase in the
     * HTTP message flow is detected.
     */
    private interface IImmediateSendDetector
    {
        /**
         * Determine if the response should be sent immediately, or wait for the end of the stream
         *
         * @param msg The response to test
         * @return {@code true} if the message should be sent immediately
         *         {@code false) if we should wait for the end of the stream
         */
        bool MustSendImmediately(IFullHttpMessage msg);

        /**
         * Determine if a copy must be made after an immediate send happens.
         * <p>
         * An example of this use case is if a request is received
         * with a 'Expect: 100-continue' header. The message will be sent immediately,
         * and the data will be queued and sent at the end of the stream.
         *
         * @param allocator The {@link ByteBufAllocator} that can be used to allocate
         * @param msg The message which has just been sent due to {@link #mustSendImmediately(FullHttpMessage)}
         * @return A modified copy of the {@code msg} or {@code null} if a copy is not needed.
         */
        IFullHttpMessage CopyIfNeeded(IByteBufAllocator allocator, IFullHttpMessage msg);
    }
}
