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
using System.Security.Authentication;

namespace Netty.NET.Handler.Ssl;

/**
 * Special {@link SSLException} which will get thrown if a packet is
 * received that not looks like a TLS/SSL record. A user can check for
 * this {@link NotSslRecordException} and so detect if one peer tries to
 * use secure and the other plain connection.
 *
 *
 */
// CLR: javax.net.ssl.SSLException ("Indicates some kind of error detected by an SSL subsystem",
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLException.html) is
// System.Security.Authentication.AuthenticationException, which "The NegotiateStream and SslStream classes throw ...
// when the client or server cannot be authenticated"
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.authentication.authenticationexception?view=net-10.0;
// ssl design decision 3, docs/handler-ssl-design.md). SSLException extends java.io.IOException, AuthenticationException
// extends SystemException, so a catch of IOException does not see it. serialVersionUID is JVM serialization metadata
// and has no counterpart.
public class NotSslRecordException : AuthenticationException
{
    public NotSslRecordException() : base("")
    {
    }

    public NotSslRecordException(string message) : base(message)
    {
    }

    // CLR: SSLException(Throwable) has "a detail message of (cause==null ? null : cause.toString())"; Throwable.toString()
    // is the class name, ": " and the message, built as CodecException(Exception) builds it
    // (src/Netty.NET.Handler.Codec/CodecException.cs). AuthenticationException has no (Exception) constructor, so the
    // (string, Exception) constructor carries both.
    public NotSslRecordException(Exception cause)
        : base(cause == null ? null : cause.GetType().FullName + ": " + cause.Message, cause)
    {
    }

    public NotSslRecordException(string message, Exception cause) : base(message, cause)
    {
    }

}
