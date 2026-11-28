/*
 * Copyright 2014 The Netty Project
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
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Payload of the {@link MqttSubAckMessage}
 */
public class MqttSubAckPayload
{
    private readonly IList<MqttReasonCodes.SubAck> reasonCodes;

    public MqttSubAckPayload(params int[] reasonCodes)
    {
        ObjectUtil.CheckNotNull(reasonCodes, "reasonCodes");

        List<MqttReasonCodes.SubAck> list = new List<MqttReasonCodes.SubAck>(reasonCodes.Length);

        foreach (int v in reasonCodes)
        {
            list.Add(MqttReasonCodes.SubAck.ValueOf((byte) (v & 0xFF)));
        }

        this.reasonCodes = list.AsReadOnly();
    }

    public MqttSubAckPayload(params MqttReasonCodes.SubAck[] reasonCodes)
    {
        ObjectUtil.CheckNotNull(reasonCodes, "reasonCodes");

        List<MqttReasonCodes.SubAck> list = new List<MqttReasonCodes.SubAck>(reasonCodes.Length);
        list.AddRange(reasonCodes);
        this.reasonCodes = list.AsReadOnly();
    }

    // CLR: the elements are int, a value type that "can't" be null
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/value-types), while a Java Integer
    // element can be null; the source stops at the first null element, a case a C# caller cannot produce, so that
    // check has no counterpart.
    public MqttSubAckPayload(IEnumerable<int> reasonCodes)
    {
        ObjectUtil.CheckNotNull(reasonCodes, "reasonCodes");

        List<MqttReasonCodes.SubAck> list = new List<MqttReasonCodes.SubAck>();

        foreach (int v in reasonCodes)
        {
            list.Add(MqttReasonCodes.SubAck.ValueOf((byte) v));
        }

        this.reasonCodes = list.AsReadOnly();
    }

    public IList<int> GrantedQoSLevels()
    {
        List<int> qosLevels = new List<int>(reasonCodes.Count);

        foreach (MqttReasonCodes.SubAck code in reasonCodes)
        {
            if ((code.ByteValue() & 0xFF) > MqttQoS.EXACTLY_ONCE.Value())
            {
                qosLevels.Add(MqttQoS.FAILURE.Value());
            }
            else
            {
                qosLevels.Add(code.ByteValue() & 0xFF);
            }
        }

        return qosLevels;
    }

    public IList<int> ReasonCodes()
    {
        return TypedReasonCodesToOrdinal();
    }

    private IList<int> TypedReasonCodesToOrdinal()
    {
        List<int> intCodes = new List<int>(reasonCodes.Count);

        foreach (MqttReasonCodes.SubAck code in reasonCodes)
        {
            intCodes.Add(code.ByteValue() & 0xFF);
        }

        return intCodes;
    }

    public IList<MqttReasonCodes.SubAck> TypedReasonCodes()
    {
        return reasonCodes;
    }

    // CLR: appending a java.util.List writes AbstractCollection.toString(): the elements in iteration order "enclosed in
    // square brackets", separated by ", ", converted "as by String.valueOf(Object)"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/AbstractCollection.html#toString()); the
    // C# collection ToString() returns its type name, so that text is built here.
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
            .Append('[')
            .Append("reasonCodes=")
            .Append('[').Append(string.Join(", ", reasonCodes.Select(c => c?.ToString() ?? "null"))).Append(']')
            .Append(']')
            .ToString();
    }
}
