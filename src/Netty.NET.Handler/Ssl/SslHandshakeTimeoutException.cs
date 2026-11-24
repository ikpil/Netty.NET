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
 * {@link SSLHandshakeException} that is used when a handshake failed due a configured timeout.
 */
// CLR: javax.net.ssl.SSLHandshakeException ("Indicates that the client and server could not negotiate the desired level
// of security", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLHandshakeException.html)
// is AuthenticationException, the exception SslStream throws when its handshake fails
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.authentication.authenticationexception?view=net-10.0;
// ssl design decision 3, docs/handler-ssl-design.md). SSLException and SSLHandshakeException map to the same type, so
// a handshake failure is not distinguished by type from other SSL errors.
public sealed class SslHandshakeTimeoutException : AuthenticationException
{

    public SslHandshakeTimeoutException(string reason) : base(reason)
    {
    }
}
