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
 * Key which can be used to access {@link IAttribute} out of the {@link IAttributeMap}. Be aware that it is not be
 * possible to have multiple keys with the same name.
 *
 * @param <T>   the type of the {@link IAttribute} which can be accessed via this {@link AttributeKey}.
 */
//@SuppressWarnings("UnusedDeclaration") // 'T' is used only at compile time
public class AttributeKey<T> : AbstractConstant<AttributeKey<T>>
{
    private static readonly ConstantPool<AttributeKey<T>> _pool = new AttributeConstantPool<T>();

    /**
     * Returns the singleton instance of the {@link AttributeKey} which has the specified {@code name}.
     */
    public static AttributeKey<T> valueOf(string name)
    {
        return _pool.valueOf(name);
    }

    /**
     * Returns {@code true} if a {@link AttributeKey} exists for the given {@code name}.
     */
    public static bool exists(string name)
    {
        return _pool.exists(name);
    }

    /**
     * Creates a new {@link AttributeKey} for the given {@code name} or fail with an
     * {@link ArgumentException} if a {@link AttributeKey} for the given {@code name} exists.
     */
    public static AttributeKey<T> newInstance(string name)
    {
        return _pool.newInstance(name);
    }

    public static AttributeKey<T> valueOf(Type firstNameComponent, string secondNameComponent)
    {
        return _pool.valueOf(firstNameComponent, secondNameComponent);
    }

    internal AttributeKey(int id, string name)
        : base(id, name)
    {
    }
}