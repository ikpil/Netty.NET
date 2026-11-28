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
using System.Globalization;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Model the SubscriptionOption used in Subscribe MQTT v5 packet
 */
public sealed class MqttSubscriptionOption
{
    // CLR: an enum with a field and methods is a sealed class with static readonly constants; Name(), Values() and
    // ToString() stand for the java.lang.Enum members (see MqttQoS).
    public sealed class RetainedHandlingPolicy
    {
        public static readonly RetainedHandlingPolicy SEND_AT_SUBSCRIBE =
                new RetainedHandlingPolicy(nameof(SEND_AT_SUBSCRIBE), 0);
        public static readonly RetainedHandlingPolicy SEND_AT_SUBSCRIBE_IF_NOT_YET_EXISTS =
                new RetainedHandlingPolicy(nameof(SEND_AT_SUBSCRIBE_IF_NOT_YET_EXISTS), 1);
        public static readonly RetainedHandlingPolicy DONT_SEND_AT_SUBSCRIBE =
                new RetainedHandlingPolicy(nameof(DONT_SEND_AT_SUBSCRIBE), 2);

        private readonly int value;
        private readonly string name;

        private RetainedHandlingPolicy(string name, int value)
        {
            this.name = name;
            this.value = value;
        }

        public int Value()
        {
            return value;
        }

        public static RetainedHandlingPolicy ValueOf(int value)
        {
            switch (value)
            {
            case 0:
                return SEND_AT_SUBSCRIBE;
            case 1:
                return SEND_AT_SUBSCRIBE_IF_NOT_YET_EXISTS;
            case 2:
                return DONT_SEND_AT_SUBSCRIBE;
            default:
                throw new ArgumentException("invalid RetainedHandlingPolicy: " +
                        value.ToString(CultureInfo.InvariantCulture));
            }
        }

        public string Name()
        {
            return name;
        }

        public static RetainedHandlingPolicy[] Values()
        {
            return new[] { SEND_AT_SUBSCRIBE, SEND_AT_SUBSCRIBE_IF_NOT_YET_EXISTS, DONT_SEND_AT_SUBSCRIBE };
        }

        public override string ToString()
        {
            return name;
        }
    }

    private readonly MqttQoS qos;
    private readonly bool noLocal;
    private readonly bool retainAsPublished;
    private readonly RetainedHandlingPolicy retainHandling;

    public static MqttSubscriptionOption OnlyFromQos(MqttQoS qos)
    {
        return new MqttSubscriptionOption(qos, false, false, RetainedHandlingPolicy.SEND_AT_SUBSCRIBE);
    }

    public MqttSubscriptionOption(MqttQoS qos,
                                  bool noLocal,
                                  bool retainAsPublished,
                                  RetainedHandlingPolicy retainHandling)
    {
        this.qos = qos;
        this.noLocal = noLocal;
        this.retainAsPublished = retainAsPublished;
        this.retainHandling = retainHandling;
    }

    public MqttQoS Qos()
    {
        return qos;
    }

    public bool IsNoLocal()
    {
        return noLocal;
    }

    public bool IsRetainAsPublished()
    {
        return retainAsPublished;
    }

    public RetainedHandlingPolicy RetainHandling()
    {
        return retainHandling;
    }

    public override bool Equals(object o)
    {
        if (this == o)
        {
            return true;
        }
        if (o == null || GetType() != o.GetType())
        {
            return false;
        }

        MqttSubscriptionOption that = (MqttSubscriptionOption) o;

        if (noLocal != that.noLocal)
        {
            return false;
        }
        if (retainAsPublished != that.retainAsPublished)
        {
            return false;
        }
        if (qos != that.qos)
        {
            return false;
        }

        return retainHandling == that.retainHandling;
    }

    public override int GetHashCode()
    {
        int result = qos.GetHashCode();
        result = 31 * result + (noLocal ? 1 : 0);
        result = 31 * result + (retainAsPublished ? 1 : 0);
        result = 31 * result + retainHandling.GetHashCode();
        return result;
    }

    // CLR: Java string conversion writes null as "null" and a boolean as "true"/"false"
    // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11), while C# concatenation writes an
    // empty string for null (https://learn.microsoft.com/dotnet/csharp/language-reference/operators/addition-operator)
    // and Boolean.ToString() returns "True"/"False" (https://learn.microsoft.com/dotnet/api/system.boolean.tostring).
    public override string ToString()
    {
        return "SubscriptionOption[" +
                "qos=" + (qos?.ToString() ?? "null") +
                ", noLocal=" + (noLocal ? "true" : "false") +
                ", retainAsPublished=" + (retainAsPublished ? "true" : "false") +
                ", retainHandling=" + (retainHandling?.ToString() ?? "null") +
                ']';
    }
}
