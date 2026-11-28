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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Payload for MQTT unsuback message as in V5.
 */
public sealed class MqttUnsubAckPayload
{
    private readonly IList<MqttReasonCodes.UnsubAck> unsubscribeReasonCodes;

    private static readonly MqttUnsubAckPayload EMPTY = new MqttUnsubAckPayload();

    public static MqttUnsubAckPayload WithEmptyDefaults(MqttUnsubAckPayload payload)
    {
        if (payload == null)
        {
            return EMPTY;
        }
        else
        {
            return payload;
        }
    }

    public MqttUnsubAckPayload(params short[] unsubscribeReasonCodes)
    {
        ObjectUtil.CheckNotNull(unsubscribeReasonCodes, "unsubscribeReasonCodes");

        List<MqttReasonCodes.UnsubAck> list = new List<MqttReasonCodes.UnsubAck>(unsubscribeReasonCodes.Length);

        foreach (short v in unsubscribeReasonCodes)
        {
            list.Add(MqttReasonCodes.UnsubAck.ValueOf((byte) (v & 0xFF)));
        }

        this.unsubscribeReasonCodes = list.AsReadOnly();
    }

    // CLR: the elements are short, a value type that "can't" be null
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/value-types), so the per-element
    // ObjectUtil.checkNotNull(v, "unsubscribeReasonCode") of the boxed Java Short has no counterpart.
    public MqttUnsubAckPayload(IEnumerable<short> unsubscribeReasonCodes)
    {
        ObjectUtil.CheckNotNull(unsubscribeReasonCodes, "unsubscribeReasonCodes");

        List<MqttReasonCodes.UnsubAck> list = new List<MqttReasonCodes.UnsubAck>();

        foreach (short v in unsubscribeReasonCodes)
        {
            list.Add(MqttReasonCodes.UnsubAck.ValueOf((byte) v));
        }

        this.unsubscribeReasonCodes = list.AsReadOnly();
    }

    public IList<short> UnsubscribeReasonCodes()
    {
        return TypedReasonCodesToOrdinal();
    }

    private IList<short> TypedReasonCodesToOrdinal()
    {
        List<short> codes = new List<short>(unsubscribeReasonCodes.Count);

        foreach (MqttReasonCodes.UnsubAck code in unsubscribeReasonCodes)
        {
            codes.Add((short) (code.ByteValue() & 0xFF));
        }

        return codes;
    }

    public IList<MqttReasonCodes.UnsubAck> TypedReasonCodes()
    {
        return unsubscribeReasonCodes;
    }

    // CLR: appending a java.util.List writes AbstractCollection.toString() (see MqttSubAckPayload), built here.
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
                .Append('[')
                .Append("unsubscribeReasonCodes=")
                .Append('[').Append(string.Join(", ", unsubscribeReasonCodes.Select(c => c?.ToString() ?? "null")))
                .Append(']')
                .Append(']')
                .ToString();
    }
}
