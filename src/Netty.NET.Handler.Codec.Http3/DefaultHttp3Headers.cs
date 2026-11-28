/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http3.IHttp3Headers.PseudoHeaderName;
using static Netty.NET.Common.AsciiString;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: HashingStrategy is IEqualityComparer and the AsciiString CASE_SENSITIVE_HASHER/CASE_INSENSITIVE_HASHER are
// the Common comparers. The final class is sealed, so the Java public methods are not virtual. The inner class
// Http3HeaderEntry reads the enclosing instance's firstNonPseudo and head, so its constructor takes the
// DefaultHttp3Headers (a C# nested class has no enclosing instance), and the field initializer firstNonPseudo =
// head is an assignment in the constructors that call the base constructor, because a C# field initializer cannot
// read an instance field (CS0236, https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236). The protected
// members of the base class are protected (they are protected internal in codec-base, another assembly), except
// HeaderEntry.Remove(): codec-base gives this assembly its internals (InternalsVisibleTo, for
// ExecutorPromiseCombiner), and an override of a protected internal member it can see keeps both modifiers
// (CS0507). String.format("...[%s]", name) keeps the Java text, "null" for a null name; catch
// (Http3HeadersValidationException e) { throw e; } is throw; and Throwable is Exception. The @Nullable annotation
// has no counterpart (no nullable annotation context).
public sealed class DefaultHttp3Headers
        : DefaultHeaders<ICharSequence, ICharSequence, IHttp3Headers>, IHttp3Headers
{
    private static readonly Func<byte, bool> HTTP3_NAME_VALIDATOR_PROCESSOR = static value => !IsUpperCase(value);
    internal static readonly DefaultHeaders.INameValidator<ICharSequence> HTTP3_NAME_VALIDATOR = new Http3NameValidator();

    private sealed class Http3NameValidator : DefaultHeaders.INameValidator<ICharSequence>
    {
        public void ValidateName(ICharSequence name)
        {
            if (name == null || name.Length() == 0)
            {
                throw new Http3HeadersValidationException("empty headers are not allowed [" + ToJavaString(name) + "]");
            }

            if (name is AsciiString)
            {
                int index;

                try
                {
                    index = ((AsciiString) name).ForEachByte(HTTP3_NAME_VALIDATOR_PROCESSOR);
                }
                catch (Http3HeadersValidationException)
                {
                    throw;
                }
                catch (Exception t)
                {
                    throw new Http3HeadersValidationException(
                            "unexpected error. invalid header name [" + ToJavaString(name) + "]", t);
                }

                if (index != -1)
                {
                    throw new Http3HeadersValidationException("invalid header name [" + ToJavaString(name) + "]");
                }
            }
            else
            {
                for (int i = 0; i < name.Length(); ++i)
                {
                    if (IsUpperCase(name.CharAt(i)))
                    {
                        throw new Http3HeadersValidationException("invalid header name [" + ToJavaString(name) + "]");
                    }
                }
            }
        }

        // CLR: the %s text of String.format: "null" for null, otherwise toString().
        private static string ToJavaString(ICharSequence name)
        {
            return name == null ? "null" : name.ToString();
        }
    }

    private HeaderEntry firstNonPseudo;

    /**
     * Create a new instance.
     * <p>
     * Header names will be validated according to
     * <a href="https://tools.ietf.org/html/rfc7540">rfc7540</a>.
     */
    public DefaultHttp3Headers() : this(true)
    {
    }

    /**
     * Create a new instance.
     * @param validate {@code true} to validate header names according to
     * <a href="https://tools.ietf.org/html/rfc7540">rfc7540</a>. {@code false} to not validate header names.
     */
    public DefaultHttp3Headers(bool validate)
        // Case sensitive compare is used because it is cheaper, and header validation can be used to catch invalid
        // headers.
        : base(CASE_SENSITIVE_HASHER,
              CharSequenceValueConverter.INSTANCE,
              validate ? HTTP3_NAME_VALIDATOR : DefaultHeaders.INameValidator<ICharSequence>.NOT_NULL)
    {
        firstNonPseudo = head;
    }

    /**
     * Create a new instance.
     * @param validate {@code true} to validate header names according to
     * <a href="https://tools.ietf.org/html/rfc7540">rfc7540</a>. {@code false} to not validate header names.
     * @param arraySizeHint A hint as to how large the hash data structure should be.
     * The next positive power of two will be used. An upper bound may be enforced.
     */
    public DefaultHttp3Headers(bool validate, int arraySizeHint)
        // Case sensitive compare is used because it is cheaper, and header validation can be used to catch invalid
        // headers.
        : base(CASE_SENSITIVE_HASHER,
              CharSequenceValueConverter.INSTANCE,
              validate ? HTTP3_NAME_VALIDATOR : DefaultHeaders.INameValidator<ICharSequence>.NOT_NULL,
              arraySizeHint)
    {
        firstNonPseudo = head;
    }

    public override IHttp3Headers Clear()
    {
        this.firstNonPseudo = head;
        return base.Clear();
    }

    public override bool Equals(object o)
    {
        return o is IHttp3Headers && Equals((IHttp3Headers) o, CASE_SENSITIVE_HASHER);
    }

    public override int GetHashCode()
    {
        return HashCode(CASE_SENSITIVE_HASHER);
    }

    public IHttp3Headers Method(ICharSequence value)
    {
        Set(IHttp3Headers.PseudoHeaderName.METHOD.Value(), value);
        return this;
    }

    public IHttp3Headers Scheme(ICharSequence value)
    {
        Set(IHttp3Headers.PseudoHeaderName.SCHEME.Value(), value);
        return this;
    }

    public IHttp3Headers Authority(ICharSequence value)
    {
        Set(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value(), value);
        return this;
    }

    public IHttp3Headers Path(ICharSequence value)
    {
        Set(IHttp3Headers.PseudoHeaderName.PATH.Value(), value);
        return this;
    }

    public IHttp3Headers Status(ICharSequence value)
    {
        Set(IHttp3Headers.PseudoHeaderName.STATUS.Value(), value);
        return this;
    }

    public ICharSequence Method()
    {
        return Get(IHttp3Headers.PseudoHeaderName.METHOD.Value());
    }

    public ICharSequence Scheme()
    {
        return Get(IHttp3Headers.PseudoHeaderName.SCHEME.Value());
    }

    public ICharSequence Authority()
    {
        return Get(IHttp3Headers.PseudoHeaderName.AUTHORITY.Value());
    }

    public ICharSequence Path()
    {
        return Get(IHttp3Headers.PseudoHeaderName.PATH.Value());
    }

    public ICharSequence Status()
    {
        return Get(IHttp3Headers.PseudoHeaderName.STATUS.Value());
    }

    public override bool Contains(ICharSequence name, ICharSequence value)
    {
        return Contains(name, value, false);
    }

    public bool Contains(ICharSequence name, ICharSequence value, bool caseInsensitive)
    {
        return Contains(name, value, caseInsensitive ? CASE_INSENSITIVE_HASHER : CASE_SENSITIVE_HASHER);
    }

    protected override HeaderEntry NewHeaderEntry(int h, ICharSequence name, ICharSequence value,
                                                           HeaderEntry next)
    {
        return new Http3HeaderEntry(this, h, name, value, next);
    }

    private sealed class Http3HeaderEntry : HeaderEntry
    {
        private readonly DefaultHttp3Headers headers;

        internal Http3HeaderEntry(DefaultHttp3Headers headers, int hash, ICharSequence key, ICharSequence value,
                HeaderEntry next) : base(hash, key)
        {
            this.headers = headers;
            this.value = value;
            this.next = next;

            // Make sure the pseudo headers fields are first in iteration order
            if (HasPseudoHeaderFormat(key))
            {
                after = headers.firstNonPseudo;
                before = headers.firstNonPseudo.Before();
            }
            else
            {
                after = headers.head;
                before = headers.head.Before();

                if (headers.firstNonPseudo == headers.head)
                {
                    headers.firstNonPseudo = this;
                }
            }

            PointNeighborsToThis();
        }

        protected internal override void Remove()
        {
            if (this == headers.firstNonPseudo)
            {
                headers.firstNonPseudo = headers.firstNonPseudo.After();
            }

            base.Remove();
        }
    }
}
