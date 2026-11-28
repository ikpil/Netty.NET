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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.AsciiString;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Default implementation of {@link HttpHeaders}.
 */
// CLR: the wrapped DefaultHeaders<CharSequence, CharSequence, ?> uses a wildcard for the type that the headers return
// for "this", which C# cannot express. The headers are a DefaultHeaders<ICharSequence, ICharSequence, HeadersImpl>,
// where HeadersImpl is a public abstract nested class (CLR-only) that the default headers (DefaultHeadersImpl in Java,
// the private nested DefaultImpl here) and CombinedHttpHeaders' CombinedHttpHeadersImpl derive from. copy() is
// new DefaultHttpHeaders(headers.copy()) as in Java: DefaultHeaders.Copy() creates a DefaultHeaders<K, V, T> of its own
// exact type, which is not a HeadersImpl, and whose this-returning members return null instead of failing, as Java's
// unchecked (T) this fails only where the result is used (see DefaultHeaders.ThisT); DefaultHttpHeaders ignores those
// results, and, as in Java, the copy of combined headers is a plain DefaultHeaders. The @Deprecated
// DefaultHttpHeaders(boolean validate) names DefaultHttpHeaders() and is not ported
// (owner decision 2026-11-16); the deprecated iterator() is IteratorAsString() (see HttpHeaders). The
// BiPredicate<CharSequence, CharSequence> constants are Func<ICharSequence, ICharSequence, bool>; the static imports
// CASE_INSENSITIVE_HASHER/CASE_SENSITIVE_HASHER are the Common AsciiString comparers. In HeaderValueConverter, a C#
// string (a CharSequence in Java, but not an ICharSequence) becomes a StringCharSequence, java.util.Date is
// DateTimeOffset (docs/codec-base-porting.md, Headers) and java.util.Calendar is DateTime: Calendar.getTime() is the
// instant of the DateTimeOffset(DateTime) constructor, which keeps a Utc DateTime as UTC and reads a Local or
// Unspecified one in the local time zone
// (https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.-ctor?view=net-10.0). The package-private
// static helpers are internal. Equals/GetHashCode are the Java equals/hashCode.
public class DefaultHttpHeaders : HttpHeaders
{
    private static readonly Func<ICharSequence, ICharSequence, bool> CASE_INSENSITIVE_CONTAINS =
            (value, expected) =>
                    HttpHeaders.ContainsCommaSeparatedTrimmed(value, expected, true);
    private static readonly Func<ICharSequence, ICharSequence, bool> CASE_SENSITIVE_CONTAINS =
            (value, expected) =>
                    HttpHeaders.ContainsCommaSeparatedTrimmed(value, expected, false);

    private readonly DefaultHeaders<ICharSequence, ICharSequence, HeadersImpl> headers;

    /**
     * Create a new, empty HTTP headers object.
     * <p>
     * Header names and values are validated as they are added, to ensure they are compliant with the HTTP protocol.
     */
    public DefaultHttpHeaders() : this(NameValidator(true), ValueValidator(true))
    {
    }

    /**
     * Create an HTTP headers object with the given name validator.
     * <p>
     * <b>Warning!</b> It is strongly recommended that the name validator implement validation that is at least as
     * strict as {@link HttpHeaderValidationUtil#validateToken(CharSequence)}.
     * It is also strongly recommended that {@code validateValues} is enabled.
     * <p>
     * Without these validations in place, your code can be susceptible to
     * <a href="https://cwe.mitre.org/data/definitions/113.html">
     *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
     * </a>.
     * It is the responsibility of the caller to ensure that the values supplied
     * do not contain a non-url-escaped carriage return (CR) and/or line feed (LF) characters.
     *
     * @param validateValues Should Netty validate header values to ensure they aren't malicious.
     * @param nameValidator The {@link NameValidator} to use, never {@code null.
     */
    protected internal DefaultHttpHeaders(bool validateValues, DefaultHeaders.INameValidator<ICharSequence> nameValidator)
        : this(nameValidator, ValueValidator(validateValues))
    {
    }

