/*
 * Copyright 2013 The Netty Project
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
namespace Netty.NET.Transport.Channels;
/**
 * {@link ConnectException} which will be thrown if a connection could
 * not be established because of a connection timeout.
 */
// CLR: java.net.ConnectException (a SocketException) has no CLR counterpart type; System.TimeoutException is the
// BCL timeout exception (https://learn.microsoft.com/dotnet/api/system.timeoutexception), and the connect Task fails
// with it as the Java promise fails with ConnectTimeoutException.
public class ConnectTimeoutException : TimeoutException
{
    public ConnectTimeoutException() { }
    public ConnectTimeoutException(string msg) : base(msg) { }
    public ConnectTimeoutException(string msg, Exception innerException) : base(msg, innerException) { }
    public ConnectTimeoutException(Exception cause) : base(cause?.Message, cause) { }
}
