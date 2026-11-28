/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.Handler.Codec.Http2;
using static Netty.NET.Handler.Logging.LogLevel;

namespace Netty.NET.Example.Http2.HelloWorld.Server;

// CLR: the public build() override widens the protected AbstractHttp2ConnectionHandlerBuilder.build(); a C# override
// cannot change the accessibility of the virtual method, so it is a public member declared new that calls base, as
// codec-http2's Http2ConnectionHandlerBuilder. Http2ConnectionDecoder/Http2ConnectionEncoder take the I prefix and
// HelloWorldHttp2Handler.class is typeof(HelloWorldHttp2Handler), as testsuite-http2's HelloWorldHttp2HandlerBuilder.
public sealed class HelloWorldHttp2HandlerBuilder
        : AbstractHttp2ConnectionHandlerBuilder<HelloWorldHttp2Handler, HelloWorldHttp2HandlerBuilder>
{

    private static readonly Http2FrameLogger logger = new Http2FrameLogger(INFO, typeof(HelloWorldHttp2Handler));

    public HelloWorldHttp2HandlerBuilder()
    {
        FrameLogger(logger);
    }

    public new HelloWorldHttp2Handler Build()
    {
        return base.Build();
    }

    protected override HelloWorldHttp2Handler Build(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                           Http2Settings initialSettings)
    {
        HelloWorldHttp2Handler handler = new HelloWorldHttp2Handler(decoder, encoder, initialSettings);
        FrameListener(handler);
        return handler;
    }
}
