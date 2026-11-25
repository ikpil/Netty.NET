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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Decodes the content of the received {@link HttpRequest} and {@link HttpContent}.
 * The original content is replaced with the new content decoded by the
 * {@link EmbeddedChannel}, which is created by {@link #newContentDecoder(String)}.
 * Once decoding is finished, the value of the <tt>'Content-Encoding'</tt>
 * header is set to the target content encoding, as returned by {@link #getTargetContentEncoding(String)}.
 * Also, the <tt>'Content-Length'</tt> header is updated to the length of the
 * decoded content.  If the content encoding of the original is not supported
 * by the decoder, {@link #newContentDecoder(String)} should return {@code null}
 * so that no decoding occurs (i.e. pass-through).
 * <p>
 * Please note that this is an abstract class.  You have to extend this class
 * and implement {@link #newContentDecoder(String)} properly to make this class
 * functional.  For example, refer to the source code of {@link HttpContentDecompressor}.
 * <p>
 * This handler must be placed after {@link HttpObjectDecoder} in the pipeline
 * so that this handler can intercept HTTP requests after {@link HttpObjectDecoder}
 * converts {@link ByteBuf}s into HTTP requests.
 */
// CLR: MessageToMessageDecoder<HttpObject> with HttpObject.class is the codec-base
// MessageToMessageDecoder<IHttpObject> with typeof(IHttpObject) (docs/codec-base-porting.md, Type matching). The
// package-private IDENTITY is internal. String.trim() is HttpUtil.TrimJava (the chars up to U+0020, as Java trims).
// The inner class ByteBufForwarder writes HttpContentDecoder.this.needRead through the decoder it receives, and
// isSharable() is the IsSharable property of ChannelHandlerAdapter (docs/transport-porting.md). The String
// targetContentEncoding that Java compares as a CharSequence is wrapped in StringCharSequence for
// AsciiString.contentEquals. message.getClass().getName() is Type.FullName, catch (Throwable) is catch (Exception) and
// assert is Debug.Assert (docs/codec-base-porting.md). The protected ctx field keeps its Java name.
public abstract class HttpContentDecoder : MessageToMessageDecoder<IHttpObject>
{

    internal static readonly string IDENTITY = HttpHeaderValues.IDENTITY.ToString();

    protected IChannelHandlerContext ctx;
    private EmbeddedChannel decoder;
    private bool continueResponse;
    private bool needRead = true;
    private ByteBufForwarder forwarder;

    public HttpContentDecoder() : base(typeof(IHttpObject))
    {
    }

    protected override void Decode(IChannelHandlerContext ctx, IHttpObject msg, IList<object> @out)
    {
        needRead = true;
        if (msg is IHttpResponse && ((IHttpResponse) msg).Status().Code() == 100)
        {

            if (!(msg is ILastHttpContent))
            {
                continueResponse = true;
            }
            // 100-continue response must be passed through.
            needRead = false;
            ctx.FireChannelRead(ReferenceCountUtil.Retain(msg));
            return;
        }

        if (continueResponse)
        {
            if (msg is ILastHttpContent)
            {
                continueResponse = false;
            }
            // 100-continue response must be passed through.
            needRead = false;
            ctx.FireChannelRead(ReferenceCountUtil.Retain(msg));
            return;
        }

        if (msg is IHttpMessage)
        {
            Cleanup();
            IHttpMessage message = (IHttpMessage) msg;
            HttpHeaders headers = message.Headers();

            // Determine the content encoding.
            string contentEncoding = headers.Get(HttpHeaderNames.CONTENT_ENCODING);
            if (contentEncoding != null)
            {
                contentEncoding = HttpUtil.TrimJava(contentEncoding);
            }
            else
            {
                string transferEncoding = headers.Get(HttpHeaderNames.TRANSFER_ENCODING);
                if (transferEncoding != null)
                {
                    int idx = transferEncoding.IndexOf(',');
                    if (idx != -1)
                    {
                        contentEncoding = HttpUtil.TrimJava(transferEncoding.Substring(0, idx));
                    }
                    else
                    {
                        contentEncoding = HttpUtil.TrimJava(transferEncoding);
                    }
                }
                else
                {
                    contentEncoding = IDENTITY;
                }
            }
            decoder = NewContentDecoder(contentEncoding);

            if (decoder == null)
            {
                if (message is IHttpContent)
                {
                    ((IHttpContent) message).Retain();
                }
                needRead = false;
                ctx.FireChannelRead(message);
                return;
            }
            decoder.Pipeline.AddLast(forwarder);
            // Remove content-length header:
            // the correct value can be set only after all chunks are processed/decoded.
            // If buffering is not an issue, add HttpObjectAggregator down the chain, it will set the header.
            // Otherwise, rely on LastHttpContent message.
            if (headers.Contains(HttpHeaderNames.CONTENT_LENGTH))
            {
                headers.Remove(HttpHeaderNames.CONTENT_LENGTH);
                headers.Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
            }
            // Either it is already chunked or EOF terminated.
            // See https://github.com/netty/netty/issues/5892

            // set new content encoding,
            string targetContentEncoding = GetTargetContentEncoding(contentEncoding);
            if (HttpHeaderValues.IDENTITY.ContentEquals(new StringCharSequence(targetContentEncoding)))
            {
                // Do NOT set the 'Content-Encoding' header if the target encoding is 'identity'
                // as per: https://tools.ietf.org/html/rfc2616#section-14.11
                headers.Remove(HttpHeaderNames.CONTENT_ENCODING);
            }
            else
            {
                headers.Set(HttpHeaderNames.CONTENT_ENCODING, targetContentEncoding);
            }

            if (message is IHttpContent)
            {
                // If message is a full request or response object (headers + data), don't copy data part into out.
                // Output headers only; data part will be decoded below.
                // Note: "copy" object must not be an instance of LastHttpContent class,
                // as this would (erroneously) indicate the end of the HttpMessage to other handlers.
                IHttpMessage copy;
                if (message is IHttpRequest)
                {
                    IHttpRequest r = (IHttpRequest) message; // HttpRequest or FullHttpRequest
                    copy = new DefaultHttpRequest(r.ProtocolVersion(), r.Method(), r.Uri());
                }
                else if (message is IHttpResponse)
                {
                    IHttpResponse r = (IHttpResponse) message; // HttpResponse or FullHttpResponse
                    copy = new DefaultHttpResponse(r.ProtocolVersion(), r.Status());
                }
                else
                {
                    throw new CodecException("Object of class " + message.GetType().FullName +
                                             " is not an HttpRequest or HttpResponse");
                }
                copy.Headers().Set(message.Headers());
                copy.SetDecoderResult(message.DecoderResult());
                needRead = false;
                ctx.FireChannelRead(copy);
            }
            else
            {
                needRead = false;
                ctx.FireChannelRead(message);
            }
        }

        if (msg is IHttpContent)
        {
            IHttpContent c = (IHttpContent) msg;
            if (decoder == null)
            {
                needRead = false;
                ctx.FireChannelRead(c.Retain());
            }
            else
            {
                // call retain here as it will call release after its written to the channel
                decoder.WriteInbound(c.Content.Retain());

                if (c is ILastHttpContent)
                {
                    bool notEmpty = decoder.Finish();
                    decoder = null;
                    Debug.Assert(!notEmpty);
                    ILastHttpContent last = (ILastHttpContent) c;
                    // Generate an additional chunk if the decoder produced
                    // the last product on closure,
                    HttpHeaders headers = last.TrailingHeaders();
                    needRead = false;
                    if (headers.IsEmpty())
                    {
                        ctx.FireChannelRead(ILastHttpContent.EMPTY_LAST_CONTENT);
                    }
                    else
                    {
                        ctx.FireChannelRead(new ComposedLastHttpContent(headers, DecoderResult.SUCCESS));
                    }
                }
            }
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        bool needRead = this.needRead;
        this.needRead = true;

        try
        {
            ctx.FireChannelReadComplete();
        }
        finally
        {
            if (needRead && !ctx.Channel.Config.IsAutoRead())
            {
                ctx.Read();
            }
        }
    }

    /**
     * Returns a new {@link EmbeddedChannel} that decodes the HTTP message
     * content encoded in the specified <tt>contentEncoding</tt>.
     *
     * @param contentEncoding the value of the {@code "Content-Encoding"} header
     * @return a new {@link EmbeddedChannel} if the specified encoding is supported.
     *         {@code null} otherwise (alternatively, you can throw an exception
     *         to block unknown encoding).
     */
    protected abstract EmbeddedChannel NewContentDecoder(string contentEncoding);

    /**
     * Returns the expected content encoding of the decoded content.
     * This getMethod returns {@code "identity"} by default, which is the case for
     * most decoders.
     *
     * @param contentEncoding the value of the {@code "Content-Encoding"} header
     * @return the expected content encoding of the new content
     */
    protected virtual string GetTargetContentEncoding(
            string contentEncoding)
    {
        return IDENTITY;
    }

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

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
        forwarder = new ByteBufForwarder(this, ctx);
        base.HandlerAdded(ctx);
    }

    private void Cleanup()
    {
        if (decoder != null)
        {
            // Clean-up the previous decoder if not cleaned up correctly.
            bool nonEmpty = decoder.FinishAndReleaseAll();
            decoder = null;
            Debug.Assert(!nonEmpty);
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

    private sealed class ByteBufForwarder : ChannelInboundHandlerAdapter
    {

        private readonly HttpContentDecoder contentDecoder;
        private readonly IChannelHandlerContext targetCtx;

        internal ByteBufForwarder(HttpContentDecoder contentDecoder, IChannelHandlerContext targetCtx)
        {
            this.contentDecoder = contentDecoder;
            this.targetCtx = targetCtx;
        }

        // We need to mark the handler as sharable as we will add it to every EmbeddedChannel that is
        // generated.
        public override bool IsSharable => true;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf buf = (ByteBuf) msg;
            if (!buf.IsReadable())
            {
                buf.Release();
                return;
            }
            contentDecoder.needRead = false;
            targetCtx.FireChannelRead(new DefaultHttpContent(buf));
        }
    }
}
