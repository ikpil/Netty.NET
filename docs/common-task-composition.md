# Native Task composition and producer ownership

Original baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c`.
This decision replaces four Java-shaped common helpers and the deprecated unary
notifier alias with .NET functionality;
it does not introduce another facade with renamed add/finish/listener methods.

## Source and consumer evidence

- `common/src/main/java/io/netty/util/concurrent/PromiseCombiner.java` collects
  child futures, waits for all of them, and completes an aggregate producer.
  Its consumers include `codec-base/.../MessageToMessageEncoder.java`,
  `codec-http/.../HttpObjectEncoder.java`, `codec-http2/.../CompressorHttp2ConnectionEncoder.java`,
  and `transport/.../PendingWriteQueue.java`.
- `common/.../concurrent/PromiseNotifier.java` forwards one result to other
  producers. Consumers include `codec-http/.../websocketx/WebSocketProtocolHandler.java`,
  `codec-classes-quic/.../QuicheQuicStreamChannel.java`, and
  `transport-classes-io_uring/.../AbstractIoUringChannel.java`.
- `common/.../concurrent/UnaryPromiseNotifier.java` is deprecated in favor of
  PromiseNotifier.cascade. An all-module pinned search finds only its declaration,
  constructor and logger, with no construction, cascadeTo call or test consumer.
  Its success/fault/cancellation forwarding is the same native transfer decision;
  it does not require restoring a FutureListener/Promise facade.
- `common/.../concurrent/PromiseAggregator.java` is deprecated in favor of the
  combiner. The pinned repository has no production construction of it outside
  its own declaration; its remaining constructions are common tests.
- `common/.../internal/PromiseNotificationUtil.java` logs rejected attempts to
  complete another producer. In this C# common implementation its sole caller
  was the removed notifier.

Other modules establish requirements only; their C# implementations are outside
the current common stage.

## Native decisions

| Original helper | CLR choice | Preserved requirement / explicit difference |
| --- | --- | --- |
| PromiseCombiner | Collect `List<Task>` and call `Task.WhenAll` after collection | Wait for every child, retain original failure objects and support different result types through Task. Collection/snapshot replaces the mutable builder's add/finish phase. |
| PromiseNotifier | Share the same read-only Task when appropriate; otherwise the actual producer calls `TaskCompletionSource<T>.TrySetFromTask` | Transfer result, complete exception information, canceled status/token, and reject overwriting an already-completed target. No receiver can force another operation's completion. |
| UnaryPromiseNotifier | The same shared Task / owned TCS TrySetFromTask decision | The deprecated unary alias has no distinct consumer contract. A rejected target write returns false; the actual owner chooses logging rather than allocating a notifier with a global logger. |
| PromiseAggregator | Task.WhenAll; explicitly use Task.WhenAny for a fail-fast observation if needed | Aggregate completion without forcibly failing sibling producers. Owner-controlled cancellation can be requested separately; a request is not an invented sibling result. |
| PromiseNotificationUtil | TCS TrySet* / TrySetFromTask returns a bool; the owning caller chooses whether a rejected write warrants logging | Preserve observable completion rejection without a globally selected logger or another Java Promise layer. |

Current TFM is net10.0. TrySetFromTask is available in its reference assembly,
including the non-generic TCS used for void operations; no Java Void type is
needed at this boundary.

Deliberate native differences:

- WhenAll faults if any child faults, even if a canceled child completes first.
  The Java combiner records the first unsuccessful completion and may therefore
  classify the aggregate as canceled despite a later real failure. Native fault
  precedence preserves that failure rather than treating it as canceled.
- The aggregate Task retains all child exceptions. Await may throw one original
  exception; transferring only that await exception would lose the others.
  TrySetFromTask copies the completed Task's whole status. A faulted Task carrying
  an OperationCanceledException stays faulted, rather than being reclassified.
- WhenAll captures the supplied inputs. Later List additions affect a subsequent
  batch, and multiple observers may aggregate the same tasks. There is no public
  finished-builder state or mandatory external aggregate producer.
- Null child tasks are rejected. The deprecated Java aggregator's null-child
  skipping is not carried into the native composition API.
- Canceling a waiting caller with WaitAsync(token) leaves the shared operation
  untouched. Operations that require linked cancellation use an explicitly owned
  CancellationTokenSource/token relationship. The old cascade's direct attempt
  to cancel another writable Future is not a native consumer capability.
- Task aggregation is not executor-thread-affine. Preserve executor-owned state
  by collecting tasks in its owner context and explicitly submitting state changes
  back to the executor. Await alone does not perform that dispatch.

## Consumer patterns

An encoder can return a Task representing all output writes:

```csharp
List<Task> writes = new();
foreach (var output in outputs)
    writes.Add(context.WriteAsync(output, cancellationToken));
