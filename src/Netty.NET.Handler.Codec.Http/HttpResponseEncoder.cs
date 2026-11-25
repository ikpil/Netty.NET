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
using Netty.NET.Buffer;
using static Netty.NET.Handler.Codec.Http.HttpConstants;
using static Netty.NET.Handler.Codec.Http.HttpObjectEncoder;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Encodes an {@link HttpResponse} or an {@link HttpContent} into
 * a {@link ByteBuf}.
 */
// CLR: msg.getClass() == X.class is GetType() == typeof(X).
public class HttpResponseEncoder : HttpObjectEncoder<IHttpResponse>
{

    public override bool AcceptOutboundMessage(object msg)
    {
        // JDK type checks vs non-implemented interfaces costs O(N), where
        // N is the number of interfaces already implemented by the concrete type that's being tested.
        // !(msg instanceof HttpRequest) is supposed to always be true (and meaning that msg isn't a HttpRequest),
        // but sadly was part of the original behaviour of this method and cannot be removed.
        // We place here exact checks vs DefaultHttpResponse and DefaultFullHttpResponse because bad users can
        // extends such types and make them to implement HttpRequest (non-sense, but still possible).
        Type msgClass = msg.GetType();
        if (msgClass == typeof(DefaultFullHttpResponse) || msgClass == typeof(DefaultHttpResponse))
        {
            return true;
        }
        return base.AcceptOutboundMessage(msg) && !(msg is IHttpRequest);
    }

    protected override void EncodeInitialLine(ByteBuf buf, IHttpResponse response)
    {
        response.ProtocolVersion().Encode(buf);
        buf.WriteByte(SP);
        response.Status().Encode(buf);
        ByteBufUtil.WriteShortBE(buf, CRLF_SHORT);
    }

    protected override void SanitizeHeadersBeforeEncode(IHttpResponse msg, bool isAlwaysEmpty)
    {
        if (isAlwaysEmpty)
        {
            HttpResponseStatus status = msg.Status();
            if (status.CodeClass() == HttpStatusClass.INFORMATIONAL ||
                    status.Code() == HttpResponseStatus.NO_CONTENT.Code())
            {

                // Stripping Content-Length:
                // See https://tools.ietf.org/html/rfc7230#section-3.3.2
                msg.Headers().Remove(HttpHeaderNames.CONTENT_LENGTH);

                // Stripping Transfer-Encoding:
                // See https://tools.ietf.org/html/rfc7230#section-3.3.1
                msg.Headers().Remove(HttpHeaderNames.TRANSFER_ENCODING);
            }
            else if (status.Code() == HttpResponseStatus.RESET_CONTENT.Code())
            {

                // Stripping Transfer-Encoding:
                msg.Headers().Remove(HttpHeaderNames.TRANSFER_ENCODING);

                // Set Content-Length: 0
                // https://httpstatuses.com/205
                msg.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 0);
            }
        }
    }

    protected override bool IsContentAlwaysEmpty(IHttpResponse msg)
    {
        // Correctly handle special cases as stated in:
        // https://tools.ietf.org/html/rfc7230#section-3.3.3
        HttpResponseStatus status = msg.Status();

        if (status.CodeClass() == HttpStatusClass.INFORMATIONAL)
        {

            if (status.Code() == HttpResponseStatus.SWITCHING_PROTOCOLS.Code())
            {
                // We need special handling for WebSockets version 00 as it will include an body.
                // Fortunally this version should not really be used in the wild very often.
                // See https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-00#section-1.2
                return msg.Headers().Contains(HttpHeaderNames.SEC_WEBSOCKET_VERSION);
            }
            return true;
        }
        return status.Code() == HttpResponseStatus.NO_CONTENT.Code() ||
                status.Code() == HttpResponseStatus.NOT_MODIFIED.Code() ||
                status.Code() == HttpResponseStatus.RESET_CONTENT.Code();
    }
}
