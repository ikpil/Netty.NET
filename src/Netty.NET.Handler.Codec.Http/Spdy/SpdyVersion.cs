/*
 * Copyright 2013 The Netty Project
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

namespace Netty.NET.Handler.Codec.Spdy;

// CLR: a Java enum with a constructor, fields and methods is a sealed class with static readonly instances, as
// codec-base Base64Dialect and core HttpStatusClass (docs/codec-base-porting.md): "You can't define a method inside the
// definition of an enumeration type"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). ToString() returns the
// constant name as Enum.toString() does; the implicit values()/valueOf(String) members are not added (no caller).
public sealed class SpdyVersion
{
    public static readonly SpdyVersion SPDY_3_1 = new SpdyVersion(nameof(SPDY_3_1), 3, 1);

    private readonly int version;
    private readonly int minorVersion;
    // CLR: the constant name that java.lang.Enum keeps for name() and toString().
    private readonly string name;

    private SpdyVersion(string name, int version, int minorVersion)
    {
        this.name = name;
        this.version = version;
        this.minorVersion = minorVersion;
    }

    public int Version()
    {
        return version;
    }

    public int MinorVersion()
    {
        return minorVersion;
    }

    public override string ToString()
    {
        return name;
    }
}
