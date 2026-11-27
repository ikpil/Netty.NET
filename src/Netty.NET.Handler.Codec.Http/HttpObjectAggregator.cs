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

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.StringUtil;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;

namespace Netty.NET.Handler.Codec.Http;

/**
 * A {@link ChannelHandler} that aggregates an {@link HttpMessage}
 * and its following {@link HttpContent}s into a single {@link FullHttpRequest}
 * or {@link FullHttpResponse} (depending on if it used to handle requests or responses)
 * with no following {@link HttpContent}s.  It is useful when you don't want to take
 * care of HTTP messages whose transfer encoding is 'chunked'.  Insert this
 * handler after {@link HttpResponseDecoder} in the {@link ChannelPipeline} if being used to handle
 * responses, or after {@link HttpRequestDecoder} and {@link HttpResponseEncoder} in the
 * {@link ChannelPipeline} if being used to handle requests.
 * <blockquote>
 *  <pre>
 *  {@link ChannelPipeline} p = ...;
 *  ...
 *  p.addLast("decoder", <b>new {@link HttpRequestDecoder}()</b>);
 *  p.addLast("encoder", <b>new {@link HttpResponseEncoder}()</b>);
 *  p.addLast("aggregator", <b>new {@link HttpObjectAggregator}(1048576)</b>);
 *  ...
 *  p.addLast("handler", new HttpRequestHandler());
 *  </pre>
 * </blockquote>
 * <p>
 * For convenience, consider putting a {@link HttpServerCodec} before the {@link HttpObjectAggregator}
 * as it functions as both a {@link HttpRequestDecoder} and a {@link HttpResponseEncoder}.
 * </p>
 * Be aware that {@link HttpObjectAggregator} may end up sending a {@link HttpResponse}:
 * <table border summary="Possible Responses">
 *   <tbody>
 *     <tr>
 *       <th>Response Status</th>
 *       <th>Condition When Sent</th>
 *     </tr>
 *     <tr>
 *       <td>100 Continue</td>
 *       <td>A '100-continue' expectation is received and the 'content-length' doesn't exceed maxContentLength</td>
 *     </tr>
 *     <tr>
 *       <td>417 Expectation Failed</td>
 *       <td>A '100-continue' expectation is received and the 'content-length' exceeds maxContentLength</td>
 *     </tr>
 *     <tr>
 *       <td>413 Request Entity Too Large</td>
 *       <td>Either the 'content-length' or the bytes received so far exceed maxContentLength</td>
 *     </tr>
 *   </tbody>
 * </table>
 *
 * @see FullHttpRequest
 * @see FullHttpResponse
 * @see HttpResponseDecoder
 * @see HttpServerCodec
 */
