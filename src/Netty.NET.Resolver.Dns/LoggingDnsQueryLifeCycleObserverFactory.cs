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

using System;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Handler.Logging;

namespace Netty.NET.Resolver.Dns;

/**
 * A {@link DnsQueryLifecycleObserverFactory} that enables detailed logging in the {@link DnsNameResolver}.
 * <p>
 * When {@linkplain DnsNameResolverBuilder#dnsQueryLifecycleObserverFactory(DnsQueryLifecycleObserverFactory)
 * configured on the resolver}, detailed trace information will be generated so that it is easier to understand the
 * cause of resolution failure.
 */
// CLR: LogLevel is handler's Netty.NET.Handler.Logging.LogLevel (ToInternalLevel()); InternalLogger/InternalLoggerFactory
// are Common's IInternalLogger/InternalLoggerFactory and Class<?> classContext is a Type (docs/handler-porting.md,
// Logging).
public sealed class LoggingDnsQueryLifeCycleObserverFactory : IDnsQueryLifecycleObserverFactory
{
    private static readonly IInternalLogger DEFAULT_LOGGER =
            InternalLoggerFactory.GetInstance(typeof(LoggingDnsQueryLifeCycleObserverFactory));
    private readonly IInternalLogger logger;
    private readonly InternalLogLevel level;

    /**
     * Create {@link DnsQueryLifecycleObserver} instances that log events at the default {@link LogLevel#DEBUG} level.
     */
    public LoggingDnsQueryLifeCycleObserverFactory() : this(LogLevel.DEBUG)
    {
    }

    /**
     * Create {@link DnsQueryLifecycleObserver} instances that log events at the given log level.
     * @param level The log level to use for logging resolver events.
     */
    public LoggingDnsQueryLifeCycleObserverFactory(LogLevel level)
    {
        this.level = CheckAndConvertLevel(level);
        logger = DEFAULT_LOGGER;
    }

    /**
     * Create {@link DnsQueryLifecycleObserver} instances that log events to a logger with the given class context,
     * at the given log level.
     * @param classContext The class context for the logger to use.
     * @param level The log level to use for logging resolver events.
     */
    public LoggingDnsQueryLifeCycleObserverFactory(Type classContext, LogLevel level)
    {
        this.level = CheckAndConvertLevel(level);
        logger = InternalLoggerFactory.GetInstance(ObjectUtil.CheckNotNull(classContext, "classContext"));
    }

    /**
     * Create {@link DnsQueryLifecycleObserver} instances that log events to a logger with the given name context,
     * at the given log level.
     * @param name The name for the logger to use.
     * @param level The log level to use for logging resolver events.
     */
    public LoggingDnsQueryLifeCycleObserverFactory(string name, LogLevel level)
    {
        this.level = CheckAndConvertLevel(level);
        logger = InternalLoggerFactory.GetInstance(ObjectUtil.CheckNotNull(name, "name"));
    }

    private static InternalLogLevel CheckAndConvertLevel(LogLevel level)
    {
        return ObjectUtil.CheckNotNull(level, "level").ToInternalLevel();
    }

    public IDnsQueryLifecycleObserver NewDnsQueryLifecycleObserver(IDnsQuestion question)
    {
        return new LoggingDnsQueryLifecycleObserver(question, logger, level);
    }
}
