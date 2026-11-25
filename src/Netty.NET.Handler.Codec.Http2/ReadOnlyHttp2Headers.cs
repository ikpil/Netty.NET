/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Collections;
using static Netty.NET.Handler.Codec.Http2.DefaultHttp2Headers;
using static Netty.NET.Common.AsciiString;
using static Netty.NET.Common.Internal.EmptyArrays;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A variant of {@link Http2Headers} which only supports read-only methods.
 * <p>
 * Any array passed to this class may be used directly in the underlying data structures of this class. If these
 * arrays may be modified it is the caller's responsibility to supply this class with a copy of the array.
 * <p>
 * This may be a good alternative to {@link DefaultHttp2Headers} if your have a fixed set of headers which will not
 * change.
 */
// CLR: Http2Headers is IHttp2Headers over the codec-base IHeaders (docs/codec-base-porting.md, Headers): Map.Entry and
// Iterator are IEntry and IIterator, Iterable<? extends CharSequence>/Iterable<?> are IEnumerable<ICharSequence>/
// IEnumerable<object>, Headers<? extends CharSequence, ? extends CharSequence, ?> is the generic IHeaders<ICharSequence,
// ICharSequence, T2> parameter, the boxed getters return bool?/sbyte?/char?/short?/int?/long?/float?/double? (Java's
// byte is the codec-base sbyte), and GetEnumerator() (foreach) returns iterator(). HashingStrategy is
// IEqualityComparer and AsciiString.hashCode(CharSequence) is AsciiString.HashCode. AsciiString... is
// params AsciiString[]. UnsupportedOperationException is NotSupportedException and NoSuchElementException is
// InvalidOperationException (the IIterator contract); IllegalArgumentException is ArgumentException and the index in
// its message uses the invariant culture. String.valueOf(value) of the contains* methods is the
// CharSequenceValueConverter text of the value (Java's true/false and Double.toString text, invariant numbers), and
// value.toString() of containsObject is CharSequenceValueConverter.ConvertObject (null throws NullReferenceException
// where Java throws the NullPointerException of the dereference). LinkedHashSet is the Common LinkedHashSet and
// Collections.emptySet() is ImmutableHashSet.Empty. getClass().getSimpleName() is GetType().Name. The inner iterator
// classes read the enclosing instance's arrays, so their constructors take the ReadOnlyHttp2Headers. assert is
// Debug.Assert.
public sealed class ReadOnlyHttp2Headers : IHttp2Headers
{
    private const byte PSEUDO_HEADER_TOKEN = (byte) ':';
    private readonly AsciiString[] pseudoHeaders;
    private readonly AsciiString[] otherHeaders;

    /**
     * Used to create read only object designed to represent trailers.
     * <p>
     * If this is used for a purpose other than trailers you may violate the header serialization ordering defined by
     * <a href="https://tools.ietf.org/html/rfc7540#section-8.1.2.1">RFC 7540, 8.1.2.1</a>.
     * @param validateHeaders {@code true} will run validation on each header name/value pair to ensure protocol
     *                        compliance.
     * @param otherHeaders An array of key:value pairs. Must not contain any
     *                     <a href="https://tools.ietf.org/html/rfc7540#section-8.1.2.1">pseudo headers</a>
     *                     or {@code null} names/values.
     *                     A copy will <strong>NOT</strong> be made of this array. If the contents of this array
     *                     may be modified externally you are responsible for passing in a copy.
     * @return A read only representation of the headers.
     */
    public static ReadOnlyHttp2Headers Trailers(bool validateHeaders, params AsciiString[] otherHeaders)
    {
        return new ReadOnlyHttp2Headers(validateHeaders, EMPTY_ASCII_STRINGS, otherHeaders);
    }

