/*
 * Copyright 2017 The Netty Project
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
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Dns;
using DnsResponseCode = Netty.NET.Handler.Codec.Dns.DnsResponseCode;

namespace Netty.NET.Resolver.Dns;

// CLR: InternalLogger/InternalLogLevel are Common's IInternalLogger/InternalLogLevel; Common's MessageFormatter
// treats a trailing Exception argument as the throwable, as the JDK MessageFormatter does. An address prints as
// EndPoint.ToString(). InternalLogLevel is an enum, so checkNotNull(level) has no counterpart.
internal sealed class LoggingDnsQueryLifecycleObserver : IDnsQueryLifecycleObserver
{
    private readonly IInternalLogger logger;
    private readonly InternalLogLevel level;
    private readonly IDnsQuestion question;
    private EndPoint dnsServerAddress;

    internal LoggingDnsQueryLifecycleObserver(IDnsQuestion question, IInternalLogger logger, InternalLogLevel level)
    {
        this.question = ObjectUtil.CheckNotNull(question, "question");
        this.logger = ObjectUtil.CheckNotNull(logger, "logger");
        this.level = level;
    }

    public void QueryWritten(EndPoint dnsServerAddress, Task future)
    {
        this.dnsServerAddress = dnsServerAddress;
    }

    public void QueryCancelled(int queriesRemaining)
    {
        if (dnsServerAddress != null)
        {
            logger.Log(level, "from {} : {} cancelled with {} queries remaining", dnsServerAddress, question,
                        queriesRemaining);
        }
        else
        {
            logger.Log(level, "{} query never written and cancelled with {} queries remaining", question,
                        queriesRemaining);
        }
    }

    public IDnsQueryLifecycleObserver QueryRedirected(IReadOnlyList<EndPoint> nameServers)
    {
        logger.Log(level, "from {} : {} redirected", dnsServerAddress, question);
        return this;
    }

    public IDnsQueryLifecycleObserver QueryCNAMEd(IDnsQuestion cnameQuestion)
    {
        logger.Log(level, "from {} : {} CNAME question {}", dnsServerAddress, question, cnameQuestion);
        return this;
    }

    public IDnsQueryLifecycleObserver QueryNoAnswer(DnsResponseCode code)
    {
        logger.Log(level, "from {} : {} no answer {}", dnsServerAddress, question, code);
        return this;
    }

    public void QueryFailed(Exception cause)
    {
        if (dnsServerAddress != null)
        {
            logger.Log(level, "from {} : {} failure", dnsServerAddress, question, cause);
        }
        else
        {
            logger.Log(level, "{} query never written and failed", question, cause);
        }
    }

    public void QuerySucceed()
    {
    }
}
