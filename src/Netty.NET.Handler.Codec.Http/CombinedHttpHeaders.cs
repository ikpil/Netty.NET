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
using static Netty.NET.Common.AsciiString;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Common.Internal.StringUtil;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Will add multiple values for the same header as single header with a comma separated list of values.
 * <p>
 * Please refer to section <a href="https://tools.ietf.org/html/rfc7230#section-3.2.2">RFC 7230, 3.2.2</a>.
 */
// CLR: The @Deprecated CombinedHttpHeaders(boolean validate) names
// DefaultHttpHeadersFactory.withCombiningHeaders(boolean) and is not ported.
// StringUtil.escapeCsv/unescapeCsvFields/trimOws take and return CharSequence in Java and string in the Common
// port (CSV rules unchanged), so the values are converted with ToString() and wrapped in StringCharSequence; a
// Java StringBuilder is a CharSequence, so the comma separated values are a StringCharSequence of the C#
// StringBuilder's text. Collection is the non-generic ICollection (List<T> implements it); BiPredicate is Func;
// IllegalArgumentException/IllegalStateException are ArgumentException/InvalidOperationException.
public class CombinedHttpHeaders : DefaultHttpHeaders
{
    internal CombinedHttpHeaders(DefaultHeaders.INameValidator<ICharSequence> nameValidator,
                                 DefaultHeaders.IValueValidator<ICharSequence> valueValidator)
        : base(new CombinedHttpHeadersImpl(
                CASE_INSENSITIVE_HASHER,
                ValueConverter(),
                CheckNotNull(nameValidator, "nameValidator"),
                CheckNotNull(valueValidator, "valueValidator")))
    {
    }

    internal CombinedHttpHeaders(
            DefaultHeaders.INameValidator<ICharSequence> nameValidator,
            DefaultHeaders.IValueValidator<ICharSequence> valueValidator, int sizeHint)
        : base(new CombinedHttpHeadersImpl(
                CASE_INSENSITIVE_HASHER,
                ValueConverter(),
                CheckNotNull(nameValidator, "nameValidator"),
                CheckNotNull(valueValidator, "valueValidator"),
                sizeHint))
    {
    }

    public override bool ContainsValue(ICharSequence name, ICharSequence value, bool ignoreCase)
    {
        return base.ContainsValue(name, new StringCharSequence(TrimOws(value.ToString())), ignoreCase);
    }

    private sealed class CombinedHttpHeadersImpl : HeadersImpl
    {
        /**
         * An estimate of the size of a header value.
         */
        private const int VALUE_LENGTH_ESTIMATE = 10;
        private ICsvValueEscaper<object> objectEscaper;
        private ICsvValueEscaper<ICharSequence> charSequenceEscaper;

        private ICsvValueEscaper<object> ObjectEscaper()
        {
            if (objectEscaper == null)
            {
                objectEscaper = new ObjectCsvValueEscaper(this);
            }

            return objectEscaper;
        }

        private sealed class ObjectCsvValueEscaper : ICsvValueEscaper<object>
        {
            private readonly CombinedHttpHeadersImpl headers;

            internal ObjectCsvValueEscaper(CombinedHttpHeadersImpl headers)
            {
                this.headers = headers;
            }

            public ICharSequence Escape(ICharSequence name, object value)
            {
                ICharSequence converted;

                try
                {
                    converted = headers.ValueConverter().ConvertObject(value);
                }
                catch (ArgumentException e)
                {
                    throw new ArgumentException(
                            "Failed to convert object value for header '" + name + '\'', e);
                }

                return new StringCharSequence(EscapeCsv(converted.ToString(), true));
            }
        }

        private ICsvValueEscaper<ICharSequence> CharSequenceEscaper()
        {
            if (charSequenceEscaper == null)
            {
                charSequenceEscaper = new CharSequenceCsvValueEscaper();
            }

            return charSequenceEscaper;
        }

        private sealed class CharSequenceCsvValueEscaper : ICsvValueEscaper<ICharSequence>
        {
            public ICharSequence Escape(ICharSequence name, ICharSequence value)
            {
                return new StringCharSequence(EscapeCsv(value.ToString(), true));
            }
        }

        internal CombinedHttpHeadersImpl(IEqualityComparer<ICharSequence> nameHashingStrategy,
                                IValueConverter<ICharSequence> valueConverter,
                                DefaultHeaders.INameValidator<ICharSequence> nameValidator,
                                DefaultHeaders.IValueValidator<ICharSequence> valueValidator)
            : this(nameHashingStrategy, valueConverter, nameValidator, valueValidator, 16)
        {
        }