    /**
     * Create an HTTP headers object with the given name and value validators.
     * <p>
     * <b>Warning!</b> It is strongly recommended that the name validator implement validation that is at least as
     * strict as {@link HttpHeaderValidationUtil#validateToken(CharSequence)}.
     * And that the value validator is at least as strict as
     * {@link HttpHeaderValidationUtil#validateValidHeaderValue(CharSequence)}.
     * <p>
     * Without these validations in place, your code can be susceptible to
     * <a href="https://cwe.mitre.org/data/definitions/113.html">
     *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
     * </a>.
     * It is the responsibility of the caller to ensure that the values supplied
     * do not contain a non-url-escaped carriage return (CR) and/or line feed (LF) characters.
     *
     * @param nameValidator The {@link NameValidator} to use, never {@code null}.
     * @param valueValidator The {@link ValueValidator} to use, never {@code null}.
     */
    protected internal DefaultHttpHeaders(
            DefaultHeaders.INameValidator<ICharSequence> nameValidator,
            DefaultHeaders.IValueValidator<ICharSequence> valueValidator)
        : this(nameValidator, valueValidator, 16)
    {
    }

    /**
     * Create an HTTP headers object with the given name and value validators.
     * <p>
     * <b>Warning!</b> It is strongly recommended that the name validator implement validation that is at least as
     * strict as {@link HttpHeaderValidationUtil#validateToken(CharSequence)}.
     * And that the value validator is at least as strict as
     * {@link HttpHeaderValidationUtil#validateValidHeaderValue(CharSequence)}.
     * <p>
     * Without these validations in place, your code can be susceptible to
     * <a href="https://cwe.mitre.org/data/definitions/113.html">
     *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
     * </a>.
     * It is the responsibility of the caller to ensure that the values supplied
     * do not contain a non-url-escaped carriage return (CR) and/or line feed (LF) characters.
     *
     * @param nameValidator The {@link NameValidator} to use, never {@code null}.
     * @param valueValidator The {@link ValueValidator} to use, never {@code null}.
     * @param sizeHint A hint about the anticipated number of entries.
     */
    protected internal DefaultHttpHeaders(
            DefaultHeaders.INameValidator<ICharSequence> nameValidator,
            DefaultHeaders.IValueValidator<ICharSequence> valueValidator,
            int sizeHint)
        : this(new DefaultImpl(
                CASE_INSENSITIVE_HASHER,
                HeaderValueConverter.INSTANCE,
                nameValidator,
                sizeHint,
                valueValidator))
    {
    }

    protected DefaultHttpHeaders(DefaultHeaders<ICharSequence, ICharSequence, HeadersImpl> headers)
    {
        this.headers = headers;
    }

    public IHeaders<ICharSequence, ICharSequence, HeadersImpl> Unwrap()
    {
        return headers;
    }

    public override HttpHeaders Add(HttpHeaders headers)
    {
        if (headers is DefaultHttpHeaders)
        {
            this.headers.Add(((DefaultHttpHeaders) headers).headers);
            return this;
        }
        else
        {
            return base.Add(headers);
        }
    }

    public override HttpHeaders Set(HttpHeaders headers)
    {
        if (headers is DefaultHttpHeaders)
        {
            this.headers.Set(((DefaultHttpHeaders) headers).headers);
            return this;
        }
        else
        {
            return base.Set(headers);
        }
    }

    public override HttpHeaders Add(string name, object value)
    {
        headers.AddObject(Of(name), value);
        return this;
    }

    public override HttpHeaders Add(ICharSequence name, object value)
    {
        headers.AddObject(name, value);
        return this;
    }

    public override HttpHeaders Add(string name, IEnumerable<object> values)
    {
        headers.AddObject(Of(name), values);
        return this;
    }

    public override HttpHeaders Add(ICharSequence name, IEnumerable<object> values)
    {
        headers.AddObject(name, values);
        return this;
    }

    public override HttpHeaders AddInt(ICharSequence name, int value)
    {
        headers.AddInt(name, value);
        return this;
    }

    public override HttpHeaders AddShort(ICharSequence name, short value)
    {
        headers.AddShort(name, value);
        return this;
    }

    public override HttpHeaders Remove(string name)
    {
        headers.Remove(Of(name));
        return this;
    }

