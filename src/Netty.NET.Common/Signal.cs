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
    public static Signal ValueOf(string name)
    {
        return _pool.ValueOf(name);
    }

    /**
     * Shortcut of {@link #valueOf(String) valueOf(firstNameComponent.getName() + "#" + secondNameComponent)}.
     */
    public static Signal ValueOf(Type firstNameComponent, string secondNameComponent)
    {
        return _pool.ValueOf(firstNameComponent, secondNameComponent);
    }

    private readonly SignalConstant constant;

    /**
     * Creates a new {@link Signal} with the specified {@code name}.
     */
    private Signal(int id, string name)
    {
        constant = new SignalConstant(id, name);
    }

    /**
     * Check if the given {@link Signal} is the same as this instance. If not an {@link IllegalStateException} will
     * be thrown.
     */
    public void Expect(Signal signal)
    {
        if (!ReferenceEquals(this, signal))
        {
            throw new InvalidOperationException("unexpected signal: " + (signal?.ToString() ?? "null"));
        }
    }

    // Suppress a warning since the method doesn't need synchronization
    // CLR: Java initCause has no native counterpart. Only the private registry
    // constructor creates this marker and it never supplies an InnerException.

    // Suppress a warning since the method doesn't need synchronization
    // CLR: expose empty signal diagnostics as Java fillInStackTrace does. The
    // runtime still captures throw state; this does not eliminate CLR throw cost.
    public override string StackTrace => string.Empty;

    public int Id()
    {
        return constant.Id();
    }

    public string Name()
    {
        return constant.Name();
    }

    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public int CompareTo(Signal other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        return constant.CompareTo(other.constant);
    }

    public override string ToString()
    {
        return Name();
    }

    private sealed class SignalConstantPool : ConstantPool<Signal>
    {
        protected override Signal NewConstant(int id, string name) => new Signal(id, name);
    }

    private sealed class SignalConstant : AbstractConstant<SignalConstant>
    {
        internal SignalConstant(int id, string name) : base(id, name)
        {
        }
    }
}
