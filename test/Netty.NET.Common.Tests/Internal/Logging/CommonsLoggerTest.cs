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
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Tests.Internal.Logging;

public class CommonsLoggerTest
{
    private static readonly Exception e = new Exception();

    [Fact]
    public void testIsTraceEnabled()
    {
        Log mockLog = mock(Log.class);

        when(mockLog.isTraceEnabled()).thenReturn(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isTraceEnabled());

        verify(mockLog).isTraceEnabled();
    }

    [Fact]
    public void testIsDebugEnabled()
    {
        Log mockLog = mock(Log.class);

        when(mockLog.isDebugEnabled()).thenReturn(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isDebugEnabled());

        verify(mockLog).isDebugEnabled();
    }

    [Fact]
    public void testIsInfoEnabled()
    {
        Log mockLog = mock(Log.class);

        when(mockLog.isInfoEnabled()).thenReturn(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isInfoEnabled());

        verify(mockLog).isInfoEnabled();
    }

    [Fact]
    public void testIsWarnEnabled()
    {
        Log mockLog = mock(Log.class);

        when(mockLog.isWarnEnabled()).thenReturn(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isWarnEnabled());

        verify(mockLog).isWarnEnabled();
    }

    [Fact]
    public void testIsErrorEnabled()
    {
        Log mockLog = mock(Log.class);

        when(mockLog.isErrorEnabled()).thenReturn(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isErrorEnabled());

        verify(mockLog).isErrorEnabled();
    }

    [Fact]
    public void testTrace()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.trace("a");

        verify(mockLog).trace("a");
    }

    [Fact]
    public void testTraceWithException()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.trace("a", e);

        verify(mockLog).trace("a", e);
    }

    [Fact]
    public void testDebug()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.debug("a");

        verify(mockLog).debug("a");
    }

    [Fact]
    public void testDebugWithException()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.debug("a", e);

        verify(mockLog).debug("a", e);
    }

    [Fact]
    public void testInfo()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.info("a");

        verify(mockLog).info("a");
    }

    [Fact]
    public void testInfoWithException()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.info("a", e);

        verify(mockLog).info("a", e);
    }

    [Fact]
    public void testWarn()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.warn("a");

        verify(mockLog).warn("a");
    }

    [Fact]
    public void testWarnWithException()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.warn("a", e);

        verify(mockLog).warn("a", e);
    }

    [Fact]
    public void testError()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.error("a");

        verify(mockLog).error("a");
    }

    [Fact]
    public void testErrorWithException()
    {
        Log mockLog = mock(Log.class);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.error("a", e);

        verify(mockLog).error("a", e);
    }
}