        internal CombinedHttpHeadersImpl(IEqualityComparer<ICharSequence> nameHashingStrategy,
                                IValueConverter<ICharSequence> valueConverter,
                                DefaultHeaders.INameValidator<ICharSequence> nameValidator,
                                DefaultHeaders.IValueValidator<ICharSequence> valueValidator,
                                int sizeHint)
            : base(nameHashingStrategy, valueConverter, nameValidator, sizeHint, valueValidator)
        {
        }

        public override IIterator<ICharSequence> ValueIterator(ICharSequence name)
        {
            IIterator<ICharSequence> itr = base.ValueIterator(name);
            if (!itr.HasNext() || CannotBeCombined(name))
            {
                return itr;
            }

            IIterator<ICharSequence> unescapedItr = new ListIterator(UnescapeCsvFields(itr.Next()));
            if (itr.HasNext())
            {
                throw new InvalidOperationException("CombinedHttpHeaders should only have one value");
            }

            return unescapedItr;
        }

        public override bool ContainsAny(ICharSequence name, ICharSequence predicateArg,
                                   Func<ICharSequence, ICharSequence, bool> valuePredicate)
        {
            IIterator<ICharSequence> itr = ValueIterator(name);

            while (itr.HasNext())
            {
                if (valuePredicate(itr.Next(), predicateArg))
                {
                    return true;
                }
            }

            return false;
        }

        public override IList<ICharSequence> GetAll(ICharSequence name)
        {
            IList<ICharSequence> values = base.GetAll(name);
            if (values.Count == 0 || CannotBeCombined(name))
            {
                return values;
            }
            if (values.Count != 1)
            {
                throw new InvalidOperationException("CombinedHttpHeaders should only have one value");
            }

            return UnescapeCsvFields(values[0]);
        }

        public override CombinedHttpHeadersImpl Add<T2>(IHeaders<ICharSequence, ICharSequence, T2> headers)
        {
            // Override the fast-copy mechanism used by DefaultHeaders
            if (ReferenceEquals(headers, this))
            {
                throw new ArgumentException("can't add to itself.");
            }

            if (headers is CombinedHttpHeadersImpl)
            {
                if (IsEmpty())
                {
                    // Can use the fast underlying copy
                    AddImpl(headers);
                }
                else
                {
                    // Values are already escaped so don't escape again
                    foreach (IEntry<ICharSequence, ICharSequence> header in headers)
                    {
                        AddEscapedValue(header.GetKey(), header.GetValue());
                    }
                }
            }
            else
            {
                foreach (IEntry<ICharSequence, ICharSequence> header in headers)
                {
                    Add(header.GetKey(), header.GetValue());
                }
            }

            return this;
        }

        public override CombinedHttpHeadersImpl Set<T2>(IHeaders<ICharSequence, ICharSequence, T2> headers)
        {
            if (ReferenceEquals(headers, this))
            {
                return this;
            }

            Clear();

            return Add(headers);
        }

        public override CombinedHttpHeadersImpl SetAll<T2>(IHeaders<ICharSequence, ICharSequence, T2> headers)
        {
            if (ReferenceEquals(headers, this))
            {
                return this;
            }

            foreach (ICharSequence key in headers.Names())
            {
                Remove(key);
            }

            return Add(headers);
        }

        public override CombinedHttpHeadersImpl Add(ICharSequence name, ICharSequence value)
        {
            return AddEscapedValue(name, CharSequenceEscaper().Escape(name, value));
        }

        public override CombinedHttpHeadersImpl Add(ICharSequence name, params ICharSequence[] values)
        {
            return AddEscapedValue(name, CommaSeparate(name, CharSequenceEscaper(), values));
        }

        public override CombinedHttpHeadersImpl Add(ICharSequence name, IEnumerable<ICharSequence> values)
        {
            return AddEscapedValue(name, CommaSeparate(name, CharSequenceEscaper(), values));
        }

        public override CombinedHttpHeadersImpl AddObject(ICharSequence name, object value)
        {
            return AddEscapedValue(name, CommaSeparate(name, ObjectEscaper(), value));
        }

        public override CombinedHttpHeadersImpl AddObject(ICharSequence name, IEnumerable<object> values)
        {
            return AddEscapedValue(name, CommaSeparate(name, ObjectEscaper(), values));
        }

        public override CombinedHttpHeadersImpl AddObject(ICharSequence name, params object[] values)
        {
            return AddEscapedValue(name, CommaSeparate(name, ObjectEscaper(), values));
        }

