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
using System.Collections.Generic;
using System.Collections.Immutable;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http.Cors;

/**
 * Builder used to configure and build a {@link CorsConfig} instance.
 */
// CLR: java.util.concurrent.Callable<T> is Func<T>, the Common mapping of the executor submissions
// (src/Netty.NET.Common/Concurrent/EventExecutorExtensions.cs SubmitAsync(Func<T>)), and Map<CharSequence, Callable<?>>
// is Dictionary<ICharSequence, Func<object>>. Func<TResult> is covariant, but "variance applies only to reference types"
// (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance), so a Func<T> of a reference
// type is stored as it is and one of a value type (the Func<DateTimeOffset> of DateValueGenerator) is wrapped in a lambda
// that boxes its result. ConstantValueGenerator and DateValueGenerator keep their call() as Call(), and the map holds
// their method-group delegates. Iterable<T> is IEnumerable<object> (the core HttpHeaders mapping of Iterable<?>): a
// generic IEnumerable<T> parameter would also bind a string argument (an IEnumerable<char>), which Java's String is not.
// Arrays.asList(values), a fixed-size list backed by the array, is the object[] itself passed as IEnumerable<object>. The
// package-private fields and constructors are internal; the CharSequence... and String... overloads are params arrays
// (with no argument the C# call is ambiguous, where Java picks String...). Set/HashSet are ISet/HashSet, LinkedHashSet is
// the Common LinkedHashSet (insertion order, which origin() relies on), and Collections.emptySet() is
// ImmutableHashSet<string>.Empty. A HashMap accepts a null key, a Dictionary throws ArgumentNullException for one, so a
// null header name fails when it is configured instead of when the preflight headers are built. checkNotNullWithIAE is
// the Common CheckNotNullWithIAE (ArgumentException). The private constructor of the private ConstantValueGenerator,
// which the builder calls, is internal (a C# outer class cannot reach a nested class's private members). java.util.Date
// is DateTimeOffset (codec-base DateFormatter); new Date() is the current instant at millisecond precision.
public sealed class CorsConfigBuilder
{

    /**
     * Creates a Builder instance with it's origin set to '*'.
     *
     * @return Builder to support method chaining.
     */
    public static CorsConfigBuilder ForAnyOrigin()
    {
        return new CorsConfigBuilder();
    }

    /**
     * Creates a {@link CorsConfigBuilder} instance with the specified origin.
     *
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public static CorsConfigBuilder ForOrigin(string origin)
    {
        if ("*".Equals(origin))
        {
            return new CorsConfigBuilder();
        }

        return new CorsConfigBuilder(origin);
    }

    /**
     * Creates a {@link CorsConfigBuilder} instance with the specified origins.
     *
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public static CorsConfigBuilder ForOrigins(params string[] origins)
    {
        return new CorsConfigBuilder(origins);
    }

    internal readonly ISet<string> origins;
    internal readonly bool anyOrigin;
    internal bool allowNullOrigin;
    internal bool enabled = true;
    internal bool allowCredentials;
    internal readonly ISet<string> exposeHeaders = new HashSet<string>();
    internal long maxAge;
    internal readonly ISet<HttpMethod> requestMethods = new HashSet<HttpMethod>();
    internal readonly ISet<string> requestHeaders = new HashSet<string>();
    internal readonly Dictionary<ICharSequence, Func<object>> preflightHeaders = new Dictionary<ICharSequence, Func<object>>();
    private bool noPreflightHeaders;
    internal bool shortCircuit;
    internal bool allowPrivateNetwork;

    /**
     * Creates a new Builder instance with the origin passed in.
     *
     * @param origins the origin to be used for this builder.
     */
    internal CorsConfigBuilder(params string[] origins)
    {
        this.origins = new LinkedHashSet<string>(origins);
        anyOrigin = false;
    }

    /**
     * Creates a new Builder instance allowing any origin, "*" which is the
     * wildcard origin.
     *
     */
    internal CorsConfigBuilder()
    {
        anyOrigin = true;
        origins = ImmutableHashSet<string>.Empty;
    }

