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
using Netty.NET.Common.Concurrent;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * A pool of {@link Constant}s.
 *
 * @param <T> the type of the constant
 */
public abstract class ConstantPool<T> where T : IConstant<T>
{
    private readonly ConcurrentDictionary<string, T> _constants = new ConcurrentDictionary<string, T>();

    private readonly AtomicInteger _nextId = new AtomicInteger(1);

    /**
     * Shortcut of {@link #valueOf(string) valueOf(firstNameComponent.getName() + "#" + secondNameComponent)}.
     */
    public T valueOf(Type firstNameComponent, string secondNameComponent)
    {
        return valueOf(
            checkNotNull(firstNameComponent, "firstNameComponent").Name +
            '#' +
            checkNotNull(secondNameComponent, "secondNameComponent"));
    }

    /**
     * Returns the {@link Constant} which is assigned to the specified {@code name}.
     * If there's no such {@link Constant}, a new one will be created and returned.
     * Once created, the subsequent calls with the same {@code name} will always return the previously created one
     * (i.e. singleton.)
     *
     * @param name the name of the {@link Constant}
     */
    public T valueOf(string name)
    {
        return getOrCreate(checkNonEmpty(name, "name"));
    }

    /**
     * Get existing constant by name or creates new one if not exists. Threadsafe
     *
     * @param name the name of the {@link Constant}
     */
    private T getOrCreate(string name)
    {
        return _constants.GetOrAdd(name, k => newConstant(nextId(), name));
    }

    /**
     * Returns {@code true} if a {@link AttributeKey} exists for the given {@code name}.
     */
    public bool exists(string name)
    {
        return _constants.ContainsKey(checkNonEmpty(name, "name"));
    }

    /**
     * Creates a new {@link Constant} for the given {@code name} or fail with an
     * {@link ArgumentException} if a {@link Constant} for the given {@code name} exists.
     */
    public T newInstance(string name)
    {
        return createOrThrow(checkNonEmpty(name, "name"));
    }

    /**
     * Creates constant by name or throws exception. Threadsafe
     *
     * @param name the name of the {@link Constant}
     */
    private T createOrThrow(string name)
    {
        _constants.TryGetValue(name, out var constant);
        if (constant == null)
        {
            T tempConstant = newConstant(nextId(), name);
            bool added = _constants.TryAdd(name, tempConstant);
            if (added)
            {
                return tempConstant;
            }
        }

        throw new ArgumentException(($"'{name}' is already in use"));
    }

    protected abstract T newConstant(int id, string name);

    public int nextId()
    {
        return _nextId.getAndIncrement();
    }
}