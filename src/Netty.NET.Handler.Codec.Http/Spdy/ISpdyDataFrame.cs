/*
 * Copyright 2013 The Netty Project
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

/**
 * A SPDY Protocol DATA Frame
 */
// CLR: the Java interface takes the I prefix (owner rule). ByteBufHolder is IByteBufHolder, whose content() is the
// read-only Content property. setStreamId, setLast, copy(), duplicate(), retainedDuplicate(), replace(ByteBuf),
// retain(), retain(int), touch() and touch(Object) are re-declared only to narrow the return type, and content()
// only to document it, so they are inherited from ISpdyStreamFrame and IByteBufHolder, not repeated;
// DefaultSpdyDataFrame narrows them.
public interface ISpdyDataFrame : IByteBufHolder, ISpdyStreamFrame
{

    /**
     * Returns the data payload of this frame.  If there is no data payload
     * {@link Unpooled#EMPTY_BUFFER} is returned.
     *
     * The data payload cannot exceed 16777215 bytes.
     */
    // CLR: the inherited IByteBufHolder.Content property (see the type comment).
}
