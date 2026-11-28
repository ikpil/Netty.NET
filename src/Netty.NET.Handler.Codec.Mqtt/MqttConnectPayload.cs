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

using System.Globalization;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * Payload of {@link MqttConnectMessage}
 */
// CLR: the @Deprecated String-based constructor and the @Deprecated willMessage()/password() accessors name their
// replacements (the byte[] constructor, willMessageInBytes() and passwordInBytes()), so they are not ported (owner
// decision 2026-11-16); their Javadoc is recorded in docs/codec-mqtt-porting-manifest.json.
public sealed class MqttConnectPayload
{
    private readonly string clientIdentifier;
    private readonly MqttProperties willProperties;
    private readonly string willTopic;
    private readonly byte[] willMessage;
    private readonly string userName;
    private readonly byte[] password;

    public MqttConnectPayload(
            string clientIdentifier,
            string willTopic,
            byte[] willMessage,
            string userName,
            byte[] password)
        : this(clientIdentifier,
                MqttProperties.NO_PROPERTIES,
                willTopic,
                willMessage,
                userName,
                password)
    {
    }

    public MqttConnectPayload(
            string clientIdentifier,
            MqttProperties willProperties,
            string willTopic,
            byte[] willMessage,
            string userName,
            byte[] password)
    {
        this.clientIdentifier = clientIdentifier;
        this.willProperties = MqttProperties.WithEmptyDefaults(willProperties);
        this.willTopic = willTopic;
        this.willMessage = willMessage;
        this.userName = userName;
        this.password = password;
    }

    public string ClientIdentifier()
    {
        return clientIdentifier;
    }

    public MqttProperties WillProperties()
    {
        return willProperties;
    }

    public string WillTopic()
    {
        return willTopic;
    }

    public byte[] WillMessageInBytes()
    {
        return willMessage;
    }

    public string UserName()
    {
        return userName;
    }

    public byte[] PasswordInBytes()
    {
        return password;
    }

    // CLR: Java string conversion writes null as "null" (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11);
    // Arrays.toString(byte[]) is ArraysToString below.
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
            .Append('[')
            .Append("clientIdentifier=").Append(clientIdentifier ?? "null")
            .Append(", willTopic=").Append(willTopic ?? "null")
            .Append(", willMessage=").Append(ArraysToString(willMessage))
            .Append(", userName=").Append(userName ?? "null")
            .Append(", password=").Append("****")
            .Append(']')
            .ToString();
    }

    // CLR: Arrays.toString(byte[]) lists the elements "enclosed in square brackets", separated by ", ", converted "as by
    // String.valueOf(byte)", and returns "null" for a null array
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html#toString(byte%5B%5D)). The Java
    // byte is signed (https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1), so each C# byte is
    // printed through sbyte with the invariant culture to keep the Java text.
    private static string ArraysToString(byte[] a)
    {
        if (a == null)
        {
            return "null";
        }

        StringBuilder b = new StringBuilder("[");

        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0)
            {
                b.Append(", ");
            }

            b.Append(((sbyte) a[i]).ToString(CultureInfo.InvariantCulture));
        }

        return b.Append(']').ToString();
    }
}
