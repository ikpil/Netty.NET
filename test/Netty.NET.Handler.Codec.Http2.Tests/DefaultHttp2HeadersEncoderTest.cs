/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using Netty.NET.Buffer;
using Netty.NET.Common;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link DefaultHttp2HeadersEncoder}.
 */
// CLR: @BeforeEach/@AfterEach are the constructor and Dispose() ("xUnit creates a new instance of the test class
// for every test that is run", docs/codec-http-porting.md, Shared CLR adaptations), which call the Java-named
// methods. The anonymous Executable is a lambda and assertThrows(StreamException.class) is
// Assert.ThrowsAny<StreamException> (docs/codec-http-porting.md, Core test notes). The package-private
// DefaultHttp2HeadersEncoder(SensitivityDetector, HpackEncoder) constructor is internal and reached through
// InternalsVisibleTo.
public class DefaultHttp2HeadersEncoderTest : IDisposable
{

    private DefaultHttp2HeadersEncoder encoder;

    public DefaultHttp2HeadersEncoderTest()
    {
        Setup();
    }

    private void Setup()
    {
        encoder = new DefaultHttp2HeadersEncoder(IHttp2HeadersEncoder.NEVER_SENSITIVE, NewTestEncoder());
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        encoder.Close();
    }

    [Fact]
    public void EncodeShouldSucceed()
    {
        IHttp2Headers headers = Headers();
        ByteBuf buf = Unpooled.Buffer();

        try
        {
            encoder.EncodeHeaders(3 /* randomly chosen */, headers, buf);
            Assert.True(buf.WriterIndex > 0);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void HeadersExceedMaxSetSizeShouldFail()
    {
        IHttp2Headers headers = Headers();
        encoder.MaxHeaderListSize(2);
        Assert.ThrowsAny<StreamException>(() =>
        {
            ByteBuf buf = Unpooled.Buffer();

            try
            {
                encoder.EncodeHeaders(3 /* randomly chosen */, headers, buf);
            }
            finally
            {
                buf.Release();
            }
        });
    }

    private static IHttp2Headers Headers()
    {
        return new DefaultHttp2Headers().Method(new AsciiString("GET")).Add(new AsciiString("a"), new AsciiString("1"))
                .Add(new AsciiString("a"), new AsciiString("2"));
    }
}
