/*
 * Copyright 2026 The Netty Project
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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Xunit.Sdk;
using Xunit.v3;

namespace Netty.NET.Common.Test;

// CLR: a CLR-only unit with no Java file of its own. It is what JUnit does for LeakPresenceExtension: the service file
// of testsuite-common registers the extension, junit.jupiter.extensions.autodetection.enabled (argLine.leak of the
// parent pom) loads it, and JUnit calls beforeAll and afterAll around every test class of the modules whose tests
// depend on testsuite-common. A test assembly of such a module declares
//
//     [assembly: TestFramework(typeof(LeakPresenceTestFramework))]
//
// and this xUnit v3 test framework runs the default xUnit pipeline with one change: the collection runner context
// calls LeakPresenceExtension.BeforeAll before the default class runner runs a test class and AfterAll after it.
// The class runner covers the class fixtures, every test class instance and test, and the disposal of the fixtures,
// so AfterAll checks after the class-level cleanup, as JUnit's afterAll runs after @AfterAll. A leak fails the run as a
// failing afterAll fails the class container in JUnit: the collection runner adds it to the collection's
// ExceptionAggregator when the collection finishes, which xUnit reports as a test collection cleanup failure whose
// message names the class scope (one collection per class unless [Collection] groups classes). Adding it when the class
// finishes would fail every later class of a shared collection (xUnit fails the classes of a collection whose
// aggregator holds an exception; measured with the AllocatorLeakDetectionCollection of the Buffer tests).
//
// xUnit creates the default runners through their static Instance properties and the contexts in their Run methods,
// so the assembly runner, its context, the collection runner and its context are replaced along the path from the
// executor to RunTestClass. Each replacement does what the default does (measured against xunit.v3 4.0.3:
// XunitTestFramework.CreateExecutor, XunitTestFrameworkExecutor.RunTestCases, XunitTestAssemblyRunner.Run,
// XunitTestAssemblyRunnerBaseContext.RunTestCollection, XunitTestCollectionRunner.Run and
// XunitTestCollectionRunnerContext.RunTestClass), with its own type in place of the default one. With
// io.netty.test.leakPresenceDetection.disabled=true the framework is xUnit's own (LeakPresenceExtension
// .EvaluateExecutionCondition).
public sealed class LeakPresenceTestFramework : XunitTestFramework
{
    private readonly string configFileName;

    public LeakPresenceTestFramework()
        : this(null)
    {
    }

    public LeakPresenceTestFramework(string configFileName)
        : base(configFileName)
    {
        this.configFileName = configFileName;
        // CLR: xUnit creates the test framework before it discovers or runs tests, so the custom detector is set
        // before any resource leak detector exists.
        LeakPresenceExtension.Initialize();
    }

    protected override ITestFrameworkExecutor CreateExecutor(Assembly assembly)
    {
        if (!LeakPresenceExtension.EvaluateExecutionCondition(out _))
        {
            return base.CreateExecutor(assembly);
        }
        // CLR: the arguments of XunitTestFramework.CreateExecutor.
        return new Executor(new XunitTestAssembly(assembly, configFileName, null, null, null, null, null));
    }

    private sealed class Executor(IXunitTestAssembly testAssembly) : XunitTestFrameworkExecutor(testAssembly)
    {
        public override async ValueTask RunTestCases(
            IReadOnlyCollection<IXunitTestCase> testCases,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions,
            CancellationToken cancellationToken) =>
            await AssemblyRunner.Instance.Run(
                TestAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
    }

    private sealed class AssemblyRunner : XunitTestAssemblyRunner
    {
        internal static new AssemblyRunner Instance { get; } = new AssemblyRunner();

        internal new async ValueTask<RunSummary> Run(
            IXunitTestAssembly testAssembly,
            IReadOnlyCollection<IXunitTestCase> testCases,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions,
            CancellationToken cancellationToken)
        {
            await using AssemblyRunnerContext ctxt = new AssemblyRunnerContext(
                testAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
            await ctxt.InitializeAsync();
            return await Run(ctxt);
        }
    }

    private sealed class AssemblyRunnerContext(
        IXunitTestAssembly testAssembly,
        IReadOnlyCollection<IXunitTestCase> testCases,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions,
        CancellationToken cancellationToken)
        : XunitTestAssemblyRunnerContext(testAssembly, testCases, executionMessageSink, executionOptions,
            cancellationToken)
    {
        public override ValueTask<RunSummary> RunTestCollection(
            IXunitTestCollection testCollection,
            IReadOnlyCollection<IXunitTestCase> testCases) =>
            CollectionRunner.Instance.Run(
                testCollection,
                testCases,
                ExplicitOption,
                MessageBus,
                Aggregator.Clone(),
                CancellationTokenSource,
                ParallelMode,
                Scheduler,
                AssemblyFixtureMappings);
    }

    private sealed class CollectionRunner : XunitTestCollectionRunner
    {
        internal static new CollectionRunner Instance { get; } = new CollectionRunner();

        internal new async ValueTask<RunSummary> Run(
            IXunitTestCollection testCollection,
            IReadOnlyCollection<IXunitTestCase> testCases,
            ExplicitOption explicitOption,
            IMessageBus messageBus,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource,
            ParallelMode parallelMode,
            ExecutionScheduler scheduler,
            FixtureMappingManager assemblyFixtureMappings)
        {
            await using CollectionRunnerContext ctxt = new CollectionRunnerContext(
                testCollection,
                testCases,
                explicitOption,
                messageBus,
                aggregator,
                cancellationTokenSource,
                parallelMode,
                scheduler,
                assemblyFixtureMappings);
            await ctxt.InitializeAsync();
            return await Run(ctxt);
        }

        protected override ValueTask<bool> OnTestCollectionFinished(
            XunitTestCollectionRunnerContext ctxt,
            RunSummary summary)
        {
            foreach (Exception leak in ((CollectionRunnerContext) ctxt).Leaks)
            {
                ctxt.Aggregator.Add(leak);
            }
            return base.OnTestCollectionFinished(ctxt, summary);
        }
    }

    private sealed class CollectionRunnerContext(
        IXunitTestCollection testCollection,
        IReadOnlyCollection<IXunitTestCase> testCases,
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        ParallelMode parallelMode,
        ExecutionScheduler scheduler,
        FixtureMappingManager assemblyFixtureMappings)
        : XunitTestCollectionRunnerContext(testCollection, testCases, explicitOption, messageBus, aggregator,
            cancellationTokenSource, parallelMode, scheduler, assemblyFixtureMappings)
    {
        // The leaks of the classes run so far, reported when the collection finishes.
        internal readonly List<Exception> Leaks = new List<Exception>();

        public override async ValueTask<RunSummary> RunTestClass(
            IXunitTestClass testClass,
            IReadOnlyCollection<IXunitTestCase> testCases)
        {
            // CLR: BeforeAll sets the scope holder of this async method's flow; the default class runner below runs in
            // that flow, and the change is undone when this method returns, so the next class starts without a scope.
            LeakPresenceExtension.BeforeAll(testClass.Class, testClass.TestClassSimpleName);
            LeakPresenceExtension.ScopeWrapper scope = LeakPresenceExtension.WithTransferableScope.SCOPE.Value;
            RunSummary summary = await base.RunTestClass(testClass, testCases);
            try
            {
                await AfterAll(scope, testClass.Class);
            }
            catch (Exception leak)
            {
                Leaks.Add(leak);
            }
            return summary;
        }

        // CLR: with io.netty.util.LeakPresenceDetector.trackCreationStack=true the leak carries the creation stacks as
        // suppressed exceptions (ThrowableUtil.AddSuppressed), which JUnit prints with the failure and xUnit does not
        // see; they become the inner exceptions of the reported failure, with the same message.
        private static async ValueTask AfterAll(LeakPresenceExtension.ScopeWrapper scope, Type testClass)
        {
            try
            {
                await LeakPresenceExtension.AfterAll(scope, testClass);
            }
            catch (InvalidOperationException leak) when (ThrowableUtil.GetSuppressed(leak).Length > 0)
            {
                throw new InvalidOperationException(leak.Message, new AggregateException(ThrowableUtil.GetSuppressed(leak)));
            }
        }
    }
}
