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

public class InternalLoggerFactoryTest : IDisposable
{
    private static readonly Exception e = new Exception();
    private IInternalLoggerFactory oldLoggerFactory;
    private IInternalLogger mockLogger;

    public InternalLoggerFactoryTest()
    {
        oldLoggerFactory = InternalLoggerFactory.getDefaultFactory();

        InternalLoggerFactory mockFactory = Mock.Of<InternalLoggerFactory>();
        mockLogger = Mock.Of<IInternalLogger>();
        
        Mock.Get(mockFactory).Setup(x => x.newInstance("mock")).Returns(mockLogger);
        InternalLoggerFactory.setDefaultFactory(mockFactory);
    }

    public void Dispose()
    {
        Mock.Get(mockLogger).Reset();
        InternalLoggerFactory.setDefaultFactory(oldLoggerFactory);
    }

    [Fact]
    public void shouldNotAllowNullDefaultFactory()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            InternalLoggerFactory.setDefaultFactory(null);
        });
    }

    [Fact]
    public void shouldGetInstance()
    {
        InternalLoggerFactory.setDefaultFactory(oldLoggerFactory);

        string helloWorld = "Hello, world!";

        IInternalLogger one = InternalLoggerFactory.getInstance("helloWorld");
        IInternalLogger two = InternalLoggerFactory.getInstance(helloWorld.GetType());

        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.NotSame(one, two);
    }

    [Fact]
    public void testIsTraceEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.isTraceEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        Assert.True(logger.isTraceEnabled());
        
        Mock.Get(mockLogger).Verify(x => x.isTraceEnabled(), Times.Once);
    }

    [Fact]
    public void testIsDebugEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.isDebugEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        Assert.True(logger.isDebugEnabled());
        Mock.Get(mockLogger).Verify(x => x.isDebugEnabled(), Times.Once);
    }

    [Fact]
    public void testIsInfoEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.isInfoEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        Assert.True(logger.isInfoEnabled());
        Mock.Get(mockLogger).Verify(x => x.isInfoEnabled(), Times.Once);
    }

    [Fact]
    public void testIsWarnEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.isWarnEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        Assert.True(logger.isWarnEnabled());
        Mock.Get(mockLogger).Verify(x => x.isWarnEnabled(), Times.Once);
    }

    [Fact]
    public void testIsErrorEnabled()
    {
        Mock.Get(mockLogger).Setup(x => x.isErrorEnabled()).Returns(true);

        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        Assert.True(logger.isErrorEnabled());
        Mock.Get(mockLogger).Verify(x => x.isErrorEnabled(), Times.Once);
    }

    [Fact]
    public void testTrace()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.trace("a");
        Mock.Get(mockLogger).Verify(x => x.trace("a"), Times.Once);
    }

    [Fact]
    public void testTraceWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.trace("a", e);
        Mock.Get(mockLogger).Verify(x => x.trace("a", e), Times.Once);
    }

    [Fact]
    public void testDebug()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.debug("a");
        Mock.Get(mockLogger).Verify(x => x.debug("a"), Times.Once);
    }

    [Fact]
    public void testDebugWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.debug("a", e);
        Mock.Get(mockLogger).Verify(x => x.debug("a", e), Times.Once);
    }

    [Fact]
    public void testInfo()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.info("a");
        Mock.Get(mockLogger).Verify(x => x.info("a"), Times.Once);
    }

    [Fact]
    public void testInfoWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.info("a", e);
        Mock.Get(mockLogger).Verify(x => x.info("a", e), Times.Once);
    }

    [Fact]
    public void testWarn()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.warn("a");
        Mock.Get(mockLogger).Verify(x => x.warn("a"), Times.Once);
    }

    [Fact]
    public void testWarnWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.warn("a", e);
        Mock.Get(mockLogger).Verify(x => x.warn("a", e), Times.Once);
    }

    [Fact]
    public void testError()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.error("a");
        Mock.Get(mockLogger).Verify(x => x.error("a"), Times.Once);
    }

    [Fact]
    public void testErrorWithException()
    {
        IInternalLogger logger = InternalLoggerFactory.getInstance("mock");
        logger.error("a", e);
        Mock.Get(mockLogger).Verify(x => x.error("a", e), Times.Once);
    }
}