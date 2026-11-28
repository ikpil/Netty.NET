/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec;

// CLR: the static members of DefaultHeaders (HASH_CODE_SEED and the nested NameValidator/ValueValidator interfaces)
// do not depend on K, V or T, but a type nested in a C# generic class is parameterized by the class's type
// parameters, so DefaultHeaders<A, B, C>.INameValidator<K> would be a different type for every A, B, C. They are
// declared on this non-generic DefaultHeaders class, which keeps the Java spelling DefaultHeaders.NameValidator<K>
// as DefaultHeaders.INameValidator<K>.
public static class DefaultHeaders
{
    /**
     * Constant used to seed the hash code generation. Could be anything but this was borrowed from murmur3.
     */
    internal const int HASH_CODE_SEED = unchecked((int) 0xc2b2ae35);

    public interface INameValidator<K>
    {
        /**
         * Verify that {@code name} is valid.
         * @param name The name to validate.
         * @throws RuntimeException if {@code name} is not valid.
         */
        void ValidateName(K name);

        // CLR: the raw-typed constant is one instance per closed type INameValidator<K> (interfaces may declare
        // static fields, https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/interface), so no
        // unchecked cast is needed where it is used.
        public static readonly INameValidator<K> NOT_NULL = new NotNullNameValidator<K>();
    }

    public interface IValueValidator<V>
    {
        /**
         * Validate the given value. If the validation fails, then an implementation specific runtime exception may be
         * thrown.
         *
         * @param value The value to validate.
         */
        void Validate(V value);

        // CLR: the ValueValidator<?> constant is one instance per closed type IValueValidator<V>, which replaces the
        // (ValueValidator<V>) ValueValidator.NO_VALIDATION cast.
        public static readonly IValueValidator<V> NO_VALIDATION = new NoValidationValueValidator<V>();
    }

    private sealed class NotNullNameValidator<K> : INameValidator<K>
    {
        public void ValidateName(K name)
        {
            ObjectUtil.CheckNotNull((object) name, "name");
        }
    }

    private sealed class NoValidationValueValidator<V> : IValueValidator<V>
    {
        public void Validate(V value)
        {
        }
    }
}

/**
 * Default implementation of {@link Headers};
 *
 * @param <K> the type of the header name.
 * @param <V> the type of the header value.
 * @param <T> the type to use for return values when the intention is to return {@code this} object.
 */
