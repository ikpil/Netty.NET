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

[Collection("System properties")]
public class InternalLoggerFactoryTest : IDisposable
{
    private static readonly Exception e = new Exception();
    private IInternalLoggerFactory oldLoggerFactory;
    private IInternalLogger mockLogger;

    public InternalLoggerFactoryTest()
    {
        oldLoggerFactory = InternalLoggerFactory.GetDefaultFactory();

        InternalLoggerFactory mockFactory = Mock.Of<InternalLoggerFactory>();
        mockLogger = Mock.Of<IInternalLogger>();
        
        // CLR tests share process-wide factories with background executors.
        // Only the observed category is mocked; unrelated logger creation
        // must still return a real logger even during this exclusive test.
        Mock.Get(mockFactory).Setup(x => x.NewInstance(It.IsAny<string>()))
            .Returns((string name) => name == "mock" ? mockLogger : oldLoggerFactory.NewInstance(name));
        InternalLoggerFactory.SetDefaultFactory(mockFactory);
    }

    public void Dispose()
    {
        Mock.Get(mockLogger).Reset();
        InternalLoggerFactory.SetDefaultFactory(oldLoggerFactory);
    }

    [Fact]
    public void ShouldNotAllowNullDefaultFactory()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            InternalLoggerFactory.SetDefaultFactory(null);
        });
    }

    [Fact]
    public void ShouldGetInstance()
    {
        InternalLoggerFactory.SetDefaultFactory(oldLoggerFactory);

        string helloWorld = "Hello, world!";

        IInternalLogger one = InternalLoggerFactory.GetInstance("helloWorld");
        IInternalLogger two = InternalLoggerFactory.GetInstance(helloWorld.GetType());

        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.NotSame(one, two);
    }

    [Fact]
    public void TestIsTraceEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.IsTraceEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        Assert.True(logger.IsTraceEnabled());
        
        Mock.Get(mockLogger).Verify(x => x.IsTraceEnabled(), Times.Once);
    }

    [Fact]
    public void TestIsDebugEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.IsDebugEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        Assert.True(logger.IsDebugEnabled());
        Mock.Get(mockLogger).Verify(x => x.IsDebugEnabled(), Times.Once);
    }

    [Fact]
    public void TestIsInfoEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.IsInfoEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        Assert.True(logger.IsInfoEnabled());
        Mock.Get(mockLogger).Verify(x => x.IsInfoEnabled(), Times.Once);
    }

    [Fact]
    public void TestIsWarnEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.IsWarnEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        Assert.True(logger.IsWarnEnabled());
        Mock.Get(mockLogger).Verify(x => x.IsWarnEnabled(), Times.Once);
    }

    [Fact]
    public void TestIsErrorEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.IsErrorEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        Assert.True(logger.IsErrorEnabled());
        Mock.Get(mockLogger).Verify(x => x.IsErrorEnabled(), Times.Once);
    }

    [Fact]
    public void TestTrace()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Trace("a");
        Mock.Get(mockLogger).Verify(x => x.Trace("a"), Times.Once);
    }

    [Fact]
    public void TestTraceWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Trace("a", e);
        Mock.Get(mockLogger).Verify(x => x.Trace("a", e), Times.Once);
    }

    [Fact]
    public void TestDebug()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Debug("a");
        Mock.Get(mockLogger).Verify(x => x.Debug("a"), Times.Once);
    }

    [Fact]
    public void TestDebugWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Debug("a", e);
        Mock.Get(mockLogger).Verify(x => x.Debug("a", e), Times.Once);
    }

    [Fact]
    public void TestInfo()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Info("a");
        Mock.Get(mockLogger).Verify(x => x.Info("a"), Times.Once);
    }

    [Fact]
    public void TestInfoWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Info("a", e);
        Mock.Get(mockLogger).Verify(x => x.Info("a", e), Times.Once);
    }

    [Fact]
    public void TestWarn()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Warn("a");
        Mock.Get(mockLogger).Verify(x => x.Warn("a"), Times.Once);
    }

    [Fact]
    public void TestWarnWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Warn("a", e);
        Mock.Get(mockLogger).Verify(x => x.Warn("a", e), Times.Once);
    }

    [Fact]
    public void TestError()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Error("a");
        Mock.Get(mockLogger).Verify(x => x.Error("a"), Times.Once);
    }

    [Fact]
    public void TestErrorWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance("mock");
        logger.Error("a", e);
        Mock.Get(mockLogger).Verify(x => x.Error("a", e), Times.Once);
    }
}
