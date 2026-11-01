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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Original operation deadlines and independently completed fixture cleanup.
public class DefaultChannelPipelineMutationSourceTest
{
    [Fact]
    public void TestRemoveChannelHandler()
    {
        var pipeline = new LocalChannel().Pipeline;
        var handler1 = NewHandler();
        var handler2 = NewHandler();
        var handler3 = NewHandler();
        pipeline.AddLast("handler1", handler1).AddLast("handler2", handler2).AddLast("handler3", handler3);
        Assert.Same(handler1, pipeline.Get("handler1"));
        Assert.Same(handler2, pipeline.Get("handler2"));
        Assert.Same(handler3, pipeline.Get("handler3"));
        pipeline.Remove(handler1);
        Assert.Null(pipeline.Get("handler1"));
        pipeline.Remove(handler2);
        Assert.Null(pipeline.Get("handler2"));
        pipeline.Remove(handler3);
        Assert.Null(pipeline.Get("handler3"));
    }
    [Fact]
    public void TestRemoveIfExists()
    {
        var pipeline = new DefaultChannelPipeline(new LocalChannel());
        var handler1 = NewHandler();
        var handler2 = NewHandler();
        var handler3 = NewHandler();
        pipeline.AddLast("handler1", handler1).AddLast("handler2", handler2).AddLast("handler3", handler3);
        Assert.NotNull(pipeline.RemoveIfExists(handler1));
        Assert.Null(pipeline.Get("handler1"));
        Assert.NotNull(pipeline.RemoveIfExists("handler2"));
        Assert.Null(pipeline.Get("handler2"));
        Assert.NotNull(pipeline.RemoveIfExists(typeof(TestHandler)));
        Assert.Null(pipeline.Get("handler3"));
    }
    [Fact]
    public void TestRemoveIfExistsDoesNotThrowException()
    {
        var pipeline = new DefaultChannelPipeline(new LocalChannel());
        var handler1 = NewHandler();
        var handler2 = NewHandler();
        pipeline.AddLast("handler1", handler1);
        Assert.Null(pipeline.RemoveIfExists("handlerXXX"));
        Assert.Null(pipeline.RemoveIfExists(handler2));
        Assert.Null(pipeline.RemoveIfExists(typeof(ChannelOutboundHandlerAdapter)));
        Assert.NotNull(pipeline.Get("handler1"));
    }
    [Fact]
    public void TestRemoveThrowNoSuchElementException()
    {
        var pipeline = new DefaultChannelPipeline(new LocalChannel());
        pipeline.AddLast("handler1", NewHandler());
        Assert.Throws<KeyNotFoundException>(() => pipeline.Remove("handlerXXX"));
    }
    [Fact]
    public void TestReplaceChannelHandler()
    {
        var pipeline = new LocalChannel().Pipeline;
        var handler1 = NewHandler();
        pipeline.AddLast("handler1", handler1).AddLast("handler2", handler1).AddLast("handler3", handler1);
        Assert.Same(handler1, pipeline.Get("handler1"));
        Assert.Same(handler1, pipeline.Get("handler2"));
        Assert.Same(handler1, pipeline.Get("handler3"));
        var newHandler1 = NewHandler();
        pipeline.Replace("handler1", "handler1", newHandler1);
        Assert.Same(newHandler1, pipeline.Get("handler1"));
        var newHandler3 = NewHandler();
        pipeline.Replace("handler3", "handler3", newHandler3);
        Assert.Same(newHandler3, pipeline.Get("handler3"));
        var newHandler2 = NewHandler();
        pipeline.Replace("handler2", "handler2", newHandler2);
        Assert.Same(newHandler2, pipeline.Get("handler2"));
    }
    [Fact]
    public void TestReplaceHandlerChecksDuplicateNames()
    {
        var pipeline = new LocalChannel().Pipeline;
        pipeline.AddLast("handler1", NewHandler()).AddLast("handler2", NewHandler());
        Assert.Throws<ArgumentException>(() => pipeline.Replace("handler1", "handler2", NewHandler()));
    }
    [Fact]
    public void TestReplaceNameWithGenerated()
    {
        var pipeline = new LocalChannel().Pipeline;
        var handler = NewHandler();
        pipeline.AddLast("handler1", handler);
        Assert.Same(handler, pipeline.Get("handler1"));
        var replacement = NewHandler();
        pipeline.Replace("handler1", null, replacement);
        // CLR nested type names do not use the JVM's Outer$Inner spelling.
        Assert.Same(replacement, pipeline.Get("TestHandler#0"));
        Assert.Null(pipeline.Get("handler1"));
    }
    [Fact]
    public void TestRenameChannelHandler()
    {
        var pipeline = new LocalChannel().Pipeline;
        var handler1 = NewHandler();
        pipeline.AddLast("handler1", handler1).AddLast("handler2", handler1).AddLast("handler3", handler1);
        Assert.Same(handler1, pipeline.Get("handler1"));
        Assert.Same(handler1, pipeline.Get("handler2"));
        Assert.Same(handler1, pipeline.Get("handler3"));
        var newHandler1 = NewHandler();
        pipeline.Replace("handler1", "newHandler1", newHandler1);
        Assert.Same(newHandler1, pipeline.Get("newHandler1"));
        Assert.Null(pipeline.Get("handler1"));
        var newHandler3 = NewHandler();
        pipeline.Replace("handler3", "newHandler3", newHandler3);
        Assert.Same(newHandler3, pipeline.Get("newHandler3"));
        Assert.Null(pipeline.Get("handler3"));
        var newHandler2 = NewHandler();
        pipeline.Replace("handler2", "newHandler2", newHandler2);
        Assert.Same(newHandler2, pipeline.Get("newHandler2"));
        Assert.Null(pipeline.Get("handler2"));
    }
    [Fact]
    public void TestChannelHandlerContextNavigation()
    {
        var pipeline = new LocalChannel().Pipeline;
        const int HANDLER_ARRAY_LEN = 5;
        var firstHandlers = NewHandlers(HANDLER_ARRAY_LEN);
        var lastHandlers = NewHandlers(HANDLER_ARRAY_LEN);
        pipeline.AddFirst(firstHandlers);
        pipeline.AddLast(lastHandlers);
        VerifyContextNumber(pipeline, HANDLER_ARRAY_LEN * 2);
    }
    [Fact]
    public void TestPipelineOperation()
    {
        var pipeline = new LocalChannel().Pipeline;
        const int handlerNum = 5;
        var handlers1 = NewHandlers(handlerNum);
        var handlers2 = NewHandlers(handlerNum);
        const string prefixX = "x";
        for (int i = 0; i < handlerNum; i++)
            if (i % 2 == 0)
                pipeline.AddFirst(prefixX + i, handlers1[i]);
            else
                pipeline.AddLast(prefixX + i, handlers1[i]);
        for (int i = 0; i < handlerNum; i++)
            if (i % 2 != 0)
                pipeline.AddBefore(prefixX + i, i.ToString(), handlers2[i]);
            else
                pipeline.AddAfter(prefixX + i, i.ToString(), handlers2[i]);
        VerifyContextNumber(pipeline, handlerNum * 2);
    }
    [Fact]
    public void TestChannelHandlerContextOrder()
    {
        var pipeline = new LocalChannel().Pipeline;
        pipeline.AddFirst("1", NewHandler()).AddLast("10", NewHandler());
        pipeline.AddBefore("10", "5", NewHandler()).AddAfter("1", "3", NewHandler());
        pipeline.AddBefore("5", "4", NewHandler()).AddAfter("5", "6", NewHandler());
        pipeline.AddBefore("1", "0", NewHandler()).AddAfter("10", "11", NewHandler());
        var context = pipeline.FirstContext;
        Assert.NotNull(context);
        while (context != null)
        {
            int i = ToInt(context.Name), j = Next(context);
            if (j != -1)
                Assert.True(i < j);
            else
                Assert.Null(context.Next.Next);
            context = context.Next;
        }
        VerifyContextNumber(pipeline, 8);
    }
    [Fact]
    public void TestFirstContextEmptyPipeline() => Assert.Null(new LocalChannel().Pipeline.FirstContext);
    [Fact]
    public void TestLastContextEmptyPipeline() => Assert.Null(new LocalChannel().Pipeline.LastContext);
    [Fact]
    public void TestFirstHandlerEmptyPipeline() => Assert.Null(new LocalChannel().Pipeline.First);
    [Fact]
    public void TestLastHandlerEmptyPipeline() => Assert.Null(new LocalChannel().Pipeline.Last);
    [Fact]
    public void TestNullName()
    {
        var pipeline = new LocalChannel().Pipeline;
        pipeline.AddLast(NewHandler());
        pipeline.AddLast((string)null, NewHandler());
        pipeline.AddFirst(NewHandler());
        pipeline.AddFirst((string)null, NewHandler());
        pipeline.AddLast("test", NewHandler());
        pipeline.AddAfter("test", null, NewHandler());
        pipeline.AddBefore("test", null, NewHandler());
    }
    [Fact]
    public async Task TestPinExecutor()
    {
        var group = new DefaultEventLoopGroup();
        try
        {
            var pipeline = new LocalChannel().Pipeline;
            var pipeline2 = new LocalChannel().Pipeline;
            pipeline.AddLast(group, "h1", new ChannelInboundHandlerAdapter()).AddLast(group, "h2", new ChannelInboundHandlerAdapter());
            pipeline2.AddLast(group, "h3", new ChannelInboundHandlerAdapter());
            var executor1 = pipeline.Context("h1").Executor;
            var executor2 = pipeline.Context("h2").Executor;
            Assert.NotNull(executor1);
            Assert.NotNull(executor2);
            Assert.Same(executor1, executor2);
            var executor3 = pipeline2.Context("h3").Executor;
            Assert.NotNull(executor3);
            Assert.NotSame(executor3, executor2);
        }
        finally { await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero); }
    }
    [Fact]
    public async Task TestNotPinExecutor()
    {
        var group = new DefaultEventLoopGroup(2);
        try
        {
            var pipeline = new LocalChannel().Pipeline;
            pipeline.Channel.Config.SetOption(ChannelOption.SINGLE_EVENTEXECUTOR_PER_GROUP, false);
            pipeline.AddLast(group, "h1", new ChannelInboundHandlerAdapter()).AddLast(group, "h2", new ChannelInboundHandlerAdapter());
            var executor1 = pipeline.Context("h1").Executor;
            var executor2 = pipeline.Context("h2").Executor;
            Assert.NotNull(executor1);
            Assert.NotNull(executor2);
            Assert.NotSame(executor1, executor2);
        }
        finally { await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero); }
    }
    [Fact(Timeout = 10000)]
    public async Task TestLifeCycleAwareness()
    {
        await using var connection = await Connection.SetUp();
        var p = connection.Self.Pipeline;
        var handlers = new List<LifeCycleAwareTestHandler>();
        const int COUNT = 20;
        var adds = new List<Task>();
        for (int i = 0; i < COUNT; i++)
        {
            var handler = new LifeCycleAwareTestHandler("handler-" + i);
            // Add handler.
            p.AddFirst(handler.Name, handler);
            adds.Add(connection.Self.EventLoop.SubmitAsync(() =>
            {
                // Validate handler life-cycle methods called.
                handler.Validate(true, false);
                // Store handler into the list.
                handlers.Add(handler);
            }));
        }
        await Task.WhenAll(adds).WaitAsync(TestContext.Current.CancellationToken);
        // Change the order of remove operations over all handlers in the pipeline.
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(handlers));
        var removes = new List<Task>();
        foreach (var handler in handlers)
        {
            Assert.Same(handler, p.Remove(handler.Name));
            removes.Add(connection.Self.EventLoop.SubmitAsync(() =>
            {
                // Validate handler life-cycle methods called.
                handler.Validate(true, true);
            }));
        }
        await Task.WhenAll(removes);
    }
    [Fact(Timeout = 10000)]
    public async Task TestRemoveAndForwardInbound()
    {
        var handler1 = new BufferedTestHandler();
        var handler2 = new BufferedTestHandler();
        await using var connection = await Connection.SetUp(handler1, handler2);
        await connection.Self.EventLoop.SubmitAsync(() =>
        {
            handler1.InboundBuffer.Enqueue(8);
            Assert.Equal(8, handler1.InboundBuffer.Peek());
            Assert.Empty(handler2.InboundBuffer);
            connection.Self.Pipeline.Remove(handler1);
            Assert.Single(handler2.InboundBuffer);
            Assert.Equal(8, handler2.InboundBuffer.Peek());
        }).WaitAsync(TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 10000)]
    public async Task TestRemoveAndForwardOutbound()
    {
        var handler1 = new BufferedTestHandler();
        var handler2 = new BufferedTestHandler();
        await using var connection = await Connection.SetUp(handler1, handler2);
        await connection.Self.EventLoop.SubmitAsync(() =>
        {
            handler2.OutboundBuffer.Enqueue(8);
            Assert.Equal(8, handler2.OutboundBuffer.Peek());
            Assert.Empty(handler1.OutboundBuffer);
            connection.Self.Pipeline.Remove(handler2);
            Assert.Single(handler1.OutboundBuffer);
            Assert.Equal(8, handler1.OutboundBuffer.Peek());
        }).WaitAsync(TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 10000)]
    public async Task TestReplaceAndForwardOutbound()
    {
        var handler1 = new BufferedTestHandler();
        var handler2 = new BufferedTestHandler();
        await using var connection = await Connection.SetUp(handler1);
        await connection.Self.EventLoop.SubmitAsync(() =>
        {
            handler1.OutboundBuffer.Enqueue(8);
            Assert.Equal(8, handler1.OutboundBuffer.Peek());
            Assert.Empty(handler2.OutboundBuffer);
            connection.Self.Pipeline.Replace(handler1, "handler2", handler2);
            Assert.Equal(8, handler2.OutboundBuffer.Peek());
        }).WaitAsync(TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 10000)]
    public async Task TestReplaceAndForwardInboundAndOutbound()
    {
        var handler1 = new BufferedTestHandler();
        var handler2 = new BufferedTestHandler();
        await using var connection = await Connection.SetUp(handler1);
        await connection.Self.EventLoop.SubmitAsync(() =>
        {
            handler1.InboundBuffer.Enqueue(8);
            handler1.OutboundBuffer.Enqueue(8);
            Assert.Equal(8, handler1.InboundBuffer.Peek());
            Assert.Equal(8, handler1.OutboundBuffer.Peek());
            Assert.Empty(handler2.InboundBuffer);
            Assert.Empty(handler2.OutboundBuffer);
            connection.Self.Pipeline.Replace(handler1, "handler2", handler2);
            Assert.Equal(8, handler2.OutboundBuffer.Peek());
            Assert.Equal(8, handler2.InboundBuffer.Peek());
        }).WaitAsync(TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 10000)]
    public async Task TestRemoveAndForwardInboundOutbound()
    {
        var handler1 = new BufferedTestHandler();
        var handler2 = new BufferedTestHandler();
        var handler3 = new BufferedTestHandler();
        await using var connection = await Connection.SetUp(handler1, handler2, handler3);
        await connection.Self.EventLoop.SubmitAsync(() =>
        {
            handler2.InboundBuffer.Enqueue(8);
            handler2.OutboundBuffer.Enqueue(8);
            Assert.Equal(8, handler2.InboundBuffer.Peek());
            Assert.Equal(8, handler2.OutboundBuffer.Peek());
            Assert.Empty(handler1.OutboundBuffer);
            Assert.Empty(handler3.InboundBuffer);
            connection.Self.Pipeline.Remove(handler2);
            Assert.Equal(8, handler3.InboundBuffer.Peek());
            Assert.Equal(8, handler1.OutboundBuffer.Peek());
        }).WaitAsync(TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 3000)]
    public async Task TestAddHandlerBeforeRegisteredThenRemove()
    {
        var group = new DefaultEventLoopGroup(1);
        var channel = new LocalChannel();
        try
        {
            var loop = group.Next();
            var handler = new CheckEventExecutorHandler(loop);
            channel.Pipeline.AddFirst(handler);
            Assert.False(handler.Added.Task.IsCompleted);
            await channel.RegisterAsync(loop);
            await handler.Added.Task.WaitAsync(TestContext.Current.CancellationToken);
            channel.Pipeline.Remove(handler);
            await handler.Removed.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Fact(Timeout = 3000)]
    public async Task TestAddHandlerBeforeRegisteredThenReplace()
    {
        var group = new DefaultEventLoopGroup(1);
        var channel = new LocalChannel();
        try
        {
            var loop = group.Next();
            var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new CheckEventExecutorHandler(loop);
            channel.Pipeline.AddFirst(handler);
            Assert.False(handler.Added.Task.IsCompleted);
            await channel.RegisterAsync(loop);
            await handler.Added.Task.WaitAsync(TestContext.Current.CancellationToken);
            channel.Pipeline.Replace(handler, null, new AddedSignal(latch));
            await handler.Removed.Task.WaitAsync(TestContext.Current.CancellationToken);
            await latch.Task;
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Fact]
    public void TestAddRemoveHandlerNotRegistered()
    {
        Exception error = null;
        var handler = new ErrorChannelHandler(value => error = value);
        var pipeline = new LocalChannel().Pipeline;
        pipeline.AddFirst(handler);
        pipeline.Remove(handler);
        if (error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact]
    public void TestAddReplaceHandlerNotRegistered()
    {
        Exception error = null;
        var handler = new ErrorChannelHandler(value => error = value);
        var pipeline = new LocalChannel().Pipeline;
        pipeline.AddFirst(handler);
        pipeline.Replace(handler, null, new ErrorChannelHandler(value => error = value));
        if (error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact(Timeout = 3000)]
    public async Task TestAddRemoveHandlerCalledOnceRegistered()
    {
        var group = new DefaultEventLoopGroup(1);
        var channel = new LocalChannel();
        try
        {
            var handler = new CallbackCheckHandler();
            var pipeline = channel.Pipeline;
            pipeline.AddFirst(handler);
            pipeline.Remove(handler);
            Assert.False(handler.Added.Task.IsCompleted);
            Assert.False(handler.Removed.Task.IsCompleted);
            await channel.RegisterAsync(group.Next(), TestContext.Current.CancellationToken);
            handler.CheckError();
            Assert.True(await handler.Added.Task);
            Assert.True(await handler.Removed.Task);
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Fact(Timeout = 3000)]
    public async Task TestAddReplaceHandlerCalledOnceRegistered()
    {
        var group = new DefaultEventLoopGroup(1);
        var channel = new LocalChannel();
        try
        {
            var handler = new CallbackCheckHandler();
            var handler2 = new CallbackCheckHandler();
            var pipeline = channel.Pipeline;
            pipeline.AddFirst(handler);
            pipeline.Replace(handler, null, handler2);
            Assert.False(handler.Added.Task.IsCompleted);
            Assert.False(handler.Removed.Task.IsCompleted);
            Assert.False(handler2.Added.Task.IsCompleted);
            Assert.False(handler2.Removed.Task.IsCompleted);
            await channel.RegisterAsync(group.Next(), TestContext.Current.CancellationToken);
            handler.CheckError();
            Assert.True(await handler.Added.Task);
            Assert.True(await handler.Removed.Task);
            handler2.CheckError();
            Assert.True(await handler2.Added.Task);
            Assert.False(handler2.Removed.Task.IsCompleted);
            pipeline.Remove(handler2);
            Assert.True(await handler2.Removed.Task);
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Fact(Timeout = 3000)]
    public async Task TestHandlerAddedAndRemovedCalledInCorrectOrder()
    {
        var group = new DefaultEventLoopGroup(1);
        var group1 = new DefaultEventLoopGroup(1);
        var group2 = new DefaultEventLoopGroup(1);
        var channel = new LocalChannel();
        try
        {
            var added = Channel.CreateUnbounded<CheckOrderHandler>();
            var removed = Channel.CreateUnbounded<CheckOrderHandler>();
            var handler1 = new CheckOrderHandler(added, removed);
            var handler2 = new CheckOrderHandler(added, removed);
            var handler3 = new CheckOrderHandler(added, removed);
            var handler4 = new CheckOrderHandler(added, removed);
            var pipeline = channel.Pipeline;
            pipeline.AddLast(handler1);
            await channel.RegisterAsync(group.Next(), TestContext.Current.CancellationToken);
            pipeline.AddLast(group1, null, handler2).AddLast(group2, null, handler3).AddLast(handler4);
            Assert.False(removed.Reader.TryPeek(out _));
            await channel.CloseAsync();
            AssertHandler(await added.Reader.ReadAsync(TestContext.Current.CancellationToken), handler1);
            // Depending on timing this can be handler2 or handler3 as these use different EventExecutorGroups.
            AssertHandler(await added.Reader.ReadAsync(TestContext.Current.CancellationToken), handler2, handler3, handler4);
            AssertHandler(await added.Reader.ReadAsync(TestContext.Current.CancellationToken), handler2, handler3, handler4);
            AssertHandler(await added.Reader.ReadAsync(TestContext.Current.CancellationToken), handler2, handler3, handler4);
            Assert.False(added.Reader.TryPeek(out _));
            AssertHandler(await removed.Reader.ReadAsync(TestContext.Current.CancellationToken), handler4);
            AssertHandler(await removed.Reader.ReadAsync(TestContext.Current.CancellationToken), handler3);
            AssertHandler(await removed.Reader.ReadAsync(TestContext.Current.CancellationToken), handler2);
            AssertHandler(await removed.Reader.ReadAsync(TestContext.Current.CancellationToken), handler1);
            Assert.False(removed.Reader.TryPeek(out _));
        }
        finally { await channel.CloseAsync(); await Task.WhenAll(group.StopAsync(), group1.StopAsync(), group2.StopAsync()); }
    }
    [Fact(Timeout = 3000)]
    public async Task TestAddBefore()
    {
        var channel1 = new LocalChannel();
        var channel2 = new LocalChannel();
        var defaultGroup = new DefaultEventLoopGroup(2);
        try
        {
            var loop1 = defaultGroup.Next();
            var loop2 = defaultGroup.Next();
            await channel1.RegisterAsync(loop1, TestContext.Current.CancellationToken);
            await channel2.RegisterAsync(loop2);
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(loop1.SubmitAsync(() => channel2.Pipeline.AddLast(new ChannelInboundHandlerAdapter())));
                tasks.Add(loop2.SubmitAsync(() => channel1.Pipeline.AddLast(new ChannelInboundHandlerAdapter())));
            }
            await Task.WhenAll(tasks).WaitAsync(TestContext.Current.CancellationToken);
        }
        finally { await channel1.CloseAsync(); await channel2.CloseAsync(); await defaultGroup.StopAsync(); }
    }
    private static void VerifyContextNumber(DefaultChannelPipeline pipeline, int expectedNumber)
    {
        var context = pipeline.FirstContext;
        int number = 0;
        while (context != pipeline.Tail)
        { number++; context = context.Next; }
        Assert.Equal(expectedNumber, number);
    }
    private static int Next(ChannelHandlerContext context) => context.Next == null ? int.MaxValue : ToInt(context.Next.Name);
    private static int ToInt(string name) => int.TryParse(name, out int value) ? value : -1;
    private static IChannelHandler[] NewHandlers(int number)
    { Assert.True(number > 0); return Enumerable.Range(0, number).Select(_ => (IChannelHandler)NewHandler()).ToArray(); }
    private static TestHandler NewHandler() => new();
    [Sharable]
    private sealed class TestHandler : ChannelDuplexHandler { }
    private sealed class BufferedTestHandler : ChannelDuplexHandler
    {
        internal readonly Queue<object> InboundBuffer = new();
        internal readonly Queue<object> OutboundBuffer = new();
        // The original buffered write stores only the message and deliberately leaves its completion pending.
        public override Task WriteAsync(ChannelHandlerContext context, object message, CancellationToken token)
        { OutboundBuffer.Enqueue(message); return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task; }
        public override void ChannelRead(ChannelHandlerContext context, object message) => InboundBuffer.Enqueue(message);
        public override void HandlerRemoved(ChannelHandlerContext context)
        {
            if (InboundBuffer.Count != 0)
            { foreach (var value in InboundBuffer) context.FireChannelRead(value); context.FireChannelReadComplete(); }
            if (OutboundBuffer.Count != 0)
            { foreach (var value in OutboundBuffer) _ = context.WriteAsync(value); context.Flush(); }
        }
    }
    /** Test handler to validate life-cycle aware behavior. */
    private sealed class LifeCycleAwareTestHandler : ChannelHandlerAdapter
    {
        internal readonly string Name;
        private bool _afterAdd, _afterRemove;
        /**
         * Constructs life-cycle aware test handler.
         *
         * @param name Handler name to display in assertion messages.
         */
        internal LifeCycleAwareTestHandler(string name) => Name = name;
        internal void Validate(bool afterAdd, bool afterRemove)
        { Assert.True(afterAdd == _afterAdd, Name); Assert.True(afterRemove == _afterRemove, Name); }
        public override void HandlerAdded(ChannelHandlerContext context) { Validate(false, false); _afterAdd = true; }
        public override void HandlerRemoved(ChannelHandlerContext context) { Validate(true, false); _afterRemove = true; }
    }
    private sealed class CheckEventExecutorHandler(IEventExecutor executor) : ChannelHandlerAdapter
    {
        internal readonly TaskCompletionSource Added = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void HandlerAdded(ChannelHandlerContext context) => AssertExecutor(context, Added);
        public override void HandlerRemoved(ChannelHandlerContext context) => AssertExecutor(context, Removed);
        private void AssertExecutor(ChannelHandlerContext context, TaskCompletionSource promise)
        { if (ReferenceEquals(executor, context.Executor)) promise.SetResult(); else promise.SetException(new InvalidOperationException("EventExecutor not the same")); }
    }
    private sealed class AddedSignal(TaskCompletionSource signal) : ChannelHandlerAdapter
    { public override void HandlerAdded(ChannelHandlerContext context) => signal.SetResult(); }
    private sealed class ErrorChannelHandler(Action<Exception> error) : ChannelHandlerAdapter
    {
        public override void HandlerAdded(ChannelHandlerContext context) => error(new InvalidOperationException());
        public override void HandlerRemoved(ChannelHandlerContext context) => error(new InvalidOperationException());
    }
    private sealed class CallbackCheckHandler : ChannelHandlerAdapter
    {
        internal readonly TaskCompletionSource<bool> Added = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception _error;
        public override void HandlerAdded(ChannelHandlerContext context)
        {
            if (!Added.TrySetResult(true))
                _error = new InvalidOperationException("handlerAdded(...) called multiple times: " + context.Name);
            else if (Removed.Task.IsCompletedSuccessfully && Removed.Task.Result)
                _error = new InvalidOperationException("handlerRemoved(...) called before handlerAdded(...): " + context.Name);
        }
        public override void HandlerRemoved(ChannelHandlerContext context)
        {
            if (!Removed.TrySetResult(true))
                _error = new InvalidOperationException("handlerRemoved(...) called multiple times: " + context.Name);
            else if (Added.Task.IsCompletedSuccessfully && !Added.Task.Result)
                _error = new InvalidOperationException("handlerRemoved(...) called before handlerAdded(...): " + context.Name);
        }
        internal void CheckError() { if (_error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_error).Throw(); }
    }
    private sealed class CheckOrderHandler(Channel<CheckOrderHandler> added, Channel<CheckOrderHandler> removed) : ChannelHandlerAdapter
    {
        private Exception _error;
        public override void HandlerAdded(ChannelHandlerContext context) { added.Writer.TryWrite(this); CheckExecutor(context); }
        public override void HandlerRemoved(ChannelHandlerContext context) { removed.Writer.TryWrite(this); CheckExecutor(context); }
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause) => _error = cause;
        private void CheckExecutor(ChannelHandlerContext context) { if (!context.Executor.InEventLoop()) _error = new InvalidOperationException(); }
        internal void CheckError() { if (_error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_error).Throw(); }
    }
    private static void AssertHandler(CheckOrderHandler actual, params CheckOrderHandler[] handlers)
    { Assert.Contains(actual, handlers); actual.CheckError(); }
    private sealed class Connection : IAsyncDisposable
    {
        private readonly DefaultEventLoopGroup _group = new(1);
        private IChannel _server;
        internal IChannel Self, Peer;
        internal static async Task<Connection> SetUp(params IChannelHandler[] handlers)
        {
            var connection = new Connection();
            try
            {
                var peer = new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
                connection._server = await new ServerBootstrap().Group(connection._group).Channel<LocalServerChannel>()
                    .ChildHandler(new PeerRecorder(peer)).BindAsync(LocalAddress.ANY);
                connection.Self = await new Bootstrap().Group(connection._group).Channel<LocalChannel>()
                    .Handler(new ActionChannelInitializer<LocalChannel>(ch => ch.Pipeline.AddLast(handlers))).ConnectAsync(connection._server.LocalAddress);
                connection.Peer = await peer.Task;
                await connection._server.CloseAsync();
                return connection;
            }
            catch { await connection.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            if (Peer != null)
                await Peer.CloseAsync();
            if (Self != null)
                await Self.CloseAsync();
            if (_server != null)
                await _server.CloseAsync();
            await _group.StopAsync();
        }
    }
    private sealed class PeerRecorder(TaskCompletionSource<IChannel> peer) : ChannelInboundHandlerAdapter
    {
        public override void ChannelRegistered(ChannelHandlerContext context) => peer.TrySetResult(context.Channel);
        public override void ChannelRead(ChannelHandlerContext context, object message) => ReferenceCountUtil.Release(message);
    }
}