// CLR: HashingStrategy<K> is IEqualityComparer<K> (GetHashCode/Equals) and JAVA_HASHER is
// EqualityComparer<K>.Default. BiPredicate<? super V, ? super V> is Func<V, V, bool>, whose parameters are
// contravariant. Java methods are virtual unless final, so the non-final public and protected members are virtual.
// Java protected (subclasses and the package) is protected internal. RuntimeException is Exception and
// IllegalArgumentException is ArgumentException (see IsIllegalArgument). IEquatable<T> routes generic equality
// (EqualityComparer<T>.Default, which xUnit consults before treating the headers as an IEnumerable of entries) to
// the Java equals(Object) body.
public class DefaultHeaders<K, V, T> : IHeaders<K, V, T>, IEquatable<T>
    where K : class
    where V : class
    where T : IHeaders<K, V, T>
{
    private readonly HeaderEntry[] entries;
    protected readonly HeaderEntry head;

    private readonly byte hashMask;
    private readonly IValueConverter<V> valueConverter;
    private readonly DefaultHeaders.INameValidator<K> nameValidator;
    private readonly DefaultHeaders.IValueValidator<V> valueValidator;
    private readonly IEqualityComparer<K> hashingStrategy;
    internal int size;

    public DefaultHeaders(IValueConverter<V> valueConverter) : this(EqualityComparer<K>.Default, valueConverter)
    {
    }

    public DefaultHeaders(IValueConverter<V> valueConverter, DefaultHeaders.INameValidator<K> nameValidator)
        : this(EqualityComparer<K>.Default, valueConverter, nameValidator)
    {
    }

    public DefaultHeaders(IEqualityComparer<K> nameHashingStrategy, IValueConverter<V> valueConverter)
        : this(nameHashingStrategy, valueConverter, DefaultHeaders.INameValidator<K>.NOT_NULL)
    {
    }

    public DefaultHeaders(IEqualityComparer<K> nameHashingStrategy,
            IValueConverter<V> valueConverter, DefaultHeaders.INameValidator<K> nameValidator)
        : this(nameHashingStrategy, valueConverter, nameValidator, 16)
    {
    }

    /**
     * Create a new instance.
     * @param nameHashingStrategy Used to hash and equality compare names.
     * @param valueConverter Used to convert values to/from native types.
     * @param nameValidator Used to validate name elements.
     * @param arraySizeHint A hint as to how large the hash data structure should be.
     * The next positive power of two will be used. An upper bound may be enforced.
     */
    public DefaultHeaders(IEqualityComparer<K> nameHashingStrategy,
                          IValueConverter<V> valueConverter, DefaultHeaders.INameValidator<K> nameValidator,
                          int arraySizeHint)
        : this(nameHashingStrategy, valueConverter, nameValidator, arraySizeHint,
                DefaultHeaders.IValueValidator<V>.NO_VALIDATION)
    {
    }

    /**
     * Create a new instance.
     * @param nameHashingStrategy Used to hash and equality compare names.
     * @param valueConverter Used to convert values to/from native types.
     * @param nameValidator Used to validate name elements.
     * @param arraySizeHint A hint as to how large the hash data structure should be.
     * The next positive power of two will be used. An upper bound may be enforced.
     * @param valueValidator The validation strategy for entry values.
     */
    public DefaultHeaders(IEqualityComparer<K> nameHashingStrategy, IValueConverter<V> valueConverter,
                          DefaultHeaders.INameValidator<K> nameValidator, int arraySizeHint,
                          DefaultHeaders.IValueValidator<V> valueValidator)
    {
        this.valueConverter = ObjectUtil.CheckNotNull(valueConverter, "valueConverter");
        this.nameValidator = ObjectUtil.CheckNotNull(nameValidator, "nameValidator");
        hashingStrategy = ObjectUtil.CheckNotNull(nameHashingStrategy, "nameHashingStrategy");
        this.valueValidator = ObjectUtil.CheckNotNull(valueValidator, "valueValidator");
        // Enforce a bound of [2, 128] because hashMask is a byte. The max possible value of hashMask is one less
        // than the length of this array, and we want the mask to be > 0.
        entries = new HeaderEntry[MathUtil.FindNextPositivePowerOfTwo(Math.Max(2, Math.Min(arraySizeHint, 128)))];
        hashMask = (byte) (entries.Length - 1);
        head = new HeaderEntry();
    }

    /**
     * Returns {@code true} if there exists a header with the given {@code name} for which the
     * supplied {@code valuePredicate} returns {@code true} when invoked with the stored header
     * value as the first argument and {@code predicateArg} as the second argument.
     * <p>
     * Matching is performed by invoking {@code valuePredicate.test(storedValue, predicateArg)}
     * on each stored header value for {@code name}.
     *
     * @param name           the header name to search for (must not be {@code null})
     * @param predicateArg   argument passed as the second parameter to {@code valuePredicate} (may be {@code null})
     * @param valuePredicate predicate used to test stored header values (must not be {@code null})
     */
    public virtual bool ContainsAny(K name, V predicateArg, Func<V, V, bool> valuePredicate)
    {
        ObjectUtil.CheckNotNull(name, "name");
        ObjectUtil.CheckNotNull(valuePredicate, "valuePredicate");

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);
        HeaderEntry e = entries[i];

        while (e != null)
        {
            if (e.hash == h && hashingStrategy.Equals(name, e.key) && valuePredicate(e.value, predicateArg))
            {
                return true;
            }

            e = e.next;
        }

        return false;
    }

    public virtual V Get(K name)
    {
        ObjectUtil.CheckNotNull(name, "name");

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);
        HeaderEntry e = entries[i];
        V value = null;

        // loop until the first header was found
        while (e != null)
        {
            if (e.hash == h && hashingStrategy.Equals(name, e.key))
            {
                value = e.value;
            }

            e = e.next;
        }

        return value;
    }

    public virtual V Get(K name, V defaultValue)
    {
        V value = Get(name);
        if (value == null)
        {
            return defaultValue;
        }

        return value;
    }

    public virtual V GetAndRemove(K name)
    {
        int h = hashingStrategy.GetHashCode(name);
        return Remove0(h, Index(h), ObjectUtil.CheckNotNull(name, "name"));
    }

    public virtual V GetAndRemove(K name, V defaultValue)
    {
        V value = GetAndRemove(name);
        if (value == null)
        {
            return defaultValue;
        }

        return value;
    }

    // CLR: Collections.emptyList() and Collections.singletonList(value) are immutable; ReadOnlyCollection<V>.Empty
    // ("The returned instance is immutable and will always be empty",
    // https://learn.microsoft.com/dotnet/api/system.collections.objectmodel.readonlycollection-1.empty) and a
    // ReadOnlyCollection<V> over a one-element array keep that. The ArrayList result stays a mutable List<V>.
    public virtual IList<V> GetAll(K name)
    {
        ObjectUtil.CheckNotNull(name, "name");

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);

        List<V> values = null;
        V value = null;
        HeaderEntry e = entries[i];

        while (e != null)
        {
            if (e.hash == h && hashingStrategy.Equals(name, e.key))
            {
                if (value == null)
                {
                    value = e.GetValue();
                }
                else
                {
                    if (values == null)
                    {
                        values = new List<V>(2);
                        values.Add(value);
                    }

                    values.Add(e.GetValue());
                }
            }

            e = e.next;
        }

        if (values == null)
        {
            if (value == null)
            {
                return ReadOnlyCollection<V>.Empty;
            }

            return new ReadOnlyCollection<V>(new V[] { value });
        }

        Debug.Assert(values.Count > 1);
        values.Reverse();

        return values;
    }

    /**
     * Equivalent to {@link #getAll(Object)} but no intermediate list is generated.
     * @param name the name of the header to retrieve
     * @return an {@link Iterator} of header values corresponding to {@code name}.
     */
    public virtual IIterator<V> ValueIterator(K name)
    {
        return new ValueIterator_(this, name);
    }

    public virtual IList<V> GetAllAndRemove(K name)
    {
        IList<V> all = GetAll(name);
        Remove(name);
        return all;
    }

    public virtual bool Contains(K name)
    {
        return Get(name) != null;
    }

    public virtual bool ContainsObject(K name, object value)
    {
        return Contains(name, FromObject(name, value));
    }

    public virtual bool ContainsBoolean(K name, bool value)
    {
        return Contains(name, FromBoolean(name, value));
    }

    public virtual bool ContainsByte(K name, sbyte value)
    {
        return Contains(name, FromByte(name, value));
    }

    public virtual bool ContainsChar(K name, char value)
    {
        return Contains(name, FromChar(name, value));
    }

    public virtual bool ContainsShort(K name, short value)
    {
        return Contains(name, FromShort(name, value));
    }

    public virtual bool ContainsInt(K name, int value)
    {
        return Contains(name, FromInt(name, value));
    }

    public virtual bool ContainsLong(K name, long value)
    {
        return Contains(name, FromLong(name, value));
    }

    public virtual bool ContainsFloat(K name, float value)
    {
        return Contains(name, FromFloat(name, value));
    }

    public virtual bool ContainsDouble(K name, double value)
    {
        return Contains(name, FromDouble(name, value));
    }

    public virtual bool ContainsTimeMillis(K name, long value)
    {
        return Contains(name, FromTimeMillis(name, value));
    }

    public virtual bool Contains(K name, V value)
    {
        return Contains(name, value, EqualityComparer<V>.Default);
    }

    public bool Contains(K name, V value, IEqualityComparer<V> valueHashingStrategy)
    {
        ObjectUtil.CheckNotNull(name, "name");

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);
        HeaderEntry e = entries[i];

        while (e != null)
        {
            if (e.hash == h && hashingStrategy.Equals(name, e.key) && valueHashingStrategy.Equals(value, e.value))
            {
                return true;
            }

            e = e.next;
        }

        return false;
    }

    public virtual int Size()
    {
        return size;
    }

    public virtual bool IsEmpty()
    {
        return head == head.after;
    }

    // CLR: Collections.emptySet() is the immutable ImmutableHashSet<K>.Empty, whose ISet<K> mutators throw
    // NotSupportedException; LinkedHashSet is Netty.NET.Common.Collections.LinkedHashSet, which keeps insertion order
    // and, as Java's, compares names with their own Equals/GetHashCode.
    public virtual ISet<K> Names()
    {
        if (IsEmpty())
        {
            return ImmutableHashSet<K>.Empty;
        }

        ISet<K> names = new LinkedHashSet<K>(Size());
        HeaderEntry e = head.after;

        while (e != head)
        {
            names.Add(e.GetKey());
            e = e.after;
        }

        return names;
    }

    public virtual T Add(K name, V value)
    {
        ValidateName(nameValidator, true, name);
        ValidateValue(valueValidator, name, value);
        ObjectUtil.CheckNotNull(value, "value");

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);
        Add0(h, i, name, value);

        return ThisT();
    }

    public virtual T Add(K name, IEnumerable<V> values)
    {
        ValidateName(nameValidator, true, name);
        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);

        foreach (V v in values)
        {
            ValidateValue(valueValidator, name, v);
            Add0(h, i, name, v);
        }

        return ThisT();
    }

    public virtual T Add(K name, params V[] values)
    {
        ValidateName(nameValidator, true, name);
        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);

        foreach (V v in values)
        {
            ValidateValue(valueValidator, name, v);
            Add0(h, i, name, v);
        }

        return ThisT();
    }

    public virtual T AddObject(K name, object value)
    {
        return Add(name, FromObject(name, value));
    }

    public virtual T AddObject(K name, IEnumerable<object> values)
    {
        foreach (object value in values)
        {
            AddObject(name, value);
        }

        return ThisT();
    }

    public virtual T AddObject(K name, params object[] values)
    {
        foreach (object value in values)
        {
            AddObject(name, value);
        }

        return ThisT();
    }

    public virtual T AddInt(K name, int value)
    {
        return Add(name, FromInt(name, value));
    }

    public virtual T AddLong(K name, long value)
    {
        return Add(name, FromLong(name, value));
    }

    public virtual T AddDouble(K name, double value)
    {
        return Add(name, FromDouble(name, value));
    }

    public virtual T AddTimeMillis(K name, long value)
    {
        return Add(name, FromTimeMillis(name, value));
    }

    public virtual T AddChar(K name, char value)
    {
        return Add(name, FromChar(name, value));
    }

    public virtual T AddBoolean(K name, bool value)
    {
        return Add(name, FromBoolean(name, value));
    }

    public virtual T AddFloat(K name, float value)
    {
        return Add(name, FromFloat(name, value));
    }

    public virtual T AddByte(K name, sbyte value)
    {
        return Add(name, FromByte(name, value));
    }

    public virtual T AddShort(K name, short value)
    {
        return Add(name, FromShort(name, value));
    }

    public virtual T Add<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>
    {
        if (ReferenceEquals(headers, this))
        {
            throw new ArgumentException("can't add to itself.");
        }

        AddImpl(headers);

        return ThisT();
    }

    // CLR: instanceof DefaultHeaders with the unchecked cast to DefaultHeaders<? extends K, ? extends V, T> is a
    // pattern match on DefaultHeaders<K, V, T2>; the other instantiation's private and protected members are
    // accessible inside this class declaration, as in Java.
    protected virtual void AddImpl<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>
    {
        if (headers is DefaultHeaders<K, V, T2>)
        {
            DefaultHeaders<K, V, T2> defaultHeaders = (DefaultHeaders<K, V, T2>) headers;
            DefaultHeaders<K, V, T2>.HeaderEntry e = defaultHeaders.head.after;

            if (defaultHeaders.hashingStrategy == hashingStrategy &&
                    defaultHeaders.nameValidator == nameValidator)
            {
                // Fastest copy
                while (e != defaultHeaders.head)
                {
                    Add0(e.hash, Index(e.hash), e.key, e.value);
                    e = e.after;
                }
            }
            else
            {
                // Fast copy
                while (e != defaultHeaders.head)
                {
                    Add(e.key, e.value);
                    e = e.after;
                }
            }
        }
        else
        {
            // Slow copy
            foreach (IEntry<K, V> header in headers)
            {
                Add(header.GetKey(), header.GetValue());
            }
        }
    }

    public virtual T Set(K name, V value)
    {
        ValidateName(nameValidator, false, name);
        ValidateValue(valueValidator, name, value);
        ObjectUtil.CheckNotNull(value, "value");

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);
        Remove0(h, i, name);
        Add0(h, i, name, value);

        return ThisT();
    }

    public virtual T Set(K name, IEnumerable<V> values)
    {
        ValidateName(nameValidator, false, name);
        ObjectUtil.CheckNotNull(values, "values");

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);

        Remove0(h, i, name);

        foreach (V v in values)
        {
            if (v == null)
            {
                break;
            }

            ValidateValue(valueValidator, name, v);
            Add0(h, i, name, v);
        }

        return ThisT();
    }

    public virtual T Set(K name, params V[] values)
    {
        ValidateName(nameValidator, false, name);
        ObjectUtil.CheckNotNull(values, "values");

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);

        Remove0(h, i, name);

        foreach (V v in values)
        {
            if (v == null)
            {
                break;
            }

            ValidateValue(valueValidator, name, v);
            Add0(h, i, name, v);
        }

        return ThisT();
    }

    public virtual T SetObject(K name, object value)
    {
        V convertedValue = ObjectUtil.CheckNotNull(FromObject(name, value), "convertedValue");
        return Set(name, convertedValue);
    }

    public virtual T SetObject(K name, IEnumerable<object> values)
    {
        ValidateName(nameValidator, false, name);

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);

        Remove0(h, i, name);

        foreach (object v in values)
        {
            if (v == null)
            {
                break;
            }

            V converted = FromObject(name, v);
            ValidateValue(valueValidator, name, converted);
            Add0(h, i, name, converted);
        }

        return ThisT();
    }

    public virtual T SetObject(K name, params object[] values)
    {
        ValidateName(nameValidator, false, name);

        int h = hashingStrategy.GetHashCode(name);
        int i = Index(h);

        Remove0(h, i, name);

        foreach (object v in values)
        {
            if (v == null)
            {
                break;
            }

            V converted = FromObject(name, v);
            ValidateValue(valueValidator, name, converted);
            Add0(h, i, name, converted);
        }

        return ThisT();
    }

    public virtual T SetInt(K name, int value)
    {
        return Set(name, FromInt(name, value));
    }

    public virtual T SetLong(K name, long value)
    {
        return Set(name, FromLong(name, value));
    }

    public virtual T SetDouble(K name, double value)
    {
        return Set(name, FromDouble(name, value));
    }

    public virtual T SetTimeMillis(K name, long value)
    {
        return Set(name, FromTimeMillis(name, value));
    }

    public virtual T SetFloat(K name, float value)
    {
        return Set(name, FromFloat(name, value));
    }

    public virtual T SetChar(K name, char value)
    {
        return Set(name, FromChar(name, value));
    }

    public virtual T SetBoolean(K name, bool value)
    {
        return Set(name, FromBoolean(name, value));
    }

    public virtual T SetByte(K name, sbyte value)
    {
        return Set(name, FromByte(name, value));
    }

    public virtual T SetShort(K name, short value)
    {
        return Set(name, FromShort(name, value));
    }

    public virtual T Set<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>
    {
        if (!ReferenceEquals(headers, this))
        {
            Clear();
            AddImpl(headers);
        }

        return ThisT();
    }

    public virtual T SetAll<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>
    {
        if (!ReferenceEquals(headers, this))
        {
            foreach (K key in headers.Names())
            {
                Remove(key);
            }

            AddImpl(headers);
        }

        return ThisT();
    }

    public virtual bool Remove(K name)
    {
        return GetAndRemove(name) != null;
    }

    public virtual T Clear()
    {
        Array.Fill(entries, null);
        head.before = head.after = head;
        size = 0;
        return ThisT();
    }

    public virtual IIterator<IEntry<K, V>> Iterator()
    {
        return new HeaderIterator(head);
    }

    public IEnumerator<IEntry<K, V>> GetEnumerator()
    {
        return Iterator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // CLR: catch (RuntimeException ignore) is catch (Exception): C# has no checked exceptions, so this also
    // discards the ParseException that CharSequenceValueConverter.convertToTimeMillis rethrows unchecked in Java (the
    // documented result, null when the value "can't be converted to milliseconds", is returned instead). The unused
    // ignore variable is not declared, as C# warns about an unused catch variable (CS0168).
    public virtual bool? GetBoolean(K name)
    {
        V v = Get(name);

        try
        {
            return v != null ? ToBoolean(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual bool GetBoolean(K name, bool defaultValue)
    {
        bool? v = GetBoolean(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual sbyte? GetByte(K name)
    {
        V v = Get(name);

        try
        {
            return v != null ? ToByte(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual sbyte GetByte(K name, sbyte defaultValue)
    {
        sbyte? v = GetByte(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual char? GetChar(K name)
    {
        V v = Get(name);

        try
        {
            return v != null ? ToChar(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual char GetChar(K name, char defaultValue)
    {
        char? v = GetChar(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual short? GetShort(K name)
    {
        V v = Get(name);

        try
        {
            return v != null ? ToShort(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual short GetShort(K name, short defaultValue)
    {
        short? v = GetShort(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual int? GetInt(K name)
    {
        V v = Get(name);

        try
        {
            return v != null ? ToInt(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual int GetInt(K name, int defaultValue)
    {
        int? v = GetInt(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual long? GetLong(K name)
    {
        V v = Get(name);

        try
        {
            return v != null ? ToLong(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual long GetLong(K name, long defaultValue)
    {
        long? v = GetLong(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual float? GetFloat(K name)
    {
        V v = Get(name);

        try
        {
            return v != null ? ToFloat(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual float GetFloat(K name, float defaultValue)
    {
        float? v = GetFloat(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual double? GetDouble(K name)
    {
        V v = Get(name);

        try
        {
            return v != null ? ToDouble(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual double GetDouble(K name, double defaultValue)
    {
        double? v = GetDouble(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual long? GetTimeMillis(K name)
    {
        V v = Get(name);

        try
        {
            return v != null ? ToTimeMillis(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual long GetTimeMillis(K name, long defaultValue)
    {
        long? v = GetTimeMillis(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual bool? GetBooleanAndRemove(K name)
    {
        V v = GetAndRemove(name);

        try
        {
            return v != null ? ToBoolean(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual bool GetBooleanAndRemove(K name, bool defaultValue)
    {
        bool? v = GetBooleanAndRemove(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual sbyte? GetByteAndRemove(K name)
    {
        V v = GetAndRemove(name);

        try
        {
            return v != null ? ToByte(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual sbyte GetByteAndRemove(K name, sbyte defaultValue)
    {
        sbyte? v = GetByteAndRemove(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual char? GetCharAndRemove(K name)
    {
        V v = GetAndRemove(name);

        try
        {
            return v != null ? ToChar(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual char GetCharAndRemove(K name, char defaultValue)
    {
        char? v = GetCharAndRemove(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual short? GetShortAndRemove(K name)
    {
        V v = GetAndRemove(name);

        try
        {
            return v != null ? ToShort(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual short GetShortAndRemove(K name, short defaultValue)
    {
        short? v = GetShortAndRemove(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual int? GetIntAndRemove(K name)
    {
        V v = GetAndRemove(name);

        try
        {
            return v != null ? ToInt(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual int GetIntAndRemove(K name, int defaultValue)
    {
        int? v = GetIntAndRemove(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual long? GetLongAndRemove(K name)
    {
        V v = GetAndRemove(name);

        try
        {
            return v != null ? ToLong(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual long GetLongAndRemove(K name, long defaultValue)
    {
        long? v = GetLongAndRemove(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual float? GetFloatAndRemove(K name)
    {
        V v = GetAndRemove(name);

        try
        {
            return v != null ? ToFloat(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual float GetFloatAndRemove(K name, float defaultValue)
    {
        float? v = GetFloatAndRemove(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual double? GetDoubleAndRemove(K name)
    {
        V v = GetAndRemove(name);

        try
        {
            return v != null ? ToDouble(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual double GetDoubleAndRemove(K name, double defaultValue)
    {
        double? v = GetDoubleAndRemove(name);
        return v != null ? v.Value : defaultValue;
    }

    public virtual long? GetTimeMillisAndRemove(K name)
    {
        V v = GetAndRemove(name);

        try
        {
            return v != null ? ToTimeMillis(name, v) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public virtual long GetTimeMillisAndRemove(K name, long defaultValue)
    {
        long? v = GetTimeMillisAndRemove(name);
        return v != null ? v.Value : defaultValue;
    }

    // CLR: instanceof Headers with the unchecked cast to Headers<K, V, ?> cannot name the wildcard in a CLR type test;
    // headers that share the self type T (every Netty implementation family, e.g. DefaultHttp2Headers and
    // ReadOnlyHttp2Headers both use Http2Headers) are compared, other objects are not equal.
    public override bool Equals(object o)
    {
        if (!(o is IHeaders<K, V, T>))
        {
            return false;
        }

        return Equals((IHeaders<K, V, T>) o, EqualityComparer<V>.Default);
    }

    public bool Equals(T other)
    {
        return Equals((object) other);
    }

    public override int GetHashCode()
    {
        return HashCode(EqualityComparer<V>.Default);
    }

    /**
     * Test this object for equality against {@code h2}.
     * @param h2 The object to check equality for.
     * @param valueHashingStrategy Defines how values will be compared for equality.
     * @return {@code true} if this object equals {@code h2} given {@code valueHashingStrategy}.
     * {@code false} otherwise.
     */
    public bool Equals<T2>(IHeaders<K, V, T2> h2, IEqualityComparer<V> valueHashingStrategy)
        where T2 : IHeaders<K, V, T2>
    {
        if (h2.Size() != Size())
        {
            return false;
        }

        if (ReferenceEquals(this, h2))
        {
            return true;
        }

        foreach (K name in Names())
        {
            IList<V> otherValues = h2.GetAll(name);
            IList<V> values = GetAll(name);
            if (otherValues.Count != values.Count)
            {
                return false;
            }

            for (int i = 0; i < otherValues.Count; i++)
            {
                if (!valueHashingStrategy.Equals(otherValues[i], values[i]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /**
     * Generate a hash code for this object given a {@link HashingStrategy} to generate hash codes for
     * individual values.
     * @param valueHashingStrategy Defines how values will be hashed.
     */
    public int HashCode(IEqualityComparer<V> valueHashingStrategy)
    {
        int result = DefaultHeaders.HASH_CODE_SEED;

        foreach (K name in Names())
        {
            result = unchecked(31 * result + hashingStrategy.GetHashCode(name));
            IList<V> values = GetAll(name);

            for (int i = 0; i < values.Count; ++i)
            {
                result = unchecked(31 * result + valueHashingStrategy.GetHashCode(values[i]));
            }
        }

        return result;
    }

    public override string ToString()
    {
        return HeadersUtils.ToString(GetType(), Iterator(), Size());
    }

    /**
     * Call out to the given {@link NameValidator} to validate the given name.
     *
     * @param validator the validator to use
     * @param forAdd {@code true } if this validation is for adding to the headers, or {@code false} if this is for
     * setting (overwriting) the given header.
     * @param name the name to validate.
     */
    protected virtual void ValidateName(DefaultHeaders.INameValidator<K> validator, bool forAdd, K name)
    {
        validator.ValidateName(name);
    }

    protected virtual void ValidateValue(DefaultHeaders.IValueValidator<V> validator, K name, V value)
    {
        try
        {
            validator.Validate(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Validation failed for header '" + name + "'", e);
        }
    }

    protected virtual HeaderEntry NewHeaderEntry(int h, K name, V value, HeaderEntry next)
    {
        return new HeaderEntry(h, name, value, next, head);
    }

    protected virtual IValueConverter<V> ValueConverter()
    {
        return valueConverter;
    }

    protected virtual DefaultHeaders.INameValidator<K> NameValidator()
    {
        return nameValidator;
    }

    protected virtual DefaultHeaders.IValueValidator<V> ValueValidator()
    {
        return valueValidator;
    }

    private int Index(int hash)
    {
        return hash & hashMask;
    }

    private void Add0(int h, int i, K name, V value)
    {
        // Update the hash table.
        entries[i] = NewHeaderEntry(h, name, value, entries[i]);
        ++size;
    }

    /**
     * @return the first value inserted whose hash code equals {@code h} and whose name is equal to {@code name}.
     */
    private V Remove0(int h, int i, K name)
    {
        HeaderEntry e = entries[i];
        if (e == null)
        {
            return null;
        }

        V value = null;
        HeaderEntry next = e.next;

        while (next != null)
        {
            if (next.hash == h && hashingStrategy.Equals(name, next.key))
            {
                value = next.value;
                e.next = next.next;
                next.Remove();
                --size;
            }
            else
            {
                e = next;
            }

            next = e.next;
        }

        e = entries[i];
        if (e.hash == h && hashingStrategy.Equals(name, e.key))
        {
            if (value == null)
            {
                value = e.value;
            }

            entries[i] = e.next;
            e.Remove();
            --size;
        }

        return value;
    }

    internal HeaderEntry Remove0(HeaderEntry entry, HeaderEntry previous)
    {
        int i = Index(entry.hash);
        HeaderEntry firstEntry = entries[i];
        if (firstEntry == entry)
        {
            entries[i] = entry.next;
            previous = entries[i];
        }
        else if (previous == null)
        {
            // If we don't have any existing starting point, then start from the beginning.
            previous = firstEntry;
            HeaderEntry next = firstEntry.next;

            while (next != null && next != entry)
            {
                previous = next;
                next = next.next;
            }

            Debug.Assert(next != null, "Entry not found in its hash bucket: " + entry);
            previous.next = entry.next;
        }
        else
        {
            previous.next = entry.next;
        }

        entry.Remove();
        --size;
        return previous;
    }

    // CLR: (T) this is an unchecked cast in Java: "A narrowing reference conversion from a type S to a type variable T
    // is unchecked" (JLS 5.1.6.2). The erasure of DefaultHeaders is a subtype of the erasure of T, its bound Headers
    // (JLS 4.6), so the conversion is completely unchecked and "no run time check is performed" (JLS 5.1.6.3 and 5.5,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.5). A DefaultHeaders that is not a T, such as
    // the exact DefaultHeaders<K, V, T> of copy(), therefore returns itself from its this-returning methods, and the
    // ClassCastException occurs only where a caller reads the result as a T (heap pollution, JLS 4.12.2,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.12.2). A C# cast checks at once: "At run time,
    // an explicit conversion might not succeed and a cast expression might throw an exception"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/type-testing-and-cast#cast-expression),
    // so (T) (object) this would throw InvalidCastException even for an ignored result. The declaration pattern
    // matches when "the run-time type of an expression result derives from type T"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/patterns#declaration-and-type-patterns),
    // so every T instance returns itself; any other instance returns null, which works while the result is ignored and
    // fails where a caller uses it (NullReferenceException in place of ClassCastException).
    private T ThisT()
    {
        return this is T t ? t : default;
    }

    // CLR: catch (IllegalArgumentException e) is a filtered catch. ArgumentNullException and
    // ArgumentOutOfRangeException derive from ArgumentException but stand for NullPointerException and
    // IndexOutOfBoundsException, which are not IllegalArgumentExceptions, while NumberFormatException, which is, maps
    // to FormatException or OverflowException.
    private static bool IsIllegalArgument(Exception e)
    {
        return e is ArgumentException && !(e is ArgumentNullException) && !(e is ArgumentOutOfRangeException)
               || e is FormatException || e is OverflowException;
    }

    private V FromObject(K name, object value)
    {
        try
        {
            return valueConverter.ConvertObject(ObjectUtil.CheckNotNull(value, "value"));
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert object value for header '" + name + '\'', e);
        }
    }

    private V FromBoolean(K name, bool value)
    {
        try
        {
            return valueConverter.ConvertBoolean(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert boolean value for header '" + name + '\'', e);
        }
    }

    private V FromByte(K name, sbyte value)
    {
        try
        {
            return valueConverter.ConvertByte(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert byte value for header '" + name + '\'', e);
        }
    }

    private V FromChar(K name, char value)
    {
        try
        {
            return valueConverter.ConvertChar(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert char value for header '" + name + '\'', e);
        }
    }

    private V FromShort(K name, short value)
    {
        try
        {
            return valueConverter.ConvertShort(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert short value for header '" + name + '\'', e);
        }
    }

    private V FromInt(K name, int value)
    {
        try
        {
            return valueConverter.ConvertInt(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert int value for header '" + name + '\'', e);
        }
    }

    private V FromLong(K name, long value)
    {
        try
        {
            return valueConverter.ConvertLong(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert long value for header '" + name + '\'', e);
        }
    }

    private V FromFloat(K name, float value)
    {
        try
        {
            return valueConverter.ConvertFloat(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert float value for header '" + name + '\'', e);
        }
    }

    private V FromDouble(K name, double value)
    {
        try
        {
            return valueConverter.ConvertDouble(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert double value for header '" + name + '\'', e);
        }
    }

    private V FromTimeMillis(K name, long value)
    {
        try
        {
            return valueConverter.ConvertTimeMillis(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert millsecond value for header '" + name + '\'', e);
        }
    }

    private bool ToBoolean(K name, V value)
    {
        try
        {
            return valueConverter.ConvertToBoolean(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert header value to boolean for header '" + name + '\'');
        }
    }

    private sbyte ToByte(K name, V value)
    {
        try
        {
            return valueConverter.ConvertToByte(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert header value to byte for header '" + name + '\'');
        }
    }

    private char ToChar(K name, V value)
    {
        try
        {
            return valueConverter.ConvertToChar(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert header value to char for header '" + name + '\'');
        }
    }

    private short ToShort(K name, V value)
    {
        try
        {
            return valueConverter.ConvertToShort(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert header value to short for header '" + name + '\'');
        }
    }

    private int ToInt(K name, V value)
    {
        try
        {
            return valueConverter.ConvertToInt(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert header value to int for header '" + name + '\'');
        }
    }

    private long ToLong(K name, V value)
    {
        try
        {
            return valueConverter.ConvertToLong(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert header value to long for header '" + name + '\'');
        }
    }

    private float ToFloat(K name, V value)
    {
        try
        {
            return valueConverter.ConvertToFloat(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert header value to float for header '" + name + '\'');
        }
    }

    private double ToDouble(K name, V value)
    {
        try
        {
            return valueConverter.ConvertToDouble(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException("Failed to convert header value to double for header '" + name + '\'');
        }
    }

    private long ToTimeMillis(K name, V value)
    {
        try
        {
            return valueConverter.ConvertToTimeMillis(value);
        }
        catch (Exception e) when (IsIllegalArgument(e))
        {
            throw new ArgumentException(
                    "Failed to convert header value to millsecond for header '" + name + '\'');
        }
    }

    /**
     * Returns a deep copy of this instance.
     */
    public virtual DefaultHeaders<K, V, T> Copy()
    {
        DefaultHeaders<K, V, T> copy = new DefaultHeaders<K, V, T>(
                hashingStrategy, valueConverter, nameValidator, entries.Length);
        copy.AddImpl(this);
        return copy;
    }

    // CLR: the inner class reads DefaultHeaders.this.head, which it receives instead. The Java iterator's
    // remove() refuses with "read only"; Iterator.remove() is kept by IIterator.
    private sealed class HeaderIterator : IIterator<IEntry<K, V>>
    {
        private readonly HeaderEntry head;
        private HeaderEntry current;

        internal HeaderIterator(HeaderEntry head)
        {
            this.head = head;
            current = head;
        }

        public bool HasNext()
        {
            return current.after != head;
        }

        public IEntry<K, V> Next()
        {
            current = current.after;

            if (current == head)
            {
                throw new InvalidOperationException();
            }

            return current;
        }

        public void Remove()
        {
            throw new NotSupportedException("read only");
        }

        // CLR: IEnumerator<T> over the same hasNext()/next() traversal (MoveNext = hasNext() then next()); Current is
        // the entry next() last returned and is invalid before the first MoveNext.
        public IEntry<K, V> Current => current != head ? current : throw new InvalidOperationException();

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }

            Next();
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }

    // CLR: renamed from ValueIterator because a C# type cannot declare a nested type and a method with the same name
    // ("The type 'type name' already contains a definition for 'identifier'",
    // https://learn.microsoft.com/dotnet/csharp/misc/cs0102); the public valueIterator(K) keeps the Java name. The
    // inner class receives DefaultHeaders.this.
    private sealed class ValueIterator_ : IIterator<V>
    {
        private readonly DefaultHeaders<K, V, T> _headers;
        private readonly K name;
        private readonly int hash;
        private HeaderEntry removalPrevious;
        private HeaderEntry previous;
        private HeaderEntry next;
        private V _current;

        internal ValueIterator_(DefaultHeaders<K, V, T> headers, K name)
        {
            _headers = headers;
            this.name = ObjectUtil.CheckNotNull(name, "name");
            hash = headers.hashingStrategy.GetHashCode(name);
            CalculateNext(headers.entries[headers.Index(hash)]);
        }

        public bool HasNext()
        {
            return next != null;
        }

        public V Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }

            if (previous != null)
            {
                removalPrevious = previous;
            }

            previous = next;
            CalculateNext(next.next);
            return previous.value;
        }

        public void Remove()
        {
            if (previous == null)
            {
                throw new InvalidOperationException();
            }

            removalPrevious = _headers.Remove0(previous, removalPrevious);
            previous = null;
        }

        private void CalculateNext(HeaderEntry entry)
        {
            while (entry != null)
            {
                if (entry.hash == hash && _headers.hashingStrategy.Equals(name, entry.key))
                {
                    next = entry;
                    return;
                }

                entry = entry.next;
            }

            next = null;
        }

        // CLR: IEnumerator<T> over the same hasNext()/next() traversal (MoveNext = hasNext() then next()).
        public V Current => _current;

        object IEnumerator.Current => _current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }

            _current = Next();
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }

    // CLR: the static nested HeaderEntry<K, V> uses this class's K and V (a C# nested type already sees them);
    // its Java protected members (subclass and package access) are protected internal.
    protected internal class HeaderEntry : IEntry<K, V>
    {
        protected internal readonly int hash;
        protected internal readonly K key;
        protected internal V value;
        /**
         * In bucket linked list
         */
        protected internal HeaderEntry next;
        /**
         * Overall insertion order linked list
         */
        protected internal HeaderEntry before, after;

        protected internal HeaderEntry(int hash, K key)
        {
            this.hash = hash;
            this.key = key;
        }

        internal HeaderEntry(int hash, K key, V value, HeaderEntry next, HeaderEntry head)
        {
            this.hash = hash;
            this.key = key;
            this.value = value;
            this.next = next;

            after = head;
            before = head.before;
            PointNeighborsToThis();
        }

        internal HeaderEntry()
        {
            hash = -1;
            key = null;
            before = after = this;
        }

        protected internal void PointNeighborsToThis()
        {
            before.after = this;
            after.before = this;
        }

        public HeaderEntry Before()
        {
            return before;
        }

        public HeaderEntry After()
        {
            return after;
        }

        protected internal virtual void Remove()
        {
            before.after = after;
            after.before = before;
        }

        public K GetKey()
        {
            return key;
        }

        public V GetValue()
        {
            return value;
        }

        public V SetValue(V value)
        {
            ObjectUtil.CheckNotNull(value, "value");

            V oldValue = this.value;
            this.value = value;
            return oldValue;
        }

        public sealed override string ToString()
        {
            return key.ToString() + '=' + value.ToString();
        }

        // CLR: instanceof Map.Entry with the Entry<?, ?> cast is a test for IEntry<K, V>; entries of other type
        // arguments are not equal.
        public override bool Equals(object o)
        {
            if (!(o is IEntry<K, V>))
            {
                return false;
            }

            IEntry<K, V> other = (IEntry<K, V>) o;
            return (GetKey() == null ? other.GetKey() == null : GetKey().Equals(other.GetKey()))  &&
                   (GetValue() == null ? other.GetValue() == null : GetValue().Equals(other.GetValue()));
        }

        public override int GetHashCode()
        {
            return (key == null ? 0 : key.GetHashCode()) ^ (value == null ? 0 : value.GetHashCode());
        }
    }
}
