/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: it declares no test; the four theories of AbstractHttp3FrameTypeValidationHandlerTest run for it with both data
// rows, and the two inbound ones skip their cases (assumeTrue(isInbound) is Assert.SkipUnless), because the handler
// validates outbound frames only. Arrays.asList is a List initializer and Unpooled.EMPTY_BUFFER is
// Unpooled.EmptyBuffer.
public class Http3PushStreamServerValidationHandlerTest :
        AbstractHttp3FrameTypeValidationHandlerTest<IHttp3PushStreamFrame>
{

    public Http3PushStreamServerValidationHandlerTest() : base(QuicStreamType.UNIDIRECTIONAL, false, true)
    {
    }

    protected override IChannelHandler NewHandler(bool server)
    {
        return Http3PushStreamServerValidationHandler.INSTANCE;
    }

    protected override List<IHttp3PushStreamFrame> NewValidFrames()
    {
        return new List<IHttp3PushStreamFrame>
        {
            new DefaultHttp3HeadersFrame(), new DefaultHttp3DataFrame(Unpooled.EmptyBuffer)
        };
    }

    protected override List<IHttp3Frame> NewInvalidFrames()
    {
        return new List<IHttp3Frame>
        {
            Http3TestUtils.NewHttp3RequestStreamFrame(), Http3TestUtils.NewHttp3ControlStreamFrame()
        };
    }
}
