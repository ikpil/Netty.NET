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

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Builds an {@link InboundHttp2ToHttpAdapter}.
 */
// CLR: the @Override methods widen protected members of AbstractInboundHttp2ToHttpAdapterBuilder to public; a C# override
// "can't change the accessibility of the virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override), so they are public members
// declared new that call base ("you explicitly hide a member that a base class provides",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier), as Http2ConnectionHandlerBuilder
// does. The abstract build(...) is implemented with protected override; "throws Exception" has no counterpart.
public sealed class InboundHttp2ToHttpAdapterBuilder
        : AbstractInboundHttp2ToHttpAdapterBuilder<InboundHttp2ToHttpAdapter, InboundHttp2ToHttpAdapterBuilder>
{

    /**
     * Creates a new {@link InboundHttp2ToHttpAdapter} builder for the specified {@link Http2Connection}.
     *
     * @param connection the object which will provide connection notification events
     *                   for the current connection
     */
    public InboundHttp2ToHttpAdapterBuilder(IHttp2Connection connection) : base(connection)
    {
    }

    public new InboundHttp2ToHttpAdapterBuilder MaxContentLength(int maxContentLength)
    {
        return base.MaxContentLength(maxContentLength);
    }

    public new InboundHttp2ToHttpAdapterBuilder ValidateHttpHeaders(bool validate)
    {
        return base.ValidateHttpHeaders(validate);
    }

    public new InboundHttp2ToHttpAdapterBuilder PropagateSettings(bool propagate)
    {
        return base.PropagateSettings(propagate);
    }

    public new InboundHttp2ToHttpAdapter Build()
    {
        return base.Build();
    }

    protected override InboundHttp2ToHttpAdapter Build(IHttp2Connection connection,
                                              int maxContentLength,
                                              bool validateHttpHeaders,
                                              bool propagateSettings)
    {

        return new InboundHttp2ToHttpAdapter(connection, maxContentLength,
                                             validateHttpHeaders, propagateSettings);
    }
}
