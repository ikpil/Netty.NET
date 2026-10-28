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

using System;
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels;

/**
 * Default {@link MessageSizeEstimator} implementation which supports the estimation of the size of
 * {@link ByteBuf}, {@link ByteBufHolder} and {@link FileRegion}.
 */
public sealed class DefaultMessageSizeEstimator : IMessageSizeEstimator
{
    /**
     * Return the default implementation which returns {@code 8} for unknown messages.
     */
    public static readonly IMessageSizeEstimator DEFAULT = new DefaultMessageSizeEstimator(8);
    private readonly IMessageSizeEstimator.IHandle _handle;
    /**
     * Create a new instance
     *
     * @param unknownSize       The size which is returned for unknown messages.
     */
    public DefaultMessageSizeEstimator(int unknownSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unknownSize);
        _handle = new HandleImpl(unknownSize);
    }
    public IMessageSizeEstimator.IHandle NewHandle() => _handle;
    private sealed class HandleImpl(int unknownSize) : IMessageSizeEstimator.IHandle
    {
        public int Size(object msg) => msg switch
        {
            ByteBuf buffer => buffer.ReadableBytes,
            IByteBufHolder holder => holder.Content.ReadableBytes,
            IFileRegion => 0,
            _ => unknownSize
        };
    }
}
