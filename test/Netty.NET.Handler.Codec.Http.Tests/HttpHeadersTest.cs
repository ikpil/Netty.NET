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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: the @Deprecated DefaultHttpHeaders(boolean validate) names DefaultHttpHeaders() and is not ported (owner
// decision 2026-11-16): new DefaultHttpHeaders(true) is new DefaultHttpHeaders(), and new DefaultHttpHeaders(false),
// which uses the factory's validators with validation disabled, is
// DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders() (rule 7). NullPointerException from
// checkNotNull is ArgumentNullException and IllegalArgumentException is ArgumentException (docs/codec-base-porting.md);
// the anonymous Executables are lambdas. AsciiString.contentEqualsIgnoreCase takes the Strings as StringCharSequence,
// because a C# string is not an ICharSequence.
public class HttpHeadersTest
{

    [Fact]
    public void TestRemoveTransferEncodingIgnoreCase()
    {
        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, "Chunked");
        Assert.False(message.Headers().IsEmpty());
        HttpUtil.SetTransferEncodingChunked(message, false);
        Assert.True(message.Headers().IsEmpty());
    }

    // Test for https://github.com/netty/netty/issues/1690
    [Fact]
    public void TestGetOperations()
    {
        HttpHeaders headers = new DefaultHttpHeaders();
        headers.Add(Of("Foo"), Of("1"));
        headers.Add(Of("Foo"), Of("2"));

        Assert.Equal("1", headers.Get(Of("Foo")));

        IList<string> values = headers.GetAll(Of("Foo"));
        Assert.Equal(2, values.Count);
        Assert.Equal("1", values[0]);
        Assert.Equal("2", values[1]);
    }

    [Fact]
    public void TestEqualsIgnoreCase()
    {
        Assert.True(AsciiString.ContentEqualsIgnoreCase(null, null));
        Assert.False(AsciiString.ContentEqualsIgnoreCase(null, new StringCharSequence("foo")));
        Assert.False(AsciiString.ContentEqualsIgnoreCase(new StringCharSequence("bar"), null));
        Assert.True(AsciiString.ContentEqualsIgnoreCase(new StringCharSequence("FoO"), new StringCharSequence("fOo")));
    }

    [Fact]
    public void TestSetNullHeaderValueValidate()
    {
        HttpHeaders headers = new DefaultHttpHeaders();
        Assert.Throws<ArgumentNullException>(() => headers.Set(Of("test"), (ICharSequence) null));
    }

    [Fact]
    public void TestSetNullHeaderValueNotValidate()
    {
        HttpHeaders headers = DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders();
        Assert.Throws<ArgumentNullException>(() => headers.Set(Of("test"), (ICharSequence) null));
    }

    [Fact]
    public void TestAddSelf()
    {
        HttpHeaders headers = DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders();
        Assert.Throws<ArgumentException>(() => headers.Add(headers));
    }

    [Fact]
    public void TestSetSelfIsNoOp()
    {
        HttpHeaders headers = DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders();
        headers.Add("name", "value");
        headers.Set(headers);
        Assert.Equal(1, headers.Size());
    }
}
