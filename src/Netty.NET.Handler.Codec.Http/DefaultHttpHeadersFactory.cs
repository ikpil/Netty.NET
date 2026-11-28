/*
 * Copyright 2023 The Netty Project
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
using System.Globalization;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * A builder of {@link HttpHeadersFactory} instances, that itself implements {@link HttpHeadersFactory}.
 * The builder is immutable, and every {@code with-} method produce a new, modified instance.
 * <p>
 * The default builder you most likely want to start with is {@link DefaultHttpHeadersFactory#headersFactory()}.
 */
// CLR: The anonymous validators are the private nested classes NameValidator_, ValueValidator_ and
// TrailerNameValidator. IllegalArgumentException is ArgumentException; a null name prints as "null" and
// Integer.toHexString is the invariant lower-case "x" format.
public sealed class DefaultHttpHeadersFactory : IHttpHeadersFactory
{
    private static readonly DefaultHeaders.INameValidator<ICharSequence> DEFAULT_NAME_VALIDATOR = new NameValidator_();
    private static readonly DefaultHeaders.IValueValidator<ICharSequence> DEFAULT_VALUE_VALIDATOR = new ValueValidator_();
    private static readonly DefaultHeaders.INameValidator<ICharSequence> DEFAULT_TRAILER_NAME_VALIDATOR =
            new TrailerNameValidator();

    private sealed class NameValidator_ : DefaultHeaders.INameValidator<ICharSequence>
    {
        public void ValidateName(ICharSequence name)
        {
            if (name == null || name.Length() == 0)
            {
                throw new ArgumentException("empty headers are not allowed [" + (name?.ToString() ?? "null") + ']');
            }

            int index = HttpHeaderValidationUtil.ValidateToken(name);
            if (index != -1)
            {
                throw new ArgumentException("a header name can only contain \"token\" characters, " +
                        "but found invalid character 0x" + ((int) name.CharAt(index)).ToString("x", CultureInfo.InvariantCulture) +
                        " at index " + index.ToString(CultureInfo.InvariantCulture) + " of header '" + name + "'.");
            }
        }
    }

    private sealed class ValueValidator_ : DefaultHeaders.IValueValidator<ICharSequence>
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

    private sealed class TrailerNameValidator : DefaultHeaders.INameValidator<ICharSequence>
    {
        public void ValidateName(ICharSequence name)
        {
            DEFAULT_NAME_VALIDATOR.ValidateName(name);
            if (HttpHeaderNames.CONTENT_LENGTH.ContentEqualsIgnoreCase(name)
                    || HttpHeaderNames.TRANSFER_ENCODING.ContentEqualsIgnoreCase(name)
                    || HttpHeaderNames.TRAILER.ContentEqualsIgnoreCase(name))
            {
                throw new ArgumentException("prohibited trailing header: " + name);
            }
        }
    }

    private static readonly DefaultHeaders.INameValidator<ICharSequence> NO_NAME_VALIDATOR =
            DefaultHeaders.INameValidator<ICharSequence>.NOT_NULL;
    private static readonly DefaultHeaders.IValueValidator<ICharSequence> NO_VALUE_VALIDATOR =
            DefaultHeaders.IValueValidator<ICharSequence>.NO_VALIDATION;

    private static readonly DefaultHttpHeadersFactory DEFAULT =
            new DefaultHttpHeadersFactory(DEFAULT_NAME_VALIDATOR, DEFAULT_VALUE_VALIDATOR, false);
    private static readonly DefaultHttpHeadersFactory DEFAULT_TRAILER =
            new DefaultHttpHeadersFactory(DEFAULT_TRAILER_NAME_VALIDATOR, DEFAULT_VALUE_VALIDATOR, false);
    private static readonly DefaultHttpHeadersFactory DEFAULT_COMBINING =
            new DefaultHttpHeadersFactory(DEFAULT.nameValidator, DEFAULT.valueValidator, true);
    private static readonly DefaultHttpHeadersFactory DEFAULT_NO_VALIDATION =
            new DefaultHttpHeadersFactory(NO_NAME_VALIDATOR, NO_VALUE_VALIDATOR, false);

    private readonly DefaultHeaders.INameValidator<ICharSequence> nameValidator;
    private readonly DefaultHeaders.IValueValidator<ICharSequence> valueValidator;
    private readonly bool combiningHeaders;

