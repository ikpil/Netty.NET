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

namespace Netty.NET.Common;

/**
 * A special {@link Error} which is used to signal some state or request by throwing it.
 * {@link Signal} has an empty stack trace and has no cause to save the instantiation overhead.
 */
public sealed class Signal : Exception, IConstant<Signal>
{
    private static readonly ConstantPool<Signal> _pool = new SignalConstantPool();

    /**
     * Returns the {@link Signal} of the specified name.
     */
    public static Signal valueOf(string name)
    {
        return _pool.valueOf(name);
    }

    /**
     * Shortcut of {@link #valueOf(string) valueOf(firstNameComponent.getName() + "#" + secondNameComponent)}.
     */
    public static Signal valueOf(Type firstNameComponent, string secondNameComponent)
    {
        return _pool.valueOf(firstNameComponent, secondNameComponent);
    }

    private readonly SignalConstant constant;

    /**
     * Creates a new {@link Signal} with the specified {@code name}.
     */
    public Signal(int id, string name)
    {
        constant = new SignalConstant(id, name);
    }

    /**
     * Check if the given {@link Signal} is the same as this instance. If not an {@link InvalidOperationException} will
     * be thrown.
     */
    public void expect(Signal signal)
    {
        if (!ReferenceEquals(this, signal))
        {
            throw new InvalidOperationException("unexpected signal: " + signal);
        }
    }

    public int id()
    {
        return constant.id();
    }

    public string name()
    {
        return constant.name();
    }

    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj);
    }

    public override int GetHashCode()
    {
        //return System.identityHashCode(this);
        return id();
    }

    public int CompareTo(Signal other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        return constant.CompareTo(other.constant);
    }

    public override string ToString()
    {
        return name();
    }
}