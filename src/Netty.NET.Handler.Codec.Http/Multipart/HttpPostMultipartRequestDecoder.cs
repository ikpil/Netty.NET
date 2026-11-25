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
using System.IO;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.Multipart.HttpPostBodyUtil;
using static Netty.NET.Handler.Codec.Http.Multipart.HttpPostRequestDecoder;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * This decoder will decode Body and can handle POST BODY.
 *
 * You <strong>MUST</strong> call {@link #destroy()} after completion to release all resources.
 *
 */
// CLR: HttpRequest is IHttpRequest, HttpContent IHttpContent, LastHttpContent ILastHttpContent and Charset Encoding;
// Charset.forName is Common's CharsetUtil.ForName (Java's standard charset names, then Encoding.GetEncoding), whose
// ArgumentException stands for both IllegalCharsetNameException and UnsupportedCharsetException, so the catches of either
// one catch ArgumentException: the constructor therefore also wraps an unsupported (legal) charset name in
// ErrorDataDecoderException, which Java lets through as UnsupportedCharsetException, and the FIELD/FILEUPLOAD steps also
// wrap an illegal name, which Java lets through as IllegalCharsetNameException. CharsetUtil.US_ASCII/ISO_8859_1 are
// Encoding.ASCII/Latin1. The TreeMaps with CaseIgnoringComparator.INSTANCE are SortedDictionary instances with its
// IComparer<string>; the Map<CharSequence, Attribute> of the field attributes is keyed by the string text of the
// AsciiString/String keys, which compare alike under that comparator, and map.get() returning null is the Get helper.
// offer(HttpContent) returns this class, so IInterfaceHttpPostRequestDecoder.Offer is implemented explicitly. The
// constructor's catch (Throwable) with PlatformDependent.throwException(e) is catch (Exception) with a rethrow;
// IllegalStateException is InvalidOperationException. The Buffer port throws ArgumentOutOfRangeException where Java
// throws IndexOutOfBoundsException (docs/codec-dns-porting.md), and catch (NullPointerException | IllegalArgumentException)
// keeps the Java types with HttpPostBodyUtil.IsNullPointer/IsIllegalArgument; ArrayIndexOutOfBoundsException is
// IndexOutOfRangeException and NumberFormatException FormatException. Long.parseLong is core's HttpUtil.ParseLong,
// toLowerCase(Locale.US) ToLowerInvariant ("the casing rules of the invariant culture",
// https://learn.microsoft.com/en-us/dotnet/api/system.string.tolowerinvariant?view=net-10.0), String.trim() core's
// HttpUtil.TrimJava, Character.isISOControl char.IsControl (U+0000-U+001F and U+007F-U+009F,
// https://learn.microsoft.com/en-us/dotnet/api/system.char.iscontrol?view=net-10.0) and Character.isWhitespace core's
// HttpVersion.IsJavaWhitespace. String.split(regex, limit) with a positive limit is string.Split(char, count), and
// split(regex) with limit 0, which discards trailing empty strings
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), is SplitDiscardingTrailingEmpty.
// String concatenation of a null String gives "null" in Java, which the mixed boundary keeps. The switch of cleanString
// casts the byte constants of HttpConstants to char, because a C# case label must convert implicitly to char. readByte()
// and readUnsignedByte() are the unsigned ReadByte() of the Buffer port; every byte comparison is with an ASCII
// constant, so the Java sign does not change any result. InternalThreadLocalMap.get().arrayList(1) is a local list
// (as the Common AsciiString.Split does, src/Netty.NET.Common/AsciiString.cs). The second and third ByteBuf content
// locals of loadDataMultipartOptimized are content2/content3, because a C# local's scope is its whole block and the
// nested blocks before declare the same name (CS0136).
public class HttpPostMultipartRequestDecoder : IInterfaceHttpPostRequestDecoder
{

    /**
     * Factory used to create InterfaceHttpData
     */
    private readonly IHttpDataFactory factory;

    /**
     * Request to decode
     */
    private readonly IHttpRequest request;

    /**
     * The maximum number of fields allows by the form
     */
    private readonly int maxFields;

    /**
     * The maximum number of accumulated bytes when decoding a field
     */
    private readonly int maxBufferedBytes;

    /**
     * Default charset to use
     */
    private Encoding charset;

    /**
     * Does the last chunk already received
     */
    private bool isLastChunk;

    /**
     * HttpDatas from Body
     */
    private readonly List<IInterfaceHttpData> bodyListHttpData = new List<IInterfaceHttpData>();

    /**
     * HttpDatas as Map from Body
     */
    private readonly SortedDictionary<string, List<IInterfaceHttpData>> bodyMapHttpData =
            new SortedDictionary<string, List<IInterfaceHttpData>>(CaseIgnoringComparator.INSTANCE);

    /**
     * The current channelBuffer
     */
    private ByteBuf undecodedChunk;

    /**
     * Body HttpDatas current position
     */
    private int bodyListHttpDataRank;

    /**
     * If multipart, this is the boundary for the global multipart
     */
    private readonly string multipartDataBoundary;

    /**
     * If multipart, there could be internal multiparts (mixed) to the global
     * multipart. Only one level is allowed.
     */
    private string multipartMixedBoundary;

    /**
     * Current getStatus
     */
    private MultiPartStatus currentStatus = MultiPartStatus.NOTSTARTED;

    /**
     * Used in Multipart
     */
    private SortedDictionary<string, IAttribute> currentFieldAttributes;

    /**
     * The current FileUpload that is currently in decode process
     */
    private IFileUpload currentFileUpload;

    /**
     * The current Attribute that is currently in decode process
     */
    private IAttribute currentAttribute;

    private bool destroyed;

    private int discardThreshold = HttpPostRequestDecoder.DEFAULT_DISCARD_THRESHOLD;

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
    public HttpPostMultipartRequestDecoder(IHttpRequest request)
        : this(new DefaultHttpDataFactory(DefaultHttpDataFactory.MINSIZE), request, HttpConstants.DEFAULT_CHARSET)
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
    public HttpPostMultipartRequestDecoder(IHttpDataFactory factory, IHttpRequest request)
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
    public HttpPostMultipartRequestDecoder(IHttpDataFactory factory, IHttpRequest request, Encoding charset)
        : this(factory, request, charset, HttpPostRequestDecoder.DEFAULT_MAX_FIELDS,
                HttpPostRequestDecoder.DEFAULT_MAX_BUFFERED_BYTES)
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
    public HttpPostMultipartRequestDecoder(IHttpDataFactory factory, IHttpRequest request, Encoding charset,
                                           int maxFields, int maxBufferedBytes)
    {
        this.request = CheckNotNull(request, "request");
        this.charset = CheckNotNull(charset, "charset");
        this.factory = CheckNotNull(factory, "factory");
        this.maxFields = maxFields;
        this.maxBufferedBytes = maxBufferedBytes;
        // Fill default values

        string contentTypeValue = this.request.Headers().Get(HttpHeaderNames.CONTENT_TYPE);
        if (contentTypeValue == null || contentTypeValue.Length == 0)
        {
            // Content-Type must be present and cannot have an empty value, because it defines the data boundary value.
            throw new ErrorDataDecoderException(
                "No '" + HttpHeaderNames.CONTENT_TYPE + "' header present, or value is empty.");
        }

        string[] dataBoundary = HttpPostRequestDecoder.GetMultipartDataBoundary(contentTypeValue);
        if (dataBoundary != null)
        {
            multipartDataBoundary = dataBoundary[0];
            if (dataBoundary.Length > 1 && dataBoundary[1] != null)
            {
                try
                {
                    this.charset = CharsetUtil.ForName(dataBoundary[1]);
                }
                catch (Exception e) when (IsIllegalArgument(e))
                {
                    throw new ErrorDataDecoderException(e);
                }
            }
        }
        else
        {
            multipartDataBoundary = null;
        }
        currentStatus = MultiPartStatus.HEADERDELIMITER;

        try
        {
            if (request is IHttpContent)
            {
                // Offer automatically if the given request is als type of HttpContent
                // See #1089
                Offer((IHttpContent) request);
            }
            else
            {
                ParseBody();
            }
        }
        catch (Exception)
        {
            Destroy();
            throw;
        }
    }

