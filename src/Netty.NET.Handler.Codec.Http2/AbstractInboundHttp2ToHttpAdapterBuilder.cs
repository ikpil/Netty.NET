/*
 * Copyright 2015 The Netty Project
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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A skeletal builder implementation of {@link InboundHttp2ToHttpAdapter} and its subtypes.
 */
// CLR: the self-typed generic keeps its two type parameters with the same constraints, and self() casts through object
// (an unchecked cast in Java), as AbstractHttp2ConnectionHandlerBuilder does. The protected non-final Java methods are
// protected virtual; a Java subclass widens them to public with @Override, which a C# override cannot ("An override
// declaration can't change the accessibility of the virtual method",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), so InboundHttp2ToHttpAdapterBuilder
// exposes them with public members declared new that call base, as Http2ConnectionHandlerBuilder does. Throwable is
// Exception and IllegalStateException is InvalidOperationException (docs/codec-base-porting.md); the abstract build(...)
// "throws Exception" has no checked-exception counterpart.
public abstract class AbstractInboundHttp2ToHttpAdapterBuilder<T, B>
    where T : InboundHttp2ToHttpAdapter
    where B : AbstractInboundHttp2ToHttpAdapterBuilder<T, B>
{

    private readonly IHttp2Connection connection;
    private int maxContentLength;
    private bool validateHttpHeaders;
    private bool propagateSettings;

    /**
     * Creates a new {@link InboundHttp2ToHttpAdapter} builder for the specified {@link Http2Connection}.
     *
     * @param connection the object which will provide connection notification events
     *                   for the current connection
     */
    protected AbstractInboundHttp2ToHttpAdapterBuilder(IHttp2Connection connection)
    {
        this.connection = CheckNotNull(connection, "connection");
    }

    protected B Self()
    {
        return (B) (object) this;
    }

    /**
     * Returns the {@link Http2Connection}.
     */
    protected virtual IHttp2Connection Connection()
    {
        return connection;
    }

    /**
     * Returns the maximum length of the message content.
     */
    protected virtual int MaxContentLength()
    {
        return maxContentLength;
    }

    /**
     * Specifies the maximum length of the message content.
     *
     * @param maxContentLength the maximum length of the message content. If the length of the message content
     *        exceeds this value, a {@link TooLongFrameException} will be raised
     * @return {@link AbstractInboundHttp2ToHttpAdapterBuilder} the builder for the {@link InboundHttp2ToHttpAdapter}
     */
    protected virtual B MaxContentLength(int maxContentLength)
    {
        this.maxContentLength = maxContentLength;
        return Self();
    }

    /**
     * Return {@code true} if HTTP header validation should be performed.
     */
    protected virtual bool IsValidateHttpHeaders()
    {
        return validateHttpHeaders;
    }

    /**
     * Specifies whether validation of HTTP headers should be performed.
     *
     * @param validate
     * <ul>
     * <li>{@code true} to validate HTTP headers in the http-codec</li>
     * <li>{@code false} not to validate HTTP headers in the http-codec</li>
     * </ul>
     * @return {@link AbstractInboundHttp2ToHttpAdapterBuilder} the builder for the {@link InboundHttp2ToHttpAdapter}
     */
    protected virtual B ValidateHttpHeaders(bool validate)
    {
        validateHttpHeaders = validate;
        return Self();
    }

    /**
     * Returns {@code true} if a read settings frame should be propagated along the channel pipeline.
     */
    protected virtual bool IsPropagateSettings()
    {
        return propagateSettings;
    }

    /**
     * Specifies whether a read settings frame should be propagated along the channel pipeline.
     *
     * @param propagate if {@code true} read settings will be passed along the pipeline. This can be useful
     *                     to clients that need hold off sending data until they have received the settings.
     * @return {@link AbstractInboundHttp2ToHttpAdapterBuilder} the builder for the {@link InboundHttp2ToHttpAdapter}
     */
    protected virtual B PropagateSettings(bool propagate)
    {
        propagateSettings = propagate;
        return Self();
    }

    /**
     * Builds/creates a new {@link InboundHttp2ToHttpAdapter} instance using this builder's current settings.
     */
    protected virtual T Build()
    {
        T instance;
        try
        {
            instance = Build(Connection(), MaxContentLength(),
                                     IsValidateHttpHeaders(), IsPropagateSettings());
        }
        catch (Exception t)
        {
            throw new InvalidOperationException("failed to create a new InboundHttp2ToHttpAdapter", t);
        }
        connection.AddListener(instance);
        return instance;
    }

    /**
     * Creates a new {@link InboundHttp2ToHttpAdapter} with the specified properties.
     */
    protected abstract T Build(IHttp2Connection connection, int maxContentLength,
                               bool validateHttpHeaders, bool propagateSettings);
}
