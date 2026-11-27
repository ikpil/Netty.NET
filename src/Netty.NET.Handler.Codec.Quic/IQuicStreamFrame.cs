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
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * A QUIC STREAM_FRAME.
 */
// CLR: ByteBufHolder is IByteBufHolder, whose content() is the read-only Content property and refCnt() the RefCnt
// property (docs/buffer-porting.md, Buffer holders and conversion). copy(), duplicate(), retainedDuplicate(),
// replace(ByteBuf), retain(), retain(int), touch() and touch(Object) are re-declared only to narrow the return type, so
// they are inherited from IByteBufHolder, not repeated; the classes narrow them (owner decision 2026-11-19, as
// IHttp2DataFrame). The anonymous class of EMPTY_FIN is the private nested EmptyFinQuicStreamFrame, whose narrowed
// members implement the IByteBufHolder/IReferenceCounted members explicitly (as DefaultHttp2DataFrame does).
public interface IQuicStreamFrame : IByteBufHolder
{

    /**
     * An empty {@link QuicStreamFrame} that has the {@code FIN} flag set.
     */
    public static readonly IQuicStreamFrame EMPTY_FIN = new EmptyFinQuicStreamFrame();

    private sealed class EmptyFinQuicStreamFrame : IQuicStreamFrame
    {
        public bool HasFin()
        {
            return true;
        }

        public IQuicStreamFrame Copy()
        {
            return this;
        }

        public IQuicStreamFrame Duplicate()
        {
            return this;
        }

        public IQuicStreamFrame RetainedDuplicate()
        {
            return this;
        }

        public IQuicStreamFrame Replace(ByteBuf content)
        {
            return new DefaultQuicStreamFrame(content, HasFin());
        }

        public IQuicStreamFrame Retain()
        {
            return this;
        }

        public IQuicStreamFrame Retain(int increment)
        {
            return this;
        }

        public IQuicStreamFrame Touch()
        {
            return this;
        }

        public IQuicStreamFrame Touch(object hint)
        {
            return this;
        }

        public ByteBuf Content => Unpooled.EmptyBuffer;

        public int RefCnt => 1;

        public bool Release()
        {
            return false;
        }

        public bool Release(int decrement)
        {
            return false;
        }

        IByteBufHolder IByteBufHolder.Copy()
        {
            return Copy();
        }

        IByteBufHolder IByteBufHolder.Duplicate()
        {
            return Duplicate();
        }

        IByteBufHolder IByteBufHolder.RetainedDuplicate()
        {
            return RetainedDuplicate();
        }

        IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
        {
            return Replace(content);
        }

        IByteBufHolder IByteBufHolder.Retain()
        {
            return Retain();
        }

        IByteBufHolder IByteBufHolder.Retain(int increment)
        {
            return Retain(increment);
        }

        IByteBufHolder IByteBufHolder.Touch()
        {
            return Touch();
        }

        IByteBufHolder IByteBufHolder.Touch(object hint)
        {
            return Touch(hint);
        }

        IReferenceCounted IReferenceCounted.Retain()
        {
            return Retain();
        }

        IReferenceCounted IReferenceCounted.Retain(int increment)
        {
            return Retain(increment);
        }

        IReferenceCounted IReferenceCounted.Touch()
        {
            return Touch();
        }

        IReferenceCounted IReferenceCounted.Touch(object hint)
        {
            return Touch(hint);
        }
    }

    /**
     * Returns {@code true} if the frame has the FIN set, which means it notifies the remote peer that
     * there will be no more writing happen. {@code false} otherwise.
     *
     * @return {@code true} if the FIN flag should be set, {@code false} otherwise.
     */
    bool HasFin();
}
