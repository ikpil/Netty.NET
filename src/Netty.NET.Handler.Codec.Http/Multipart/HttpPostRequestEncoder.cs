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
using System.Globalization;
using System.IO;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Stream;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * This encoder will help to encode Request for a FORM as POST.
 *
 * <P>According to RFC 7231, POST, PUT and OPTIONS allow to have a body.
 * This encoder will support widely all methods except TRACE since the RFC notes
 * for GET, DELETE, HEAD and CONNECT: (replaces XXX by one of these methods)</P>
 * <P>"A payload within a XXX request message has no defined semantics;
 * sending a payload body on a XXX request might cause some existing
 * implementations to reject the request."</P>
 * <P>On the contrary, for TRACE method, RFC says:</P>
 * <P>"A client MUST NOT send a message body in a TRACE request."</P>
 */
// CLR: ChunkedInput<HttpContent> is the handler port's IChunkedInput<IHttpContent>; the @Deprecated
// readChunk(ChannelHandlerContext) override has no counterpart, because the interface member it overrides is
// @Deprecated with the replacement readChunk(ByteBufAllocator) named and not ported (src/Netty.NET.Handler/Stream/
// IChunkedInput.cs), and "throws Exception" has no C# counterpart. HttpRequest/FullHttpRequest/HttpContent/
// LastHttpContent are the I-prefixed core interfaces, java.io.File is FileInfo (File.getName() is Name and File.length()
// AbstractDiskHttpData.FileLength) and Charset is Encoding (Charset.name() is HttpPostBodyUtil.CharsetName). The package-
// private multipartHttpDatas, multipartDataBoundary and multipartMixedBoundary, which the tests read, are internal.
// The ListIterator over multipartHttpDatas is the int index of its next element (hasNext() is iterator <
// multipartHttpDatas.Count and next() multipartHttpDatas[iterator++]). Long.toHexString of
// ThreadLocalRandom.current().nextLong(), the unsigned hexadecimal text without leading zeros in lower case
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Long.html), is the "x" format of the ulong of
// Random.Shared.NextInt64(long.MinValue, long.MaxValue), whose range "includes minValue but not maxValue"
// (https://learn.microsoft.com/en-us/dotnet/api/system.random.nextint64?view=net-10.0); Random.Shared is "a thread-safe
// Random instance" (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0).
// URLEncoder.encode(s, charset.name()) is UrlEncode(s, charset), which follows the URLEncoder rules
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/URLEncoder.html): a-z, A-Z, 0-9, '.', '-', '*'
// and '_' stay, ' ' becomes '+', and every other run of characters is encoded with the charset and each byte written as
// %XY with upper-case hexadecimal digits, as the JDK writes them. The BCL WebUtility.UrlEncode keeps '!', '(' and ')'
// too, so it is not used. The UnsupportedEncodingException of encode() cannot occur, because the Encoding is already
// resolved, so its catch is not ported. toLowerCase(Locale.US) is ToLowerInvariant
// (https://learn.microsoft.com/en-us/dotnet/api/system.string.tolowerinvariant?view=net-10.0), String.startsWith is
// StartsWith with StringComparison.Ordinal, and String.valueOf(long) the invariant-culture text. The checked
// ErrorDataEncoderException extends Exception; its cause-only constructor keeps Java's detail message cause.toString()
// (Common's ThrowableUtil.ThrowableToString). WrappedHttpRequest/WrappedFullHttpRequest implement IHttpRequest/
// IFullHttpRequest: the @Deprecated getMethod()/getUri()/getProtocolVersion()/getDecoderResult() overrides are not ported
// with the interface members (core's IHttpRequest), setProtocolVersion narrows its return type with an explicit
// IHttpMessage implementation and covariant overrides, and the IByteBufHolder/IReferenceCounted members, which those
// interfaces declare with their own return types, are implemented explicitly (as core's DefaultFullHttpRequest does).
// The local InternalAttribute internal is @internal (internal is a C# keyword); the one that ends a mixed part before an
// Attribute is endMixedInternal, because a C# local's scope is its whole block and the enclosing block declares
// internal too (CS0136).
public class HttpPostRequestEncoder : IChunkedInput<IHttpContent>
{

    /**
     * Different modes to use to encode form data.
     */
    public enum EncoderMode
    {
        /**
         * Legacy mode which should work for most. It is known to not work with OAUTH. For OAUTH use
         * {@link EncoderMode#RFC3986}. The W3C form recommendations this for submitting post form data.
         */
        RFC1738,

        /**
         * Mode which is more new and is used for OAUTH
         */
        RFC3986,

        /**
         * The HTML5 spec disallows mixed mode in multipart/form-data
         * requests. More concretely this means that more files submitted
         * under the same name will not be encoded using mixed mode, but
         * will be treated as distinct fields.
         *
         * Reference:
         *   https://www.w3.org/TR/html5/forms.html#multipart-form-data
         */
        HTML5
    }

    private const string ASTERISK = "*";
    private const string PLUS = "+";
    private const string TILDE = "~";
    private const string ASTERISK_REPLACEMENT = "%2A";
    private const string PLUS_REPLACEMENT = "%20";
    private const string TILDE_REPLACEMENT = "%7E";

    /**
     * Factory used to create InterfaceHttpData
     */
    private readonly IHttpDataFactory factory;

    /**
     * Request to encode
     */
    private readonly IHttpRequest request;

    /**
     * Default charset to use
     */
    private readonly Encoding charset;

    /**
     * Chunked false by default
     */
    private bool isChunked;

    /**
     * InterfaceHttpData for Body (without encoding)
     */
    private readonly List<IInterfaceHttpData> bodyListDatas;
    /**
     * The final Multipart List of InterfaceHttpData including encoding
     */
    internal readonly List<IInterfaceHttpData> multipartHttpDatas;

    /**
     * Does this request is a Multipart request
     */
    private readonly bool isMultipart;

    /**
     * If multipart, this is the boundary for the flobal multipart
     */
    internal string multipartDataBoundary;

