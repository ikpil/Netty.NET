/*
 * Copyright 2016 The Netty Project
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

public class PromiseCombinerTest
{
    private readonly Mock<IPromise<Void>> p1 = new Mock<IPromise<Void>>();
    private readonly Mock<IPromise<Void>> p2 = new Mock<IPromise<Void>>();
    private readonly Mock<IPromise<Void>> p3 = new Mock<IPromise<Void>>();
    private IGenericFutureListener<IFuture> l1, l2;
    private readonly PromiseCombiner combiner = new PromiseCombiner(ImmediateEventExecutor.INSTANCE);
    [Fact]
    public void testNullArgument()
    {
        Assert.Throws<ArgumentNullException>(() => combiner.finish(null));
        combiner.finish(p1.Object);
        verifySuccess(p1);
    }
    [Fact]
    public void testNullAggregatePromise()
    {
        combiner.finish(p1.Object);
        verifySuccess(p1);
    }
    [Fact]
    public void testAddNullPromise() => Assert.Throws<ArgumentNullException>(() => combiner.add((IFuture)null));
    [Fact]
    public void testAddAllNullPromise() => Assert.Throws<ArgumentNullException>(() => combiner.addAll((IFuture[])null));
    [Fact]
    public void testAddAfterFinish()
    {
        combiner.finish(p1.Object);
        Assert.Throws<InvalidOperationException>(() => combiner.add(p2.Object));
    }
    [Fact]
    public void testAddAllAfterFinish()
    {
        combiner.finish(p1.Object);
        Assert.Throws<InvalidOperationException>(() => combiner.addAll(p2.Object));
    }
    [Fact]
    public void testFinishCalledTwiceThrows()
    {
        combiner.finish(p1.Object);
        Assert.Throws<InvalidOperationException>(() => combiner.finish(p1.Object));
    }
    [Fact]
    public void testAddAllSuccess()
    {
        mockSuccessPromise(p1, listener => l1 = listener);
        mockSuccessPromise(p2, listener => l2 = listener);
        combiner.addAll(p1.Object, p2.Object);
        combiner.finish(p3.Object);
        l1.operationComplete(p1.Object);
        verifyNotCompleted(p3);
        l2.operationComplete(p2.Object);
        verifySuccess(p3);
    }
    [Fact]
    public void testAddSuccess()
    {
        mockSuccessPromise(p1, listener => l1 = listener);
        mockSuccessPromise(p2, listener => l2 = listener);
        combiner.add(p1.Object);
        l1.operationComplete(p1.Object);
        combiner.add(p2.Object);
        l2.operationComplete(p2.Object);
        verifyNotCompleted(p3);
        combiner.finish(p3.Object);
        verifySuccess(p3);
    }
    [Fact]
    public void testAddAllFail()
    {
        var e1 = new Exception("fake exception 1");
        var e2 = new Exception("fake exception 2");
        mockFailedPromise(p1, e1, listener => l1 = listener);
        mockFailedPromise(p2, e2, listener => l2 = listener);
        combiner.addAll(p1.Object, p2.Object);
        combiner.finish(p3.Object);
        l1.operationComplete(p1.Object);
        verifyNotCompleted(p3);
        l2.operationComplete(p2.Object);
        verifyFail(p3, e1);
    }
    [Fact]
    public void testAddFail()
    {
        var e1 = new Exception("fake exception 1");
        var e2 = new Exception("fake exception 2");
        mockFailedPromise(p1, e1, listener => l1 = listener);
        mockFailedPromise(p2, e2, listener => l2 = listener);
        combiner.add(p1.Object);
        l1.operationComplete(p1.Object);
        combiner.add(p2.Object);
        l2.operationComplete(p2.Object);
        verifyNotCompleted(p3);
        combiner.finish(p3.Object);
        verifyFail(p3, e1);
    }
    [Fact]
    public void testEventExecutor()
    {
        var executor = new Mock<IEventExecutor>();
        executor.Setup(e => e.inEventLoop()).Returns(false);
        var other = new PromiseCombiner(executor.Object);
        var future = new Mock<IFuture>();
        Assert.Throws<InvalidOperationException>(() => other.add(future.Object));
        Assert.Throws<InvalidOperationException>(() => other.addAll(future.Object));
        Assert.Throws<InvalidOperationException>(() => other.finish(p1.Object));
    }
    private static void verifyFail(Mock<IPromise<Void>> p, Exception cause) => p.Verify(x => x.tryFailure(cause), Times.Once);
    private static void verifySuccess(Mock<IPromise<Void>> p) => p.Verify(x => x.trySuccess(null), Times.Once);
    private static void verifyNotCompleted(Mock<IPromise<Void>> p)
    {
        p.Verify(x => x.trySuccess(It.IsAny<Void>()), Times.Never);
        p.Verify(x => x.tryFailure(It.IsAny<Exception>()), Times.Never);
        p.Verify(x => x.setSuccess(It.IsAny<Void>()), Times.Never);
        p.Verify(x => x.setFailure(It.IsAny<Exception>()), Times.Never);
    }
    private static void mockSuccessPromise(Mock<IPromise<Void>> p, Action<IGenericFutureListener<IFuture>> consumer)
    {
        p.Setup(x => x.isDone()).Returns(true);
        p.Setup(x => x.isSuccess()).Returns(true);
        mockListener(p, consumer);
    }
    private static void mockFailedPromise(Mock<IPromise<Void>> p, Exception cause, Action<IGenericFutureListener<IFuture>> consumer)
    {
        p.Setup(x => x.isDone()).Returns(true);
        p.Setup(x => x.isSuccess()).Returns(false);
        p.Setup(x => x.cause()).Returns(cause);
        mockListener(p, consumer);
    }
    private static void mockListener(Mock<IPromise<Void>> p, Action<IGenericFutureListener<IFuture>> consumer)
    {
        // CLR adaptation: the non-generic bridge models Java's erased Future<?> observer.
        p.As<IFuture>().Setup(x => x.addListener(It.IsAny<IGenericFutureListener<IFuture>>())).Callback(consumer).Returns(p.Object);
    }
}
