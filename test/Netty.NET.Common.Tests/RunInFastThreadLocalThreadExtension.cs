/*
 * Copyright 2023 The Netty Project
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
using System.Runtime.ExceptionServices;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests;

/**
 * Annotate your test class with {@code @ExtendWith(RunInFastThreadLocalThreadExtension.class)} to have all test methods
 * run in a {@link io.netty.util.concurrent.FastThreadLocalThread}.
 * <p>
 * This extension implementation is modified from the JUnit 5
 * <a href="https://junit.org/junit5/docs/current/user-guide/#extensions-intercepting-invocations">
 * intercepting invocations</a> example.
 */
public static class RunInFastThreadLocalThreadExtension
{
    private sealed class Worker(IRunnable invocation, Action<Exception> cleanupFailure) : FastThreadLocalThread(invocation)
    {
        public override void Run()
        {
            try { base.Run(); }
            catch (Exception error) { cleanupFailure(error); }
        }
    }

    // CLR adaptation: xUnit cases call this helper explicitly rather than using
    // JUnit interception. ExceptionDispatchInfo preserves the worker's stack.
    public static void Run(Action invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ExceptionDispatchInfo failure = null;
        var thread = new Worker(Runnables.Create(() =>
        {
            try { invocation(); }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        }), error => failure ??= ExceptionDispatchInfo.Capture(error));
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
