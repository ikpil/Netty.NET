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
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the package-private abstract JUnit fixture is public abstract, because its derived test class
// Http3RequestStreamValidationHandlerTest is public (xUnit runs public test classes) and a public class may not derive
// from a less accessible one (CS0060); its package-private constructor is protected. Http3RequestStreamFrame.class is
// typeof(IHttp3RequestStreamFrame) for the internal Http3FrameTypeDuplexValidationHandler<T> (the validation handlers
// keep the Class argument as a Type, docs/codec-http3-porting.md). Collections.singletonList and Arrays.asList are List
// initializers. It declares no test; the four inherited theories run for each derived class.
public abstract class Http3FrameTypeValidationHandlerTest :
        AbstractHttp3FrameTypeValidationHandlerTest<IHttp3RequestStreamFrame>
{

    protected Http3FrameTypeValidationHandlerTest(bool isInbound, bool isOutbound)
        : base(QuicStreamType.BIDIRECTIONAL, isInbound, isOutbound)
    {
    }

    protected override IChannelHandler NewHandler(bool server)
    {
        return new Http3FrameTypeDuplexValidationHandler<IHttp3RequestStreamFrame>(typeof(IHttp3RequestStreamFrame));
    }

    protected override List<IHttp3RequestStreamFrame> NewValidFrames()
    {
        return new List<IHttp3RequestStreamFrame> { Http3TestUtils.NewHttp3RequestStreamFrame() };
    }

    protected override List<IHttp3Frame> NewInvalidFrames()
    {
        return new List<IHttp3Frame>
        {
            Http3TestUtils.NewHttp3ControlStreamFrame(), Http3TestUtils.NewHttp3PushStreamFrame()
        };
    }
}
