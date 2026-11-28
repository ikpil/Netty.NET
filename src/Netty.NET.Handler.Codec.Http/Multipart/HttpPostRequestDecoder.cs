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
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * This decoder will decode Body and can handle POST BODY.
 *
 * You <strong>MUST</strong> call {@link #destroy()} after completion to release all resources.
 *
 */
// CLR: HttpRequest is IHttpRequest, HttpContent IHttpContent and Charset Encoding; List<InterfaceHttpData> is
// IList<IInterfaceHttpData>. The package-private DEFAULT_* constants are internal constants. The protected enum
// MultiPartStatus and the protected static getMultipartDataBoundary(String), which the other decoders of the package
// use, are protected internal, as Java protected also admits the package (docs/codec-base-porting.md, Package access;
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal). String.startsWith is
// StartsWith with StringComparison.Ordinal, String.regionMatches(true, 0, other, 0, len) string.Compare with
// StringComparison.OrdinalIgnoreCase over that length (a shorter string does not match, as in Java,
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html) and String.trim() core's
// HttpUtil.TrimJava. The nested exceptions extend codec-base DecoderException with the same constructors;
// serialVersionUID has no counterpart (docs/codec-base-porting.md, Exceptions).
public class HttpPostRequestDecoder : IInterfaceHttpPostRequestDecoder
{

    internal const int DEFAULT_DISCARD_THRESHOLD = 10 * 1024 * 1024;

    internal const int DEFAULT_MAX_FIELDS = 128;

    internal const int DEFAULT_MAX_BUFFERED_BYTES = 1024;

    private readonly IInterfaceHttpPostRequestDecoder decoder;

    /**
     *
     * @param request
     *            the request to decode
     * @throws NullPointerException
     *             for request
     * @throws ErrorDataDecoderException
     *             if the default charset was wrong when decoding or other
     *             errors
     */
    public HttpPostRequestDecoder(IHttpRequest request)
        : this(new DefaultHttpDataFactory(DefaultHttpDataFactory.MINSIZE), request, HttpConstants.DEFAULT_CHARSET)
    {
    }

    /**
     *
     * @param request
     *            the request to decode
     * @param maxFields
     *            the maximum number of fields the form can have, {@code -1} to disable
     * @param maxBufferedBytes
     *            the maximum number of bytes the decoder can buffer when decoding a field, {@code -1} to disable
     * @throws NullPointerException
     *             for request
     * @throws ErrorDataDecoderException
     *             if the default charset was wrong when decoding or other
     *             errors
     */
    public HttpPostRequestDecoder(IHttpRequest request, int maxFields, int maxBufferedBytes)
        : this(new DefaultHttpDataFactory(DefaultHttpDataFactory.MINSIZE), request, HttpConstants.DEFAULT_CHARSET,
             maxFields, maxBufferedBytes)
    {
    }

    /**
     *
     * @param factory
     *            the factory used to create InterfaceHttpData
     * @param request
     *            the request to decode
     * @throws NullPointerException
     *             for request or factory
     * @throws ErrorDataDecoderException
     *             if the default charset was wrong when decoding or other
     *             errors
     */
    public HttpPostRequestDecoder(IHttpDataFactory factory, IHttpRequest request)
        : this(factory, request, HttpConstants.DEFAULT_CHARSET)
    {
    }

    /**
     *
     * @param factory
     *            the factory used to create InterfaceHttpData
     * @param request
     *            the request to decode
     * @param charset
     *            the charset to use as default
     * @throws NullPointerException
     *             for request or charset or factory
     * @throws ErrorDataDecoderException
     *             if the default charset was wrong when decoding or other
     *             errors
     */
    public HttpPostRequestDecoder(IHttpDataFactory factory, IHttpRequest request, Encoding charset)
    {
        ObjectUtil.CheckNotNull(factory, "factory");
        ObjectUtil.CheckNotNull(request, "request");
        ObjectUtil.CheckNotNull(charset, "charset");

        // Fill default values
        if (IsMultipart(request))
        {
            decoder = new HttpPostMultipartRequestDecoder(factory, request, charset);
        }
        else
        {
            decoder = new HttpPostStandardRequestDecoder(factory, request, charset);
        }
    }

    /**
     *
     * @param factory
     *            the factory used to create InterfaceHttpData
     * @param request
     *            the request to decode
     * @param charset
     *            the charset to use as default
     * @param maxFields
     *            the maximum number of fields the form can have, {@code -1} to disable
     * @param maxBufferedBytes
     *            the maximum number of bytes the decoder can buffer when decoding a field, {@code -1} to disable
     * @throws NullPointerException
     *             for request or charset or factory
     * @throws ErrorDataDecoderException
     *             if the default charset was wrong when decoding or other
     *             errors
     */
    public HttpPostRequestDecoder(IHttpDataFactory factory, IHttpRequest request, Encoding charset,
                                  int maxFields, int maxBufferedBytes)
    {
        ObjectUtil.CheckNotNull(factory, "factory");
        ObjectUtil.CheckNotNull(request, "request");
        ObjectUtil.CheckNotNull(charset, "charset");

        // Fill default values
        if (IsMultipart(request))
        {
            decoder = new HttpPostMultipartRequestDecoder(factory, request, charset, maxFields, maxBufferedBytes);
        }
        else
        {
            decoder = new HttpPostStandardRequestDecoder(factory, request, charset, maxFields, maxBufferedBytes);
        }
    }