    /**
     * If multipart, there could be internal multiparts (mixed) to the global multipart. Only one level is allowed.
     */
    internal string multipartMixedBoundary;
    /**
     * To check if the header has been finalized
     */
    private bool headerFinalized;

    private readonly EncoderMode encoderMode;

    /**
     *
     * @param request
     *            the request to encode
     * @param multipart
     *            True if the FORM is a ENCTYPE="multipart/form-data"
     * @throws NullPointerException
     *             for request
     * @throws ErrorDataEncoderException
     *             if the request is a TRACE
     */
    public HttpPostRequestEncoder(IHttpRequest request, bool multipart)
        : this(new DefaultHttpDataFactory(DefaultHttpDataFactory.MINSIZE), request, multipart,
                HttpConstants.DEFAULT_CHARSET, EncoderMode.RFC1738)
    {
    }

    /**
     *
     * @param factory
     *            the factory used to create InterfaceHttpData
     * @param request
     *            the request to encode
     * @param multipart
     *            True if the FORM is a ENCTYPE="multipart/form-data"
     * @throws NullPointerException
     *             for request and factory
     * @throws ErrorDataEncoderException
     *             if the request is a TRACE
     */
    public HttpPostRequestEncoder(IHttpDataFactory factory, IHttpRequest request, bool multipart)
        : this(factory, request, multipart, HttpConstants.DEFAULT_CHARSET, EncoderMode.RFC1738)
    {
    }

    /**
     *
     * @param factory
     *            the factory used to create InterfaceHttpData
     * @param request
     *            the request to encode
     * @param multipart
     *            True if the FORM is a ENCTYPE="multipart/form-data"
     * @param charset
     *            the charset to use as default
     * @param encoderMode
     *            the mode for the encoder to use. See {@link EncoderMode} for the details.
     * @throws NullPointerException
     *             for request or charset or factory
     * @throws ErrorDataEncoderException
     *             if the request is a TRACE
     */
    public HttpPostRequestEncoder(
            IHttpDataFactory factory, IHttpRequest request, bool multipart, Encoding charset,
            EncoderMode encoderMode)
    {
        this.request = CheckNotNull(request, "request");
        this.charset = CheckNotNull(charset, "charset");
        this.factory = CheckNotNull(factory, "factory");
        if (HttpMethod.TRACE.Equals(request.Method()))
        {
            throw new ErrorDataEncoderException("Cannot create a Encoder if request is a TRACE");
        }

        // Fill default values
        bodyListDatas = new List<IInterfaceHttpData>();
        // default mode
        isLastChunk = false;
        isLastChunkSent = false;
        isMultipart = multipart;
        multipartHttpDatas = new List<IInterfaceHttpData>();
        this.encoderMode = encoderMode;

        if (isMultipart)
        {
            InitDataMultipart();
        }
    }

    /**
     * Clean all HttpDatas (on Disk) for the current request.
     */
    public virtual void CleanFiles()
    {
        factory.CleanRequestHttpData(request);
    }

    /**
     * Does the last non empty chunk already encoded so that next chunk will be empty (last chunk)
     */
    private bool isLastChunk;
    /**
     * Last chunk already sent
     */
    private bool isLastChunkSent;
    /**
     * The current FileUpload that is currently in encode process
     */
    private IFileUpload currentFileUpload;
    /**
     * While adding a FileUpload, is the multipart currently in Mixed Mode
     */
    private bool duringMixedMode;
    /**
     * Global Body size
     */
    private long globalBodySize;
    /**
     * Global Transfer progress
     */
    private long globalProgress;

    /**
     * True if this request is a Multipart request
     *
     * @return True if this request is a Multipart request
     */
    public virtual bool IsMultipart()
    {
        return isMultipart;
    }

    /**
     * Init the delimiter for Global Part (Data).
     */
    private void InitDataMultipart()
    {
        multipartDataBoundary = GetNewMultipartDelimiter();
    }

    /**
     * Init the delimiter for Mixed Part (Mixed).
     */
    private void InitMixedMultipart()
    {
        multipartMixedBoundary = GetNewMultipartDelimiter();
    }

    /**
     *
     * @return a newly generated Delimiter (either for DATA or MIXED)
     */
    private static string GetNewMultipartDelimiter()
    {
        // construct a generated delimiter
        return unchecked((ulong) Random.Shared.NextInt64(long.MinValue, long.MaxValue))
                .ToString("x", CultureInfo.InvariantCulture);
    }

    /**
     * This getMethod returns a List of all InterfaceHttpData from body part.<br>

     * @return the list of InterfaceHttpData from Body part
     */
    public virtual IList<IInterfaceHttpData> GetBodyListAttributes()
    {
        return bodyListDatas;
    }

    /**
     * Set the Body HttpDatas list
     *
     * @throws NullPointerException
     *             for datas
     * @throws ErrorDataEncoderException
     *             if the encoding is in error or if the finalize were already done
     */
    public virtual void SetBodyHttpDatas(IList<IInterfaceHttpData> datas)
    {
        ObjectUtil.CheckNotNull(datas, "datas");

        globalBodySize = 0;
        bodyListDatas.Clear();
        currentFileUpload = null;
        duringMixedMode = false;
        multipartHttpDatas.Clear();

        foreach (IInterfaceHttpData data in datas)
        {
            AddBodyHttpData(data);
        }
    }

    /**
     * Add a simple attribute in the body as Name=Value
     *
     * @param name
     *            name of the parameter
     * @param value
     *            the value of the parameter
     * @throws NullPointerException
     *             for name
     * @throws ErrorDataEncoderException
     *             if the encoding is in error or if the finalize were already done
     */
    public virtual void AddBodyAttribute(string name, string value)
    {
        string svalue = value != null? value : StringUtil.EMPTY_STRING;
        IAttribute data = factory.CreateAttribute(request, CheckNotNull(name, "name"), svalue);
        AddBodyHttpData(data);
    }

