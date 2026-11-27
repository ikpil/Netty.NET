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

namespace Netty.NET.Handler.Codec.Quic;

/**
 * The type of the
 * <a href="https://quicwg.org/base-drafts/rfc9000.html#name-packets-and-frames">QUIC packet</a>.
 */
// CLR: a Java enum with a method is a sealed class with static readonly instances, as codec-http2 Http2Error and
// codec-haproxy HAProxyTLV.Type ("You can't define a method inside the definition of an enumeration type",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). Name() and ToString() return
// the constant name as Enum.name()/toString() do; instances are compared by reference, as Java compares enum constants.
// The package-private of(byte) is internal; IllegalArgumentException is ArgumentException, and the byte prints signed
// as Java's byte does.
public sealed class QuicPacketType
{
    /**
     * Initial packet.
     */
    public static readonly QuicPacketType INITIAL = new QuicPacketType(nameof(INITIAL));

    /**
     * Retry packet.
     */
    public static readonly QuicPacketType RETRY = new QuicPacketType(nameof(RETRY));

    /**
     * Handshake packet.
     */
    public static readonly QuicPacketType HANDSHAKE = new QuicPacketType(nameof(HANDSHAKE));

    /**
     * 0-RTT packet.
     */
    public static readonly QuicPacketType ZERO_RTT = new QuicPacketType(nameof(ZERO_RTT));

    /**
     * 1-RTT short header packet.
     */
    public static readonly QuicPacketType SHORT = new QuicPacketType(nameof(SHORT));

    /**
     * Version negotiation packet.
     */
    public static readonly QuicPacketType VERSION_NEGOTIATION = new QuicPacketType(nameof(VERSION_NEGOTIATION));

    private readonly string name;

    private QuicPacketType(string name)
    {
        this.name = name;
    }

    public string Name()
    {
        return name;
    }

    public override string ToString()
    {
        return name;
    }

    /**
     * Return the {@link QuicPacketType} for the given byte.
     *
     * @param type  the byte that represent the type.
     * @return      the {@link QuicPacketType}.
     */
    internal static QuicPacketType Of(byte type)
    {
        switch (type)
        {
            case 1:
                return INITIAL;
            case 2:
                return RETRY;
            case 3:
                return HANDSHAKE;
            case 4:
                return ZERO_RTT;
            case 5:
                return SHORT;
            case 6:
                return VERSION_NEGOTIATION;
            default:
                throw new ArgumentException("Unknown QUIC packet type: " + ((sbyte) type).ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
