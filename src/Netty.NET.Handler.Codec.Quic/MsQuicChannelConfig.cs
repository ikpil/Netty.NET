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

namespace Netty.NET.Handler.Codec.Quic;

// CLR-only: the IQuicChannelConfig of MsQuicChannel, the counterpart of the JNI unit QuicheQuicChannelConfig
// (jvm-only-replaced). The option dispatch of the Transport's DefaultChannelConfig is TryGetValue/SetValue. QLOG has no
// System.Net.Quic counterpart, so setting it reports NotSupportedException (Quic.SetupChannel logs it as a failed option,
// as for any option a channel rejects); SEGMENTED_DATAGRAM_PACKET_ALLOCATOR is not ported (the datagram writes belong to
// msquic, docs/codec-classes-quic-porting.md).
internal sealed class MsQuicChannelConfig : DefaultChannelConfig, IQuicChannelConfig
{
    internal MsQuicChannelConfig(IChannel channel) : base(channel)
    {
    }

    public override IReadOnlyDictionary<ChannelOption, object> GetOptions()
    {
        Dictionary<ChannelOption, object> options = new Dictionary<ChannelOption, object>();

        foreach (KeyValuePair<ChannelOption, object> entry in base.GetOptions())
        {
            options[entry.Key] = entry.Value;
        }

        options[QuicChannelOption.QLOG] = null;
        return options;
    }

    protected override bool TryGetValue(ChannelOption option, out object value)
    {
        if (option == QuicChannelOption.QLOG)
        {
            value = null;
            return true;
        }

        return base.TryGetValue(option, out value);
    }

    protected override bool SetValue(ChannelOption option, object value)
    {
        if (option == QuicChannelOption.QLOG)
        {
            if (value != null)
            {
                throw new NotSupportedException("QLOG is not supported by System.Net.Quic (msquic)");
            }

            return true;
        }

        return base.SetValue(option, value);
    }
}
