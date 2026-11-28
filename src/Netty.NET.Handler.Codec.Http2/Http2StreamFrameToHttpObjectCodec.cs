/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * This handler converts from {@link Http2StreamFrame} to {@link HttpObject},
 * and back. It can be used as an adapter in conjunction with {@link
 * Http2MultiplexCodec} to make http/2 connections backward-compatible with
 * {@link ChannelHandler}s expecting {@link HttpObject}
 *
 * For simplicity, it converts to chunked encoding unless the entire stream
 * is a single header.
 */
// CLR: @Sharable is the Transport's [Sharable] (src/Netty.NET.Transport/Channels/IChannelHandler.cs), as codec-http's
// WebSocket00FrameEncoder. MessageToMessageCodec<Http2StreamFrame, HttpObject> is the codec-base
// MessageToMessageCodec<IHttp2StreamFrame, IHttpObject>, and super(Http2StreamFrame.class, HttpObject.class) passes the
// Types (docs/codec-base-porting.md, Type matching); List<Object> out is IList<object> @out (docs/codec-base-porting.md,
// Lists). The codec-http interfaces take the I prefix and their getters are methods, ByteBufHolder.content() is the
// Content property (docs/codec-http-porting.md, Shared CLR adaptations). The @Deprecated
// DefaultLastHttpContent(ByteBuf, boolean), whose @deprecated text names DefaultLastHttpContent(ByteBuf), is not ported
// in codec-http (src/Netty.NET.Handler.Codec.Http/DefaultLastHttpContent.cs), so decode calls the HttpHeadersFactory
// constructor with trailersFactory().withValidation(validateHeaders), which is what the deprecated constructor does in
// Java, as HttpConversionUtil does for the message constructors (src/Netty.NET.Handler.Codec.Http2/HttpConversionUtil.cs).
// Attribute<HttpScheme> is the Common IAttribute<HttpScheme> and AttributeKey.valueOf(Class, String) is
// AttributeKey<HttpScheme>.ValueOf(Type, string); ctx.channel(), ch.parent() and ch.pipeline() are the Transport's
// Channel, Parent and Pipeline properties and pipeline().get(SslHandler.class) is Pipeline.Get<SslHandler>()
// (docs/transport-porting.md, Decisions). Http2StreamChannel is the IHttp2StreamChannel interface. "throws Exception"
// and "throws Http2Exception" have no C# counterpart. The public class is not sealed, so the protected isSsl is
// virtual.
[Sharable]
public class Http2StreamFrameToHttpObjectCodec : MessageToMessageCodec<IHttp2StreamFrame, IHttpObject>
{

    private static readonly AttributeKey<HttpScheme> SCHEME_ATTR_KEY =
        AttributeKey<HttpScheme>.ValueOf(typeof(HttpScheme), "STREAMFRAMECODEC_SCHEME");

    private readonly bool isServer;
    private readonly bool validateHeaders;

    public Http2StreamFrameToHttpObjectCodec(bool isServer,
                                             bool validateHeaders) : base(typeof(IHttp2StreamFrame), typeof(IHttpObject))
    {
        this.isServer = isServer;
        this.validateHeaders = validateHeaders;
    }

    public Http2StreamFrameToHttpObjectCodec(bool isServer) : this(isServer, true)
    {
    }

    public override bool AcceptInboundMessage(object msg)
    {
        return msg is IHttp2HeadersFrame || msg is IHttp2DataFrame;
    }

