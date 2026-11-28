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

using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http3;

internal class QpackHeaderField
{

    /**
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-dynamic-table-size">
     *     Section 3.2.1 Dynamic Table Size</a>.
     * The size of an entry is the sum of its name's length in bytes, its
     * value's length in bytes, and 32.
     */
    internal const int ENTRY_OVERHEAD = 32;

    internal static long SizeOf(ICharSequence name, ICharSequence value)
    {
        return name.Length() + value.Length() + ENTRY_OVERHEAD;
    }

    internal readonly ICharSequence name;
    internal readonly ICharSequence value;

    // This constructor can only be used if name and value are ISO-8859-1 encoded.
    internal QpackHeaderField(ICharSequence name, ICharSequence value)
    {
        this.name = CheckNotNull(name, "name");
        this.value = CheckNotNull(value, "value");
    }

    internal long Size()
    {
        return SizeOf(name, value);
    }

    public override string ToString()
    {
        return name + ": " + value;
    }
}
