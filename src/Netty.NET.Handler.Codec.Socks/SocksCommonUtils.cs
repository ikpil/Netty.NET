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

using System.Diagnostics;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socks;

internal sealed class SocksCommonUtils
{
    public static readonly SocksRequest UNKNOWN_SOCKS_REQUEST = new UnknownSocksRequest();
    public static readonly SocksResponse UNKNOWN_SOCKS_RESPONSE = new UnknownSocksResponse();

    /**
     * A constructor to stop this class being constructed.
     */
    private SocksCommonUtils()
    {
        // NOOP
    }

    private const char ipv6hextetSeparator = ':';

    /**
     * Converts numeric IPv6 to standard (non-compressed) format.
     */
    public static string Ipv6toStr(byte[] src)
    {
        Debug.Assert(src.Length == 16);
        StringBuilder sb = new StringBuilder(39);
        Ipv6toStr(sb, src, 0, 8);
        return sb.ToString();
    }

    private static void Ipv6toStr(StringBuilder sb, byte[] src, int fromHextet, int toHextet)
    {
        int i;
        toHextet --;
        for (i = fromHextet; i < toHextet; i++)
        {
            AppendHextet(sb, src, i);
            sb.Append(ipv6hextetSeparator);
        }

        AppendHextet(sb, src, i);
    }

    private static void AppendHextet(StringBuilder sb, byte[] src, int i)
    {
        StringUtil.ToHexString(sb, src, i << 1, 2);
    }

}