    private void CheckDestroyed()
    {
        if (destroyed)
        {
            throw new InvalidOperationException(nameof(HttpPostMultipartRequestDecoder)
                    + " was destroyed already");
        }
    }

    /**
     * True if this request is a Multipart request
     *
     * @return True if this request is a Multipart request
     */
    public virtual bool IsMultipart()
    {
        CheckDestroyed();
        return true;
    }

    /**
     * Set the amount of bytes after which read bytes in the buffer should be discarded.
     * Setting this lower gives lower memory usage but with the overhead of more memory copies.
     * Use {@code 0} to disable it.
     */
    public virtual void SetDiscardThreshold(int discardThreshold)
    {
        this.discardThreshold = CheckPositiveOrZero(discardThreshold, "discardThreshold");
    }

    /**
     * Return the threshold in bytes after which read data in the buffer should be discarded.
     */
    public virtual int GetDiscardThreshold()
    {
        return discardThreshold;
    }

    /**
     * This getMethod returns a List of all HttpDatas from body.<br>
     *
     * If chunked, all chunks must have been offered using offer() getMethod. If
     * not, NotEnoughDataDecoderException will be raised.
     *
     * @return the list of HttpDatas from Body part for POST getMethod
     * @throws NotEnoughDataDecoderException
     *             Need more chunks
     */
    public virtual IList<IInterfaceHttpData> GetBodyHttpDatas()
    {
        CheckDestroyed();

        if (!isLastChunk)
        {
            throw new NotEnoughDataDecoderException();
        }
        return bodyListHttpData;
    }

    /**
     * This getMethod returns a List of all HttpDatas with the given name from
     * body.<br>
     *
     * If chunked, all chunks must have been offered using offer() getMethod. If
     * not, NotEnoughDataDecoderException will be raised.
     *
     * @return All Body HttpDatas with the given name (ignore case)
     * @throws NotEnoughDataDecoderException
     *             need more chunks
     */
    public virtual IList<IInterfaceHttpData> GetBodyHttpDatas(string name)
    {
        CheckDestroyed();

        if (!isLastChunk)
        {
            throw new NotEnoughDataDecoderException();
        }
        return bodyMapHttpData.TryGetValue(name, out List<IInterfaceHttpData> list) ? list : null;
    }

    /**
     * This getMethod returns the first InterfaceHttpData with the given name from
     * body.<br>
     *
     * If chunked, all chunks must have been offered using offer() getMethod. If
     * not, NotEnoughDataDecoderException will be raised.
     *
     * @return The first Body InterfaceHttpData with the given name (ignore
     *         case)
     * @throws NotEnoughDataDecoderException
     *             need more chunks
     */
    public virtual IInterfaceHttpData GetBodyHttpData(string name)
    {
        CheckDestroyed();

        if (!isLastChunk)
        {
            throw new NotEnoughDataDecoderException();
        }
        bodyMapHttpData.TryGetValue(name, out List<IInterfaceHttpData> list);
        if (list != null)
        {
            return list[0];
        }
        return null;
    }

    /**
     * Initialized the internals from a new chunk
     *
     * @param content
     *            the new received chunk
     * @throws ErrorDataDecoderException
     *             if there is a problem with the charset decoding or other
     *             errors
     */
    public virtual HttpPostMultipartRequestDecoder Offer(IHttpContent content)
    {
        CheckDestroyed();

        if (content is ILastHttpContent)
        {
            isLastChunk = true;
        }

        ByteBuf buf = content.Content;
        if (undecodedChunk == null)
        {
            undecodedChunk =
                    // Since the Handler will release the incoming later on, we need to copy it
                    //
                    // We are explicit allocate a buffer and NOT calling copy() as otherwise it may set a maxCapacity
                    // which is not really usable for us as we may exceed it once we add more bytes.
                    buf.Alloc().Buffer(buf.ReadableBytes).WriteBytes(buf);
        }
        else
        {
            undecodedChunk.WriteBytes(buf);
        }
        ParseBody();
        if (maxBufferedBytes > 0 && undecodedChunk != null && undecodedChunk.ReadableBytes > maxBufferedBytes)
        {
            throw new HttpPostRequestDecoder.TooLongFormFieldException();
        }
        if (undecodedChunk != null && undecodedChunk.WriterIndex > discardThreshold)
        {
            if (undecodedChunk.RefCnt == 1)
            {
                // It's safe to call discardBytes() as we are the only owner of the buffer.
                undecodedChunk.DiscardReadBytes();
            }
            else
            {
                // There seems to be multiple references of the buffer. Let's copy the data and release the buffer to
                // ensure we can give back memory to the system.
                ByteBuf buffer = undecodedChunk.Alloc().Buffer(undecodedChunk.ReadableBytes);
                buffer.WriteBytes(undecodedChunk);
                undecodedChunk.Release();
                undecodedChunk = buffer;
            }
        }
        return this;
    }

    IInterfaceHttpPostRequestDecoder IInterfaceHttpPostRequestDecoder.Offer(IHttpContent content) => Offer(content);

    /**
     * True if at current getStatus, there is an available decoded
     * InterfaceHttpData from the Body.
     *
     * This getMethod works for chunked and not chunked request.
     *
     * @return True if at current getStatus, there is a decoded InterfaceHttpData
     * @throws EndOfDataDecoderException
     *             No more data will be available
     */
    public virtual bool HasNext()
    {
        CheckDestroyed();

        if (currentStatus == MultiPartStatus.EPILOGUE)
        {
            // OK except if end of list
            if (bodyListHttpDataRank >= bodyListHttpData.Count)
            {
                throw new EndOfDataDecoderException();
            }
        }
        return bodyListHttpData.Count != 0 && bodyListHttpDataRank < bodyListHttpData.Count;
    }

