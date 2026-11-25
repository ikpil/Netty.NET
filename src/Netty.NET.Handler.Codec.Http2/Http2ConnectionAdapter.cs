/*
 * Copyright 2014 The Netty Project
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

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Provides empty implementations of all {@link Http2Connection.Listener} methods.
 */
// CLR: the non-final Java methods are virtual so that subclasses override them.
public class Http2ConnectionAdapter : IHttp2Connection.IListener
{
    public virtual void OnStreamAdded(IHttp2Stream stream)
    {
    }

    public virtual void OnStreamActive(IHttp2Stream stream)
    {
    }

    public virtual void OnStreamHalfClosed(IHttp2Stream stream)
    {
    }

    public virtual void OnStreamClosed(IHttp2Stream stream)
    {
    }

    public virtual void OnStreamRemoved(IHttp2Stream stream)
    {
    }

    public virtual void OnGoAwaySent(int lastStreamId, long errorCode, ByteBuf debugData)
    {
    }

    public virtual void OnGoAwayReceived(int lastStreamId, long errorCode, ByteBuf debugData)
    {
    }
}
