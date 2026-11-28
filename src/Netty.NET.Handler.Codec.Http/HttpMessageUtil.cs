/*
 * Copyright 2014 The Netty Project
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

using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Provides some utility methods for HTTP message implementations.
 */
// CLR: StringBuilder.setLength is the Length setter, and Map.Entry is the codec-base IEntry.
internal static class HttpMessageUtil
{

    internal static StringBuilder AppendRequest(StringBuilder buf, IHttpRequest req)
    {
        AppendCommon(buf, req);
        AppendInitialLine(buf, req);
        AppendHeaders(buf, req.Headers());
        RemoveLastNewLine(buf);
        return buf;
    }

    internal static StringBuilder AppendResponse(StringBuilder buf, IHttpResponse res)
    {
        AppendCommon(buf, res);
        AppendInitialLine(buf, res);
        AppendHeaders(buf, res.Headers());
        RemoveLastNewLine(buf);
        return buf;
    }

    private static void AppendCommon(StringBuilder buf, IHttpMessage msg)
    {
        buf.Append(StringUtil.SimpleClassName(msg));
        buf.Append("(decodeResult: ");
        buf.Append(msg.DecoderResult());
        buf.Append(", version: ");
        buf.Append(msg.ProtocolVersion());
        buf.Append(')');
        buf.Append(StringUtil.NEWLINE);
    }

    internal static StringBuilder AppendFullRequest(StringBuilder buf, IFullHttpRequest req)
    {
        AppendFullCommon(buf, req);
        AppendInitialLine(buf, req);
        AppendHeaders(buf, req.Headers());
        AppendHeaders(buf, req.TrailingHeaders());
        RemoveLastNewLine(buf);
        return buf;
    }

    internal static StringBuilder AppendFullResponse(StringBuilder buf, IFullHttpResponse res)
    {
        AppendFullCommon(buf, res);
        AppendInitialLine(buf, res);
        AppendHeaders(buf, res.Headers());
        AppendHeaders(buf, res.TrailingHeaders());
        RemoveLastNewLine(buf);
        return buf;
    }

    private static void AppendFullCommon(StringBuilder buf, IFullHttpMessage msg)
    {
        buf.Append(StringUtil.SimpleClassName(msg));
        buf.Append("(decodeResult: ");
        buf.Append(msg.DecoderResult());
        buf.Append(", version: ");
        buf.Append(msg.ProtocolVersion());
        buf.Append(", content: ");
        buf.Append(msg.Content);
        buf.Append(')');
        buf.Append(StringUtil.NEWLINE);
    }

    private static void AppendInitialLine(StringBuilder buf, IHttpRequest req)
    {
        buf.Append(req.Method());
        buf.Append(' ');
        buf.Append(req.Uri());
        buf.Append(' ');
        buf.Append(req.ProtocolVersion());
        buf.Append(StringUtil.NEWLINE);
    }

    private static void AppendInitialLine(StringBuilder buf, IHttpResponse res)
    {
        buf.Append(res.ProtocolVersion());
        buf.Append(' ');
        buf.Append(res.Status());
        buf.Append(StringUtil.NEWLINE);
    }

    private static void AppendHeaders(StringBuilder buf, HttpHeaders headers)
    {
        foreach (IEntry<string, string> e in headers)
        {
            buf.Append(e.GetKey());
            buf.Append(": ");
            buf.Append(e.GetValue());
            buf.Append(StringUtil.NEWLINE);
        }
    }

    private static void RemoveLastNewLine(StringBuilder buf)
    {
        buf.Length = buf.Length - StringUtil.NEWLINE.Length;
    }
}
