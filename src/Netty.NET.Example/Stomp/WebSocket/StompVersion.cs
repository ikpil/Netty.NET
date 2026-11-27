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
using Netty.NET.Common;

namespace Netty.NET.Example.Stomp.WebSocket;

// CLR: a Java enum with a constructor, fields and methods is a sealed class with static readonly instances, as Handler's
// LogLevel and codec-mqtt's MqttMessageType are: a C# enum is a set of named integral constants and cannot declare
// methods (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). The private
// constructor also takes the constant name, which ToString() returns as Enum.toString() does, and the implicit
// values() is a private Values() in declaration order. The static block is the static constructor, which runs after
// the field initializers in textual order, as the Java static initializers do. AttributeKey.valueOf is
// AttributeKey<StompVersion>.ValueOf, StringUtil.join(",", subProtocols).toString() is string.Join, and
// IllegalArgumentException is ArgumentException.
public sealed class StompVersion
{

    public static readonly StompVersion STOMP_V11 = new StompVersion(nameof(STOMP_V11), "1.1", "v11.stomp");

    public static readonly StompVersion STOMP_V12 = new StompVersion(nameof(STOMP_V12), "1.2", "v12.stomp");

    public static readonly AttributeKey<StompVersion> CHANNEL_ATTRIBUTE_KEY = AttributeKey<StompVersion>.ValueOf("stomp_version");
    public static readonly string SUB_PROTOCOLS;

    static StompVersion()
    {
        List<string> subProtocols = new List<string>(Values().Length);
        foreach (StompVersion stompVersion in Values())
        {
            subProtocols.Add(stompVersion.subProtocol);
        }

        SUB_PROTOCOLS = string.Join(",", subProtocols);
    }

    private readonly string name;
    private readonly string version;
    private readonly string subProtocol;

    private StompVersion(string name, string version, string subProtocol)
    {
        this.name = name;
        this.version = version;
        this.subProtocol = subProtocol;
    }

    public string Version()
    {
        return version;
    }

    public string SubProtocol()
    {
        return subProtocol;
    }

    public static StompVersion FindBySubProtocol(string subProtocol)
    {
        if (subProtocol != null)
        {
            foreach (StompVersion stompVersion in Values())
            {
                if (stompVersion.SubProtocol().Equals(subProtocol))
                {
                    return stompVersion;
                }
            }
        }

        throw new ArgumentException("Not found StompVersion for '" + subProtocol + "'");
    }

    private static StompVersion[] Values()
    {
        return new[] { STOMP_V11, STOMP_V12 };
    }

    public override string ToString()
    {
        return name;
    }
}
