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

using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * The default {@link Http2WindowUpdateFrame} implementation.
 */
// CLR: stream(Http2FrameStream) is a covariant override of AbstractHttp2StreamFrame.Stream; the non-final Java methods
// are virtual. toString() keeps Java's text: a null stream prints "null" and numbers use the invariant culture
// (Http2CodecUtil.ToJavaString).
public class DefaultHttp2WindowUpdateFrame : AbstractHttp2StreamFrame, IHttp2WindowUpdateFrame
{

    private readonly int windowUpdateIncrement;

    public DefaultHttp2WindowUpdateFrame(int windowUpdateIncrement)
    {
        this.windowUpdateIncrement = windowUpdateIncrement;
    }

    public override DefaultHttp2WindowUpdateFrame Stream(IHttp2FrameStream stream)
    {
        base.Stream(stream);
        return this;
    }

    public override string Name()
    {
        return "WINDOW_UPDATE";
    }

    public virtual int WindowSizeIncrement()
    {
        return windowUpdateIncrement;
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) +
                "(stream=" + ToJavaString(Stream()) + ", windowUpdateIncrement=" + ToJavaString(windowUpdateIncrement) +
                ')';
    }
}
