/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Decorator around another {@link Http2ConnectionDecoder} instance.
 */
// CLR: the non-final Java methods are virtual (Http2EmptyDataFrameConnectionDecoder and Http2MaxRstFrameDecoder
// override frameListener); the field and parameter delegate are @delegate (a C# keyword). List<Object> out is
// IList<object> @out. Closeable's close() is Close(), which IHttp2ConnectionDecoder's IDisposable.Dispose() calls.
public class DecoratingHttp2ConnectionDecoder : IHttp2ConnectionDecoder
{
    private readonly IHttp2ConnectionDecoder @delegate;

    public DecoratingHttp2ConnectionDecoder(IHttp2ConnectionDecoder @delegate)
    {
        this.@delegate = CheckNotNull(@delegate, "delegate");
    }

    public virtual void LifecycleManager(IHttp2LifecycleManager lifecycleManager)
    {
        @delegate.LifecycleManager(lifecycleManager);
    }

    public virtual IHttp2Connection Connection()
    {
        return @delegate.Connection();
    }

    public virtual IHttp2LocalFlowController FlowController()
    {
        return @delegate.FlowController();
    }

    public virtual void FrameListener(IHttp2FrameListener listener)
    {
        @delegate.FrameListener(listener);
    }

    public virtual IHttp2FrameListener FrameListener()
    {
        return @delegate.FrameListener();
    }

    public virtual void DecodeFrame(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        @delegate.DecodeFrame(ctx, @in, @out);
    }

    public virtual Http2Settings LocalSettings()
    {
        return @delegate.LocalSettings();
    }

    public virtual bool PrefaceReceived()
    {
        return @delegate.PrefaceReceived();
    }

    public virtual void Close()
    {
        @delegate.Close();
    }
}