    /**
     * Create a header builder with the given settings.
     *
     * @param nameValidator The name validator to use, not null.
     * @param valueValidator The value validator to use, not null.
     * @param combiningHeaders {@code true} if multi-valued headers should be combined into single lines.
     */
    private DefaultHttpHeadersFactory(
            DefaultHeaders.INameValidator<ICharSequence> nameValidator,
            DefaultHeaders.IValueValidator<ICharSequence> valueValidator,
            bool combiningHeaders)
    {
        this.nameValidator = CheckNotNull(nameValidator, "nameValidator");
        this.valueValidator = CheckNotNull(valueValidator, "valueValidator");
        this.combiningHeaders = combiningHeaders;
    }

    /**
     * Get the default implementation of {@link HttpHeadersFactory} for creating headers.
     * <p>
     * This {@link DefaultHttpHeadersFactory} creates {@link HttpHeaders} instances that has the
     * recommended header validation enabled.
     */
    public static DefaultHttpHeadersFactory HeadersFactory()
    {
        return DEFAULT;
    }

    /**
     * Get the default implementation of {@link HttpHeadersFactory} for creating trailers.
     * <p>
     * This {@link DefaultHttpHeadersFactory} creates {@link HttpHeaders} instances that has the
     * validation enabled that is recommended for trailers.
     */
    public static DefaultHttpHeadersFactory TrailersFactory()
    {
        return DEFAULT_TRAILER;
    }

    public HttpHeaders NewHeaders()
    {
        if (IsCombiningHeaders())
        {
            return new CombinedHttpHeaders(GetNameValidator(), GetValueValidator());
        }

        return new DefaultHttpHeaders(GetNameValidator(), GetValueValidator());
    }

    public HttpHeaders NewEmptyHeaders()
    {
        if (IsCombiningHeaders())
        {
            return new CombinedHttpHeaders(GetNameValidator(), GetValueValidator(), 2);
        }

        return new DefaultHttpHeaders(GetNameValidator(), GetValueValidator(), 2);
    }

    /**
     * Create a new builder that has HTTP header name validation enabled or disabled.
     * <p>
     * <b>Warning!</b> Setting {@code validation} to {@code false} will mean that Netty won't
     * validate & protect against user-supplied headers that are malicious.
     * This can leave your server implementation vulnerable to
     * <a href="https://cwe.mitre.org/data/definitions/113.html">
     *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
     * </a>.
     * When disabling this validation, it is the responsibility of the caller to ensure that the values supplied
     * do not contain a non-url-escaped carriage return (CR) and/or line feed (LF) characters.
     *
     * @param validation If validation should be enabled or disabled.
     * @return The new builder.
     */
    public DefaultHttpHeadersFactory WithNameValidation(bool validation)
    {
        return WithNameValidator(validation ? DEFAULT_NAME_VALIDATOR : NO_NAME_VALIDATOR);
    }

    /**
     * Create a new builder that with the given {@link NameValidator}.
     * <p>
     * <b>Warning!</b> If the given validator does not check that the header names are standards compliant, Netty won't
     * validate & protect against user-supplied headers that are malicious.
     * This can leave your server implementation vulnerable to
     * <a href="https://cwe.mitre.org/data/definitions/113.html">
     *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
     * </a>.
     * When disabling this validation, it is the responsibility of the caller to ensure that the values supplied
     * do not contain a non-url-escaped carriage return (CR) and/or line feed (LF) characters.
     *
     * @param validator The HTTP header name validator to use.
     * @return The new builder.
     */
    public DefaultHttpHeadersFactory WithNameValidator(DefaultHeaders.INameValidator<ICharSequence> validator)
    {
        if (nameValidator == CheckNotNull(validator, "validator"))
        {
            return this;
        }
        if (validator == DEFAULT_NAME_VALIDATOR && valueValidator == DEFAULT_VALUE_VALIDATOR)
        {
            return combiningHeaders ? DEFAULT_COMBINING : DEFAULT;
        }

        return new DefaultHttpHeadersFactory(validator, valueValidator, combiningHeaders);
    }

    /**
     * Create a new builder that has HTTP header value validation enabled or disabled.
     * <p>
     * <b>Warning!</b> Setting {@code validation} to {@code false} will mean that Netty won't
     * validate & protect against user-supplied headers that are malicious.
     * This can leave your server implementation vulnerable to
     * <a href="https://cwe.mitre.org/data/definitions/113.html">
     *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
     * </a>.
     * When disabling this validation, it is the responsibility of the caller to ensure that the values supplied
     * do not contain a non-url-escaped carriage return (CR) and/or line feed (LF) characters.
     *
     * @param validation If validation should be enabled or disabled.
     * @return The new builder.
     */
    public DefaultHttpHeadersFactory WithValueValidation(bool validation)
    {
        return WithValueValidator(validation ? DEFAULT_VALUE_VALIDATOR : NO_VALUE_VALIDATOR);
    }

