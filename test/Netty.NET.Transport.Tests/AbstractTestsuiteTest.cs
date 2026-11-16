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

using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Tests;

// CLR: the source bounds T by AbstractBootstrap<?, ?>. In this port Bootstrap and ServerBootstrap do not share a
// public generic AbstractBootstrap<B, C> base (AbstractBootstrap is an internal static helper class), so T is only
// constrained to a reference type, which also allows the volatile field below.
// CLR: the source testsuite runs its classes one after another ("By default, JUnit Jupiter tests are run
// sequentially in a single thread", https://docs.junit.org/5.10.0/user-guide/index.html; Netty enables
// junit.jupiter.execution.parallel only for buffer, codec-http, codec-http2 and handler), and every class shares
// SocketTestPermutation's NIO_GROUP. xUnit runs one test collection per class in parallel but "tests within a single
// test collection will not be run in parallel against each other" (https://xunit.net/docs/running-tests-in-parallel),
// so all harness classes join one collection; CollectionAttribute is declared Inherited = true.
[Collection("io.netty.testsuite.transport")]
public abstract class AbstractTestsuiteTest<T> where T : class
{
    protected readonly IInternalLogger logger;
    protected volatile T cb;

    // CLR: a C# field initializer cannot call the instance GetType(), so the source's field initializer
    // InternalLoggerFactory.getInstance(getClass()) runs in the constructor.
    protected AbstractTestsuiteTest()
    {
        logger = InternalLoggerFactory.GetInstance(GetType());
    }

    protected abstract List<TestsuitePermutation.IBootstrapFactory<T>> NewFactories();

    protected virtual List<IByteBufAllocator> NewAllocators()
    {
        return TestsuitePermutation.Allocator();
    }

    // CLR: ported test bodies are Task-based (sync()/await() become await), so run awaits every permutation in the
    // source order. The caller passes xUnit's TestContext.Current where the source receives JUnit's TestInfo.
    // String.format keeps the JVM default-locale formatting through CultureInfo.CurrentCulture.
    protected async Task Run(ITestContext testInfo, Runner runner)
    {
        List<TestsuitePermutation.IBootstrapFactory<T>> combos = NewFactories();
        string methodName = TestUtils.TestMethodName(testInfo);
        foreach (IByteBufAllocator allocator in NewAllocators())
        {
            int i = 0;
            foreach (TestsuitePermutation.IBootstrapFactory<T> e in combos)
            {
                cb = e.NewInstance();
                Configure(cb, allocator);
                logger.Info(string.Format(CultureInfo.CurrentCulture,
                        "Running: {0} {1} of {2} with {3}",
                        methodName, ++ i, combos.Count, StringUtil.SimpleClassName(allocator)));
                await runner(cb);
            }
        }
    }

    protected abstract void Configure(T bootstrap, IByteBufAllocator allocator);

    // CLR: the source's single-method Runner<CB> interface is a delegate over this class's T, so tests pass a method
    // group or lambda where the source passes an anonymous Runner ("Delegates are used to pass methods as arguments
    // to other methods", https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/delegates/). It returns
    // the Task of the ported asynchronous body instead of void.
    public delegate Task Runner(T cb);
}
