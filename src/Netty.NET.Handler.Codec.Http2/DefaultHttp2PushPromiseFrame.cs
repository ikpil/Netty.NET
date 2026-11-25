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

using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Default implementation of {@link Http2PushPromiseFrame}
 */
// CLR: stream(Http2FrameStream) narrows its return type to Http2PushPromiseFrame; C# 9 covariant returns cover class
// overrides only, so the public Stream(IHttp2FrameStream) returns IHttp2PushPromiseFrame and IHttp2StreamFrame.Stream
// is implemented explicitly with it (docs/codec-http-porting.md, Shared CLR adaptations). The package-private
// constructor is internal. toString() keeps Java's text: null members print "null" and numbers use the invariant
// culture (Http2CodecUtil.ToJavaString).
public sealed class DefaultHttp2PushPromiseFrame : IHttp2PushPromiseFrame
{

    private IHttp2FrameStream pushStreamFrame;
    private readonly IHttp2Headers http2Headers;
    private IHttp2FrameStream streamFrame;
    private readonly int padding;
    private readonly int promisedStreamId;

    public DefaultHttp2PushPromiseFrame(IHttp2Headers http2Headers) : this(http2Headers, 0)
    {
    }

    public DefaultHttp2PushPromiseFrame(IHttp2Headers http2Headers, int padding) : this(http2Headers, padding, -1)
    {
    }

    internal DefaultHttp2PushPromiseFrame(IHttp2Headers http2Headers, int padding, int promisedStreamId)
    {
        this.http2Headers = http2Headers;
        this.padding = padding;
        this.promisedStreamId = promisedStreamId;
    }

    public IHttp2StreamFrame PushStream(IHttp2FrameStream stream)
    {
        pushStreamFrame = stream;
        return this;
    }

    public IHttp2FrameStream PushStream()
    {
        return pushStreamFrame;
    }

    public IHttp2Headers Http2Headers()
    {
        return http2Headers;
    }

    public int Padding()
    {
        return padding;
    }

    public int PromisedStreamId()
    {
        if (pushStreamFrame != null)
        {
            return pushStreamFrame.Id();
        }
        else
        {
            return promisedStreamId;
        }
    }

    public IHttp2PushPromiseFrame Stream(IHttp2FrameStream stream)
    {
        streamFrame = stream;
        return this;
    }

    IHttp2StreamFrame IHttp2StreamFrame.Stream(IHttp2FrameStream stream) => Stream(stream);

    public IHttp2FrameStream Stream()
    {
        return streamFrame;
    }

    public string Name()
    {
        return "PUSH_PROMISE_FRAME";
    }

    public override string ToString()
    {
        return "DefaultHttp2PushPromiseFrame{" +
                "pushStreamFrame=" + ToJavaString(pushStreamFrame) +
                ", http2Headers=" + ToJavaString(http2Headers) +
                ", streamFrame=" + ToJavaString(streamFrame) +
                ", padding=" + ToJavaString(padding) +
                '}';
    }
}
