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
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal.Logging;

namespace Netty.Common.Tests.Internal.Logging;

/**
 * We only need to test methods defined by {@link IInternalLogger}.
 */
public abstract class AbstractInternalLoggerTest<T>
{
    protected string loggerName = "foo";
    protected T mockLog;
    protected IInternalLogger logger;
    protected readonly Dictionary<string, object> result = new Dictionary<string, object>();

    //@SuppressWarnings("unchecked")
    protected V getResult<V>(string key)
    {
        result.TryGetValue(key, out var o);
        if (null == o)
            return default!;

        return (V)o;
    }

    [Fact]
    public void testName()
    {
        Assert.Equal(loggerName, logger.name());
    }

    [Fact]
    public void testAllLevel()
    {
        testLevel(InternalLogLevel.TRACE);
        testLevel(InternalLogLevel.DEBUG);
        testLevel(InternalLogLevel.INFO);
        testLevel(InternalLogLevel.WARN);
        testLevel(InternalLogLevel.ERROR);
    }

    protected void testLevel(InternalLogLevel level)
    {
        result.Clear();

        string format1 = "a={}", format2 = "a={}, b= {}", format3 = "a={}, b= {}, c= {}";
        string msg = "a test message from Junit";
        Exception ex = new Exception("a test Exception from Junit");

        Type clazz = typeof(IInternalLogger);
        string levelName = level.ToString(), logMethod = levelName.ToLower();
        var isXXEnabled = clazz
            .GetMethod("is" + levelName.charAt(0) + levelName.substring(1).ToLower() + "Enabled")!;

        // when level log is disabled
        setLevelEnable(level, false);
        Assert.False((bool)isXXEnabled.Invoke(logger, null));

        // test xx(msg)
        clazz.GetMethod(logMethod, [typeof(string)]).Invoke(logger, new object[] { msg });
        Assert.True(result.IsEmpty());

        // test xx(format, arg)
        clazz.GetMethod(logMethod, [typeof(string), typeof(object)]).Invoke(logger, new object[] { format1, msg });
        Assert.True(result.IsEmpty());

        // test xx(format, argA, argB)
        clazz.GetMethod(logMethod, [typeof(string), typeof(object), typeof(object)]).Invoke(logger, new object[] { format2, msg, msg });
        Assert.True(result.IsEmpty());

        // test xx(format, ...arguments)
        clazz.GetMethod(logMethod, [typeof(string), typeof(object[])]).Invoke(logger, new object[] { format3, msg, msg, msg });
        Assert.True(result.IsEmpty());

        // test xx(format, ...arguments), the last argument is Throwable
        clazz.GetMethod(logMethod, [typeof(string), typeof(object[])]).Invoke(logger, new object[] { format3, msg, msg, msg, ex });
        Assert.True(result.IsEmpty());

        // test xx(msg, Throwable)
        clazz.GetMethod(logMethod, [typeof(string), typeof(object)]).Invoke(logger, new object[] { msg, ex });
        Assert.True(result.IsEmpty());

        // test xx(Throwable)
        clazz.GetMethod(logMethod, [typeof(Exception)]).Invoke(logger, new object[] { ex });
        Assert.True(result.IsEmpty());

        // when level log is enabled
        setLevelEnable(level, true);
        Assert.True((bool)isXXEnabled.Invoke(logger, null));

        // test xx(msg)
        result.Clear();
        clazz.GetMethod(logMethod, [typeof(string)]).Invoke(logger, new object[] { msg });
        assertResult(level, null, null, msg);

        // test xx(format, arg)
        result.Clear();
        clazz.GetMethod(logMethod, [typeof(string), typeof(object)]).Invoke(logger, new object[] { format1, msg });
        assertResult(level, format1, null, msg);

        // test xx(format, argA, argB)
        result.Clear();
        clazz.GetMethod(logMethod, [typeof(string), typeof(object), typeof(object)]).Invoke(logger, new object[] { format2, msg, msg });
        assertResult(level, format2, null, msg, msg);

        // test xx(format, ...arguments)
        result.Clear();
        clazz.GetMethod(logMethod, [typeof(string), typeof(object[])]).Invoke(logger, new object[] { format3, msg, msg, msg });
        assertResult(level, format3, null, msg, msg, msg);

        // test xx(format, ...arguments), the last argument is Throwable
        result.Clear();
        clazz.GetMethod(logMethod, [typeof(string), typeof(object[])]).Invoke(logger, new object[] { format3, msg, msg, msg, ex });
        assertResult(level, format3, ex, msg, msg, msg, ex);

        // test xx(msg, Throwable)
        result.Clear();
        clazz.GetMethod(logMethod, [typeof(string), typeof(Exception)]).Invoke(logger, new object[] { msg, ex });
        assertResult(level, null, ex, msg);

        // test xx(Throwable)
        result.Clear();
        clazz.GetMethod(logMethod, [typeof(Exception)]).Invoke(logger, [ex]);
        assertResult(level, null, ex);
    }

    /** a just default code, you can override to fix {@linkplain #mockLog} */
    protected virtual void assertResult(InternalLogLevel level, string format, Exception t, params object[] args)
    {
        Assert.False(result.IsEmpty());
    }

    protected abstract void setLevelEnable(InternalLogLevel level, bool enable);
}