    /**
     * Create a new read only representation of headers used by clients.
     * @param validateHeaders {@code true} will run validation on each header name/value pair to ensure protocol
     *                        compliance.
     * @param method The value for {@link PseudoHeaderName#METHOD}.
     * @param path The value for {@link PseudoHeaderName#PATH}.
     * @param scheme The value for {@link PseudoHeaderName#SCHEME}.
     * @param authority The value for {@link PseudoHeaderName#AUTHORITY}.
     * @param otherHeaders An array of key:value pairs. Must not contain any
     *                     <a href="https://tools.ietf.org/html/rfc7540#section-8.1.2.1">pseudo headers</a>
     *                     or {@code null} names/values.
     *                     A copy will <strong>NOT</strong> be made of this array. If the contents of this array
     *                     may be modified externally you are responsible for passing in a copy.
     * @return a new read only representation of headers used by clients.
     */
    public static ReadOnlyHttp2Headers ClientHeaders(bool validateHeaders,
                                                     AsciiString method, AsciiString path,
                                                     AsciiString scheme, AsciiString authority,
                                                     params AsciiString[] otherHeaders)
    {
        return new ReadOnlyHttp2Headers(validateHeaders,
                new AsciiString[] {
                  IHttp2Headers.PseudoHeaderName.METHOD.Value(), method,
                  IHttp2Headers.PseudoHeaderName.PATH.Value(), path,
                  IHttp2Headers.PseudoHeaderName.SCHEME.Value(), scheme,
                  IHttp2Headers.PseudoHeaderName.AUTHORITY.Value(), authority
                },
                otherHeaders);
    }

    /**
     * Create a new read only representation of headers used by servers.
     * @param validateHeaders {@code true} will run validation on each header name/value pair to ensure protocol
     *                        compliance.
     * @param status The value for {@link PseudoHeaderName#STATUS}.
     * @param otherHeaders An array of key:value pairs. Must not contain any
     *                     <a href="https://tools.ietf.org/html/rfc7540#section-8.1.2.1">pseudo headers</a>
     *                     or {@code null} names/values.
     *                     A copy will <strong>NOT</strong> be made of this array. If the contents of this array
     *                     may be modified externally you are responsible for passing in a copy.
     * @return a new read only representation of headers used by servers.
     */
    public static ReadOnlyHttp2Headers ServerHeaders(bool validateHeaders,
                                                     AsciiString status,
                                                     params AsciiString[] otherHeaders)
    {
        return new ReadOnlyHttp2Headers(validateHeaders,
                                        new AsciiString[] { IHttp2Headers.PseudoHeaderName.STATUS.Value(), status },
                                        otherHeaders);
    }

    private ReadOnlyHttp2Headers(bool validateHeaders, AsciiString[] pseudoHeaders, params AsciiString[] otherHeaders)
    {
        Debug.Assert((pseudoHeaders.Length & 1) == 0); // pseudoHeaders are only set internally so assert should be enough.
        if ((otherHeaders.Length & 1) != 0)
        {
            throw NewInvalidArraySizeException();
        }
        if (validateHeaders)
        {
            ValidateHeaders(pseudoHeaders, otherHeaders);
        }
        this.pseudoHeaders = pseudoHeaders;
        this.otherHeaders = otherHeaders;
    }

    private static ArgumentException NewInvalidArraySizeException()
    {
        return new ArgumentException("pseudoHeaders and otherHeaders must be arrays of [name, value] pairs");
    }

    private static void ValidateHeaders(AsciiString[] pseudoHeaders, params AsciiString[] otherHeaders)
    {
        // We are only validating values... so start at 1 and go until end.
        for (int i = 1; i < pseudoHeaders.Length; i += 2)
        {
            // pseudoHeaders names are only set internally so they are assumed to be valid.
            AsciiString value = pseudoHeaders[i];
            CheckNotNullArrayParam(value, i, "pseudoHeaders");
            DefaultHttp2ValueValidator().Validate(value);
        }

        bool seenNonPseudoHeader = false;
        int otherHeadersEnd = otherHeaders.Length - 1;
        for (int i = 0; i < otherHeadersEnd; i += 2)
        {
            AsciiString name = otherHeaders[i];
            DefaultHtt2NameValidator().ValidateName(name);
            if (!seenNonPseudoHeader && !name.IsEmpty() && name.ByteAt(0) != PSEUDO_HEADER_TOKEN)
            {
                seenNonPseudoHeader = true;
            }
            else if (seenNonPseudoHeader && !name.IsEmpty() && name.ByteAt(0) == PSEUDO_HEADER_TOKEN)
            {
                throw new ArgumentException(
                     "otherHeaders name at index " + i.ToString(CultureInfo.InvariantCulture) +
                     " is a pseudo header that appears after non-pseudo headers.");
            }
            AsciiString value = otherHeaders[i + 1];
            CheckNotNullArrayParam(value, i + 1, "otherHeaders");
            DefaultHttp2ValueValidator().Validate(value);
        }
    }

