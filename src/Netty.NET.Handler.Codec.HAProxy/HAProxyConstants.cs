/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.Codec.HAProxy;

// CLR: All values are below 0x80, so the unsigned C# byte (owner decision) holds the same numbers.
internal sealed class HAProxyConstants
{

    /**
     * Command byte constants
     */
    internal const byte COMMAND_LOCAL_BYTE = 0x00;
    internal const byte COMMAND_PROXY_BYTE = 0x01;

    /**
     * Version byte constants
     */
    internal const byte VERSION_ONE_BYTE = 0x10;
    internal const byte VERSION_TWO_BYTE = 0x20;

    /**
     * Transport protocol byte constants
     */
    internal const byte TRANSPORT_UNSPEC_BYTE = 0x00;
    internal const byte TRANSPORT_STREAM_BYTE = 0x01;
    internal const byte TRANSPORT_DGRAM_BYTE = 0x02;

    /**
     * Address family byte constants
     */
    internal const byte AF_UNSPEC_BYTE = 0x00;
    internal const byte AF_IPV4_BYTE = 0x10;
    internal const byte AF_IPV6_BYTE = 0x20;
    internal const byte AF_UNIX_BYTE = 0x30;

    /**
     * Transport protocol and address family byte constants
     */
    internal const byte TPAF_UNKNOWN_BYTE = 0x00;
    internal const byte TPAF_TCP4_BYTE = 0x11;
    internal const byte TPAF_TCP6_BYTE = 0x21;
    internal const byte TPAF_UDP4_BYTE = 0x12;
    internal const byte TPAF_UDP6_BYTE = 0x22;
    internal const byte TPAF_UNIX_STREAM_BYTE = 0x31;
    internal const byte TPAF_UNIX_DGRAM_BYTE = 0x32;

    /**
     * V2 protocol binary header prefix
     */
    internal static readonly byte[] BINARY_PREFIX = {
            (byte) 0x0D,
            (byte) 0x0A,
            (byte) 0x0D,
            (byte) 0x0A,
            (byte) 0x00,
            (byte) 0x0D,
            (byte) 0x0A,
            (byte) 0x51,
            (byte) 0x55,
            (byte) 0x49,
            (byte) 0x54,
            (byte) 0x0A
    };

    internal static readonly byte[] TEXT_PREFIX = {
            (byte) 'P',
            (byte) 'R',
            (byte) 'O',
            (byte) 'X',
            (byte) 'Y',
    };

    private HAProxyConstants()
    {
    }
}
