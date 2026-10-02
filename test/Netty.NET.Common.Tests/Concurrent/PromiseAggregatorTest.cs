/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Common.Concurrent;
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Concurrent;

public class PromiseAggregatorTest
{
    [Fact]
    public void testNullAggregatePromise() =>
        Assert.Throws<ArgumentNullException>(() => new PromiseAggregator<Void, IFuture<Void>>(null));

    [Fact]
    public void testAddNullFuture()
    {
        var p = new Mock<IPromise<Void>>();
        var a = new PromiseAggregator<Void, IFuture<Void>>(p.Object);
        Assert.Throws<ArgumentNullException>(() => a.add((IPromise<Void>[])null));
    }

    [Fact]
    public void testSuccessfulNoPending()
    {
        var p = new Mock<IPromise<Void>>();
        var a = new PromiseAggregator<Void, IFuture<Void>>(p.Object);
        var future = new Mock<IFuture<Void>>();
        p.Setup(x => x.setSuccess(null)).Returns(p.Object);
        a.add();
        a.operationComplete(future.Object);
        future.VerifyNoOtherCalls();
        p.Verify(x => x.setSuccess(null), Times.Once);
    }

    [Fact]
    public void testSuccessfulPending()
    {
        var p = new Mock<IPromise<Void>>();
        var a = new PromiseAggregator<Void, IFuture<Void>>(p.Object);
        var p1 = new Mock<IPromise<Void>>();
        var p2 = new Mock<IPromise<Void>>();
        p1.Setup(x => x.addListener<IFuture<Void>>(a)).Returns(p1.Object);
        p2.Setup(x => x.addListener<IFuture<Void>>(a)).Returns(p2.Object);
        p1.Setup(x => x.isSuccess()).Returns(true);
        p2.Setup(x => x.isSuccess()).Returns(true);
        p.Setup(x => x.setSuccess(null)).Returns(p.Object);

        Assert.Same(a, a.add(p1.Object, null, p2.Object));
        a.operationComplete(p1.Object);
        a.operationComplete(p2.Object);

        p1.Verify(x => x.addListener<IFuture<Void>>(a), Times.Once);
        p2.Verify(x => x.addListener<IFuture<Void>>(a), Times.Once);
        p1.Verify(x => x.isSuccess(), Times.Once);
        p2.Verify(x => x.isSuccess(), Times.Once);
        p.Verify(x => x.setSuccess(null), Times.Once);
    }

    [Fact]
    public void testFailedFutureFailPending()
    {
        var p = new Mock<IPromise<Void>>();
        var a = new PromiseAggregator<Void, IFuture<Void>>(p.Object);
        var p1 = new Mock<IPromise<Void>>();
        var p2 = new Mock<IPromise<Void>>();
        var cause = new Exception();
        p1.Setup(x => x.addListener<IFuture<Void>>(a)).Returns(p1.Object);
        p2.Setup(x => x.addListener<IFuture<Void>>(a)).Returns(p2.Object);
        p1.Setup(x => x.isSuccess()).Returns(false);
        p1.Setup(x => x.cause()).Returns(cause);
        p.Setup(x => x.setFailure(cause)).Returns(p.Object);
        p2.Setup(x => x.setFailure(cause)).Returns(p2.Object);

        a.add(p1.Object, p2.Object);
        a.operationComplete(p1.Object);

        p1.Verify(x => x.addListener<IFuture<Void>>(a), Times.Once);
        p2.Verify(x => x.addListener<IFuture<Void>>(a), Times.Once);
        p1.Verify(x => x.cause(), Times.Once);
        p.Verify(x => x.setFailure(cause), Times.Once);
        p2.Verify(x => x.setFailure(cause), Times.Once);
    }

    [Fact]
    public void testFailedFutureNoFailPending()
    {
        var p = new Mock<IPromise<Void>>();
        var a = new PromiseAggregator<Void, IFuture<Void>>(p.Object, false);
        var p1 = new Mock<IPromise<Void>>();
        var p2 = new Mock<IPromise<Void>>();
        var cause = new Exception();
        p1.Setup(x => x.addListener<IFuture<Void>>(a)).Returns(p1.Object);
        p2.Setup(x => x.addListener<IFuture<Void>>(a)).Returns(p2.Object);
        p1.Setup(x => x.isSuccess()).Returns(false);
        p1.Setup(x => x.cause()).Returns(cause);
        p.Setup(x => x.setFailure(cause)).Returns(p.Object);

        a.add(p1.Object, p2.Object);
        a.operationComplete(p1.Object);

        p1.Verify(x => x.addListener<IFuture<Void>>(a), Times.Once);
        p2.Verify(x => x.addListener<IFuture<Void>>(a), Times.Once);
        p1.Verify(x => x.isSuccess(), Times.Once);
        p1.Verify(x => x.cause(), Times.Once);
        p.Verify(x => x.setFailure(cause), Times.Once);
        p2.Verify(x => x.setFailure(It.IsAny<Exception>()), Times.Never);
    }
}