    public override HttpHeaders Remove(ICharSequence name)
    {
        headers.Remove(name);
        return this;
    }

    public override HttpHeaders Set(string name, object value)
    {
        headers.SetObject(Of(name), value);
        return this;
    }

    public override HttpHeaders Set(ICharSequence name, object value)
    {
        headers.SetObject(name, value);
        return this;
    }

    public override HttpHeaders Set(string name, IEnumerable<object> values)
    {
        headers.SetObject(Of(name), values);
        return this;
    }

    public override HttpHeaders Set(ICharSequence name, IEnumerable<object> values)
    {
        headers.SetObject(name, values);
        return this;
    }

    public override HttpHeaders SetInt(ICharSequence name, int value)
    {
        headers.SetInt(name, value);
        return this;
    }

    public override HttpHeaders SetShort(ICharSequence name, short value)
    {
        headers.SetShort(name, value);
        return this;
    }

    public override HttpHeaders Clear()
    {
        headers.Clear();
        return this;
    }

    public override string Get(string name)
    {
        return Get(Of(name));
    }

    public override string Get(ICharSequence name)
    {
        return HeadersUtils.GetAsString(headers, name);
    }

    public override int? GetInt(ICharSequence name)
    {
        return headers.GetInt(name);
    }

    public override int GetInt(ICharSequence name, int defaultValue)
    {
        return headers.GetInt(name, defaultValue);
    }

    public override short? GetShort(ICharSequence name)
    {
        return headers.GetShort(name);
    }

    public override short GetShort(ICharSequence name, short defaultValue)
    {
        return headers.GetShort(name, defaultValue);
    }

    public override long? GetTimeMillis(ICharSequence name)
    {
        return headers.GetTimeMillis(name);
    }

    public override long GetTimeMillis(ICharSequence name, long defaultValue)
    {
        return headers.GetTimeMillis(name, defaultValue);
    }

    public override IList<string> GetAll(string name)
    {
        return GetAll(Of(name));
    }

    public override IList<string> GetAll(ICharSequence name)
    {
        return HeadersUtils.GetAllAsString(headers, name);
    }

    public override IList<IEntry<string, string>> Entries()
    {
        if (IsEmpty())
        {
            return Array.Empty<IEntry<string, string>>();
        }

        IList<IEntry<string, string>> entriesConverted = new List<IEntry<string, string>>(
                headers.Size());

        foreach (IEntry<string, string> entry in this)
        {
            entriesConverted.Add(entry);
        }

        return entriesConverted;
    }

    public override IIterator<IEntry<string, string>> IteratorAsString()
    {
        return HeadersUtils.IteratorAsString(headers);
    }

    public override IIterator<IEntry<ICharSequence, ICharSequence>> IteratorCharSequence()
    {
        return headers.Iterator();
    }

    public override IIterator<string> ValueStringIterator(ICharSequence name)
    {
        IIterator<ICharSequence> itr = ValueCharSequenceIterator(name);
        return new StringValueIterator(itr);
    }

    // CLR: the anonymous Iterator<String> of valueStringIterator(CharSequence).
    private sealed class StringValueIterator : IIterator<string>
    {
        private readonly IIterator<ICharSequence> itr;
        private string current;

        internal StringValueIterator(IIterator<ICharSequence> itr)
        {
            this.itr = itr;
        }

        public bool HasNext()
        {
            return itr.HasNext();
        }

        public string Next()
        {
            return itr.Next().ToString();
        }

        public void Remove()
        {
            itr.Remove();
        }

        public string Current => current;

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

    public override IIterator<ICharSequence> ValueCharSequenceIterator(ICharSequence name)
    {
        return headers.ValueIterator(name);
    }

    public override bool ContainsValue(ICharSequence name, ICharSequence value, bool ignoreCase)
    {
        return headers.ContainsAny(name, value, ignoreCase ? CASE_INSENSITIVE_CONTAINS : CASE_SENSITIVE_CONTAINS);
    }

    public override bool Contains(string name)
    {
        return Contains(Of(name));
    }

    public override bool Contains(ICharSequence name)
    {
        return headers.Contains(name);
    }

    public override bool IsEmpty()
    {
        return headers.IsEmpty();
    }

