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
using System.Net;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.Multipart;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

/**
 * {@link HttpPostRequestDecoder} test case.
 */
// CLR: FullHttpRequest/HttpRequest/LastHttpContent are the I-prefixed interfaces and DecoderResult the codec-base
// type. The texts are ASCII except the encoded file names. ArrayIndexOutOfBoundsException is
// IndexOutOfRangeException and IllegalArgumentException ArgumentException. URLEncoder.encode(name, "utf-8") of the
// test file names is WebUtility.UrlEncode, which encodes them to the same %XY UTF-8 bytes as the JDK ("Converts a
// text string into a URL-encoded string",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.webutility.urlencode?view=net-10.0; the names contain
// only letters, '_', '.' and non-ASCII characters, on which both agree). assertThrows(T.class,...) is
// Assert.Throws<T> where the decoder throws T itself and Assert.ThrowsAny<DecoderException> where JUnit's
// assertThrows admits the subclasses that the test then tells apart by getClass(); the anonymous Executables are
// lambdas. assertInstanceOf(T.class, x) is Assert.IsAssignableFrom<T> (AssertM.True of an `is` test when a message
// is given), assertFalse(list.isEmpty()) Assert.NotEmpty, AssertJ assertThat(x).hasSize(1) Assert.Single and
// isInstanceOf/isEqualTo Assert.IsAssignableFrom/ Assert.Equal, and fail(message) Assert.Fail.
// UnpooledByteBufAllocator(true).heapBuffer(n, n) is the Buffer port's
// UnpooledByteBufAllocator(true).HeapBuffer(n, n), Unpooled.directBuffer(n) DirectBuffer(n), and
// writeCharSequence(text, US_ASCII) WriteCharSequence(text, ASCII). The unused StringBuffer of
// testHttpPostStandardRequestDecoderToDiskNameContainingUnauthorizedChar is a StringBuilder. The test methods with
// package access (no modifier in JUnit 5) are public, as xUnit requires.
public class HttpPostRequestDecoderTest
{

    [Fact]
    public void TestBinaryStreamUploadWithSpace()
    {
        TestBinaryStreamUpload(true);
    }

    // https://github.com/netty/netty/issues/1575
    [Fact]
    public void TestBinaryStreamUploadWithoutSpace()
    {
        TestBinaryStreamUpload(false);
    }

    private static void TestBinaryStreamUpload(bool withSpace)
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";
        string contentTypeValue;

        if (withSpace)
        {
            contentTypeValue = "multipart/form-data; boundary=" + boundary;
        }
        else
        {
            contentTypeValue = "multipart/form-data;boundary=" + boundary;
        }

        DefaultHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                "http://localhost");

        req.SetDecoderResult(DecoderResult.SUCCESS);
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, contentTypeValue);
        req.Headers().Add(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);

        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);

        foreach (string data in new[] { "", "\r", "\r\r", "\r\r\r" })
        {
            string body =
                    "--" + boundary + "\r\n" +
                            "Content-Disposition: form-data; name=\"file\"; filename=\"tmp-0.txt\"\r\n" +
                            "Content-Type: image/gif\r\n" +
                            "\r\n" +
                            data + "\r\n" +
                            "--" + boundary + "--\r\n";

            // Create decoder instance to test.
            HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);

            ByteBuf buf = Unpooled.CopiedBuffer(body, Encoding.UTF8);
            decoder.Offer(new DefaultHttpContent(buf));
            decoder.Offer(new DefaultHttpContent(Unpooled.EmptyBuffer));

            // Validate it's enough chunks to decode upload.
            Assert.True(decoder.HasNext());

            // Decode binary upload.
            MemoryFileUpload upload = (MemoryFileUpload) decoder.Next();

            // Validate data has been parsed correctly as it was passed into request.
            Assert.Equal(data, upload.GetString(Encoding.UTF8),
                    "Invalid decoded data [data=" + data.Replace("\r", "\\r") + ", upload=" + upload + ']');
            upload.Release();
            decoder.Destroy();
            buf.Release();
        }
    }

    // See https://github.com/netty/netty/issues/1089
    [Fact]
    public void TestFullHttpRequestUpload()
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";

        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                "http://localhost");

        req.SetDecoderResult(DecoderResult.SUCCESS);
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        req.Headers().Add(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);

        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);

        foreach (string data in new[] { "", "\r", "\r\r", "\r\r\r" })
        {
            string body =
                    "--" + boundary + "\r\n" +
                            "Content-Disposition: form-data; name=\"file\"; filename=\"tmp-0.txt\"\r\n" +
                            "Content-Type: image/gif\r\n" +
                            "\r\n" +
                            data + "\r\n" +
                            "--" + boundary + "--\r\n";

            req.Content.WriteBytes(Encoding.UTF8.GetBytes(body));
        }

        // Create decoder instance to test.
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        decoder.Destroy();
        Assert.True(req.Release());
    }

    // See https://github.com/netty/netty/issues/2544
    [Fact]
    public void TestMultipartCodecWithCRasEndOfAttribute()
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";

        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        // Build test case
        string extradata = "aaaa";
        string[] datas = new string[5];

        for (int i = 0; i < 4; i++)
        {
            datas[i] = extradata;

            for (int j = 0; j < i; j++)
            {
                datas[i] += '\r';
            }
        }

        for (int i = 0; i < 4; i++)
        {
            DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                    "http://localhost");
            req.SetDecoderResult(DecoderResult.SUCCESS);
            req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
            req.Headers().Add(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
            string body =
                    "--" + boundary + "\r\n" +
                            "Content-Disposition: form-data; name=\"file" + i + "\"\r\n" +
                            "Content-Type: image/gif\r\n" +
                            "\r\n" +
                            datas[i] + "\r\n" +
                            "--" + boundary + "--\r\n";

            req.Content.WriteBytes(Encoding.UTF8.GetBytes(body));
            // Create decoder instance to test.
            HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
            Assert.NotEmpty(decoder.GetBodyHttpDatas());
            // Check correctness: data size
            IInterfaceHttpData httpdata = decoder.GetBodyHttpData("file" + i);
            Assert.NotNull(httpdata);
            IAttribute attribute = (IAttribute) httpdata;
            byte[] datar = attribute.Get();
            Assert.NotNull(datar);
            Assert.Equal(Encoding.UTF8.GetBytes(datas[i]).Length, datar.Length);

            decoder.Destroy();
            Assert.True(req.Release());
        }
    }

    // See https://github.com/netty/netty/issues/2542
    [Fact]
    public void TestQuotedBoundary()
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";

        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                "http://localhost");

        req.SetDecoderResult(DecoderResult.SUCCESS);
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=\"" + boundary + '"');
        req.Headers().Add(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);

        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);

        foreach (string data in new[] { "", "\r", "\r\r", "\r\r\r" })
        {
            string body =
                    "--" + boundary + "\r\n" +
                            "Content-Disposition: form-data; name=\"file\"; filename=\"tmp-0.txt\"\r\n" +
                            "Content-Type: image/gif\r\n" +
                            "\r\n" +
                            data + "\r\n" +
                            "--" + boundary + "--\r\n";

            req.Content.WriteBytes(Encoding.UTF8.GetBytes(body));
        }

        // Create decoder instance to test.
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        decoder.Destroy();
        Assert.True(req.Release());
    }

    // See https://github.com/netty/netty/issues/1848
    [Fact]
    public void TestNoZeroOut()
    {
        string boundary = "E832jQp_Rq2ErFmAduHSR8YlMSm0FCY";

        DefaultHttpDataFactory aMemFactory = new DefaultHttpDataFactory(false);

        DefaultHttpRequest aRequest = new DefaultHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST,
                "http://localhost");
        aRequest.Headers().Set(HttpHeaderNames.CONTENT_TYPE,
                "multipart/form-data; boundary=" + boundary);
        aRequest.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING,
                HttpHeaderValues.CHUNKED);

        HttpPostRequestDecoder aDecoder = new HttpPostRequestDecoder(aMemFactory, aRequest);

        string aData = "some data would be here. the data should be long enough that it " +
                "will be longer than the original buffer length of 256 bytes in " +
                "the HttpPostRequestDecoder in order to trigger the issue. Some more " +
                "data just to be on the safe side.";

        string body =
                "--" + boundary + "\r\n" +
                        "Content-Disposition: form-data; name=\"root\"\r\n" +
                        "Content-Type: text/plain\r\n" +
                        "\r\n" +
                        aData +
                        "\r\n" +
                        "--" + boundary + "--\r\n";

        byte[] aBytes = Encoding.Default.GetBytes(body);

        int split = 125;

        IByteBufAllocator aAlloc = new UnpooledByteBufAllocator(true);
        ByteBuf aSmallBuf = aAlloc.HeapBuffer(split, split);
        ByteBuf aLargeBuf = aAlloc.HeapBuffer(aBytes.Length - split, aBytes.Length - split);

        aSmallBuf.WriteBytes(aBytes.AsSpan(0, split));
        aLargeBuf.WriteBytes(aBytes.AsSpan(split, aBytes.Length - split));

        aDecoder.Offer(new DefaultHttpContent(aSmallBuf));
        aDecoder.Offer(new DefaultHttpContent(aLargeBuf));

        aDecoder.Offer(ILastHttpContent.EMPTY_LAST_CONTENT);

        Assert.True(aDecoder.HasNext(), "Should have a piece of data");

        IInterfaceHttpData aDecodedData = aDecoder.Next();
        Assert.Equal(IInterfaceHttpData.HttpDataType.Attribute, aDecodedData.GetHttpDataType());

        IAttribute aAttr = (IAttribute) aDecodedData;
        Assert.Equal(aData, aAttr.GetValue());

        aDecodedData.Release();
        aDecoder.Destroy();
        aSmallBuf.Release();
        aLargeBuf.Release();
    }

    // See https://github.com/netty/netty/issues/2305
    [Fact]
    public void TestChunkCorrect()
    {
        string payload = "town=794649819&town=784444184&town=794649672&town=794657800&town=" +
                "794655734&town=794649377&town=794652136&town=789936338&town=789948986&town=" +
                "789949643&town=786358677&town=794655880&town=786398977&town=789901165&town=" +
                "789913325&town=789903418&town=789903579&town=794645251&town=794694126&town=" +
                "794694831&town=794655274&town=789913656&town=794653956&town=794665634&town=" +
                "789936598&town=789904658&town=789899210&town=799696252&town=794657521&town=" +
                "789904837&town=789961286&town=789958704&town=789948839&town=789933899&town=" +
                "793060398&town=794659180&town=794659365&town=799724096&town=794696332&town=" +
                "789953438&town=786398499&town=794693372&town=789935439&town=794658041&town=" +
                "789917595&town=794655427&town=791930372&town=794652891&town=794656365&town=" +
                "789960339&town=794645586&town=794657688&town=794697211&town=789937427&town=" +
                "789902813&town=789941130&town=794696907&town=789904328&town=789955151&town=" +
                "789911570&town=794655074&town=789939531&town=789935242&town=789903835&town=" +
                "789953800&town=794649962&town=789939841&town=789934819&town=789959672&town=" +
                "794659043&town=794657035&town=794658938&town=794651746&town=794653732&town=" +
                "794653881&town=786397909&town=794695736&town=799724044&town=794695926&town=" +
                "789912270&town=794649030&town=794657946&town=794655370&town=794659660&town=" +
                "794694617&town=799149862&town=789953234&town=789900476&town=794654995&town=" +
                "794671126&town=789908868&town=794652942&town=789955605&town=789901934&town=" +
                "789950015&town=789937922&town=789962576&town=786360170&town=789954264&town=" +
                "789911738&town=789955416&town=799724187&town=789911879&town=794657462&town=" +
                "789912561&town=789913167&town=794655195&town=789938266&town=789952099&town=" +
                "794657160&town=789949414&town=794691293&town=794698153&town=789935636&town=" +
                "789956374&town=789934635&town=789935475&town=789935085&town=794651425&town=" +
                "794654936&town=794655680&town=789908669&town=794652031&town=789951298&town=" +
                "789938382&town=794651503&town=794653330&town=817675037&town=789951623&town=" +
                "789958999&town=789961555&town=794694050&town=794650241&town=794656286&town=" +
                "794692081&town=794660090&town=794665227&town=794665136&town=794669931";
        DefaultHttpRequest defaultHttpRequest =
                new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");

        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(defaultHttpRequest,
                140, HttpPostRequestDecoder.DEFAULT_MAX_BUFFERED_BYTES);

        int firstChunk = 10;
        int middleChunk = 1024;

        byte[] payload1 = Encoding.Default.GetBytes(payload.Substring(0, firstChunk));
        byte[] payload2 = Encoding.Default.GetBytes(payload.Substring(firstChunk, middleChunk));
        byte[] payload3 = Encoding.Default.GetBytes(payload.Substring(firstChunk + middleChunk, middleChunk));
        byte[] payload4 = Encoding.Default.GetBytes(payload.Substring(firstChunk + middleChunk * 2));

        ByteBuf buf1 = Unpooled.DirectBuffer(payload1.Length);
        ByteBuf buf2 = Unpooled.DirectBuffer(payload2.Length);
        ByteBuf buf3 = Unpooled.DirectBuffer(payload3.Length);
        ByteBuf buf4 = Unpooled.DirectBuffer(payload4.Length);

        buf1.WriteBytes(payload1);
        buf2.WriteBytes(payload2);
        buf3.WriteBytes(payload3);
        buf4.WriteBytes(payload4);

        decoder.Offer(new DefaultHttpContent(buf1));
        decoder.Offer(new DefaultHttpContent(buf2));
        decoder.Offer(new DefaultHttpContent(buf3));
        decoder.Offer(new DefaultLastHttpContent(buf4));

        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        Assert.Equal(139, decoder.GetBodyHttpDatas().Count);

        IAttribute attr = (IAttribute) decoder.GetBodyHttpData("town");
        Assert.Equal("794649819", attr.GetValue());

        decoder.Destroy();
        buf1.Release();
        buf2.Release();
        buf3.Release();
        buf4.Release();
    }

    // See https://github.com/netty/netty/issues/3326
    [Fact]
    public void TestFilenameContainingSemicolon()
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";
        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                "http://localhost");
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        string data = "asdf";
        string filename = "tmp;0.txt";
        string body =
                "--" + boundary + "\r\n" +
                        "Content-Disposition: form-data; name=\"file\"; filename=\"" + filename + "\"\r\n" +
                        "Content-Type: image/gif\r\n" +
                        "\r\n" +
                        data + "\r\n" +
                        "--" + boundary + "--\r\n";

        req.Content.WriteBytes(Encoding.UTF8.GetBytes(body));
        // Create decoder instance to test.
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        decoder.Destroy();
        Assert.True(req.Release());
    }

    [Fact]
    public void TestFilenameContainingSemicolon2()
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";
        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                "http://localhost");
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        string data = "asdf";
        string filename = "tmp;0.txt";
        string body =
                "--" + boundary + "\r\n" +
                        "Content-Disposition: form-data; name=\"file\"; filename=\"" + filename + "\"\r\n" +
                        "Content-Type: image/gif\r\n" +
                        "\r\n" +
                        data + "\r\n" +
                        "--" + boundary + "--\r\n";

        req.Content.WriteBytes(Encoding.UTF8.GetBytes(body));
        // Create decoder instance to test.
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        IInterfaceHttpData part1 = decoder.GetBodyHttpDatas()[0];
        Assert.IsAssignableFrom<IFileUpload>(part1);
        IFileUpload fileUpload = (IFileUpload) part1;
        Assert.Equal("tmp 0.txt", fileUpload.GetFilename());
        decoder.Destroy();
        Assert.True(req.Release());
    }

    [Fact]
    public void TestMultipartRequestWithoutContentTypeBody()
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";

        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                "http://localhost");

        req.SetDecoderResult(DecoderResult.SUCCESS);
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        req.Headers().Add(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);

        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);

        foreach (string data in new[] { "", "\r", "\r\r", "\r\r\r" })
        {
            string body =
                    "--" + boundary + "\r\n" +
                            "Content-Disposition: form-data; name=\"file\"; filename=\"tmp-0.txt\"\r\n" +
                            "\r\n" +
                            data + "\r\n" +
                            "--" + boundary + "--\r\n";

            req.Content.WriteBytes(Encoding.UTF8.GetBytes(body));
        }

        // Create decoder instance to test without any exception.
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.Single(decoder.GetBodyHttpDatas());

        while (decoder.HasNext())
        {
            IInterfaceHttpData data = decoder.Next();
            Assert.IsAssignableFrom<IFileUpload>(data);
            IFileUpload upload = (IFileUpload) data;
            Assert.Equal("tmp-0.txt", upload.GetFilename());
            Assert.Equal(HttpPostBodyUtil.DEFAULT_BINARY_CONTENT_TYPE, upload.GetContentType());
        }

        decoder.Destroy();
        Assert.True(req.Release());
    }

    [Fact]
    public void TestDecodeOtherMimeHeaderFields()
    {
        string boundary = "74e78d11b0214bdcbc2f86491eeb4902";
        string filecontent = "123456";

        string body = "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename=" + "\"" + "attached.txt" + "\"" +
                "\r\n" +
                "Content-Type: application/octet-stream" + "\r\n" +
                "Content-Encoding: gzip" + "\r\n" +
                "\r\n" +
                filecontent +
                "\r\n" +
                "--" + boundary + "--";

        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST,
                "http://localhost",
                Unpooled.WrappedBuffer(Encoding.Default.GetBytes(body)));
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        req.Headers().Add(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        IInterfaceHttpData part1 = decoder.GetBodyHttpDatas()[0];
        Assert.True(part1 is IFileUpload, "the item should be a FileUpload");
        IFileUpload fileUpload = (IFileUpload) part1;
        byte[] fileBytes = fileUpload.Get();
        Assert.True(filecontent.Equals(Encoding.Default.GetString(fileBytes)), "the filecontent should not be decoded");
        decoder.Destroy();
        Assert.True(req.Release());
    }

    [Fact]
    public void TestMultipartRequestWithFileInvalidCharset()
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";
        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                "http://localhost");
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        string data = "asdf";
        string filename = "tmp;0.txt";
        string body =
                "--" + boundary + "\r\n" +
                        "Content-Disposition: form-data; name=\"file\"; filename=\"" + filename + "\"\r\n" +
                        "Content-Type: image/gif; charset=ABCD\r\n" +
                        "\r\n" +
                        data + "\r\n" +
                        "--" + boundary + "--\r\n";

        req.Content.WriteBytes(Encoding.UTF8.GetBytes(body));

        // Create decoder instance to test.
        try
        {
            HttpPostRequestDecoder.ErrorDataDecoderException e = Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => new HttpPostRequestDecoder(inMemoryFactory, req));
            Assert.IsAssignableFrom<ArgumentException>(e.InnerException);
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void TestMultipartRequestWithFieldInvalidCharset()
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";
        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                "http://localhost");
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        string aData = "some data would be here. the data should be long enough that it " +
                "will be longer than the original buffer length of 256 bytes in " +
                "the HttpPostRequestDecoder in order to trigger the issue. Some more " +
                "data just to be on the safe side.";
        string body =
                "--" + boundary + "\r\n" +
                        "Content-Disposition: form-data; name=\"root\"\r\n" +
                        "Content-Type: text/plain; charset=ABCD\r\n" +
                        "\r\n" +
                        aData +
                        "\r\n" +
                        "--" + boundary + "--\r\n";

        req.Content.WriteBytes(Encoding.UTF8.GetBytes(body));

        // Create decoder instance to test.
        try
        {
            HttpPostRequestDecoder.ErrorDataDecoderException e = Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => new HttpPostRequestDecoder(inMemoryFactory, req));
            Assert.IsAssignableFrom<ArgumentException>(e.InnerException);
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void TestFormEncodeIncorrect()
    {
        ILastHttpContent content = new DefaultLastHttpContent(
                Unpooled.CopiedBuffer("project=netty&=netty&project=netty", Encoding.ASCII));
        DefaultHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(req);

        try
        {
            HttpPostRequestDecoder.ErrorDataDecoderException e = Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => decoder.Offer(content));
            Assert.IsAssignableFrom<ArgumentException>(e.InnerException);
        }
        finally
        {
            content.Release();
            decoder.Destroy();
        }
    }

    // https://github.com/netty/netty/pull/7265
    [Fact]
    public void TestDecodeContentDispositionFieldParameters()
    {

        string boundary = "74e78d11b0214bdcbc2f86491eeb4902";

        string encoding = "utf-8";
        string filename = "attached_файл.txt";
        string filenameEncoded = WebUtility.UrlEncode(filename);

        string body = "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename*=" + encoding + "''" + filenameEncoded +
                "\r\n\r\n" +
                "foo\r\n" +
                "\r\n" +
                "--" + boundary + "--";

        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST,
                "http://localhost",
                Unpooled.WrappedBuffer(Encoding.Default.GetBytes(body)));

        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        IInterfaceHttpData part1 = decoder.GetBodyHttpDatas()[0];
        Assert.True(part1 is IFileUpload, "the item should be a FileUpload");
        IFileUpload fileUpload = (IFileUpload) part1;
        Assert.Equal(filename, fileUpload.GetFilename(), "the filename should be decoded");
        decoder.Destroy();
        Assert.True(req.Release());
    }

    // https://github.com/netty/netty/pull/7265
    [Fact]
    public void TestDecodeWithLanguageContentDispositionFieldParameters()
    {

        string boundary = "74e78d11b0214bdcbc2f86491eeb4902";

        string encoding = "utf-8";
        string filename = "attached_файл.txt";
        string language = "anything";
        string filenameEncoded = WebUtility.UrlEncode(filename);

        string body = "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename*=" +
                encoding + "'" + language + "'" + filenameEncoded + "\r\n" +
                "\r\n" +
                "foo\r\n" +
                "\r\n" +
                "--" + boundary + "--";

        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST,
                "http://localhost",
                Unpooled.WrappedBuffer(Encoding.Default.GetBytes(body)));

        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        IInterfaceHttpData part1 = decoder.GetBodyHttpDatas()[0];
        Assert.True(part1 is IFileUpload, "the item should be a FileUpload");
        IFileUpload fileUpload = (IFileUpload) part1;
        Assert.Equal(filename, fileUpload.GetFilename(), "the filename should be decoded");
        decoder.Destroy();
        Assert.True(req.Release());
    }

    // https://github.com/netty/netty/pull/7265
    [Fact]
    public void TestDecodeMalformedNotEncodedContentDispositionFieldParameters()
    {

        string boundary = "74e78d11b0214bdcbc2f86491eeb4902";

        string body = "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename*=not-encoded\r\n" +
                "\r\n" +
                "foo\r\n" +
                "\r\n" +
                "--" + boundary + "--";

        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST,
                "http://localhost",
                Unpooled.WrappedBuffer(Encoding.Default.GetBytes(body)));

        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);

        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);

        try
        {
            HttpPostRequestDecoder.ErrorDataDecoderException e = Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => new HttpPostRequestDecoder(inMemoryFactory, req));
            Assert.IsAssignableFrom<IndexOutOfRangeException>(e.InnerException);
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    // https://github.com/netty/netty/pull/7265
    [Fact]
    public void TestDecodeMalformedBadCharsetContentDispositionFieldParameters()
    {

        string boundary = "74e78d11b0214bdcbc2f86491eeb4902";

        string body = "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename*=not-a-charset''filename\r\n" +
                "\r\n" +
                "foo\r\n" +
                "\r\n" +
                "--" + boundary + "--";

        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST,
                "http://localhost",
                Unpooled.WrappedBuffer(Encoding.Default.GetBytes(body)));

        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);

        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);

        try
        {
            HttpPostRequestDecoder.ErrorDataDecoderException e = Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => new HttpPostRequestDecoder(inMemoryFactory, req));
            Assert.IsAssignableFrom<ArgumentException>(e.InnerException);
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    // https://github.com/netty/netty/issues/7620
    [Theory]
    [InlineData("")]
    [InlineData("foo")]
    [InlineData("mal;form=\"e/d\"")]
    public void TestDecodeMalformedContentTypeFieldParameters(string contentType)
    {
        string boundary = "dLV9Wyq26L_-JQxk6ferf-RT153LhOO";
        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
                "http://localhost");
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        // Force to use memory-based data.
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        string data = "asdf";
        string filename = "tmp-0.txt";
        string body =
                "--" + boundary + "\r\n" +
                        "Content-Disposition: form-data; name=\"file\"; filename=\"" + filename + "\"\r\n" +
                        "Content-Type: " + contentType + "\r\n" +
                        "\r\n" +
                        data + "\r\n" +
                        "--" + boundary + "--\r\n";

        req.Content.WriteBytes(Encoding.UTF8.GetBytes(body));
        // Create decoder instance to test.
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        IInterfaceHttpData part1 = decoder.GetBodyHttpDatas()[0];
        Assert.IsAssignableFrom<IFileUpload>(part1);
        IFileUpload fileUpload = (IFileUpload) part1;
        Assert.Equal("tmp-0.txt", fileUpload.GetFilename());
        Assert.Equal(HttpPostBodyUtil.DEFAULT_BINARY_CONTENT_TYPE, fileUpload.GetContentType());
        decoder.Destroy();
        Assert.True(req.Release());
    }

    // https://github.com/netty/netty/issues/8575
    [Fact]
    public void TestMultipartRequest()
    {
        string BOUNDARY = "01f136d9282f";

        byte[] bodyBytes = Encoding.Default.GetBytes("--" + BOUNDARY + "\n" +
            "Content-Disposition: form-data; name=\"msg_id\"\n" +
            "\n" +
            "15200\n" +
            "--" + BOUNDARY + "\n" +
            "Content-Disposition: form-data; name=\"msg\"\n" +
            "\n" +
            "test message\n" +
            "--" + BOUNDARY + "--");
        ByteBuf byteBuf = Unpooled.DirectBuffer(bodyBytes.Length);
        byteBuf.WriteBytes(bodyBytes);

        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_0, HttpMethod.POST, "/up", byteBuf);
        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + BOUNDARY);

        HttpPostRequestDecoder decoder =
                new HttpPostRequestDecoder(new DefaultHttpDataFactory(DefaultHttpDataFactory.MINSIZE),
                        req,
                        Encoding.UTF8);

        Assert.True(decoder.IsMultipart());
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        Assert.Equal(2, decoder.GetBodyHttpDatas().Count);

        IAttribute attrMsg = (IAttribute) decoder.GetBodyHttpData("msg");
        Assert.Equal("test message", attrMsg.GetValue());
        IAttribute attrMsgId = (IAttribute) decoder.GetBodyHttpData("msg_id");
        Assert.Equal("15200", attrMsgId.GetValue());

        decoder.Destroy();
        Assert.True(req.Release());
    }

    [Fact]
    public void TestNotLeak()
    {
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/",
                Unpooled.CopiedBuffer("a=1&=2&b=3", Encoding.ASCII));

        try
        {
            Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => new HttpPostStandardRequestDecoder(request).Destroy());
        }
        finally
        {
            Assert.True(request.Release());
        }
    }

    [Fact]
    public void TestNotLeakDirectBufferWhenWrapIllegalArgumentException()
    {
        Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                () => TestNotLeakWhenWrapIllegalArgumentException(Unpooled.DirectBuffer()));
    }

    [Fact]
    public void TestNotLeakHeapBufferWhenWrapIllegalArgumentException()
    {
        Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                () => TestNotLeakWhenWrapIllegalArgumentException(Unpooled.Buffer()));
    }

    private static void TestNotLeakWhenWrapIllegalArgumentException(ByteBuf buf)
    {
        buf.WriteCharSequence("a=b&foo=%22bar%22&==", Encoding.ASCII);
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/", buf);

        try
        {
            new HttpPostStandardRequestDecoder(request).Destroy();
        }
        finally
        {
            Assert.True(request.Release());
        }
    }

    [Fact]
    public void TestMultipartFormDataContentType()
    {
        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        Assert.False(HttpPostRequestDecoder.IsMultipart(request));

        string multipartDataValue = HttpHeaderValues.MULTIPART_FORM_DATA + ";" + "boundary=gc0p4Jq0M2Yt08jU534c0p";
        request.Headers().Set(HttpHeaderNames.CONTENT_TYPE, ";" + multipartDataValue);
        Assert.False(HttpPostRequestDecoder.IsMultipart(request));

        request.Headers().Set(HttpHeaderNames.CONTENT_TYPE, multipartDataValue);
        Assert.True(HttpPostRequestDecoder.IsMultipart(request));
    }

    // see https://github.com/netty/netty/issues/10087
    [Fact]
    public void TestDecodeWithLanguageContentDispositionFieldParametersForFix()
    {

        string boundary = "952178786863262625034234";

        string encoding = "UTF-8";
        string filename = "测试test.txt";
        string filenameEncoded = WebUtility.UrlEncode(filename);

        string body = "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename*=\"" +
                encoding + "''" + filenameEncoded + "\"\r\n" +
                "\r\n" +
                "foo\r\n" +
                "\r\n" +
                "--" + boundary + "--";

        DefaultFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST,
                "http://localhost",
                Unpooled.WrappedBuffer(Encoding.Default.GetBytes(body)));

        req.Headers().Add(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);
        DefaultHttpDataFactory inMemoryFactory = new DefaultHttpDataFactory(false);
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(inMemoryFactory, req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        IInterfaceHttpData part1 = decoder.GetBodyHttpDatas()[0];
        Assert.True(part1 is IFileUpload, "the item should be a FileUpload");
        IFileUpload fileUpload = (IFileUpload) part1;
        Assert.Equal(filename, fileUpload.GetFilename(), "the filename should be decoded");

        decoder.Destroy();
        Assert.True(req.Release());
    }

    [Fact]
    public void TestDecodeFullHttpRequestWithUrlEncodedBody()
    {
        byte[] bodyBytes = Encoding.Default.GetBytes(
                "foo=bar&a=b&empty=&city=%3c%22new%22%20york%20city%3e&other_city=los+angeles");
        ByteBuf content = Unpooled.DirectBuffer(bodyBytes.Length);
        content.WriteBytes(bodyBytes);

        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/", content);
        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(req);
        Assert.NotEmpty(decoder.GetBodyHttpDatas());

        Assert.NotEmpty(decoder.GetBodyHttpDatas());
        Assert.Equal(5, decoder.GetBodyHttpDatas().Count);

        IAttribute attr = (IAttribute) decoder.GetBodyHttpData("foo");
        Assert.Equal("bar", attr.GetValue());

        attr = (IAttribute) decoder.GetBodyHttpData("a");
        Assert.Equal("b", attr.GetValue());

        attr = (IAttribute) decoder.GetBodyHttpData("empty");
        Assert.Equal("", attr.GetValue());

        attr = (IAttribute) decoder.GetBodyHttpData("city");
        Assert.Equal("<\"new\" york city>", attr.GetValue());

        attr = (IAttribute) decoder.GetBodyHttpData("other_city");
        Assert.Equal("los angeles", attr.GetValue());

        decoder.Destroy();
        Assert.True(req.Release());
    }

    [Fact]
    public void TestDecodeFullHttpRequestWithUrlEncodedBodyWithBrokenHexByte0()
    {
        byte[] bodyBytes = Encoding.Default.GetBytes("foo=bar&a=b&empty=%&city=paris");
        ByteBuf content = Unpooled.DirectBuffer(bodyBytes.Length);
        content.WriteBytes(bodyBytes);

        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/", content);

        try
        {
            HttpPostRequestDecoder.ErrorDataDecoderException e = Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => new HttpPostRequestDecoder(req));
            Assert.Equal("Invalid hex byte at index '0' in string: '%'", e.Message);
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void TestDecodeFullHttpRequestWithUrlEncodedBodyWithBrokenHexByte1()
    {
        byte[] bodyBytes = Encoding.Default.GetBytes("foo=bar&a=b&empty=%2&city=london");
        ByteBuf content = Unpooled.DirectBuffer(bodyBytes.Length);
        content.WriteBytes(bodyBytes);

        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/", content);

        try
        {
            HttpPostRequestDecoder.ErrorDataDecoderException e = Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => new HttpPostRequestDecoder(req));
            Assert.Equal("Invalid hex byte at index '0' in string: '%2'", e.Message);
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void TestDecodeFullHttpRequestWithUrlEncodedBodyWithInvalidHexNibbleHi()
    {
        byte[] bodyBytes = Encoding.Default.GetBytes("foo=bar&a=b&empty=%Zc&city=london");
        ByteBuf content = Unpooled.DirectBuffer(bodyBytes.Length);
        content.WriteBytes(bodyBytes);

        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/", content);

        try
        {
            HttpPostRequestDecoder.ErrorDataDecoderException e = Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => new HttpPostRequestDecoder(req));
            Assert.Equal("Invalid hex byte at index '0' in string: '%Zc'", e.Message);
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void TestDecodeFullHttpRequestWithUrlEncodedBodyWithInvalidHexNibbleLo()
    {
        byte[] bodyBytes = Encoding.Default.GetBytes("foo=bar&a=b&empty=%2g&city=london");
        ByteBuf content = Unpooled.DirectBuffer(bodyBytes.Length);
        content.WriteBytes(bodyBytes);

        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/", content);

        try
        {
            HttpPostRequestDecoder.ErrorDataDecoderException e = Assert.Throws<HttpPostRequestDecoder.ErrorDataDecoderException>(
                    () => new HttpPostRequestDecoder(req));
            Assert.Equal("Invalid hex byte at index '0' in string: '%2g'", e.Message);
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void TestDecodeMultipartRequest()
    {
        byte[] bodyBytes = Encoding.Default.GetBytes("--be38b42a9ad2713f\n" +
                "content-disposition: form-data; name=\"title\"\n" +
                "content-length: 10\n" +
                "content-type: text/plain; charset=UTF-8\n" +
                "\n" +
                "bar-stream\n" +
                "--be38b42a9ad2713f\n" +
                "content-disposition: form-data; name=\"data\"; filename=\"data.json\"\n" +
                "content-length: 16\n" +
                "content-type: application/json; charset=UTF-8\n" +
                "\n" +
                "{\"title\":\"Test\"}\n" +
                "--be38b42a9ad2713f--");
        ByteBuf content = Unpooled.DirectBuffer(bodyBytes.Length);
        content.WriteBytes(bodyBytes);
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/", content);
        req.Headers().Add("Content-Type", "multipart/form-data;boundary=be38b42a9ad2713f");

        try
        {
            HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(new DefaultHttpDataFactory(false), req);
            Assert.Equal(2, decoder.GetBodyHttpDatas().Count);
            IInterfaceHttpData data = decoder.GetBodyHttpData("title");
            Assert.IsAssignableFrom<MemoryAttribute>(data);
            Assert.Equal("bar-stream", ((MemoryAttribute) data).GetString());
            Assert.True(data.Release());
            data = decoder.GetBodyHttpData("data");
            Assert.IsAssignableFrom<MemoryFileUpload>(data);
            Assert.Equal("{\"title\":\"Test\"}", ((MemoryFileUpload) data).GetString());
            Assert.True(data.Release());
            decoder.Destroy();
        }
        catch (HttpPostRequestDecoder.ErrorDataDecoderException)
        {
            Assert.Fail("Was not expecting an exception");
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    /**
     * when diskFilename contain "\" create temp file error
     */
    [Fact]
    public void TestHttpPostStandardRequestDecoderToDiskNameContainingUnauthorizedChar()
    {
        StringBuilder sb = new StringBuilder();
        byte[] bodyBytes = Encoding.ASCII.GetBytes("aaaa/bbbb=aaaaaaaaaa" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaa" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaa" +
                "aaaaaaaaaaaaaaaaaaa");
        ByteBuf content = Unpooled.DirectBuffer(bodyBytes.Length);
        content.WriteBytes(bodyBytes);

        IFullHttpRequest req =
                new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1,
                HttpMethod.POST,
                "/",
                content);
        HttpPostStandardRequestDecoder decoder = null;

        try
        {
            decoder = new HttpPostStandardRequestDecoder(
                    new DefaultHttpDataFactory(true),
                    req
            );
            decoder.Destroy();
        }
        catch (HttpPostRequestDecoder.ErrorDataDecoderException)
        {
            if (null != decoder)
            {
                decoder.Destroy();
            }

            Assert.Fail("Was not expecting an exception");
        }
        finally
        {
            Assert.True(req.Release());
        }
    }

    [Fact]
    public void TestTooManyFormFieldsPostStandardDecoder()
    {
        IHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");

        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(req, 1024, -1);

        int num = 0;

        while (true)
        {
            try
            {
                decoder.Offer(new DefaultHttpContent(Unpooled.WrappedBuffer(Encoding.Default.GetBytes("foo=bar&"))));
            }
            catch (DecoderException e)
            {
                Assert.Equal(typeof(HttpPostRequestDecoder.TooManyFormFieldsException), e.GetType());
                break;
            }

            Assert.True(num++ < 1024);
        }

        Assert.Equal(1024, num);
        decoder.Destroy();
    }

    [Fact]
    public void TestTooManyFormFieldsPostMultipartDecoder()
    {
        IHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        req.Headers().Add("Content-Type", "multipart/form-data;boundary=be38b42a9ad2713f");

        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(req, 1024, -1);
        decoder.Offer(new DefaultHttpContent(Unpooled.WrappedBuffer(Encoding.Default.GetBytes("--be38b42a9ad2713f\n"))));

        int num = 0;

        while (true)
        {
            try
            {
                byte[] bodyBytes = Encoding.Default.GetBytes("content-disposition: form-data; name=\"title\"\n" +
                        "content-length: 10\n" +
                        "content-type: text/plain; charset=UTF-8\n" +
                        "\n" +
                        "bar-stream\n" +
                        "--be38b42a9ad2713f\n");
                ByteBuf content = Unpooled.WrappedBuffer(bodyBytes);
                decoder.Offer(new DefaultHttpContent(content));
            }
            catch (DecoderException e)
            {
                Assert.Equal(typeof(HttpPostRequestDecoder.TooManyFormFieldsException), e.GetType());
                break;
            }

            Assert.True(num++ < 1024);
        }

        Assert.Equal(1024, num);
        decoder.Destroy();
    }

    [Fact]
    public void TestTooLongFormFieldStandardDecoder()
    {
        IHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");

        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(req, -1, 16 * 1024);

        DecoderException e = Assert.ThrowsAny<DecoderException>(() =>
                decoder.Offer(new DefaultHttpContent(Unpooled.WrappedBuffer(new byte[16 * 1024 + 1]))));
        Assert.Equal(typeof(HttpPostRequestDecoder.TooLongFormFieldException), e.GetType());
        decoder.Destroy();
    }

    [Fact]
    public void TestFieldGreaterThanMaxBufferedBytesStandardDecoder()
    {
        IHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");

        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(req, -1, 6);

        decoder.Offer(new DefaultHttpContent(Unpooled.WrappedBuffer(Encoding.Default.GetBytes("foo=bar"))));
        decoder.Destroy();
    }

    [Fact]
    public void TestTooLongFormFieldMultipartDecoder()
    {
        IHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        req.Headers().Add("Content-Type", "multipart/form-data;boundary=be38b42a9ad2713f");

        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(req, -1, 16 * 1024);

        DecoderException e = Assert.ThrowsAny<DecoderException>(() =>
                decoder.Offer(new DefaultHttpContent(Unpooled.WrappedBuffer(new byte[16 * 1024 + 1]))));
        Assert.Equal(typeof(HttpPostRequestDecoder.TooLongFormFieldException), e.GetType());
        decoder.Destroy();
    }

    [Fact]
    public void TestFieldGreaterThanMaxBufferedBytesMultipartDecoder()
    {
        IHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        req.Headers().Add("Content-Type", "multipart/form-data;boundary=be38b42a9ad2713f");

        byte[] bodyBytes = Encoding.Default.GetBytes("content-disposition: form-data; name=\"title\"\n" +
                "content-length: 10\n" +
                "content-type: text/plain; charset=UTF-8\n" +
                "\n" +
                "bar-stream\n" +
                "--be38b42a9ad2713f\n");

        HttpPostRequestDecoder decoder = new HttpPostRequestDecoder(req, -1, bodyBytes.Length - 1);

        decoder.Offer(new DefaultHttpContent(Unpooled.WrappedBuffer(bodyBytes)));
        decoder.Destroy();
    }

    [Theory]
    [InlineData((byte) 0x00)]
    [InlineData(HttpConstants.CR)]
    [InlineData((byte) 0x19)]
    [InlineData(HttpConstants.DEL)]
    public void MultipartContentTypeMustBeSanitizedIfItContainsIllegalCharacters(byte illegal)
    {
        // NOTE: We cannot test with HttpConstants.LF because it's structural and
        // simply breaks the header line into two.
        string boundary = "861fbeab-cd20-470c-9609-d40a0f704466";
        string content = "--" + boundary + "\r\n" +
            "Content-Disposition: form-data; name=\"file\"; filename=\"x.txt\"\r\n" +
            "Content-Type: text/pl" + ((char) illegal) + "ain\r\n" +
            "\r\n" +
            "x\r\n" +
            "--" + boundary + "--\r\n";

        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
            Unpooled.CopiedBuffer(content, Encoding.Latin1));
        req.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "multipart/form-data; boundary=" + boundary);

        HttpPostMultipartRequestDecoder decoder = new HttpPostMultipartRequestDecoder(req);

        try
        {
            Assert.True(decoder.HasNext());
            IInterfaceHttpData data = decoder.Next();
            Assert.Equal(IInterfaceHttpData.HttpDataType.FileUpload, data.GetHttpDataType());
            IFileUpload upload = (IFileUpload) data;
            Assert.Equal(HttpPostBodyUtil.DEFAULT_BINARY_CONTENT_TYPE, upload.GetContentType());
        }
        finally
        {
            decoder.Destroy();
            Assert.True(req.Release());
        }
    }
}
