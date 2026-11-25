/*
 * Copyright 2013 The Netty Project
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
using System.Collections.ObjectModel;
using System.Linq;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.Cors;

/**
 * Configuration for Cross-Origin Resource Sharing (CORS).
 */
// CLR: Set<T> is ISet<T>; LinkedHashSet is the Common LinkedHashSet (insertion order: origin() returns the first
// configured origin). Collections.unmodifiableSet is a ReadOnlySet<T> view, "a wrapper around the specified set"
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.readonlyset-1?view=net-10.0, as handler's
// SslUtils). Callable<?> is Func<object> and Map<CharSequence, Callable<?>> an IDictionary<ICharSequence, Func<object>>
// (see CorsConfigBuilder); "value instanceof Iterable" is "value is IEnumerable<object>", the core mapping of Iterable<?>
// (a string or an ICharSequence is not one, as a Java String is not Iterable; a sequence of a value type is added as one
// object). IllegalStateException is InvalidOperationException. toString() keeps the Java text: booleans print as
// true/false, the sets as AbstractCollection.toString ("[a, b]",
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/AbstractCollection.html) and the map as
// AbstractMap.toString ("{k=v}", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/AbstractMap.html),
// through the private CLR helpers JavaString/CollectionToString/MapToString; a Func value prints its delegate type name
// where Java prints the Callable's class name and identity hash. The package-private constructor is internal.
// The @Deprecated members whose @deprecated text names a replacement (isShortCurcuit(), withAnyOrigin(), withOrigin(String),
// withOrigins(String...) and the nested Builder, replaced by isShortCircuit() and CorsConfigBuilder) are not ported (owner
// decision 2026-11-16); DateValueGenerator ("Removed without alternatives.") is ported with [Obsolete].
public sealed class CorsConfig
{

    private readonly ISet<string> origins;
    private readonly bool anyOrigin;
    private readonly bool enabled;
    private readonly ISet<string> exposeHeaders;
    private readonly bool allowCredentials;
    private readonly long maxAge;
    private readonly ISet<HttpMethod> allowedRequestMethods;
    private readonly ISet<string> allowedRequestHeaders;
    private readonly bool allowNullOrigin;
    private readonly IDictionary<ICharSequence, Func<object>> preflightHeaders;
    private readonly bool shortCircuit;
    private readonly bool allowPrivateNetwork;

    internal CorsConfig(CorsConfigBuilder builder)
    {
        origins = new LinkedHashSet<string>(builder.origins);
        anyOrigin = builder.anyOrigin;
        enabled = builder.enabled;
        exposeHeaders = builder.exposeHeaders;
        allowCredentials = builder.allowCredentials;
        maxAge = builder.maxAge;
        allowedRequestMethods = builder.requestMethods;
        allowedRequestHeaders = builder.requestHeaders;
        allowNullOrigin = builder.allowNullOrigin;
        preflightHeaders = builder.preflightHeaders;
        shortCircuit = builder.shortCircuit;
        allowPrivateNetwork = builder.allowPrivateNetwork;
    }

    /**
     * Determines if support for CORS is enabled.
     *
     * @return {@code true} if support for CORS is enabled, false otherwise.
     */
    public bool IsCorsSupportEnabled()
    {
        return enabled;
    }

    /**
     * Determines whether a wildcard origin, '*', is supported.
     *
     * @return {@code boolean} true if any origin is allowed.
     */
    public bool IsAnyOriginSupported()
    {
        return anyOrigin;
    }

    /**
     * Returns the allowed origin. This can either be a wildcard or an origin value.
     *
     * @return the value that will be used for the CORS response header 'Access-Control-Allow-Origin'
     */
    public string Origin()
    {
        return origins.Count == 0 ? "*" : origins.First();
    }

    /**
     * Returns the set of allowed origins.
     *
     * @return {@code Set} the allowed origins.
     */
    public ISet<string> Origins()
    {
        return origins;
    }

    /**
     * Web browsers may set the 'Origin' request header to 'null' if a resource is loaded
     * from the local file system.
     *
     * If isNullOriginAllowed is true then the server will response with the wildcard for
     * the CORS response header 'Access-Control-Allow-Origin'.
     *
     * @return {@code true} if a 'null' origin should be supported.
     */
    public bool IsNullOriginAllowed()
    {
        return allowNullOrigin;
    }

    /**
     * Web browsers may set the 'Access-Control-Request-Private-Network' request header if a resource is loaded
     * from a local network.
     * By default direct access to private network endpoints from public websites is not allowed.
     *
     * If isPrivateNetworkAllowed is true the server will response with the CORS response header
     * 'Access-Control-Request-Private-Network'.
     *
     * @return {@code true} if private network access should be allowed.
     */
    public bool IsPrivateNetworkAllowed()
    {
        return allowPrivateNetwork;
    }

    /**
     * Returns a set of headers to be exposed to calling clients.
     *
     * During a simple CORS request only certain response headers are made available by the
     * browser, for example using:
     * <pre>
     * xhr.getResponseHeader("Content-Type");
     * </pre>
     * The headers that are available by default are:
     * <ul>
     * <li>Cache-Control</li>
     * <li>Content-Language</li>
     * <li>Content-Type</li>
     * <li>Expires</li>
     * <li>Last-Modified</li>
     * <li>Pragma</li>
     * </ul>
     * To expose other headers they need to be specified, which is what this method enables by
     * adding the headers names to the CORS 'Access-Control-Expose-Headers' response header.
     *
     * @return {@code List<String>} a list of the headers to expose.
     */
    public ISet<string> ExposedHeaders()
    {
        return new ReadOnlySet<string>(exposeHeaders);
    }

