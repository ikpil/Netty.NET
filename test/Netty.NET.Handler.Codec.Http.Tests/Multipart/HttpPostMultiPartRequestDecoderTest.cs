/*
 * Copyright 2021 The Netty Project
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
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.Multipart;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

// CLR: FullHttpRequest/HttpRequest are IFullHttpRequest/IHttpRequest, CharsetUtil.UTF_8/US_ASCII are Encoding.UTF8/
// ASCII and String.getBytes(UTF_8) is Encoding.UTF8.GetBytes; content.length() and String.length() are the string
// Length. The headers' set(String, Object) takes the boxed int as Object. Arrays.fill/copyOf/copyOfRange are
// Array.Fill and array slices, Arrays.equals SequenceEqual, and toArray(new InterfaceHttpData[0]) is a copy of the list
// into an array; Integer.toString is the invariant-culture text. currentPartialHttpData() and getBodyHttpDatas() are
// the decoder methods, content() is the Content property and refCnt() RefCnt; the (HttpData) casts stay casts.
// assertThrows(T.class, ...) is Assert.Throws<T>, the exact type, as the decoder throws ErrorDataDecoderException
// itself. The test helper commonTestBigFileDelimiterInMiddleChunk keeps its 10 MB of chunks.
public class HttpPostMultiPartRequestDecoderTest
{

    [Fact]
    public void TestDecodeFullHttpRequestWithNoContentTypeHeader()
    {
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        try
        {
            Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(() =>
                    new HttpPostMultipartRequestDecoder(req));
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void TestDecodeFullHttpRequestWithInvalidCharset()
    {
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        req.Headers().Set(HttpHeaderNames.CONTENT_TYPE,
                "multipart/form-data; boundary=--89421926422648 [; charset=UTF-8]");

        try
        {
            Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(() =>
                    new HttpPostMultipartRequestDecoder(req));
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void TestDecodeFullHttpRequestWithInvalidPayloadReleaseBuffer()
    {
        string content = "\n--861fbeab-cd20-470c-9609-d40a0f704466\n" +
                "Content-Disposition: form-data; name=\"image1\"; filename*=\"'some.jpeg\"\n" +
                        "Content-Type: image/jpeg\n" +
                        "Content-Length: 1\n" +
                        "x\n" +
                        "--861fbeab-cd20-470c-9609-d40a0f704466--\n";

        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                Unpooled.CopiedBuffer(content, Encoding.ASCII));
        req.Headers().Set("content-type", "multipart/form-data; boundary=861fbeab-cd20-470c-9609-d40a0f704466");
        req.Headers().Set("content-length", content.Length);

        try
        {
            Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(() =>
                    new HttpPostMultipartRequestDecoder(req));
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void DecodeMustCleanFilenameCharacters()
    {
        string content = "\n--861fbeab-cd20-470c-9609-d40a0f704466\r\n" +
                "content-disposition: form-data; " +
                "name=\"file\"; filename=\" dir\\file\0\u007F÷.txt \"\r\n" +
                "content-type: text/plain\r\n" +
                "Content-Length: 1\r\n\r\n" +
                "x\r\n--861fbeab-cd20-470c-9609-d40a0f704466--\r\n";
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                Unpooled.CopiedBuffer(content, Encoding.UTF8));
        req.Headers().Set("content-type", "multipart/form-data; boundary=861fbeab-cd20-470c-9609-d40a0f704466");
        req.Headers().Set("content-length", content.Length);

        HttpPostMultipartRequestDecoder test = new HttpPostMultipartRequestDecoder(req);
        IFileUpload httpData = (IFileUpload) test.GetBodyHttpDatas("file")[0];
        try
        {
            Assert.Equal("dirfile  ÷.txt", httpData.GetFilename());
        }
        finally
        {
            test.Destroy();
        }
    }

    [Fact]
    public void DecodeMustCleanFilenamePercentEncodedCharacters()
    {
        string content = "\n--861fbeab-cd20-470c-9609-d40a0f704466\r\n" +
                "content-disposition: form-data; " +
                "name=\"file\"; filename*=UTF-8''\"%20dir\\file%00%13%7F÷.txt%20\"\r\n" +
                "content-type: text/plain\r\n" +
                "Content-Length: 1\r\n\r\n" +
                "x\r\n--861fbeab-cd20-470c-9609-d40a0f704466--\r\n";
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                Unpooled.CopiedBuffer(content, Encoding.UTF8));
        req.Headers().Set("content-type", "multipart/form-data; boundary=861fbeab-cd20-470c-9609-d40a0f704466");
        req.Headers().Set("content-length", content.Length);

        HttpPostMultipartRequestDecoder test = new HttpPostMultipartRequestDecoder(req);
        IFileUpload httpData = (IFileUpload) test.GetBodyHttpDatas("file")[0];
        try
        {
            Assert.Equal("dirfile   ÷.txt", httpData.GetFilename());
        }
        finally
        {
            test.Destroy();
        }
    }

    [Fact]
    public void TestDelimiterExceedLeftSpaceInCurrentBuffer()
    {
        string delimiter = "--861fbeab-cd20-470c-9609-d40a0f704466";
        string suffix = '\n' + delimiter + "--\n";
        byte[] bsuffix = Encoding.UTF8.GetBytes(suffix);
        int partOfDelimiter = bsuffix.Length / 2;
        int bytesLastChunk = 355 - partOfDelimiter; // to try to have an out of bound since content is > delimiter
        byte[] bsuffix1 = bsuffix[..partOfDelimiter];
        byte[] bsuffix2 = bsuffix[partOfDelimiter..];
        string prefix = delimiter + "\n" +
                        "Content-Disposition: form-data; name=\"image\"; filename=\"guangzhou.jpeg\"\n" +
                        "Content-Type: image/jpeg\n" +
                        "Content-Length: " + bytesLastChunk + "\n\n";
        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");
        request.Headers().Set("content-type", "multipart/form-data; boundary=861fbeab-cd20-470c-9609-d40a0f704466");
        request.Headers().Set("content-length", prefix.Length + bytesLastChunk + suffix.Length);

        // Factory using Memory mode
        IHttpDataFactory factory = new DefaultHttpDataFactory(false);
        HttpPostMultipartRequestDecoder decoder = new HttpPostMultipartRequestDecoder(factory, request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(prefix));
        DefaultHttpContent httpContent = new DefaultHttpContent(buf);
        decoder.Offer(httpContent);
        Assert.NotNull((IHttpData) decoder.CurrentPartialHttpData());
        httpContent.Release();
        // Chunk less than Delimiter size but containing part of delimiter
        byte[] body = new byte[bytesLastChunk + bsuffix1.Length];
        Array.Fill(body, (byte) 2);
        for (int i = 0; i < bsuffix1.Length; i++)
        {
            body[bytesLastChunk + i] = bsuffix1[i];
        }
        ByteBuf content = Unpooled.WrappedBuffer(body);
        httpContent = new DefaultHttpContent(content);
        decoder.Offer(httpContent); // Ouf of range before here
        Assert.NotNull(((IHttpData) decoder.CurrentPartialHttpData()).Content);
        httpContent.Release();
        content = Unpooled.WrappedBuffer(bsuffix2);
        httpContent = new DefaultHttpContent(content);
        decoder.Offer(httpContent);
        Assert.Null((IHttpData) decoder.CurrentPartialHttpData());
        httpContent.Release();
        decoder.Offer(new DefaultLastHttpContent());
        IFileUpload data = (IFileUpload) decoder.GetBodyHttpDatas()[0];
        Assert.Equal(data.Length(), bytesLastChunk);
        Assert.Equal(true, data.IsInMemory());

        IInterfaceHttpData[] httpDatas = ToArray(decoder);
        foreach (IInterfaceHttpData httpData in httpDatas)
        {
            Assert.Equal(1, httpData.RefCnt, "Before cleanAllHttpData should be 1");
        }
        factory.CleanAllHttpData();
        foreach (IInterfaceHttpData httpData in httpDatas)
        {
            Assert.Equal(1, httpData.RefCnt, "After cleanAllHttpData should be 1 if in Memory");
        }
        decoder.Destroy();
        foreach (IInterfaceHttpData httpData in httpDatas)
        {
            Assert.Equal(0, httpData.RefCnt, "RefCnt should be 0");
        }
    }

    private void CommonTestBigFileDelimiterInMiddleChunk(IHttpDataFactory factory, bool inMemory)
    {
        int nbChunks = 100;
        int bytesPerChunk = 100000;
        int bytesLastChunk = 10000;
        int fileSize = bytesPerChunk * nbChunks + bytesLastChunk; // set Xmx to a number lower than this and it crashes

        string delimiter = "--861fbeab-cd20-470c-9609-d40a0f704466";
        string prefix = delimiter + "\n" +
                "Content-Disposition: form-data; name=\"image\"; filename=\"guangzhou.jpeg\"\n" +
                "Content-Type: image/jpeg\n" +
                "Content-Length: " + fileSize + "\n" +
                "\n";

        string suffix1 = "\n" +
                "--861fbeab-";
        string suffix2 = "cd20-470c-9609-d40a0f704466--\n";
        string suffix = suffix1 + suffix2;

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");
        request.Headers().Set("content-type", "multipart/form-data; boundary=861fbeab-cd20-470c-9609-d40a0f704466");
        request.Headers().Set("content-length", prefix.Length + fileSize + suffix.Length);

        HttpPostMultipartRequestDecoder decoder = new HttpPostMultipartRequestDecoder(factory, request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(prefix));
        DefaultHttpContent httpContent = new DefaultHttpContent(buf);
        decoder.Offer(httpContent);
        Assert.NotNull(((IHttpData) decoder.CurrentPartialHttpData()).Content);
        httpContent.Release();

        byte[] body = new byte[bytesPerChunk];
        Array.Fill(body, (byte) 1);
        // Set first bytes as CRLF to ensure it is correctly getting the last CRLF
        body[0] = HttpConstants.CR;
        body[1] = HttpConstants.LF;
        for (int i = 0; i < nbChunks; i++)
        {
            ByteBuf content = Unpooled.WrappedBuffer(body, 0, bytesPerChunk);
            httpContent = new DefaultHttpContent(content);
            decoder.Offer(httpContent); // **OutOfMemory previously here**
            Assert.NotNull(((IHttpData) decoder.CurrentPartialHttpData()).Content);
            httpContent.Release();
        }

        byte[] bsuffix1 = Encoding.UTF8.GetBytes(suffix1);
        byte[] previousLastbody = new byte[bytesLastChunk - bsuffix1.Length];
        byte[] bdelimiter = Encoding.UTF8.GetBytes(delimiter);
        byte[] lastbody = new byte[2 * bsuffix1.Length];
        Array.Fill(previousLastbody, (byte) 1);
        previousLastbody[0] = HttpConstants.CR;
        previousLastbody[1] = HttpConstants.LF;
        Array.Fill(lastbody, (byte) 1);
        // put somewhere a not valid delimiter
        for (int i = 0; i < bdelimiter.Length; i++)
        {
            previousLastbody[i + 10] = bdelimiter[i];
        }
        lastbody[0] = HttpConstants.CR;
        lastbody[1] = HttpConstants.LF;
        for (int i = 0; i < bsuffix1.Length; i++)
        {
            lastbody[bsuffix1.Length + i] = bsuffix1[i];
        }

        ByteBuf content2 = Unpooled.WrappedBuffer(previousLastbody, 0, previousLastbody.Length);
        httpContent = new DefaultHttpContent(content2);
        decoder.Offer(httpContent);
        Assert.NotNull(((IHttpData) decoder.CurrentPartialHttpData()).Content);
        httpContent.Release();
        content2 = Unpooled.WrappedBuffer(lastbody, 0, lastbody.Length);
        httpContent = new DefaultHttpContent(content2);
        decoder.Offer(httpContent);
        Assert.NotNull(((IHttpData) decoder.CurrentPartialHttpData()).Content);
        httpContent.Release();
        content2 = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(suffix2));
        httpContent = new DefaultHttpContent(content2);
        decoder.Offer(httpContent);
        Assert.Null(decoder.CurrentPartialHttpData());
        httpContent.Release();
        decoder.Offer(new DefaultLastHttpContent());

        IFileUpload data = (IFileUpload) decoder.GetBodyHttpDatas()[0];
        Assert.Equal(data.Length(), fileSize);
        Assert.Equal(inMemory, data.IsInMemory());
        if (data.IsInMemory())
        {
            // To be done only if not inMemory: assertEquals(data.get().length, fileSize);
            Assert.False(data.GetByteBuf().Capacity < 1024 * 1024,
                    "Capacity should be higher than 1M");
        }
        Assert.True(decoder.GetCurrentAllocatedCapacity() < 1024 * 1024,
                "Capacity should be less than 1M");
        IInterfaceHttpData[] httpDatas = ToArray(decoder);
        foreach (IInterfaceHttpData httpData in httpDatas)
        {
            Assert.Equal(1, httpData.RefCnt, "Before cleanAllHttpData should be 1");
        }
        factory.CleanAllHttpData();
        foreach (IInterfaceHttpData httpData in httpDatas)
        {
            Assert.Equal(inMemory? 1 : 0, httpData.RefCnt, "After cleanAllHttpData should be 1 if in Memory");
        }
        decoder.Destroy();
        foreach (IInterfaceHttpData httpData in httpDatas)
        {
            Assert.Equal(0, httpData.RefCnt, "RefCnt should be 0");
        }
    }

    [Fact]
    public void TestBIgFileUploadDelimiterInMiddleChunkDecoderDiskFactory()
    {
        // Factory using Disk mode
        IHttpDataFactory factory = new DefaultHttpDataFactory(true);

        CommonTestBigFileDelimiterInMiddleChunk(factory, false);
    }

    [Fact]
    public void TestBIgFileUploadDelimiterInMiddleChunkDecoderMemoryFactory()
    {
        // Factory using Memory mode
        IHttpDataFactory factory = new DefaultHttpDataFactory(false);

        CommonTestBigFileDelimiterInMiddleChunk(factory, true);
    }

    [Fact]
    public void TestBIgFileUploadDelimiterInMiddleChunkDecoderMixedFactory()
    {
        // Factory using Mixed mode, where file shall be on Disk
        IHttpDataFactory factory = new DefaultHttpDataFactory(10000);

        CommonTestBigFileDelimiterInMiddleChunk(factory, false);
    }

    [Fact]
    public void TestNotBadReleaseBuffersDuringDecodingDiskFactory()
    {
        // Using Disk Factory
        IHttpDataFactory factory = new DefaultHttpDataFactory(true);
        CommonNotBadReleaseBuffersDuringDecoding(factory, false);
    }

    [Fact]
    public void TestNotBadReleaseBuffersDuringDecodingMemoryFactory()
    {
        // Using Memory Factory
        IHttpDataFactory factory = new DefaultHttpDataFactory(false);
        CommonNotBadReleaseBuffersDuringDecoding(factory, true);
    }

    [Fact]
    public void TestNotBadReleaseBuffersDuringDecodingMixedFactory()
    {
        // Using Mixed Factory
        IHttpDataFactory factory = new DefaultHttpDataFactory(100);
        CommonNotBadReleaseBuffersDuringDecoding(factory, false);
    }

    [Fact]
    public void TestDecodeFullHttpRequestWithOptionalParameters()
    {
        string content = "\n--861fbeab-cd20-470c-9609-d40a0f704466\r\n" +
                "content-disposition: form-data; " +
                "name=\"file\"; filename=\"myfile.ogg\"\r\n" +
                "content-type: audio/ogg; codecs=opus; charset=UTF8\r\ncontent-transfer-encoding: binary\r\n" +
                "\r\n\u0001\u0002\u0003\u0004\r\n--861fbeab-cd20-470c-9609-d40a0f704466--\r\n\",\n";

        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                Unpooled.CopiedBuffer(content, Encoding.ASCII));
        req.Headers().Set("content-type", "multipart/form-data; boundary=861fbeab-cd20-470c-9609-d40a0f704466");
        req.Headers().Set("content-length", content.Length);

        HttpPostMultipartRequestDecoder test = new HttpPostMultipartRequestDecoder(req);
        IFileUpload httpData = (IFileUpload) test.GetBodyHttpDatas("file")[0];
        Assert.Equal("audio/ogg", httpData.GetContentType());
        test.Destroy();
    }

    private static void CommonNotBadReleaseBuffersDuringDecoding(IHttpDataFactory factory, bool inMemory)
    {
        int nbItems = 20;
        int bytesPerItem = 1000;
        int maxMemory = 500;

        string prefix1 = "\n--861fbeab-cd20-470c-9609-d40a0f704466\n" +
                "Content-Disposition: form-data; name=\"image";
        string prefix2 =
                "\"; filename=\"guangzhou.jpeg\"\n" +
                        "Content-Type: image/jpeg\n" +
                        "Content-Length: " + bytesPerItem + "\n" + "\n";

        string suffix = "\n--861fbeab-cd20-470c-9609-d40a0f704466--\n";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");
        request.Headers().Set("content-type", "multipart/form-data; boundary=861fbeab-cd20-470c-9609-d40a0f704466");
        request.Headers().Set("content-length", nbItems * (prefix1.Length + prefix2.Length + 2 + bytesPerItem)
                + suffix.Length);
        HttpPostMultipartRequestDecoder decoder = new HttpPostMultipartRequestDecoder(factory, request);
        decoder.SetDiscardThreshold(maxMemory);
        for (int rank = 0; rank < nbItems; rank++)
        {
            byte[] bp1 = Encoding.UTF8.GetBytes(prefix1);
            byte[] bp2 = Encoding.UTF8.GetBytes(prefix2);
            byte[] prefix = new byte[bp1.Length + 2 + bp2.Length];
            for (int i = 0; i < bp1.Length; i++)
            {
                prefix[i] = bp1[i];
            }
            byte[] brank = Encoding.UTF8.GetBytes((10 + rank).ToString(CultureInfo.InvariantCulture));
            prefix[bp1.Length] = brank[0];
            prefix[bp1.Length + 1] = brank[1];
            for (int i = 0; i < bp2.Length; i++)
            {
                prefix[bp1.Length + 2 + i] = bp2[i];
            }
            ByteBuf buf = Unpooled.WrappedBuffer(prefix);
            DefaultHttpContent httpContent = new DefaultHttpContent(buf);
            decoder.Offer(httpContent);
            httpContent.Release();
            byte[] body = new byte[bytesPerItem];
            Array.Fill(body, (byte) rank);
            ByteBuf content = Unpooled.WrappedBuffer(body, 0, bytesPerItem);
            httpContent = new DefaultHttpContent(content);
            decoder.Offer(httpContent);
            httpContent.Release();
        }
        byte[] lastbody = Encoding.UTF8.GetBytes(suffix);
        ByteBuf content2 = Unpooled.WrappedBuffer(lastbody, 0, lastbody.Length);
        DefaultHttpContent httpContent2 = new DefaultHttpContent(content2);
        decoder.Offer(httpContent2);
        httpContent2.Release();
        decoder.Offer(new DefaultLastHttpContent());

        for (int rank = 0; rank < nbItems; rank++)
        {
            IFileUpload data = (IFileUpload) decoder.GetBodyHttpData("image" + (10 + rank));
            Assert.Equal(bytesPerItem, data.Length());
            Assert.Equal(inMemory, data.IsInMemory());
            byte[] body = new byte[bytesPerItem];
            Array.Fill(body, (byte) rank);
            Assert.True(body.AsSpan().SequenceEqual(data.Get()));
        }
        // To not be done since will load full file on memory: assertEquals(data.get().length, fileSize);
        // Not mandatory since implicitly called during destroy of decoder
        foreach (IInterfaceHttpData httpData in decoder.GetBodyHttpDatas())
        {
            httpData.Release();
            factory.RemoveHttpDataFromClean(request, httpData);
        }
        factory.CleanAllHttpData();
        decoder.Destroy();
    }

    // Issue #11668
    private static void CommonTestFileDelimiterLFLastChunk(IHttpDataFactory factory, bool inMemory)
    {
        int nbChunks = 2;
        int bytesPerChunk = 100000;
        int bytesLastChunk = 10000;
        int fileSize = bytesPerChunk * nbChunks + bytesLastChunk; // set Xmx to a number lower than this and it crashes

        string delimiter = "--861fbeab-cd20-470c-9609-d40a0f704466";
        string prefix = delimiter + "\n" +
                        "Content-Disposition: form-data; name=\"image\"; filename=\"guangzhou.jpeg\"\n" +
                        "Content-Type: image/jpeg\n" +
                        "Content-Length: " + fileSize + "\n" +
                        "\n";

        string suffix = "--861fbeab-cd20-470c-9609-d40a0f704466--";
        byte[] bsuffix = Encoding.UTF8.GetBytes(suffix);
        byte[] bsuffixReal = new byte[bsuffix.Length + 2];
        for (int i = 0; i < bsuffix.Length; i++)
        {
            bsuffixReal[1 + i] = bsuffix[i];
        }
        bsuffixReal[0] = HttpConstants.LF;
        bsuffixReal[bsuffixReal.Length - 1] = HttpConstants.CR;
        byte[] lastbody = {HttpConstants.LF};

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");
        request.Headers().Set("content-type", "multipart/form-data; boundary=861fbeab-cd20-470c-9609-d40a0f704466");
        // +4 => 2xCRLF (beginning, end)
        request.Headers().Set("content-length", prefix.Length + fileSize + suffix.Length + 4);

        HttpPostMultipartRequestDecoder decoder = new HttpPostMultipartRequestDecoder(factory, request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(prefix));
        DefaultHttpContent httpContent = new DefaultHttpContent(buf);
        decoder.Offer(httpContent);
        Assert.NotNull(((IHttpData) decoder.CurrentPartialHttpData()).Content);
        httpContent.Release();

        byte[] body = new byte[bytesPerChunk];
        Array.Fill(body, (byte) 1);
        // Set first bytes as CRLF to ensure it is correctly getting the last CRLF
        body[0] = HttpConstants.CR;
        body[1] = HttpConstants.LF;
        for (int i = 0; i < nbChunks; i++)
        {
            ByteBuf content = Unpooled.WrappedBuffer(body, 0, bytesPerChunk);
            httpContent = new DefaultHttpContent(content);
            decoder.Offer(httpContent); // **OutOfMemory previously here**
            Assert.NotNull(((IHttpData) decoder.CurrentPartialHttpData()).Content);
            httpContent.Release();
        }
        // Last -2 body = content + CR but no delimiter
        byte[] previousLastbody = new byte[bytesLastChunk + 1];
        Array.Fill(previousLastbody, (byte) 1);
        previousLastbody[bytesLastChunk] = HttpConstants.CR;
        ByteBuf content2 = Unpooled.WrappedBuffer(previousLastbody, 0, previousLastbody.Length);
        httpContent = new DefaultHttpContent(content2);
        decoder.Offer(httpContent);
        Assert.NotNull(decoder.CurrentPartialHttpData());
        httpContent.Release();
        // Last -1 body = LF+delimiter+CR but no LF
        content2 = Unpooled.WrappedBuffer(bsuffixReal, 0, bsuffixReal.Length);
        httpContent = new DefaultHttpContent(content2);
        decoder.Offer(httpContent);
        Assert.Null(decoder.CurrentPartialHttpData());
        httpContent.Release();
        // Last (LF)
        content2 = Unpooled.WrappedBuffer(lastbody, 0, lastbody.Length);
        httpContent = new DefaultHttpContent(content2);
        decoder.Offer(httpContent);
        Assert.Null(decoder.CurrentPartialHttpData());
        httpContent.Release();
        // End
        decoder.Offer(new DefaultLastHttpContent());

        IFileUpload data = (IFileUpload) decoder.GetBodyHttpDatas()[0];
        Assert.Equal(data.Length(), fileSize);
        Assert.Equal(inMemory, data.IsInMemory());
        if (data.IsInMemory())
        {
            // To be done only if not inMemory: assertEquals(data.get().length, fileSize);
            Assert.False(data.GetByteBuf().Capacity < fileSize,
                        "Capacity should be at least file size");
        }
        Assert.True(decoder.GetCurrentAllocatedCapacity() < fileSize,
                   "Capacity should be less than 1M");
        IInterfaceHttpData[] httpDatas = ToArray(decoder);
        foreach (IInterfaceHttpData httpData in httpDatas)
        {
            Assert.Equal(1, httpData.RefCnt, "Before cleanAllHttpData should be 1");
        }
        factory.CleanAllHttpData();
        foreach (IInterfaceHttpData httpData in httpDatas)
        {
            Assert.Equal(inMemory? 1 : 0, httpData.RefCnt, "After cleanAllHttpData should be 1 if in Memory");
        }
        decoder.Destroy();
        foreach (IInterfaceHttpData httpData in httpDatas)
        {
            Assert.Equal(0, httpData.RefCnt, "RefCnt should be 0");
        }
    }

    [Fact]
    public void TestFileDelimiterLFLastChunkDecoderDiskFactory()
    {
        // Factory using Disk mode
        IHttpDataFactory factory = new DefaultHttpDataFactory(true);

        CommonTestFileDelimiterLFLastChunk(factory, false);
    }

    [Fact]
    public void TestFileDelimiterLFLastChunkDecoderMemoryFactory()
    {
        // Factory using Memory mode
        IHttpDataFactory factory = new DefaultHttpDataFactory(false);

        CommonTestFileDelimiterLFLastChunk(factory, true);
    }

    [Fact]
    public void TestFileDelimiterLFLastChunkDecoderMixedFactory()
    {
        // Factory using Mixed mode, where file shall be on Disk
        IHttpDataFactory factory = new DefaultHttpDataFactory(10000);

        CommonTestFileDelimiterLFLastChunk(factory, false);
    }

    // CLR: getBodyHttpDatas().toArray(new InterfaceHttpData[0]).
    private static IInterfaceHttpData[] ToArray(HttpPostMultipartRequestDecoder decoder)
    {
        IInterfaceHttpData[] array = new IInterfaceHttpData[decoder.GetBodyHttpDatas().Count];
        decoder.GetBodyHttpDatas().CopyTo(array, 0);
        return array;
    }

}
