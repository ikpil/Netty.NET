/*
 * Copyright 2024 The Netty Project
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

namespace Netty.NET.Handler.Codec.Spdy;

/** A SPDY Control frame. */
// CLR: the Java interface takes the I prefix (owner rule). ByteBufHolder is IByteBufHolder; copy(), duplicate(),
// retainedDuplicate(), replace(ByteBuf), retain(), retain(int), touch() and touch(Object) are re-declared only to
// narrow the return type, so they are inherited from IByteBufHolder; DefaultSpdyUnknownFrame narrows them with
// covariant override returns (owner decision 2026-11-19; docs/transport-porting.md, Decisions). The Java byte flags is
// the CLR byte (the wire octet).
public interface ISpdyUnknownFrame : ISpdyFrame, IByteBufHolder
{
    int FrameType();

    byte Flags();
}
