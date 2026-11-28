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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.AsciiString;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Provides the constants for the standard HTTP header names and values and
 * commonly used utility methods that accesses an {@link HttpMessage}.
 * <p>
 * Concrete instances of this class are most easily obtained from its default factory:
 * {@link DefaultHttpHeadersFactory#headersFactory()}.
 */
// CLR: CharSequence is ICharSequence and String is string; both Java overloads are kept, so a string name binds to the
// String overload and an AsciiString to the CharSequence one. A C# string is not an ICharSequence, so the CharSequence
// values that the String overloads compare are wrapped in the Common StringCharSequence. Integer/Short/Long results are
// int?/short?/long?; Iterable<?> is IEnumerable<object> (reference-type covariance: an IEnumerable of a value type, such
// as List<int>, binds to the Object overload, so value types are passed boxed); List and Set are IList and ISet;
// Map.Entry and Iterator are the codec-base IEntry and IIterator (docs/codec-base-porting.md, Headers).
// Iterable<Map.Entry<String, String>> is IEnumerable<IEntry<string, string>>. Its iterator() is @Deprecated and names
// iteratorCharSequence() and iteratorAsString() as replacements, so it is not ported (owner decision 2026-11-16): the
// final iteratorAsString(), which only returned iterator(), is the abstract IteratorAsString() that the implementations
// override with the body of their iterator(), and GetEnumerator() (foreach, the C# form of the Iterable contract)
// returns IteratorAsString(). The @Deprecated EMPTY_HEADERS, Names, Values and static helpers name their replacements
// (EmptyHttpHeaders.INSTANCE, HttpHeaderNames, HttpHeaderValues, HttpUtil, the instance methods and AsciiString) and are
// not ported; encodeAscii(CharSequence, ByteBuf) has no @deprecated text and is ported with [Obsolete]. Iterator-based
// defaults use the private nested ListIterator/CharSequenceIterator adapters (Java's List.iterator() supports remove()).
// String.equalsIgnoreCase is string.Equals with StringComparison.OrdinalIgnoreCase. getClass() is GetType(), and
// ObjectUtil.checkNotNull throws ArgumentNullException (docs/codec-base-porting.md). IEquatable<HttpHeaders> routes generic
// equality (EqualityComparer<T>.Default, which xUnit consults before treating the headers as an IEnumerable of entries)
// to the Java equals(Object) of the implementation, as codec-base DefaultHeaders does (docs/codec-base-porting.md,
// src/Netty.NET.Handler.Codec/DefaultHeaders.cs).
public abstract class HttpHeaders : IEnumerable<IEntry<string, string>>, IEquatable<HttpHeaders>
{
    public bool Equals(HttpHeaders other)
    {
        return Equals((object) other);
    }

    [Obsolete]
    public static void EncodeAscii(ICharSequence seq, ByteBuf buf)
    {
        if (seq is AsciiString)
        {
            ByteBufUtil.Copy((AsciiString) seq, 0, buf, seq.Length());
        }
        else
        {
            ByteBufUtil.WriteAscii(buf, seq.ToString());
        }
    }

    protected HttpHeaders()
    {
    }

    /**
     * @see #get(CharSequence)
     */
    public abstract string Get(string name);

    /**
     * Returns the value of a header with the specified name.  If there are
     * more than one values for the specified name, the first value is returned.
     *
     * @param name The name of the header to search
     * @return The first header value or {@code null} if there is no such header
     * @see #getAsString(CharSequence)
     */
    public virtual string Get(ICharSequence name)
    {
        return Get(name.ToString());
    }

    /**
     * Returns the value of a header with the specified name.  If there are
     * more than one values for the specified name, the first value is returned.
     *
     * @param name The name of the header to search
     * @return The first header value or {@code defaultValue} if there is no such header
     */
    public virtual string Get(ICharSequence name, string defaultValue)
    {
        string value = Get(name);
        if (value == null)
        {
            return defaultValue;
        }

        return value;
    }

    /**
     * Returns the integer value of a header with the specified name. If there are more than one values for the
     * specified name, the first value is returned.
     *
     * @param name the name of the header to search
     * @return the first header value if the header is found and its value is an integer. {@code null} if there's no
     *         such header or its value is not an integer.
     */
    public abstract int? GetInt(ICharSequence name);

    /**
     * Returns the integer value of a header with the specified name. If there are more than one values for the
     * specified name, the first value is returned.
     *
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the first header value if the header is found and its value is an integer. {@code defaultValue} if
     *         there's no such header or its value is not an integer.
     */
    public abstract int GetInt(ICharSequence name, int defaultValue);

    /**
     * Returns the short value of a header with the specified name. If there are more than one values for the
     * specified name, the first value is returned.
     *
     * @param name the name of the header to search
     * @return the first header value if the header is found and its value is a short. {@code null} if there's no
     *         such header or its value is not a short.
     */
    public abstract short? GetShort(ICharSequence name);

    /**
     * Returns the short value of a header with the specified name. If there are more than one values for the
     * specified name, the first value is returned.
     *
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the first header value if the header is found and its value is a short. {@code defaultValue} if
     *         there's no such header or its value is not a short.
     */
    public abstract short GetShort(ICharSequence name, short defaultValue);

    /**
     * Returns the date value of a header with the specified name. If there are more than one values for the
     * specified name, the first value is returned.
     *
     * @param name the name of the header to search
     * @return the first header value if the header is found and its value is a date. {@code null} if there's no
     *         such header or its value is not a date.
     */
    public abstract long? GetTimeMillis(ICharSequence name);

    /**
     * Returns the date value of a header with the specified name. If there are more than one values for the
     * specified name, the first value is returned.
     *
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the first header value if the header is found and its value is a date. {@code defaultValue} if
     *         there's no such header or its value is not a date.
     */
    public abstract long GetTimeMillis(ICharSequence name, long defaultValue);

    /**
     * @see #getAll(CharSequence)
     */
    public abstract IList<string> GetAll(string name);

    /**
     * Returns the values of headers with the specified name
     *
     * @param name The name of the headers to search
     * @return A {@link List} of header values which will be empty if no values
     *         are found
     * @see #getAllAsString(CharSequence)
     */
    public virtual IList<string> GetAll(ICharSequence name)
    {
        return GetAll(name.ToString());
    }

    /**
     * Returns a new {@link List} that contains all headers in this object.  Note that modifying the
     * returned {@link List} will not affect the state of this object.  If you intend to enumerate over the header
     * entries only, use {@link #iterator()} instead, which has much less overhead.
     * @see #iteratorCharSequence()
     */
    public abstract IList<IEntry<string, string>> Entries();

    /**
     * @see #contains(CharSequence)
     */
    public abstract bool Contains(string name);

    /**
     * @return Iterator over the name/value header pairs.
     */
    public abstract IIterator<IEntry<ICharSequence, ICharSequence>> IteratorCharSequence();

    /**
     * Equivalent to {@link #getAll(String)} but it is possible that no intermediate list is generated.
     * @param name the name of the header to retrieve
     * @return an {@link Iterator} of header values corresponding to {@code name}.
     */
    public virtual IIterator<string> ValueStringIterator(ICharSequence name)
    {
        return new ListIterator<string>(GetAll(name));
    }

    /**
     * Equivalent to {@link #getAll(String)} but it is possible that no intermediate list is generated.
     * @param name the name of the header to retrieve
     * @return an {@link Iterator} of header values corresponding to {@code name}.
     */
    public virtual IIterator<ICharSequence> ValueCharSequenceIterator(ICharSequence name)
    {
        return new CharSequenceIterator(ValueStringIterator(name));
    }

    /**
     * Checks to see if there is a header with the specified name
     *
     * @param name The name of the header to search for
     * @return True if at least one header is found
     */
    public virtual bool Contains(ICharSequence name)
    {
        return Contains(name.ToString());
    }

    /**
     * Checks if no header exists.
     */
    public abstract bool IsEmpty();

    /**
     * Returns the number of headers in this object.
     */
    public abstract int Size();

    /**
     * Returns a new {@link Set} that contains the names of all headers in this object.  Note that modifying the
     * returned {@link Set} will not affect the state of this object.  If you intend to enumerate over the header
     * entries only, use {@link #iterator()} instead, which has much less overhead.
     */
    public abstract ISet<string> Names();

    /**
     * @see #add(CharSequence, Object)
     */
    public abstract HttpHeaders Add(string name, object value);

    /**
     * Adds a new header with the specified name and value.
     *
     * If the specified value is not a {@link String}, it is converted
     * into a {@link String} by {@link Object#toString()}, except in the cases
     * of {@link Date} and {@link Calendar}, which are formatted to the date
     * format defined in <a href="https://www.w3.org/Protocols/rfc2616/rfc2616-sec3.html#sec3.3.1">RFC2616</a>.
     *
     * @param name The name of the header being added
     * @param value The value of the header being added
     *
     * @return {@code this}
     */
    public virtual HttpHeaders Add(ICharSequence name, object value)
    {
        return Add(name.ToString(), value);
    }

    /**
     * @see #add(CharSequence, Iterable)
     */
    public abstract HttpHeaders Add(string name, IEnumerable<object> values);

    /**
     * Adds a new header with the specified name and values.
     *
     * This getMethod can be represented approximately as the following code:
     * <pre>
     * for (Object v: values) {
     *     if (v == null) {
     *         break;
     *     }
     *     headers.add(name, v);
     * }
     * </pre>
     *
     * @param name The name of the headers being set
     * @param values The values of the headers being set
     * @return {@code this}
     */
    public virtual HttpHeaders Add(ICharSequence name, IEnumerable<object> values)
    {
        return Add(name.ToString(), values);
    }

    /**
     * Adds all header entries of the specified {@code headers}.
     *
     * @return {@code this}
     */
    public virtual HttpHeaders Add(HttpHeaders headers)
    {
        ObjectUtil.CheckNotNull(headers, "headers");

        foreach (IEntry<string, string> e in headers)
        {
            Add(e.GetKey(), e.GetValue());
        }

        return this;
    }

    /**
     * Add the {@code name} to {@code value}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    public abstract HttpHeaders AddInt(ICharSequence name, int value);

    /**
     * Add the {@code name} to {@code value}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    public abstract HttpHeaders AddShort(ICharSequence name, short value);

    /**
     * @see #set(CharSequence, Object)
     */
    public abstract HttpHeaders Set(string name, object value);

    /**
     * Sets a header with the specified name and value.
     *
     * If there is an existing header with the same name, it is removed.
     * If the specified value is not a {@link String}, it is converted into a
     * {@link String} by {@link Object#toString()}, except for {@link Date}
     * and {@link Calendar}, which are formatted to the date format defined in
     * <a href="https://www.w3.org/Protocols/rfc2616/rfc2616-sec3.html#sec3.3.1">RFC2616</a>.
     *
     * @param name The name of the header being set
     * @param value The value of the header being set
     * @return {@code this}
     */
    public virtual HttpHeaders Set(ICharSequence name, object value)
    {
        return Set(name.ToString(), value);
    }

    /**
     * @see #set(CharSequence, Iterable)
     */
    public abstract HttpHeaders Set(string name, IEnumerable<object> values);

    /**
     * Sets a header with the specified name and values.
     *
     * If there is an existing header with the same name, it is removed.
     * This getMethod can be represented approximately as the following code:
     * <pre>
     * headers.remove(name);
     * for (Object v: values) {
     *     if (v == null) {
     *         break;
     *     }
     *     headers.add(name, v);
     * }
     * </pre>
     *
     * @param name The name of the headers being set
     * @param values The values of the headers being set
     * @return {@code this}
     */
    public virtual HttpHeaders Set(ICharSequence name, IEnumerable<object> values)
    {
        return Set(name.ToString(), values);
    }

    /**
     * Cleans the current header entries and copies all header entries of the specified {@code headers}.
     *
     * @return {@code this}
     */
    public virtual HttpHeaders Set(HttpHeaders headers)
    {
        CheckNotNull(headers, "headers");

        Clear();

        if (headers.IsEmpty())
        {
            return this;
        }

        foreach (IEntry<string, string> entry in headers)
        {
            Add(entry.GetKey(), entry.GetValue());
        }

        return this;
    }

    /**
     * Retains all current headers but calls {@link #set(String, Object)} for each entry in {@code headers}
     *
     * @param headers The headers used to {@link #set(String, Object)} values in this instance
     * @return {@code this}
     */
    public virtual HttpHeaders SetAll(HttpHeaders headers)
    {
        CheckNotNull(headers, "headers");

        if (headers.IsEmpty())
        {
            return this;
        }

        foreach (IEntry<string, string> entry in headers)
        {
            Set(entry.GetKey(), entry.GetValue());
        }

        return this;
    }

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    public abstract HttpHeaders SetInt(ICharSequence name, int value);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    public abstract HttpHeaders SetShort(ICharSequence name, short value);

    /**
     * @see #remove(CharSequence)
     */
    public abstract HttpHeaders Remove(string name);

    /**
     * Removes the header with the specified name.
     *
     * @param name The name of the header to remove
     * @return {@code this}
     */
    public virtual HttpHeaders Remove(ICharSequence name)
    {
        return Remove(name.ToString());
    }

    /**
     * Removes all headers from this {@link HttpMessage}.
     *
     * @return {@code this}
     */
    public abstract HttpHeaders Clear();

    /**
     * @see #contains(CharSequence, CharSequence, boolean)
     */
    public virtual bool Contains(string name, string value, bool ignoreCase)
    {
        IIterator<string> valueIterator = ValueStringIterator(new StringCharSequence(name));

        if (ignoreCase)
        {
            while (valueIterator.HasNext())
            {
                if (string.Equals(valueIterator.Next(), value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        else
        {
            while (valueIterator.HasNext())
            {
                if (valueIterator.Next().Equals(value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /**
     * Returns {@code true} if a header with the {@code name} and {@code value} exists, {@code false} otherwise.
     * This also handles multiple values that are separated with a {@code ,}.
     * <p>
     * If {@code ignoreCase} is {@code true} then a case insensitive compare is done on the value.
     * @param name the name of the header to find
     * @param value the value of the header to find
     * @param ignoreCase {@code true} then a case insensitive compare is run to compare values.
     * otherwise a case sensitive compare is run to compare values.
     */
    public virtual bool ContainsValue(ICharSequence name, ICharSequence value, bool ignoreCase)
    {
        IIterator<ICharSequence> itr = ValueCharSequenceIterator(name);

        while (itr.HasNext())
        {
            if (ContainsCommaSeparatedTrimmed(itr.Next(), value, ignoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ContainsCommaSeparatedTrimmed(ICharSequence rawNext, ICharSequence expected,
                                                         bool ignoreCase)
    {
        int begin = 0;
        int end;

        if (ignoreCase)
        {
            if ((end = AsciiString.IndexOf(rawNext, ',', begin)) == -1)
            {
                if (ContentEqualsIgnoreCase(Trim(rawNext), expected))
                {
                    return true;
                }
            }
            else
            {
                do
                {
                    if (ContentEqualsIgnoreCase(Trim(rawNext.SubSequence(begin, end)), expected))
                    {
                        return true;
                    }

                    begin = end + 1;
                } while ((end = AsciiString.IndexOf(rawNext, ',', begin)) != -1);

                if (begin < rawNext.Length())
                {
                    if (ContentEqualsIgnoreCase(Trim(rawNext.SubSequence(begin, rawNext.Length())), expected))
                    {
                        return true;
                    }
                }
            }
        }
        else
        {
            if ((end = AsciiString.IndexOf(rawNext, ',', begin)) == -1)
            {
                if (ContentEquals(Trim(rawNext), expected))
                {
                    return true;
                }
            }
            else
            {
                do
                {
                    if (ContentEquals(Trim(rawNext.SubSequence(begin, end)), expected))
                    {
                        return true;
                    }

                    begin = end + 1;
                } while ((end = AsciiString.IndexOf(rawNext, ',', begin)) != -1);

                if (begin < rawNext.Length())
                {
                    if (ContentEquals(Trim(rawNext.SubSequence(begin, rawNext.Length())), expected))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /**
     * {@link Headers#get(Object)} and convert the result to a {@link String}.
     * @param name the name of the header to retrieve
     * @return the first header value if the header is found. {@code null} if there's no such header.
     */
    public string GetAsString(ICharSequence name)
    {
        return Get(name);
    }

    /**
     * {@link Headers#getAll(Object)} and convert each element of {@link List} to a {@link String}.
     * @param name the name of the header to retrieve
     * @return a {@link List} of header values or an empty {@link List} if no values are found.
     */
    public IList<string> GetAllAsString(ICharSequence name)
    {
        return GetAll(name);
    }

    /**
     * {@link Iterator} that converts each {@link Entry}'s key and value to a {@link String}.
     */
    public abstract IIterator<IEntry<string, string>> IteratorAsString();

    public IEnumerator<IEntry<string, string>> GetEnumerator()
    {
        return IteratorAsString();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /**
     * Returns {@code true} if a header with the {@code name} and {@code value} exists, {@code false} otherwise.
     * <p>
     * If {@code ignoreCase} is {@code true} then a case insensitive compare is done on the value.
     * @param name the name of the header to find
     * @param value the value of the header to find
     * @param ignoreCase {@code true} then a case insensitive compare is run to compare values.
     * otherwise a case sensitive compare is run to compare values.
     */
    public virtual bool Contains(ICharSequence name, ICharSequence value, bool ignoreCase)
    {
        return Contains(name.ToString(), value.ToString(), ignoreCase);
    }

    public override string ToString()
    {
        return HeadersUtils.ToString(GetType(), IteratorCharSequence(), Size());
    }

    /**
     * Returns a deep copy of the passed in {@link HttpHeaders}.
     */
    public virtual HttpHeaders Copy()
    {
        return new DefaultHttpHeaders().Set(this);
    }

    // CLR: List.iterator() of the list that getAll(...) returns; remove() removes the last returned element.
    private sealed class ListIterator<T> : IIterator<T>
    {
        private readonly IList<T> list;
        private int next;
        private int last = -1;
        private T current;

        internal ListIterator(IList<T> list)
        {
            this.list = list;
        }

        public bool HasNext()
        {
            return next < list.Count;
        }

        public T Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }

            last = next;
            return list[next++];
        }

        public void Remove()
        {
            if (last < 0)
            {
                throw new InvalidOperationException();
            }

            list.RemoveAt(last);
            next = last;
            last = -1;
        }

        public T Current => current;

        object IEnumerator.Current => current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }

            current = Next();
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

    // CLR: the Iterator<String> that valueCharSequenceIterator returns as an Iterator<? extends CharSequence>; a C#
    // string is not an ICharSequence, so each value is wrapped in a StringCharSequence.
    private sealed class CharSequenceIterator : IIterator<ICharSequence>
    {
        private readonly IIterator<string> itr;
        private ICharSequence current;

        internal CharSequenceIterator(IIterator<string> itr)
        {
            this.itr = itr;
        }

        public bool HasNext()
        {
            return itr.HasNext();
        }

        public ICharSequence Next()
        {
            string next = itr.Next();
            return next == null ? null : new StringCharSequence(next);
        }

        public void Remove()
        {
            itr.Remove();
        }

        public ICharSequence Current => current;

        object IEnumerator.Current => current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }

            current = Next();
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
}
