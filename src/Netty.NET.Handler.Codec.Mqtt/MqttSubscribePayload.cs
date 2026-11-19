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
 * Payload of the {@link MqttSubscribeMessage}
 */
// CLR: java.util.List is IList<T>; Collections.unmodifiableList, an "unmodifiable view" whose reads "read through" to
// the list (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Collections.html#unmodifiableList(java.util.List)),
// is a ReadOnlyCollection<T> wrapper, which "reflects" changes to the underlying list
// (https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.asreadonly).
public sealed class MqttSubscribePayload
{
    private readonly IList<MqttTopicSubscription> topicSubscriptions;

    public MqttSubscribePayload(IList<MqttTopicSubscription> topicSubscriptions)
    {
        this.topicSubscriptions = new ReadOnlyCollection<MqttTopicSubscription>(topicSubscriptions);
    }

    public IList<MqttTopicSubscription> TopicSubscriptions()
    {
        return topicSubscriptions;
    }

    // CLR: StringBuilder.append(Object) writes a null element as "null"
    // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
    public override string ToString()
    {
        StringBuilder builder = new StringBuilder(StringUtil.SimpleClassName(this)).Append('[');
        for (int i = 0; i < topicSubscriptions.Count; i++)
        {
            builder.Append(topicSubscriptions[i]?.ToString() ?? "null").Append(", ");
        }
        if (topicSubscriptions.Count != 0)
        {
            builder.Length = builder.Length - 2;
        }
        return builder.Append(']').ToString();
    }
}
