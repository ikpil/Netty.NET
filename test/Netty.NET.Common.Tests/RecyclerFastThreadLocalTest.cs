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
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests;

public class RecyclerFastThreadLocalTest : RecyclerTest
{
    protected override void RunTest(Action invocation) => RunInFastThreadLocalThreadExtension.Run(invocation);
    protected override Thread NewThread(Action invocation)
    {
        var owner = new FastThreadLocalThread(invocation);
        owner.Thread.IsBackground = true;
        return owner.Thread;
    }

    // CLR Thread is sealed; the inherited GC fixture uses WeakReference<Thread>
    // instead of a subclass finalizer. This override runs all six original rows
    // with FastThreadLocalThread owners and the same retained-object assertions.
    // Store a reference to the HandledObject to ensure it is not collected when the run method finish.
    // Null out so it can be collected.
    // Loop until the Thread was collected. If we can not collect it the Test will fail due of a timeout.
    // Now call recycle after the Thread was collected to ensure this still works...
    [Theory]
    [MemberData(nameof(OwnerTypeAndUnguarded))]
    public override void TestThreadCanBeCollectedEvenIfHandledObjectIsReferenced(OwnerType ownerType, bool unguarded)
        => base.TestThreadCanBeCollectedEvenIfHandledObjectIsReferenced(ownerType, unguarded);
}
