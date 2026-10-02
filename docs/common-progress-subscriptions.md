# Native progress subscriptions

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common progress policy and its consumer contracts; transport/handler are
read for evidence and are not implemented in this checkpoint.

## Evidence and decision

DefaultProgressivePromise validates counts and delegates notification to
DefaultPromise. DefaultPromise snapshots progressive listener membership when
reporting, filters completion-only listeners, and invokes callbacks on the
assigned executor while isolating failures. DefaultFutureListeners uses object
identity and permits duplicate registrations. ChannelOutboundBuffer accumulates
entry bytes and reports against the entry total. ChunkedWriteHandler.PendingWrite
reports the final total before success. Channel-specific promises associate the
channel's executor with these notifications.

Task/TaskCompletionSource owns the operation result, failure and cancellation.
IProgress<TransferProgress> represents reports. ExecutorProgress supplies the
executor dispatch, FIFO, callback isolation and detachable subscriptions which
System.Progress's captured SynchronizationContext does not establish. There is
no second result-owning progressive Promise hierarchy. DefaultProgressivePromise,
IProgressivePromise, IProgressiveFuture, IGenericProgressiveFutureListener and
ImmediateProgressivePromise, plus newProgressivePromise factories, are removed.
Plain Promise and backend submission/scheduling adapters have since migrated to
native APIs and their result hierarchy is removed; see common-native-future-retirement.md.

## Registration and lifetime

The empty-observer constructor accepts an executor and the existing operation
Task. Register accepts progress callbacks, terminal callbacks, or both, and
returns a unique ProgressRegistration. Duplicate delegates have independent
handles; removing a subscription never relies on delegate/value equality.
The constructor accepting callbacks remains a convenient initial registration.

```csharp
var source = new TaskCompletionSource<int>(
    TaskCreationOptions.RunContinuationsAsynchronously);
using var progress = new ExecutorProgress(executor, source.Task);
using var subscription = progress.Register(
    value => UpdateTransferDisplay(value.Completed, value.Total),
    task => OnTransferCompleted(task));
IProgress<TransferProgress> sink = progress;
sink.Report(new TransferProgress(3, 10));
sink.Report(new TransferProgress(10, 10));
source.SetResult(10);
int result = await source.Task;
await subscription.NotificationsCompleted;
await progress.NotificationsCompleted;
```

Reports capture registration membership at admission. Later subscribers receive
future reports, with no historical replay. A gate serializes registry mutations,
report admission and batch claim. A BCL LinkedList holds registrations and a
BCL Queue holds admitted snapshots. Once a batch is claimed, detached callbacks
in that batch may finish; detach suppresses later unclaimed batches. Terminal
claim lets the subscription finish normally even if its own handle is disposed.
Whole-observation disposal stops later callback invocations, including a claimed
terminal batch, and settles any detached handles in that batch as canceled.
Pinned Java can still invoke a listener removed after the report's snapshot was
captured. Native handle disposal deliberately suppresses an unclaimed batch so
that a consumer can release its callback owner; it does not promise to interrupt
an already claimed batch. Snapshot admission and both sides of this claim
boundary have separate tests.

Register closes when the source Task completes. Late completion observers use
ExecutorCompletion(source.Task), keeping operation identity and executor policy
explicit. Java's fluent Future return types have no role in this native API.
The existing CLR duplicate/removal/count scenarios are translated to disposable
handles, Task identity, nullable unknown totals and CLR argument exceptions.
Immediate/pool progress probes use pending Tasks and in-loop reports instead of
adding Java blocking-detection methods to Task. A subsequent producer checkpoint
also migrated the separate plain-Promise probes; see common-native-producers.md.

One FIFO drain serializes progress and terminal callbacks, even on a pool.
Reentrant reports run after the current batch, bounding recursion; pinned Java
explicitly does not guarantee order across overlapping progress notifications.
This strengthened native ordering is intentional. Registration/producer context
is not captured; synchronous callbacks use isolated executor ExecutionContext.
Thrown callback exceptions are logged and do not change the source outcome.
Async-void callbacks are unsupported; asynchronous consumers must own their Tasks
and explicitly marshal executor-affine state after awaiting.

The source is the sole result owner. Per-subscription NotificationsCompleted
tracks callback lifetime: terminal delivery succeeds, detach/whole disposal/native
queue removal cancels, and dispatch rejection faults. These notification outcomes
never complete or cancel the source. A retained subscription or notification Task
keeps a pending dispatcher alive. Settlement clears callbacks, operation and
executor references, so retaining a completed notification Task does not retain
the producer's payload. Pending report snapshots retain registration handles
whose callbacks are cleared by detach, not removed callback targets.

## Verification

DynamicProgressContractTest covers snapshot admission, duplicate delegates,
registration/reentrant reporting, progress versus terminal claim/disposal,
late completion, rejection identity, callback context/failure isolation, detached
callback collection, notification-task ownership, result/callback release and
direct-pool/NonSticky shutdown removal. The initial dynamic implementation left
a detached terminal handle pending when another observer disposed the reporter.
dynamic-progress-before.trx contains that timeout; dynamic-progress-after.trx
records the initial nine cases passing after settling claimed handles on abort.
This was a defect in the new subscription implementation, not an upstream bug.