        public override CombinedHttpHeadersImpl Set(ICharSequence name, params ICharSequence[] values)
        {
            Set(name, CommaSeparate(name, CharSequenceEscaper(), values));
            return this;
        }

        public override CombinedHttpHeadersImpl Set(ICharSequence name, IEnumerable<ICharSequence> values)
        {
            Set(name, CommaSeparate(name, CharSequenceEscaper(), values));
            return this;
        }

        public override CombinedHttpHeadersImpl SetObject(ICharSequence name, object value)
        {
            Set(name, CommaSeparate(name, ObjectEscaper(), value));
            return this;
        }

        public override CombinedHttpHeadersImpl SetObject(ICharSequence name, params object[] values)
        {
            Set(name, CommaSeparate(name, ObjectEscaper(), values));
            return this;
        }

        public override CombinedHttpHeadersImpl SetObject(ICharSequence name, IEnumerable<object> values)
        {
            Set(name, CommaSeparate(name, ObjectEscaper(), values));
            return this;
        }

        private static bool CannotBeCombined(ICharSequence name)
        {
            return SET_COOKIE.ContentEqualsIgnoreCase(name);
        }

        private CombinedHttpHeadersImpl AddEscapedValue(ICharSequence name, ICharSequence escapedValue)
        {
            ICharSequence currentValue = Get(name);
            if (currentValue == null || CannotBeCombined(name))
            {
                base.Add(name, escapedValue);
            }
            else
            {
                Set(name, CommaSeparateEscapedValues(currentValue, escapedValue));
            }

            return this;
        }

        private static ICharSequence CommaSeparate<T>(ICharSequence name, ICsvValueEscaper<T> escaper, params T[] values)
        {
            StringBuilder sb = new StringBuilder(values.Length * VALUE_LENGTH_ESTIMATE);

            if (values.Length > 0)
            {
                int end = values.Length - 1;

                for (int i = 0; i < end; i++)
                {
                    sb.Append(escaper.Escape(name, values[i])).Append(COMMA);
                }

                sb.Append(escaper.Escape(name, values[end]));
            }

            return new StringCharSequence(sb.ToString());
        }

        private static ICharSequence CommaSeparate<T>(ICharSequence name, ICsvValueEscaper<T> escaper,
                                                      IEnumerable<T> values)
        {
            StringBuilder sb = values is ICollection
                    ? new StringBuilder(((ICollection) values).Count * VALUE_LENGTH_ESTIMATE) : new StringBuilder();
            IEnumerator<T> iterator = values.GetEnumerator();
            if (iterator.MoveNext())
            {
                T next = iterator.Current;

                while (iterator.MoveNext())
                {
                    sb.Append(escaper.Escape(name, next)).Append(COMMA);
                    next = iterator.Current;
                }

                sb.Append(escaper.Escape(name, next));
            }

            return new StringCharSequence(sb.ToString());
        }

        private static ICharSequence CommaSeparateEscapedValues(ICharSequence currentValue, ICharSequence value)
        {
            return new StringCharSequence(new StringBuilder(currentValue.Length() + 1 + value.Length())
                    .Append(currentValue)
                    .Append(COMMA)
                    .Append(value).ToString());
        }

        // CLR: StringUtil.unescapeCsvFields(CharSequence) returns List<CharSequence>.
        private static IList<ICharSequence> UnescapeCsvFields(ICharSequence value)
        {
            List<string> fields = StringUtil.UnescapeCsvFields(value.ToString());
            List<ICharSequence> result = new List<ICharSequence>(fields.Count);

            foreach (string field in fields)
            {
                result.Add(new StringCharSequence(field));
            }

            return result;
        }

        private sealed class ListIterator : IIterator<ICharSequence>
        {
            private readonly IList<ICharSequence> list;
            private int next;
            private ICharSequence current;

            internal ListIterator(IList<ICharSequence> list)
            {
                this.list = list;
            }

            public bool HasNext()
            {
                return next < list.Count;
            }

            public ICharSequence Next()
            {
                if (!HasNext())
                {
                    throw new InvalidOperationException();
                }

                return list[next++];
            }

            public void Remove()
            {
                if (next == 0)
                {
                    throw new InvalidOperationException();
                }

                list.RemoveAt(--next);
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

        /**
         * Escapes comma separated values (CSV).
         *
         * @param <T> The type that a concrete implementation handles
         */
        private interface ICsvValueEscaper<T>
        {
            /**
             * Appends the value to the specified {@link StringBuilder}, escaping if necessary.
             *
             * @param name the name of the header for the value being escaped
             * @param value the value to be appended, escaped if necessary
             */
            ICharSequence Escape(ICharSequence name, T value);
        }
    }
}