// CLR: MessageAggregator<HttpObject, HttpMessage, HttpContent, FullHttpMessage> is the codec-base
// MessageAggregator<IHttpObject, IHttpMessage, IHttpContent, IFullHttpMessage>, and HttpObject.class is
// typeof(IHttpObject) (docs/codec-base-porting.md, Type matching). The static initializer is the static constructor.
// The ChannelFuture listeners of handleOversizedMessage are Task continuations that run synchronously, as codec-base
// MessageAggregator attaches its own (docs/codec-base-porting.md, MessageAggregator); ctx.close() discards the Task.
// NumberFormatException is FormatException, IllegalStateException InvalidOperationException, java.lang.Error
// UnreachableException (docs/codec-memcache-porting.md) and assert Debug.Assert. In the nested messages,
// setProtocolVersion/retain/touch/copy/duplicate/retainedDuplicate/replace return IFullHttpMessage and are narrowed by
// covariant overrides in the request/response classes; the IHttpMessage, IHttpRequest, IHttpResponse, IByteBufHolder and
// IReferenceCounted members that the interfaces declare with other return types are implemented explicitly (see
// DefaultHttpContent). The overrides of the @Deprecated getProtocolVersion()/getDecoderResult()/getMethod()/getUri()/
// getStatus() have no counterpart (see IHttpObject); method()/uri()/status() return the message's value that Java reads
// through them, and replace(...) reads protocolVersion()/status().
public class HttpObjectAggregator
        : MessageAggregator<IHttpObject, IHttpMessage, IHttpContent, IFullHttpMessage>
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(HttpObjectAggregator));
    private static readonly IFullHttpResponse CONTINUE =
            new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.CONTINUE, Unpooled.EmptyBuffer);
    private static readonly IFullHttpResponse EXPECTATION_FAILED = new DefaultFullHttpResponse(
            HttpVersion.HTTP_1_1, HttpResponseStatus.EXPECTATION_FAILED, Unpooled.EmptyBuffer);
    private static readonly IFullHttpResponse TOO_LARGE_CLOSE = new DefaultFullHttpResponse(
            HttpVersion.HTTP_1_1, HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, Unpooled.EmptyBuffer);
    private static readonly IFullHttpResponse TOO_LARGE = new DefaultFullHttpResponse(
        HttpVersion.HTTP_1_1, HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, Unpooled.EmptyBuffer);

    static HttpObjectAggregator()
    {
        EXPECTATION_FAILED.Headers().SetInt(CONTENT_LENGTH, 0);
        TOO_LARGE.Headers().SetInt(CONTENT_LENGTH, 0);

        TOO_LARGE_CLOSE.Headers().SetInt(CONTENT_LENGTH, 0);
        TOO_LARGE_CLOSE.Headers().Set(CONNECTION, HttpHeaderValues.CLOSE);
    }

    private readonly bool closeOnExpectationFailed;

    /**
     * Creates a new instance.
     * @param maxContentLength the maximum length of the aggregated content in bytes.
     * If the length of the aggregated content exceeds this value,
     * {@link #handleOversizedMessage(ChannelHandlerContext, HttpMessage)} will be called.
     */
    public HttpObjectAggregator(int maxContentLength) : this(maxContentLength, false)
    {
    }

    /**
     * Creates a new instance.
     * @param maxContentLength the maximum length of the aggregated content in bytes.
     * If the length of the aggregated content exceeds this value,
     * {@link #handleOversizedMessage(ChannelHandlerContext, HttpMessage)} will be called.
     * @param closeOnExpectationFailed If a 100-continue response is detected but the content length is too large
     * then {@code true} means close the connection. otherwise the connection will remain open and data will be
     * consumed and discarded until the next request is received.
     */
    public HttpObjectAggregator(int maxContentLength, bool closeOnExpectationFailed)
        : base(maxContentLength, typeof(IHttpObject))
    {
        this.closeOnExpectationFailed = closeOnExpectationFailed;
    }

    protected override bool IsStartMessage(IHttpObject msg)
    {
        return msg is IHttpMessage;
    }

    protected override bool IsContentMessage(IHttpObject msg)
    {
        return msg is IHttpContent;
    }

    protected override bool IsLastContentMessage(IHttpContent msg)
    {
        return msg is ILastHttpContent;
    }

    protected override bool IsAggregated(IHttpObject msg)
    {
        return msg is IFullHttpMessage;
    }

    protected override bool IsContentLengthInvalid(IHttpMessage start, int maxContentLength)
    {
        try
        {
            return HttpUtil.GetContentLength(start, -1L) > maxContentLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private object ContinueResponse(IHttpMessage start, int maxContentLength, IChannelPipeline pipeline)
    {
        if (HttpUtil.IsUnsupportedExpectation(start))
        {
            // if the request contains an unsupported expectation, we return 417
            pipeline.FireUserEventTriggered(HttpExpectationFailedEvent.INSTANCE);
            return EXPECTATION_FAILED.RetainedDuplicate();
        }
        else if (HttpUtil.Is100ContinueExpected(start))
        {
            // if the request contains 100-continue but the content-length is too large, we return 413
            if (!IsContentLengthInvalid(start, maxContentLength))
            {
                return CONTINUE.RetainedDuplicate();
            }
            pipeline.FireUserEventTriggered(HttpExpectationFailedEvent.INSTANCE);
            return TOO_LARGE.RetainedDuplicate();
        }

        return null;
    }

    protected override object NewContinueResponse(IHttpMessage start, int maxContentLength, IChannelPipeline pipeline)
    {
        object response = ContinueResponse(start, maxContentLength, pipeline);
        // we're going to respond based on the request expectation so there's no
        // need to propagate the expectation further.
        if (response != null)
        {
            start.Headers().Remove(EXPECT);
        }
        return response;
    }

    protected override bool CloseAfterContinueResponse(object msg)
    {
        return closeOnExpectationFailed && IgnoreContentAfterContinueResponse(msg);
    }

    protected override bool IgnoreContentAfterContinueResponse(object msg)
    {
        if (msg is IHttpResponse)
        {
            IHttpResponse httpResponse = (IHttpResponse) msg;
            return httpResponse.Status().CodeClass().Equals(HttpStatusClass.CLIENT_ERROR);
        }
        return false;
    }

    protected override IFullHttpMessage BeginAggregation(IHttpMessage start, ByteBuf content)
    {
        Debug.Assert(!(start is IFullHttpMessage));

        HttpUtil.SetTransferEncodingChunked(start, false);

        if (start is IHttpRequest)
        {
            return new AggregatedFullHttpRequest((IHttpRequest) start, content, null);
        }
        else if (start is IHttpResponse)
        {
            return new AggregatedFullHttpResponse((IHttpResponse) start, content, null);
        }
        else
        {
            throw new UnreachableException("Unexpected http message type: " + ClassName(start));
        }
    }

    protected override void Aggregate(IFullHttpMessage aggregated, IHttpContent content)
    {
        if (content is ILastHttpContent)
        {
            // Merge trailing headers into the message.
            ((AggregatedFullHttpMessage) aggregated).SetTrailingHeaders(((ILastHttpContent) content).TrailingHeaders());
        }
    }

    protected override void FinishAggregation(IFullHttpMessage aggregated)
    {
        // Set the 'Content-Length' header. If one isn't already set.
        // This is important as HEAD responses will use a 'Content-Length' header which
        // does not match the actual body, but the number of bytes that would be
        // transmitted if a GET would have been used.
        //
        // See rfc2616 14.13 Content-Length
        if (!HttpUtil.IsContentLengthSet(aggregated))
        {
            aggregated.Headers().SetInt(
                    CONTENT_LENGTH,
                    aggregated.Content.ReadableBytes);
        }
    }

    protected override void HandleOversizedMessage(IChannelHandlerContext ctx, IHttpMessage oversized)
    {
        if (oversized is IHttpRequest)
        {
            // send back a 413 and close the connection

            // If the client started to send data already, close because it's impossible to recover.
            // If keep-alive is off and 'Expect: 100-continue' is missing, no need to leave the connection open.
            // If auto read is false the channel must be closed or it will be stuck without a call to read()
            if (oversized is IFullHttpMessage || !ctx.Channel.Config.IsAutoRead() ||
                !HttpUtil.Is100ContinueExpected(oversized) && !HttpUtil.IsKeepAlive(oversized))
            {
                Task future = ctx.WriteAndFlushAsync(TOO_LARGE_CLOSE.RetainedDuplicate());
                _ = future.ContinueWith(f =>
                {
                    if (!f.IsCompletedSuccessfully)
                    {
                        logger.Debug("Failed to send a 413 Request Entity Too Large.", f.Exception?.InnerException);
                    }
                    _ = ctx.CloseAsync();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else
            {
                _ = ctx.WriteAndFlushAsync(TOO_LARGE.RetainedDuplicate()).ContinueWith(future =>
                {
                    if (!future.IsCompletedSuccessfully)
                    {
                        logger.Debug("Failed to send a 413 Request Entity Too Large.", future.Exception?.InnerException);
                        _ = ctx.CloseAsync();
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        else if (oversized is IHttpResponse)
        {
            _ = ctx.CloseAsync();
            throw new TooLongHttpContentException("Response entity too large: " + oversized);
        }
        else
        {
            throw new InvalidOperationException();
        }
    }

    private abstract class AggregatedFullHttpMessage : IFullHttpMessage
    {
        protected readonly IHttpMessage message;
        private readonly ByteBuf content;
        private HttpHeaders trailingHeaders;

        internal AggregatedFullHttpMessage(IHttpMessage message, ByteBuf content, HttpHeaders trailingHeaders)
        {
            this.message = message;
            this.content = content;
            this.trailingHeaders = trailingHeaders;
        }

        public HttpHeaders TrailingHeaders()
        {
            HttpHeaders trailingHeaders = this.trailingHeaders;
            if (trailingHeaders == null)
            {
                return EmptyHttpHeaders.INSTANCE;
            }
            else
            {
                return trailingHeaders;
            }
        }

        internal void SetTrailingHeaders(HttpHeaders trailingHeaders)
        {
            this.trailingHeaders = trailingHeaders;
        }

        public HttpVersion ProtocolVersion()
        {
            return message.ProtocolVersion();
        }

        public virtual IFullHttpMessage SetProtocolVersion(HttpVersion version)
        {
            message.SetProtocolVersion(version);
            return this;
        }

        public HttpHeaders Headers()
        {
            return message.Headers();
        }

        public DecoderResult DecoderResult()
        {
            return message.DecoderResult();
        }

        public void SetDecoderResult(DecoderResult result)
        {
            message.SetDecoderResult(result);
        }

        public ByteBuf Content => content;

        public int RefCnt => content.RefCnt;

        public virtual IFullHttpMessage Retain()
        {
            content.Retain();
            return this;
        }

        public virtual IFullHttpMessage Retain(int increment)
        {
            content.Retain(increment);
            return this;
        }

        public virtual IFullHttpMessage Touch(object hint)
        {
            content.Touch(hint);
            return this;
        }

        public virtual IFullHttpMessage Touch()
        {
            content.Touch();
            return this;
        }

        public bool Release()
        {
            return content.Release();
        }

        public bool Release(int decrement)
        {
            return content.Release(decrement);
        }

        public abstract IFullHttpMessage Copy();

        public abstract IFullHttpMessage Duplicate();

        public abstract IFullHttpMessage RetainedDuplicate();

        public abstract IFullHttpMessage Replace(ByteBuf content);

        IHttpMessage IHttpMessage.SetProtocolVersion(HttpVersion version)
        {
            return SetProtocolVersion(version);
        }
        IByteBufHolder IByteBufHolder.Copy()
        {
            return Copy();
        }
        IByteBufHolder IByteBufHolder.Duplicate()
        {
            return Duplicate();
        }
        IByteBufHolder IByteBufHolder.RetainedDuplicate()
        {
            return RetainedDuplicate();
        }
        IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
        {
            return Replace(content);
        }
        IByteBufHolder IByteBufHolder.Retain()
        {
            return Retain();
        }
        IByteBufHolder IByteBufHolder.Retain(int increment)
        {
            return Retain(increment);
        }
        IByteBufHolder IByteBufHolder.Touch()
        {
            return Touch();
        }
        IByteBufHolder IByteBufHolder.Touch(object hint)
        {
            return Touch(hint);
        }
        IReferenceCounted IReferenceCounted.Retain()
        {
            return Retain();
        }
        IReferenceCounted IReferenceCounted.Retain(int increment)
        {
            return Retain(increment);
        }
        IReferenceCounted IReferenceCounted.Touch()
        {
            return Touch();
        }
        IReferenceCounted IReferenceCounted.Touch(object hint)
        {
            return Touch(hint);
        }
    }

    private sealed class AggregatedFullHttpRequest : AggregatedFullHttpMessage, IFullHttpRequest
    {

        internal AggregatedFullHttpRequest(IHttpRequest request, ByteBuf content, HttpHeaders trailingHeaders)
            : base(request, content, trailingHeaders)
        {
        }

        public override IFullHttpRequest Copy()
        {
            return Replace(Content.Copy());
        }

        public override IFullHttpRequest Duplicate()
        {
            return Replace(Content.Duplicate());
        }

        public override IFullHttpRequest RetainedDuplicate()
        {
            return Replace(Content.RetainedDuplicate());
        }

        public override IFullHttpRequest Replace(ByteBuf content)
        {
            DefaultFullHttpRequest dup = new DefaultFullHttpRequest(ProtocolVersion(), Method(), Uri(), content,
                    Headers().Copy(), TrailingHeaders().Copy());
            dup.SetDecoderResult(DecoderResult());
            return dup;
        }

        public override IFullHttpRequest Retain(int increment)
        {
            base.Retain(increment);
            return this;
        }

        public override IFullHttpRequest Retain()
        {
            base.Retain();
            return this;
        }

        public override IFullHttpRequest Touch()
        {
            base.Touch();
            return this;
        }

        public override IFullHttpRequest Touch(object hint)
        {
            base.Touch(hint);
            return this;
        }

        public IFullHttpRequest SetMethod(HttpMethod method)
        {
            ((IHttpRequest) message).SetMethod(method);
            return this;
        }

        public IFullHttpRequest SetUri(string uri)
        {
            ((IHttpRequest) message).SetUri(uri);
            return this;
        }

        public HttpMethod Method()
        {
            return ((IHttpRequest) message).Method();
        }

        public string Uri()
        {
            return ((IHttpRequest) message).Uri();
        }

        public override IFullHttpRequest SetProtocolVersion(HttpVersion version)
        {
            base.SetProtocolVersion(version);
            return this;
        }

        public override string ToString()
        {
            return HttpMessageUtil.AppendFullRequest(new StringBuilder(256), this).ToString();
        }

        IHttpRequest IHttpRequest.SetMethod(HttpMethod method)
        {
            return SetMethod(method);
        }
        IHttpRequest IHttpRequest.SetUri(string uri)
        {
            return SetUri(uri);
        }
    }

    private sealed class AggregatedFullHttpResponse : AggregatedFullHttpMessage, IFullHttpResponse
    {

        internal AggregatedFullHttpResponse(IHttpResponse message, ByteBuf content, HttpHeaders trailingHeaders)
            : base(message, content, trailingHeaders)
        {
        }

        public override IFullHttpResponse Copy()
        {
            return Replace(Content.Copy());
        }

        public override IFullHttpResponse Duplicate()
        {
            return Replace(Content.Duplicate());
        }

        public override IFullHttpResponse RetainedDuplicate()
        {
            return Replace(Content.RetainedDuplicate());
        }

        public override IFullHttpResponse Replace(ByteBuf content)
        {
            DefaultFullHttpResponse dup = new DefaultFullHttpResponse(ProtocolVersion(), Status(), content,
                    Headers().Copy(), TrailingHeaders().Copy());
            dup.SetDecoderResult(DecoderResult());
            return dup;
        }

        public IFullHttpResponse SetStatus(HttpResponseStatus status)
        {
            ((IHttpResponse) message).SetStatus(status);
            return this;
        }

        public HttpResponseStatus Status()
        {
            return ((IHttpResponse) message).Status();
        }

        public override IFullHttpResponse SetProtocolVersion(HttpVersion version)
        {
            base.SetProtocolVersion(version);
            return this;
        }

        public override IFullHttpResponse Retain(int increment)
        {
            base.Retain(increment);
            return this;
        }

        public override IFullHttpResponse Retain()
        {
            base.Retain();
            return this;
        }

        public override IFullHttpResponse Touch(object hint)
        {
            base.Touch(hint);
            return this;
        }

        public override IFullHttpResponse Touch()
        {
            base.Touch();
            return this;
        }

        public override string ToString()
        {
            return HttpMessageUtil.AppendFullResponse(new StringBuilder(256), this).ToString();
        }

        IHttpResponse IHttpResponse.SetStatus(HttpResponseStatus status)
        {
            return SetStatus(status);
        }
    }
}
