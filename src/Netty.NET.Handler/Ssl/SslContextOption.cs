/*
 * Copyright 2021 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

// CLR: Java keeps one ConstantPool<SslContextOption<Object>> for every T and casts its constants unchecked, which
// erasure permits. Static fields of a C# generic class belong to each closed type and a constant created for one T is not
// an SslContextOption<U>, so the pool, the static valueOf/exists methods and the AbstractConstant base live on this
// non-generic SslContextOption, as Common AttributeKey keeps its registry ("Java shares static fields across all uses of
// AttributeKey<T>. CLR closed generic types do not, so names and IDs must live in a non-generic registry",
// src/Netty.NET.Common/AttributeKey.cs). The pool is a ConcurrentDictionary of name to option whose first ValueOf<T>
// fixes T; ValueOf<U> of the same name with another U throws ArgumentException where Java returns the same object.
// Class.getName() is Type.FullName, as ConstantPool.ValueOf(Type, string) uses it (src/Netty.NET.Common/ConstantPool.cs).
public abstract class SslContextOption : AbstractConstant<SslContextOption>
{

    private static readonly ConcurrentDictionary<string, SslContextOption> pool =
            new ConcurrentDictionary<string, SslContextOption>();
    private static int nextId;

    /**
     * Returns the {@link SslContextOption} of the specified name.
     */
    public static SslContextOption<T> ValueOf<T>(string name)
    {
        SslContextOption option = pool.GetOrAdd(ObjectUtil.CheckNonEmpty(name, "name"),
                n => new SslContextOption<T>(NextId(), n));
        return option as SslContextOption<T> ?? throw new ArgumentException(
                "'" + name + "' is already in use with a different value type", nameof(name));
    }

    /**
     * Shortcut of {@link #valueOf(String) valueOf(firstNameComponent.getName() + "#" + secondNameComponent)}.
     */
    public static SslContextOption<T> ValueOf<T>(Type firstNameComponent, string secondNameComponent)
    {
        ObjectUtil.CheckNotNull(firstNameComponent, "firstNameComponent");
        ObjectUtil.CheckNotNull(secondNameComponent, "secondNameComponent");

        string firstName = firstNameComponent.FullName ?? throw new ArgumentException(
                "A type with a full name is required.", nameof(firstNameComponent));
        return ValueOf<T>(firstName + '#' + secondNameComponent);
    }

    /**
     * Returns {@code true} if a {@link SslContextOption} exists for the given {@code name}.
     */
    public static bool Exists(string name)
    {
        return pool.ContainsKey(ObjectUtil.CheckNonEmpty(name, "name"));
    }

    // CLR: ConstantPool.nextId() of the shared pool.
    internal static int NextId()
    {
        return Interlocked.Increment(ref nextId);
    }

    // CLR: only SslContextOption<T> derives from the non-generic base.
    private protected SslContextOption(int id, string name) : base(id, name)
    {
    }
}

/**
 * A {@link SslContextOption} allows to configure a {@link SslContext} in a type-safe
 * way. Which {@link SslContextOption} is supported depends on the actual implementation
 * of {@link SslContext} and may depend on the nature of the SSL implementation it belongs
 * to.
 *
 * @param <T>   the type of the value which is valid for the {@link SslContextOption}
 */
public class SslContextOption<T> : SslContextOption
{

    /**
     * Creates a new {@link SslContextOption} with the specified unique {@code name}.
     */
    // CLR: internal instead of private, so the pool on the non-generic SslContextOption can call it; no other assembly
    // can.
    internal SslContextOption(int id, string name) : base(id, name)
    {
    }

    /**
     * Should be used by sub-classes.
     *
     * @param name the name of the option
     */
    protected SslContextOption(string name) : this(NextId(), name)
    {
    }

    /**
     * Validate the value which is set for the {@link SslContextOption}. Sub-classes
     * may override this for special checks.
     */
    // CLR: ObjectUtil.checkNotNull(value, "value") takes a reference type; T may be a value type, which is never null,
    // so the null check is written out with the ArgumentNullException that CheckNotNull throws.
    public virtual void Validate(T value)
    {
        if (value == null)
        {
            throw new ArgumentNullException("value");
        }
    }
}
