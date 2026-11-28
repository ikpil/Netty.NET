/*
 * Copyright 2026 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: The assertion with a message uses XunitAssertMessages.AssertM.
public class HttpMethodTest
{

    private static readonly string NUL = ((char) 0x00).ToString();

    [Fact]
    public void ValueOfReturnsCachedInstanceForKnownMethods()
    {
        Assert.Same(HttpMethod.GET, HttpMethod.ValueOf("GET"));
        Assert.Same(HttpMethod.POST, HttpMethod.ValueOf("POST"));
        Assert.Same(HttpMethod.QUERY, HttpMethod.ValueOf("QUERY"));
    }

    [Fact]
    public void ConstructorAcceptsCustomMethodName()
    {
        HttpMethod custom = new HttpMethod("CUSTOM");
        Assert.Equal("CUSTOM", custom.Name());
    }

    [Fact]
    public void ConstructorRejectsLeadingAndTrailingSpaces()
    {
        // SP is not a valid HTTP token character; the name must already be a clean token.
        Assert.Throws<ArgumentException>(NewInstance("  GET  "));
    }

    [Fact]
    public void ConstructorRejectsLeadingAndTrailingTabs()
    {
        // HT is not a valid HTTP token character; the name must already be a clean token.
        Assert.Throws<ArgumentException>(NewInstance("\tGET\t"));
    }

    [Fact]
    public void ConstructorRejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(NewInstance(""));
    }

    [Fact]
    public void ConstructorRejectsLeadingNul()
    {
        Assert.Throws<ArgumentException>(NewInstance(NUL + "GET"));
    }

    [Fact]
    public void ConstructorRejectsTrailingNul()
    {
        Assert.Throws<ArgumentException>(NewInstance("GET" + NUL));
    }

    [Fact]
    public void ConstructorRejectsLeadingAndTrailingNul()
    {
        Assert.Throws<ArgumentException>(NewInstance(NUL + "GET" + NUL));
    }

    [Fact]
    public void ConstructorRejectsEmbeddedNul()
    {
        Assert.Throws<ArgumentException>(NewInstance("GE" + NUL + "T"));
    }

    [Fact]
    public void ConstructorRejectsCarriageReturn()
    {
        Assert.Throws<ArgumentException>(NewInstance("GET\r"));
    }

    [Fact]
    public void ConstructorRejectsLineFeed()
    {
        Assert.Throws<ArgumentException>(NewInstance("GET\n"));
    }

    [Fact]
    public void ConstructorRejectsVerticalTab()
    {
        Assert.Throws<ArgumentException>(NewInstance("GET" + (char) 0x0B));
    }

    [Fact]
    public void ConstructorRejectsFormFeed()
    {
        Assert.Throws<ArgumentException>(NewInstance("GET\f"));
    }

    [Fact]
    public void ConstructorRejectsEmbeddedSpace()
    {
        Assert.Throws<ArgumentException>(NewInstance("GE T"));
    }

    [Fact]
    public void ConstructorRejectsEmptyString()
    {
        Assert.Throws<ArgumentException>(NewInstance(""));
    }

    [Fact]
    public void ConstructorRejectsBlankString()
    {
        Assert.Throws<ArgumentException>(NewInstance("   "));
    }

    [Fact]
    public void RequestDecoderRejectsNulPaddedMethod()
    {
        // RFC 9112 forbids any non-token character in the method. NUL-padded methods are
        // a known request-smuggling vector if silently stripped, so the decoder must
        // surface a decoder failure rather than producing a valid GET message.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpRequestDecoder());

        try
        {
            byte[] data = Encoding.ASCII.GetBytes(NUL + "GET" + NUL + " / HTTP/1.1\r\nHost: x\r\n\r\n");
            ch.WriteInbound(Unpooled.WrappedBuffer(data));
            IHttpRequest req = ch.ReadInbound<IHttpRequest>();

            try
            {
                Assert.NotNull(req);
                Assert.False(req.DecoderResult().IsSuccess(),
                        "decoder must reject method names containing NUL bytes");
            }
            finally
            {
                ReferenceCountUtil.Release(req);
            }
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void RequestDecoderAcceptsCleanMethod()
    {
        // Regression: ordinary GET requests must still parse normally.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpRequestDecoder());

        try
        {
            byte[] data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n");
            ch.WriteInbound(Unpooled.WrappedBuffer(data));
            IHttpRequest req = ch.ReadInbound<IHttpRequest>();

            try
            {
                Assert.NotNull(req);
                Assert.Equal(HttpMethod.GET, req.Method());
            }
            finally
            {
                ReferenceCountUtil.Release(req);
            }
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    private static Action NewInstance(string name)
    {
        return () => new HttpMethod(name);
    }
}
