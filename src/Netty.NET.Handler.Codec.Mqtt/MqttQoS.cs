/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Globalization;

namespace Netty.NET.Handler.Codec.Mqtt;

// CLR: a Java enum with a field, a constructor and methods is a sealed class whose constants are static readonly
// instances, as Base64Dialect in codec-base: a C# enum is a set of named integral constants and "You can't define a
// method inside the definition of an enumeration type" (https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/enum),
// while a Java enum class "has no instances other than those defined by its enum constants"
// (https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html#jls-8.9). The members java.lang.Enum supplies are kept:
// Name() and ToString() return the constant name, Values() returns a new array in declaration order (JLS 8.9.3,
// https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Enum.html), and Equals/GetHashCode stay
// reference based like the final Enum.equals/hashCode. Numbers in messages use the invariant culture, because
// Int32.ToString() uses the current culture (https://learn.microsoft.com/dotnet/api/system.int32.tostring).
public sealed class MqttQoS
{
    public static readonly MqttQoS AT_MOST_ONCE = new MqttQoS(nameof(AT_MOST_ONCE), 0);
    public static readonly MqttQoS AT_LEAST_ONCE = new MqttQoS(nameof(AT_LEAST_ONCE), 1);
    public static readonly MqttQoS EXACTLY_ONCE = new MqttQoS(nameof(EXACTLY_ONCE), 2);
    public static readonly MqttQoS FAILURE = new MqttQoS(nameof(FAILURE), 0x80);

    private readonly int value;
    private readonly string name;

    private MqttQoS(string name, int value)
    {
        this.name = name;
        this.value = value;
    }

    public int Value()
    {
        return value;
    }

    public static MqttQoS ValueOf(int value)
    {
        switch (value)
        {
        case 0:
            return AT_MOST_ONCE;
        case 1:
            return AT_LEAST_ONCE;
        case 2:
            return EXACTLY_ONCE;
        case 0x80:
            return FAILURE;
        default:
            throw new ArgumentException("invalid QoS: " + value.ToString(CultureInfo.InvariantCulture));
        }
    }

    public string Name()
    {
        return name;
    }

    public static MqttQoS[] Values()
    {
        return new[] { AT_MOST_ONCE, AT_LEAST_ONCE, EXACTLY_ONCE, FAILURE };
    }

    public override string ToString()
    {
        return name;
    }
}