    /**
     * Returns the next available InterfaceHttpData or null if, at the time it
     * is called, there is no more available InterfaceHttpData. A subsequent
     * call to offer(httpChunk) could enable more data.
     *
     * Be sure to call {@link InterfaceHttpData#release()} after you are done
     * with processing to make sure to not leak any resources
     *
     * @return the next available InterfaceHttpData or null if none
     * @throws EndOfDataDecoderException
     *             No more data will be available
     */
    public virtual IInterfaceHttpData Next()
    {
        CheckDestroyed();

        if (HasNext())
        {
            return bodyListHttpData[bodyListHttpDataRank++];
        }
        return null;
    }

    public virtual IInterfaceHttpData CurrentPartialHttpData()
    {
        if (currentFileUpload != null)
        {
            return currentFileUpload;
        }
        else
        {
            return currentAttribute;
        }
    }

    /**
     * This getMethod will parse as much as possible data and fill the list and map
     *
     * @throws ErrorDataDecoderException
     *             if there is a problem with the charset decoding or other
     *             errors
     */
    private void ParseBody()
    {
        if (currentStatus == MultiPartStatus.PREEPILOGUE || currentStatus == MultiPartStatus.EPILOGUE)
        {
            if (isLastChunk)
            {
                currentStatus = MultiPartStatus.EPILOGUE;
            }
            return;
        }
        ParseBodyMultipart();
    }

    /**
     * Utility function to add a new decoded data
     */
    protected virtual void AddHttpData(IInterfaceHttpData data)
    {
        if (data == null)
        {
            return;
        }
        if (maxFields > 0 && bodyListHttpData.Count >= maxFields)
        {
            throw new HttpPostRequestDecoder.TooManyFormFieldsException();
        }
        if (!bodyMapHttpData.TryGetValue(data.GetName(), out List<IInterfaceHttpData> datas))
        {
            datas = new List<IInterfaceHttpData>(1);
            bodyMapHttpData[data.GetName()] = datas;
        }
        datas.Add(data);
        bodyListHttpData.Add(data);
    }

    /**
     * Parse the Body for multipart
     *
     * @throws ErrorDataDecoderException
     *             if there is a problem with the charset decoding or other
     *             errors
     */
    private void ParseBodyMultipart()
    {
        if (undecodedChunk == null || undecodedChunk.ReadableBytes == 0)
        {
            // nothing to decode
            return;
        }
        IInterfaceHttpData data = DecodeMultipart(currentStatus);
        while (data != null)
        {
            AddHttpData(data);
            if (currentStatus == MultiPartStatus.PREEPILOGUE || currentStatus == MultiPartStatus.EPILOGUE)
            {
                break;
            }
            data = DecodeMultipart(currentStatus);
        }
    }

    /**
     * Decode a multipart request by pieces<br>
     * <br>
     * NOTSTARTED PREAMBLE (<br>
     * (HEADERDELIMITER DISPOSITION (FIELD | FILEUPLOAD))*<br>
     * (HEADERDELIMITER DISPOSITION MIXEDPREAMBLE<br>
     * (MIXEDDELIMITER MIXEDDISPOSITION MIXEDFILEUPLOAD)+<br>
     * MIXEDCLOSEDELIMITER)*<br>
     * CLOSEDELIMITER)+ EPILOGUE<br>
     *
     * Inspired from HttpMessageDecoder
     *
     * @return the next decoded InterfaceHttpData or null if none until now.
     * @throws ErrorDataDecoderException
     *             if an error occurs
     */
    private IInterfaceHttpData DecodeMultipart(MultiPartStatus state)
    {
        switch (state)
        {
        case MultiPartStatus.NOTSTARTED:
            throw new ErrorDataDecoderException("Should not be called with the current getStatus");
        case MultiPartStatus.PREAMBLE:
            // Content-type: multipart/form-data, boundary=AaB03x
            throw new ErrorDataDecoderException("Should not be called with the current getStatus");
        case MultiPartStatus.HEADERDELIMITER:
        {
            // --AaB03x or --AaB03x--
            return FindMultipartDelimiter(multipartDataBoundary, MultiPartStatus.DISPOSITION,
                    MultiPartStatus.PREEPILOGUE);
        }
        case MultiPartStatus.DISPOSITION:
        {
            // content-disposition: form-data; name="field1"
            // content-disposition: form-data; name="pics"; filename="file1.txt"
            // and other immediate values like
            // Content-type: image/gif
            // Content-Type: text/plain
            // Content-Type: text/plain; charset=ISO-8859-1
            // Content-Transfer-Encoding: binary
            // The following line implies a change of mode (mixed mode)
            // Content-type: multipart/mixed, boundary=BbC04y
            return FindMultipartDisposition();
        }
        case MultiPartStatus.FIELD:
        {
            // Now get value according to Content-Type and Charset
            Encoding localCharset = null;
            IAttribute charsetAttribute = Get(currentFieldAttributes, HttpHeaderValues.CHARSET.ToString());
            if (charsetAttribute != null)
            {
                try
                {
                    localCharset = CharsetUtil.ForName(charsetAttribute.GetValue());
                }
                catch (Exception e) when (e is IOException || IsIllegalArgument(e))
                {
                    throw new ErrorDataDecoderException(e);
                }
            }
            IAttribute nameAttribute = Get(currentFieldAttributes, HttpHeaderValues.NAME.ToString());
            if (currentAttribute == null)
            {
                IAttribute lengthAttribute = Get(currentFieldAttributes,
                        HttpHeaderNames.CONTENT_LENGTH.ToString());
                long size;
                try
                {
                    size = lengthAttribute != null? HttpUtil.ParseLong(lengthAttribute
                            .GetValue()) : 0L;
                }
                catch (IOException e)
                {
                    throw new ErrorDataDecoderException(e);
                }
                catch (FormatException)
                {
                    size = 0;
                }
                try
                {
                    if (size > 0)
                    {
                        currentAttribute = factory.CreateAttribute(request,
                                CleanString(nameAttribute.GetValue()), size);
                    }
                    else
                    {
                        currentAttribute = factory.CreateAttribute(request,
                                CleanString(nameAttribute.GetValue()));
                    }
                }
                catch (Exception e) when (IsNullPointer(e) || IsIllegalArgument(e) || e is IOException)
                {
                    throw new ErrorDataDecoderException(e);
                }
                if (localCharset != null)
                {
                    currentAttribute.SetCharset(localCharset);
                }
            }
            // load data
            if (!LoadDataMultipartOptimized(undecodedChunk, multipartDataBoundary, currentAttribute))
            {
                // Delimiter is not found. Need more chunks.
                return null;
            }
            IAttribute finalAttribute = currentAttribute;
            currentAttribute = null;
            currentFieldAttributes = null;
            // ready to load the next one
            currentStatus = MultiPartStatus.HEADERDELIMITER;
            return finalAttribute;
        }
        case MultiPartStatus.FILEUPLOAD:
        {
            // eventually restart from existing FileUpload
            return GetFileUpload(multipartDataBoundary);
        }
        case MultiPartStatus.MIXEDDELIMITER:
        {
            // --AaB03x or --AaB03x--
            // Note that currentFieldAttributes exists
            return FindMultipartDelimiter(multipartMixedBoundary, MultiPartStatus.MIXEDDISPOSITION,
                    MultiPartStatus.HEADERDELIMITER);
        }
        case MultiPartStatus.MIXEDDISPOSITION:
        {
            return FindMultipartDisposition();
        }
        case MultiPartStatus.MIXEDFILEUPLOAD:
        {
            // eventually restart from existing FileUpload
            return GetFileUpload(multipartMixedBoundary);
        }
        case MultiPartStatus.PREEPILOGUE:
            return null;
        case MultiPartStatus.EPILOGUE:
            return null;
        default:
            throw new ErrorDataDecoderException("Shouldn't reach here.");
        }
    }

