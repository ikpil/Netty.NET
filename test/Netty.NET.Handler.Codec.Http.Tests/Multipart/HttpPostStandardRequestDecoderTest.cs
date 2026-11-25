/*
 * Copyright 2022 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.Multipart;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

// CLR: HttpRequest/LastHttpContent are IHttpRequest/ILastHttpContent, CharsetUtil.UTF_8 is Encoding.UTF8 and
// String.getBytes(UTF_8) Encoding.UTF8.GetBytes; the static import headersFactory() is
// DefaultHttpHeadersFactory.HeadersFactory(). assertEquals(n, list.size()) is Assert.Equal(n, list.Count), and
// Assert.Single/Assert.Empty for 1/0, the forms the xUnit analyzer requires for those sizes (xUnit2013,
// https://xunit.net/xunit.analyzers/rules/xUnit2013).
// The test methods with package access (no modifier in JUnit 5) are public, as xUnit requires.
public class HttpPostStandardRequestDecoderTest
{

    [Fact]
    public void TestDecodeAttributes()
    {
        string requestBody = "key1=value1&key2=value2";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Equal(2, decoder.GetBodyHttpDatas().Count);
        AssertMemoryAttribute(decoder.GetBodyHttpData("key1"), "value1");
        AssertMemoryAttribute(decoder.GetBodyHttpData("key2"), "value2");
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeSingleAttributeWithNoValue()
    {
        string requestBody = "key1";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                DefaultHttpHeadersFactory.HeadersFactory().NewHeaders().Add("Content-Type", "application/x-www-form-urlencoded"));

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Single(decoder.GetBodyHttpDatas());
        AssertMemoryAttribute(decoder.GetBodyHttpData("key1"), "");
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeSingleAttributeWithNoValueEmptyLast()
    {
        string requestBody = "key1";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                DefaultHttpHeadersFactory.HeadersFactory().NewHeaders().Add("Content-Type", "application/x-www-form-urlencoded"));

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultHttpContent(buf);
        decoder.Offer(httpContent);

        decoder.Offer(ILastHttpContent.EMPTY_LAST_CONTENT);

        Assert.Single(decoder.GetBodyHttpDatas());
        AssertMemoryAttribute(decoder.GetBodyHttpData("key1"), "");
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeEndAttributeWithNoValue()
    {
        string requestBody = "key1=value1&key2";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                DefaultHttpHeadersFactory.HeadersFactory().NewHeaders().Add("Content-Type", "application/x-www-form-urlencoded"));

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Equal(2, decoder.GetBodyHttpDatas().Count);
        AssertMemoryAttribute(decoder.GetBodyHttpData("key1"), "value1");
        AssertMemoryAttribute(decoder.GetBodyHttpData("key2"), "");
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeJsonAttributeAsEmpty()
    {
        string requestBody = "{\"iAm\": \" a JSON!\"}";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                DefaultHttpHeadersFactory.HeadersFactory().NewHeaders().Add("Content-Type", "application/json"));

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Empty(decoder.GetBodyHttpDatas());
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeJsonAttributeAsEmptyAndNoHeaders()
    {
        string requestBody = "{\"iAm\": \" a JSON!\"}";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Empty(decoder.GetBodyHttpDatas());
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeStartAttributeWithNoValue()
    {
        string requestBody = "key1&key2=value2";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Equal(2, decoder.GetBodyHttpDatas().Count);
        AssertMemoryAttribute(decoder.GetBodyHttpData("key1"), "");
        AssertMemoryAttribute(decoder.GetBodyHttpData("key2"), "value2");
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeMultipleAttributesWithNoValue()
    {
        string requestBody = "key1&key2&key3";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload",
                DefaultHttpHeadersFactory.HeadersFactory().NewHeaders().Add("Content-Type", "application/x-www-form-urlencoded"));

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Equal(3, decoder.GetBodyHttpDatas().Count);
        AssertMemoryAttribute(decoder.GetBodyHttpData("key1"), "");
        AssertMemoryAttribute(decoder.GetBodyHttpData("key2"), "");
        AssertMemoryAttribute(decoder.GetBodyHttpData("key3"), "");
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeNestedAttributeWithNoValue()
    {
        string requestBody = "key1=value1&key2&key3=value3";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Equal(3, decoder.GetBodyHttpDatas().Count);
        AssertMemoryAttribute(decoder.GetBodyHttpData("key1"), "value1");
        AssertMemoryAttribute(decoder.GetBodyHttpData("key2"), "");
        AssertMemoryAttribute(decoder.GetBodyHttpData("key3"), "value3");
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeAttributesWithAmpersandPrefixSkipsNullAttribute()
    {
        string requestBody = "&key1=value1";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Single(decoder.GetBodyHttpDatas());
        AssertMemoryAttribute(decoder.GetBodyHttpData("key1"), "value1");
        decoder.Destroy();
    }

    [Fact]
    public void TestDecodeZeroAttributesWithAmpersandPrefix()
    {
        string requestBody = "&";

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/upload");

        HttpPostStandardRequestDecoder decoder = new HttpPostStandardRequestDecoder(HttpDiskDataFactory(), request);
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(requestBody));
        DefaultHttpContent httpContent = new DefaultLastHttpContent(buf);
        decoder.Offer(httpContent);

        Assert.Empty(decoder.GetBodyHttpDatas());
        decoder.Destroy();
    }

    private static DefaultHttpDataFactory HttpDiskDataFactory()
    {
        return new DefaultHttpDataFactory(false);
    }

    private static void AssertMemoryAttribute(IInterfaceHttpData data, string expectedValue)
    {
        Assert.Equal(IInterfaceHttpData.HttpDataType.Attribute, data.GetHttpDataType());
        Assert.Equal(((MemoryAttribute) data).GetValue(), expectedValue);
    }

}