    protected override void Decode(IChannelHandlerContext ctx, IHttp2StreamFrame frame, IList<object> @out)
    {
        if (frame is IHttp2HeadersFrame)
        {
            IHttp2HeadersFrame headersFrame = (IHttp2HeadersFrame) frame;
            IHttp2Headers headers = headersFrame.Headers();
            IHttp2FrameStream stream = headersFrame.Stream();
            int id = stream == null ? 0 : stream.Id();

            ICharSequence status = headers.Status();

            // 1xx response (excluding 101) is a special case where Http2HeadersFrame#isEndStream=false
            // but we need to decode it as a FullHttpResponse to play nice with HttpObjectAggregator.
            if (null != status && IsInformationalResponseHeaderFrame(status))
            {
                IFullHttpMessage fullMsg = NewFullMessage(id, headers, ctx.Alloc());
                @out.Add(fullMsg);
                return;
            }

            if (headersFrame.IsEndStream())
            {
                if (headers.Method() == null && status == null)
                {
                    // CLR: new DefaultLastHttpContent(EMPTY_BUFFER, validateHeaders) (deprecated, see the class comment).
                    ILastHttpContent last = new DefaultLastHttpContent(Unpooled.EmptyBuffer,
                            DefaultHttpHeadersFactory.TrailersFactory().WithValidation(validateHeaders));
                    HttpConversionUtil.AddHttp2ToHttpHeaders(id, headers, last.TrailingHeaders(),
                                                             HttpVersion.HTTP_1_1, true, true);
                    @out.Add(last);
                }
                else
                {
                    IFullHttpMessage full = NewFullMessage(id, headers, ctx.Alloc());
                    @out.Add(full);
                }
            }
            else
            {
                IHttpMessage req = NewMessage(id, headers);
                if ((status == null || !IsContentAlwaysEmpty(status)) && !HttpUtil.IsContentLengthSet(req))
                {
                    req.Headers().Add(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
                }

                @out.Add(req);
            }
        }
        else if (frame is IHttp2DataFrame)
        {
            IHttp2DataFrame dataFrame = (IHttp2DataFrame) frame;
            if (dataFrame.IsEndStream())
            {
                // CLR: new DefaultLastHttpContent(content, validateHeaders) (deprecated, see the class comment).
                @out.Add(new DefaultLastHttpContent(dataFrame.Content.Retain(),
                        DefaultHttpHeadersFactory.TrailersFactory().WithValidation(validateHeaders)));
            }
            else
            {
                @out.Add(new DefaultHttpContent(dataFrame.Content.Retain()));
            }
        }
    }

    private void EncodeLastContent(ILastHttpContent last, IList<object> @out)
    {
        bool needFiller = !(last is IFullHttpMessage) && last.TrailingHeaders().IsEmpty();
        if (last.Content.IsReadable() || needFiller)
        {
            @out.Add(new DefaultHttp2DataFrame(last.Content.Retain(), last.TrailingHeaders().IsEmpty()));
        }

        if (!last.TrailingHeaders().IsEmpty())
        {
            IHttp2Headers headers = HttpConversionUtil.ToHttp2Headers(last.TrailingHeaders(), validateHeaders);
            @out.Add(new DefaultHttp2HeadersFrame(headers, true));
        }
    }

    /**
     * Encode from an {@link HttpObject} to an {@link Http2StreamFrame}. This method will
     * be called for each written message that can be handled by this encoder.
     *
     * NOTE: 100-Continue responses that are NOT {@link FullHttpResponse} will be rejected.
     *
     * @param ctx           the {@link ChannelHandlerContext} which this handler belongs to
     * @param obj           the {@link HttpObject} message to encode
     * @param out           the {@link List} into which the encoded msg should be added
     *                      needs to do some kind of aggregation
     * @throws Exception    is thrown if an error occurs
     */
    protected override void Encode(IChannelHandlerContext ctx, IHttpObject obj, IList<object> @out)
    {
        // 1xx (excluding 101) is typically a FullHttpResponse, but the decoded
        // Http2HeadersFrame should not be marked as endStream=true
        if (obj is IHttpResponse)
        {
            IHttpResponse res = (IHttpResponse) obj;
            HttpResponseStatus status = res.Status();
            int code = status.Code();
            HttpStatusClass statusClass = status.CodeClass();
            // An informational response using a 1xx status code other than 101 is
            // transmitted as a HEADERS frame
            if (statusClass == HttpStatusClass.INFORMATIONAL && code != 101)
            {
                if (res is IFullHttpResponse)
                {
                    IHttp2Headers headers = ToHttp2Headers(ctx, res);
                    @out.Add(new DefaultHttp2HeadersFrame(headers, false));
                    return;
                }
                else
                {
                    throw new EncoderException(status + " must be a FullHttpResponse");
                }
            }
        }

        if (obj is IHttpMessage)
        {
            IHttp2Headers headers = ToHttp2Headers(ctx, (IHttpMessage) obj);
            bool noMoreFrames = false;

            if (obj is IFullHttpMessage)
            {
                IFullHttpMessage full = (IFullHttpMessage) obj;
                noMoreFrames = !full.Content.IsReadable() && full.TrailingHeaders().IsEmpty();
            }

            @out.Add(new DefaultHttp2HeadersFrame(headers, noMoreFrames));
        }

        if (obj is ILastHttpContent)
        {
            ILastHttpContent last = (ILastHttpContent) obj;
            EncodeLastContent(last, @out);
        }
        else if (obj is IHttpContent)
        {
            IHttpContent cont = (IHttpContent) obj;
            @out.Add(new DefaultHttp2DataFrame(cont.Content.Retain(), false));
        }
    }

    private IHttp2Headers ToHttp2Headers(IChannelHandlerContext ctx, IHttpMessage msg)
    {
        if (msg is IHttpRequest)
        {
            msg.Headers().Set(
                    HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(),
                    ConnectionScheme(ctx));
        }

        return HttpConversionUtil.ToHttp2Headers(msg, validateHeaders);
    }

    private IHttpMessage NewMessage(int id,
                                    IHttp2Headers headers)
    {
        return isServer ?
                HttpConversionUtil.ToHttpRequest(id, headers, validateHeaders) :
                HttpConversionUtil.ToHttpResponse(id, headers, validateHeaders);
    }

    private IFullHttpMessage NewFullMessage(int id,
                                            IHttp2Headers headers,
                                            IByteBufAllocator alloc)
    {
        return isServer ?
                HttpConversionUtil.ToFullHttpRequest(id, headers, alloc, validateHeaders) :
                HttpConversionUtil.ToFullHttpResponse(id, headers, alloc, validateHeaders);
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        base.HandlerAdded(ctx);

        // this handler is typically used on an Http2StreamChannel. At this
        // stage, ssl handshake should've been established. checking for the
        // presence of SslHandler in the parent's channel pipeline to
        // determine the HTTP scheme should suffice, even for the case where
        // SniHandler is used.
        IAttribute<HttpScheme> schemeAttribute = ConnectionSchemeAttribute(ctx);
        if (schemeAttribute.Get() == null)
        {
            HttpScheme scheme = IsSsl(ctx) ? HttpScheme.HTTPS : HttpScheme.HTTP;
            schemeAttribute.Set(scheme);
        }
    }

    protected virtual bool IsSsl(IChannelHandlerContext ctx)
    {
        IChannel connChannel = ConnectionChannel(ctx);
        return null != connChannel.Pipeline.Get<SslHandler>();
    }

    private static HttpScheme ConnectionScheme(IChannelHandlerContext ctx)
    {
        HttpScheme scheme = ConnectionSchemeAttribute(ctx).Get();
        return scheme == null ? HttpScheme.HTTP : scheme;
    }

    private static IAttribute<HttpScheme> ConnectionSchemeAttribute(IChannelHandlerContext ctx)
    {
        IChannel ch = ConnectionChannel(ctx);
        return ch.Attr(SCHEME_ATTR_KEY);
    }

    private static IChannel ConnectionChannel(IChannelHandlerContext ctx)
    {
        IChannel ch = ctx.Channel;
        return ch is IHttp2StreamChannel ? ch.Parent : ch;
    }

    /**
     *    An informational response using a 1xx status code other than 101 is
     *    transmitted as a HEADERS frame
     */
    private static bool IsInformationalResponseHeaderFrame(ICharSequence status)
    {
        if (status.Length() == 3)
        {
            char char0 = status.CharAt(0);
            char char1 = status.CharAt(1);
            char char2 = status.CharAt(2);
            return char0 == '1'
                && char1 >= '0' && char1 <= '9'
                && char2 >= '0' && char2 <= '9' && char2 != '1';
        }

        return false;
    }

    /*
     * https://datatracker.ietf.org/doc/html/rfc9113#section-8.1.1
     * '204' or '304' responses contain no content
     */
    private static bool IsContentAlwaysEmpty(ICharSequence status)
    {
        if (status.Length() == 3)
        {
            char char0 = status.CharAt(0);
            char char1 = status.CharAt(1);
            char char2 = status.CharAt(2);
            return (char0 == '2' || char0 == '3')
                && char1 == '0'
                && char2 == '4';
        }

        return false;
    }
}