return Task.WhenAll(writes);
```

An owned producer can forward a completion after it has completed, including all
exceptions. If its state transition must occur on the event loop, submit it there:

```csharp
Task all = Task.WhenAll(writes);
try { await all.ConfigureAwait(false); }
catch (Exception) { /* The completed Task contains the status to transfer. */ }
await executor.SubmitAsync(() => aggregate.TrySetFromTask(all));
```

If no separate producer is necessary, return the original Task. A completed Task
does not need a notifier object merely to serve another reader.

PendingWriteQueue's original synchronous write calls can revive its queue before
it drains. The native consumer test collects all revived writes before WhenAll;
this requirement must remain when transport is implemented.

## Original-test migration map

Test paths retain their original names to record provenance. Test classes and
bodies use native Task/TCS APIs. Each upstream test file's original license comment
is preserved. The four source helper files have no mechanical CLR counterpart;
their manifest entries are framework replacements, with this decision as evidence.

### PromiseCombinerTest (12 original methods)

| Original method | Native scenario / decision |
| --- | --- |
| testNullArgument | Required input rejection and recovery through TCS TrySetFromTask; a separate aggregate producer is optional. |
| testNullAggregatePromise | An empty Task batch succeeds without an aggregate producer. |
| testAddNullPromise | WhenAll rejects a null child; its native argument exception is used. |
| testAddAllNullPromise | WhenAll rejects a null task array. |
| testAddAfterFinish, testAddAllAfterFinish | A started batch snapshots the inputs; later collection additions cannot change that batch. Java builder prohibitions have no native counterpart. |
| testFinishCalledTwiceThrows | Multiple independent observers are allowed; there is no builder instance to finish twice. |
| testAddAllSuccess | Two pending Tasks complete the aggregate only after both finish. |
| testAddSuccess | Incrementally collected already-completed Tasks succeed. |
| testAddAllFail | Wait for all pending failures and retain both original exception objects. |
| testAddFail | Already-completed failures retain their identities. |
| testEventExecutor | Real-executor consumer collects different Task<T> values and explicitly transfers foreign completions on the event loop. No mutable combiner with a separate thread guard remains. |

Additional cases cover input duplication, fault/cancellation precedence in both
orders, canceled token retention and synchronously revived write collection.

### PromiseNotifierTest (5 original methods)

| Original method | Native scenario / decision |
| --- | --- |
| testNullPromisesArray, testNullPromiseInArray | The notifier constructor/array is removed. Required completed-source validation belongs to native TrySetFromTask; callers own any fan-out collection. |
| testListenerSuccess | Two actual TCS producers receive the identical result reference. |
| testListenerFailure | Two actual TCS producers receive the original failure object. |
| testCancelPropagationWhenFusedFromFuture | Completed source cancellation/token is transferred, or the source Task is shared directly. Consumer cancellation requests use explicit owned tokens. |

Additional cases cover already-completed targets, faulted cancellation exceptions,
multiple/nested exception preservation, null successful results, incomplete-source
rejection, independently canceled waiters and an explicitly linked cooperative
operation running on DefaultEventExecutor.

The old CLR-only defensive-copy/cascade tests are replaced by these ownership and
transfer cases. No array-copy or direct writable-Future cancellation behavior is
claimed for a removed notifier constructor.

### PromiseAggregatorTest (6 original methods)

| Original method | Native scenario / decision |
| --- | --- |
| testNullAggregatePromise | No required aggregate-producer argument exists; an optional owner uses TCS TrySetFromTask and cannot overwrite its result. |
| testAddNullFuture | A required task array cannot be null. |
| testSuccessfulNoPending | An empty batch succeeds. |
| testSuccessfulPending | Two pending Tasks are observed through their read-only views. Null entries are rejected rather than silently skipped. |
| testFailedFutureFailPending | A failure cannot manufacture sibling producer outcomes. WhenAll waits for their real completion; explicit cancellation remains the owner's choice. |
| testFailedFutureNoFailPending | Siblings remain unaffected. WhenAny supplies early observation when required, and WhenAll retains the eventual aggregate failure. |

This intentionally does not recreate deprecated Java fail-pending mutation as a
second native aggregation class.

## Constant identity review

Pinned `common/.../AbstractConstant.java` seals Object identity methods and has a
single static uniquifier despite Java generics. Its C# implementation now has sealed
identity/description overrides and a non-generic native Interlocked sequence.
ConstantPool<T> requires a reference type, because singleton identity and its
absence sentinel are not value-type/default(T) semantics.

Comparison retains identity/hash/discriminator ordering. Native integer CompareTo
avoids subtraction overflow breaking antisymmetry; numeric identity-hash ordering
is runtime-local and is not an interoperability promise. Comparing null produces
ArgumentNullException as the CLR translation of Java's null dereference.

Before these changes, four of six new constant identity tests failed: reference
constraint, shared generic-static sequence, final identity overrides, and null
comparison. The remaining original and CLR registry tests also validate key identity
and metadata.

## Verification checkpoint

At this historical checkpoint on Windows/net10.0, both Debug and Release PortingBatch passed 965 cases, with 14
existing runtime/upstream skips and zero failures (979 total). The affected
Release selection passed 80 cases, including the actual registry consumers.
At that checkpoint the default build failed with 286 AsciiStringCharacterTest
diagnostics. The later [AsciiString checkpoint](common-ascii-memory.md) restores
both default builds and executes the full suites; [common-porting.md](common-porting.md)
records current results. The common completion gate remains open.

Manifest decisions cover all four removed helpers and all three original test
files. The common public Future/Promise/scheduling surface and the remaining
source/test inventory still require review; this is a concrete native composition
step, not a claim that common is complete.

## CLR specifications

- [Task.WhenAll completion, exceptions and cancellation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenall?view=net-10.0).
- [Generic TrySetFromTask](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1.trysetfromtask?view=net-10.0).
- [Non-generic TrySetFromTask](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetfromtask?view=net-10.0).

## Deprecated unary notifier review and comment provenance

The pending UnaryPromiseNotifier entry is a CLR replacement, not a missing native
implementation. The pinned all-module search finds no caller. Its completion
forwarding is already exercised by TaskCompletionTransferPortTest's ten cases:
result/failure identity, canceled token, target overwrite rejection, faulted
OperationCanceledException, full non-generic failure transfer, validation, observer
cancellation and real executor cancellation ownership. These cases run in the
current default full suite; no redundant alias-specific test or public C# class
is introduced. As with PromiseNotifier, TrySetFromTask keeps the entire completed
Task outcome and the actual producer owns any rejected-write logging. This does
not claim that a native consumer still supports Java FutureListener or direct
cancellation of another writable result.

Both original comments below are from
common/src/main/java/io/netty/util/concurrent/UnaryPromiseNotifier.java at the pinned
baseline. They record source provenance rather than documenting a C# notifier API.

```java
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

/**
 *
 * @deprecated use {@link PromiseNotifier#cascade(boolean, Future, Promise)}.
 */
```
