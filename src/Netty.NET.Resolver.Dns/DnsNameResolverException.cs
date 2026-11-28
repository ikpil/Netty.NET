/*
 * Copyright 2015 The Netty Project
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
using System.Net;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Dns;

namespace Netty.NET.Resolver.Dns;

/**
 * A {@link RuntimeException} raised when {@link DnsNameResolver} failed to perform a successful query.
 */
// CLR: RuntimeException is Exception; InetSocketAddress is EndPoint (the name server address, an IPEndPoint when
// the core passes a resolved one; docs/handler-porting.md Addresses) and DnsQuestion is codec-dns IDnsQuestion.
// serialVersionUID has no counterpart. The fillInStackTrace() override, which replaces the stack trace that the
// JDK records at construction with EMPTY_STACK_TRACE
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Throwable.html), has no counterpart: a
// CLR exception records its stack trace only when it is thrown
// (https://learn.microsoft.com/en-us/dotnet/api/system.exception.stacktrace?view=net-10.0), so an instance that is
// passed to a Task without being thrown has none.
public class DnsNameResolverException : Exception
{

    private readonly EndPoint remoteAddress;
    private readonly IDnsQuestion question;

    public DnsNameResolverException(EndPoint remoteAddress, IDnsQuestion question, string message) : base(message)
    {
        this.remoteAddress = ValidateRemoteAddress(remoteAddress);
        this.question = ValidateQuestion(question);
    }

    public DnsNameResolverException(
            EndPoint remoteAddress, IDnsQuestion question, string message, Exception cause) : base(message, cause)
    {
        this.remoteAddress = ValidateRemoteAddress(remoteAddress);
        this.question = ValidateQuestion(question);
    }

    private static EndPoint ValidateRemoteAddress(EndPoint remoteAddress)
    {
        return ObjectUtil.CheckNotNull(remoteAddress, "remoteAddress");
    }

    private static IDnsQuestion ValidateQuestion(IDnsQuestion question)
    {
        return ObjectUtil.CheckNotNull(question, "question");
    }

    /**
     * Returns the {@link InetSocketAddress} of the DNS query that has failed.
     */
    public EndPoint RemoteAddress()
    {
        return remoteAddress;
    }

    /**
     * Returns the {@link DnsQuestion} of the DNS query that has failed.
     */
    public IDnsQuestion Question()
    {
        return question;
    }
}
