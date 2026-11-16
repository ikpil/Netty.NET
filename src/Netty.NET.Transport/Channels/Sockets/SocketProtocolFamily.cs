/*
 * Copyright 2022 The Netty Project
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
using System.Net.Sockets;

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * {@link ProtocolFamily} implementation that is used by the different transport implementations.
 */
// CLR: java.net.ProtocolFamily has no CLR interface; System.Net.Sockets.AddressFamily is the platform family.
public enum SocketProtocolFamily
{
    /**
     * IPv4
     */
    Inet,
    /**
     * IPv6
     */
    Inet6,
    /**
     * Unix Domain Socket
     */
    Unix,
}

// CLR: a C# enum cannot declare methods. C# 14 extension members keep the original call shapes:
// family.ToJdkFamily() and SocketProtocolFamily.Of(family).
public static class SocketProtocolFamilyExtensions
{
    extension(SocketProtocolFamily @this)
    {
        /**
         * Return a {@link ProtocolFamily} that is "known" by the JDK itself and represent the same as
         * {@link SocketProtocolFamily}.
         *
         * @return the JDK {@link ProtocolFamily}.
         * @throws UnsupportedOperationException if it can't be converted.
         */
        // CLR: the runtime-known family is AddressFamily; UnsupportedOperationException maps to NotSupportedException.
        public AddressFamily ToJdkFamily()
        {
            switch (@this)
            {
                case SocketProtocolFamily.Inet:
                    return AddressFamily.InterNetwork;
                case SocketProtocolFamily.Inet6:
                    return AddressFamily.InterNetworkV6;
                case SocketProtocolFamily.Unix:
                    // Just use valueOf as we compile with Java11. If the JDK does not support unix domain sockets, this
                    // will throw.
                    // CLR: AddressFamily.Unix is always defined; OS support is decided when a socket is created.
                    return AddressFamily.Unix;
                default:
                    throw new NotSupportedException(
                            "ProtocolFamily cant be converted to something that is known by the JDKi: " + @this);
            }
        }
    }

    extension(SocketProtocolFamily)
    {
        /**
         * Return the {@link SocketProtocolFamily} for the given {@link ProtocolFamily} if possible.
         *
         * @param family    the JDK {@link ProtocolFamily} to convert.
         * @return          the {@link SocketProtocolFamily}.
         * @throws          UnsupportedOperationException if it can't be converted.
         */
        // CLR: an enum cannot implement ProtocolFamily, so the SocketProtocolFamily identity branch has no input.
        public static SocketProtocolFamily Of(AddressFamily family)
        {
            switch (family)
            {
                case AddressFamily.InterNetwork:
                    return SocketProtocolFamily.Inet;
                case AddressFamily.InterNetworkV6:
                    return SocketProtocolFamily.Inet6;
                default:
                    // Just compare the name as we compile with Java11
                    if (family == AddressFamily.Unix)
                    {
                        return SocketProtocolFamily.Unix;
                    }
                    // Fall-through
                    break;
            }
            throw new NotSupportedException("ProtocolFamily is not supported: " + family);
        }
    }
}
