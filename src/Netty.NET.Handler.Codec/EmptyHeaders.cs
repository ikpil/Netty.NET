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
using System.Text;

namespace Netty.NET.Handler.Codec;

// CLR: UnsupportedOperationException is NotSupportedException. The non-final Java methods are virtual.
// IEquatable<T> routes generic equality to equals(Object), as in DefaultHeaders.
public class EmptyHeaders<K, V, T> : IHeaders<K, V, T>, IEquatable<T>
    where K : class
    where V : class
    where T : IHeaders<K, V, T>
{
    public virtual V Get(K name)
    {
        return null;
    }

    public virtual V Get(K name, V defaultValue)
    {
        return defaultValue;
    }

    public virtual V GetAndRemove(K name)
    {
        return null;
    }

    public virtual V GetAndRemove(K name, V defaultValue)
    {
        return defaultValue;
    }

    public virtual IList<V> GetAll(K name)
    {
        return ReadOnlyCollection<V>.Empty;
    }

    public virtual IList<V> GetAllAndRemove(K name)
    {
        return ReadOnlyCollection<V>.Empty;
    }

    public virtual bool? GetBoolean(K name)
    {
        return null;
    }

    public virtual bool GetBoolean(K name, bool defaultValue)
    {
        return defaultValue;
    }

    public virtual sbyte? GetByte(K name)
    {
        return null;
    }

    public virtual sbyte GetByte(K name, sbyte defaultValue)
    {
        return defaultValue;
    }

    public virtual char? GetChar(K name)
    {
        return null;
    }

    public virtual char GetChar(K name, char defaultValue)
    {
        return defaultValue;
    }

    public virtual short? GetShort(K name)
    {
        return null;
    }

    public virtual short GetShort(K name, short defaultValue)
    {
        return defaultValue;
    }

    public virtual int? GetInt(K name)
    {
        return null;
    }

    public virtual int GetInt(K name, int defaultValue)
    {
        return defaultValue;
    }

    public virtual long? GetLong(K name)
    {
        return null;
    }

    public virtual long GetLong(K name, long defaultValue)
    {
        return defaultValue;
    }

    public virtual float? GetFloat(K name)
    {
        return null;
    }

    public virtual float GetFloat(K name, float defaultValue)
    {
        return defaultValue;
    }

    public virtual double? GetDouble(K name)
    {
        return null;
    }

    public virtual double GetDouble(K name, double defaultValue)
    {
        return defaultValue;
    }

    public virtual long? GetTimeMillis(K name)
    {
        return null;
    }

    public virtual long GetTimeMillis(K name, long defaultValue)
    {
        return defaultValue;
    }

    public virtual bool? GetBooleanAndRemove(K name)
    {
        return null;
    }

    public virtual bool GetBooleanAndRemove(K name, bool defaultValue)
    {
        return defaultValue;
    }

    public virtual sbyte? GetByteAndRemove(K name)
    {
        return null;
    }

    public virtual sbyte GetByteAndRemove(K name, sbyte defaultValue)
    {
        return defaultValue;
    }

    public virtual char? GetCharAndRemove(K name)
    {
        return null;
    }

    public virtual char GetCharAndRemove(K name, char defaultValue)
    {
        return defaultValue;
    }

    public virtual short? GetShortAndRemove(K name)
    {
        return null;
    }

    public virtual short GetShortAndRemove(K name, short defaultValue)
    {
        return defaultValue;
    }

    public virtual int? GetIntAndRemove(K name)
    {
        return null;
    }

    public virtual int GetIntAndRemove(K name, int defaultValue)
    {
        return defaultValue;
    }

    public virtual long? GetLongAndRemove(K name)
    {
        return null;
    }

    public virtual long GetLongAndRemove(K name, long defaultValue)
    {
        return defaultValue;
    }

    public virtual float? GetFloatAndRemove(K name)
    {
        return null;
    }

    public virtual float GetFloatAndRemove(K name, float defaultValue)
    {
        return defaultValue;
    }

    public virtual double? GetDoubleAndRemove(K name)
    {
        return null;
    }

    public virtual double GetDoubleAndRemove(K name, double defaultValue)
    {
        return defaultValue;
    }

    public virtual long? GetTimeMillisAndRemove(K name)
    {
        return null;
    }

    public virtual long GetTimeMillisAndRemove(K name, long defaultValue)
    {
        return defaultValue;
    }

    public virtual bool Contains(K name)
    {
        return false;
    }

    public virtual bool Contains(K name, V value)
    {
        return false;
    }

    public virtual bool ContainsObject(K name, object value)
    {
        return false;
    }

    public virtual bool ContainsBoolean(K name, bool value)
    {
        return false;
    }

    public virtual bool ContainsByte(K name, sbyte value)
    {
        return false;
    }

    public virtual bool ContainsChar(K name, char value)
    {
        return false;
    }

    public virtual bool ContainsShort(K name, short value)
    {
        return false;
    }

    public virtual bool ContainsInt(K name, int value)
    {
        return false;
    }

    public virtual bool ContainsLong(K name, long value)
    {
        return false;
    }

    public virtual bool ContainsFloat(K name, float value)
    {
        return false;
    }

    public virtual bool ContainsDouble(K name, double value)
    {
        return false;
    }

    public virtual bool ContainsTimeMillis(K name, long value)
    {
        return false;
    }

    public virtual int Size()
    {
        return 0;
    }

    public virtual bool IsEmpty()
    {
        return true;
    }

    public virtual ISet<K> Names()
    {
        return ImmutableHashSet<K>.Empty;
    }

    public virtual T Add(K name, V value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T Add(K name, IEnumerable<V> values)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T Add(K name, params V[] values)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddObject(K name, object value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddObject(K name, IEnumerable<object> values)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddObject(K name, params object[] values)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddBoolean(K name, bool value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddByte(K name, sbyte value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddChar(K name, char value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddShort(K name, short value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddInt(K name, int value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddLong(K name, long value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddFloat(K name, float value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddDouble(K name, double value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T AddTimeMillis(K name, long value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T Add<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>
    {
        throw new NotSupportedException("read only");
    }

    public virtual T Set(K name, V value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T Set(K name, IEnumerable<V> values)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T Set(K name, params V[] values)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetObject(K name, object value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetObject(K name, IEnumerable<object> values)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetObject(K name, params object[] values)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetBoolean(K name, bool value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetByte(K name, sbyte value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetChar(K name, char value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetShort(K name, short value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetInt(K name, int value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetLong(K name, long value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetFloat(K name, float value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetDouble(K name, double value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetTimeMillis(K name, long value)
    {
        throw new NotSupportedException("read only");
    }

    public virtual T Set<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>
    {
        throw new NotSupportedException("read only");
    }

    public virtual T SetAll<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>
    {
        throw new NotSupportedException("read only");
    }

    public virtual bool Remove(K name)
    {
        return false;
    }

    public virtual T Clear()
    {
        return ThisT();
    }

    /**
     * Equivalent to {@link #getAll(Object)} but no intermediate list is generated.
     * @param name the name of the header to retrieve
     * @return an {@link Iterator} of header values corresponding to {@code name}.
     */
    public virtual IIterator<V> ValueIterator(K name)
    {
        return new EmptyIterator<V>();
    }

    public virtual IIterator<IEntry<K, V>> Iterator()
    {
        return new EmptyIterator<IEntry<K, V>>();
    }

    public IEnumerator<IEntry<K, V>> GetEnumerator()
    {
        return Iterator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // CLR: instanceof Headers with the Headers<?, ?, ?> cast is a test for headers that share K, V and the self type
    // T, as in DefaultHeaders.equals(Object).
    public override bool Equals(object o)
    {
        if (!(o is IHeaders<K, V, T>))
        {
            return false;
        }

        IHeaders<K, V, T> rhs = (IHeaders<K, V, T>) o;
        return IsEmpty() && rhs.IsEmpty();
    }

    public bool Equals(T other)
    {
        return Equals((object) other);
    }

    public override int GetHashCode()
    {
        return DefaultHeaders.HASH_CODE_SEED;
    }

    // CLR: getClass().getSimpleName() is HeadersUtils.GetSimpleName(GetType()).
    public override string ToString()
    {
        return new StringBuilder(HeadersUtils.GetSimpleName(GetType())).Append('[').Append(']').ToString();
    }

    private T ThisT()
    {
        return (T) (object) this;
    }

    // CLR: the iterator of an empty List: hasNext() is false, next() throws NoSuchElementException and remove()
    // throws IllegalStateException (InvalidOperationException for both).
    private sealed class EmptyIterator<E> : IIterator<E>
    {
        public bool HasNext()
        {
            return false;
        }

        public E Next()
        {
            throw new InvalidOperationException();
        }

        public void Remove()
        {
            throw new InvalidOperationException();
        }

        public E Current => throw new InvalidOperationException();

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            return false;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}
