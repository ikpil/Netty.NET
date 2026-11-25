/*
 * Copyright 2015 The Netty Project
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

using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http.HttpConstants;

namespace Netty.NET.Handler.Codec.Rtsp;

/**
 * Encodes an RTSP message represented in {@link HttpMessage} or an {@link HttpContent} into
 * a {@link ByteBuf}.
 */
// CLR: HttpMessage, HttpRequest and HttpResponse are IHttpMessage, IHttpRequest and IHttpResponse.
// buf.writeCharSequence(s, UTF_8) is ByteBufUtil.WriteUtf8 and buf.writeCharSequence(s, US_ASCII) is
// ByteBufUtil.WriteAscii (AsciiString.c2b per char), the mappings of core HttpRequestEncoder and HttpVersion; a class
// literal is typeof(...). The private CRLF_SHORT hides the internal HttpObjectEncoder.CRLF_SHORT, as the Java field
// does.
public class RtspEncoder : HttpObjectEncoder<IHttpMessage>
{
    private const int CRLF_SHORT = (CR << 8) | LF;

    public override bool AcceptOutboundMessage(object msg)
    {
        return base.AcceptOutboundMessage(msg) && ((msg is IHttpRequest) || (msg is IHttpResponse));
    }

    protected override void EncodeInitialLine(ByteBuf buf, IHttpMessage message)
    {
        if (message is IHttpRequest)
        {
            IHttpRequest request = (IHttpRequest) message;
            ByteBufUtil.Copy(request.Method().AsciiName(), buf);
            buf.WriteByte(SP);
            ByteBufUtil.WriteUtf8(buf, request.Uri());
            buf.WriteByte(SP);
            ByteBufUtil.WriteAscii(buf, request.ProtocolVersion().ToString());
            ByteBufUtil.WriteShortBE(buf, CRLF_SHORT);
        }
        else if (message is IHttpResponse)
        {
            IHttpResponse response = (IHttpResponse) message;
            ByteBufUtil.WriteAscii(buf, response.ProtocolVersion().ToString());
            buf.WriteByte(SP);
            ByteBufUtil.Copy(response.Status().CodeAsText(), buf);
            buf.WriteByte(SP);
            ByteBufUtil.WriteAscii(buf, response.Status().ReasonPhrase());
            ByteBufUtil.WriteShortBE(buf, CRLF_SHORT);
        }
        else
        {
            throw new UnsupportedMessageTypeException(message, typeof(IHttpRequest), typeof(IHttpResponse));
        }
    }
}