    /**
     * Create a new builder that with the given {@link ValueValidator}.
     * <p>
     * <b>Warning!</b> If the given validator does not check that the header values are standards compliant, Netty won't
     * validate & protect against user-supplied headers that are malicious.
     * This can leave your server implementation vulnerable to
     * <a href="https://cwe.mitre.org/data/definitions/113.html">
     *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
     * </a>.
     * When disabling this validation, it is the responsibility of the caller to ensure that the values supplied
     * do not contain a non-url-escaped carriage return (CR) and/or line feed (LF) characters.
     *
     * @param validator The HTTP header name validator to use.
     * @return The new builder.
     */
    public DefaultHttpHeadersFactory WithValueValidator(DefaultHeaders.IValueValidator<ICharSequence> validator)
    {
        if (valueValidator == CheckNotNull(validator, "validator"))
        {
            return this;
        }
        if (nameValidator == DEFAULT_NAME_VALIDATOR && validator == DEFAULT_VALUE_VALIDATOR)
        {
            return combiningHeaders ? DEFAULT_COMBINING : DEFAULT;
        }

        return new DefaultHttpHeadersFactory(nameValidator, validator, combiningHeaders);
    }

    /**
     * Create a new builder that has HTTP header validation enabled or disabled.
     * <p>
     * <b>Warning!</b> Setting {@code validation} to {@code false} will mean that Netty won't
     * validate & protect against user-supplied headers that are malicious.
     * This can leave your server implementation vulnerable to
     * <a href="https://cwe.mitre.org/data/definitions/113.html">
     *     CWE-113: Improper Neutralization of CRLF Sequences in HTTP Headers ('HTTP Response Splitting')
     * </a>.
     * When disabling this validation, it is the responsibility of the caller to ensure that the values supplied
     * do not contain a non-url-escaped carriage return (CR) and/or line feed (LF) characters.
     *
     * @param validation If validation should be enabled or disabled.
     * @return The new builder.
     */
    public DefaultHttpHeadersFactory WithValidation(bool validation)
    {
        if (this == DEFAULT && !validation)
        {
            return DEFAULT_NO_VALIDATION;
        }
        if (this == DEFAULT_NO_VALIDATION && validation)
        {
            return DEFAULT;
        }

        return WithNameValidation(validation).WithValueValidation(validation);
    }

    /**
     * Create a new builder that will build {@link HttpHeaders} objects that either combine
     * multi-valued headers, or not.
     *
     * @param combiningHeaders {@code true} if multi-valued headers should be combined, otherwise {@code false}.
     * @return The new builder.
     */
    public DefaultHttpHeadersFactory WithCombiningHeaders(bool combiningHeaders)
    {
        if (this.combiningHeaders == combiningHeaders)
        {
            return this;
        }

        return new DefaultHttpHeadersFactory(nameValidator, valueValidator, combiningHeaders);
    }

    /**
     * Get the currently configured {@link NameValidator}.
     * <p>
     * This method will be used by the {@link #newHeaders()} method.
     *
     * @return The configured name validator.
     */
    public DefaultHeaders.INameValidator<ICharSequence> GetNameValidator()
    {
        return nameValidator;
    }

    /**
     * Get the currently configured {@link ValueValidator}.
     * <p>
     * This method will be used by the {@link #newHeaders()} method.
     *
     * @return The configured value validator.
     */
    public DefaultHeaders.IValueValidator<ICharSequence> GetValueValidator()
    {
        return valueValidator;
    }

    /**
     * Check whether header combining is enabled or not.
     *
     * @return {@code true} if header value combining is enabled, otherwise {@code false}.
     */
    public bool IsCombiningHeaders()
    {
        return combiningHeaders;
    }

    /**
     * Check whether header name validation is enabled.
     *
     * @return {@code true} if header name validation is enabled, otherwise {@code false}.
     */
    public bool IsValidatingHeaderNames()
    {
        return nameValidator != NO_NAME_VALIDATOR;
    }

    /**
     * Check whether header value validation is enabled.
     *
     * @return {@code true} if header value validation is enabled, otherwise {@code false}.
     */
    public bool IsValidatingHeaderValues()
    {
        return valueValidator != NO_VALUE_VALIDATOR;
    }
}
