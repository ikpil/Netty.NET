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
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Transport.Tests;

public class ChannelPipelineSourceTest
{
    [Fact]
    public void TestHandlerAddedReplacesItselfAndThrows()
    {
        var replacement = new LifecycleCountingHandler();
        var error = new InvalidOperationException();
        var boot = new SelfReplacingHandler(replacement, error);
        var channel = new EmbeddedChannel();
        channel.Pipeline.AddLast("first", new ChannelInboundHandlerAdapter());
        channel.Pipeline.AddLast("boot", boot);
        Assert.Same(error, Assert.Throws<ChannelPipelineException>(channel.CheckException).InnerException);
        AssertPipelineLinked(channel.Pipeline, "first", "real");
        Assert.Equal(1, boot.Removed);
        Assert.Equal(1, replacement.Added);
        Assert.Equal(0, replacement.Removed);
        object message = new object();
        Assert.True(channel.WriteInbound(message));
        Assert.Equal(1, replacement.Read);
        Assert.Same(message, channel.ReadInbound());
        Assert.False(channel.Finish());
        Assert.Equal(1, boot.Removed);
        Assert.Equal(1, replacement.Removed);
    }
    [Fact]
    public void TestHandlerAddedRemovesItselfAndThrows()
    {
        var error = new InvalidOperationException();
        var boot = new SelfRemovingHandler(error);
        var channel = new EmbeddedChannel();
        channel.Pipeline.AddLast("first", new ChannelInboundHandlerAdapter());
        channel.Pipeline.AddLast("boot", boot);
        channel.Pipeline.AddLast("last", new ChannelInboundHandlerAdapter());
        Assert.Same(error, Assert.Throws<ChannelPipelineException>(channel.CheckException).InnerException);
        AssertPipelineLinked(channel.Pipeline, "first", "last");
        Assert.Equal(1, boot.Removed);
        object message = new object();
        Assert.True(channel.WriteInbound(message));
        Assert.Same(message, channel.ReadInbound());
        Assert.False(channel.Finish());
        Assert.Equal(1, boot.Removed);
    }
    [Fact]
    public void TestHandlerReplacedBeforeRegistrationAndHandlerAddedThrows()
    {
        var replacement = new LifecycleCountingHandler();
        var error = new InvalidOperationException();
        var handler = new DeferredThrowingHandler(error);
        var channel = new EmbeddedChannel(false, false);
        channel.Pipeline.AddLast("handler", handler);
        channel.Pipeline.Replace("handler", "replacement", replacement);
        // The deferred handlerAdded(...) of the replaced handler throws once the channel is registered.
        channel.Register();
        Assert.Same(error, Assert.Throws<ChannelPipelineException>(channel.CheckException).InnerException);
        AssertPipelineLinked(channel.Pipeline, "replacement");
        Assert.Equal(1, handler.Removed);
        Assert.Equal(1, replacement.Added);
        object message = new object();
        Assert.True(channel.WriteInbound(message));
        Assert.Equal(1, replacement.Read);
        Assert.Same(message, channel.ReadInbound());
        Assert.False(channel.Finish());
        Assert.Equal(1, handler.Removed);
        Assert.Equal(1, replacement.Removed);
    }
    [Fact]
    public void TestCloseWhenHandlerRemovedReplacesOtherHandler()
    {
        var handler = new LifecycleCountingHandler();
        var replacement = new LifecycleCountingHandler();
        var channel = new EmbeddedChannel();
        channel.Pipeline.AddLast("handler", handler);
        channel.Pipeline.AddLast("replacing", new ReplaceOnRemoval(replacement));
        Assert.True(channel.CloseAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);
        AssertPipelineLinked(channel.Pipeline);
        Assert.Equal(1, handler.Added);
        Assert.Equal(1, handler.Removed);
        Assert.Equal(1, replacement.Added);
        Assert.Equal(1, replacement.Removed);
    }
    [Fact]
    public void TestRemoveOfHandlerReplacedConcurrently()
    {
        // remove(...) and replace(...) look up the context before they take the pipeline lock, so run them
        // concurrently many times to hit the window in which one of them works on a context that the other one has
        // already unlinked.
        for (int i = 0; i < 2000; i++)
        {
            var handler = new LifecycleCountingHandler();
            var replacement = new LifecycleCountingHandler();
            var channel = new EmbeddedChannel();
            var pipeline = channel.Pipeline;
            pipeline.AddLast("before", new ChannelInboundHandlerAdapter());
            pipeline.AddLast("handler", handler);
            pipeline.AddLast("after", new ChannelInboundHandlerAdapter());
            int ready = 0;
            Exception removeResult = null;
            bool removed = false;
            var remover = new Thread(() =>
            {
                AwaitOtherThread(ref ready);
                try
                { pipeline.Remove(handler); removed = true; }
                catch (Exception cause) { removeResult = cause; }
            });
            remover.Start();
            AwaitOtherThread(ref ready);
            Exception replaceResult = null;
            try
            { pipeline.Replace(handler, "replacement", replacement); }
            catch (Exception cause) { replaceResult = cause; }
            remover.Join();
            // Exactly one of remove(...) and replace(...) wins, the other one fails as if the handler was never
            // part of the pipeline.
            if (removed)
            {
                Assert.IsType<KeyNotFoundException>(replaceResult);
                AssertPipelineLinked(pipeline, "before", "after");
                Assert.Equal(0, replacement.Added);
            }
            else
            {
                Assert.IsType<KeyNotFoundException>(removeResult);
                Assert.Null(replaceResult);
                AssertPipelineLinked(pipeline, "before", "replacement", "after");
                Assert.Equal(1, replacement.Added);
            }
            Assert.Equal(1, handler.Removed);
            Assert.Equal(0, replacement.Removed);
            Assert.False(channel.Finish());
            Assert.Equal(1, handler.Removed);
            Assert.Equal(removed ? 0 : 1, replacement.Removed);
        }
    }
    private static void AwaitOtherThread(ref int ready)
    {
        // Spin instead of blocking so both threads start at (nearly) the same time.
        Interlocked.Increment(ref ready);
        while (Volatile.Read(ref ready) < 2)
            Thread.Yield();
    }
    private static void AssertPipelineLinked(DefaultChannelPipeline pipeline, params string[] expectedNames)
    {
        var names = new List<string>();
        var context = pipeline.Head;
        while (context != pipeline.Tail)
        {
            var next = context.Next;
            Assert.NotSame(context, next);
            Assert.Same(context, next.Previous);
            if (next != pipeline.Tail)
            {
                names.Add(next.Name);
                // Stop early if the list contains a cycle.
                Assert.True(names.Count <= expectedNames.Length);
            }
            context = next;
        }
        Assert.Equal(expectedNames, names);
    }
    private sealed class LifecycleCountingHandler : ChannelInboundHandlerAdapter
    {
        internal int Added, Removed, Read;
        public override void HandlerAdded(ChannelHandlerContext context) => Interlocked.Increment(ref Added);
        public override void HandlerRemoved(ChannelHandlerContext context) => Interlocked.Increment(ref Removed);
        public override void ChannelRead(ChannelHandlerContext context, object message)
        { Interlocked.Increment(ref Read); context.FireChannelRead(message); }
    }
    private sealed class SelfReplacingHandler(IChannelHandler replacement, Exception error) : ChannelInboundHandlerAdapter
    {
        internal int Removed;
        public override void HandlerAdded(ChannelHandlerContext context)
        { context.Pipeline.Replace(this, "real", replacement); throw error; }
        public override void HandlerRemoved(ChannelHandlerContext context) => Interlocked.Increment(ref Removed);
    }
    private sealed class SelfRemovingHandler(Exception error) : ChannelInboundHandlerAdapter
    {
        internal int Removed;
        public override void HandlerAdded(ChannelHandlerContext context) { context.Pipeline.Remove(this); throw error; }
        public override void HandlerRemoved(ChannelHandlerContext context) => Interlocked.Increment(ref Removed);
    }
    private sealed class DeferredThrowingHandler(Exception error) : ChannelInboundHandlerAdapter
    {
        internal int Removed;
        public override void HandlerAdded(ChannelHandlerContext context) => throw error;
        public override void HandlerRemoved(ChannelHandlerContext context) => Interlocked.Increment(ref Removed);
    }
    private sealed class ReplaceOnRemoval(IChannelHandler replacement) : ChannelInboundHandlerAdapter
    { public override void HandlerRemoved(ChannelHandlerContext context) => context.Pipeline.Replace("handler", "replacement", replacement); }
}
