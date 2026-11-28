/*
 * Copyright 2016 The Netty Project
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

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * HTTP/2 DATA frame.
 */
// CLR: ByteBufHolder is IByteBufHolder, whose content() is the read-only Content property. copy(), duplicate(),
// retainedDuplicate(), replace(ByteBuf), retain(), retain(int), touch() and touch(Object) are re-declared only to
// narrow the return type, so they are inherited from IByteBufHolder, not repeated; the classes narrow them with
// covariant overrides.
public interface IHttp2DataFrame : IHttp2StreamFrame, IByteBufHolder
{

    /**
     * Frame padding to use. Will be non-negative and less than 256.
     */
    int Padding();

    /**
     * Payload of DATA frame. Will not be {@code null}.
     */

    /**
     * Returns the number of bytes that are flow-controlled initially, so even if the {@link #content()} is consumed
     * this will not change.
     */
    int InitialFlowControlledBytes();

    /**
     * Returns {@code true} if the END_STREAM flag is set.
     */
    bool IsEndStream();
}
