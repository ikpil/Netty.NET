/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http.Multipart;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.Multipart.HttpPostRequestEncoder;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

/** {@link HttpPostRequestEncoder} test case. */
// CLR: new File(getClass().getResource("/file-01.txt").toURI()) is ResourcesUtil.GetFile(typeof(...), "file-01.txt"):
// the resources are deployed next to the test assembly (the lead's resource decision); File is FileInfo and
// file.length() is AbstractDiskHttpData.FileLength. readChunk((ByteBufAllocator) null) is
// ReadChunk((IByteBufAllocator) null). HttpRequest/HttpContent/LastHttpContent are the I-prefixed interfaces,
// CharsetUtil.UTF_8 is Encoding.UTF8 and StringUtil.NEWLINE the Common StringUtil.NEWLINE. The static imports of
// HttpMethod.POST/HttpVersion.HTTP_1_1 and HttpPostBodyUtil.chunkSize are written in full. assertInstanceOf(type,
// value, message) is AssertM.True of an `is` test with the message, and content.unwrap() == content is
// ReferenceEquals. LinkedHashMap<String, String> is a list of key/value pairs, which keeps the insertion order the
// test relies on, and StringBuilder.setLength(n) is the Length property. new String(char[]) after Arrays.fill is
// new string(char, count).
public class HttpPostRequestEncoderTest
{

    [Fact]
    public void TestAllowedMethods()
    {
        ShouldThrowExceptionIfNotAllowed(HttpMethod.CONNECT);
        ShouldThrowExceptionIfNotAllowed(HttpMethod.PUT);
        ShouldThrowExceptionIfNotAllowed(HttpMethod.POST);
        ShouldThrowExceptionIfNotAllowed(HttpMethod.PATCH);
        ShouldThrowExceptionIfNotAllowed(HttpMethod.DELETE);
        ShouldThrowExceptionIfNotAllowed(HttpMethod.GET);
        ShouldThrowExceptionIfNotAllowed(HttpMethod.HEAD);
        ShouldThrowExceptionIfNotAllowed(HttpMethod.OPTIONS);
        Assert.Throws<ErrorDataEncoderException>(() => ShouldThrowExceptionIfNotAllowed(HttpMethod.TRACE));
    }

    private void ShouldThrowExceptionIfNotAllowed(HttpMethod method)
    {
        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                method, "http://localhost");

        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(request, true);
        FileInfo file1 = ResourcesUtil.GetFile(GetType(), "file-01.txt");
        encoder.AddBodyAttribute("foo", "bar");
        encoder.AddBodyFileUpload("quux", file1, "text/plain", false);

        string multipartDataBoundary = encoder.multipartDataBoundary;
        string content = GetRequestBody(encoder);

        string expected = "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"foo\"" + "\r\n" +
                CONTENT_LENGTH + ": 3" + "\r\n" +
                CONTENT_TYPE + ": text/plain; charset=UTF-8" + "\r\n" +
                "\r\n" +
                "bar" +
                "\r\n" +
                "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"quux\"; filename=\"file-01.txt\"" + "\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file1) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 01" + StringUtil.NEWLINE +
                "\r\n" +
                "--" + multipartDataBoundary + "--" + "\r\n";

