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
using System.Threading;

namespace Netty.NET.Common.Internal.Logging;

/**
 * Creates an {@link IInternalLogger} or changes the default factory
 * implementation.  This factory allows you to choose what logging framework
 * Netty should use.  The default factory is {@link Slf4JLoggerFactory}.  If SLF4J
 * is not available, {@link Log4JLoggerFactory} is used.  If Log4J is not available,
 * {@link JdkLoggerFactory} is used.  You can change it to your preferred
 * logging framework before other Netty classes are loaded:
 * <pre>
 * {@link InternalLoggerFactory}.setDefaultFactory({@link Log4JLoggerFactory}.INSTANCE);
 * </pre>
 * Please note that the new default factory is effective only for the classes
 * which were loaded after the default factory is changed.  Therefore,
 * {@link #setDefaultFactory(InternalLoggerFactory)} should be called as early
 * as possible and shouldn't be called more than once.
 */
public abstract class InternalLoggerFactory : IInternalLoggerFactory
{
    private static IInternalLoggerFactory defaultFactory;

    private static IInternalLoggerFactory newDefaultFactory(string name)
    {
        return new InternalDefaultLoggerFactory();
    }

    /**
     * Returns the default factory.  The initial default factory is
     * {@link JdkLoggerFactory}.
     */
    public static IInternalLoggerFactory getDefaultFactory()
    {
        if (defaultFactory == null)
        {
            var factory = Volatile.Read(ref defaultFactory);
            if (factory == null)
            {
                factory = newDefaultFactory(typeof(InternalLoggerFactory).FullName);
                var current = Interlocked.CompareExchange(ref defaultFactory, factory, null);
                if (current == null)
                {
                    return factory;
                }
            }
        }

        return defaultFactory;
    }

    /**
     * Changes the default factory.
     */
    public static void setDefaultFactory(IInternalLoggerFactory factory)
    {
        ObjectUtil.checkNotNull(factory, "defaultFactory");
        Volatile.Write(ref defaultFactory, factory);
    }

    /**
     * Creates a new logger instance with the name of the specified class.
     */
    public static IInternalLogger getInstance<T>()
    {
        return getInstance(typeof(T));
    }

    public static IInternalLogger getInstance(Type type)
    {
        return getInstance(type.FullName);
    }


    /**
     * Creates a new logger instance with the specified name.
     */
    public static IInternalLogger getInstance(string name)
    {
        return getDefaultFactory().newInstance(name);
    }

    /**
     * Creates a new logger instance with the specified name.
     */
    public abstract IInternalLogger newInstance(string name);
}