    public override int Size()
    {
        return headers.Size();
    }

    public override bool Contains(string name, string value, bool ignoreCase)
    {
        return Contains(Of(name), Of(value), ignoreCase);
    }

    public override bool Contains(ICharSequence name, ICharSequence value, bool ignoreCase)
    {
        return headers.Contains(name, value, ignoreCase ? CASE_INSENSITIVE_HASHER : CASE_SENSITIVE_HASHER);
    }

    public override ISet<string> Names()
    {
        return HeadersUtils.NamesAsString(headers);
    }

    public override bool Equals(object o)
    {
        return o is DefaultHttpHeaders
                && headers.Equals(((DefaultHttpHeaders) o).headers, CASE_SENSITIVE_HASHER);
    }

    public override int GetHashCode()
    {
        return headers.HashCode(CASE_SENSITIVE_HASHER);
    }

    public override HttpHeaders Copy()
    {
        return new DefaultHttpHeaders(headers.Copy());
    }

    // CLR: a Java String is a CharSequence; a C# string is wrapped in a StringCharSequence, and null stays null so that
    // the name validator rejects it with IllegalArgumentException (ArgumentException) as in Java, instead of the
    // StringCharSequence constructor throwing ArgumentNullException.
    private static ICharSequence Of(string s)
    {
        return s == null ? null : new StringCharSequence(s);
    }

    internal static IValueConverter<ICharSequence> ValueConverter()
    {
        return HeaderValueConverter.INSTANCE;
    }

    internal static DefaultHeaders.IValueValidator<ICharSequence> ValueValidator(bool validate)
    {
        return validate ? DefaultHttpHeadersFactory.HeadersFactory().GetValueValidator() :
                DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).GetValueValidator();
    }

    internal static DefaultHeaders.INameValidator<ICharSequence> NameValidator(bool validate)
    {
        return validate ? DefaultHttpHeadersFactory.HeadersFactory().GetNameValidator() :
                DefaultHttpHeadersFactory.HeadersFactory().WithNameValidation(false).GetNameValidator();
    }

    private class HeaderValueConverter : CharSequenceValueConverter
    {
        internal static new readonly HeaderValueConverter INSTANCE = new HeaderValueConverter();

        public override ICharSequence ConvertObject(object value)
        {
            if (value is ICharSequence)
            {
                return (ICharSequence) value;
            }
            if (value is string)
            {
                return new StringCharSequence((string) value);
            }
            if (value is DateTimeOffset)
            {
                return new StringCharSequence(DateFormatter.Format((DateTimeOffset) value));
            }
            if (value is DateTime)
            {
                return new StringCharSequence(DateFormatter.Format(new DateTimeOffset((DateTime) value)));
            }

            return base.ConvertObject(value);
        }
    }

    // CLR: the T of the wrapped DefaultHeaders (see the class comment); it inherits DefaultHeaders.Copy(), as Java's
    // DefaultHeadersImpl and CombinedHttpHeadersImpl do.
    public abstract class HeadersImpl : DefaultHeaders<ICharSequence, ICharSequence, HeadersImpl>
    {
        internal HeadersImpl(IEqualityComparer<ICharSequence> nameHashingStrategy,
                             IValueConverter<ICharSequence> valueConverter,
                             DefaultHeaders.INameValidator<ICharSequence> nameValidator, int arraySizeHint,
                             DefaultHeaders.IValueValidator<ICharSequence> valueValidator)
            : base(nameHashingStrategy, valueConverter, nameValidator, arraySizeHint, valueValidator)
        {
        }
    }

    // CLR: io.netty.handler.codec.DefaultHeadersImpl<CharSequence, CharSequence> (see the class comment).
    private sealed class DefaultImpl : HeadersImpl
    {
        internal DefaultImpl(IEqualityComparer<ICharSequence> nameHashingStrategy,
                             IValueConverter<ICharSequence> valueConverter,
                             DefaultHeaders.INameValidator<ICharSequence> nameValidator, int arraySizeHint,
                             DefaultHeaders.IValueValidator<ICharSequence> valueValidator)
            : base(nameHashingStrategy, valueConverter, nameValidator, arraySizeHint, valueValidator)
        {
        }
    }
}