    /**
     * states follow NOTSTARTED PREAMBLE ( (HEADERDELIMITER DISPOSITION (FIELD |
     * FILEUPLOAD))* (HEADERDELIMITER DISPOSITION MIXEDPREAMBLE (MIXEDDELIMITER
     * MIXEDDISPOSITION MIXEDFILEUPLOAD)+ MIXEDCLOSEDELIMITER)* CLOSEDELIMITER)+
     * EPILOGUE
     *
     * First getStatus is: NOSTARTED
     *
     * Content-type: multipart/form-data, boundary=AaB03x => PREAMBLE in Header
     *
     * --AaB03x => HEADERDELIMITER content-disposition: form-data; name="field1"
     * => DISPOSITION
     *
     * Joe Blow => FIELD --AaB03x => HEADERDELIMITER content-disposition:
     * form-data; name="pics" => DISPOSITION Content-type: multipart/mixed,
     * boundary=BbC04y
     *
     * --BbC04y => MIXEDDELIMITER Content-disposition: attachment;
     * filename="file1.txt" => MIXEDDISPOSITION Content-Type: text/plain
     *
     * ... contents of file1.txt ... => MIXEDFILEUPLOAD --BbC04y =>
     * MIXEDDELIMITER Content-disposition: file; filename="file2.gif" =>
     * MIXEDDISPOSITION Content-type: image/gif Content-Transfer-Encoding:
     * binary
     *
     * ...contents of file2.gif... => MIXEDFILEUPLOAD --BbC04y-- =>
     * MIXEDCLOSEDELIMITER --AaB03x-- => CLOSEDELIMITER
     *
     * Once CLOSEDELIMITER is found, last getStatus is EPILOGUE
     */
    protected internal enum MultiPartStatus
    {
        NOTSTARTED, PREAMBLE, HEADERDELIMITER, DISPOSITION, FIELD, FILEUPLOAD, MIXEDPREAMBLE, MIXEDDELIMITER,
        MIXEDDISPOSITION, MIXEDFILEUPLOAD, MIXEDCLOSEDELIMITER, CLOSEDELIMITER, PREEPILOGUE, EPILOGUE
    }

    /**
     * Check if the given request is a multipart request
     * @return True if the request is a Multipart request
     */
    public static bool IsMultipart(IHttpRequest request)
    {
        string mimeType = request.Headers().Get(HttpHeaderNames.CONTENT_TYPE);
        if (mimeType != null && mimeType.StartsWith(HttpHeaderValues.MULTIPART_FORM_DATA.ToString(), StringComparison.Ordinal))
        {
            return GetMultipartDataBoundary(mimeType) != null;
        }

        return false;
    }

