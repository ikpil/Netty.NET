/*
 * Copyright 2016 The Netty Project
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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.String;

/**
 * A class to represent line separators in different environments.
 */
public sealed class LineSeparator
{
    /**
     * The default line separator in the current system.
     */
    public static readonly LineSeparator DEFAULT = new LineSeparator(StringUtil.NEWLINE);

    /**
     * The Unix line separator(LF)
     */
    public static readonly LineSeparator UNIX = new LineSeparator("\n");

    /**
     * The Windows line separator(CRLF)
     */
    public static readonly LineSeparator WINDOWS = new LineSeparator("\r\n");

    private readonly string value;

    /**
     * Create {@link LineSeparator} with the specified {@code lineSeparator} string.
     */
    public LineSeparator(string lineSeparator)
    {
        this.value = ObjectUtil.CheckNotNull(lineSeparator, "lineSeparator");
    }

    /**
     * Return the string value of this line separator.
     */
    public string Value()
    {
        return value;
    }

    public override bool Equals(object o)
    {
        if (this == o)
        {
            return true;
        }
        if (!(o is LineSeparator))
        {
            return false;
        }
        LineSeparator that = (LineSeparator) o;
        return value != null ? value.Equals(that.value) : that.value == null;
    }

    public override int GetHashCode()
    {
        return value != null ? value.GetHashCode() : 0;
    }

    /**
     * Return a <a href="https://en.wikipedia.org/wiki/Hex_dump">hex dump</a> of the line separator in UTF-8 encoding.
     */
    // CLR: CharsetUtil.UTF_8 is Encoding.UTF8, whose replacement fallback matches String.getBytes(Charset), which "always
    // replaces malformed-input and unmappable-character sequences"
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/String.html#getBytes(java.nio.charset.Charset),
    // https://learn.microsoft.com/dotnet/api/system.text.encoding.utf8). GetBytes does not prefix the preamble
    // (https://learn.microsoft.com/dotnet/api/system.text.encoding.getpreamble; docs/common-clr-design.md).
    public override string ToString()
    {
        return ByteBufUtil.HexDump(Encoding.UTF8.GetBytes(value));
    }
}
