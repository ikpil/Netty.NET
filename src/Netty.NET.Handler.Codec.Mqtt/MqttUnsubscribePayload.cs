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
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Payload of the {@link MqttUnsubscribeMessage}
 */
// CLR: java.util.List is IList<T> and Collections.unmodifiableList is a ReadOnlyCollection<T> view, as in
// MqttSubscribePayload.
public sealed class MqttUnsubscribePayload
{
    private readonly IList<string> topics;

    public MqttUnsubscribePayload(IList<string> topics)
    {
        this.topics = new ReadOnlyCollection<string>(topics);
    }

    public IList<string> Topics()
    {
        return topics;
    }

    // CLR: StringBuilder.append(String) writes null as "null"
    // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
    public override string ToString()
    {
        StringBuilder builder = new StringBuilder(StringUtil.SimpleClassName(this)).Append('[');
        for (int i = 0; i < topics.Count; i++)
        {
            builder.Append("topicName = ").Append(topics[i] ?? "null").Append(", ");
        }
        if (topics.Count != 0)
        {
            builder.Length = builder.Length - 2;
        }
        return builder.Append("]").ToString();
    }
}
