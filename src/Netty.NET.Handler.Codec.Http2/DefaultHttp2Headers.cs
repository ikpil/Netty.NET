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
using System.Globalization;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.AsciiString;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: DefaultHeaders<CharSequence, CharSequence, Http2Headers> is the codec-base DefaultHeaders<ICharSequence,
// ICharSequence, IHttp2Headers> (docs/codec-base-porting.md, Headers); HashingStrategy is IEqualityComparer and the
// AsciiString CASE_SENSITIVE_HASHER/CASE_INSENSITIVE_HASHER are the Common comparers. NameValidator<CharSequence> and
// ValueValidator<CharSequence> are DefaultHeaders.INameValidator<ICharSequence>/IValueValidator<ICharSequence>, whose
// NOT_NULL/NO_VALIDATION constants need no unchecked cast; the anonymous validators are the private nested classes
// Http2NameValidator and Http2ValueValidator, and the anonymous ByteProcessor is a Func<byte, bool> (the Common
// ByteProcessor mapping). PlatformDependent.throwException(e) throws a checked exception without declaring it; C# has
// no checked exceptions, so it is a throw statement and the unreachable return after it is left out (as handler's
// SslHandler, docs/handler-porting.md). The inner class Http2HeaderEntry reads the enclosing instance's firstNonPseudo
// and head, so its constructor takes the DefaultHttp2Headers (a C# nested class has no enclosing instance), and the
// field initializer firstNonPseudo = head is an assignment in the constructor, because a C# field initializer cannot
// read an instance field (CS0236, https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236). The protected
// members of the base class are protected (they are protected internal in codec-base, another assembly), except
// HeaderEntry.Remove(): codec-base gives this assembly its internals (InternalsVisibleTo, for ExecutorPromiseCombiner),
// and an override of a protected internal member it can see keeps both modifiers (CS0507).
// IllegalArgumentException is ArgumentException; Integer.toHexString and the index use the invariant culture.
public class DefaultHttp2Headers
        : DefaultHeaders<ICharSequence, ICharSequence, IHttp2Headers>, IHttp2Headers
{
    private static readonly Func<byte, bool> HTTP2_NAME_VALIDATOR_PROCESSOR = static value => !IsUpperCase(value);
    private static readonly DefaultHeaders.INameValidator<ICharSequence> HTTP2_NAME_VALIDATOR = new Http2NameValidator();

    // CLR: the anonymous NameValidator of HTTP2_NAME_VALIDATOR.
    private sealed class Http2NameValidator : DefaultHeaders.INameValidator<ICharSequence>
    {
        public void ValidateName(ICharSequence name)
        {
            if (name == null || name.Length() == 0)
            {
                throw ConnectionError(PROTOCOL_ERROR,
                        "empty headers are not allowed [%s]", name);
            }

            if (IHttp2Headers.PseudoHeaderName.HasPseudoHeaderFormat(name))
            {
                if (!IHttp2Headers.PseudoHeaderName.IsPseudoHeader(name))
                {
                    throw ConnectionError(
                            PROTOCOL_ERROR, "Invalid HTTP/2 pseudo-header '%s' encountered.", name);
                }
                // no need for lower-case validation, we trust our own pseudo header constants
                return;
            }

            // RFC 9113 Section 8.2.1: HTTP/2 field names are valid HTTP/1.1 tokens (RFC 7230 Section 3.2.6)
            // with the additional constraint that they MUST be lowercase. Reject anything outside the token
            // grammar (non-ASCII, control characters, SP/HTAB, separators) before the lowercase check.
            int tokenIndex = HttpHeaderValidationUtil.ValidateToken(name);
            if (tokenIndex != -1)
            {
                throw ConnectionError(PROTOCOL_ERROR,
                        "invalid header name [%s]", name);
            }

            if (name is AsciiString)
            {
                int index;
                try
                {
                    index = ((AsciiString) name).ForEachByte(HTTP2_NAME_VALIDATOR_PROCESSOR);
                }
                catch (Http2Exception)
                {
                    throw;
                }
                catch (Exception t)
                {
                    throw ConnectionError(PROTOCOL_ERROR, t,
                            "unexpected error. invalid header name [%s]", name);
                }

                if (index != -1)
                {
                    throw ConnectionError(PROTOCOL_ERROR,
                            "invalid header name [%s]", name);
                }
            }
            else
            {
                for (int i = 0; i < name.Length(); ++i)
                {
                    if (IsUpperCase(name.CharAt(i)))
                    {
                        throw ConnectionError(PROTOCOL_ERROR,
                                "invalid header name [%s]", name);
                    }
                }
            }
        }
    }

    private static readonly DefaultHeaders.IValueValidator<ICharSequence> VALUE_VALIDATOR = new Http2ValueValidator();

    // CLR: the anonymous ValueValidator of VALUE_VALIDATOR.
    private sealed class Http2ValueValidator : DefaultHeaders.IValueValidator<ICharSequence>
    {
        public void Validate(ICharSequence value)
        {
            int index = HttpHeaderValidationUtil.ValidateValidHeaderValue(value);
            if (index != -1)
            {
                throw new ArgumentException("a header value contains prohibited character 0x" +
                        ((int) value.CharAt(index)).ToString("x", CultureInfo.InvariantCulture) + " at index " +
                        index.ToString(CultureInfo.InvariantCulture) + '.');
            }
        }
    }

    private HeaderEntry firstNonPseudo;

    /**
     * Create a new instance.
     * <p>
     * Header names and values will be validated according to
     * <a href="https://www.rfc-editor.org/rfc/rfc9113.html">RFC 9113</a>.
     */
    public DefaultHttp2Headers() : this(true, true, 16)
    {
    }

    /**
     * Create a new instance.
     * @param validate {@code true} to validate header names and values according to
     * <a href="https://www.rfc-editor.org/rfc/rfc9113.html">RFC 9113</a>.
     * {@code false} to not validate header names or values.
     */
    public DefaultHttp2Headers(bool validate) : this(validate, validate, 16)
    {
    }

    /**
     * Create a new instance.
     * @param validate {@code true} to validate header names and values according to
     * <a href="https://www.rfc-editor.org/rfc/rfc9113.html">RFC 9113</a>.
     * {@code false} to not validate header names or values.
     * @param arraySizeHint A hint as to how large the hash data structure should be.
     * The next positive power of two will be used. An upper bound may be enforced.
     * @see DefaultHttp2Headers#DefaultHttp2Headers(boolean, boolean, int)
     */
    public DefaultHttp2Headers(bool validate, int arraySizeHint) : this(validate, validate, arraySizeHint)
    {
    }

    /**
     * Create a new instance.
     * @param validateNames {@code true} to validate header names according to
     * <a href="https://www.rfc-editor.org/rfc/rfc9113.html">RFC 9113</a>. {@code false} to not validate header names.
     * @param validateValues {@code true} to validate header values according to
     * <a href="https://www.rfc-editor.org/rfc/rfc9113.html#name-http-fields">RFC 9113</a>. Otherwise, {@code false}
     * to not validate values.
     * @param arraySizeHint A hint as to how large the hash data structure should be.
     * The next positive power of two will be used. An upper bound may be enforced.
     */
    public DefaultHttp2Headers(bool validateNames, bool validateValues, int arraySizeHint)
        // Case sensitive compare is used because it is cheaper, and header validation can be used to catch invalid
        // headers.
        : base(CASE_SENSITIVE_HASHER,
                CharSequenceValueConverter.INSTANCE,
                validateNames ? HTTP2_NAME_VALIDATOR : DefaultHeaders.INameValidator<ICharSequence>.NOT_NULL,
                arraySizeHint,
                validateValues ? VALUE_VALIDATOR : DefaultHeaders.IValueValidator<ICharSequence>.NO_VALIDATION)
    {
        firstNonPseudo = head;
    }

    protected override void ValidateName(DefaultHeaders.INameValidator<ICharSequence> validator, bool forAdd,
                                         ICharSequence name)
    {
        base.ValidateName(validator, forAdd, name);
        if (NameValidator() == HTTP2_NAME_VALIDATOR && forAdd &&
            IHttp2Headers.PseudoHeaderName.HasPseudoHeaderFormat(name))
        {
            if (Contains(name))
            {
                throw ConnectionError(
                        PROTOCOL_ERROR, "Duplicate HTTP/2 pseudo-header '%s' encountered.", name);
            }
        }
    }

    protected override void ValidateValue(DefaultHeaders.IValueValidator<ICharSequence> validator, ICharSequence name,
                                          ICharSequence value)
    {
        // This method has a noop override for backward compatibility, see https://github.com/netty/netty/pull/12975
        base.ValidateValue(validator, name, value);
        // https://datatracker.ietf.org/doc/html/rfc9113#section-8.3.1
        // pseudo headers must not be empty
        if (NameValidator() == HTTP2_NAME_VALIDATOR && (value == null || value.Length() == 0) &&
                IHttp2Headers.PseudoHeaderName.HasPseudoHeaderFormat(name))
        {
            throw ConnectionError(
                    PROTOCOL_ERROR, "HTTP/2 pseudo-header '%s' must not be empty.", name);
        }
    }

    public override IHttp2Headers Clear()
    {
        firstNonPseudo = head;
        return base.Clear();
    }

    public override bool Equals(object o)
    {
        return o is IHttp2Headers && Equals((IHttp2Headers) o, CASE_SENSITIVE_HASHER);
    }

    public override int GetHashCode()
    {
        return HashCode(CASE_SENSITIVE_HASHER);
    }

    public virtual IHttp2Headers Method(ICharSequence value)
    {
        Set(IHttp2Headers.PseudoHeaderName.METHOD.Value(), value);
        return this;
    }

    public virtual IHttp2Headers Scheme(ICharSequence value)
    {
        Set(IHttp2Headers.PseudoHeaderName.SCHEME.Value(), value);
        return this;
    }

    public virtual IHttp2Headers Authority(ICharSequence value)
    {
        Set(IHttp2Headers.PseudoHeaderName.AUTHORITY.Value(), value);
        return this;
    }

    public virtual IHttp2Headers Path(ICharSequence value)
    {
        Set(IHttp2Headers.PseudoHeaderName.PATH.Value(), value);
        return this;
    }

    public virtual IHttp2Headers Status(ICharSequence value)
    {
        Set(IHttp2Headers.PseudoHeaderName.STATUS.Value(), value);
        return this;
    }

    public virtual ICharSequence Method()
    {
        return Get(IHttp2Headers.PseudoHeaderName.METHOD.Value());
    }

    public virtual ICharSequence Scheme()
    {
        return Get(IHttp2Headers.PseudoHeaderName.SCHEME.Value());
    }

    public virtual ICharSequence Authority()
    {
        return Get(IHttp2Headers.PseudoHeaderName.AUTHORITY.Value());
    }

    public virtual ICharSequence Path()
    {
        return Get(IHttp2Headers.PseudoHeaderName.PATH.Value());
    }

    public virtual ICharSequence Status()
    {
        return Get(IHttp2Headers.PseudoHeaderName.STATUS.Value());
    }

    public override bool Contains(ICharSequence name, ICharSequence value)
    {
        return Contains(name, value, false);
    }

    public virtual bool Contains(ICharSequence name, ICharSequence value, bool caseInsensitive)
    {
        return Contains(name, value, caseInsensitive ? CASE_INSENSITIVE_HASHER : CASE_SENSITIVE_HASHER);
    }

    protected sealed override HeaderEntry NewHeaderEntry(int h, ICharSequence name, ICharSequence value,
                                                           HeaderEntry next)
    {
        return new Http2HeaderEntry(this, h, name, value, next);
    }

    private sealed class Http2HeaderEntry : HeaderEntry
    {
        private readonly DefaultHttp2Headers headers;

        internal Http2HeaderEntry(DefaultHttp2Headers headers, int hash, ICharSequence key, ICharSequence value,
                HeaderEntry next) : base(hash, key)
        {
            this.headers = headers;
            this.value = value;
            this.next = next;

            // Make sure the pseudo headers fields are first in iteration order
            if (IHttp2Headers.PseudoHeaderName.HasPseudoHeaderFormat(key))
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

    /**
     * Default {@link io.netty.handler.codec.DefaultHeaders.NameValidator} used for HTTP/2.
     */
    public static DefaultHeaders.INameValidator<ICharSequence> DefaultHtt2NameValidator()
    {
        return HTTP2_NAME_VALIDATOR;
    }

    /**
     * Default {@link io.netty.handler.codec.DefaultHeaders.ValueValidator} used for HTTP/2.
     */
    public static DefaultHeaders.IValueValidator<ICharSequence> DefaultHttp2ValueValidator()
    {
        return VALUE_VALIDATOR;
    }
}
