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
 * Variable Header for the {@link MqttConnectMessage}
 */
public sealed class MqttConnectVariableHeader
{
    private readonly string name;
    private readonly int version;
    private readonly bool hasUserName;
    private readonly bool hasPassword;
    private readonly bool isWillRetain;
    private readonly int willQos;
    private readonly bool isWillFlag;
    private readonly bool isCleanSession;
    private readonly int keepAliveTimeSeconds;
    private readonly MqttProperties properties;

    public MqttConnectVariableHeader(
            string name,
            int version,
            bool hasUserName,
            bool hasPassword,
            bool isWillRetain,
            int willQos,
            bool isWillFlag,
            bool isCleanSession,
            int keepAliveTimeSeconds)
        : this(name,
                version,
                hasUserName,
                hasPassword,
                isWillRetain,
                willQos,
                isWillFlag,
                isCleanSession,
                keepAliveTimeSeconds,
                MqttProperties.NO_PROPERTIES)
    {
    }

    public MqttConnectVariableHeader(
            string name,
            int version,
            bool hasUserName,
            bool hasPassword,
            bool isWillRetain,
            int willQos,
            bool isWillFlag,
            bool isCleanSession,
            int keepAliveTimeSeconds,
            MqttProperties properties)
    {
        this.name = name;
        this.version = version;
        this.hasUserName = hasUserName;
        this.hasPassword = hasPassword;
        this.isWillRetain = isWillRetain;
        this.willQos = willQos;
        this.isWillFlag = isWillFlag;
        this.isCleanSession = isCleanSession;
        this.keepAliveTimeSeconds = keepAliveTimeSeconds;
        this.properties = MqttProperties.WithEmptyDefaults(properties);
    }

    public string Name()
    {
        return name;
    }

    public int Version()
    {
        return version;
    }

    public bool HasUserName()
    {
        return hasUserName;
    }

    public bool HasPassword()
    {
        return hasPassword;
    }

    public bool IsWillRetain()
    {
        return isWillRetain;
    }

    public int WillQos()
    {
        return willQos;
    }

    public bool IsWillFlag()
    {
        return isWillFlag;
    }

    public bool IsCleanSession()
    {
        return isCleanSession;
    }

    public int KeepAliveTimeSeconds()
    {
        return keepAliveTimeSeconds;
    }

    public MqttProperties Properties()
    {
        return properties;
    }

    // CLR: Java string conversion writes null as "null", a boolean as "true"/"false" and an int without culture
    // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11); the C# StringBuilder writes nothing for
    // null, "True"/"False" (https://learn.microsoft.com/dotnet/api/system.boolean.tostring) and culture-formatted
    // numbers (https://learn.microsoft.com/dotnet/api/system.int32.tostring), so the Java text is kept explicitly.
    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
            .Append('[')
            .Append("name=").Append(name ?? "null")
            .Append(", version=").Append(version.ToString(CultureInfo.InvariantCulture))
            .Append(", hasUserName=").Append(hasUserName ? "true" : "false")
            .Append(", hasPassword=").Append(hasPassword ? "true" : "false")
            .Append(", isWillRetain=").Append(isWillRetain ? "true" : "false")
            .Append(", isWillFlag=").Append(isWillFlag ? "true" : "false")
            .Append(", isCleanSession=").Append(isCleanSession ? "true" : "false")
            .Append(", keepAliveTimeSeconds=").Append(keepAliveTimeSeconds.ToString(CultureInfo.InvariantCulture))
            .Append(']')
            .ToString();
    }
}