    /**
     * Add a file as a FileUpload
     *
     * @param name
     *            the name of the parameter
     * @param file
     *            the file to be uploaded (if not Multipart mode, only the filename will be included)
     * @param contentType
     *            the associated contentType for the File
     * @param isText
     *            True if this file should be transmitted in Text format (else binary)
     * @throws NullPointerException
     *             for name and file
     * @throws ErrorDataEncoderException
     *             if the encoding is in error or if the finalize were already done
     */
    public virtual void AddBodyFileUpload(string name, FileInfo file, string contentType, bool isText)
    {
        AddBodyFileUpload(name, file.Name, file, contentType, isText);
    }

    /**
     * Add a file as a FileUpload
     *
     * @param name
     *            the name of the parameter
     * @param file
     *            the file to be uploaded (if not Multipart mode, only the filename will be included)
     * @param filename
     *            the filename to use for this File part, empty String will be ignored by
     *            the encoder
     * @param contentType
     *            the associated contentType for the File
     * @param isText
     *            True if this file should be transmitted in Text format (else binary)
     * @throws NullPointerException
     *             for name and file
     * @throws ErrorDataEncoderException
     *             if the encoding is in error or if the finalize were already done
     */
    public virtual void AddBodyFileUpload(string name, string filename, FileInfo file, string contentType, bool isText)
    {
        CheckNotNull(name, "name");
        CheckNotNull(file, "file");

        if (filename == null)
        {
            filename = StringUtil.EMPTY_STRING;
        }

        string scontentType = contentType;
        string contentTransferEncoding = null;

        if (contentType == null)
        {
            if (isText)
            {
                scontentType = HttpPostBodyUtil.DEFAULT_TEXT_CONTENT_TYPE;
            }
            else
            {
                scontentType = HttpPostBodyUtil.DEFAULT_BINARY_CONTENT_TYPE;
            }
        }

        if (!isText)
        {
            contentTransferEncoding = HttpPostBodyUtil.TransferEncodingMechanism.BINARY.Value();
        }

        IFileUpload fileUpload = factory.CreateFileUpload(request, name, filename, scontentType,
                contentTransferEncoding, null, AbstractDiskHttpData.FileLength(file));

        try
        {
            fileUpload.SetContent(file);
        }
        catch (IOException e)
        {
            throw new ErrorDataEncoderException(e);
        }

        AddBodyHttpData(fileUpload);
    }

    /**
     * Add a series of Files associated with one File parameter
     *
     * @param name
     *            the name of the parameter
     * @param file
     *            the array of files
     * @param contentType
     *            the array of content Types associated with each file
     * @param isText
     *            the array of isText attribute (False meaning binary mode) for each file
     * @throws IllegalArgumentException
     *             also throws if array have different sizes
     * @throws ErrorDataEncoderException
     *             if the encoding is in error or if the finalize were already done
     */
    public virtual void AddBodyFileUploads(string name, FileInfo[] file, string[] contentType, bool[] isText)
    {
        if (file.Length != contentType.Length && file.Length != isText.Length)
        {
            throw new ArgumentException("Different array length");
        }

        for (int i = 0; i < file.Length; i++)
        {
            AddBodyFileUpload(name, file[i], contentType[i], isText[i]);
        }
    }

