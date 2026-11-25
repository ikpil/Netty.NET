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

using System;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http2.Http2Error;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: new RuntimeException().getMessage() is null in Java; the CLR Exception.Message is never null (an exception
// created without a message reports a default text, docs/codec-http-porting.md: "never null in .NET"), so the two
// tests of a null message pass the null that Java's getMessage() returns. The package-private class is public, as
// xUnit requires.
public class Http2ExceptionTest
{

    [Fact]
    public void ConnectionErrorHandlesMessage()
    {
        DecoderException e = new TooLongHttpLineException("An HTTP line is larger than 1024 bytes.");
        Http2Exception http2Exception = Http2Exception.ConnectionError(COMPRESSION_ERROR, e, e.Message);
        Assert.Same(COMPRESSION_ERROR, http2Exception.Error());
        Assert.Equal("An HTTP line is larger than 1024 bytes.", http2Exception.Message);
    }

    [Fact]
    public void ConnectionErrorHandlesNullExceptionMessage()
    {
        Exception e = new Exception();
        Http2Exception http2Exception = Http2Exception.ConnectionError(COMPRESSION_ERROR, e, null);
        Assert.Same(COMPRESSION_ERROR, http2Exception.Error());
        Assert.Equal("Unexpected error", http2Exception.Message);
    }

    [Fact]
    public void ConnectionErrorHandlesMultipleMessages()
    {
        Exception e = new Exception();
        Http2Exception http2Exception = Http2Exception.ConnectionError(COMPRESSION_ERROR, e, null, "a", "b");
        Assert.Same(COMPRESSION_ERROR, http2Exception.Error());
        Assert.Equal("Unexpected error: [a, b]", http2Exception.Message);
    }
}