        Assert.Equal(expected, content);
    }

    [Fact]
    public void TestSingleFileUploadNoName()
    {
        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST, "http://localhost");

        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(request, true);
        FileInfo file1 = ResourcesUtil.GetFile(GetType(), "file-01.txt");
        encoder.AddBodyAttribute("foo", "bar");
        encoder.AddBodyFileUpload("quux", "", file1, "text/plain", false);

        string multipartDataBoundary = encoder.multipartDataBoundary;
        string content = GetRequestBody(encoder);

        string expected = "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"foo\"" + "\r\n" +
                CONTENT_LENGTH + ": 3" + "\r\n" +
                CONTENT_TYPE + ": text/plain; charset=UTF-8" + "\r\n" +
                "\r\n" +
                "bar" +
                "\r\n" +
                "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"quux\"\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file1) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 01" + StringUtil.NEWLINE +
                "\r\n" +
                "--" + multipartDataBoundary + "--" + "\r\n";

        Assert.Equal(expected, content);
    }

    [Fact]
    public void TestMultiFileUploadInMixedMode()
    {
        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST, "http://localhost");

        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(request, true);
        FileInfo file1 = ResourcesUtil.GetFile(GetType(), "file-01.txt");
        FileInfo file2 = ResourcesUtil.GetFile(GetType(), "file-02.txt");
        FileInfo file3 = ResourcesUtil.GetFile(GetType(), "file-03.txt");
        encoder.AddBodyAttribute("foo", "bar");
        encoder.AddBodyFileUpload("quux", file1, "text/plain", false);
        encoder.AddBodyFileUpload("quux", file2, "text/plain", false);
        encoder.AddBodyFileUpload("quux", file3, "text/plain", false);

        // We have to query the value of these two fields before finalizing
        // the request, which unsets one of them.
        string multipartDataBoundary = encoder.multipartDataBoundary;
        string multipartMixedBoundary = encoder.multipartMixedBoundary;
        string content = GetRequestBody(encoder);

        string expected = "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"foo\"" + "\r\n" +
                CONTENT_LENGTH + ": 3" + "\r\n" +
                CONTENT_TYPE + ": text/plain; charset=UTF-8" + "\r\n" +
                "\r\n" +
                "bar" + "\r\n" +
                "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"quux\"" + "\r\n" +
                CONTENT_TYPE + ": multipart/mixed; boundary=" + multipartMixedBoundary + "\r\n" +
                "\r\n" +
                "--" + multipartMixedBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": attachment; filename=\"file-01.txt\"" + "\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file1) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 01" + StringUtil.NEWLINE +
                "\r\n" +
                "--" + multipartMixedBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": attachment; filename=\"file-02.txt\"" + "\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file2) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 02" + StringUtil.NEWLINE +
                "\r\n" +
                "--" + multipartMixedBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": attachment; filename=\"file-03.txt\"" + "\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file3) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 03" + StringUtil.NEWLINE +
                "\r\n" +
                "--" + multipartMixedBoundary + "--" + "\r\n" +
                "--" + multipartDataBoundary + "--" + "\r\n";

        Assert.Equal(expected, content);
    }

    [Fact]
    public void TestMultiFileUploadInMixedModeNoName()
    {
        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST, "http://localhost");

        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(request, true);
        FileInfo file1 = ResourcesUtil.GetFile(GetType(), "file-01.txt");
        FileInfo file2 = ResourcesUtil.GetFile(GetType(), "file-02.txt");
        encoder.AddBodyAttribute("foo", "bar");
        encoder.AddBodyFileUpload("quux", "", file1, "text/plain", false);
        encoder.AddBodyFileUpload("quux", "", file2, "text/plain", false);

        // We have to query the value of these two fields before finalizing
        // the request, which unsets one of them.
        string multipartDataBoundary = encoder.multipartDataBoundary;
        string multipartMixedBoundary = encoder.multipartMixedBoundary;
        string content = GetRequestBody(encoder);

        string expected = "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"foo\"" + "\r\n" +
                CONTENT_LENGTH + ": 3" + "\r\n" +
                CONTENT_TYPE + ": text/plain; charset=UTF-8" + "\r\n" +
                "\r\n" +
                "bar" + "\r\n" +
                "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"quux\"" + "\r\n" +
                CONTENT_TYPE + ": multipart/mixed; boundary=" + multipartMixedBoundary + "\r\n" +
                "\r\n" +
                "--" + multipartMixedBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": attachment\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file1) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 01" + StringUtil.NEWLINE +
                "\r\n" +
                "--" + multipartMixedBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": attachment\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file2) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 02" + StringUtil.NEWLINE +
                "\r\n" +
                "--" + multipartMixedBoundary + "--" + "\r\n" +
                "--" + multipartDataBoundary + "--" + "\r\n";

        Assert.Equal(expected, content);
    }

    [Fact]
    public void TestSingleFileUploadInHtml5Mode()
    {
        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST, "http://localhost");

        DefaultHttpDataFactory factory = new DefaultHttpDataFactory(DefaultHttpDataFactory.MINSIZE);

        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(factory,
                request, true, Encoding.UTF8, EncoderMode.HTML5);
        FileInfo file1 = ResourcesUtil.GetFile(GetType(), "file-01.txt");
        FileInfo file2 = ResourcesUtil.GetFile(GetType(), "file-02.txt");
        encoder.AddBodyAttribute("foo", "bar");
        encoder.AddBodyFileUpload("quux", file1, "text/plain", false);
        encoder.AddBodyFileUpload("quux", file2, "text/plain", false);

        string multipartDataBoundary = encoder.multipartDataBoundary;
        string content = GetRequestBody(encoder);

        string expected = "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"foo\"" + "\r\n" +
                CONTENT_LENGTH + ": 3" + "\r\n" +
                CONTENT_TYPE + ": text/plain; charset=UTF-8" + "\r\n" +
                "\r\n" +
                "bar" + "\r\n" +
                "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"quux\"; filename=\"file-01.txt\"" + "\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file1) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 01" + StringUtil.NEWLINE + "\r\n" +
                "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"quux\"; filename=\"file-02.txt\"" + "\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file2) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 02" + StringUtil.NEWLINE +
                "\r\n" +
                "--" + multipartDataBoundary + "--" + "\r\n";

        Assert.Equal(expected, content);
    }

    [Fact]
    public void TestMultiFileUploadInHtml5Mode()
    {
        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST, "http://localhost");

        DefaultHttpDataFactory factory = new DefaultHttpDataFactory(DefaultHttpDataFactory.MINSIZE);

        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(factory,
                request, true, Encoding.UTF8, EncoderMode.HTML5);
        FileInfo file1 = ResourcesUtil.GetFile(GetType(), "file-01.txt");
        encoder.AddBodyAttribute("foo", "bar");
        encoder.AddBodyFileUpload("quux", file1, "text/plain", false);

        string multipartDataBoundary = encoder.multipartDataBoundary;
        string content = GetRequestBody(encoder);

        string expected = "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"foo\"" + "\r\n" +
                CONTENT_LENGTH + ": 3" + "\r\n" +
                CONTENT_TYPE + ": text/plain; charset=UTF-8" + "\r\n" +
                "\r\n" +
                "bar" +
                "\r\n" +
                "--" + multipartDataBoundary + "\r\n" +
                CONTENT_DISPOSITION + ": form-data; name=\"quux\"; filename=\"file-01.txt\"" + "\r\n" +
                CONTENT_LENGTH + ": " + AbstractDiskHttpData.FileLength(file1) + "\r\n" +
                CONTENT_TYPE + ": text/plain" + "\r\n" +
                CONTENT_TRANSFER_ENCODING + ": binary" + "\r\n" +
                "\r\n" +
                "File 01" + StringUtil.NEWLINE +
                "\r\n" +
                "--" + multipartDataBoundary + "--" + "\r\n";

        Assert.Equal(expected, content);
    }

    [Fact]
    public void TestHttpPostRequestEncoderSlicedBuffer()
    {
        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST, "http://localhost");

        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(request, true);
        // add Form attribute
        encoder.AddBodyAttribute("getform", "POST");
        encoder.AddBodyAttribute("info", "first value");
        encoder.AddBodyAttribute("secondinfo", "secondvalue a&");
        encoder.AddBodyAttribute("thirdinfo", "short text");
        int length = 100000;
        string longText = new string('a', length);
        encoder.AddBodyAttribute("fourthinfo", longText.Substring(0, 7470));
        FileInfo file1 = ResourcesUtil.GetFile(GetType(), "file-01.txt");
        encoder.AddBodyFileUpload("myfile", file1, "application/x-zip-compressed", false);
        encoder.FinalizeRequest();

        while (! encoder.IsEndOfInput())
        {
            IHttpContent httpContent = encoder.ReadChunk((IByteBufAllocator) null);
            ByteBuf content = httpContent.Content;
            int refCnt = content.RefCnt;
            Assert.True((ReferenceEquals(content.Unwrap(), content) || content.Unwrap() == null) && refCnt == 1 ||
                    !ReferenceEquals(content.Unwrap(), content) && refCnt == 2,
                    "content: " + content + " content.unwrap(): " + content.Unwrap() + " refCnt: " + refCnt);
            httpContent.Release();
        }

        encoder.CleanFiles();
        encoder.Close();
    }

    private static string GetRequestBody(HttpPostRequestEncoder encoder)
    {
        encoder.FinalizeRequest();

        List<IInterfaceHttpData> chunks = encoder.multipartHttpDatas;
        ByteBuf[] buffers = new ByteBuf[chunks.Count];

        for (int i = 0; i < buffers.Length; i++)
        {
            IInterfaceHttpData data = chunks[i];
            if (data is InternalAttribute)
            {
                buffers[i] = ((InternalAttribute) data).ToByteBuf();
            }
            else if (data is IHttpData)
            {
                buffers[i] = ((IHttpData) data).GetByteBuf();
            }
        }

        ByteBuf content = Unpooled.WrappedBuffer(buffers);
        string contentStr = content.ToString(Encoding.UTF8);
        content.Release();
        return contentStr;
    }

    [Fact]
    public void TestDataIsMultipleOfChunkSize1()
    {
        DefaultHttpDataFactory factory = new DefaultHttpDataFactory(DefaultHttpDataFactory.MINSIZE);
        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST, "http://localhost");
        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(factory, request, true,
                HttpConstants.DEFAULT_CHARSET, HttpPostRequestEncoder.EncoderMode.RFC1738);

        MemoryFileUpload first = new MemoryFileUpload("resources", "", "application/json", null,
                Encoding.UTF8, -1);
        first.SetMaxSize(-1);
        first.SetContent(new MemoryStream(new byte[7955]));
        encoder.AddBodyHttpData(first);

        MemoryFileUpload second = new MemoryFileUpload("resources2", "", "application/json", null,
                Encoding.UTF8, -1);
        second.SetMaxSize(-1);
        second.SetContent(new MemoryStream(new byte[7928]));
        encoder.AddBodyHttpData(second);

        Assert.NotNull(encoder.FinalizeRequest());

        CheckNextChunkSize(encoder, 8080);
        CheckNextChunkSize(encoder, 8080);

        IHttpContent httpContent = encoder.ReadChunk((IByteBufAllocator) null);
        Assert.True(httpContent is ILastHttpContent, "Expected LastHttpContent is not received");
        httpContent.Release();

           Assert.True(encoder.IsEndOfInput(), "Expected end of input is not receive");
    }

    [Fact]
    public void TestDataIsMultipleOfChunkSize2()
    {
        DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST, "http://localhost");
        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(request, true);
        int length = 7943;
        string longText = new string('a', length);
        encoder.AddBodyAttribute("foo", longText);

        Assert.NotNull(encoder.FinalizeRequest());

        CheckNextChunkSize(encoder, 8080);

        IHttpContent httpContent = encoder.ReadChunk((IByteBufAllocator) null);
        Assert.True(httpContent is ILastHttpContent, "Expected LastHttpContent is not received");
        httpContent.Release();

        Assert.True(encoder.IsEndOfInput(), "Expected end of input is not receive");
    }

    private static void CheckNextChunkSize(HttpPostRequestEncoder encoder, int sizeWithoutDelimiter)
    {
        // 16 bytes as HttpPostRequestEncoder uses Long.toHexString(...) to generate a hex-string which will be between
        // 2 and 16 bytes.
        // See https://github.com/netty/netty/blob/4.1/codec-http/src/main/java/io/netty/handler/
        // codec/http/multipart/HttpPostRequestEncoder.java#L291
        int expectedSizeMin = sizeWithoutDelimiter + 2;
        int expectedSizeMax = sizeWithoutDelimiter + 16;

        IHttpContent httpContent = encoder.ReadChunk((IByteBufAllocator) null);

        int readable = httpContent.Content.ReadableBytes;
        bool expectedSize = readable >= expectedSizeMin && readable <= expectedSizeMax;
        Assert.True(expectedSize, "Chunk size is not in expected range (" + expectedSizeMin + " - "
                + expectedSizeMax + "), was: " + readable);
        httpContent.Release();
    }

    [Fact]
    public void TestEncodeChunkedContent()
    {
        IHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(req, false);

        int length = 8077 + 8096;
        string longText = new string('a', length);

        encoder.AddBodyAttribute("data", longText);
        encoder.AddBodyAttribute("moreData", "abcd");

        Assert.NotNull(encoder.FinalizeRequest());

        while (!encoder.IsEndOfInput())
        {
            encoder.ReadChunk((IByteBufAllocator) null).Release();
        }

        Assert.True(encoder.IsEndOfInput());
        encoder.CleanFiles();
    }

    [Fact]
    public void TestEncodeChunkedContentV2()
    {
        List<KeyValuePair<string, string>> @params = new List<KeyValuePair<string, string>>();
        @params.Add(new KeyValuePair<string, string>("data", LargeText("data", 3)));
        @params.Add(new KeyValuePair<string, string>("empty", ""));
        @params.Add(new KeyValuePair<string, string>("tiny", "a"));
        @params.Add(new KeyValuePair<string, string>("large",
                GenerateString(HttpPostBodyUtil.chunkSize - 7 - 7 - 1 - 8)));
        @params.Add(new KeyValuePair<string, string>("huge", LargeText("huge", 8)));
        @params.Add(new KeyValuePair<string, string>("small", "abcd"));

        StringBuilder expectQueryStr = new StringBuilder();
        IHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        HttpPostRequestEncoder encoder = new HttpPostRequestEncoder(req, false);

        foreach (KeyValuePair<string, string> entry in @params)
        {
            string key = entry.Key;
            string value = entry.Value;
            encoder.AddBodyAttribute(key, value);
            expectQueryStr.Append(key).Append('=').Append(value).Append('&');
        }

        expectQueryStr.Length = expectQueryStr.Length - 1; //remove last '&'
        Assert.NotNull(encoder.FinalizeRequest());

        StringBuilder chunks = new StringBuilder();

        while (!encoder.IsEndOfInput())
        {
            IHttpContent httpContent = encoder.ReadChunk((IByteBufAllocator) null);
            chunks.Append(httpContent.Content.ToString(Encoding.UTF8));
            httpContent.Release();
        }

        Assert.True(encoder.IsEndOfInput());
        Assert.Equal(expectQueryStr.ToString(), chunks.ToString());
        encoder.CleanFiles();
    }

    private string LargeText(string key, int multipleChunks)
    {
        return GenerateString(HttpPostBodyUtil.chunkSize * multipleChunks - key.Length - 2); // 2 is '=' and '&'
    }

    private string GenerateString(int length)
    {
        return new string('a', length);
    }
}
