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
using System.Runtime.CompilerServices;
using System.Threading;

namespace Netty.NET.Common;

/**
 * Base implementation of {@link Constant}.
 */
public abstract class AbstractConstant<T> : IConstant<T> 
    where T : AbstractConstant<T>
{
    private readonly int _id;
    private readonly string _name;
    private readonly long _uniquifier;

    /**
     * Creates a new instance.
     */
    protected AbstractConstant(int id, string name)
    {
        _id = id;
        _name = name;
        _uniquifier = ConstantIdentitySequence.Next();
    }

    public string Name => _name;

    public int Id => _id;

    public sealed override string ToString()
    {
        return Name;
    }

    // Java final Object identity methods cannot be replaced with value equality
    // by a subclass. CLR sealed overrides keep that contract for constant keys.
    public sealed override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public sealed override bool Equals(object obj) => ReferenceEquals(this, obj);

    public int CompareTo(T o)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (ReferenceEquals(this, o))
        {
            return 0;
        }

        AbstractConstant<T> other = o;
        // Native comparison avoids subtraction overflow violating antisymmetry.
        // CLR identity hashes differ from JVM hashes, so numeric hash order is
        // runtime-local; identity and a nonzero order between different keys remain.
        int returnCode = GetHashCode().CompareTo(other.GetHashCode());
        if (returnCode != 0)
        {
            return returnCode;
        }

        if (_uniquifier < other._uniquifier)
        {
            return -1;
        }

        if (_uniquifier > other._uniquifier)
        {
            return 1;
        }

        throw new InvalidOperationException("failed to compare two different constants");
    }
}

// Java erased generics have one static generator for all AbstractConstant<T>.
// CLR closed generic statics would duplicate it; use one native atomic counter.
internal static class ConstantIdentitySequence
{
    private static long _next;

    internal static long Next() => unchecked(Interlocked.Increment(ref _next) - 1);
}
