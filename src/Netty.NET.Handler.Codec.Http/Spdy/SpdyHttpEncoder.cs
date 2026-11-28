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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * Encodes {@link HttpRequest}s, {@link HttpResponse}s, and {@link HttpContent}s
 * into {@link SpdySynStreamFrame}s and {@link SpdySynReplyFrame}s.
 *
 * <h3>Request Annotations</h3>
 *
 * SPDY specific headers must be added to {@link HttpRequest}s:
 * <table border=1>
 * <tr>
 * <th>Header Name</th><th>Header Value</th>
 * </tr>
 * <tr>
 * <td>{@code "X-SPDY-Stream-ID"}</td>
 * <td>The Stream-ID for this request.
 * Stream-IDs must be odd, positive integers, and must increase monotonically.</td>
 * </tr>
 * <tr>
 * <td>{@code "X-SPDY-Priority"}</td>
 * <td>The priority value for this request.
 * The priority should be between 0 and 7 inclusive.
 * 0 represents the highest priority and 7 represents the lowest.
 * This header is optional and defaults to 0.</td>
 * </tr>
 * </table>
 *
 * <h3>Response Annotations</h3>
 *
 * SPDY specific headers must be added to {@link HttpResponse}s:
 * <table border=1>
 * <tr>
 * <th>Header Name</th><th>Header Value</th>
 * </tr>
 * <tr>
 * <td>{@code "X-SPDY-Stream-ID"}</td>
 * <td>The Stream-ID of the request corresponding to this response.</td>
 * </tr>
 * </table>
 *
 * <h3>Pushed Resource Annotations</h3>
 *
 * SPDY specific headers must be added to pushed {@link HttpRequest}s:
 * <table border=1>
 * <tr>
 * <th>Header Name</th><th>Header Value</th>
 * </tr>
 * <tr>
 * <td>{@code "X-SPDY-Stream-ID"}</td>
 * <td>The Stream-ID for this resource.
 * Stream-IDs must be even, positive integers, and must increase monotonically.</td>
 * </tr>
 * <tr>
 * <td>{@code "X-SPDY-Associated-To-Stream-ID"}</td>
 * <td>The Stream-ID of the request that initiated this pushed resource.</td>
 * </tr>
 * <tr>
 * <td>{@code "X-SPDY-Priority"}</td>
 * <td>The priority value for this resource.
 * The priority should be between 0 and 7 inclusive.
 * 0 represents the highest priority and 7 represents the lowest.
 * This header is optional and defaults to 0.</td>
 * </tr>
 * </table>
 *
 * <h3>Required Annotations</h3>
 *
 * SPDY requires that all Requests and Pushed Resources contain
 * an HTTP "Host" header.
 *
 * <h3>Optional Annotations</h3>
 *
 * Requests and Pushed Resources must contain a SPDY scheme header.
 * This can be set via the {@code "X-SPDY-Scheme"} header but otherwise
 * defaults to "https" as that is the most common SPDY deployment.
 *
 * <h3>Chunked Content</h3>
 *
 * This encoder associates all {@link HttpContent}s that it receives
 * with the most recently received 'chunked' {@link HttpRequest}
 * or {@link HttpResponse}.
 *
 * <h3>Pushed Resources</h3>
 *
 * All pushed resources should be sent before sending the response
 * that corresponds to the initial request.
 */
// CLR: MessageToMessageEncoder<HttpObject> is the codec-base MessageToMessageEncoder<IHttpObject> with a
// typeof(...) matcher, and the HTTP message types are the core I-prefixed interfaces. headers.getInt(name) returns
// int? (Integer) and its unboxing into an int is.Value, which throws InvalidOperationException for a missing
// header where Java's unboxing throws NullPointerException; both propagate out of encode. (byte) getInt(PRIORITY,
// 0) is an unchecked narrowing to the CLR byte (the same low eight bits as the Java byte), and setPriority then
// rejects a value above 7 as Java rejects a value outside 0-7. The @SuppressWarnings line has no counterpart.
public class SpdyHttpEncoder : MessageToMessageEncoder<IHttpObject>
{

    private int currentStreamId;

    private readonly bool validateHeaders;
    private readonly bool headersToLowerCase;

    /**
     * Creates a new instance.
     *
     * @param version the protocol version
     */
    public SpdyHttpEncoder(SpdyVersion version) : this(version, true, true)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param version            the protocol version
     * @param headersToLowerCase convert header names to lowercase. In a controlled environment,
     *                           one can disable the conversion.
     * @param validateHeaders    validate the header names and values when adding them to the {@link SpdyHeaders}
     */
    public SpdyHttpEncoder(SpdyVersion version, bool headersToLowerCase, bool validateHeaders)
        : base(typeof(IHttpObject))
    {
        ObjectUtil.CheckNotNull(version, "version");

        this.headersToLowerCase = headersToLowerCase;
        this.validateHeaders = validateHeaders;
    }

