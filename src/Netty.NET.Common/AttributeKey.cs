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
using System.Collections.Concurrent;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public class AttributeKey
{
    // Java shares static fields across all uses of AttributeKey<T>. CLR closed
    // generic types do not, so names and IDs must live in a non-generic registry.
    private static readonly ConcurrentDictionary<string, IAttributeKey> Keys = new();
    private static int _nextId;

    public static bool exists(string name)
    {
        return Keys.ContainsKey(ObjectUtil.checkNonEmpty(name, nameof(name)));
    }

    public static bool exists<T>(string name) where T : class
    {
        return exists(name);
    }
    
    public static AttributeKey<T> valueOf<T>(string name) where T : class
    {
        ObjectUtil.checkNonEmpty(name, nameof(name));
        IAttributeKey key = Keys.GetOrAdd(name,
            n => new AttributeKey<T>(Interlocked.Increment(ref _nextId), n));
        return key as AttributeKey<T> ?? throw new ArgumentException(
            "Attribute key '" + name + "' is already registered with a different value type.", nameof(name));
    }

    public static AttributeKey<T> valueOf<T>(Type firstNameComponent, string secondNameComponent) where T : class
    {
        return AttributeKey<T>.valueOf(firstNameComponent, secondNameComponent);
    }
    
    public static AttributeKey<T> newInstance<T>(string name) where T : class
    {
        ObjectUtil.checkNonEmpty(name, nameof(name));
        var key = new AttributeKey<T>(Interlocked.Increment(ref _nextId), name);
        if (!Keys.TryAdd(name, key))
        {
            throw new ArgumentException("'" + name + "' is already in use", nameof(name));
        }
        return key;
    }
}

/**
 * Key which can be used to access {@link Attribute} out of the {@link AttributeMap}. Be aware that it is not be
 * possible to have multiple keys with the same name.
 *
 * @param <T>   the type of the {@link Attribute} which can be accessed via this {@link AttributeKey}.
 */
// 'T' is used only at compile time
// CLR adaptation: the registry is shared, but generic value types remain checked.
public class AttributeKey<T> : AbstractConstant<AttributeKey<T>>, IAttributeKey where T : class
{
    /**
     * Returns the singleton instance of the {@link AttributeKey} which has the specified {@code name}.
     */
    public static AttributeKey<T> valueOf(string name)
    {
        return AttributeKey.valueOf<T>(name);
    }

    /**
     * Returns {@code true} if a {@link AttributeKey} exists for the given {@code name}.
     */
    public static bool exists(string name)
    {
        return AttributeKey.exists(name);
    }

    /**
     * Creates a new {@link AttributeKey} for the given {@code name} or fail with an
     * {@link IllegalArgumentException} if a {@link AttributeKey} for the given {@code name} exists.
     */
    public static AttributeKey<T> newInstance(string name)
    {
        return AttributeKey.newInstance<T>(name);
    }

    public static AttributeKey<T> valueOf(Type firstNameComponent, string secondNameComponent)
    {
        ObjectUtil.checkNotNull(firstNameComponent, nameof(firstNameComponent));
        ObjectUtil.checkNotNull(secondNameComponent, nameof(secondNameComponent));
        return valueOf(firstNameComponent.FullName + '#' + secondNameComponent);
    }

    internal AttributeKey(int id, string name)
        : base(id, name)
    {
    }
}
