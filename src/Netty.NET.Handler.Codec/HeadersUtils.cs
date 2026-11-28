/*
 * Copyright 2015 The Netty Project
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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec;

/**
 * Provides utility methods related to {@link Headers}.
 */
// CLR: a final class with only a private constructor and static methods is a static class. The wildcard
// Headers<K, V, ?> parameters take the self type as a method type parameter T (see IHeaders.cs).
public static class HeadersUtils
{
    /**
     * {@link Headers#get(Object)} and convert each element of {@link List} to a {@link String}.
     * @param name the name of the header to retrieve
     * @return a {@link List} of header values or an empty {@link List} if no values are found.
     */
    public static IList<string> GetAllAsString<K, V, T>(IHeaders<K, V, T> headers, K name)
        where K : class
        where V : class
        where T : IHeaders<K, V, T>
    {
        IList<V> allNames = headers.GetAll(name);
        return new StringList<V>(allNames);
    }

    /**
     * {@link Headers#get(Object)} and convert the result to a {@link String}.
     * @param headers the headers to get the {@code name} from
     * @param name the name of the header to retrieve
     * @return the first header value if the header is found. {@code null} if there's no such entry.
     */
    public static string GetAsString<K, V, T>(IHeaders<K, V, T> headers, K name)
        where K : class
        where V : class
        where T : IHeaders<K, V, T>
    {
        V orig = headers.Get(name);
        return orig != null ? orig.ToString() : null;
    }

    /**
     * {@link Headers#iterator()} which converts each {@link Entry}'s key and value to a {@link String}.
     */
    // CLR: Iterable.iterator() is IEnumerable<T>.GetEnumerator(), whose IEnumerator cannot remove(); a headers
    // enumerator is the IIterator that iterator() returns, and its remove() is delegated (see StringEntryIterator).
    public static IIterator<IEntry<string, string>> IteratorAsString(
            IEnumerable<IEntry<ICharSequence, ICharSequence>> headers)
    {
        return new StringEntryIterator(headers.GetEnumerator());
    }

    /**
     * Helper for implementing toString for {@link DefaultHeaders} and wrappers such as DefaultHttpHeaders.
     * @param headersClass the class of headers
     * @param headersIt the iterator on the actual headers
     * @param size the size of the iterator
     * @return a String representation of the headers
     */
    public static string ToString<K, V>(Type headersClass, IIterator<IEntry<K, V>> headersIt, int size)
    {
        string simpleName = GetSimpleName(headersClass);

        if (size == 0)
        {
            return simpleName + "[]";
        }
        else
        {
            // original capacity assumes 20 chars per headers
            StringBuilder sb = new StringBuilder(simpleName.Length + 2 + size * 20)
                    .Append(simpleName)
                    .Append('[');

            while (headersIt.HasNext())
            {
                IEntry<K, V> header = headersIt.Next();
                sb.Append(header.GetKey()).Append(": ").Append(header.GetValue()).Append(", ");
            }

            sb.Length = sb.Length - 2;
            return sb.Append(']').ToString();
        }
    }

    // CLR: Class.getSimpleName(). Type.Name has no namespace or declaring type, but a generic type's name ends with
    // a backtick and its arity (List<T> is "List`1",
    // https://learn.microsoft.com/dotnet/framework/reflection-and-codedom/specifying-fully-qualified-type-names),
    // which getSimpleName() does not have.
    internal static string GetSimpleName(Type type)
    {
        string name = type.Name;
        int arity = name.IndexOf('`');
        return arity >= 0 ? name.Substring(0, arity) : name;
    }

    /**
     * {@link Headers#names()} and convert each element of {@link Set} to a {@link String}.
     * @param headers the headers to get the names from
     * @return a {@link Set} of header values or an empty {@link Set} if no values are found.
     */
    public static ISet<string> NamesAsString<T>(IHeaders<ICharSequence, ICharSequence, T> headers)
        where T : IHeaders<ICharSequence, ICharSequence, T>
    {
        return new DelegatingNameSet<T>(headers);
    }

