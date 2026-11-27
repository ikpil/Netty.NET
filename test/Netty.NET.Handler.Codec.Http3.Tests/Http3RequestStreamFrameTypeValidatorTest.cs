/*
 * Copyright 2021 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the inherited tests of Http3FrameTypeValidatorTest run for this class, as codec-http2's derived fixtures of
// AbstractDecoratingHttp2ConnectionDecoderTest. newValidator() returns the internal IHttp3FrameTypeValidator, so the
// override is private protected (see Http3FrameTypeValidatorTest); the internal Http3CodecUtils constants and the
// validator's INSTANCE field are reached through InternalsVisibleTo. The int frame-type constants widen to long in the
// long[] initializers, as in Java.
public class Http3RequestStreamFrameTypeValidatorTest : Http3FrameTypeValidatorTest
{

    protected override long[] InvalidFramesTypes()
    {
        return new long[] {
                Http3CodecUtils.HTTP3_CANCEL_PUSH_FRAME_TYPE,
                Http3CodecUtils.HTTP3_GO_AWAY_FRAME_TYPE,
                Http3CodecUtils.HTTP3_MAX_PUSH_ID_FRAME_TYPE,
                Http3CodecUtils.HTTP3_SETTINGS_FRAME_TYPE
        };
    }

    protected override long[] ValidFrameTypes()
    {
        return new long[] {
                Http3CodecUtils.HTTP3_DATA_FRAME_TYPE,
                Http3CodecUtils.HTTP3_HEADERS_FRAME_TYPE,
                Http3CodecUtils.HTTP3_PUSH_PROMISE_FRAME_TYPE
        };
    }

    private protected override IHttp3FrameTypeValidator NewValidator()
    {
        return Http3RequestStreamFrameTypeValidator.INSTANCE;
    }
}
