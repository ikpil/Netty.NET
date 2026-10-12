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

using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

public abstract partial class ByteBuf
{
    private protected virtual int DiagnosticMaxCapacity => MaxCapacity;
    /**
     * Returns the string representation of this buffer.  This method does not
     * necessarily return the whole content of the buffer but returns
     * the values of the key properties such as {@link #readerIndex()},
     * {@link #writerIndex()} and {@link #capacity()}.
     */
    public override string ToString()
    {
        string name = StringUtil.SimpleClassName(this);
        if (ReferenceCount == 0) return name + "(freed)";
        // CLR: invariant formatting preserves Java's decimal metadata across caller cultures.
        string result = FormattableString.Invariant($"{name}(ridx: {_readerIndex}, widx: {_writerIndex}, cap: {Capacity}");
        int maximum = DiagnosticMaxCapacity;
        if (maximum != int.MaxValue) result += FormattableString.Invariant($"/{maximum}");
        ByteBuf unwrapped = Unwrap();
        if (unwrapped != null) result += ", unwrapped: " + unwrapped;
        return result + ')';
    }
}
