/*
 * Copyright 2013 The Netty Project
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
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Spdy.ISpdyHeaders.HttpNames;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * Decodes {@link SpdySynStreamFrame}s, {@link SpdySynReplyFrame}s,
 * and {@link SpdyDataFrame}s into {@link FullHttpRequest}s and {@link FullHttpResponse}s.
 */
// CLR: MessageToMessageDecoder<SpdyFrame> is the codec-base MessageToMessageDecoder<ISpdyFrame> with a typeof(...)
// matcher; the HTTP message types are the core I-prefixed interfaces and HttpHeadersFactory is IHttpHeadersFactory.
// Map<Integer, FullHttpMessage> is IDictionary<int, IFullHttpMessage>: put/get/remove return the previous value or null
// through TryGetValue and Remove(key, out value). IntObjectMap<Long> is Dictionary<int, long> (owner decision
// 2026-11-19 for io.netty.util.collection). The two @Deprecated constructors with validateHeaders, whose @deprecated
// texts name SpdyHttpDecoder(SpdyVersion, int, Map, HttpHeadersFactory, HttpHeadersFactory), are not ported (owner
// decision 2026-11-16). Java protected constructors are protected internal, because SpdyHttpCodec (same package) calls
// them and the tests construct the decoder with them (InternalsVisibleTo). catch (Throwable) is catch (Exception); the
// writeAndFlush(...) futures are discarded Tasks. Map.Entry<CharSequence, CharSequence> is the codec-base
// IEntry<ICharSequence, ICharSequence>. setInt(PRIORITY, priority()) widens the CLR byte, the same value 0-7 as the
// Java byte (setPriority rejects other values). Numbers in exception texts use the invariant culture.
public class SpdyHttpDecoder : MessageToMessageDecoder<ISpdyFrame>
{

    // Same default as SpdyFrameCodec's maxHeaderSize.
    private const int DEFAULT_MAX_HEADER_SIZE = 16384;
    private readonly int spdyVersion;
    private readonly int maxHeaderSize;
    private readonly int maxContentLength;
    private readonly IDictionary<int, IFullHttpMessage> messageMap;
    private readonly Dictionary<int, long> headerSizeMap = new Dictionary<int, long>();
    private readonly IHttpHeadersFactory headersFactory;
    private readonly IHttpHeadersFactory trailersFactory;

    /**
     * Creates a new instance.
     *
     * @param version the protocol version
     * @param maxContentLength the maximum length of the message content.
     *        If the length of the message content exceeds this value,
     *        a {@link TooLongFrameException} will be raised.
     */
    public SpdyHttpDecoder(SpdyVersion version, int maxContentLength)
        : this(version, maxContentLength, new Dictionary<int, IFullHttpMessage>(),
                DefaultHttpHeadersFactory.HeadersFactory(), DefaultHttpHeadersFactory.TrailersFactory())
    {
    }

    /**
     * Creates a new instance with the specified parameters.
     *
     * @param version the protocol version
     * @param maxContentLength the maximum length of the message content.
     *        If the length of the message content exceeds this value,
     *        a {@link TooLongFrameException} will be raised.
     * @param messageMap the {@link Map} used to hold partially received messages.
     */
    protected internal SpdyHttpDecoder(SpdyVersion version, int maxContentLength,
                                       IDictionary<int, IFullHttpMessage> messageMap)
        : this(version, maxContentLength, messageMap,
                DefaultHttpHeadersFactory.HeadersFactory(), DefaultHttpHeadersFactory.TrailersFactory())
    {
    }

    /**
     * Creates a new instance with the specified parameters.
     *
     * @param version the protocol version
     * @param maxContentLength the maximum length of the message content.
     *        If the length of the message content exceeds this value,
     *        a {@link TooLongFrameException} will be raised.
     * @param messageMap the {@link Map} used to hold partially received messages.
     * @param headersFactory The factory used for creating HTTP headers
     * @param trailersFactory The factory used for creating HTTP trailers.
     */
    protected internal SpdyHttpDecoder(SpdyVersion version, int maxContentLength,
            IDictionary<int, IFullHttpMessage> messageMap, IHttpHeadersFactory headersFactory,
            IHttpHeadersFactory trailersFactory)
        : this(version, DEFAULT_MAX_HEADER_SIZE, maxContentLength, messageMap, headersFactory, trailersFactory)
    {
    }