    /**
     * Web browsers may set the 'Origin' request header to 'null' if a resource is loaded
     * from the local file system. Calling this method will enable a successful CORS response
     * with a {@code "null"} value for the CORS response header 'Access-Control-Allow-Origin'.
     *
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder AllowNullOrigin()
    {
        allowNullOrigin = true;
        return this;
    }

    /**
     * Disables CORS support.
     *
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder Disable()
    {
        enabled = false;
        return this;
    }

    /**
     * Specifies the headers to be exposed to calling clients.
     *
     * During a simple CORS request, only certain response headers are made available by the
     * browser, for example using:
     * <pre>
     * xhr.getResponseHeader("Content-Type");
     * </pre>
     *
     * The headers that are available by default are:
     * <ul>
     * <li>Cache-Control</li>
     * <li>Content-Language</li>
     * <li>Content-Type</li>
     * <li>Expires</li>
     * <li>Last-Modified</li>
     * <li>Pragma</li>
     * </ul>
     *
     * To expose other headers they need to be specified which is what this method enables by
     * adding the headers to the CORS 'Access-Control-Expose-Headers' response header.
     *
     * @param headers the values to be added to the 'Access-Control-Expose-Headers' response header
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder ExposeHeaders(params string[] headers)
    {
        exposeHeaders.UnionWith(headers);
        return this;
    }

    /**
     * Specifies the headers to be exposed to calling clients.
     *
     * During a simple CORS request, only certain response headers are made available by the
     * browser, for example using:
     * <pre>
     * xhr.getResponseHeader(HttpHeaderNames.CONTENT_TYPE);
     * </pre>
     *
     * The headers that are available by default are:
     * <ul>
     * <li>Cache-Control</li>
     * <li>Content-Language</li>
     * <li>Content-Type</li>
     * <li>Expires</li>
     * <li>Last-Modified</li>
     * <li>Pragma</li>
     * </ul>
     *
     * To expose other headers they need to be specified which is what this method enables by
     * adding the headers to the CORS 'Access-Control-Expose-Headers' response header.
     *
     * @param headers the values to be added to the 'Access-Control-Expose-Headers' response header
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder ExposeHeaders(params ICharSequence[] headers)
    {
        foreach (ICharSequence header in headers)
        {
            exposeHeaders.Add(header.ToString());
        }

        return this;
    }

    /**
     * By default cookies are not included in CORS requests, but this method will enable cookies to
     * be added to CORS requests. Calling this method will set the CORS 'Access-Control-Allow-Credentials'
     * response header to true.
     *
     * Please note, that cookie support needs to be enabled on the client side as well.
     * The client needs to opt-in to send cookies by calling:
     * <pre>
     * xhr.withCredentials = true;
     * </pre>
     * The default value for 'withCredentials' is false in which case no cookies are sent.
     * Setting this to true will included cookies in cross origin requests.
     *
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder AllowCredentials()
    {
        allowCredentials = true;
        return this;
    }

    /**
     * When making a preflight request the client has to perform two request with can be inefficient.
     * This setting will set the CORS 'Access-Control-Max-Age' response header and enables the
     * caching of the preflight response for the specified time. During this time no preflight
     * request will be made.
     *
     * @param max the maximum time, in seconds, that the preflight response may be cached.
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder MaxAge(long max)
    {
        maxAge = max;
        return this;
    }

    /**
     * Specifies the allowed set of HTTP Request Methods that should be returned in the
     * CORS 'Access-Control-Request-Method' response header.
     *
     * @param methods the {@link HttpMethod}s that should be allowed.
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder AllowedRequestMethods(params HttpMethod[] methods)
    {
        requestMethods.UnionWith(methods);
        return this;
    }

    /**
     * Specifies the if headers that should be returned in the CORS 'Access-Control-Allow-Headers'
     * response header.
     *
     * If a client specifies headers on the request, for example by calling:
     * <pre>
     * xhr.setRequestHeader('My-Custom-Header', "SomeValue");
     * </pre>
     * the server will receive the above header name in the 'Access-Control-Request-Headers' of the
     * preflight request. The server will then decide if it allows this header to be sent for the
     * real request (remember that a preflight is not the real request but a request asking the server
     * if it allow a request).
     *
     * @param headers the headers to be added to the preflight 'Access-Control-Allow-Headers' response header.
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder AllowedRequestHeaders(params string[] headers)
    {
        requestHeaders.UnionWith(headers);
        return this;
    }

    /**
     * Specifies the if headers that should be returned in the CORS 'Access-Control-Allow-Headers'
     * response header.
     *
     * If a client specifies headers on the request, for example by calling:
     * <pre>
     * xhr.setRequestHeader('My-Custom-Header', "SomeValue");
     * </pre>
     * the server will receive the above header name in the 'Access-Control-Request-Headers' of the
     * preflight request. The server will then decide if it allows this header to be sent for the
     * real request (remember that a preflight is not the real request but a request asking the server
     * if it allow a request).
     *
     * @param headers the headers to be added to the preflight 'Access-Control-Allow-Headers' response header.
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder AllowedRequestHeaders(params ICharSequence[] headers)
    {
        foreach (ICharSequence header in headers)
        {
            requestHeaders.Add(header.ToString());
        }

        return this;
    }

    /**
     * Returns HTTP response headers that should be added to a CORS preflight response.
     *
     * An intermediary like a load balancer might require that a CORS preflight request
     * have certain headers set. This enables such headers to be added.
     *
     * @param name the name of the HTTP header.
     * @param values the values for the HTTP header.
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder PreflightResponseHeader(ICharSequence name, params object[] values)
    {
        if (values.Length == 1)
        {
            preflightHeaders[name] = new ConstantValueGenerator(values[0]).Call;
        }
        else
        {
            PreflightResponseHeader(name, (IEnumerable<object>) values);
        }

        return this;
    }

    /**
     * Returns HTTP response headers that should be added to a CORS preflight response.
     *
     * An intermediary like a load balancer might require that a CORS preflight request
     * have certain headers set. This enables such headers to be added.
     *
     * @param name the name of the HTTP header.
     * @param value the values for the HTTP header.
     * @param <T> the type of values that the Iterable contains.
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder PreflightResponseHeader(ICharSequence name, IEnumerable<object> value)
    {
        preflightHeaders[name] = new ConstantValueGenerator(value).Call;
        return this;
    }

    /**
     * Returns HTTP response headers that should be added to a CORS preflight response.
     *
     * An intermediary like a load balancer might require that a CORS preflight request
     * have certain headers set. This enables such headers to be added.
     *
     * Some values must be dynamically created when the HTTP response is created, for
     * example the 'Date' response header. This can be accomplished by using a Callable
     * which will have its 'call' method invoked when the HTTP response is created.
     *
     * @param name the name of the HTTP header.
     * @param valueGenerator a Callable which will be invoked at HTTP response creation.
     * @param <T> the type of the value that the Callable can return.
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder PreflightResponseHeader<T>(ICharSequence name, Func<T> valueGenerator)
    {
        preflightHeaders[name] = valueGenerator as Func<object> ?? (() => valueGenerator());
        return this;
    }

    /**
     * Specifies that no preflight response headers should be added to a preflight response.
     *
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder NoPreflightResponseHeaders()
    {
        noPreflightHeaders = true;
        return this;
    }

    /**
     * Specifies that a CORS request should be rejected if it's invalid before being
     * further processing.
     *
     * CORS headers are set after a request is processed. This may not always be desired
     * and this setting will check that the Origin is valid and if it is not valid no
     * further processing will take place, and an error will be returned to the calling client.
     *
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder ShortCircuit()
    {
        shortCircuit = true;
        return this;
    }

    /**
     * Web browsers may set the 'Access-Control-Request-Private-Network' request header if a resource is loaded
     * from a local network.
     * By default direct access to private network endpoints from public websites is not allowed.
     * Calling this method will set the CORS 'Access-Control-Request-Private-Network' response header to true.
     *
     * @return {@link CorsConfigBuilder} to support method chaining.
     */
    public CorsConfigBuilder AllowPrivateNetwork()
    {
        allowPrivateNetwork = true;
        return this;
    }

