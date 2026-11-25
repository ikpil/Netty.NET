/*
 * Copyright 2017 The Netty Project
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

// CLR: the simple Java enum Type is a C# enum; type() is Type_(): a member cannot share the name of the nested Type
// enum (CS0102, owner rule: trailing underscore). The package-private factories are internal.
public sealed class Http2FrameStreamEvent
{

    private readonly IHttp2FrameStream stream;
    private readonly Type type;

    public enum Type
    {
        State,
        Writability
    }

    private Http2FrameStreamEvent(IHttp2FrameStream stream, Type type)
    {
        this.stream = stream;
        this.type = type;
    }

    public IHttp2FrameStream Stream()
    {
        return stream;
    }

    public Type Type_()
    {
        return type;
    }

    internal static Http2FrameStreamEvent StateChanged(IHttp2FrameStream stream)
    {
        return new Http2FrameStreamEvent(stream, Type.State);
    }

    internal static Http2FrameStreamEvent WritabilityChanged(IHttp2FrameStream stream)
    {
        return new Http2FrameStreamEvent(stream, Type.Writability);
    }
}
