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
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * An abstract class that defines a SocksMessage, providing common properties for
 * {@link SocksRequest} and {@link SocksResponse}.
 *
 * @see SocksRequest
 * @see SocksResponse
 */

public abstract class SocksMessage
{
    private readonly SocksMessageType type;
    private readonly SocksProtocolVersion protocolVersion = SocksProtocolVersion.SOCKS5;

    // CLR: SocksMessageType is a C# enum, "a value type"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum), so the argument cannot
    // be null and checkNotNull(type, "type") has no counterpart.
    protected SocksMessage(SocksMessageType type)
    {
        this.type = type;
    }

    /**
     * Returns the {@link SocksMessageType} of this {@link SocksMessage}
     *
     * @return The {@link SocksMessageType} of this {@link SocksMessage}
     */
    public SocksMessageType Type()
    {
        return type;
    }

    /**
     * Returns the {@link SocksProtocolVersion} of this {@link SocksMessage}
     *
     * @return The {@link SocksProtocolVersion} of this {@link SocksMessage}
     */
    public SocksProtocolVersion ProtocolVersion()
    {
        return protocolVersion;
    }

    /**
     * @deprecated Do not use; this method was intended for an internal use only.
     */
    // CLR: the @deprecated text names no replacement, so the member is ported with [Obsolete] and that text (owner
    // decision 2026-11-16; https://learn.microsoft.com/en-us/dotnet/api/system.obsoleteattribute?view=net-10.0 "Marks
    // program elements that are no longer in use"), as MessageAggregator.IsHandlingOversizedMessage in codec-base.
    [Obsolete("Do not use; this method was intended for an internal use only.")]
    public abstract void EncodeAsByteBuf(ByteBuf byteBuf);
}