    /**
     * Add the InterfaceHttpData to the Body list
     *
     * @throws NullPointerException
     *             for data
     * @throws ErrorDataEncoderException
     *             if the encoding is in error or if the finalize were already done
     */
    public virtual void AddBodyHttpData(IInterfaceHttpData data)
    {
        if (headerFinalized)
        {
            throw new ErrorDataEncoderException("Cannot add value once finalized");
        }

        bodyListDatas.Add(CheckNotNull(data, "data"));

        if (!isMultipart)
        {
            if (data is IAttribute)
            {
                IAttribute attribute = (IAttribute) data;

                try
                {
                    // name=value& with encoded name and attribute
                    string key = EncodeAttribute(attribute.GetName(), charset);
                    string value = EncodeAttribute(attribute.GetValue(), charset);
                    IAttribute newattribute = factory.CreateAttribute(request, key, value);
                    multipartHttpDatas.Add(newattribute);
                    globalBodySize += newattribute.GetName().Length + 1 + newattribute.Length() + 1;
                }
                catch (IOException e)
                {
                    throw new ErrorDataEncoderException(e);
                }
            }
            else if (data is IFileUpload)
            {
                // since not Multipart, only name=filename => Attribute
                IFileUpload fileUpload = (IFileUpload) data;
                // name=filename& with encoded name and filename
                string key = EncodeAttribute(fileUpload.GetName(), charset);
                string value = EncodeAttribute(fileUpload.GetFilename(), charset);
                IAttribute newattribute = factory.CreateAttribute(request, key, value);
                multipartHttpDatas.Add(newattribute);
                globalBodySize += newattribute.GetName().Length + 1 + newattribute.Length() + 1;
            }

            return;
        }

        /*
         * Logic:
         * if not Attribute:
         *      add Data to body list
         *      if (duringMixedMode)
         *          add endmixedmultipart delimiter
         *          currentFileUpload = null
         *          duringMixedMode = false;
         *      add multipart delimiter, multipart body header and Data to multipart list
         *      reset currentFileUpload, duringMixedMode
         * if FileUpload: take care of multiple file for one field => mixed mode
         *      if (duringMixedMode)
         *          if (currentFileUpload.name == data.name)
         *              add mixedmultipart delimiter, mixedmultipart body header and Data to multipart list
         *          else
         *              add endmixedmultipart delimiter, multipart body header and Data to multipart list
         *              currentFileUpload = data
         *              duringMixedMode = false;
         *      else
         *          if (currentFileUpload.name == data.name)
         *              change multipart body header of previous file into multipart list to
         *                      mixedmultipart start, mixedmultipart body header
         *              add mixedmultipart delimiter, mixedmultipart body header and Data to multipart list
         *              duringMixedMode = true
         *          else
         *              add multipart delimiter, multipart body header and Data to multipart list
         *              currentFileUpload = data
         *              duringMixedMode = false;
         * Do not add last delimiter! Could be:
         * if duringmixedmode: endmixedmultipart + endmultipart
         * else only endmultipart
         */
        if (data is IAttribute)
        {
            if (duringMixedMode)
            {
                InternalAttribute endMixedInternal = new InternalAttribute(charset);
                endMixedInternal.AddValue("\r\n--" + multipartMixedBoundary + "--");
                multipartHttpDatas.Add(endMixedInternal);
                multipartMixedBoundary = null;
                currentFileUpload = null;
                duringMixedMode = false;
            }

            InternalAttribute @internal = new InternalAttribute(charset);

            if (multipartHttpDatas.Count != 0)
            {
                // previously a data field so CRLF
                @internal.AddValue("\r\n");
            }

            @internal.AddValue("--" + multipartDataBoundary + "\r\n");
            // content-disposition: form-data; name="field1"
            IAttribute attribute = (IAttribute) data;
            @internal.AddValue(HttpHeaderNames.CONTENT_DISPOSITION + ": " + HttpHeaderValues.FORM_DATA + "; "
                    + HttpHeaderValues.NAME + "=\"" + attribute.GetName() + "\"\r\n");
            // Add Content-Length: xxx
            @internal.AddValue(HttpHeaderNames.CONTENT_LENGTH + ": " +
                    attribute.Length() + "\r\n");
            Encoding localcharset = attribute.GetCharset();
            if (localcharset != null)
            {
                // Content-Type: text/plain; charset=charset
                @internal.AddValue(HttpHeaderNames.CONTENT_TYPE + ": " +
                        HttpPostBodyUtil.DEFAULT_TEXT_CONTENT_TYPE + "; " +
                        HttpHeaderValues.CHARSET + '='
                        + HttpPostBodyUtil.CharsetName(localcharset) + "\r\n");
            }

            // CRLF between body header and data
            @internal.AddValue("\r\n");
            multipartHttpDatas.Add(@internal);
            multipartHttpDatas.Add(data);
            globalBodySize += attribute.Length() + @internal.Size();
        }
        else if (data is IFileUpload)
        {
            IFileUpload fileUpload = (IFileUpload) data;
            InternalAttribute @internal = new InternalAttribute(charset);

            if (multipartHttpDatas.Count != 0)
            {
                // previously a data field so CRLF
                @internal.AddValue("\r\n");
            }

            bool localMixed;

            if (duringMixedMode)
            {
                if (currentFileUpload != null && currentFileUpload.GetName().Equals(fileUpload.GetName()))
                {
                    // continue a mixed mode

                    localMixed = true;
                }
                else
                {
                    // end a mixed mode

                    // add endmixedmultipart delimiter, multipart body header
                    // and
                    // Data to multipart list
                    @internal.AddValue("--" + multipartMixedBoundary + "--");
                    multipartHttpDatas.Add(@internal);
                    multipartMixedBoundary = null;
                    // start a new one (could be replaced if mixed start again
                    // from here
                    @internal = new InternalAttribute(charset);
                    @internal.AddValue("\r\n");
                    localMixed = false;
                    // new currentFileUpload and no more in Mixed mode
                    currentFileUpload = fileUpload;
                    duringMixedMode = false;
                }
            }
            else
            {
                if (encoderMode != EncoderMode.HTML5 && currentFileUpload != null
                        && currentFileUpload.GetName().Equals(fileUpload.GetName()))
                {
                    // create a new mixed mode (from previous file)

                    // change multipart body header of previous file into
                    // multipart list to
                    // mixedmultipart start, mixedmultipart body header

                    // change Internal (size()-2 position in multipartHttpDatas)
                    // from (line starting with *)
                    // --AaB03x
                    // * Content-Disposition: form-data; name="files";
                    // filename="file1.txt"
                    // Content-Type: text/plain
                    // to (lines starting with *)
                    // --AaB03x
                    // * Content-Disposition: form-data; name="files"
                    // * Content-Type: multipart/mixed; boundary=BbC04y
                    // *
                    // * --BbC04y
                    // * Content-Disposition: attachment; filename="file1.txt"
                    // Content-Type: text/plain
                    InitMixedMultipart();
                    InternalAttribute pastAttribute = (InternalAttribute) multipartHttpDatas[multipartHttpDatas
                            .Count - 2];
                    // remove past size
                    globalBodySize -= pastAttribute.Size();
                    StringBuilder replacement = new StringBuilder(
                            139 + multipartDataBoundary.Length + multipartMixedBoundary.Length * 2 +
                                    fileUpload.GetFilename().Length + fileUpload.GetName().Length)

                        .Append("--")
                        .Append(multipartDataBoundary)
                        .Append("\r\n")

                        .Append(HttpHeaderNames.CONTENT_DISPOSITION)
                        .Append(": ")
                        .Append(HttpHeaderValues.FORM_DATA)
                        .Append("; ")
                        .Append(HttpHeaderValues.NAME)
                        .Append("=\"")
                        .Append(fileUpload.GetName())
                        .Append("\"\r\n")

                        .Append(HttpHeaderNames.CONTENT_TYPE)
                        .Append(": ")
                        .Append(HttpHeaderValues.MULTIPART_MIXED)
                        .Append("; ")
                        .Append(HttpHeaderValues.BOUNDARY)
                        .Append('=')
                        .Append(multipartMixedBoundary)
                        .Append("\r\n\r\n")

                        .Append("--")
                        .Append(multipartMixedBoundary)
                        .Append("\r\n")

                        .Append(HttpHeaderNames.CONTENT_DISPOSITION)
                        .Append(": ")
                        .Append(HttpHeaderValues.ATTACHMENT);

                    if (fileUpload.GetFilename().Length != 0)
                    {
                        replacement.Append("; ")
                                   .Append(HttpHeaderValues.FILENAME)
                                   .Append("=\"")
                                   .Append(currentFileUpload.GetFilename())
                                   .Append('"');
                    }

                    replacement.Append("\r\n");

                    pastAttribute.SetValue(replacement.ToString(), 1);
                    pastAttribute.SetValue("", 2);

                    // update past size
                    globalBodySize += pastAttribute.Size();

                    // now continue
                    // add mixedmultipart delimiter, mixedmultipart body header
                    // and
                    // Data to multipart list
                    localMixed = true;
                    duringMixedMode = true;
                }
                else
                {
                    // a simple new multipart
                    // add multipart delimiter, multipart body header and Data
                    // to multipart list
                    localMixed = false;
                    currentFileUpload = fileUpload;
                    duringMixedMode = false;
                }
            }

            if (localMixed)
            {
                // add mixedmultipart delimiter, mixedmultipart body header and
                // Data to multipart list
                @internal.AddValue("--" + multipartMixedBoundary + "\r\n");

                if (fileUpload.GetFilename().Length == 0)
                {
                    // Content-Disposition: attachment
                    @internal.AddValue(HttpHeaderNames.CONTENT_DISPOSITION + ": "
                            + HttpHeaderValues.ATTACHMENT + "\r\n");
                }
                else
                {
                    // Content-Disposition: attachment; filename="file1.txt"
                    @internal.AddValue(HttpHeaderNames.CONTENT_DISPOSITION + ": "
                            + HttpHeaderValues.ATTACHMENT + "; "
                            + HttpHeaderValues.FILENAME + "=\"" + fileUpload.GetFilename() + "\"\r\n");
                }
            }
            else
            {
                @internal.AddValue("--" + multipartDataBoundary + "\r\n");

                if (fileUpload.GetFilename().Length == 0)
                {
                    // Content-Disposition: form-data; name="files";
                    @internal.AddValue(HttpHeaderNames.CONTENT_DISPOSITION + ": " + HttpHeaderValues.FORM_DATA + "; "
                            + HttpHeaderValues.NAME + "=\"" + fileUpload.GetName() + "\"\r\n");
                }
                else
                {
                    // Content-Disposition: form-data; name="files";
                    // filename="file1.txt"
                    @internal.AddValue(HttpHeaderNames.CONTENT_DISPOSITION + ": " + HttpHeaderValues.FORM_DATA + "; "
                            + HttpHeaderValues.NAME + "=\"" + fileUpload.GetName() + "\"; "
                            + HttpHeaderValues.FILENAME + "=\"" + fileUpload.GetFilename() + "\"\r\n");
                }
            }

            // Add Content-Length: xxx
            @internal.AddValue(HttpHeaderNames.CONTENT_LENGTH + ": " +
                    fileUpload.Length() + "\r\n");
            // Content-Type: image/gif
            // Content-Type: text/plain; charset=ISO-8859-1
            // Content-Transfer-Encoding: binary
            @internal.AddValue(HttpHeaderNames.CONTENT_TYPE + ": " + fileUpload.GetContentType());
            string contentTransferEncoding = fileUpload.GetContentTransferEncoding();
            if (contentTransferEncoding != null
                    && contentTransferEncoding.Equals(HttpPostBodyUtil.TransferEncodingMechanism.BINARY.Value()))
            {
                @internal.AddValue("\r\n" + HttpHeaderNames.CONTENT_TRANSFER_ENCODING + ": "
                        + HttpPostBodyUtil.TransferEncodingMechanism.BINARY.Value() + "\r\n\r\n");
            }
            else if (fileUpload.GetCharset() != null)
            {
                @internal.AddValue("; " + HttpHeaderValues.CHARSET + '=' +
                        HttpPostBodyUtil.CharsetName(fileUpload.GetCharset()) + "\r\n\r\n");
            }
            else
            {
                @internal.AddValue("\r\n\r\n");
            }

            multipartHttpDatas.Add(@internal);
            multipartHttpDatas.Add(data);
            globalBodySize += fileUpload.Length() + @internal.Size();
        }
    }

