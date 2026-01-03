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
using Moq;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Tests.Internal.Logging;

public class CommonsLoggerTest
{
    private static readonly Exception e = new Exception();

    [Fact]
    public void testIsTraceEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.isTraceEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isTraceEnabled());

        Mock.Get(mockLog).Verify(x => x.isTraceEnabled(), Times.Once);
    }

    [Fact]
    public void testIsDebugEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.isDebugEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isDebugEnabled());

        Mock.Get(mockLog).Verify(x => x.isDebugEnabled(), Times.Once);
    }

    [Fact]
    public void testIsInfoEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.isInfoEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isInfoEnabled());

        Mock.Get(mockLog).Verify(x => x.isInfoEnabled(), Times.Once);
    }

    [Fact]
    public void testIsWarnEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.isWarnEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isWarnEnabled());

        Mock.Get(mockLog).Verify(x => x.isWarnEnabled(), Times.Once);
    }

    [Fact]
    public void testIsErrorEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.isErrorEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.isErrorEnabled());

        Mock.Get(mockLog).Verify(x => x.isErrorEnabled(), Times.Once);
    }

    [Fact]
    public void testTrace()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.trace("a");

        Mock.Get(mockLog).Verify(x => x.trace("a"), Times.Once);
    }

    [Fact]
    public void testTraceWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.trace("a", e);

        Mock.Get(mockLog).Verify(x => x.trace("a", e), Times.Once);
    }

    [Fact]
    public void testDebug()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.debug("a");

        Mock.Get(mockLog).Verify(x => x.debug("a"), Times.Once);
    }

    [Fact]
    public void testDebugWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.debug("a", e);

        Mock.Get(mockLog).Verify(x => x.debug("a", e), Times.Once);
    }

    [Fact]
    public void testInfo()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.info("a");

        Mock.Get(mockLog).Verify(x => x.info("a"), Times.Once);
    }

    [Fact]
    public void testInfoWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.info("a", e);

        Mock.Get(mockLog).Verify(x => x.info("a", e), Times.Once);
    }

    [Fact]
    public void testWarn()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.warn("a");

        Mock.Get(mockLog).Verify(x => x.warn("a"), Times.Once);
    }

    [Fact]
    public void testWarnWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.warn("a", e);

        Mock.Get(mockLog).Verify(x => x.warn("a", e), Times.Once);
    }

    [Fact]
    public void testError()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.error("a");

        Mock.Get(mockLog).Verify(x => x.error("a"), Times.Once);
    }

    [Fact]
    public void testErrorWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.error("a", e);

        Mock.Get(mockLog).Verify(x => x.error("a", e), Times.Once);
    }
}