    // CLR: the anonymous AbstractList<String> is a named nested IList<string>. AbstractList derives every member from
    // get(int) and size() (size() is Count) and rejects modification with UnsupportedOperationException, which is
    // NotSupportedException here; IsReadOnly reports that.
    private sealed class StringList<V> : IList<string>
        where V : class
    {
        private readonly IList<V> allNames;

        internal StringList(IList<V> allNames)
        {
            this.allNames = allNames;
        }

        public string this[int index]
        {
            get
            {
                V value = allNames[index];
                return value != null ? value.ToString() : null;
            }
            set => throw new NotSupportedException();
        }

        public int Count => allNames.Count;

        public bool IsReadOnly => true;

        public int IndexOf(string item)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Equals(item, this[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        public bool Contains(string item)
        {
            return IndexOf(item) >= 0;
        }

        public void CopyTo(string[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);

            for (int i = 0; i < Count; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(string item)
        {
            throw new NotSupportedException();
        }

        public void Insert(int index, string item)
        {
            throw new NotSupportedException();
        }

        public bool Remove(string item)
        {
            throw new NotSupportedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }
    }

    // CLR: the wrapped enumerator comes from Iterable.iterator(). When it is an IIterator (every IHeaders enumerator
    // is), hasNext()/next()/remove() are delegated to it as in Java. Another IEnumerator has no hasNext(), so it is
    // advanced ahead and the result kept until next() consumes it, and it cannot remove(): remove() then throws as
    // Iterator.remove()'s default implementation does.
    private sealed class StringEntryIterator : IIterator<IEntry<string, string>>
    {
        private readonly IEnumerator<IEntry<ICharSequence, ICharSequence>> iter;
        private bool advanced;
        private bool hasNext;
        private IEntry<string, string> _current;

        internal StringEntryIterator(IEnumerator<IEntry<ICharSequence, ICharSequence>> iter)
        {
            this.iter = iter;
        }

        public bool HasNext()
        {
            if (iter is IIterator<IEntry<ICharSequence, ICharSequence>> it)
            {
                return it.HasNext();
            }

            if (!advanced)
            {
                hasNext = iter.MoveNext();
                advanced = true;
            }

            return hasNext;
        }

        public IEntry<string, string> Next()
        {
            if (iter is IIterator<IEntry<ICharSequence, ICharSequence>> it)
            {
                return new StringEntry(it.Next());
            }
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }

            advanced = false;
            return new StringEntry(iter.Current);
        }

        public void Remove()
        {
            if (iter is IIterator<IEntry<ICharSequence, ICharSequence>> it)
            {
                it.Remove();
                return;
            }

            throw new NotSupportedException("remove");
        }

        // CLR: IEnumerator<T> over the same hasNext()/next() traversal (MoveNext = hasNext() then next()).
        public IEntry<string, string> Current => _current;

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
            iter.Dispose();
        }
    }

    // CLR: a Java String is a CharSequence; the CLR string value is wrapped in a StringCharSequence for the entry.
    private sealed class StringEntry : IEntry<string, string>
    {
        private readonly IEntry<ICharSequence, ICharSequence> entry;
        private string name;
        private string value;

        internal StringEntry(IEntry<ICharSequence, ICharSequence> entry)
        {
            this.entry = entry;
        }

        public string GetKey()
        {
            if (name == null)
            {
                name = entry.GetKey().ToString();
            }

            return name;
        }

        public string GetValue()
        {
            if (value == null && entry.GetValue() != null)
            {
                value = entry.GetValue().ToString();
            }

            return value;
        }

        public string SetValue(string value)
        {
            string old = GetValue();
            entry.SetValue(value != null ? new StringCharSequence(value) : null);
            return old;
        }

        public override string ToString()
        {
            return entry.ToString();
        }
    }

    // CLR: names().iterator() is the ISet<K> enumerator, which has no hasNext() and cannot remove(); it is advanced
    // ahead and the result kept until next() consumes it, and remove() throws as Iterator.remove()'s default
    // implementation does. (Java removed from the names() snapshot, which did not change the headers.)
    private sealed class StringIterator<T> : IIterator<string>
    {
        private readonly IEnumerator<T> iter;
        private bool advanced;
        private bool hasNext;
        private string _current;

        internal StringIterator(IEnumerator<T> iter)
        {
            this.iter = iter;
        }

        public bool HasNext()
        {
            if (!advanced)
            {
                hasNext = iter.MoveNext();
                advanced = true;
            }

            return hasNext;
        }

        public string Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }

            advanced = false;
            T next = iter.Current;
            return next != null ? next.ToString() : null;
        }

        public void Remove()
        {
            throw new NotSupportedException("remove");
        }

        // CLR: IEnumerator<T> over the same hasNext()/next() traversal (MoveNext = hasNext() then next()).
        public string Current => _current;

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
            iter.Dispose();
        }
    }

    // CLR: AbstractCollection<String> implements Set<String> with size(), isEmpty(), contains(Object) and iterator()
    // (size() is Count). ISet<string> also requires modification members, which throw NotSupportedException as
    // AbstractCollection.add does (IsReadOnly is true), and set comparisons, which have no member in the source and
    // evaluate over an ordinal HashSet<string> of the enumerated names.
    private sealed class DelegatingNameSet<T> : ISet<string>
        where T : IHeaders<ICharSequence, ICharSequence, T>
    {
        private readonly IHeaders<ICharSequence, ICharSequence, T> headers;

        internal DelegatingNameSet(IHeaders<ICharSequence, ICharSequence, T> headers)
        {
            this.headers = ObjectUtil.CheckNotNull(headers, "headers");
        }

        public int Count => headers.Names().Count;

        public bool IsEmpty()
        {
            return headers.IsEmpty();
        }

        public bool Contains(string o)
        {
            return headers.Contains(new StringCharSequence(o.ToString()));
        }

        public IEnumerator<string> GetEnumerator()
        {
            return new StringIterator<ICharSequence>(headers.Names().GetEnumerator());
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool IsReadOnly => true;

        public void CopyTo(string[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);

            foreach (string name in this)
            {
                array[arrayIndex++] = name;
            }
        }

        public bool Add(string item)
        {
            throw new NotSupportedException();
        }

        void ICollection<string>.Add(string item)
        {
            throw new NotSupportedException();
        }

        public bool Remove(string item)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public void UnionWith(IEnumerable<string> other)
        {
            throw new NotSupportedException();
        }

        public void IntersectWith(IEnumerable<string> other)
        {
            throw new NotSupportedException();
        }

        public void ExceptWith(IEnumerable<string> other)
        {
            throw new NotSupportedException();
        }

        public void SymmetricExceptWith(IEnumerable<string> other)
        {
            throw new NotSupportedException();
        }

        public bool IsSubsetOf(IEnumerable<string> other)
        {
            return new HashSet<string>(this).IsSubsetOf(other);
        }

        public bool IsSupersetOf(IEnumerable<string> other)
        {
            return new HashSet<string>(this).IsSupersetOf(other);
        }

        public bool IsProperSupersetOf(IEnumerable<string> other)
        {
            return new HashSet<string>(this).IsProperSupersetOf(other);
        }

        public bool IsProperSubsetOf(IEnumerable<string> other)
        {
            return new HashSet<string>(this).IsProperSubsetOf(other);
        }

        public bool Overlaps(IEnumerable<string> other)
        {
            return new HashSet<string>(this).Overlaps(other);
        }

        public bool SetEquals(IEnumerable<string> other)
        {
            return new HashSet<string>(this).SetEquals(other);
        }
    }
}
