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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http.HttpConstants;
using static Netty.NET.Handler.Codec.Http.HttpObjectEncoder;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Encodes an {@link HttpRequest} or an {@link HttpContent} into
 * a {@link ByteBuf}.
 */
// CLR: buf.writeCharSequence(uri, UTF_8) is ByteBufUtil.WriteUtf8, the Buffer port's counterpart of Java's
// ByteBufUtil.writeUtf8 path of writeCharSequence (an unpaired surrogate becomes '?', where Encoding.UTF8 would write
// U+FFFD); a StringBuilder is a CharSequence in Java, so the URI with the inserted slash is written from its text.
// String.indexOf/lastIndexOf are the ordinal string methods.
public class HttpRequestEncoder : HttpObjectEncoder<IHttpRequest>
{
    private const char SLASH = '/';
    private const char QUESTION_MARK = '?';
    private const int SLASH_AND_SPACE_SHORT = (SLASH << 8) | SP;
    private const int SPACE_SLASH_AND_SPACE_MEDIUM = (SP << 16) | SLASH_AND_SPACE_SHORT;

    public override bool AcceptOutboundMessage(object msg)
    {
        return base.AcceptOutboundMessage(msg) && !(msg is IHttpResponse);
    }

    protected override void EncodeInitialLine(ByteBuf buf, IHttpRequest request)
    {
        ByteBufUtil.Copy(request.Method().AsciiName(), buf);

        string uri = request.Uri();

        if (uri.Length == 0)
        {
            // Add " / " as absolute path if uri is not present.
            // See https://tools.ietf.org/html/rfc2616#section-5.1.2
            ByteBufUtil.WriteMediumBE(buf, SPACE_SLASH_AND_SPACE_MEDIUM);
        }
        else
        {
            string uriCharSequence = uri;
            bool needSlash = false;
            int start = uri.IndexOf("://", StringComparison.Ordinal);
            if (start != -1 && uri[0] != SLASH)
            {
                start += 3;
                // Correctly handle query params.
                // See https://github.com/netty/netty/issues/2732
                int index = uri.IndexOf(QUESTION_MARK, start);
                if (index == -1)
                {
                    if (uri.LastIndexOf(SLASH) < start)
                    {
                        needSlash = true;
                    }
                }
                else
                {
                    if (uri.LastIndexOf(SLASH, index) < start)
                    {
                        uriCharSequence = new StringBuilder(uri).Insert(index, SLASH).ToString();
                    }
                }
            }
            buf.WriteByte(SP);
            ByteBufUtil.WriteUtf8(buf, uriCharSequence);
            if (needSlash)
            {
                // write "/ " after uri
                ByteBufUtil.WriteShortBE(buf, SLASH_AND_SPACE_SHORT);
            }
            else
            {
                buf.WriteByte(SP);
            }
        }

        request.ProtocolVersion().Encode(buf);
        ByteBufUtil.WriteShortBE(buf, CRLF_SHORT);
    }
}