    protected override void Encode(IChannelHandlerContext ctx, IHttpObject msg, IList<object> @out)
    {

        bool valid = false;
        bool last = false;

        if (msg is IHttpRequest)
        {

            IHttpRequest httpRequest = (IHttpRequest) msg;
            ISpdySynStreamFrame spdySynStreamFrame = CreateSynStreamFrame(httpRequest);
            @out.Add(spdySynStreamFrame);

            last = spdySynStreamFrame.IsLast() || spdySynStreamFrame.IsUnidirectional();
            valid = true;
        }

        if (msg is IHttpResponse)
        {

            IHttpResponse httpResponse = (IHttpResponse) msg;
            ISpdyHeadersFrame spdyHeadersFrame = CreateHeadersFrame(httpResponse);
            @out.Add(spdyHeadersFrame);

            last = spdyHeadersFrame.IsLast();
            valid = true;
        }

        if (msg is IHttpContent && !last)
        {

            IHttpContent chunk = (IHttpContent) msg;

            chunk.Content.Retain();
            ISpdyDataFrame spdyDataFrame = new DefaultSpdyDataFrame(currentStreamId, chunk.Content);

            if (chunk is ILastHttpContent)
            {
                ILastHttpContent trailer = (ILastHttpContent) chunk;
                HttpHeaders trailers = trailer.TrailingHeaders();
                if (trailers.IsEmpty())
                {
                    spdyDataFrame.SetLast(true);
                    @out.Add(spdyDataFrame);
                }
                else
                {
                    // Create SPDY HEADERS frame out of trailers
                    ISpdyHeadersFrame spdyHeadersFrame = new DefaultSpdyHeadersFrame(currentStreamId, validateHeaders);
                    spdyHeadersFrame.SetLast(true);
                    IIterator<IEntry<ICharSequence, ICharSequence>> itr = trailers.IteratorCharSequence();

                    while (itr.HasNext())
                    {
                        IEntry<ICharSequence, ICharSequence> entry = itr.Next();
                        ICharSequence headerName =
                                headersToLowerCase ? AsciiString.Of(entry.GetKey()).ToLowerCase() : entry.GetKey();
                        spdyHeadersFrame.Headers().Add(headerName, entry.GetValue());
                    }

                    // Write DATA frame and append HEADERS frame
                    @out.Add(spdyDataFrame);
                    @out.Add(spdyHeadersFrame);
                }
            }
            else
            {
                @out.Add(spdyDataFrame);
            }

            valid = true;
        }

        if (!valid)
        {
            throw new UnsupportedMessageTypeException(msg);
        }
    }

    private ISpdySynStreamFrame CreateSynStreamFrame(IHttpRequest httpRequest)
    {
        // Get the Stream-ID, Associated-To-Stream-ID, Priority, and scheme from the headers
        HttpHeaders httpHeaders = httpRequest.Headers();
        int streamId = httpHeaders.GetInt(SpdyHttpHeaders.Names.STREAM_ID).Value;
        int associatedToStreamId = httpHeaders.GetInt(SpdyHttpHeaders.Names.ASSOCIATED_TO_STREAM_ID, 0);
        byte priority = unchecked((byte) httpHeaders.GetInt(SpdyHttpHeaders.Names.PRIORITY, 0));
        ICharSequence scheme = Of(httpHeaders.Get(SpdyHttpHeaders.Names.SCHEME));
        httpHeaders.Remove(SpdyHttpHeaders.Names.STREAM_ID);
        httpHeaders.Remove(SpdyHttpHeaders.Names.ASSOCIATED_TO_STREAM_ID);
        httpHeaders.Remove(SpdyHttpHeaders.Names.PRIORITY);
        httpHeaders.Remove(SpdyHttpHeaders.Names.SCHEME);

        // The Connection, Keep-Alive, Proxy-Connection, and Transfer-Encoding
        // headers are not valid and MUST not be sent.
        httpHeaders.Remove(HttpHeaderNames.CONNECTION);
        httpHeaders.Remove("Keep-Alive");
        httpHeaders.Remove("Proxy-Connection");
        httpHeaders.Remove(HttpHeaderNames.TRANSFER_ENCODING);

        ISpdySynStreamFrame spdySynStreamFrame =
                new DefaultSpdySynStreamFrame(streamId, associatedToStreamId, priority, validateHeaders);

        // Unfold the first line of the message into name/value pairs
        ISpdyHeaders frameHeaders = spdySynStreamFrame.Headers();
        frameHeaders.Set(ISpdyHeaders.HttpNames.METHOD, Of(httpRequest.Method().Name()));
        frameHeaders.Set(ISpdyHeaders.HttpNames.PATH, Of(httpRequest.Uri()));
        frameHeaders.Set(ISpdyHeaders.HttpNames.VERSION, Of(httpRequest.ProtocolVersion().Text()));

        // Replace the HTTP host header with the SPDY host header
        ICharSequence host = Of(httpHeaders.Get(HttpHeaderNames.HOST));
        httpHeaders.Remove(HttpHeaderNames.HOST);
        frameHeaders.Set(ISpdyHeaders.HttpNames.HOST, host);

        // Set the SPDY scheme header
        if (scheme == null)
        {
            scheme = Of("https");
        }

        frameHeaders.Set(ISpdyHeaders.HttpNames.SCHEME, scheme);

        // Transfer the remaining HTTP headers
        IIterator<IEntry<ICharSequence, ICharSequence>> itr = httpHeaders.IteratorCharSequence();

        while (itr.HasNext())
        {
            IEntry<ICharSequence, ICharSequence> entry = itr.Next();
            ICharSequence headerName =
                    headersToLowerCase ? AsciiString.Of(entry.GetKey()).ToLowerCase() : entry.GetKey();
            frameHeaders.Add(headerName, entry.GetValue());
        }

        currentStreamId = spdySynStreamFrame.StreamId();

        if (associatedToStreamId == 0)
        {
            spdySynStreamFrame.SetLast(IsLast(httpRequest));
        }
        else
        {
            spdySynStreamFrame.SetUnidirectional(true);
        }

        return spdySynStreamFrame;
    }