    /**
     * Creates a new instance with the specified parameters.
     *
     * @param version the protocol version
     * @param maxHeaderSize the maximum length of all headers.  If the sum of the length of each
     *        header exceeds this value, a {@link TooLongHttpHeaderException} will be raised.
     * @param maxContentLength the maximum length of the message content.
     *        If the length of the message content exceeds this value,
     *        a {@link TooLongFrameException} will be raised.
     * @param messageMap the {@link Map} used to hold partially received messages.
     * @param headersFactory The factory used for creating HTTP headers
     * @param trailersFactory The factory used for creating HTTP trailers.
     */
    protected internal SpdyHttpDecoder(SpdyVersion version, int maxHeaderSize, int maxContentLength,
            IDictionary<int, IFullHttpMessage> messageMap, IHttpHeadersFactory headersFactory,
            IHttpHeadersFactory trailersFactory)
        : base(typeof(ISpdyFrame))
    {
        spdyVersion = ObjectUtil.CheckNotNull(version, "version").Version();
        this.maxHeaderSize = CheckPositive(maxHeaderSize, "maxHeaderSize");
        this.maxContentLength = CheckPositive(maxContentLength, "maxContentLength");
        this.messageMap = messageMap;
        this.headersFactory = headersFactory;
        this.trailersFactory = trailersFactory;
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        // Release any outstanding messages from the map
        foreach (KeyValuePair<int, IFullHttpMessage> entry in messageMap)
        {
            ReferenceCountUtil.SafeRelease(entry.Value);
        }
        messageMap.Clear();
        headerSizeMap.Clear();
        base.HandlerRemoved(ctx);
    }

    protected virtual IFullHttpMessage PutMessage(int streamId, IFullHttpMessage message)
    {
        messageMap.TryGetValue(streamId, out IFullHttpMessage old);
        messageMap[streamId] = message;
        return old;
    }

    protected virtual IFullHttpMessage GetMessage(int streamId)
    {
        messageMap.TryGetValue(streamId, out IFullHttpMessage message);
        return message;
    }

    protected virtual IFullHttpMessage RemoveMessage(int streamId)
    {
        headerSizeMap.Remove(streamId);
        messageMap.Remove(streamId, out IFullHttpMessage message);
        return message;
    }

