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

using System.Security.Authentication;

namespace Netty.NET.Handler.Ssl;

/**
 * {@link SSLException} which signals that the exception was caused by an {@link javax.net.ssl.SSLEngine} which was
 * closed already.
 */
// CLR: SSLException is AuthenticationException, as in NotSslRecordException (ssl design decision 3,
// docs/handler-ssl-design.md); the SSLEngine of the Javadoc is the SslStream that SslHandler drives. serialVersionUID
// is JVM serialization metadata and has no counterpart.
public sealed class SslClosedEngineException : AuthenticationException
{

    public SslClosedEngineException(string reason) : base(reason)
    {
    }
}
