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

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Abstract implementation of {@link Http2StreamFrame}.
 */
// CLR: stream(Http2FrameStream) narrows its return type to AbstractHttp2StreamFrame, which the subclasses narrow
// further; C# 9 covariant returns cover class overrides only, so the virtual Stream(IHttp2FrameStream) returns
// AbstractHttp2StreamFrame and IHttp2StreamFrame.Stream is implemented explicitly with it. The abstract Java class
// leaves name() of Http2Frame to its subclasses; a C# class must map every interface member, so it declares the
// abstract Name(). The non-final Java methods are virtual; equals/hashCode are Equals/GetHashCode.
public abstract class AbstractHttp2StreamFrame : IHttp2StreamFrame
{

    private IHttp2FrameStream stream;

    public virtual AbstractHttp2StreamFrame Stream(IHttp2FrameStream stream)
    {
        this.stream = stream;
        return this;
    }

    IHttp2StreamFrame IHttp2StreamFrame.Stream(IHttp2FrameStream stream)
    {
        return Stream(stream);
    }

    public virtual IHttp2FrameStream Stream()
    {
        return stream;
    }

    public abstract string Name();

    /**
     * Returns {@code true} if {@code o} has equal {@code stream} to this object.
     */
    public override bool Equals(object o)
    {
        if (!(o is IHttp2StreamFrame))
        {
            return false;
        }

        IHttp2StreamFrame other = (IHttp2StreamFrame) o;
        return ReferenceEquals(stream, other.Stream()) || stream != null && stream.Equals(other.Stream());
    }

    public override int GetHashCode()
    {
        IHttp2FrameStream stream = this.stream;
        // Must be consistent with equals; super.hashCode() is Object's identity hash.
        if (stream == null)
        {
            return 0;
        }

        return stream.GetHashCode();
    }
}
