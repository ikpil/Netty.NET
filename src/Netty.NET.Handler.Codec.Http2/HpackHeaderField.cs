/*
 * Copyright 2015 The Netty Project
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

/*
 * Copyright 2014 Twitter, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.HpackUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: the package-private class is internal; its package-private fields, constructor and members are internal
// (the tests reach them through InternalsVisibleTo). name + ": " + value concatenates the CharSequence texts.
internal class HpackHeaderField
{

    // Section 4.1. Calculating Table Size
    // The additional 32 octets account for an estimated
    // overhead associated with the structure.
    internal const int HEADER_ENTRY_OVERHEAD = 32;

    internal static long SizeOf(ICharSequence name, ICharSequence value)
    {
        return name.Length() + value.Length() + HEADER_ENTRY_OVERHEAD;
    }

    internal readonly ICharSequence name;
    internal readonly ICharSequence value;

    // This constructor can only be used if name and value are ISO-8859-1 encoded.
    internal HpackHeaderField(ICharSequence name, ICharSequence value)
    {
        this.name = CheckNotNull(name, "name");
        this.value = CheckNotNull(value, "value");
    }

    internal int Size()
    {
        return name.Length() + value.Length() + HEADER_ENTRY_OVERHEAD;
    }

    public bool EqualsForTest(HpackHeaderField other)
    {
        return EqualsVariableTime(name, other.name) && EqualsVariableTime(value, other.value);
    }

    public override string ToString()
    {
        return name + ": " + value;
    }
}
