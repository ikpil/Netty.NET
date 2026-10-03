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
    public void TestIsTraceEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.IsTraceEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.IsTraceEnabled());

        Mock.Get(mockLog).Verify(x => x.IsTraceEnabled(), Times.Once);
    }

    [Fact]
    public void TestIsDebugEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.IsDebugEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.IsDebugEnabled());

        Mock.Get(mockLog).Verify(x => x.IsDebugEnabled(), Times.Once);
    }

    [Fact]
    public void TestIsInfoEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.IsInfoEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.IsInfoEnabled());

        Mock.Get(mockLog).Verify(x => x.IsInfoEnabled(), Times.Once);
    }

    [Fact]
    public void TestIsWarnEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.IsWarnEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.IsWarnEnabled());

        Mock.Get(mockLog).Verify(x => x.IsWarnEnabled(), Times.Once);
    }

    [Fact]
    public void TestIsErrorEnabled()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        Mock.Get(mockLog).Setup(x => x.IsErrorEnabled()).Returns(true);

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        Assert.True(logger.IsErrorEnabled());

        Mock.Get(mockLog).Verify(x => x.IsErrorEnabled(), Times.Once);
    }

    [Fact]
    public void TestTrace()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Trace("a");

        Mock.Get(mockLog).Verify(x => x.Trace("a"), Times.Once);
    }

    [Fact]
    public void TestTraceWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Trace("a", e);

        Mock.Get(mockLog).Verify(x => x.Trace("a", e), Times.Once);
    }

    [Fact]
    public void TestDebug()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Debug("a");

        Mock.Get(mockLog).Verify(x => x.Debug("a"), Times.Once);
    }

    [Fact]
    public void TestDebugWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Debug("a", e);

        Mock.Get(mockLog).Verify(x => x.Debug("a", e), Times.Once);
    }

    [Fact]
    public void TestInfo()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Info("a");

        Mock.Get(mockLog).Verify(x => x.Info("a"), Times.Once);
    }

    [Fact]
    public void TestInfoWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Info("a", e);

        Mock.Get(mockLog).Verify(x => x.Info("a", e), Times.Once);
    }

    [Fact]
    public void TestWarn()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Warn("a");

        Mock.Get(mockLog).Verify(x => x.Warn("a"), Times.Once);
    }

    [Fact]
    public void TestWarnWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Warn("a", e);

        Mock.Get(mockLog).Verify(x => x.Warn("a", e), Times.Once);
    }

    [Fact]
    public void TestError()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Error("a");

        Mock.Get(mockLog).Verify(x => x.Error("a"), Times.Once);
    }

    [Fact]
    public void TestErrorWithException()
    {
        IInternalLogger mockLog = Mock.Of<IInternalLogger>();

        IInternalLogger logger = new CommonsLogger(mockLog, "foo");
        logger.Error("a", e);

        Mock.Get(mockLog).Verify(x => x.Error("a", e), Times.Once);
    }
}