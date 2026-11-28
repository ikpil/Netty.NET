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
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.Multipart.HttpPostRequestDecoder;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * This decoder will decode Body and can handle POST BODY.
 *
 * You <strong>MUST</strong> call {@link #destroy()} after completion to release all resources.
 *
 */
// CLR: HttpRequest is IHttpRequest, HttpContent IHttpContent, LastHttpContent ILastHttpContent and Charset Encoding.
// TreeMap<String, List<InterfaceHttpData>>(CaseIgnoringComparator.INSTANCE) is a SortedDictionary with the comparator's
// IComparer<string> ("SortedDictionary<TKey,TValue> requires a comparer implementation to perform key comparisons",
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.sorteddictionary-2?view=net-10.0); map.get()
// returning null is TryGetValue. offer(HttpContent) returns this class, which a C# interface implementation cannot
// narrow, so IInterfaceHttpPostRequestDecoder.Offer is implemented explicitly and calls it. The constructor's
// catch (Throwable) with PlatformDependent.throwException(e) is catch (Exception) with a rethrow;
// getClass().getSimpleName() is nameof, and IllegalStateException is InvalidOperationException. readUnsignedByte() is
// the unsigned ReadByte() of the Buffer port. catch (IOException | IllegalArgumentException) keeps the Java types with
// HttpPostBodyUtil.IsIllegalArgument. The ByteProcessors UrlEncodedDetector and UrlDecoder are Func<byte, bool> in the
// Common port, passed as their Process methods (as core's HttpChunkLineValidatingByteProcessor); their byte comparisons
// and the (char) hiByte/(char) value conversions give the Java results for every octet with unsigned bytes ('%', '+'
// and the hexadecimal digits are ASCII, and StringUtil.DecodeHexNibble returns -1 both for Java's sign-extended chars
// and for the C# chars 0x80-0xFF). String.format("... '%d' ... '%s'") is string.Format with the invariant culture.
public class HttpPostStandardRequestDecoder : IInterfaceHttpPostRequestDecoder
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
     * Default charset to use
     */
    private readonly Encoding charset;

    /**
     * The maximum number of fields allows by the form
     */
    private readonly int maxFields;

    /**
     * The maximum number of accumulated bytes when decoding a field
     */
    private readonly int maxBufferedBytes;

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
     * Current getStatus
     */
    private MultiPartStatus currentStatus = MultiPartStatus.NOTSTARTED;

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
    public HttpPostStandardRequestDecoder(IHttpRequest request)
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
    public HttpPostStandardRequestDecoder(IHttpDataFactory factory, IHttpRequest request)
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
    public HttpPostStandardRequestDecoder(IHttpDataFactory factory, IHttpRequest request, Encoding charset)
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
    public HttpPostStandardRequestDecoder(IHttpDataFactory factory, IHttpRequest request, Encoding charset,
                                          int maxFields, int maxBufferedBytes)
    {
        this.request = CheckNotNull(request, "request");
        this.charset = CheckNotNull(charset, "charset");
        this.factory = CheckNotNull(factory, "factory");
        this.maxFields = maxFields;
        this.maxBufferedBytes = maxBufferedBytes;

        try
        {
            if (request is IHttpContent)
            {
                // Offer automatically if the given request is as type of HttpContent
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
            throw new InvalidOperationException(nameof(HttpPostStandardRequestDecoder)
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

        return false;
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
    public virtual HttpPostStandardRequestDecoder Offer(IHttpContent content)
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
            throw new TooLongFormFieldException();
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

    IInterfaceHttpPostRequestDecoder IInterfaceHttpPostRequestDecoder.Offer(IHttpContent content)
    {
        return Offer(content);
    }

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
        return currentAttribute;
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

        ParseBodyAttributes();
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
            throw new TooManyFormFieldsException();
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
     * This getMethod fill the map and list with as much Attribute as possible from
     * Body in not Multipart mode.
     *
     * @throws ErrorDataDecoderException
     *             if there is a problem with the charset decoding or other
     *             errors
     */
    private void ParseBodyAttributesStandard()
    {
        int firstpos = undecodedChunk.ReaderIndex;
        int currentpos = firstpos;
        int equalpos;
        int ampersandpos;

        if (currentStatus == MultiPartStatus.NOTSTARTED)
        {
            currentStatus = MultiPartStatus.DISPOSITION;
        }

        bool contRead = true;

        try
        {
            while (undecodedChunk.IsReadable() && contRead)
            {
                char read = (char) undecodedChunk.ReadByte();
                currentpos++;

                switch (currentStatus)
                {
                case MultiPartStatus.DISPOSITION:// search '='
                    if (read == '=')
                    {
                        currentStatus = MultiPartStatus.FIELD;
                        equalpos = currentpos - 1;
                        string key = DecodeAttribute(undecodedChunk.ToString(firstpos, equalpos - firstpos, charset),
                                charset);
                        currentAttribute = factory.CreateAttribute(request, key);
                        firstpos = currentpos;
                    }
                    else if (read == '&' ||
                            (isLastChunk && !undecodedChunk.IsReadable() && HasFormBody()))
                    { // special empty FIELD
                        currentStatus = MultiPartStatus.DISPOSITION;
                        ampersandpos = read == '&' ? currentpos - 1 : currentpos;
                        string key = DecodeAttribute(
                                undecodedChunk.ToString(firstpos, ampersandpos - firstpos, charset), charset);
                        // Some weird request bodies start with an '&' character, eg: &name=J&age=17.
                        // In that case, key would be "", will get exception:
                        // java.lang.IllegalArgumentException: Param 'name' must not be empty;
                        // Just check and skip empty key.
                        if (key.Length != 0)
                        {
                            currentAttribute = factory.CreateAttribute(request, key);
                            currentAttribute.SetValue(""); // empty
                            AddHttpData(currentAttribute);
                        }

                        currentAttribute = null;
                        firstpos = currentpos;
                        contRead = true;
                    }

                    break;
                case MultiPartStatus.FIELD:// search '&' or end of line
                    if (read == '&')
                    {
                        currentStatus = MultiPartStatus.DISPOSITION;
                        ampersandpos = currentpos - 1;
                        SetFinalBuffer(undecodedChunk.RetainedSlice(firstpos, ampersandpos - firstpos));
                        firstpos = currentpos;
                        contRead = true;
                    }
                    else if (read == HttpConstants.CR)
                    {
                        if (undecodedChunk.IsReadable())
                        {
                            read = (char) undecodedChunk.ReadByte();
                            currentpos++;

                            if (read == HttpConstants.LF)
                            {
                                currentStatus = MultiPartStatus.PREEPILOGUE;
                                ampersandpos = currentpos - 2;
                                SetFinalBuffer(undecodedChunk.RetainedSlice(firstpos, ampersandpos - firstpos));
                                firstpos = currentpos;
                                contRead = false;
                            }
                            else
                            {
                                // Error
                                throw new ErrorDataDecoderException("Bad end of line");
                            }
                        }
                        else
                        {
                            currentpos--;
                        }
                    }
                    else if (read == HttpConstants.LF)
                    {
                        currentStatus = MultiPartStatus.PREEPILOGUE;
                        ampersandpos = currentpos - 1;
                        SetFinalBuffer(undecodedChunk.RetainedSlice(firstpos, ampersandpos - firstpos));
                        firstpos = currentpos;
                        contRead = false;
                    }

                    break;
                default:
                    // just stop
                    contRead = false;
                    break;
                }
            }

            if (isLastChunk && currentAttribute != null)
            {
                // special case
                ampersandpos = currentpos;
                if (ampersandpos > firstpos)
                {
                    SetFinalBuffer(undecodedChunk.RetainedSlice(firstpos, ampersandpos - firstpos));
                }
                else if (!currentAttribute.IsCompleted())
                {
                    SetFinalBuffer(Unpooled.EmptyBuffer);
                }

                firstpos = currentpos;
                currentStatus = MultiPartStatus.EPILOGUE;
            }
            else if (contRead && currentAttribute != null && currentStatus == MultiPartStatus.FIELD)
            {
                // reset index except if to continue in case of FIELD getStatus
                currentAttribute.AddContent(undecodedChunk.RetainedSlice(firstpos, currentpos - firstpos),
                                            false);
                firstpos = currentpos;
            }

            undecodedChunk.ReaderIndex = firstpos;
        }
        catch (ErrorDataDecoderException)
        {
            // error while decoding
            undecodedChunk.ReaderIndex = firstpos;
            throw;
        }
        catch (Exception e) when (e is IOException || HttpPostBodyUtil.IsIllegalArgument(e))
        {
            // error while decoding
            undecodedChunk.ReaderIndex = firstpos;
            throw new ErrorDataDecoderException(e);
        }
    }

    /**
     * This getMethod fill the map and list with as much Attribute as possible from
     * Body in not Multipart mode.
     *
     * @throws ErrorDataDecoderException
     *             if there is a problem with the charset decoding or other
     *             errors
     */
    private void ParseBodyAttributes()
    {
        if (undecodedChunk == null)
        {
            return;
        }

        ParseBodyAttributesStandard();
    }

    private void SetFinalBuffer(ByteBuf buffer)
    {
        currentAttribute.AddContent(buffer, true);
        ByteBuf decodedBuf = DecodeAttribute(currentAttribute.GetByteBuf(), charset);
        if (decodedBuf != null)
        { // override content only when ByteBuf needed decoding
            currentAttribute.SetContent(decodedBuf);
        }

        AddHttpData(currentAttribute);
        currentAttribute = null;
    }

    /**
     * Decode component
     *
     * @return the decoded component
     */
    private static string DecodeAttribute(string s, Encoding charset)
    {
        try
        {
            return QueryStringDecoder.DecodeComponent(s, charset);
        }
        catch (Exception e) when (HttpPostBodyUtil.IsIllegalArgument(e))
        {
            throw new ErrorDataDecoderException("Bad string: '" + s + '\'', e);
        }
    }

    private static ByteBuf DecodeAttribute(ByteBuf b, Encoding charset)
    {
        int firstEscaped = b.ForEachByte(new UrlEncodedDetector().Process);
        if (firstEscaped == -1)
        {
            return null; // nothing to decode
        }

        ByteBuf buf = b.Alloc().Buffer(b.ReadableBytes);
        UrlDecoder urlDecode = new UrlDecoder(buf);
        int idx = b.ForEachByte(urlDecode.Process);

        if (urlDecode.nextEscapedIdx != 0)
        { // incomplete hex byte
            if (idx == -1)
            {
                idx = b.ReadableBytes - 1;
            }

            idx -= urlDecode.nextEscapedIdx - 1;
            buf.Release();
            throw new ErrorDataDecoderException(
                string.Format(CultureInfo.InvariantCulture, "Invalid hex byte at index '{0}' in string: '{1}'", idx,
                    b.ToString(charset)));
        }

        return buf;
    }

    /**
     * Destroy the {@link HttpPostStandardRequestDecoder} and release all it resources. After this method
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
     * Clean all {@link HttpData}s for the current request.
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
     * Check if request has headers indicating that it contains form body
     */
    private bool HasFormBody()
    {
        string contentHeaderValue = request.Headers().Get(HttpHeaderNames.CONTENT_TYPE);
        if (contentHeaderValue == null)
        {
            return false;
        }

        return HttpHeaderValues.APPLICATION_X_WWW_FORM_URLENCODED.ContentEquals(new StringCharSequence(contentHeaderValue))
                || HttpHeaderValues.MULTIPART_FORM_DATA.ContentEquals(new StringCharSequence(contentHeaderValue));
    }

    private sealed class UrlEncodedDetector
    {
        public bool Process(byte value)
        {
            return value != '%' && value != '+';
        }
    }

    private sealed class UrlDecoder
    {

        private readonly ByteBuf output;
        internal int nextEscapedIdx;
        private byte hiByte;

        internal UrlDecoder(ByteBuf output)
        {
            this.output = output;
        }

        public bool Process(byte value)
        {
            if (nextEscapedIdx != 0)
            {
                if (nextEscapedIdx == 1)
                {
                    hiByte = value;
                    ++nextEscapedIdx;
                }
                else
                {
                    int hi = StringUtil.DecodeHexNibble((char) hiByte);
                    int lo = StringUtil.DecodeHexNibble((char) value);
                    if (hi == -1 || lo == -1)
                    {
                        ++nextEscapedIdx;
                        return false;
                    }

                    output.WriteByte((hi << 4) + lo);
                    nextEscapedIdx = 0;
                }
            }
            else if (value == '%')
            {
                nextEscapedIdx = 1;
            }
            else if (value == '+')
            {
                output.WriteByte(' ');
            }
            else
            {
                output.WriteByte(value);
            }

            return true;
        }
    }
}