    /**
     * Determines if cookies are supported for CORS requests.
     *
     * By default cookies are not included in CORS requests but if isCredentialsAllowed returns
     * true cookies will be added to CORS requests. Setting this value to true will set the
     * CORS 'Access-Control-Allow-Credentials' response header to true.
     *
     * Please note that cookie support needs to be enabled on the client side as well.
     * The client needs to opt-in to send cookies by calling:
     * <pre>
     * xhr.withCredentials = true;
     * </pre>
     * The default value for 'withCredentials' is false in which case no cookies are sent.
     * Setting this to true will included cookies in cross origin requests.
     *
     * @return {@code true} if cookies are supported.
     */
    public bool IsCredentialsAllowed()
    {
        return allowCredentials;
    }

    /**
     * Gets the maxAge setting.
     *
     * When making a preflight request the client has to perform two request with can be inefficient.
     * This setting will set the CORS 'Access-Control-Max-Age' response header and enables the
     * caching of the preflight response for the specified time. During this time no preflight
     * request will be made.
     *
     * @return {@code long} the time in seconds that a preflight request may be cached.
     */
    public long MaxAge()
    {
        return maxAge;
    }

    /**
     * Returns the allowed set of Request Methods. The Http methods that should be returned in the
     * CORS 'Access-Control-Request-Method' response header.
     *
     * @return {@code Set} of {@link HttpMethod}s that represent the allowed Request Methods.
     */
    public ISet<HttpMethod> AllowedRequestMethods()
    {
        return new ReadOnlySet<HttpMethod>(allowedRequestMethods);
    }

    /**
     * Returns the allowed set of Request Headers.
     *
     * The header names returned from this method will be used to set the CORS
     * 'Access-Control-Allow-Headers' response header.
     *
     * @return {@code Set<String>} of strings that represent the allowed Request Headers.
     */
    public ISet<string> AllowedRequestHeaders()
    {
        return new ReadOnlySet<string>(allowedRequestHeaders);
    }

    /**
     * Returns HTTP response headers that should be added to a CORS preflight response.
     *
     * @return {@link HttpHeaders} the HTTP response headers to be added.
     */
    public HttpHeaders PreflightResponseHeaders()
    {
        if (this.preflightHeaders.Count == 0)
        {
            return EmptyHttpHeaders.INSTANCE;
        }
        HttpHeaders preflightHeaders = new DefaultHttpHeaders();
        foreach (KeyValuePair<ICharSequence, Func<object>> entry in this.preflightHeaders)
        {
            object value = GetValue(entry.Value);
            if (value is IEnumerable<object>)
            {
                preflightHeaders.Add(entry.Key, (IEnumerable<object>) value);
            }
            else
            {
                preflightHeaders.Add(entry.Key, value);
            }
        }
        return preflightHeaders;
    }

    /**
     * Determines whether a CORS request should be rejected if it's invalid before being
     * further processing.
     *
     * CORS headers are set after a request is processed. This may not always be desired
     * and this setting will check that the Origin is valid and if it is not valid no
     * further processing will take place, and an error will be returned to the calling client.
     *
     * @return {@code true} if a CORS request should short-circuit upon receiving an invalid Origin header.
     */
    public bool IsShortCircuit()
    {
        return shortCircuit;
    }

    private static T GetValue<T>(Func<T> callable)
    {
        try
        {
            return callable();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Could not generate value for callable [" + callable + ']', e);
        }
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + "[enabled=" + JavaString(enabled) +
                ", origins=" + CollectionToString(origins) +
                ", anyOrigin=" + JavaString(anyOrigin) +
                ", exposedHeaders=" + CollectionToString(exposeHeaders) +
                ", isCredentialsAllowed=" + JavaString(allowCredentials) +
                ", maxAge=" + maxAge.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ", allowedRequestMethods=" + CollectionToString(allowedRequestMethods) +
                ", allowedRequestHeaders=" + CollectionToString(allowedRequestHeaders) +
                ", preflightHeaders=" + MapToString(preflightHeaders) +
                ", isPrivateNetworkAllowed=" + JavaString(allowPrivateNetwork) + ']';
    }

    // CLR: String.valueOf(boolean).
    private static string JavaString(bool value)
    {
        return value ? "true" : "false";
    }

    // CLR: AbstractCollection.toString() (String.valueOf of each element: null prints as "null").
    private static string CollectionToString<T>(IEnumerable<T> c)
    {
        return "[" + string.Join(", ", c.Select(e => e == null ? "null" : e.ToString())) + "]";
    }

    // CLR: AbstractMap.toString().
    private static string MapToString<K, V>(IDictionary<K, V> m)
    {
        return "{" + string.Join(", ", m.Select(e => (e.Key == null ? "null" : e.Key.ToString()) + "=" +
                (e.Value == null ? "null" : e.Value.ToString()))) + "}";
    }

    /**
     * @deprecated Removed without alternatives.
     */
    // CLR: @deprecated names no replacement, so it is ported with [Obsolete] (owner decision 2026-11-16).
    // Callable<Date> is a class with Call() returning DateTimeOffset (see CorsConfigBuilder); new Date() is the current
    // instant at millisecond precision. The "throws Exception" clause has no C# counterpart.
    [Obsolete("Removed without alternatives.")]
    public sealed class DateValueGenerator
    {

        public DateTimeOffset Call()
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }
}