    /**
     * Iterator to be used when encoding will be called chunk after chunk
     */
    private int iterator;

    /**
     * Finalize the request by preparing the Header in the request and returns the request ready to be sent.<br>
     * Once finalized, no data must be added.<br>
     * If the request does not need chunk (isChunked() == false), this request is the only object to send to the remote
     * server.
     *
     * @return the request object (chunked or not according to size of body)
     * @throws ErrorDataEncoderException
     *             if the encoding is in error or if the finalize were already done
     */
    public virtual IHttpRequest FinalizeRequest()
    {
        // Finalize the multipartHttpDatas
        if (!headerFinalized)
        {
            if (isMultipart)
            {
                InternalAttribute @internal = new InternalAttribute(charset);

                if (duringMixedMode)
                {
                    @internal.AddValue("\r\n--" + multipartMixedBoundary + "--");
                }

                @internal.AddValue("\r\n--" + multipartDataBoundary + "--\r\n");
                multipartHttpDatas.Add(@internal);
                multipartMixedBoundary = null;
                currentFileUpload = null;
                duringMixedMode = false;
                globalBodySize += @internal.Size();
            }

            headerFinalized = true;
        }
        else
        {
            throw new ErrorDataEncoderException("Header already encoded");
        }

        HttpHeaders headers = request.Headers();
        IList<string> contentTypes = headers.GetAll(HttpHeaderNames.CONTENT_TYPE);
        IList<string> transferEncoding = headers.GetAll(HttpHeaderNames.TRANSFER_ENCODING);

        if (contentTypes != null)
        {
            headers.Remove(HttpHeaderNames.CONTENT_TYPE);

            foreach (string contentType in contentTypes)
            {
                // "multipart/form-data; boundary=--89421926422648"
                // toLowerCase() without a Locale would corrupt the comparison under Turkish
                // (tr_TR) locale, where 'I' -> 'ı' (U+0131): a request that sets
                // Content-Type: MULTIPART/form-data would lowercase to "multıpart/form-data" and
                // miss the prefix check, leaving the original header in place alongside the
                // multipart one this encoder is about to add.
                string lowercased = contentType.ToLowerInvariant();
                if (lowercased.StartsWith(HttpHeaderValues.MULTIPART_FORM_DATA.ToString(), StringComparison.Ordinal) ||
                        lowercased.StartsWith(HttpHeaderValues.APPLICATION_X_WWW_FORM_URLENCODED.ToString(),
                            StringComparison.Ordinal))
                {
                    // ignore
                }
                else
                {
                    headers.Add(HttpHeaderNames.CONTENT_TYPE, contentType);
                }
            }
        }

        if (isMultipart)
        {
            string value = HttpHeaderValues.MULTIPART_FORM_DATA + "; " + HttpHeaderValues.BOUNDARY + '='
                    + multipartDataBoundary;
            headers.Add(HttpHeaderNames.CONTENT_TYPE, value);
        }
        else
        {
            // Not multipart
            headers.Add(HttpHeaderNames.CONTENT_TYPE, HttpHeaderValues.APPLICATION_X_WWW_FORM_URLENCODED);
        }

        // Now consider size for chunk or not
        long realSize = globalBodySize;

        if (!isMultipart)
        {
            realSize -= 1; // last '&' removed
        }

        iterator = 0;

        headers.Set(HttpHeaderNames.CONTENT_LENGTH, realSize.ToString(CultureInfo.InvariantCulture));

        if (realSize > HttpPostBodyUtil.chunkSize || isMultipart)
        {
            isChunked = true;

            if (transferEncoding != null)
            {
                headers.Remove(HttpHeaderNames.TRANSFER_ENCODING);

                foreach (string v in transferEncoding)
                {
                    if (HttpHeaderValues.CHUNKED.ContentEqualsIgnoreCase(new StringCharSequence(v)))
                    {
                        // ignore
                    }
                    else
                    {
                        headers.Add(HttpHeaderNames.TRANSFER_ENCODING, v);
                    }
                }
            }

            HttpUtil.SetTransferEncodingChunked(request, true);

            // wrap to hide the possible content
            return new WrappedHttpRequest(request);
        }
        else
        {
            // get the only one body and set it to the request
            IHttpContent chunk = NextChunk();

            if (request is IFullHttpRequest)
            {
                IFullHttpRequest fullRequest = (IFullHttpRequest) request;
                ByteBuf chunkContent = chunk.Content;
                if (fullRequest.Content != chunkContent)
                {
                    fullRequest.Content.Clear().WriteBytes(chunkContent);
                    chunkContent.Release();
                }

                return fullRequest;
            }
            else
            {
                return new WrappedFullHttpRequest(request, chunk);
            }
        }
    }