    protected override void Decode(IChannelHandlerContext ctx, ISpdyFrame msg, IList<object> @out)
    {
        if (msg is ISpdySynStreamFrame)
        {

            // HTTP requests/responses are mapped one-to-one to SPDY streams.
            ISpdySynStreamFrame spdySynStreamFrame = (ISpdySynStreamFrame) msg;
            int streamId = spdySynStreamFrame.StreamId();

            if (SpdyCodecUtil.IsServerId(streamId))
            {
                // SYN_STREAM frames initiated by the server are pushed resources
                int associatedToStreamId = spdySynStreamFrame.AssociatedStreamId();

                // If a client receives a SYN_STREAM with an Associated-To-Stream-ID of 0
                // it must reply with a RST_STREAM with error code INVALID_STREAM.
                if (associatedToStreamId == 0)
                {
                    ISpdyRstStreamFrame spdyRstStreamFrame =
                        new DefaultSpdyRstStreamFrame(streamId, SpdyStreamStatus.INVALID_STREAM);
                    _ = ctx.WriteAndFlushAsync(spdyRstStreamFrame);
                    return;
                }

                // If a client receives a SYN_STREAM with isLast set,
                // reply with a RST_STREAM with error code PROTOCOL_ERROR
                // (we only support pushed resources divided into two header blocks).
                if (spdySynStreamFrame.IsLast())
                {
                    ISpdyRstStreamFrame spdyRstStreamFrame =
                        new DefaultSpdyRstStreamFrame(streamId, SpdyStreamStatus.PROTOCOL_ERROR);
                    _ = ctx.WriteAndFlushAsync(spdyRstStreamFrame);
                    return;
                }

                // If a client receives a response with a truncated header block,
                // reply with a RST_STREAM with error code INTERNAL_ERROR.
                if (spdySynStreamFrame.IsTruncated())
                {
                    ISpdyRstStreamFrame spdyRstStreamFrame =
                    new DefaultSpdyRstStreamFrame(streamId, SpdyStreamStatus.INTERNAL_ERROR);
                    _ = ctx.WriteAndFlushAsync(spdyRstStreamFrame);
                    return;
                }

                IFullHttpRequest httpRequestWithEntity = null;
                try
                {
                    httpRequestWithEntity = CreateHttpRequest(spdySynStreamFrame, ctx.Alloc());

                    // Set the Stream-ID, Associated-To-Stream-ID, and Priority as headers
                    httpRequestWithEntity.Headers().SetInt(SpdyHttpHeaders.Names.STREAM_ID, streamId);
                    httpRequestWithEntity.Headers().SetInt(SpdyHttpHeaders.Names.ASSOCIATED_TO_STREAM_ID,
                            associatedToStreamId);
                    httpRequestWithEntity.Headers().SetInt(SpdyHttpHeaders.Names.PRIORITY,
                            spdySynStreamFrame.Priority());

                    @out.Add(httpRequestWithEntity);
                    httpRequestWithEntity = null;
                }
                catch (Exception)
                {
                    if (httpRequestWithEntity != null)
                    {
                        httpRequestWithEntity.Release();
                    }
                    ISpdyRstStreamFrame spdyRstStreamFrame =
                        new DefaultSpdyRstStreamFrame(streamId, SpdyStreamStatus.PROTOCOL_ERROR);
                    _ = ctx.WriteAndFlushAsync(spdyRstStreamFrame);
                }
            }
            else
            {
                // SYN_STREAM frames initiated by the client are HTTP requests

                // If a client sends a request with a truncated header block, the server must
                // reply with an HTTP 431 REQUEST HEADER FIELDS TOO LARGE reply.
                if (spdySynStreamFrame.IsTruncated())
                {
                    ISpdySynReplyFrame spdySynReplyFrame = new DefaultSpdySynReplyFrame(streamId);
                    spdySynReplyFrame.SetLast(true);
                    ISpdyHeaders frameHeaders = spdySynReplyFrame.Headers();
                    frameHeaders.SetInt(STATUS, HttpResponseStatus.REQUEST_HEADER_FIELDS_TOO_LARGE.Code());
                    frameHeaders.SetObject(VERSION, HttpVersion.HTTP_1_0);
                    _ = ctx.WriteAndFlushAsync(spdySynReplyFrame);
                    return;
                }

                IFullHttpRequest httpRequestWithEntity = null;
                try
                {
                    httpRequestWithEntity = CreateHttpRequest(spdySynStreamFrame, ctx.Alloc());

                    // Set the Stream-ID as a header
                    httpRequestWithEntity.Headers().SetInt(SpdyHttpHeaders.Names.STREAM_ID, streamId);

                    if (spdySynStreamFrame.IsLast())
                    {
                        @out.Add(httpRequestWithEntity);
                        httpRequestWithEntity = null;
                    }
                    else
                    {
                        // Request body will follow in a series of Data Frames
                        IFullHttpMessage old = PutMessage(streamId, httpRequestWithEntity);
                        httpRequestWithEntity = null;
                        if (old != null)
                        {
                            old.Release();
                        }
                    }
                }
                catch (Exception)
                {
                    if (httpRequestWithEntity != null)
                    {
                        httpRequestWithEntity.Release();
                    }
                    // If a client sends a SYN_STREAM without all of the getMethod, url (host and path),
                    // scheme, and version headers the server must reply with an HTTP 400 BAD REQUEST reply.
                    // Also sends HTTP 400 BAD REQUEST reply if header name/value pairs are invalid
                    ISpdySynReplyFrame spdySynReplyFrame = new DefaultSpdySynReplyFrame(streamId);
                    spdySynReplyFrame.SetLast(true);
                    ISpdyHeaders frameHeaders = spdySynReplyFrame.Headers();
                    frameHeaders.SetInt(STATUS, HttpResponseStatus.BAD_REQUEST.Code());
                    frameHeaders.SetObject(VERSION, HttpVersion.HTTP_1_0);
                    _ = ctx.WriteAndFlushAsync(spdySynReplyFrame);
                }
            }

        }
        else if (msg is ISpdySynReplyFrame)
        {

            ISpdySynReplyFrame spdySynReplyFrame = (ISpdySynReplyFrame) msg;
            int streamId = spdySynReplyFrame.StreamId();

            // If a client receives a SYN_REPLY with a truncated header block,
            // reply with a RST_STREAM frame with error code INTERNAL_ERROR.
            if (spdySynReplyFrame.IsTruncated())
            {
                ISpdyRstStreamFrame spdyRstStreamFrame =
                        new DefaultSpdyRstStreamFrame(streamId, SpdyStreamStatus.INTERNAL_ERROR);
                _ = ctx.WriteAndFlushAsync(spdyRstStreamFrame);
                return;
            }

            IFullHttpResponse httpResponseWithEntity = null;
            try
            {
                httpResponseWithEntity = CreateHttpResponse(spdySynReplyFrame, ctx.Alloc());

                // Set the Stream-ID as a header
                httpResponseWithEntity.Headers().SetInt(SpdyHttpHeaders.Names.STREAM_ID, streamId);

                if (spdySynReplyFrame.IsLast())
                {
                    HttpUtil.SetContentLength(httpResponseWithEntity, 0);
                    @out.Add(httpResponseWithEntity);
                    httpResponseWithEntity = null;
                }
                else
                {
                    // Response body will follow in a series of Data Frames
                    IFullHttpMessage old = PutMessage(streamId, httpResponseWithEntity);
                    httpResponseWithEntity = null;
                    if (old != null)
                    {
                        old.Release();
                    }
                }
            }
            catch (Exception)
            {
                if (httpResponseWithEntity != null)
                {
                    httpResponseWithEntity.Release();
                }
                // If a client receives a SYN_REPLY without valid getStatus and version headers
                // the client must reply with a RST_STREAM frame indicating a PROTOCOL_ERROR
                ISpdyRstStreamFrame spdyRstStreamFrame =
                    new DefaultSpdyRstStreamFrame(streamId, SpdyStreamStatus.PROTOCOL_ERROR);
                _ = ctx.WriteAndFlushAsync(spdyRstStreamFrame);
            }

        }
        else if (msg is ISpdyHeadersFrame)
        {

            ISpdyHeadersFrame spdyHeadersFrame = (ISpdyHeadersFrame) msg;
            int streamId = spdyHeadersFrame.StreamId();
            IFullHttpMessage fullHttpMessage = GetMessage(streamId);

            if (fullHttpMessage == null)
            {
                // HEADERS frames may initiate a pushed response
                if (SpdyCodecUtil.IsServerId(streamId))
                {

                    // If a client receives a HEADERS with a truncated header block,
                    // reply with a RST_STREAM frame with error code INTERNAL_ERROR.
                    if (spdyHeadersFrame.IsTruncated())
                    {
                        ISpdyRstStreamFrame spdyRstStreamFrame =
                            new DefaultSpdyRstStreamFrame(streamId, SpdyStreamStatus.INTERNAL_ERROR);
                        _ = ctx.WriteAndFlushAsync(spdyRstStreamFrame);
                        return;
                    }

                    try
                    {
                        fullHttpMessage = CreateHttpResponse(spdyHeadersFrame, ctx.Alloc());

                        // Set the Stream-ID as a header
                        fullHttpMessage.Headers().SetInt(SpdyHttpHeaders.Names.STREAM_ID, streamId);

                        if (spdyHeadersFrame.IsLast())
                        {
                            HttpUtil.SetContentLength(fullHttpMessage, 0);
                            @out.Add(fullHttpMessage);
                            fullHttpMessage = null;
                        }
                        else
                        {
                            // Response body will follow in a series of Data Frames
                            IFullHttpMessage old = PutMessage(streamId, fullHttpMessage);
                            fullHttpMessage = null;
                            if (old != null)
                            {
                                old.Release();
                            }
                        }
                    }
                    catch (Exception)
                    {
                        if (fullHttpMessage != null)
                        {
                            fullHttpMessage.Release();
                        }
                        // If a client receives a SYN_REPLY without valid getStatus and version headers
                        // the client must reply with a RST_STREAM frame indicating a PROTOCOL_ERROR
                        ISpdyRstStreamFrame spdyRstStreamFrame =
                            new DefaultSpdyRstStreamFrame(streamId, SpdyStreamStatus.PROTOCOL_ERROR);
                        _ = ctx.WriteAndFlushAsync(spdyRstStreamFrame);
                    }
                }
                return;
            }

            // Ignore trailers in a truncated HEADERS frame.
            if (!spdyHeadersFrame.IsTruncated())
            {
                long headerSize = headerSizeMap.TryGetValue(streamId, out long oldHeaderSize) ? oldHeaderSize : 0;

                bool addHeaderSize = true;
                try
                {
                    foreach (IEntry<ICharSequence, ICharSequence> e in spdyHeadersFrame.Headers())
                    {
                        long entrySize = HeaderEntrySize(e.GetKey(), e.GetValue());
                        if (headerSize > maxHeaderSize - entrySize)
                        {
                            IFullHttpMessage removed = RemoveMessage(streamId);
                            if (removed != null && removed != fullHttpMessage)
                            {
                                removed.Release();
                            }
                            fullHttpMessage.Release();
                            addHeaderSize = false;
                            throw new TooLongHttpHeaderException(
                                "HTTP header size exceeded " + maxHeaderSize.ToString(CultureInfo.InvariantCulture)
                                + " bytes.");
                        }
                        headerSize += entrySize;
                        fullHttpMessage.Headers().Add(e.GetKey(), e.GetValue());
                    }
                }
                finally
                {
                    if (addHeaderSize)
                    {
                        headerSizeMap[streamId] = headerSize;
                    }
                }
            }

            if (spdyHeadersFrame.IsLast())
            {
                HttpUtil.SetContentLength(fullHttpMessage, fullHttpMessage.Content.ReadableBytes);
                IFullHttpMessage removed = RemoveMessage(streamId);
                if (removed != null && removed != fullHttpMessage)
                {
                    removed.Release();
                }
                @out.Add(fullHttpMessage);
            }
        }
        else if (msg is ISpdyDataFrame)
        {

            ISpdyDataFrame spdyDataFrame = (ISpdyDataFrame) msg;
            int streamId = spdyDataFrame.StreamId();
            IFullHttpMessage fullHttpMessage = GetMessage(streamId);

            // If message is not in map discard Data Frame.
            if (fullHttpMessage == null)
            {
                return;
            }

            ByteBuf content = fullHttpMessage.Content;
            if (content.ReadableBytes > maxContentLength - spdyDataFrame.Content.ReadableBytes)
            {
                IFullHttpMessage removed = RemoveMessage(streamId);
                if (removed != null && removed != fullHttpMessage)
                {
                    removed.Release();
                }
                fullHttpMessage.Release();
                throw new TooLongFrameException(
                        "HTTP content length exceeded " + maxContentLength.ToString(CultureInfo.InvariantCulture)
                        + " bytes: " + spdyDataFrame.Content.ReadableBytes.ToString(CultureInfo.InvariantCulture));
            }

            ByteBuf spdyDataFrameData = spdyDataFrame.Content;
            int spdyDataFrameDataLen = spdyDataFrameData.ReadableBytes;
            content.WriteBytes(spdyDataFrameData, spdyDataFrameData.ReaderIndex, spdyDataFrameDataLen);

            if (spdyDataFrame.IsLast())
            {
                HttpUtil.SetContentLength(fullHttpMessage, content.ReadableBytes);
                IFullHttpMessage removed = RemoveMessage(streamId);
                if (removed != null && removed != fullHttpMessage)
                {
                    removed.Release();
                }
                @out.Add(fullHttpMessage);
            }

        }
        else if (msg is ISpdyRstStreamFrame)
        {

            ISpdyRstStreamFrame spdyRstStreamFrame = (ISpdyRstStreamFrame) msg;
            int streamId = spdyRstStreamFrame.StreamId();
            IFullHttpMessage removed = RemoveMessage(streamId);
            if (removed != null)
            {
                removed.Release();
            }
        }
    }

