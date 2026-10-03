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

using System;
using System.Collections.Concurrent;
using System.Threading;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * A pool of {@link Constant}s.
 *
 * @param <T> the type of the constant
 */
// CLR adaptation: singleton identity and the absence sentinel require reference
// constants, matching Java's T extends Constant<T> reference-type bound.
public abstract class ConstantPool<T> where T : class, IConstant<T>
{
    private readonly ConcurrentDictionary<string, T> _constants = new ConcurrentDictionary<string, T>();

    private int _nextId = 1;

    /**
     * Shortcut of {@link #valueOf(String) valueOf(firstNameComponent.getName() + "#" + secondNameComponent)}.
     */
    public T ValueOf(Type firstNameComponent, string secondNameComponent)
    {
        return ValueOf(
            CheckNotNull(firstNameComponent, "firstNameComponent").FullName +
            '#' +
            CheckNotNull(secondNameComponent, "secondNameComponent"));
    }

    /**
     * Returns the {@link Constant} which is assigned to the specified {@code name}.
     * If there's no such {@link Constant}, a new one will be created and returned.
     * Once created, the subsequent calls with the same {@code name} will always return the previously created one
     * (i.e. singleton.)
     *
     * @param name the name of the {@link Constant}
     */
    public T ValueOf(string name)
    {
        return GetOrCreate(CheckNonEmpty(name, "name"));
    }

    /**
     * Get existing constant by name or creates new one if not exists. Threadsafe
     *
     * @param name the name of the {@link Constant}
     */
    private T GetOrCreate(string name)
    {
        // CLR adaptation: competing factories may create unused constants, just
        // as upstream get/newConstant/putIfAbsent does. Return the published value,
        // not the factory's temporary instance; ID gaps are allowed.
        return _constants.GetOrAdd(name, k => NewConstant(NextId(), name));
    }

    /**
     * Returns {@code true} if a {@link AttributeKey} exists for the given {@code name}.
     */
    public bool Exists(string name)
    {
        return _constants.ContainsKey(CheckNonEmpty(name, "name"));
    }

    /**
     * Creates a new {@link Constant} for the given {@code name} or fail with an
     * {@link IllegalArgumentException} if a {@link Constant} for the given {@code name} exists.
     */
    public T NewInstance(string name)
    {
        return CreateOrThrow(CheckNonEmpty(name, "name"));
    }

    /**
     * Creates constant by name or throws exception. Threadsafe
     *
     * @param name the name of the {@link Constant}
     */
    private T CreateOrThrow(string name)
    {
        _constants.TryGetValue(name, out var constant);
        if (constant == null)
        {
            T tempConstant = NewConstant(NextId(), name);
            bool added = _constants.TryAdd(name, tempConstant);
            if (added)
            {
                return tempConstant;
            }
        }

        throw new ArgumentException(($"'{name}' is already in use"));
    }

    protected abstract T NewConstant(int id, string name);

    public int NextId()
    {
        // Preserve Java getAndIncrement, including unchecked integer wrapping,
        // using the CLR primitive rather than an AtomicInteger compatibility object.
        return unchecked(Interlocked.Increment(ref _nextId) - 1);
    }
}