    /**
     * Skip control Characters
     *
     * @throws NotEnoughDataDecoderException
     */
    private static void SkipControlCharacters(ByteBuf undecodedChunk)
    {
        if (!HasArray(undecodedChunk))
        {
            try
            {
                SkipControlCharactersStandard(undecodedChunk);
            }
            catch (ArgumentOutOfRangeException e1)
            {
                throw new NotEnoughDataDecoderException(e1);
            }
            return;
        }
        SeekAheadOptimize sao = new SeekAheadOptimize(undecodedChunk);
        while (sao.pos < sao.limit)
        {
            char c = (char) (sao.bytes[sao.pos++] & 0xFF);
            if (!char.IsControl(c) && !HttpVersion.IsJavaWhitespace(c))
            {
                sao.SetReadPosition(1);
                return;
            }
        }
        throw new NotEnoughDataDecoderException("Access out of bounds");
    }

    private static void SkipControlCharactersStandard(ByteBuf undecodedChunk)
    {
        for (;;)
        {
            char c = (char) undecodedChunk.ReadByte();
            if (!char.IsControl(c) && !HttpVersion.IsJavaWhitespace(c))
            {
                undecodedChunk.ReaderIndex = undecodedChunk.ReaderIndex - 1;
                break;
            }
        }
    }

    /**
     * Find the next Multipart Delimiter
     *
     * @param delimiter
     *            delimiter to find
     * @param dispositionStatus
     *            the next getStatus if the delimiter is a start
     * @param closeDelimiterStatus
     *            the next getStatus if the delimiter is a close delimiter
     * @return the next InterfaceHttpData if any
     * @throws ErrorDataDecoderException
     */
    private IInterfaceHttpData FindMultipartDelimiter(string delimiter, MultiPartStatus dispositionStatus,
            MultiPartStatus closeDelimiterStatus)
    {
        // --AaB03x or --AaB03x--
        int readerIndex = undecodedChunk.ReaderIndex;
        try
        {
            SkipControlCharacters(undecodedChunk);
        }
        catch (NotEnoughDataDecoderException)
        {
            undecodedChunk.ReaderIndex = readerIndex;
            return null;
        }
        SkipOneLine();
        string newline;
        try
        {
            newline = ReadDelimiterOptimized(undecodedChunk, delimiter, charset);
        }
        catch (NotEnoughDataDecoderException)
        {
            undecodedChunk.ReaderIndex = readerIndex;
            return null;
        }
        if (newline.Equals(delimiter))
        {
            currentStatus = dispositionStatus;
            return DecodeMultipart(dispositionStatus);
        }
        if (newline.Equals(delimiter + "--"))
        {
            // CLOSEDELIMITER or MIXED CLOSEDELIMITER found
            currentStatus = closeDelimiterStatus;
            if (currentStatus == MultiPartStatus.HEADERDELIMITER)
            {
                // MIXEDCLOSEDELIMITER
                // end of the Mixed part
                currentFieldAttributes = null;
                return DecodeMultipart(MultiPartStatus.HEADERDELIMITER);
            }
            return null;
        }
        undecodedChunk.ReaderIndex = readerIndex;
        throw new ErrorDataDecoderException("No Multipart delimiter found");
    }

