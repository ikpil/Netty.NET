/*
 * Copyright 2018 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: equals/hashCode are Equals/GetHashCode; assertNotEquals/assertEquals of the messages are Assert.NotEqual/Equal
// over object, which call Equals (as the JUnit assertions do).
public class DefaultHttpResponseTest
{

    [Fact]
    public void TestNotEquals()
    {
        IHttpResponse ok = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        IHttpResponse notFound = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.NOT_FOUND);
        Assert.NotEqual<object>(ok, notFound);
        Assert.NotEqual(ok.GetHashCode(), notFound.GetHashCode());
    }

    [Fact]
    public void TestEquals()
    {
        IHttpResponse ok = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        IHttpResponse ok2 = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        Assert.Equal<object>(ok, ok2);
        Assert.Equal(ok.GetHashCode(), ok2.GetHashCode());
    }
}
