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
 * Super-class for SPDY header-block encoders.
 *
 * @see SpdyHeaderBlockZlibEncoder
 * @see SpdyHeaderBlockJZlibEncoder
 * @see SpdyHeaderBlockRawEncoder
 */
// CLR: the package-private static factory and end() are internal. encode(...) is package-private here, but the public
// SpdyHeaderBlockRawEncoder overrides it as public; an override cannot change the accessibility of the method it
// overrides (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), so Encode is public
// abstract here, which keeps SpdyHeaderBlockRawEncoder.Encode public. The class still cannot be subclassed outside the
// assembly, because End() stays internal. SpdyHeaderBlockJZlibEncoder (jzlib) is library-not-ported; newInstance never
// creates it. ByteBufAllocator is IByteBufAllocator and SpdyHeadersFrame is ISpdyHeadersFrame.
public abstract class SpdyHeaderBlockEncoder
{

    internal static SpdyHeaderBlockEncoder NewInstance(
            SpdyVersion version, int compressionLevel, int windowBits, int memLevel)
    {
        return new SpdyHeaderBlockZlibEncoder(version, compressionLevel);
    }

    public abstract ByteBuf Encode(IByteBufAllocator alloc, ISpdyHeadersFrame frame);

    internal abstract void End();
}