    private ISpdyHeadersFrame CreateHeadersFrame(IHttpResponse httpResponse)
    {
        // Get the Stream-ID from the headers
        HttpHeaders httpHeaders = httpResponse.Headers();
        int streamId = httpHeaders.GetInt(SpdyHttpHeaders.Names.STREAM_ID).Value;
        httpHeaders.Remove(SpdyHttpHeaders.Names.STREAM_ID);

        // The Connection, Keep-Alive, Proxy-Connection, and Transfer-Encoding
        // headers are not valid and MUST not be sent.
        httpHeaders.Remove(HttpHeaderNames.CONNECTION);
        httpHeaders.Remove("Keep-Alive");
        httpHeaders.Remove("Proxy-Connection");
        httpHeaders.Remove(HttpHeaderNames.TRANSFER_ENCODING);

        ISpdyHeadersFrame spdyHeadersFrame;

        if (SpdyCodecUtil.IsServerId(streamId))
        {
            spdyHeadersFrame = new DefaultSpdyHeadersFrame(streamId, validateHeaders);
        }
        else
        {
            spdyHeadersFrame = new DefaultSpdySynReplyFrame(streamId, validateHeaders);
        }

        ISpdyHeaders frameHeaders = spdyHeadersFrame.Headers();
        // Unfold the first line of the response into name/value pairs
        frameHeaders.Set(ISpdyHeaders.HttpNames.STATUS, httpResponse.Status().CodeAsText());
        frameHeaders.Set(ISpdyHeaders.HttpNames.VERSION, Of(httpResponse.ProtocolVersion().Text()));

        // Transfer the remaining HTTP headers
        IIterator<IEntry<ICharSequence, ICharSequence>> itr = httpHeaders.IteratorCharSequence();

        while (itr.HasNext())
        {
            IEntry<ICharSequence, ICharSequence> entry = itr.Next();
            ICharSequence headerName =
                    headersToLowerCase ? AsciiString.Of(entry.GetKey()).ToLowerCase() : entry.GetKey();
            spdyHeadersFrame.Headers().Add(headerName, entry.GetValue());
        }

        currentStreamId = streamId;
        spdyHeadersFrame.SetLast(IsLast(httpResponse));

        return spdyHeadersFrame;
    }

    /**
     * Checks if the given HTTP message should be considered as a last SPDY frame.
     *
     * @param httpMessage check this HTTP message
     * @return whether the given HTTP message should generate a <em>last</em> SPDY frame.
     */
    private static bool IsLast(IHttpMessage httpMessage)
    {
        if (httpMessage is IFullHttpMessage)
        {
            IFullHttpMessage fullMessage = (IFullHttpMessage) httpMessage;
            if (fullMessage.TrailingHeaders().IsEmpty() && !fullMessage.Content.IsReadable())
            {
                return true;
            }
        }

        return false;
    }

    // CLR: a String passed where Java takes a CharSequence, wrapped in the Common StringCharSequence (null stays null).
    private static ICharSequence Of(string value)
    {
        return value == null ? null : new StringCharSequence(value);
    }
}