    /**
     * @return True if the request is by Chunk
     */
    public virtual bool IsChunked()
    {
        return isChunked;
    }

    /**
     * Encode one attribute
     *
     * @return the encoded attribute
     * @throws ErrorDataEncoderException
     *             if the encoding is in error
     */
    private string EncodeAttribute(string s, Encoding charset)
    {
        if (s == null)
        {
            return "";
        }

        string encoded = UrlEncode(s, charset);

        if (encoderMode == EncoderMode.RFC3986)
        {
            encoded = encoded.Replace(ASTERISK, ASTERISK_REPLACEMENT)
                             .Replace(PLUS, PLUS_REPLACEMENT)
                             .Replace(TILDE, TILDE_REPLACEMENT);
        }

        return encoded;
    }

    /**
     * The ByteBuf currently used by the encoder
     */
    private ByteBuf currentBuffer;
    /**
     * The current InterfaceHttpData to encode (used if more chunks are available)
     */
    private IInterfaceHttpData currentData;
    /**
     * If not multipart, does the currentBuffer stands for the Key or for the Value
     */
    private bool isKey = true;

    /**
     *
     * @return the next ByteBuf to send as an HttpChunk and modifying currentBuffer accordingly
     */
    private ByteBuf FillByteBuf()
    {
        int length = currentBuffer.ReadableBytes;
        if (length > HttpPostBodyUtil.chunkSize)
        {
            return currentBuffer.ReadRetainedSlice(HttpPostBodyUtil.chunkSize);
        }
        else
        {
            // to continue
            ByteBuf slice = currentBuffer;
            currentBuffer = null;
            return slice;
        }
    }

    /**
     * From the current context (currentBuffer and currentData), returns the next HttpChunk (if possible) trying to get
     * sizeleft bytes more into the currentBuffer. This is the Multipart version.
     *
     * @param sizeleft
     *            the number of bytes to try to get from currentData
     * @return the next HttpChunk or null if not enough bytes were found
     * @throws ErrorDataEncoderException
     *             if the encoding is in error
     */
    private IHttpContent EncodeNextChunkMultipart(int sizeleft)
    {
        if (currentData == null)
        {
            return null;
        }

        ByteBuf buffer;

        if (currentData is InternalAttribute)
        {
            buffer = ((InternalAttribute) currentData).ToByteBuf();
            currentData = null;
        }
        else
        {
            try
            {
                buffer = ((IHttpData) currentData).GetChunk(sizeleft);
            }
            catch (IOException e)
            {
                throw new ErrorDataEncoderException(e);
            }

            if (buffer.Capacity == 0)
            {
                // end for current InterfaceHttpData, need more data
                currentData = null;
                return null;
            }
        }

        if (currentBuffer == null)
        {
            currentBuffer = buffer;
        }
        else
        {
            currentBuffer = Unpooled.WrappedBuffer(currentBuffer, buffer);
        }

        if (currentBuffer.ReadableBytes < HttpPostBodyUtil.chunkSize)
        {
            currentData = null;
            return null;
        }

        buffer = FillByteBuf();
        return new DefaultHttpContent(buffer);
    }

