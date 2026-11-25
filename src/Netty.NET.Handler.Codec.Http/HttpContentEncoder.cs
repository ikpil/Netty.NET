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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Encodes the content of the outbound {@link HttpResponse} and {@link HttpContent}.
 * The original content is replaced with the new content encoded by the
 * {@link EmbeddedChannel}, which is created by {@link #beginEncode(HttpResponse, String)}.
 * Once encoding is finished, the value of the <tt>'Content-Encoding'</tt> header
 * is set to the target content encoding, as returned by
 * {@link #beginEncode(HttpResponse, String)}.
 * Also, the <tt>'Content-Length'</tt> header is updated to the length of the
 * encoded content.  If there is no supported or allowed encoding in the
 * corresponding {@link HttpRequest}'s {@code "Accept-Encoding"} header,
 * {@link #beginEncode(HttpResponse, String)} should return {@code null} so that
 * no encoding occurs (i.e. pass-through).
 * <p>
 * Please note that this is an abstract class.  You have to extend this class
 * and implement {@link #beginEncode(HttpResponse, String)} properly to make
 * this class functional.  For example, refer to the source code of
 * {@link HttpContentCompressor}.
 * <p>
 * This handler must be placed after {@link HttpObjectEncoder} in the pipeline
 * so that this handler can intercept HTTP responses before {@link HttpObjectEncoder}
 * converts them into {@link ByteBuf}s.
 */
// CLR: MessageToMessageCodec<HttpRequest, HttpObject> is the codec-base MessageToMessageCodec<IHttpRequest, IHttpObject>
// with the message types typeof(IHttpRequest)/typeof(IHttpObject) (docs/codec-base-porting.md, Type matching). The
// queued Accept-Encoding values are the String values of the request headers (or the String constants IDENTITY,
// ZERO_LENGTH_HEAD and ZERO_LENGTH_CONNECT, which Java types as CharSequence), so the queue holds strings and isPassthru
// keeps Java's reference comparison with ReferenceEquals; ArrayDeque.poll() is Queue.TryDequeue (null when empty) and
// StringUtil.join(",", list) is string.Join. The switch section AWAIT_HEADERS falls through into AWAIT_CONTENT after its
// try/finally, so the fall-through is a goto case after the finally block ("control can't fall through from one switch
// section to the next", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements);
// a break inside the try still runs the finally block. ByteBufHolder is IByteBufHolder (Content property),
// EmbeddedChannel.readOutbound() is ReadOutbound<ByteBuf>(), IllegalStateException is InvalidOperationException, catch
// (Throwable) is catch (Exception) and assert is Debug.Assert (docs/codec-base-porting.md). In the exception messages
// Class.getName() is Type.FullName and Class.getSimpleName() is Type.Name of the C# interface (IHttpResponse,
// IHttpContent); the pipeline depth prints with the invariant culture.
public abstract class HttpContentEncoder : MessageToMessageCodec<IHttpRequest, IHttpObject>
{
    public const int DEFAULT_MAX_PIPELINE_DEPTH = 128;

    private enum State
    {
        PASS_THROUGH,
        AWAIT_HEADERS,
        AWAIT_CONTENT
    }

    private const string ZERO_LENGTH_HEAD = "HEAD";
    private const string ZERO_LENGTH_CONNECT = "CONNECT";

    private readonly int maxPipelineDepth;
    private readonly Queue<string> acceptEncodingQueue = new Queue<string>();
    private EmbeddedChannel encoder;
    private State state = State.AWAIT_HEADERS;

    public HttpContentEncoder() : this(DEFAULT_MAX_PIPELINE_DEPTH)
    {
    }

    public HttpContentEncoder(int maxPipelineDepth) : base(typeof(IHttpRequest), typeof(IHttpObject))
    {
        this.maxPipelineDepth = ObjectUtil.CheckPositive(maxPipelineDepth, "maxPipelineDepth");
    }

    public override bool AcceptOutboundMessage(object msg)
    {
        return msg is IHttpContent || msg is IHttpResponse;
    }

    protected override void Decode(IChannelHandlerContext ctx, IHttpRequest msg, IList<object> @out)
    {
        if (maxPipelineDepth <= acceptEncodingQueue.Count)
        {
            throw new InvalidOperationException("maxPipelineDepth exceeded: " +
                    maxPipelineDepth.ToString(CultureInfo.InvariantCulture));
        }
        string acceptEncoding;
        IList<string> acceptEncodingHeaders = msg.Headers().GetAll(ACCEPT_ENCODING);
        switch (acceptEncodingHeaders.Count)
        {
        case 0:
            acceptEncoding = HttpContentDecoder.IDENTITY;
            break;
        case 1:
            acceptEncoding = acceptEncodingHeaders[0];
            break;
        default:
            // Multiple message-header fields https://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
            acceptEncoding = string.Join(",", acceptEncodingHeaders);
            break;
        }

        HttpMethod method = msg.Method();
        if (HttpMethod.HEAD.Equals(method))
        {
            acceptEncoding = ZERO_LENGTH_HEAD;
        }
        else if (HttpMethod.CONNECT.Equals(method))
        {
            acceptEncoding = ZERO_LENGTH_CONNECT;
        }

        acceptEncodingQueue.Enqueue(acceptEncoding);
        @out.Add(ReferenceCountUtil.Retain(msg));
    }

    protected override void Encode(IChannelHandlerContext ctx, IHttpObject msg, IList<object> @out)
    {
        bool isFull = msg is IHttpResponse && msg is ILastHttpContent;
        switch (state)
        {
            case State.AWAIT_HEADERS:
            {
                EnsureHeaders(msg);
                Debug.Assert(encoder == null);

                IHttpResponse res = (IHttpResponse) msg;
                int code = res.Status().Code();
                HttpStatusClass codeClass = res.Status().CodeClass();
                string acceptEncoding;
                if (codeClass == HttpStatusClass.INFORMATIONAL)
                {
                    // We need to not poll the encoding when response with 1xx codes as another response will follow
                    // for the issued request.
                    // See https://github.com/netty/netty/issues/12904 and https://github.com/netty/netty/issues/4079
                    acceptEncoding = null;
                }
                else
                {
                    // Get the list of encodings accepted by the peer.
                    acceptEncodingQueue.TryDequeue(out acceptEncoding);
                    if (acceptEncoding == null)
                    {
                        throw new InvalidOperationException("cannot send more responses than requests");
                    }
                }

                /*
                 * per rfc2616 4.3 Message Body
                 * All 1xx (informational), 204 (no content), and 304 (not modified) responses MUST NOT include a
                 * message-body. All other responses do include a message-body, although it MAY be of zero length.
                 *
                 * 9.4 HEAD
                 * The HEAD method is identical to GET except that the server MUST NOT return a message-body
                 * in the response.
                 *
                 * Also we should pass through HTTP/1.0 as transfer-encoding: chunked is not supported.
                 *
                 * See https://github.com/netty/netty/issues/5382
                 */
                if (IsPassthru(res.ProtocolVersion(), code, acceptEncoding))
                {
                    @out.Add(ReferenceCountUtil.Retain(res));
                    if (!isFull)
                    {
                        // Pass through all following contents.
                        state = State.PASS_THROUGH;
                    }
                    break;
                }

                if (isFull)
                {
                    // Pass through the full response with empty content and continue waiting for the next resp.
                    if (!((IByteBufHolder) res).Content.IsReadable())
                    {
                        @out.Add(ReferenceCountUtil.Retain(res));
                        break;
                    }
                }

                // Prepare to encode the content.
                Result result = BeginEncode(res, acceptEncoding.ToString());

                // If unable to encode, pass through.
                if (result == null)
                {
                    @out.Add(ReferenceCountUtil.Retain(res));
                    if (!isFull)
                    {
                        // Pass through all following contents.
                        state = State.PASS_THROUGH;
                    }
                    break;
                }

                EmbeddedChannel contentEncoder = result.ContentEncoder();
                try
                {
                    // Encode the content and remove or replace the existing headers
                    // so that the message looks like a decoded message.
                    res.Headers().Set(HttpHeaderNames.CONTENT_ENCODING, result.TargetContentEncoding());

                    // Output the rewritten response.
                    if (isFull)
                    {
                        // Convert full message into unfull one.
                        IHttpResponse newRes = new DefaultHttpResponse(res.ProtocolVersion(), res.Status());
                        newRes.Headers().Set(res.Headers());
                        @out.Add(newRes);

                        EnsureContent(res);
                        encoder = contentEncoder;
                        EncodeFullResponse(newRes, (IHttpContent) res, @out);
                        contentEncoder = null;
                        break;
                    }
                    else
                    {
                        // Make the response chunked to simplify content transformation.
                        res.Headers().Remove(HttpHeaderNames.CONTENT_LENGTH);
                        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);

                        @out.Add(ReferenceCountUtil.Retain(res));
                        state = State.AWAIT_CONTENT;
                        encoder = contentEncoder;
                        contentEncoder = null;
                        if (!(msg is IHttpContent))
                        {
                            // only break out the switch statement if we have not content to process
                            // See https://github.com/netty/netty/issues/2006
                            break;
                        }
                        // Fall through to encode the content
                    }
                }
                finally
                {
                    if (contentEncoder != null)
                    {
                        if (encoder == contentEncoder)
                        {
                            encoder = null;
                        }
                        contentEncoder.FinishAndReleaseAll();
                    }
                }
                goto case State.AWAIT_CONTENT;
            }
            case State.AWAIT_CONTENT:
            {
                EnsureContent(msg);
                if (EncodeContent((IHttpContent) msg, @out))
                {
                    state = State.AWAIT_HEADERS;
                }
                else if (@out.Count == 0)
                {
                    // MessageToMessageCodec needs at least one output message
                    @out.Add(new DefaultHttpContent(Unpooled.EmptyBuffer));
                }
                break;
            }
            case State.PASS_THROUGH:
            {
                EnsureContent(msg);
                @out.Add(ReferenceCountUtil.Retain(msg));
                // Passed through all following contents of the current response.
                if (msg is ILastHttpContent)
                {
                    state = State.AWAIT_HEADERS;
                }
                break;
            }
        }
    }

    private void EncodeFullResponse(IHttpResponse newRes, IHttpContent content, IList<object> @out)
    {
        int existingMessages = @out.Count;
        EncodeContent(content, @out);

        if (HttpUtil.IsContentLengthSet(newRes))
        {
            // adjust the content-length header
            int messageSize = 0;
            for (int i = existingMessages; i < @out.Count; i++)
            {
                object item = @out[i];
                if (item is IHttpContent)
                {
                    messageSize += ((IHttpContent) item).Content.ReadableBytes;
                }
            }
            HttpUtil.SetContentLength(newRes, messageSize);
        }
        else
        {
            newRes.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        }
    }

    private static bool IsPassthru(HttpVersion version, int code, string httpMethod)
    {
        return code < 200 || code == 204 || code == 304 ||
               (ReferenceEquals(httpMethod, ZERO_LENGTH_HEAD) ||
                (ReferenceEquals(httpMethod, ZERO_LENGTH_CONNECT) && code == 200)) ||
                version == HttpVersion.HTTP_1_0;
    }

    private static void EnsureHeaders(IHttpObject msg)
    {
        if (!(msg is IHttpResponse))
        {
            throw new InvalidOperationException(
                    "unexpected message type: " +
                    msg.GetType().FullName + " (expected: " + typeof(IHttpResponse).Name + ')');
        }
    }

    private static void EnsureContent(IHttpObject msg)
    {
        if (!(msg is IHttpContent))
        {
            throw new InvalidOperationException(
                    "unexpected message type: " +
                    msg.GetType().FullName + " (expected: " + typeof(IHttpContent).Name + ')');
        }
    }

    private bool EncodeContent(IHttpContent c, IList<object> @out)
    {
        ByteBuf content = c.Content;

        Encode(content, @out);

        if (c is ILastHttpContent)
        {
            FinishEncode(@out);
            ILastHttpContent last = (ILastHttpContent) c;

            // Generate an additional chunk if the decoder produced
            // the last product on closure,
            HttpHeaders headers = last.TrailingHeaders();
            if (headers.IsEmpty())
            {
                @out.Add(ILastHttpContent.EMPTY_LAST_CONTENT);
            }
            else
            {
                @out.Add(new ComposedLastHttpContent(headers, DecoderResult.SUCCESS));
            }
            return true;
        }
        return false;
    }

    /**
     * Prepare to encode the HTTP message content.
     *
     * @param httpResponse
     *        the http response
     * @param acceptEncoding
     *        the value of the {@code "Accept-Encoding"} header
     *
     * @return the result of preparation, which is composed of the determined
     *         target content encoding and a new {@link EmbeddedChannel} that
     *         encodes the content into the target content encoding.
     *         {@code null} if {@code acceptEncoding} is unsupported or rejected
     *         and thus the content should be handled as-is (i.e. no encoding).
     */
    protected abstract Result BeginEncode(IHttpResponse httpResponse, string acceptEncoding);

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        CleanupSafely(ctx);
        base.HandlerRemoved(ctx);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        CleanupSafely(ctx);
        base.ChannelInactive(ctx);
    }

    private void Cleanup()
    {
        if (encoder != null)
        {
            // Clean-up the previous encoder if not cleaned up correctly.
            encoder.FinishAndReleaseAll();
            encoder = null;
        }
    }

    private void CleanupSafely(IChannelHandlerContext ctx)
    {
        try
        {
            Cleanup();
        }
        catch (Exception cause)
        {
            // If cleanup throws any error we need to propagate it through the pipeline
            // so we don't fail to propagate pipeline events.
            ctx.FireExceptionCaught(cause);
        }
    }

    private void Encode(ByteBuf @in, IList<object> @out)
    {
        // call retain here as it will call release after its written to the channel
        encoder.WriteOutbound(@in.Retain());
        FetchEncoderOutput(@out);
    }

    private void FinishEncode(IList<object> @out)
    {
        if (encoder.Finish())
        {
            FetchEncoderOutput(@out);
        }
        encoder = null;
    }

    private void FetchEncoderOutput(IList<object> @out)
    {
        for (;;)
        {
            ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
            if (buf == null)
            {
                break;
            }
            if (!buf.IsReadable())
            {
                buf.Release();
                continue;
            }
            @out.Add(new DefaultHttpContent(buf));
        }
    }

    public sealed class Result
    {
        private readonly string targetContentEncoding;
        private readonly EmbeddedChannel contentEncoder;

        public Result(string targetContentEncoding, EmbeddedChannel contentEncoder)
        {
            this.targetContentEncoding = ObjectUtil.CheckNotNull(targetContentEncoding, "targetContentEncoding");
            this.contentEncoder = ObjectUtil.CheckNotNull(contentEncoder, "contentEncoder");
        }

        public string TargetContentEncoding()
        {
            return targetContentEncoding;
        }

        public EmbeddedChannel ContentEncoder()
        {
            return contentEncoder;
        }
    }
}
