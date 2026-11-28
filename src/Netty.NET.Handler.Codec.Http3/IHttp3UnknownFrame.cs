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

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * <a href="https://tools.ietf.org/html/draft-ietf-quic-http-32#section-7.2.8">Unknown HTTP3 frame</a>.
 * These frames are valid on all stream types.
 * <pre>
 *    HTTP/3 Frame Format {
 *      Type (i),
 *      Length (i),
 *      Frame Payload (..),
 *    }
 * </pre>
 */
// CLR: the interface takes the I prefix. ByteBufHolder is IByteBufHolder, whose content() is the read-only Content
// property. copy(), duplicate(), retainedDuplicate(), replace(ByteBuf), retain(), retain(int), touch() and
// touch(Object) are re-declared only to narrow the return type, so they are inherited from IByteBufHolder, not
// repeated; an implementation narrows them with covariant overrides, as codec-http2's IHttp2UnknownFrame. The
// default method length() is a default interface member.
public interface IHttp3UnknownFrame :
        IHttp3RequestStreamFrame, IHttp3PushStreamFrame, IHttp3ControlStreamFrame, IByteBufHolder
{

    /**
     * Return the payload length of the frame.
     *
     * @return the length.
     */
    long Length()
    {
        return Content.ReadableBytes;
    }
}