    private AsciiString Get0(ICharSequence name)
    {
        int nameHash = AsciiString.HashCode(name);

        int pseudoHeadersEnd = pseudoHeaders.Length - 1;
        for (int i = 0; i < pseudoHeadersEnd; i += 2)
        {
            AsciiString roName = pseudoHeaders[i];
            if (roName.GetHashCode() == nameHash && roName.ContentEqualsIgnoreCase(name))
            {
                return pseudoHeaders[i + 1];
            }
        }

        int otherHeadersEnd = otherHeaders.Length - 1;
        for (int i = 0; i < otherHeadersEnd; i += 2)
        {
            AsciiString roName = otherHeaders[i];
            if (roName.GetHashCode() == nameHash && roName.ContentEqualsIgnoreCase(name))
            {
                return otherHeaders[i + 1];
            }
        }
        return null;
    }

    public ICharSequence Get(ICharSequence name)
    {
        return Get0(name);
    }

    public ICharSequence Get(ICharSequence name, ICharSequence defaultValue)
    {
        ICharSequence value = Get(name);
        return value != null ? value : defaultValue;
    }

    public ICharSequence GetAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public ICharSequence GetAndRemove(ICharSequence name, ICharSequence defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public IList<ICharSequence> GetAll(ICharSequence name)
    {
        int nameHash = AsciiString.HashCode(name);
        List<ICharSequence> values = new List<ICharSequence>();

        int pseudoHeadersEnd = pseudoHeaders.Length - 1;
        for (int i = 0; i < pseudoHeadersEnd; i += 2)
        {
            AsciiString roName = pseudoHeaders[i];
            if (roName.GetHashCode() == nameHash && roName.ContentEqualsIgnoreCase(name))
            {
                values.Add(pseudoHeaders[i + 1]);
            }
        }

        int otherHeadersEnd = otherHeaders.Length - 1;
        for (int i = 0; i < otherHeadersEnd; i += 2)
        {
            AsciiString roName = otherHeaders[i];
            if (roName.GetHashCode() == nameHash && roName.ContentEqualsIgnoreCase(name))
            {
                values.Add(otherHeaders[i + 1]);
            }
        }

        return values;
    }

    public IList<ICharSequence> GetAllAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public bool? GetBoolean(ICharSequence name)
    {
        AsciiString value = Get0(name);
        return value != null ? CharSequenceValueConverter.INSTANCE.ConvertToBoolean(value) : null;
    }

    public bool GetBoolean(ICharSequence name, bool defaultValue)
    {
        bool? value = GetBoolean(name);
        return value != null ? value.Value : defaultValue;
    }

    public sbyte? GetByte(ICharSequence name)
    {
        AsciiString value = Get0(name);
        return value != null ? CharSequenceValueConverter.INSTANCE.ConvertToByte(value) : null;
    }

    public sbyte GetByte(ICharSequence name, sbyte defaultValue)
    {
        sbyte? value = GetByte(name);
        return value != null ? value.Value : defaultValue;
    }

    public char? GetChar(ICharSequence name)
    {
        AsciiString value = Get0(name);
        return value != null ? CharSequenceValueConverter.INSTANCE.ConvertToChar(value) : null;
    }

    public char GetChar(ICharSequence name, char defaultValue)
    {
        char? value = GetChar(name);
        return value != null ? value.Value : defaultValue;
    }

    public short? GetShort(ICharSequence name)
    {
        AsciiString value = Get0(name);
        return value != null ? CharSequenceValueConverter.INSTANCE.ConvertToShort(value) : null;
    }

    public short GetShort(ICharSequence name, short defaultValue)
    {
        short? value = GetShort(name);
        return value != null ? value.Value : defaultValue;
    }

    public int? GetInt(ICharSequence name)
    {
        AsciiString value = Get0(name);
        return value != null ? CharSequenceValueConverter.INSTANCE.ConvertToInt(value) : null;
    }

    public int GetInt(ICharSequence name, int defaultValue)
    {
        int? value = GetInt(name);
        return value != null ? value.Value : defaultValue;
    }

    public long? GetLong(ICharSequence name)
    {
        AsciiString value = Get0(name);
        return value != null ? CharSequenceValueConverter.INSTANCE.ConvertToLong(value) : null;
    }

    public long GetLong(ICharSequence name, long defaultValue)
    {
        long? value = GetLong(name);
        return value != null ? value.Value : defaultValue;
    }

    public float? GetFloat(ICharSequence name)
    {
        AsciiString value = Get0(name);
        return value != null ? CharSequenceValueConverter.INSTANCE.ConvertToFloat(value) : null;
    }

    public float GetFloat(ICharSequence name, float defaultValue)
    {
        float? value = GetFloat(name);
        return value != null ? value.Value : defaultValue;
    }

    public double? GetDouble(ICharSequence name)
    {
        AsciiString value = Get0(name);
        return value != null ? CharSequenceValueConverter.INSTANCE.ConvertToDouble(value) : null;
    }

    public double GetDouble(ICharSequence name, double defaultValue)
    {
        double? value = GetDouble(name);
        return value != null ? value.Value : defaultValue;
    }

    public long? GetTimeMillis(ICharSequence name)
    {
        AsciiString value = Get0(name);
        return value != null ? CharSequenceValueConverter.INSTANCE.ConvertToTimeMillis(value) : null;
    }

    public long GetTimeMillis(ICharSequence name, long defaultValue)
    {
        long? value = GetTimeMillis(name);
        return value != null ? value.Value : defaultValue;
    }

    public bool? GetBooleanAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public bool GetBooleanAndRemove(ICharSequence name, bool defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public sbyte? GetByteAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public sbyte GetByteAndRemove(ICharSequence name, sbyte defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public char? GetCharAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public char GetCharAndRemove(ICharSequence name, char defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public short? GetShortAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public short GetShortAndRemove(ICharSequence name, short defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public int? GetIntAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public int GetIntAndRemove(ICharSequence name, int defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public long? GetLongAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public long GetLongAndRemove(ICharSequence name, long defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public float? GetFloatAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public float GetFloatAndRemove(ICharSequence name, float defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public double? GetDoubleAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public double GetDoubleAndRemove(ICharSequence name, double defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public long? GetTimeMillisAndRemove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public long GetTimeMillisAndRemove(ICharSequence name, long defaultValue)
    {
        throw new NotSupportedException("read only");
    }

    public bool Contains(ICharSequence name)
    {
        return Get(name) != null;
    }

    public bool Contains(ICharSequence name, ICharSequence value)
    {
        return Contains(name, value, false);
    }

    public bool ContainsObject(ICharSequence name, object value)
    {
        if (value is ICharSequence)
        {
            return Contains(name, (ICharSequence) value);
        }
        return Contains(name, CharSequenceValueConverter.INSTANCE.ConvertObject(value));
    }

    public bool ContainsBoolean(ICharSequence name, bool value)
    {
        return Contains(name, CharSequenceValueConverter.INSTANCE.ConvertBoolean(value));
    }

    public bool ContainsByte(ICharSequence name, sbyte value)
    {
        return Contains(name, CharSequenceValueConverter.INSTANCE.ConvertByte(value));
    }

    public bool ContainsChar(ICharSequence name, char value)
    {
        return Contains(name, CharSequenceValueConverter.INSTANCE.ConvertChar(value));
    }

    public bool ContainsShort(ICharSequence name, short value)
    {
        return Contains(name, CharSequenceValueConverter.INSTANCE.ConvertShort(value));
    }

    public bool ContainsInt(ICharSequence name, int value)
    {
        return Contains(name, CharSequenceValueConverter.INSTANCE.ConvertInt(value));
    }

    public bool ContainsLong(ICharSequence name, long value)
    {
        return Contains(name, CharSequenceValueConverter.INSTANCE.ConvertLong(value));
    }

    public bool ContainsFloat(ICharSequence name, float value)
    {
        return false;
    }

    public bool ContainsDouble(ICharSequence name, double value)
    {
        return Contains(name, CharSequenceValueConverter.INSTANCE.ConvertDouble(value));
    }

    public bool ContainsTimeMillis(ICharSequence name, long value)
    {
        return Contains(name, CharSequenceValueConverter.INSTANCE.ConvertLong(value));
    }

    public int Size()
    {
        return pseudoHeaders.Length + otherHeaders.Length >>> 1;
    }

    public bool IsEmpty()
    {
        return pseudoHeaders.Length == 0 && otherHeaders.Length == 0;
    }

    public ISet<ICharSequence> Names()
    {
        if (IsEmpty())
        {
            return ImmutableHashSet<ICharSequence>.Empty;
        }
        ISet<ICharSequence> names = new LinkedHashSet<ICharSequence>(Size());
        int pseudoHeadersEnd = pseudoHeaders.Length - 1;
        for (int i = 0; i < pseudoHeadersEnd; i += 2)
        {
            names.Add(pseudoHeaders[i]);
        }

        int otherHeadersEnd = otherHeaders.Length - 1;
        for (int i = 0; i < otherHeadersEnd; i += 2)
        {
            names.Add(otherHeaders[i]);
        }
        return names;
    }

    public IHttp2Headers Add(ICharSequence name, ICharSequence value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Add(ICharSequence name, IEnumerable<ICharSequence> values)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Add(ICharSequence name, params ICharSequence[] values)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddObject(ICharSequence name, object value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddObject(ICharSequence name, IEnumerable<object> values)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddObject(ICharSequence name, params object[] values)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddBoolean(ICharSequence name, bool value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddByte(ICharSequence name, sbyte value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddChar(ICharSequence name, char value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddShort(ICharSequence name, short value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddInt(ICharSequence name, int value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddLong(ICharSequence name, long value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddFloat(ICharSequence name, float value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddDouble(ICharSequence name, double value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers AddTimeMillis(ICharSequence name, long value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Add<T2>(IHeaders<ICharSequence, ICharSequence, T2> headers)
        where T2 : IHeaders<ICharSequence, ICharSequence, T2>
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Set(ICharSequence name, ICharSequence value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Set(ICharSequence name, IEnumerable<ICharSequence> values)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Set(ICharSequence name, params ICharSequence[] values)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetObject(ICharSequence name, object value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetObject(ICharSequence name, IEnumerable<object> values)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetObject(ICharSequence name, params object[] values)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetBoolean(ICharSequence name, bool value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetByte(ICharSequence name, sbyte value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetChar(ICharSequence name, char value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetShort(ICharSequence name, short value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetInt(ICharSequence name, int value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetLong(ICharSequence name, long value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetFloat(ICharSequence name, float value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetDouble(ICharSequence name, double value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetTimeMillis(ICharSequence name, long value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Set<T2>(IHeaders<ICharSequence, ICharSequence, T2> headers)
        where T2 : IHeaders<ICharSequence, ICharSequence, T2>
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers SetAll<T2>(IHeaders<ICharSequence, ICharSequence, T2> headers)
        where T2 : IHeaders<ICharSequence, ICharSequence, T2>
    {
        throw new NotSupportedException("read only");
    }

    public bool Remove(ICharSequence name)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Clear()
    {
        throw new NotSupportedException("read only");
    }

    public IIterator<IEntry<ICharSequence, ICharSequence>> Iterator()
    {
        return new ReadOnlyIterator(this);
    }

    public IEnumerator<IEntry<ICharSequence, ICharSequence>> GetEnumerator()
    {
        return Iterator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IIterator<ICharSequence> ValueIterator(ICharSequence name)
    {
        return new ReadOnlyValueIterator(this, name);
    }

    public IHttp2Headers Method(ICharSequence value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Scheme(ICharSequence value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Authority(ICharSequence value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Path(ICharSequence value)
    {
        throw new NotSupportedException("read only");
    }

    public IHttp2Headers Status(ICharSequence value)
    {
        throw new NotSupportedException("read only");
    }

    public ICharSequence Method()
    {
        return Get(IHttp2Headers.PseudoHeaderName.METHOD.Value());
    }

    public ICharSequence Scheme()
    {
        return Get(IHttp2Headers.PseudoHeaderName.SCHEME.Value());
    }

    public ICharSequence Authority()
    {
        return Get(IHttp2Headers.PseudoHeaderName.AUTHORITY.Value());
    }

    public ICharSequence Path()
    {
        return Get(IHttp2Headers.PseudoHeaderName.PATH.Value());
    }

    public ICharSequence Status()
    {
        return Get(IHttp2Headers.PseudoHeaderName.STATUS.Value());
    }

    public bool Contains(ICharSequence name, ICharSequence value, bool caseInsensitive)
    {
        int nameHash = AsciiString.HashCode(name);
        IEqualityComparer<ICharSequence> strategy =
                caseInsensitive ? CASE_INSENSITIVE_HASHER : CASE_SENSITIVE_HASHER;
        int valueHash = strategy.GetHashCode(value);

        return Contains(name, nameHash, value, valueHash, strategy, otherHeaders)
                || Contains(name, nameHash, value, valueHash, strategy, pseudoHeaders);
    }

    private static bool Contains(ICharSequence name, int nameHash, ICharSequence value, int valueHash,
                                    IEqualityComparer<ICharSequence> hashingStrategy, AsciiString[] headers)
    {
        int headersEnd = headers.Length - 1;
        for (int i = 0; i < headersEnd; i += 2)
        {
            AsciiString roName = headers[i];
            AsciiString roValue = headers[i + 1];
            if (roName.GetHashCode() == nameHash && roValue.GetHashCode() == valueHash &&
                roName.ContentEqualsIgnoreCase(name) && hashingStrategy.Equals(roValue, value))
            {
                return true;
            }
        }
        return false;
    }

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder(GetType().Name).Append('[');
        string separator = "";
        foreach (IEntry<ICharSequence, ICharSequence> entry in this)
        {
            builder.Append(separator);
            builder.Append(entry.GetKey()).Append(": ").Append(entry.GetValue());
            separator = ", ";
        }
        return builder.Append(']').ToString();
    }

    private sealed class ReadOnlyValueIterator : IIterator<ICharSequence>
    {
        private readonly ReadOnlyHttp2Headers headers;
        private int i;
        private readonly int nameHash;
        private readonly ICharSequence name;
        private AsciiString[] current;
        private AsciiString next;
        // CLR: the IEnumerator<T> side of the iterator (IIterator, docs/codec-base-porting.md, Headers).
        private ICharSequence enumeratorCurrent;

        internal ReadOnlyValueIterator(ReadOnlyHttp2Headers headers, ICharSequence name)
        {
            this.headers = headers;
            current = headers.pseudoHeaders.Length != 0 ? headers.pseudoHeaders : headers.otherHeaders;
            nameHash = AsciiString.HashCode(name);
            this.name = name;
            CalculateNext();
        }

        public bool HasNext()
        {
            return next != null;
        }

        public ICharSequence Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }
            ICharSequence current = next;
            CalculateNext();
            return current;
        }

        public void Remove()
        {
            throw new NotSupportedException("read only");
        }

        private void CalculateNext()
        {
            for (; i < current.Length; i += 2)
            {
                AsciiString roName = current[i];
                if (roName.GetHashCode() == nameHash && roName.ContentEqualsIgnoreCase(name))
                {
                    if (i + 1 < current.Length)
                    {
                        next = current[i + 1];
                        i += 2;
                    }
                    return;
                }
            }
            if (current == headers.pseudoHeaders)
            {
                i = 0;
                current = headers.otherHeaders;
                CalculateNext();
            }
            else
            {
                next = null;
            }
        }

        public ICharSequence Current => enumeratorCurrent;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }
            enumeratorCurrent = Next();
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

    private sealed class ReadOnlyIterator : IEntry<ICharSequence, ICharSequence>,
                                                    IIterator<IEntry<ICharSequence, ICharSequence>>
    {
        private readonly ReadOnlyHttp2Headers headers;
        private int i;
        private AsciiString[] current;
        private AsciiString key;
        private AsciiString value;
        // CLR: the IEnumerator<T> side of the iterator (IIterator, docs/codec-base-porting.md, Headers).
        private bool started;

        internal ReadOnlyIterator(ReadOnlyHttp2Headers headers)
        {
            this.headers = headers;
            current = headers.pseudoHeaders.Length != 0 ? headers.pseudoHeaders : headers.otherHeaders;
        }

        public bool HasNext()
        {
            return i != current.Length;
        }

        public IEntry<ICharSequence, ICharSequence> Next()
        {
            if (!HasNext())
            {
                throw new InvalidOperationException();
            }
            key = current[i];
            value = current[i + 1];
            i += 2;
            if (i == current.Length && current == headers.pseudoHeaders)
            {
                current = headers.otherHeaders;
                i = 0;
            }
            started = true;
            return this;
        }

        public ICharSequence GetKey()
        {
            return key;
        }

        public ICharSequence GetValue()
        {
            return value;
        }

        public ICharSequence SetValue(ICharSequence value)
        {
            throw new NotSupportedException("read only");
        }

        public void Remove()
        {
            throw new NotSupportedException("read only");
        }

        public override string ToString()
        {
            return key.ToString() + '=' + value.ToString();
        }

        public IEntry<ICharSequence, ICharSequence> Current => started ? this : null;

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
}
