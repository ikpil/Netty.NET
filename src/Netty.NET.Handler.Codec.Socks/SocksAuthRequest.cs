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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * An socks auth request.
 *
 * @see SocksAuthResponse
 * @see SocksAuthRequestDecoder
 */
// CLR: IllegalArgumentException is ArgumentException.
public sealed class SocksAuthRequest : SocksRequest
{
    private static readonly SocksSubnegotiationVersion SUBNEGOTIATION_VERSION = SocksSubnegotiationVersion.AUTH_PASSWORD;
    private readonly string username;
    private readonly string password;

    public SocksAuthRequest(string username, string password) : base(SocksRequestType.AUTH)
    {
        ObjectUtil.CheckNotNull(username, "username");
        ObjectUtil.CheckNotNull(password, "password");
        // CLR: CharsetUtil.encoder(US_ASCII).canEncode(s) is true "if, and only if, this encoder can encode the given
        // character without throwing any exceptions and without performing any replacements"
        // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/nio/charset/CharsetEncoder.html), which
        // for US-ASCII means every char is below U+0080: Ascii.IsValid, "contains only ASCII chars"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.text.ascii.isvalid?view=net-10.0). No encoder object is
        // created.
        if (!Ascii.IsValid(username) || !Ascii.IsValid(password))
        {
            throw new ArgumentException(
                    "username: " + username + " or password: **** values should be in pure ascii");
        }
        if (username.Length > 255)
        {
            throw new ArgumentException("username: " + username + " exceeds 255 char limit");
        }
        if (password.Length > 255)
        {
            throw new ArgumentException("password: **** exceeds 255 char limit");
        }

        this.username = username;
        this.password = password;
    }

    /**
     * Returns username that needs to be authenticated
     *
     * @return username that needs to be authenticated
     */
    public string Username()
    {
        return username;
    }

    /**
     * Returns password that needs to be validated
     *
     * @return password that needs to be validated
     */
    public string Password()
    {
        return password;
    }

    // CLR: the override of the deprecated encodeAsByteBuf is not marked [Obsolete] (see UnknownSocksRequest).
#pragma warning disable CS0672
    public override void EncodeAsByteBuf(ByteBuf byteBuf)
#pragma warning restore CS0672
    {
        byteBuf.WriteByte(SUBNEGOTIATION_VERSION.ByteValue());
        byteBuf.WriteByte(username.Length);
        byteBuf.WriteCharSequence(username, Encoding.ASCII);
        byteBuf.WriteByte(password.Length);
        byteBuf.WriteCharSequence(password, Encoding.ASCII);
    }
}
