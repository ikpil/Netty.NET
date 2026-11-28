/*
 * Copyright 2012 The Netty Project
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

using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Handler.Logging;

/**
 * Maps the regular {@link LogLevel}s with the {@link InternalLogLevel} ones.
 */
// CLR: a Java enum with a constructor, a field and a method is a sealed class with static readonly instances, as
// codec-base Base64Dialect : a C# enum is "a value type defined by a set of named constants of the underlying
// integral numeric type", and "You can't define a method inside the definition of an enumeration type"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). Like the Java
// reference, a LogLevel variable can be null, which keeps LoggingHandler's checkNotNull(level, "level")
// meaningful. ToString() returns the constant name as Enum.toString() does; the implicit values()/valueOf(String)
// members are not added (no caller).
public sealed class LogLevel
{
    public static readonly LogLevel TRACE = new LogLevel(nameof(TRACE), InternalLogLevel.TRACE);
    public static readonly LogLevel DEBUG = new LogLevel(nameof(DEBUG), InternalLogLevel.DEBUG);
    public static readonly LogLevel INFO = new LogLevel(nameof(INFO), InternalLogLevel.INFO);
    public static readonly LogLevel WARN = new LogLevel(nameof(WARN), InternalLogLevel.WARN);
    public static readonly LogLevel ERROR = new LogLevel(nameof(ERROR), InternalLogLevel.ERROR);

    private readonly InternalLogLevel internalLevel;
    // CLR: the constant name that java.lang.Enum keeps for name() and toString().
    private readonly string name;

    private LogLevel(string name, InternalLogLevel internalLevel)
    {
        this.name = name;
        this.internalLevel = internalLevel;
    }

    /**
     * For internal use only.
     *
     * <p/>Converts the specified {@link LogLevel} to its {@link InternalLogLevel} variant.
     *
     * @return the converted level.
     */
    public InternalLogLevel ToInternalLevel()
    {
        return internalLevel;
    }

    public override string ToString()
    {
        return name;
    }
}