Final focused validation: progress-subscriptions-final-contracts-debug.trx,
159 passed / zero failed / zero skipped, Windows/net10.0. This includes 16 dynamic
cases, 26 earlier progress cases, completion observation, translated Promise
contracts and affected executor lifecycle/pool contracts. Full default Debug and
Release each discover 1333 cases: 1319 passed / zero failed / 14 existing skips.
Evidence: progress-subscriptions-full-debug.trx and
progress-subscriptions-full-release.trx in the ignored TestResults directory.
The 759 test identities outside the Porting contract folder and all skipped
identities match the preceding completed-result checkpoint in each configuration.
The original-comment audit finds zero missing in all 105 verified source/test
entries and all 21 archived progressive-source/plumbing comments. The manifest
records four former source ports as CLR replacements; this count change reflects
the native design decision, not removal of required behavior or completion of the
module. Remaining source/API reviews are recorded in common-porting.md.
No allocation or throughput improvement is claimed without measurements.

## Original comment provenance

The following original comments are copied from pinned Git objects in source
order, with line locations. Four removed progressive sources are archived in
full. Removed progressive-only comments from DefaultPromise,
DefaultFutureListeners and EventExecutor are archived separately, along with
ImmediateEventExecutor's second No check comment. These describe the original
Java contract; the native differences are stated above and in
common-native-progress.md.

### DefaultProgressivePromise.java

Upstream: common/src/main/java/io/netty/util/concurrent/DefaultProgressivePromise.java

Original line 1:

```java
/*
 * Copyright 2013 The Netty Project
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
```

Original line 23:

```java
/**
     * Creates a new instance.
     *
     * It is preferable to use {@link EventExecutor#newProgressivePromise()} to create a new progressive promise
     *
     * @param executor
     *        the {@link EventExecutor} which is used to notify the promise when it progresses or it is complete
     */
```

Original line 35:

```java
/* only for subclasses */
```

Original line 40:

```java
// total unknown
```

Original line 41:

```java
// normalize
```

### ProgressiveFuture.java

Upstream: common/src/main/java/io/netty/util/concurrent/ProgressiveFuture.java

Original line 1:

```java
/*
 * Copyright 2013 The Netty Project
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
```

Original line 19:

```java
/**
 * A {@link Future} which is used to indicate the progress of an operation.
 */
```

### ProgressivePromise.java

Upstream: common/src/main/java/io/netty/util/concurrent/ProgressivePromise.java

Original line 1:

```java
/*
 * Copyright 2013 The Netty Project
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
```

Original line 18:

```java
/**
 * Special {@link ProgressiveFuture} which is writable.
 */
```

Original line 23:

```java
/**
     * Sets the current progress of the operation and notifies the listeners that implement
     * {@link GenericProgressiveFutureListener}.
     */
```

Original line 29:

```java
/**
     * Tries to set the current progress of the operation and notifies the listeners that implement
     * {@link GenericProgressiveFutureListener}.  If the operation is already complete or the progress is out of range,
     * this method does nothing but returning {@code false}.
     */
```

### GenericProgressiveFutureListener.java

Upstream: common/src/main/java/io/netty/util/concurrent/GenericProgressiveFutureListener.java

Original line 1:

```java
/*
 * Copyright 2013 The Netty Project
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
```

Original line 20:

```java
/**
     * Invoked when the operation has progressed.
     *
     * @param progress the progress of the operation so far (cumulative)
     * @param total the number that signifies the end of the operation when {@code progress} reaches at it.
     *              {@code -1} if the end of operation is unknown.
     */
```

### DefaultPromise.java

Upstream: common/src/main/java/io/netty/util/concurrent/DefaultPromise.java

Original line 743:

```java
/**
     * Notify all progressive listeners.
     * <p>
     * No attempt is made to ensure notification order if multiple calls are made to this method before
     * the original invocation completes.
     * <p>
     * This will do an iteration over all listeners to get all of type {@link GenericProgressiveFutureListener}s.
     * @param progress the new progress.
     * @param total the total progress.
     */
```

Original line 794:

```java
/**
     * Returns a {@link GenericProgressiveFutureListener}, an array of {@link GenericProgressiveFutureListener}, or
     * {@code null}.
     */
```

Original line 802:

```java
// No listeners added
```

Original line 807:

```java
// Copy DefaultFutureListeners into an array of listeners.
```

Original line 835:

```java
// Only one listener was added and it's not a progressive listener.
```

### DefaultFutureListeners.java

Upstream: common/src/main/java/io/netty/util/concurrent/DefaultFutureListeners.java

Original line 24:

```java
// the number of progressive listeners
```

### EventExecutor.java

Upstream: common/src/main/java/io/netty/util/concurrent/EventExecutor.java

Original line 59:

```java
/**
     * Create a new {@link ProgressivePromise}.
     */
```

### ImmediateEventExecutor.java

Upstream: common/src/main/java/io/netty/util/concurrent/ImmediateEventExecutor.java

Original line 158:

```java
// No check
```