    /**
     * Find the next Disposition
     *
     * @return the next InterfaceHttpData if any
     * @throws ErrorDataDecoderException
     */
    private IInterfaceHttpData FindMultipartDisposition()
    {
        int readerIndex = undecodedChunk.ReaderIndex;
        if (currentStatus == MultiPartStatus.DISPOSITION)
        {
            currentFieldAttributes = new SortedDictionary<string, IAttribute>(CaseIgnoringComparator.INSTANCE);
        }
        // read many lines until empty line with newline found! Store all data
        while (!SkipOneLine())
        {
            string newline;
            try
            {
                SkipControlCharacters(undecodedChunk);
                newline = ReadLineOptimized(undecodedChunk, charset);
            }
            catch (NotEnoughDataDecoderException)
            {
                undecodedChunk.ReaderIndex = readerIndex;
                return null;
            }
            string[] contents = SplitMultipartHeader(newline);
            if (HttpHeaderNames.CONTENT_DISPOSITION.ContentEqualsIgnoreCase(new StringCharSequence(contents[0])))
            {
                bool checkSecondArg;
                if (currentStatus == MultiPartStatus.DISPOSITION)
                {
                    checkSecondArg = HttpHeaderValues.FORM_DATA.ContentEqualsIgnoreCase(new StringCharSequence(contents[1]));
                }
                else
                {
                    checkSecondArg = HttpHeaderValues.ATTACHMENT.ContentEqualsIgnoreCase(new StringCharSequence(contents[1]))
                            || HttpHeaderValues.FILE.ContentEqualsIgnoreCase(new StringCharSequence(contents[1]));
                }
                if (checkSecondArg)
                {
                    // read next values and store them in the map as Attribute
                    for (int i = 2; i < contents.Length; i++)
                    {
                        string[] values = contents[i].Split('=', 2);
                        IAttribute attribute;
                        try
                        {
                            attribute = GetContentDispositionAttribute(values);
                        }
                        catch (Exception e) when (IsNullPointer(e) || IsIllegalArgument(e))
                        {
                            throw new ErrorDataDecoderException(e);
                        }
                        currentFieldAttributes[attribute.GetName()] = attribute;
                    }
                }
            }
            else if (HttpHeaderNames.CONTENT_TRANSFER_ENCODING.ContentEqualsIgnoreCase(new StringCharSequence(contents[0])))
            {
                IAttribute attribute;
                try
                {
                    attribute = factory.CreateAttribute(request, HttpHeaderNames.CONTENT_TRANSFER_ENCODING.ToString(),
                            CleanString(contents[1]));
                }
                catch (Exception e) when (IsNullPointer(e) || IsIllegalArgument(e))
                {
                    throw new ErrorDataDecoderException(e);
                }

                currentFieldAttributes[HttpHeaderNames.CONTENT_TRANSFER_ENCODING.ToString()] = attribute;
            }
            else if (HttpHeaderNames.CONTENT_LENGTH.ContentEqualsIgnoreCase(new StringCharSequence(contents[0])))
            {
                IAttribute attribute;
                try
                {
                    attribute = factory.CreateAttribute(request, HttpHeaderNames.CONTENT_LENGTH.ToString(),
                            CleanString(contents[1]));
                }
                catch (Exception e) when (IsNullPointer(e) || IsIllegalArgument(e))
                {
                    throw new ErrorDataDecoderException(e);
                }

                currentFieldAttributes[HttpHeaderNames.CONTENT_LENGTH.ToString()] = attribute;
            }
            else if (HttpHeaderNames.CONTENT_TYPE.ContentEqualsIgnoreCase(new StringCharSequence(contents[0])))
            {
                // Take care of possible "multipart/mixed"
                if (HttpHeaderValues.MULTIPART_MIXED.ContentEqualsIgnoreCase(new StringCharSequence(contents[1])))
                {
                    if (currentStatus == MultiPartStatus.DISPOSITION)
                    {
                        string values = StringUtil.SubstringAfter(contents[2], '=');
                        multipartMixedBoundary = "--" + (values ?? "null");
                        currentStatus = MultiPartStatus.MIXEDDELIMITER;
                        return DecodeMultipart(MultiPartStatus.MIXEDDELIMITER);
                    }
                    else
                    {
                        throw new ErrorDataDecoderException("Mixed Multipart found in a previous Mixed Multipart");
                    }
                }
                else
                {
                    for (int i = 1; i < contents.Length; i++)
                    {
                        string charsetHeader = HttpHeaderValues.CHARSET.ToString();
                        if (string.Compare(contents[i], 0, charsetHeader, 0, charsetHeader.Length,
                                StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            string values = StringUtil.SubstringAfter(contents[i], '=');
                            IAttribute attribute;
                            try
                            {
                                attribute = factory.CreateAttribute(request, charsetHeader, CleanString(values));
                            }
                            catch (Exception e) when (IsNullPointer(e) || IsIllegalArgument(e))
                            {
                                throw new ErrorDataDecoderException(e);
                            }
                            currentFieldAttributes[HttpHeaderValues.CHARSET.ToString()] = attribute;
                        }
                        else if (contents[i].Contains('='))
                        {
                            string name = StringUtil.SubstringBefore(contents[i], '=');
                            string values = StringUtil.SubstringAfter(contents[i], '=');
                            IAttribute attribute;
                            try
                            {
                                attribute = factory.CreateAttribute(request, CleanString(name), values);
                            }
                            catch (Exception e) when (IsNullPointer(e) || IsIllegalArgument(e))
                            {
                                throw new ErrorDataDecoderException(e);
                            }
                            currentFieldAttributes[name] = attribute;
                        }
                        else
                        {
                            IAttribute attribute;
                            try
                            {
                                attribute = factory.CreateAttribute(request,
                                        CleanString(contents[0]), contents[i]);
                            }
                            catch (Exception e) when (IsNullPointer(e) || IsIllegalArgument(e))
                            {
                                throw new ErrorDataDecoderException(e);
                            }
                            currentFieldAttributes[attribute.GetName()] = attribute;
                        }
                    }
                }
            }
        }
        // Is it a FileUpload
        IAttribute filenameAttribute = Get(currentFieldAttributes, HttpHeaderValues.FILENAME.ToString());
        if (currentStatus == MultiPartStatus.DISPOSITION)
        {
            if (filenameAttribute != null)
            {
                // FileUpload
                currentStatus = MultiPartStatus.FILEUPLOAD;
                // do not change the buffer position
                return DecodeMultipart(MultiPartStatus.FILEUPLOAD);
            }
            else
            {
                // Field
                currentStatus = MultiPartStatus.FIELD;
                // do not change the buffer position
                return DecodeMultipart(MultiPartStatus.FIELD);
            }
        }
        else
        {
            if (filenameAttribute != null)
            {
                // FileUpload
                currentStatus = MultiPartStatus.MIXEDFILEUPLOAD;
                // do not change the buffer position
                return DecodeMultipart(MultiPartStatus.MIXEDFILEUPLOAD);
            }
            else
            {
                // Field is not supported in MIXED mode
                throw new ErrorDataDecoderException("Filename not found");
            }
        }
    }

    private static readonly string FILENAME_ENCODED = HttpHeaderValues.FILENAME.ToString() + '*';

    private IAttribute GetContentDispositionAttribute(params string[] values)
    {
        string name = CleanString(values[0]);
        string value = values[1];

        // Filename can be token, quoted or encoded. See https://tools.ietf.org/html/rfc5987
        if (HttpHeaderValues.FILENAME.ContentEquals(new StringCharSequence(name)))
        {
            // Value is quoted or token. Strip if quoted:
            int last = value.Length - 1;
            if (last > 0 &&
              value[0] == HttpConstants.DOUBLE_QUOTE &&
              value[last] == HttpConstants.DOUBLE_QUOTE)
            {
                value = value.Substring(1, last - 1);
            }
        }
        else if (FILENAME_ENCODED.Equals(name))
        {
            try
            {
                name = HttpHeaderValues.FILENAME.ToString();
                string[] split = CleanString(value).Split('\'', 3);
                value = QueryStringDecoder.DecodeComponent(split[2], CharsetUtil.ForName(split[0]));
            }
            catch (Exception e) when (e is IndexOutOfRangeException || IsIllegalArgument(e))
            {
                 throw new ErrorDataDecoderException(e);
            }
        }
        else
        {
            // otherwise we need to clean the value
            value = CleanString(value);
        }
        return factory.CreateAttribute(request, name, value);
    }

    /**
     * Get the FileUpload (new one or current one)
     *
     * @param delimiter
     *            the delimiter to use
     * @return the InterfaceHttpData if any
     * @throws ErrorDataDecoderException
     */
    protected virtual IInterfaceHttpData GetFileUpload(string delimiter)
    {
        // eventually restart from existing FileUpload
        // Now get value according to Content-Type and Charset
        IAttribute encoding = Get(currentFieldAttributes, HttpHeaderNames.CONTENT_TRANSFER_ENCODING.ToString());
        Encoding localCharset = charset;
        // Default
        TransferEncodingMechanism mechanism = TransferEncodingMechanism.BIT7;
        if (encoding != null)
        {
            string code;
            try
            {
                // RFC 2045 Content-Transfer-Encoding values are case-insensitive ASCII tokens.
                // toLowerCase() without a Locale would corrupt them under Turkish (tr_TR) locale,
                // where 'I' lowercases to 'ı' (U+0131) and "BINARY" becomes "bınary" - never
                // matching the lowercase ASCII constants compared against below.
                code = encoding.GetValue().ToLowerInvariant();
            }
            catch (IOException e)
            {
                throw new ErrorDataDecoderException(e);
            }
            if (code.Equals(HttpPostBodyUtil.TransferEncodingMechanism.BIT7.Value()))
            {
                localCharset = Encoding.ASCII;
            }
            else if (code.Equals(HttpPostBodyUtil.TransferEncodingMechanism.BIT8.Value()))
            {
                localCharset = Encoding.Latin1;
                mechanism = TransferEncodingMechanism.BIT8;
            }
            else if (code.Equals(HttpPostBodyUtil.TransferEncodingMechanism.BINARY.Value()))
            {
                // no real charset, so let the default
                mechanism = TransferEncodingMechanism.BINARY;
            }
            else
            {
                throw new ErrorDataDecoderException("TransferEncoding Unknown: " + code);
            }
        }
        IAttribute charsetAttribute = Get(currentFieldAttributes, HttpHeaderValues.CHARSET.ToString());
        if (charsetAttribute != null)
        {
            try
            {
                localCharset = CharsetUtil.ForName(charsetAttribute.GetValue());
            }
            catch (Exception e) when (e is IOException || IsIllegalArgument(e))
            {
                throw new ErrorDataDecoderException(e);
            }
        }
        if (currentFileUpload == null)
        {
            IAttribute filenameAttribute = Get(currentFieldAttributes, HttpHeaderValues.FILENAME.ToString());
            IAttribute nameAttribute = Get(currentFieldAttributes, HttpHeaderValues.NAME.ToString());
            IAttribute contentTypeAttribute = Get(currentFieldAttributes, HttpHeaderNames.CONTENT_TYPE.ToString());
            IAttribute lengthAttribute = Get(currentFieldAttributes, HttpHeaderNames.CONTENT_LENGTH.ToString());
            long size;
            try
            {
                size = lengthAttribute != null ? HttpUtil.ParseLong(lengthAttribute.GetValue()) : 0L;
            }
            catch (IOException e)
            {
                throw new ErrorDataDecoderException(e);
            }
            catch (FormatException)
            {
                size = 0;
            }
            try
            {
                string contentType;
                if (contentTypeAttribute != null)
                {
                    contentType = contentTypeAttribute.GetValue();
                }
                else
                {
                    contentType = HttpPostBodyUtil.DEFAULT_BINARY_CONTENT_TYPE;
                }
                currentFileUpload = factory.CreateFileUpload(request,
                        CleanString(nameAttribute.GetValue()), CleanString(filenameAttribute.GetValue()),
                        CleanMimeContentType(contentType), mechanism.Value(), localCharset,
                        size);
            }
            catch (Exception e) when (IsNullPointer(e) || IsIllegalArgument(e) || e is IOException)
            {
                throw new ErrorDataDecoderException(e);
            }
        }
        // load data as much as possible
        if (!LoadDataMultipartOptimized(undecodedChunk, delimiter, currentFileUpload))
        {
            // Delimiter is not found. Need more chunks.
            return null;
        }
        if (currentFileUpload.IsCompleted())
        {
            // ready to load the next one
            if (currentStatus == MultiPartStatus.FILEUPLOAD)
            {
                currentStatus = MultiPartStatus.HEADERDELIMITER;
                currentFieldAttributes = null;
            }
            else
            {
                currentStatus = MultiPartStatus.MIXEDDELIMITER;
                CleanMixedAttributes();
            }
            IFileUpload fileUpload = currentFileUpload;
            currentFileUpload = null;
            return fileUpload;
        }
        // do not change the buffer position
        // since some can be already saved into FileUpload
        // So do not change the currentStatus
        return null;
    }

    /**
     * Destroy the {@link HttpPostMultipartRequestDecoder} and release all it resources. After this method
     * was called it is not possible to operate on it anymore.
     */
    public virtual void Destroy()
    {
        // Release all data items, including those not yet pulled, only file based items
        CleanFiles();
        // Clean Memory based data
        foreach (IInterfaceHttpData httpData in bodyListHttpData)
        {
            // Might have been already released by the user
            if (httpData.RefCnt > 0)
            {
                httpData.Release();
            }
        }

        destroyed = true;

        if (undecodedChunk != null && undecodedChunk.RefCnt > 0)
        {
            undecodedChunk.Release();
            undecodedChunk = null;
        }
    }

    /**
     * Clean all HttpDatas (on Disk) for the current request.
     */
    public virtual void CleanFiles()
    {
        CheckDestroyed();

        factory.CleanRequestHttpData(request);
    }

    /**
     * Remove the given FileUpload from the list of FileUploads to clean
     */
    public virtual void RemoveHttpDataFromClean(IInterfaceHttpData data)
    {
        CheckDestroyed();

        factory.RemoveHttpDataFromClean(request, data);
    }

    /**
     * Remove all Attributes that should be cleaned between two FileUpload in
     * Mixed mode
     */
    private void CleanMixedAttributes()
    {
        currentFieldAttributes.Remove(HttpHeaderValues.CHARSET.ToString());
        currentFieldAttributes.Remove(HttpHeaderNames.CONTENT_LENGTH.ToString());
        currentFieldAttributes.Remove(HttpHeaderNames.CONTENT_TRANSFER_ENCODING.ToString());
        currentFieldAttributes.Remove(HttpHeaderNames.CONTENT_TYPE.ToString());
        currentFieldAttributes.Remove(HttpHeaderValues.FILENAME.ToString());
    }

    /**
     * Read one line up to the CRLF or LF
     *
     * @return the String from one line
     * @throws NotEnoughDataDecoderException
     *             Need more chunks and reset the {@code readerIndex} to the previous
     *             value
     */
    private static string ReadLineOptimized(ByteBuf undecodedChunk, Encoding charset)
    {
        int readerIndex = undecodedChunk.ReaderIndex;
        ByteBuf line = null;
        try
        {
            if (undecodedChunk.IsReadable())
            {
                int posLfOrCrLf = FindLineBreak(undecodedChunk, undecodedChunk.ReaderIndex);
                if (posLfOrCrLf <= 0)
                {
                    throw new NotEnoughDataDecoderException();
                }
                try
                {
                    line = undecodedChunk.Alloc().HeapBuffer(posLfOrCrLf);
                    line.WriteBytes(undecodedChunk, posLfOrCrLf);

                    byte nextByte = undecodedChunk.ReadByte();
                    if (nextByte == HttpConstants.CR)
                    {
                        // force read next byte since LF is the following one
                        undecodedChunk.ReadByte();
                    }
                    return line.ToString(charset);
                }
                finally
                {
                    line.Release();
                }
            }
        }
        catch (ArgumentOutOfRangeException e)
        {
            undecodedChunk.ReaderIndex = readerIndex;
            throw new NotEnoughDataDecoderException(e);
        }
        undecodedChunk.ReaderIndex = readerIndex;
        throw new NotEnoughDataDecoderException();
    }

    /**
     * Read one line up to --delimiter or --delimiter-- and if existing the CRLF
     * or LF Read one line up to --delimiter or --delimiter-- and if existing
     * the CRLF or LF. Note that CRLF or LF are mandatory for opening delimiter
     * (--delimiter) but not for closing delimiter (--delimiter--) since some
     * clients does not include CRLF in this case.
     *
     * @param delimiter
     *            of the form --string, such that '--' is already included
     * @return the String from one line as the delimiter searched (opening or
     *         closing)
     * @throws NotEnoughDataDecoderException
     *             Need more chunks and reset the {@code readerIndex} to the previous
     *             value
     */
    private static string ReadDelimiterOptimized(ByteBuf undecodedChunk, string delimiter, Encoding charset)
    {
        int readerIndex = undecodedChunk.ReaderIndex;
        byte[] bdelimiter = charset.GetBytes(delimiter);
        int delimiterLength = bdelimiter.Length;
        try
        {
            int delimiterPos = FindDelimiter(undecodedChunk, readerIndex, bdelimiter, false);
            if (delimiterPos < 0)
            {
                // delimiter not found so break here !
                undecodedChunk.ReaderIndex = readerIndex;
                throw new NotEnoughDataDecoderException();
            }
            StringBuilder sb = new StringBuilder(delimiter);
            undecodedChunk.ReaderIndex = readerIndex + delimiterPos + delimiterLength;
            // Now check if either opening delimiter or closing delimiter
            if (undecodedChunk.IsReadable())
            {
                byte nextByte = undecodedChunk.ReadByte();
                // first check for opening delimiter
                if (nextByte == HttpConstants.CR)
                {
                    nextByte = undecodedChunk.ReadByte();
                    if (nextByte == HttpConstants.LF)
                    {
                        return sb.ToString();
                    }
                    else
                    {
                        // error since CR must be followed by LF
                        // delimiter not found so break here !
                        undecodedChunk.ReaderIndex = readerIndex;
                        throw new NotEnoughDataDecoderException();
                    }
                }
                else if (nextByte == HttpConstants.LF)
                {
                    return sb.ToString();
                }
                else if (nextByte == '-')
                {
                    sb.Append('-');
                    // second check for closing delimiter
                    nextByte = undecodedChunk.ReadByte();
                    if (nextByte == '-')
                    {
                        sb.Append('-');
                        // now try to find if CRLF or LF there
                        if (undecodedChunk.IsReadable())
                        {
                            nextByte = undecodedChunk.ReadByte();
                            if (nextByte == HttpConstants.CR)
                            {
                                nextByte = undecodedChunk.ReadByte();
                                if (nextByte == HttpConstants.LF)
                                {
                                    return sb.ToString();
                                }
                                else
                                {
                                    // error CR without LF
                                    // delimiter not found so break here !
                                    undecodedChunk.ReaderIndex = readerIndex;
                                    throw new NotEnoughDataDecoderException();
                                }
                            }
                            else if (nextByte == HttpConstants.LF)
                            {
                                return sb.ToString();
                            }
                            else
                            {
                                // No CRLF but ok however (Adobe Flash uploader)
                                // minus 1 since we read one char ahead but
                                // should not
                                undecodedChunk.ReaderIndex = undecodedChunk.ReaderIndex - 1;
                                return sb.ToString();
                            }
                        }
                        // FIXME what do we do here?
                        // either considering it is fine, either waiting for
                        // more data to come?
                        // lets try considering it is fine...
                        return sb.ToString();
                    }
                    // only one '-' => not enough
                    // whatever now => error since incomplete
                }
            }
        }
        catch (ArgumentOutOfRangeException e)
        {
            undecodedChunk.ReaderIndex = readerIndex;
            throw new NotEnoughDataDecoderException(e);
        }
        undecodedChunk.ReaderIndex = readerIndex;
        throw new NotEnoughDataDecoderException();
    }

    /**
     * Rewrite buffer in order to skip lengthToSkip bytes from current readerIndex,
     * such that any readable bytes available after readerIndex + lengthToSkip (so before writerIndex)
     * are moved at readerIndex position,
     * therefore decreasing writerIndex of lengthToSkip at the end of the process.
     *
     * @param buffer the buffer to rewrite from current readerIndex
     * @param lengthToSkip the size to skip from readerIndex
     */
    private static void RewriteCurrentBuffer(ByteBuf buffer, int lengthToSkip)
    {
        if (lengthToSkip == 0)
        {
            return;
        }
        int readerIndex = buffer.ReaderIndex;
        int readableBytes = buffer.ReadableBytes;
        if (readableBytes == lengthToSkip)
        {
            buffer.ReaderIndex = readerIndex;
            buffer.WriterIndex = readerIndex;
            return;
        }
        buffer.SetBytes(readerIndex, buffer, readerIndex + lengthToSkip, readableBytes - lengthToSkip);
        buffer.ReaderIndex = readerIndex;
        buffer.WriterIndex = readerIndex + readableBytes - lengthToSkip;
    }

    /**
     * Load the field value or file data from a Multipart request
     *
     * @return {@code true} if the last chunk is loaded (boundary delimiter found), {@code false} if need more chunks
     * @throws ErrorDataDecoderException
     */
    private static bool LoadDataMultipartOptimized(ByteBuf undecodedChunk, string delimiter, IHttpData httpData)
    {
        if (!undecodedChunk.IsReadable())
        {
            return false;
        }
        int startReaderIndex = undecodedChunk.ReaderIndex;
        byte[] bdelimiter = httpData.GetCharset().GetBytes(delimiter);
        int posDelimiter = FindDelimiter(undecodedChunk, startReaderIndex, bdelimiter, true);
        if (posDelimiter < 0)
        {
            // Not found but however perhaps because incomplete so search LF or CRLF from the end.
            // Possible last bytes contain partially delimiter
            // (delimiter is possibly partially there, at least 1 missing byte),
            // therefore searching last delimiter.length +1 (+1 for CRLF instead of LF)
            int readableBytes = undecodedChunk.ReadableBytes;
            int lastPosition = readableBytes - bdelimiter.Length - 1;
            if (lastPosition < 0)
            {
                // Not enough bytes, but at most delimiter.length bytes available so can still try to find CRLF there
                lastPosition = 0;
            }
            posDelimiter = FindLastLineBreak(undecodedChunk, startReaderIndex + lastPosition);
            // No LineBreak, however CR can be at the end of the buffer, LF not yet there (issue #11668)
            // Check if last CR (if any) shall not be in the content (definedLength vs actual length + buffer - 1)
            if (posDelimiter < 0 &&
                httpData.DefinedLength() == httpData.Length() + readableBytes - 1 &&
                undecodedChunk.GetByte(readableBytes + startReaderIndex - 1) == HttpConstants.CR)
            {
                // Last CR shall precede a future LF
                lastPosition = 0;
                posDelimiter = readableBytes - 1;
            }
            if (posDelimiter < 0)
            {
                // not found so this chunk can be fully added
                ByteBuf content = undecodedChunk.Copy();
                try
                {
                    httpData.AddContent(content, false);
                }
                catch (IOException e)
                {
                    throw new ErrorDataDecoderException(e);
                }
                undecodedChunk.ReaderIndex = startReaderIndex;
                undecodedChunk.WriterIndex = startReaderIndex;
                return false;
            }
            // posDelimiter is not from startReaderIndex but from startReaderIndex + lastPosition
            posDelimiter += lastPosition;
            if (posDelimiter == 0)
            {
                // Nothing to add
                return false;
            }
            // Not fully but still some bytes to provide: httpData is not yet finished since delimiter not found
            ByteBuf content2 = undecodedChunk.Copy(startReaderIndex, posDelimiter);
            try
            {
                httpData.AddContent(content2, false);
            }
            catch (IOException e)
            {
                throw new ErrorDataDecoderException(e);
            }
            RewriteCurrentBuffer(undecodedChunk, posDelimiter);
            return false;
        }
        // Delimiter found at posDelimiter, including LF or CRLF, so httpData has its last chunk
        ByteBuf content3 = undecodedChunk.Copy(startReaderIndex, posDelimiter);
        try
        {
            httpData.AddContent(content3, true);
        }
        catch (IOException e)
        {
            throw new ErrorDataDecoderException(e);
        }
        RewriteCurrentBuffer(undecodedChunk, posDelimiter);
        return true;
    }

    /**
     * Clean the String from any unallowed character
     *
     * @return the cleaned String
     */
    private static string CleanString(string field)
    {
        int size = field.Length;
        StringBuilder sb = new StringBuilder(size);
        for (int i = 0; i < size; i++)
        {
            char nextChar = field[i];
            switch (nextChar)
            {
            case (char) HttpConstants.COLON:
            case (char) HttpConstants.COMMA:
            case (char) HttpConstants.EQUALS:
            case (char) HttpConstants.SEMICOLON:
            case (char) HttpConstants.HT:
                sb.Append(HttpConstants.SP_CHAR);
                break;
            case (char) HttpConstants.DOUBLE_QUOTE:
            case (char) HttpConstants.BACKSLASH:
                // nothing added, just removes double quote and backslash
                break;
            default:
                if (nextChar < HttpConstants.SP || nextChar == HttpConstants.DEL)
                {
                    sb.Append(HttpConstants.SP_CHAR);
                }
                else
                {
                    sb.Append(nextChar);
                }
                break;
            }
        }
        return HttpUtil.TrimJava(sb.ToString());
    }

    /**
     * The Content-Type values in part-headers are defined by RFC 2045, aka. MIME, which effectively means
     * we use a default content type if the given content type is empty or malformed.
     * @param contentType The Content-Type part header value.
     * @return A cleaned up or replaced Content-Type header value that passes validation.
     */
    private static string CleanMimeContentType(string contentType)
    {
        if (!FileUploadUtil.IsContentTypeForMultiPartValid(contentType))
        {
            // RFC 2045 section 5.2 recommends falling back to a default content type,
            // when a "syntactically invalid Content-Type header field is encountered".
            return HttpPostBodyUtil.DEFAULT_BINARY_CONTENT_TYPE;
        }
        return contentType;
    }

    /**
     * Skip one empty line
     *
     * @return True if one empty line was skipped
     */
    private bool SkipOneLine()
    {
        if (!undecodedChunk.IsReadable())
        {
            return false;
        }
        byte nextByte = undecodedChunk.ReadByte();
        if (nextByte == HttpConstants.CR)
        {
            if (!undecodedChunk.IsReadable())
            {
                undecodedChunk.ReaderIndex = undecodedChunk.ReaderIndex - 1;
                return false;
            }
            nextByte = undecodedChunk.ReadByte();
            if (nextByte == HttpConstants.LF)
            {
                return true;
            }
            undecodedChunk.ReaderIndex = undecodedChunk.ReaderIndex - 2;
            return false;
        }
        if (nextByte == HttpConstants.LF)
        {
            return true;
        }
        undecodedChunk.ReaderIndex = undecodedChunk.ReaderIndex - 1;
        return false;
    }

    /**
     * Split one header in Multipart
     *
     * @return an array of String where rank 0 is the name of the header,
     *         follows by several values that were separated by ';' or ','
     */
    private static string[] SplitMultipartHeader(string sb)
    {
        List<string> headers = new List<string>(1);
        int nameStart;
        int nameEnd;
        int colonEnd;
        int valueStart;
        int valueEnd;
        nameStart = FindNonWhitespace(sb, 0);
        for (nameEnd = nameStart; nameEnd < sb.Length; nameEnd++)
        {
            char ch = sb[nameEnd];
            if (ch == ':' || HttpVersion.IsJavaWhitespace(ch))
            {
                break;
            }
        }
        for (colonEnd = nameEnd; colonEnd < sb.Length; colonEnd++)
        {
            if (sb[colonEnd] == ':')
            {
                colonEnd++;
                break;
            }
        }
        valueStart = FindNonWhitespace(sb, colonEnd);
        valueEnd = FindEndOfString(sb);
        headers.Add(sb.Substring(nameStart, nameEnd - nameStart));
        string svalue = (valueStart >= valueEnd) ? StringUtil.EMPTY_STRING : sb.Substring(valueStart, valueEnd - valueStart);
        string[] values;
        if (svalue.IndexOf(';') >= 0)
        {
            values = SplitMultipartHeaderValues(svalue);
        }
        else
        {
            values = SplitDiscardingTrailingEmpty(svalue, ',');
        }
        foreach (string value in values)
        {
            headers.Add(HttpUtil.TrimJava(value));
        }
        string[] array = new string[headers.Count];
        for (int i = 0; i < headers.Count; i++)
        {
            array[i] = headers[i];
        }
        return array;
    }

    /**
     * Split one header value in Multipart
     * @return an array of String where values that were separated by ';' or ','
     */
    private static string[] SplitMultipartHeaderValues(string svalue)
    {
        List<string> values = new List<string>(1);
        bool inQuote = false;
        bool escapeNext = false;
        int start = 0;
        for (int i = 0; i < svalue.Length; i++)
        {
            char c = svalue[i];
            if (inQuote)
            {
                if (escapeNext)
                {
                    escapeNext = false;
                }
                else
                {
                    if (c == '\\')
                    {
                        escapeNext = true;
                    }
                    else if (c == '"')
                    {
                        inQuote = false;
                    }
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuote = true;
                }
                else if (c == ';')
                {
                    values.Add(svalue.Substring(start, i - start));
                    start = i + 1;
                }
            }
        }
        values.Add(svalue.Substring(start));
        return values.ToArray();
    }

    /**
     * This method is package private intentionally in order to allow during tests
     * to access to the amount of memory allocated (capacity) within the private
     * ByteBuf undecodedChunk
     *
     * @return the number of bytes the internal buffer can contain
     */
    internal int GetCurrentAllocatedCapacity()
    {
        return undecodedChunk.Capacity;
    }

    // CLR: Map.get(key) of the field attributes, null when the key is absent.
    private static IAttribute Get(SortedDictionary<string, IAttribute> map, string key)
    {
        return map.TryGetValue(key, out IAttribute value) ? value : null;
    }

    // CLR: String.split(regex) with the default limit 0: "trailing empty strings will be discarded", while an input that
    // the expression does not match gives an array of just that string (see the class comment).
    private static string[] SplitDiscardingTrailingEmpty(string s, char separator)
    {
        string[] parts = s.Split(separator);
        if (parts.Length == 1)
        {
            return parts;
        }
        int length = parts.Length;
        while (length > 0 && parts[length - 1].Length == 0)
        {
            length--;
        }
        if (length == parts.Length)
        {
            return parts;
        }
        string[] result = new string[length];
        Array.Copy(parts, result, length);
        return result;
    }
}