    /**
     * From the current context (currentBuffer and currentData), returns the next HttpChunk (if possible) trying to get
     * sizeleft bytes more into the currentBuffer. This is the UrlEncoded version.
     *
     * @param sizeleft
     *            the number of bytes to try to get from currentData
     * @return the next HttpChunk or null if not enough bytes were found
     * @throws ErrorDataEncoderException
     *             if the encoding is in error
     */
    private IHttpContent EncodeNextChunkUrlEncoded(int sizeleft)
    {
        if (currentData == null)
        {
            return null;
        }

        int size = sizeleft;
        ByteBuf buffer;

        // Set name=
        if (isKey)
        {
            string key = currentData.GetName();
            buffer = Unpooled.WrappedBuffer(charset.GetBytes(key));
            isKey = false;

            if (currentBuffer == null)
            {
                currentBuffer = Unpooled.WrappedBuffer(buffer, Unpooled.WrappedBuffer(charset.GetBytes("=")));
            }
            else
            {
                currentBuffer = Unpooled.WrappedBuffer(currentBuffer, buffer, Unpooled.WrappedBuffer(charset.GetBytes("=")));
            }

            // continue
            size -= buffer.ReadableBytes + 1;

            if (currentBuffer.ReadableBytes >= HttpPostBodyUtil.chunkSize)
            {
                buffer = FillByteBuf();
                return new DefaultHttpContent(buffer);
            }
        }

        // Put value into buffer
        try
        {
            buffer = ((IHttpData) currentData).GetChunk(size);
        }
        catch (IOException e)
        {
            throw new ErrorDataEncoderException(e);
        }

        // Figure out delimiter
        ByteBuf delimiter = null;

        if (buffer.ReadableBytes < size)
        {
            isKey = true;
            currentData = null;
            delimiter = iterator < multipartHttpDatas.Count ? Unpooled.WrappedBuffer(charset.GetBytes("&")) : null;
        }

        // End for current InterfaceHttpData, need potentially more data
        if (buffer.Capacity == 0)
        {
            isKey = true;
            currentData = null;

            if (currentBuffer == null)
            {
                if (delimiter == null)
                {
                    return null;
                }
                else
                {
                    currentBuffer = delimiter;
                }
            }
            else
            {
                if (delimiter != null)
                {
                    currentBuffer = Unpooled.WrappedBuffer(currentBuffer, delimiter);
                }
            }

            if (currentBuffer.ReadableBytes >= HttpPostBodyUtil.chunkSize)
            {
                buffer = FillByteBuf();
                return new DefaultHttpContent(buffer);
            }

            return null;
        }

        // Put it all together: name=value&
        if (currentBuffer == null)
        {
            if (delimiter != null)
            {
                currentBuffer = Unpooled.WrappedBuffer(buffer, delimiter);
            }
            else
            {
                currentBuffer = buffer;
            }
        }
        else
        {
            if (delimiter != null)
            {
                currentBuffer = Unpooled.WrappedBuffer(currentBuffer, buffer, delimiter);
            }
            else
            {
                currentBuffer = Unpooled.WrappedBuffer(currentBuffer, buffer);
            }
        }

        if (currentBuffer.ReadableBytes >= HttpPostBodyUtil.chunkSize)
        {
            return new DefaultHttpContent(FillByteBuf());
        }

        return null;
    }

    public virtual void Close()
    {
        // NO since the user can want to reuse (broadcast for instance)
        // cleanFiles();
    }

    /**
     * Returns the next available HttpChunk. The caller is responsible to test if this chunk is the last one (isLast()),
     * in order to stop calling this getMethod.
     *
     * @return the next available HttpChunk
     * @throws ErrorDataEncoderException
     *             if the encoding is in error
     */
    public virtual IHttpContent ReadChunk(IByteBufAllocator allocator)
    {
        if (isLastChunkSent)
        {
            return null;
        }
        else
        {
            IHttpContent nextChunk = NextChunk();
            globalProgress += nextChunk.Content.ReadableBytes;
            return nextChunk;
        }
    }

    /**
     * Returns the next available HttpChunk. The caller is responsible to test if this chunk is the last one (isLast()),
     * in order to stop calling this getMethod.
     *
     * @return the next available HttpChunk
     * @throws ErrorDataEncoderException
     *             if the encoding is in error
     */
    private IHttpContent NextChunk()
    {
        if (isLastChunk)
        {
            isLastChunkSent = true;
            return ILastHttpContent.EMPTY_LAST_CONTENT;
        }

        // first test if previous buffer is not empty
        int size = CalculateRemainingSize();
        if (size <= 0)
        {
            // NextChunk from buffer
            ByteBuf buffer = FillByteBuf();
            return new DefaultHttpContent(buffer);
        }

        // size > 0
        if (currentData != null)
        {
            // continue to read data
            IHttpContent chunk;

            if (isMultipart)
            {
                chunk = EncodeNextChunkMultipart(size);
            }
            else
            {
                chunk = EncodeNextChunkUrlEncoded(size);
            }

            if (chunk != null)
            {
                // NextChunk from data
                return chunk;
            }

            size = CalculateRemainingSize();
        }

        if (!(iterator < multipartHttpDatas.Count))
        {
            return LastChunk();
        }

        while (size > 0 && iterator < multipartHttpDatas.Count)
        {
            currentData = multipartHttpDatas[iterator++];
            IHttpContent chunk;

            if (isMultipart)
            {
                chunk = EncodeNextChunkMultipart(size);
            }
            else
            {
                chunk = EncodeNextChunkUrlEncoded(size);
            }

            if (chunk == null)
            {
                // not enough
                size = CalculateRemainingSize();
                continue;
            }

            // NextChunk from data
            return chunk;
        }

        // end since no more data
        return LastChunk();
    }

    private int CalculateRemainingSize()
    {
        int size = HttpPostBodyUtil.chunkSize;

        if (currentBuffer != null)
        {
            size -= currentBuffer.ReadableBytes;
        }

        return size;
    }

    private IHttpContent LastChunk()
    {
        isLastChunk = true;

        if (currentBuffer == null)
        {
            isLastChunkSent = true;
            // LastChunk with no more data
            return ILastHttpContent.EMPTY_LAST_CONTENT;
        }

        // NextChunk as last non empty from buffer
        ByteBuf buffer = currentBuffer;
        currentBuffer = null;
        return new DefaultHttpContent(buffer);
    }

    public virtual bool IsEndOfInput()
    {
        return isLastChunkSent;
    }

    public virtual long Length()
    {
        return isMultipart? globalBodySize : globalBodySize - 1;
    }

    public virtual long Progress()
    {
        return globalProgress;
    }

