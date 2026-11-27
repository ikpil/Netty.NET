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

using System;
using System.IO;

namespace Netty.NET.Handler.Proxy;

// CLR: java.net.ConnectException ("Signals that an error occurred while attempting to connect a socket to a remote
// address and port", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/ConnectException.html)
// extends SocketException, which extends java.io.IOException. The CLR's System.Net.Sockets.SocketException has no
// constructor that takes an inner exception (SocketException(), (int errorCode), (int errorCode, string? message),
// https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketexception.-ctor?view=net-10.0), so a subclass
// could not keep the cause that ProxyConnectException(Throwable) and (String, Throwable) record with initCause. The
// class therefore extends System.IO.IOException, the CLR counterpart of java.io.IOException ("The exception that is
// thrown when an I/O error occurs", with IOException(String, Exception),
// https://learn.microsoft.com/en-us/dotnet/api/system.io.ioexception?view=net-10.0), as the Transport's
// ClosedChannelException does. A catch of SocketException does not see it. initCause(cause) is the innerException of
// the base constructor; a constructor without a message passes none, so Exception.Message is the system's default
// message where Java's getMessage() is null. serialVersionUID is JVM serialization metadata and has no counterpart.
public class ProxyConnectException : IOException
{
    public ProxyConnectException()
    {
    }

    public ProxyConnectException(string msg) : base(msg)
    {
    }

    public ProxyConnectException(Exception cause) : base(null, cause)
    {
    }

    public ProxyConnectException(string msg, Exception cause) : base(msg, cause)
    {
    }
}