    /**
     * Builds a {@link CorsConfig} with settings specified by previous method calls.
     *
     * @return {@link CorsConfig} the configured CorsConfig instance.
     */
    public CorsConfig Build()
    {
        if (preflightHeaders.Count == 0 && !noPreflightHeaders)
        {
            preflightHeaders[HttpHeaderNames.DATE] = () => DateValueGenerator.INSTANCE.Call();
            preflightHeaders[HttpHeaderNames.CONTENT_LENGTH] = new ConstantValueGenerator("0").Call;
        }

        return new CorsConfig(this);
    }

    /**
     * This class is used for preflight HTTP response values that do not need to be
     * generated, but instead the value is "static" in that the same value will be returned
     * for each call.
     */
    private sealed class ConstantValueGenerator
    {

        private readonly object value;

        /**
         * Sole constructor.
         *
         * @param value the value that will be returned when the call method is invoked.
         */
        internal ConstantValueGenerator(object value)
        {
            this.value = CheckNotNullWithIAE(value, "value");
        }

        public object Call()
        {
            return value;
        }
    }

    /**
     * This callable is used for the DATE preflight HTTP response HTTP header.
     * It's value must be generated when the response is generated, hence will be
     * different for every call.
     */
    private sealed class DateValueGenerator
    {

        internal static readonly DateValueGenerator INSTANCE = new DateValueGenerator();

        public DateTimeOffset Call()
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }
}
