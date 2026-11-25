/*
 * Copyright 2026 The Netty Project
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

namespace Netty.NET.Common.Internal;

// CLR: java.net.UnknownHostException, "Thrown to indicate that the IP address of a host could not be determined"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/UnknownHostException.html). Owner decision
// 2026-11-25 (user: "공용 타입 도입"): one shared type for Resolver, Transport and resolver-dns, public in the Internal
// namespace like IDN, instead of SocketException(SocketError.HostNotFound), because resolver-dns records the reason of a
// failed lookup as the cause (DnsErrorCauseException, a timeout, a transport setup failure) and the BCL SocketException
// has no constructor that takes an inner exception
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketexception.-ctor?view=net-10.0).
// The Java class extends java.io.IOException, so this one extends System.IO.IOException, whose (String, Exception)
// constructor keeps the cause (https://learn.microsoft.com/en-us/dotnet/api/system.io.ioexception?view=net-10.0); a catch
// of SocketException does not see it. Java has the constructors () and (String) and sets a cause with initCause, which
// "Initializes the cause of this throwable to the specified value"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Throwable.html#initCause(java.lang.Throwable));
// the (string, Exception) constructor stands for that call. Without a message, Exception.Message is the system default
// where Java's getMessage() is null. Serializable has no counterpart.
public class UnknownHostException : IOException
{
    public UnknownHostException()
    {
    }

    public UnknownHostException(string message) : base(message)
    {
    }

    public UnknownHostException(string message, Exception cause) : base(message, cause)
    {
    }
}
