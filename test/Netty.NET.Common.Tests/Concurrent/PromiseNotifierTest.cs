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

public class PromiseNotifierTest
{
    [Fact]
    public void testNullPromisesArray() => Assert.Throws<ArgumentNullException>(() => new PromiseNotifier<Void, IFuture<Void>>((IPromise<Void>[])null));
    [Fact]
    public void testNullPromiseInArray() => Assert.Throws<ArgumentException>(() => new PromiseNotifier<Void, IFuture<Void>>((IPromise<Void>)null));
    [Fact]
    public void testListenerSuccess()
    {
        var p1 = new Mock<IPromise<Void>>();
        var p2 = new Mock<IPromise<Void>>();
        var notifier = new PromiseNotifier<Void, IFuture<Void>>(p1.Object, p2.Object);
        var future = new Mock<IFuture<Void>>();
        future.Setup(x => x.isSuccess()).Returns(true);
        future.Setup(x => x.get()).Returns((Void)null);
        p1.Setup(x => x.trySuccess(null)).Returns(true);
        p2.Setup(x => x.trySuccess(null)).Returns(true);
        notifier.operationComplete(future.Object);
        p1.Verify(x => x.trySuccess(null), Times.Once);
        p2.Verify(x => x.trySuccess(null), Times.Once);
    }
    [Fact]
    public void testListenerFailure()
    {
        var p1 = new Mock<IPromise<Void>>();
        var p2 = new Mock<IPromise<Void>>();
        var notifier = new PromiseNotifier<Void, IFuture<Void>>(p1.Object, p2.Object);
        var future = new Mock<IFuture<Void>>();
        var cause = new Exception();
        future.Setup(x => x.isSuccess()).Returns(false);
        future.Setup(x => x.isCancelled()).Returns(false);
        future.Setup(x => x.cause()).Returns(cause);
        p1.Setup(x => x.tryFailure(cause)).Returns(true);
        p2.Setup(x => x.tryFailure(cause)).Returns(true);
        notifier.operationComplete(future.Object);
        p1.Verify(x => x.tryFailure(cause), Times.Once);
        p2.Verify(x => x.tryFailure(cause), Times.Once);
    }
    [Fact]
    public void testCancelPropagationWhenFusedFromFuture()
    {
        var p1 = ImmediateEventExecutor.INSTANCE.newPromise<Void>();
        var p2 = ImmediateEventExecutor.INSTANCE.newPromise<Void>();
        var returned = PromiseNotifier<Void, IPromise<Void>>.cascade(p1, p2);
        Assert.Same(p1, returned);
        Assert.True(returned.cancel(false));
        Assert.True(returned.isCancelled());
        Assert.True(p2.isCancelled());
    }
}
