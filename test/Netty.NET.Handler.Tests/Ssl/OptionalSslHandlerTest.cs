/*
 * Copyright 2017 The Netty Project
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

using System.Collections.Generic;
using System.Net.Security;
using System.Text;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: Mockito is Moq (codec-base porting rules): the @Mock fields are Mock<T> fields created in their initializers, so
// MockitoAnnotations.initMocks has no counterpart and @BeforeEach setUp() runs from the constructor; when(...).thenReturn
// is Setup/Returns, verify(pipeline).remove/replace is Verify and verifyNoInteractions(pipeline) is VerifyNoOtherCalls on a
// mock that saw no verified call. Mockito.mock(SslHandler.class) is a Mock<SslHandler> built with server authentication
// options, as SslHandler has no constructor without its engine (src/Netty.NET.Handler/Ssl/SslHandler.cs). The tests call
// the protected decode(...) through Java package access, while C# protected access is limited to derived classes (as
// recorded at the InvokeDecode forwarder of src/Netty.NET.Handler.Codec.Mqtt/MqttDecoder.cs); OptionalSslHandler has no
// such forwarder, so the handlers of the tests are nested subclasses whose InvokeDecode forwards to the protected Decode,
// and the anonymous OptionalSslHandler subclasses derive from it. "plaintext".getBytes() uses the default charset,
// Encoding.Default (docs/codec-stomp-porting.md).
public class OptionalSslHandlerTest
{

    private const string SSL_HANDLER_NAME = "sslhandler";
    private const string HANDLER_NAME = "handler";

    private readonly Mock<IChannelHandlerContext> context = new Mock<IChannelHandlerContext>();

    private readonly Mock<SslContext> sslContext = new Mock<SslContext>();

    private readonly Mock<IChannelPipeline> pipeline = new Mock<IChannelPipeline>();

    public OptionalSslHandlerTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        context.Setup(c => c.Pipeline).Returns(pipeline.Object);
    }

    [Fact]
    public void HandlerRemoved()
    {
        DecodingOptionalSslHandler handler = new DecodingOptionalSslHandler(sslContext.Object);
        ByteBuf payload = Unpooled.CopiedBuffer(Encoding.Default.GetBytes("plaintext"));

        try
        {
            handler.InvokeDecode(context.Object, payload, null);
            pipeline.Verify(p => p.Remove(handler));
        }
        finally
        {
            payload.Release();
        }
    }

    [Fact]
    public void HandlerReplaced()
    {
        IChannelHandler nonSslHandler = new Mock<IChannelHandler>().Object;
        DecodingOptionalSslHandler handler = new NonSslReplacingHandler(sslContext.Object, nonSslHandler);
        ByteBuf payload = Unpooled.CopiedBuffer(Encoding.Default.GetBytes("plaintext"));

        try
        {
            handler.InvokeDecode(context.Object, payload, null);
            pipeline.Verify(p => p.Replace(handler, HANDLER_NAME, nonSslHandler));
        }
        finally
        {
            payload.Release();
        }
    }

    [Fact]
    public void SslHandlerReplaced()
    {
        SslHandler sslHandler = new Mock<SslHandler>(new SslServerAuthenticationOptions()).Object;
        DecodingOptionalSslHandler handler = new SslReplacingHandler(sslContext.Object, sslHandler);
        ByteBuf payload = Unpooled.WrappedBuffer(new byte[] { 22, 3, 1, 0, 5 });

        try
        {
            handler.InvokeDecode(context.Object, payload, null);
            pipeline.Verify(p => p.Replace(handler, SSL_HANDLER_NAME, sslHandler));
        }
        finally
        {
            payload.Release();
        }
    }

    [Fact]
    public void DecodeBuffered()
    {
        DecodingOptionalSslHandler handler = new DecodingOptionalSslHandler(sslContext.Object);
        ByteBuf payload = Unpooled.WrappedBuffer(new byte[] { 22, 3 });

        try
        {
            handler.InvokeDecode(context.Object, payload, null);
            pipeline.VerifyNoOtherCalls();
        }
        finally
        {
            payload.Release();
        }
    }

    // CLR: see the class comment.
    private class DecodingOptionalSslHandler : OptionalSslHandler
    {
        internal DecodingOptionalSslHandler(SslContext sslContext) : base(sslContext)
        {
        }

        internal void InvokeDecode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            Decode(ctx, @in, @out);
        }
    }

    private sealed class NonSslReplacingHandler : DecodingOptionalSslHandler
    {
        private readonly IChannelHandler nonSslHandler;

        internal NonSslReplacingHandler(SslContext sslContext, IChannelHandler nonSslHandler) : base(sslContext)
        {
            this.nonSslHandler = nonSslHandler;
        }

        protected override IChannelHandler NewNonSslHandler(IChannelHandlerContext context)
        {
            return nonSslHandler;
        }

        protected override string NewNonSslHandlerName()
        {
            return HANDLER_NAME;
        }
    }

    private sealed class SslReplacingHandler : DecodingOptionalSslHandler
    {
        private readonly SslHandler sslHandler;

        internal SslReplacingHandler(SslContext sslContext, SslHandler sslHandler) : base(sslContext)
        {
            this.sslHandler = sslHandler;
        }

        protected override SslHandler NewSslHandler(IChannelHandlerContext context, SslContext sslContext)
        {
            return sslHandler;
        }

        protected override string NewSslHandlerName()
        {
            return SSL_HANDLER_NAME;
        }
    }
}
