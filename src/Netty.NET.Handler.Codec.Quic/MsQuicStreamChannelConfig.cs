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

using System;
using System.Collections.Generic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the IQuicStreamChannelConfig of MsQuicStreamChannel, the counterpart of the JNI unit
// QuicheQuicStreamChannelConfig (jvm-only-replaced), with its behavior: half-closure is allowed by default and only a
// bidirectional stream supports changing it (ALLOW_HALF_CLOSURE is an option of bidirectional streams only), and
// READ_FRAMES selects IQuicStreamFrame reads. The DirectIoByteBufAllocator wrapper only gave quiche direct memory and is
// not ported (jvm-only-replaced), so the allocator is the configured one. Java's setOption(READ_FRAMES, value) sets the
// value and then returns super.setOption(...), which is false for an option the base config does not know; here SetValue
// returns true, because the option is set.
internal sealed class MsQuicStreamChannelConfig : DefaultChannelConfig, IQuicStreamChannelConfig
{
    private volatile bool allowHalfClosure = true;
    private volatile bool readFrames;

    internal MsQuicStreamChannelConfig(IQuicStreamChannel channel) : base(channel)
    {
    }

    public override IReadOnlyDictionary<ChannelOption, object> GetOptions()
    {
        Dictionary<ChannelOption, object> options = new Dictionary<ChannelOption, object>();
        foreach (KeyValuePair<ChannelOption, object> entry in base.GetOptions())
        {
            options[entry.Key] = entry.Value;
        }
        if (IsHalfClosureSupported())
        {
            options[ChannelOption.ALLOW_HALF_CLOSURE] = IsAllowHalfClosure();
            options[QuicChannelOption.READ_FRAMES] = IsReadFrames();
        }
        return options;
    }

    protected override bool TryGetValue(ChannelOption option, out object value)
    {
        if (option == ChannelOption.ALLOW_HALF_CLOSURE)
        {
            value = IsAllowHalfClosure();
            return true;
        }
        if (option == QuicChannelOption.READ_FRAMES)
        {
            value = IsReadFrames();
            return true;
        }
        return base.TryGetValue(option, out value);
    }

    protected override bool SetValue(ChannelOption option, object value)
    {
        if (option == ChannelOption.ALLOW_HALF_CLOSURE)
        {
            if (IsHalfClosureSupported())
            {
                SetAllowHalfClosure((bool) value);
                return true;
            }
            return false;
        }
        if (option == QuicChannelOption.READ_FRAMES)
        {
            SetReadFrames((bool) value);
            return true;
        }
        return base.SetValue(option, value);
    }

    public IQuicStreamChannelConfig SetReadFrames(bool readFrames)
    {
        this.readFrames = readFrames;
        return this;
    }

    public bool IsReadFrames()
    {
        return readFrames;
    }

    public IDuplexChannelConfig SetAllowHalfClosure(bool allowHalfClosure)
    {
        if (!IsHalfClosureSupported())
        {
            throw new NotSupportedException("Undirectional streams don't support half-closure");
        }
        this.allowHalfClosure = allowHalfClosure;
        return this;
    }

    public bool IsAllowHalfClosure()
    {
        return allowHalfClosure;
    }

    private bool IsHalfClosureSupported()
    {
        return ((IQuicStreamChannel) Channel).Type() == QuicStreamType.BIDIRECTIONAL;
    }
}
