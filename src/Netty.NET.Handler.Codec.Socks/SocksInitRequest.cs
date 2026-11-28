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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * An socks init request.
 *
 * @see SocksInitResponse
 * @see SocksInitRequestDecoder
 */
// CLR: List<SocksAuthScheme> is IList<SocksAuthScheme> (as List<Object> out is IList<object> in codec-base), and
// Collections.unmodifiableList, "an unmodifiable view of the specified list"
// (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Collections.html), is a
// ReadOnlyCollection<T> wrapper, which "reflects" the changes of the underlying collection and prevents modification
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.readonlycollection-1?view=net-10.0).
public sealed class SocksInitRequest : SocksRequest
{
    private readonly IList<SocksAuthScheme> authSchemes;

    public SocksInitRequest(IList<SocksAuthScheme> authSchemes) : base(SocksRequestType.INIT)
    {
        this.authSchemes = ObjectUtil.CheckNotNull(authSchemes, "authSchemes");
    }

    /**
     * Returns the List<{@link SocksAuthScheme}> of this {@link SocksInitRequest}
     *
     * @return The List<{@link SocksAuthScheme}> of this {@link SocksInitRequest}
     */
    public IList<SocksAuthScheme> AuthSchemes()
    {
        return new ReadOnlyCollection<SocksAuthScheme>(authSchemes);
    }

    // CLR: the override of the deprecated encodeAsByteBuf is not marked [Obsolete] (see UnknownSocksRequest).
#pragma warning disable CS0672
    public override void EncodeAsByteBuf(ByteBuf byteBuf)
#pragma warning restore CS0672
    {
        byteBuf.WriteByte(ProtocolVersion().ByteValue());
        byteBuf.WriteByte(authSchemes.Count);

        foreach (SocksAuthScheme authScheme in authSchemes)
        {
            byteBuf.WriteByte(authScheme.ByteValue());
        }
    }
}