    private static long HeaderEntrySize(ICharSequence name, ICharSequence value)
    {
        // Account for the ": " separator between name and value on the wire.
        return (long) name.Length() + value.Length() + 2;
    }

    private static IFullHttpRequest CreateHttpRequest(ISpdyHeadersFrame requestFrame, IByteBufAllocator alloc)
    {
        // Create the first line of the request from the name/value pairs
        ISpdyHeaders headers    = requestFrame.Headers();
        HttpMethod  method      = HttpMethod.ValueOf(headers.GetAsString(METHOD));
        string      url         = headers.GetAsString(PATH);
        HttpVersion httpVersion = HttpVersion.ValueOf(headers.GetAsString(VERSION));
        headers.Remove(METHOD);
        headers.Remove(PATH);
        headers.Remove(VERSION);

        bool release = true;
        ByteBuf buffer = alloc.Buffer();
        try
        {
            IFullHttpRequest req = new DefaultFullHttpRequest(httpVersion, method, url, buffer);

            // Remove the scheme header
            headers.Remove(SCHEME);

            // Replace the SPDY host header with the HTTP host header
            ICharSequence host = headers.Get(HOST);
            headers.Remove(HOST);
            req.Headers().Set(HttpHeaderNames.HOST, host);

            foreach (IEntry<ICharSequence, ICharSequence> e in requestFrame.Headers())
            {
                req.Headers().Add(e.GetKey(), e.GetValue());
            }

            // The Connection and Keep-Alive headers are no longer valid
            HttpUtil.SetKeepAlive(req, true);

            // Transfer-Encoding header is not valid
            req.Headers().Remove(HttpHeaderNames.TRANSFER_ENCODING);
            release = false;
            return req;
        }
        finally
        {
            if (release)
            {
                buffer.Release();
            }
        }
    }