    /**
     * Check from the request ContentType if this request is a Multipart request.
     * @return an array of String if multipartDataBoundary exists with the multipartDataBoundary
     * as first element, charset if any as second (missing if not set), else null
     */
    protected internal static string[] GetMultipartDataBoundary(string contentType)
    {
        // Check if Post using "multipart/form-data; boundary=--89421926422648 [; charset=xxx]"
        string[] headerContentType = SplitHeaderContentType(contentType);
        string multiPartHeader = HttpHeaderValues.MULTIPART_FORM_DATA.ToString();
        if (string.Compare(headerContentType[0], 0, multiPartHeader, 0 , multiPartHeader.Length,
                StringComparison.OrdinalIgnoreCase) == 0)
        {
            int mrank;
            int crank;
            string boundaryHeader = HttpHeaderValues.BOUNDARY.ToString();
            if (string.Compare(headerContentType[1], 0, boundaryHeader, 0, boundaryHeader.Length,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                mrank = 1;
                crank = 2;
            }
            else if (string.Compare(headerContentType[2], 0, boundaryHeader, 0, boundaryHeader.Length,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                mrank = 2;
                crank = 1;
            }
            else
            {
                return null;
            }

            string boundary = StringUtil.SubstringAfter(headerContentType[mrank], '=');
            if (boundary == null)
            {
                throw new ErrorDataDecoderException("Needs a boundary value");
            }

            if (boundary[0] == '"')
            {
                string bound = HttpUtil.TrimJava(boundary);
                int index = bound.Length - 1;
                if (bound[index] == '"')
                {
                    boundary = bound.Substring(1, index - 1);
                }
            }

            string charsetHeader = HttpHeaderValues.CHARSET.ToString();
            if (string.Compare(headerContentType[crank], 0, charsetHeader, 0, charsetHeader.Length,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                string charset = StringUtil.SubstringAfter(headerContentType[crank], '=');
                if (charset != null)
                {
                    return new string[] {"--" + boundary, charset};
                }
            }

            return new string[] {"--" + boundary};
        }

        return null;
    }

    public virtual bool IsMultipart()
    {
        return decoder.IsMultipart();
    }

    public virtual void SetDiscardThreshold(int discardThreshold)
    {
        decoder.SetDiscardThreshold(discardThreshold);
    }

    public virtual int GetDiscardThreshold()
    {
        return decoder.GetDiscardThreshold();
    }

    public virtual IList<IInterfaceHttpData> GetBodyHttpDatas()
    {
        return decoder.GetBodyHttpDatas();
    }

    public virtual IList<IInterfaceHttpData> GetBodyHttpDatas(string name)
    {
        return decoder.GetBodyHttpDatas(name);
    }

    public virtual IInterfaceHttpData GetBodyHttpData(string name)
    {
        return decoder.GetBodyHttpData(name);
    }

    public virtual IInterfaceHttpPostRequestDecoder Offer(IHttpContent content)
    {
        return decoder.Offer(content);
    }

    public virtual bool HasNext()
    {
        return decoder.HasNext();
    }

    public virtual IInterfaceHttpData Next()
    {
        return decoder.Next();
    }

    public virtual IInterfaceHttpData CurrentPartialHttpData()
    {
        return decoder.CurrentPartialHttpData();
    }

    public virtual void Destroy()
    {
        decoder.Destroy();
    }

    public virtual void CleanFiles()
    {
        decoder.CleanFiles();
    }

    public virtual void RemoveHttpDataFromClean(IInterfaceHttpData data)
    {
        decoder.RemoveHttpDataFromClean(data);
    }

    /**
     * Split the very first line (Content-Type value) in 3 Strings
     *
     * @return the array of 3 Strings
     */
    private static string[] SplitHeaderContentType(string sb)
    {
        int aStart;
        int aEnd;
        int bStart;
        int bEnd;
        int cStart;
        int cEnd;
        aStart = HttpPostBodyUtil.FindNonWhitespace(sb, 0);
        aEnd =  sb.IndexOf(';');
        if (aEnd == -1)
        {
            return new string[] { sb, "", "" };
        }

        bStart = HttpPostBodyUtil.FindNonWhitespace(sb, aEnd + 1);

        if (sb[aEnd - 1] == ' ')
        {
            aEnd--;
        }

        bEnd =  sb.IndexOf(';', bStart);
        if (bEnd == -1)
        {
            bEnd = HttpPostBodyUtil.FindEndOfString(sb);
            return new string[] { sb.Substring(aStart, aEnd - aStart), sb.Substring(bStart, bEnd - bStart), "" };
        }

        cStart = HttpPostBodyUtil.FindNonWhitespace(sb, bEnd + 1);

        if (sb[bEnd - 1] == ' ')
        {
            bEnd--;
        }

        cEnd = HttpPostBodyUtil.FindEndOfString(sb);
        return new string[] { sb.Substring(aStart, aEnd - aStart), sb.Substring(bStart, bEnd - bStart),
                sb.Substring(cStart, cEnd - cStart) };
    }

    /**
     * Exception when try reading data from request in chunked format, and not
     * enough data are available (need more chunks)
     */
    public class NotEnoughDataDecoderException : DecoderException
    {

        public NotEnoughDataDecoderException()
        {
        }

        public NotEnoughDataDecoderException(string msg) : base(msg)
        {
        }

        public NotEnoughDataDecoderException(Exception cause) : base(cause)
        {
        }

        public NotEnoughDataDecoderException(string msg, Exception cause) : base(msg, cause)
        {
        }
    }

    /**
     * Exception when the body is fully decoded, even if there is still data
     */
    public class EndOfDataDecoderException : DecoderException
    {
    }

    /**
     * Exception when an error occurs while decoding
     */
    public class ErrorDataDecoderException : DecoderException
    {

        public ErrorDataDecoderException()
        {
        }

        public ErrorDataDecoderException(string msg) : base(msg)
        {
        }

        public ErrorDataDecoderException(Exception cause) : base(cause)
        {
        }

        public ErrorDataDecoderException(string msg, Exception cause) : base(msg, cause)
        {
        }
    }

    /**
     * Exception when the maximum number of fields for a given form is reached
     */
    public sealed class TooManyFormFieldsException : DecoderException
    {
    }

    /**
     * Exception when a field content is too long
     */
    public sealed class TooLongFormFieldException : DecoderException
    {
    }
}
