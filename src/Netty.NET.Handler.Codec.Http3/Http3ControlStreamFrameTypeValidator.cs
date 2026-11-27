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

using System.Globalization;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Validate that the frame type is valid for a control stream.
 */
// CLR: the package-private final class is an internal sealed class implementing IHttp3FrameTypeValidator; INSTANCE is an
// internal static readonly field. throws Http3Exception has no counterpart (C# has no checked exceptions). The type
// prints with the invariant culture.
internal sealed class Http3ControlStreamFrameTypeValidator : IHttp3FrameTypeValidator
{

    internal static readonly Http3ControlStreamFrameTypeValidator INSTANCE = new Http3ControlStreamFrameTypeValidator();

    private Http3ControlStreamFrameTypeValidator()
    {
    }

    public void Validate(long type, bool first)
    {
        switch ((int) type)
        {
            case Http3CodecUtils.HTTP3_PUSH_PROMISE_FRAME_TYPE:
            case Http3CodecUtils.HTTP3_HEADERS_FRAME_TYPE:
            case Http3CodecUtils.HTTP3_DATA_FRAME_TYPE:
                if (first)
                {
                    throw new Http3Exception(Http3ErrorCode.H3_MISSING_SETTINGS,
                            "Missing settings frame.");
                }
                throw new Http3Exception(Http3ErrorCode.H3_FRAME_UNEXPECTED,
                        "Unexpected frame type '" + type.ToString(CultureInfo.InvariantCulture) + "' received");
            default:
               break;
        }
    }
}