    private IFullHttpResponse CreateHttpResponse(ISpdyHeadersFrame responseFrame, IByteBufAllocator alloc)
    {

        // Create the first line of the response from the name/value pairs
        ISpdyHeaders headers = responseFrame.Headers();
        HttpResponseStatus status = HttpResponseStatus.ParseLine(headers.Get(STATUS));
        HttpVersion version = HttpVersion.ValueOf(headers.GetAsString(VERSION));
        headers.Remove(STATUS);
        headers.Remove(VERSION);

        bool release = true;
        ByteBuf buffer = alloc.Buffer();
        try
        {
            IFullHttpResponse res = new DefaultFullHttpResponse(
                    version, status, buffer, headersFactory, trailersFactory);
            foreach (IEntry<ICharSequence, ICharSequence> e in responseFrame.Headers())
            {
                res.Headers().Add(e.GetKey(), e.GetValue());
            }

            // The Connection and Keep-Alive headers are no longer valid
            HttpUtil.SetKeepAlive(res, true);

            // Transfer-Encoding header is not valid
            res.Headers().Remove(HttpHeaderNames.TRANSFER_ENCODING);
            res.Headers().Remove(HttpHeaderNames.TRAILER);

            release = false;
            return res;
        }
        finally
        {
            if (release)
            {
                buffer.Release();
            }
        }
    }
}
