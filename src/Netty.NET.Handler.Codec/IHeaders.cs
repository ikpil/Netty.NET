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
using System.Collections.Generic;

namespace Netty.NET.Handler.Codec;

/**
 * Common interface for {@link Headers} which represents a mapping of key to value.
 * Duplicate keys may be allowed by implementations.
 *
 * @param <K> the type of the header name.
 * @param <V> the type of the header value.
 * @param <T> the type to use for return values when the intention is to return {@code this} object.
 */
// CLR: K and V are constrained to reference types: Java type variables range over reference types only, and a null
// V marks an absent header. The self type T keeps its F-bounded constraint. Iterable<Entry<K, V>> is
// IEnumerable<IEntry<K, V>>, and iterator() returns the Java-shaped IIterator (both declared below). The boxed
// Boolean/Byte/.../Long results are nullable value types and Java byte is sbyte (see IValueConverter).
//
// C# has no use-site wildcards and IHeaders cannot be variant, because K and V are also input types ("covariant type
// parameters can be used as the return types of the interface's methods, and contravariant type parameters can be
// used as the parameter types", https://learn.microsoft.com/dotnet/standard/generics/covariance-and-contravariance).
// Headers<? extends K, ? extends V, ?> is therefore IHeaders<K, V, T2> with a method type parameter T2 for the
// wildcard self type; Iterable<? extends V> is the covariant IEnumerable<V>, and Iterable<?> is IEnumerable<object>
// (variance applies only to reference types, so a sequence of value types must be boxed by the caller).
public interface IHeaders<K, V, T> : IEnumerable<IEntry<K, V>>
    where K : class
    where V : class
    where T : IHeaders<K, V, T>
{
    /**
     * Returns the value of a header with the specified name. If there is more than one value for the specified name,
     * the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the first header value if the header is found. {@code null} if there's no such header
     */
    V Get(K name);

    /**
     * Returns the value of a header with the specified name. If there is more than one value for the specified name,
     * the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the first header value or {@code defaultValue} if there is no such header
     */
    V Get(K name, V defaultValue);

    /**
     * Returns the value of a header with the specified name and removes it from this object. If there is more than
     * one value for the specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the first header value or {@code null} if there is no such header
     */
    V GetAndRemove(K name);

    /**
     * Returns the value of a header with the specified name and removes it from this object. If there is more than
     * one value for the specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the first header value or {@code defaultValue} if there is no such header
     */
    V GetAndRemove(K name, V defaultValue);

    /**
     * Returns all values for the header with the specified name. The returned {@link List} can't be modified.
     *
     * @param name the name of the header to retrieve
     * @return a {@link List} of header values or an empty {@link List} if no values are found.
     */
    IList<V> GetAll(K name);

    /**
     * Returns all values for the header with the specified name and removes them from this object.
     * The returned {@link List} can't be modified.
     *
     * @param name the name of the header to retrieve
     * @return a {@link List} of header values or an empty {@link List} if no values are found.
     */
    IList<V> GetAllAndRemove(K name);

    /**
     * Returns the {@code boolean} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the {@code boolean} value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to {@code boolean}.
     */
    bool? GetBoolean(K name);

    /**
     * Returns the {@code boolean} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the {@code boolean} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code boolean}.
     */
    bool GetBoolean(K name, bool defaultValue);

    /**
     * Returns the {@code byte} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the {@code byte} value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to {@code byte}.
     */
    sbyte? GetByte(K name);

    /**
     * Returns the {@code byte} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the {@code byte} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code byte}.
     */
    sbyte GetByte(K name, sbyte defaultValue);

    /**
     * Returns the {@code char} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the {@code char} value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to {@code char}.
     */
    char? GetChar(K name);

    /**
     * Returns the {@code char} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the {@code char} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code char}.
     */
    char GetChar(K name, char defaultValue);

    /**
     * Returns the {@code short} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the {@code short} value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to {@code short}.
     */
    short? GetShort(K name);

    /**
     * Returns the {@code short} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the {@code short} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code short}.
     */
    short GetShort(K name, short defaultValue);

    /**
     * Returns the {@code int} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the {@code int} value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to {@code int}.
     */
    int? GetInt(K name);

    /**
     * Returns the {@code int} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the {@code int} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code int}.
     */
    int GetInt(K name, int defaultValue);

    /**
     * Returns the {@code long} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the {@code long} value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to {@code long}.
     */
    long? GetLong(K name);

    /**
     * Returns the {@code long} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the {@code long} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code long}.
     */
    long GetLong(K name, long defaultValue);

    /**
     * Returns the {@code float} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the {@code float} value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to {@code float}.
     */
    float? GetFloat(K name);

    /**
     * Returns the {@code float} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the {@code float} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code float}.
     */
    float GetFloat(K name, float defaultValue);

    /**
     * Returns the {@code double} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the {@code double} value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to {@code double}.
     */
    double? GetDouble(K name);

    /**
     * Returns the {@code double} value of a header with the specified name. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the {@code double} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code double}.
     */
    double GetDouble(K name, double defaultValue);

    /**
     * Returns the value of a header with the specified name in milliseconds. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @return the milliseconds value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to milliseconds.
     */
    long? GetTimeMillis(K name);

    /**
     * Returns the value of a header with the specified name in milliseconds. If there is more than one value for the
     * specified name, the first value in insertion order is returned.
     *
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the milliseconds value of the first value in insertion order or {@code defaultValue} if there is no such
     *         value or it can't be converted to milliseconds.
     */
    long GetTimeMillis(K name, long defaultValue);

    /**
     * Returns the {@code boolean} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to retrieve
     * @return the {@code boolean} value of the first value in insertion order or {@code null} if there is no
     *         such value or it can't be converted to {@code boolean}.
     */
    bool? GetBooleanAndRemove(K name);

    /**
     * Returns the {@code boolean} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the {@code boolean} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code boolean}.
     */
    bool GetBooleanAndRemove(K name, bool defaultValue);

    /**
     * Returns the {@code byte} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @return the {@code byte} value of the first value in insertion order or {@code null} if there is no
     *         such value or it can't be converted to {@code byte}.
     */
    sbyte? GetByteAndRemove(K name);

    /**
     * Returns the {@code byte} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the {@code byte} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code byte}.
     */
    sbyte GetByteAndRemove(K name, sbyte defaultValue);

    /**
     * Returns the {@code char} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @return the {@code char} value of the first value in insertion order or {@code null} if there is no
     *         such value or it can't be converted to {@code char}.
     */
    char? GetCharAndRemove(K name);

    /**
     * Returns the {@code char} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the {@code char} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code char}.
     */
    char GetCharAndRemove(K name, char defaultValue);

    /**
     * Returns the {@code short} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @return the {@code short} value of the first value in insertion order or {@code null} if there is no
     *         such value or it can't be converted to {@code short}.
     */
    short? GetShortAndRemove(K name);

    /**
     * Returns the {@code short} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the {@code short} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code short}.
     */
    short GetShortAndRemove(K name, short defaultValue);

    /**
     * Returns the {@code int} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @return the {@code int} value of the first value in insertion order or {@code null} if there is no
     *         such value or it can't be converted to {@code int}.
     */
    int? GetIntAndRemove(K name);

    /**
     * Returns the {@code int} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the {@code int} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code int}.
     */
    int GetIntAndRemove(K name, int defaultValue);

    /**
     * Returns the {@code long} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @return the {@code long} value of the first value in insertion order or {@code null} if there is no
     *         such value or it can't be converted to {@code long}.
     */
    long? GetLongAndRemove(K name);

    /**
     * Returns the {@code long} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the {@code long} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code long}.
     */
    long GetLongAndRemove(K name, long defaultValue);

    /**
     * Returns the {@code float} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @return the {@code float} value of the first value in insertion order or {@code null} if there is no
     *         such value or it can't be converted to {@code float}.
     */
    float? GetFloatAndRemove(K name);

    /**
     * Returns the {@code float} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the {@code float} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code float}.
     */
    float GetFloatAndRemove(K name, float defaultValue);

    /**
     * Returns the {@code double} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @return the {@code double} value of the first value in insertion order or {@code null} if there is no
     *         such value or it can't be converted to {@code double}.
     */
    double? GetDoubleAndRemove(K name);

    /**
     * Returns the {@code double} value of a header with the specified {@code name} and removes the header from this
     * object. If there is more than one value for the specified name, the first value in insertion order is returned.
     * In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to search
     * @param defaultValue the default value
     * @return the {@code double} value of the first value in insertion order or {@code defaultValue} if there is no
     *         such value or it can't be converted to {@code double}.
     */
    double GetDoubleAndRemove(K name, double defaultValue);

    /**
     * Returns the value of a header with the specified {@code name} in milliseconds and removes the header from this
     * object. If there is more than one value for the specified {@code name}, the first value in insertion order is
     * returned. In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to retrieve
     * @return the milliseconds value of the first value in insertion order or {@code null} if there is no such
     *         value or it can't be converted to milliseconds.
     */
    long? GetTimeMillisAndRemove(K name);

    /**
     * Returns the value of a header with the specified {@code name} in milliseconds and removes the header from this
     * object. If there is more than one value for the specified {@code name}, the first value in insertion order is
     * returned. In any case all values for {@code name} are removed.
     * <p>
     * If an exception occurs during the translation from type {@code T} all entries with {@code name} may still
     * be removed.
     * @param name the name of the header to retrieve
     * @param defaultValue the default value
     * @return the milliseconds value of the first value in insertion order or {@code defaultValue} if there is no such
     *         value or it can't be converted to milliseconds.
     */
    long GetTimeMillisAndRemove(K name, long defaultValue);

    /**
     * Returns {@code true} if a header with the {@code name} exists, {@code false} otherwise.
     *
     * @param name the header name
     */
    bool Contains(K name);

    /**
     * Returns {@code true} if a header with the {@code name} and {@code value} exists, {@code false} otherwise.
     * <p>
     * The {@link Object#equals(Object)} method is used to test for equality of {@code value}.
     * </p>
     * @param name the header name
     * @param value the header value of the header to find
     */
    bool Contains(K name, V value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsObject(K name, object value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsBoolean(K name, bool value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsByte(K name, sbyte value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsChar(K name, char value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsShort(K name, short value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsInt(K name, int value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsLong(K name, long value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsFloat(K name, float value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsDouble(K name, double value);

    /**
     * Returns {@code true} if a header with the name and value exists.
     *
     * @param name the header name
     * @param value the header value
     * @return {@code true} if it contains it {@code false} otherwise
     */
    bool ContainsTimeMillis(K name, long value);

    /**
     * Returns the number of headers in this object.
     */
    int Size();

    /**
     * Returns {@code true} if {@link #size()} equals {@code 0}.
     */
    bool IsEmpty();

    /**
     * Returns a {@link Set} of all header names in this object. The returned {@link Set} cannot be modified.
     */
    ISet<K> Names();

    /**
     * Adds a new header with the specified {@code name} and {@code value}.
     *
     * @param name the name of the header
     * @param value the value of the header
     * @return {@code this}
     */
    T Add(K name, V value);

    /**
     * Adds new headers with the specified {@code name} and {@code values}. This method is semantically equivalent to
     *
     * <pre>
     * for (T value : values) {
     *     headers.add(name, value);
     * }
     * </pre>
     *
     * @param name the header name
     * @param values the values of the header
     * @return {@code this}
     */
    T Add(K name, IEnumerable<V> values);

    /**
     * Adds new headers with the specified {@code name} and {@code values}. This method is semantically equivalent to
     *
     * <pre>
     * for (T value : values) {
     *     headers.add(name, value);
     * }
     * </pre>
     *
     * @param name the header name
     * @param values the values of the header
     * @return {@code this}
     */
    T Add(K name, params V[] values);

    /**
     * Adds a new header. Before the {@code value} is added, it's converted to type {@code T}.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddObject(K name, object value);

    /**
     * Adds a new header with the specified name and values. This method is equivalent to
     *
     * <pre>
     * for (Object v : values) {
     *     headers.addObject(name, v);
     * }
     * </pre>
     *
     * @param name the header name
     * @param values the value of the header
     * @return {@code this}
     */
    T AddObject(K name, IEnumerable<object> values);

    /**
     * Adds a new header with the specified name and values. This method is equivalent to
     *
     * <pre>
     * for (Object v : values) {
     *     headers.addObject(name, v);
     * }
     * </pre>
     *
     * @param name the header name
     * @param values the value of the header
     * @return {@code this}
     */
    T AddObject(K name, params object[] values);

    /**
     * Adds a new header.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddBoolean(K name, bool value);

    /**
     * Adds a new header.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddByte(K name, sbyte value);

    /**
     * Adds a new header.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddChar(K name, char value);

    /**
     * Adds a new header.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddShort(K name, short value);

    /**
     * Adds a new header.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddInt(K name, int value);

    /**
     * Adds a new header.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddLong(K name, long value);

    /**
     * Adds a new header.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddFloat(K name, float value);

    /**
     * Adds a new header.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddDouble(K name, double value);

    /**
     * Adds a new header.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T AddTimeMillis(K name, long value);

    /**
     * Adds all header names and values of {@code headers} to this object.
     *
     * @throws IllegalArgumentException if {@code headers == this}.
     * @return {@code this}
     */
    T Add<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>;

    /**
     * Sets a header with the specified name and value. Any existing headers with the same name are overwritten.
     *
     * @param name the header name
     * @param value the value of the header
     * @return {@code this}
     */
    T Set(K name, V value);

    /**
     * Sets a new header with the specified name and values. This method is equivalent to
     *
     * <pre>
     * for (T v : values) {
     *     headers.addObject(name, v);
     * }
     * </pre>
     *
     * @param name the header name
     * @param values the value of the header
     * @return {@code this}
     */
    T Set(K name, IEnumerable<V> values);

    /**
     * Sets a header with the specified name and values. Any existing headers with this name are removed. This method
     * is equivalent to:
     *
     * <pre>
     * headers.remove(name);
     * for (T v : values) {
     *     headers.add(name, v);
     * }
     * </pre>
     *
     * @param name the header name
     * @param values the value of the header
     * @return {@code this}
     */
    T Set(K name, params V[] values);

    /**
     * Sets a new header. Any existing headers with this name are removed. Before the {@code value} is add, it's
     * converted to type {@code T}.
     *
     * @param name the header name
     * @param value the value of the header
     * @throws NullPointerException if either {@code name} or {@code value} before or after its conversion is
     *                              {@code null}.
     * @return {@code this}
     */
    T SetObject(K name, object value);

    /**
     * Sets a header with the specified name and values. Any existing headers with this name are removed. This method
     * is equivalent to:
     *
     * <pre>
     * headers.remove(name);
     * for (Object v : values) {
     *     headers.addObject(name, v);
     * }
     * </pre>
     *
     * @param name the header name
     * @param values the values of the header
     * @return {@code this}
     */
    T SetObject(K name, IEnumerable<object> values);

    /**
     * Sets a header with the specified name and values. Any existing headers with this name are removed. This method
     * is equivalent to:
     *
     * <pre>
     * headers.remove(name);
     * for (Object v : values) {
     *     headers.addObject(name, v);
     * }
     * </pre>
     *
     * @param name the header name
     * @param values the values of the header
     * @return {@code this}
     */
    T SetObject(K name, params object[] values);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    T SetBoolean(K name, bool value);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    T SetByte(K name, sbyte value);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    T SetChar(K name, char value);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    T SetShort(K name, short value);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    T SetInt(K name, int value);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    T SetLong(K name, long value);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    T SetFloat(K name, float value);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    T SetDouble(K name, double value);

    /**
     * Set the {@code name} to {@code value}. This will remove all previous values associated with {@code name}.
     * @param name The name to modify
     * @param value The value
     * @return {@code this}
     */
    T SetTimeMillis(K name, long value);

    /**
     * Clears the current header entries and copies all header entries of the specified {@code headers}.
     *
     * @return {@code this}
     */
    T Set<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>;

    /**
     * Retains all current headers but calls {@link #set(K, V)} for each entry in {@code headers}.
     *
     * @param headers The headers used to {@link #set(K, V)} values in this instance
     * @return {@code this}
     */
    T SetAll<T2>(IHeaders<K, V, T2> headers) where T2 : IHeaders<K, V, T2>;

    /**
     * Removes all headers with the specified {@code name}.
     *
     * @param name the header name
     * @return {@code true} if at least one entry has been removed.
     */
    bool Remove(K name);

    /**
     * Removes all headers. After a call to this method {@link #size()} equals {@code 0}.
     *
     * @return {@code this}
     */
    T Clear();

    IIterator<IEntry<K, V>> Iterator();
}

// CLR: java.util.Map.Entry, the element type of Headers' Iterable. KeyValuePair<TKey, TValue>, the BCL pair, only
// "Gets the key" and "Gets the value" and its foreach "allows only reading from, not writing to, the collection"
// (https://learn.microsoft.com/dotnet/api/system.collections.generic.keyvaluepair-2), while Map.Entry.setValue
// "Replaces the value corresponding to this entry with the specified value (optional operation). (Writes through to
// the map.)" (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Map.Entry.html). Headers entries
// write through (DefaultHeaders.HeaderEntry, HeadersUtils' StringEntry), so the interface keeps getKey(), getValue()
// and setValue(V); implementations override Equals/GetHashCode with the Map.Entry contract.
public interface IEntry<K, V>
{
    K GetKey();

    V GetValue();

    V SetValue(V value);
}

// CLR: java.util.Iterator. IEnumerator<T> is the BCL iteration interface, but "Enumerators can be used to read the
// data in the collection, but they cannot be used to modify the underlying collection"
// (https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerator-1), and headers' value iterators
// remove the last returned element (Iterator.remove(): "Removes from the underlying collection the last element
// returned by this iterator (optional operation)",
// https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Iterator.html). As
// ConcurrentSkipListIntObjMultimap.Iter does, the Java hasNext()/next()/remove() shape is kept and IEnumerator<E> is
// also implemented, so foreach and BCL consumers work (MoveNext() is hasNext() then next()). next() past the end
// throws InvalidOperationException for NoSuchElementException, and remove() before next() throws it for
// IllegalStateException. E is covariant as IEnumerator<out T> is.
public interface IIterator<out E> : IEnumerator<E>
{
    bool HasNext();

    E Next();

    // CLR: Java 8's default remove(): "The default implementation throws an instance of
    // UnsupportedOperationException and performs no other action" (NotSupportedException here), as a C# default
    // interface member ("An interface can define a default implementation for a member",
    // https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/interface).
    void Remove()
    {
        throw new NotSupportedException("remove");
    }
}
