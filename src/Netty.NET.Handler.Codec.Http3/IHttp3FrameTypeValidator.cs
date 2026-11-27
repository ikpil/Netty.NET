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

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the package-private @FunctionalInterface is an internal interface with the I prefix (docs/codec-http2-porting.md,
// Shared CLR adaptations). The interface constant NO_VALIDATION is a static readonly interface field and its lambda is
// the private nested class NoValidation, as codec-http2's IHttp2HeadersEncoder.NEVER_SENSITIVE. throws Http3Exception
// has no counterpart (C# has no checked exceptions).
internal interface IHttp3FrameTypeValidator
{

    static readonly IHttp3FrameTypeValidator NO_VALIDATION = new NoValidation();

    void Validate(long type, bool first);

    // CLR: the lambda (type, first) -> { } of NO_VALIDATION.
    private sealed class NoValidation : IHttp3FrameTypeValidator
    {
        public void Validate(long type, bool first)
        {
        }
    }
}
