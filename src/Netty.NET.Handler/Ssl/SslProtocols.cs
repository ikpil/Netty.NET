/*
 * Copyright 2021 The Netty Project
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

namespace Netty.NET.Handler.Ssl;

/**
 * SSL/TLS protocols
 */
// CLR: SSL_v2_HELLO, SSL_v2, SSL_v3, TLS_v1 and TLS_v1_1 are @Deprecated and their @deprecated text names the
// replacements ("Consider using {@link #TLS_v1_2} or {@link #TLS_v1_3}"), so they are not ported; code that must
// name those protocols uses their text ("SSLv2Hello", "SSLv2", "SSLv3", "TLSv1", "TLSv1.1"). Inside this namespace
// the simple name SslProtocols is this class, not System.Security.Authentication.SslProtocols.
public sealed class SslProtocols
{

    /**
     * TLS v1.2
     */
    public const string TLS_v1_2 = "TLSv1.2";

    /**
     * TLS v1.3
     */
    public const string TLS_v1_3 = "TLSv1.3";

    private SslProtocols()
    {
        // Prevent outside initialization
    }
}