    // CLR: URLEncoder.encode(String, String) (see the class comment).
    private static string UrlEncode(string s, Encoding charset)
    {
        StringBuilder @out = new StringBuilder(s.Length);
        int i = 0;

        while (i < s.Length)
        {
            char c = s[i];
            if (DontNeedEncoding(c))
            {
                @out.Append(c == ' ' ? '+' : c);
                i++;
            }
            else
            {
                int start = i;

                do
                {
                    i++;
                }
                while (i < s.Length && !DontNeedEncoding(s[i]));

                byte[] ba = charset.GetBytes(s.Substring(start, i - start));

                foreach (byte b in ba)
                {
                    @out.Append('%').Append(UPPER_HEX_DIGITS[b >> 4]).Append(UPPER_HEX_DIGITS[b & 0xF]);
                }
            }
        }

        return @out.ToString();
    }

    private const string UPPER_HEX_DIGITS = "0123456789ABCDEF";

    private static bool DontNeedEncoding(char c)
    {
        return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' ||
               c == ' ' || c == '-' || c == '_' || c == '.' || c == '*';
    }

    /**
     * Exception when an error occurs while encoding
     */
    public class ErrorDataEncoderException : Exception
    {

        public ErrorDataEncoderException()
        {
        }

        public ErrorDataEncoderException(string msg) : base(msg)
        {
        }

        public ErrorDataEncoderException(Exception cause) : base(ThrowableUtil.ThrowableToString(cause), cause)
        {
        }

        public ErrorDataEncoderException(string msg, Exception cause) : base(msg, cause)
        {
        }
    }

    private class WrappedHttpRequest : IHttpRequest
    {
        private readonly IHttpRequest request;
        internal WrappedHttpRequest(IHttpRequest request)
        {
            this.request = request;
        }

        public virtual IHttpRequest SetProtocolVersion(HttpVersion version)
        {
            request.SetProtocolVersion(version);
            return this;
        }

        public virtual IHttpRequest SetMethod(HttpMethod method)
        {
            request.SetMethod(method);
            return this;
        }

        public virtual IHttpRequest SetUri(string uri)
        {
            request.SetUri(uri);
            return this;
        }

        public HttpMethod Method()
        {
            return request.Method();
        }

        public string Uri()
        {
            return request.Uri();
        }

        public HttpVersion ProtocolVersion()
        {
            return request.ProtocolVersion();
        }

        public HttpHeaders Headers()
        {
            return request.Headers();
        }

        public global::Netty.NET.Handler.Codec.DecoderResult DecoderResult()
        {
            return request.DecoderResult();
        }

        public void SetDecoderResult(global::Netty.NET.Handler.Codec.DecoderResult result)
        {
            request.SetDecoderResult(result);
        }

        IHttpMessage IHttpMessage.SetProtocolVersion(HttpVersion version)
        {
            return SetProtocolVersion(version);
        }
    }

    private sealed class WrappedFullHttpRequest : WrappedHttpRequest, IFullHttpRequest
    {
        private readonly IHttpContent content;

        internal WrappedFullHttpRequest(IHttpRequest request, IHttpContent content) : base(request)
        {
            this.content = content;
        }

        public override IFullHttpRequest SetProtocolVersion(HttpVersion version)
        {
            base.SetProtocolVersion(version);
            return this;
        }

        public override IFullHttpRequest SetMethod(HttpMethod method)
        {
            base.SetMethod(method);
            return this;
        }

        public override IFullHttpRequest SetUri(string uri)
        {
            base.SetUri(uri);
            return this;
        }

        public IFullHttpRequest Copy()
        {
            return Replace(Content.Copy());
        }

        public IFullHttpRequest Duplicate()
        {
            return Replace(Content.Duplicate());
        }

        public IFullHttpRequest RetainedDuplicate()
        {
            return Replace(Content.RetainedDuplicate());
        }

        public IFullHttpRequest Replace(ByteBuf content)
        {
            DefaultFullHttpRequest duplicate = new DefaultFullHttpRequest(ProtocolVersion(), Method(), Uri(), content);
            duplicate.Headers().Set(Headers());
            duplicate.TrailingHeaders().Set(TrailingHeaders());
            return duplicate;
        }

        public IFullHttpRequest Retain(int increment)
        {
            content.Retain(increment);
            return this;
        }

        public IFullHttpRequest Retain()
        {
            content.Retain();
            return this;
        }

        public IFullHttpRequest Touch()
        {
            content.Touch();
            return this;
        }

        public IFullHttpRequest Touch(object hint)
        {
            content.Touch(hint);
            return this;
        }

        public ByteBuf Content => content.Content;

        public HttpHeaders TrailingHeaders()
        {
            if (content is ILastHttpContent)
            {
                return ((ILastHttpContent) content).TrailingHeaders();
            }
            else
            {
                return EmptyHttpHeaders.INSTANCE;
            }
        }

        public int RefCnt => content.RefCnt;

        public bool Release()
        {
            return content.Release();
        }

        public bool Release(int decrement)
        {
            return content.Release(decrement);
        }

        IByteBufHolder IByteBufHolder.Copy()
        {
            return Copy();
        }

        IByteBufHolder IByteBufHolder.Duplicate()
        {
            return Duplicate();
        }

        IByteBufHolder IByteBufHolder.RetainedDuplicate()
        {
            return RetainedDuplicate();
        }

        IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
        {
            return Replace(content);
        }

        IByteBufHolder IByteBufHolder.Retain()
        {
            return Retain();
        }

        IByteBufHolder IByteBufHolder.Retain(int increment)
        {
            return Retain(increment);
        }

        IByteBufHolder IByteBufHolder.Touch()
        {
            return Touch();
        }

        IByteBufHolder IByteBufHolder.Touch(object hint)
        {
            return Touch(hint);
        }

        IReferenceCounted IReferenceCounted.Retain()
        {
            return Retain();
        }

        IReferenceCounted IReferenceCounted.Retain(int increment)
        {
            return Retain(increment);
        }

        IReferenceCounted IReferenceCounted.Touch()
        {
            return Touch();
        }

        IReferenceCounted IReferenceCounted.Touch(object hint)
        {
            return Touch(hint);
        }
    }
}
