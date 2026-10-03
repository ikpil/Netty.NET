# Native completion without a Future/Promise hierarchy

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and tests; downstream modules are read only evidence.

## Requirements and native ownership

Pinned DefaultPromise publishes results, protects cancellation after an operation
commits, and orders listener delivery on an executor. AbstractFuture supplies JDK
blocking waits and exception wrapping. Future/Promise and their listener interfaces
expose that combined mutable/result/dispatch object; DefaultFutureListeners stores
its callbacks. These roles now have separate native owners: Task/TCS for outcomes,
private submission/scheduling claims for commitment, and ExecutorCompletion for
ordered detachable callbacks. Its LinkedList and unique registrations replace the
erased callback array and typed-listener adapters. There is one outcome per operation.

Pinned downstream DefaultChannelPromise binds a channel and flush checkpoint to
DefaultPromise; SSL and proxy LazyChannelPromise defer choosing an executor until
handler attachment. AbstractChannel and ChannelOutboundBuffer use setUncancellable
at actual operation commitment. These are ownership, metadata and dispatch rules,
not a requirement for inheritance from a common Future. Existing native producer,
submission, scheduling and completion consumer tests verify these boundaries.
Channel/handler implementations remain outside the common implementation scope;
future ports must keep their own channel/checkpoint/attachment metadata and claims.
Task identity is not a unique reservation or channel identity.

No common runtime caller now uses the Java result interfaces. NonSticky's obsolete
IFuture cancellation fallback is removed; its INativeSubmission rejection/shutdown
hooks still settle the native producer outside the child queue gate. Raw IRunnable
items do not acquire an asynchronous result merely because they enter a queue.
Queue tests inspect separation from Task instead of referring to a removed type.

DefaultPromise, AbstractFuture, DefaultFutureListeners, IFuture, IPromise,
IFutureListener and IGenericFutureListener are removed. Their original comments
are preserved below. The original test scenarios use TCS and ExecutorCompletion
directly, without a test-only Promise adapter or another public result factory.

## Intentional CLR differences

- await/GetAwaiter().GetResult preserves an original fault. Task.Result/Wait uses
  AggregateException. An explicitly supplied AggregateException remains that exact
  fault, with no accidental flattening by the completion layer.
- Cancellation is explicitly TrySetCanceled(token) or an owner's cooperative token
  policy. SetException(OperationCanceledException) is a fault and retains that exact
  error. A canceled Task retains its token and has no Exception/cause object; await
  constructs a cancellation exception. Neither observer cancellation nor timeout
  completes the operation owner's TCS.
- Result completion and ordered notification completion are distinct. The producer
  ordinarily uses RunContinuationsAsynchronously; callback affinity/order/reentrancy are the
  explicit ExecutorCompletion contract, not arbitrary Task continuation ordering.
- Blocking Future get/sync/await, uninterruptible waits, synthetic cancellation
  frames, getNow's null sentinel and Java listener wildcard/fluent returns are
  removed. Consumers use await, WaitAsync(timeout/token) and Task status. The
  claimed native submission scenario proves cancellation requests do not fabricate
  completion and normal callback return retains success after a commitment.
- BlockingOperationException has only two pinned runtime users: DefaultPromise's
  blocking wait and DefaultChannelGroupFuture's blocking wait; ChannelFuture
  documentation describes that guard. The removed common blocking facade needs no
  specialized exception, so its orphan class is removed too. A bare Task cannot
  intercept arbitrary caller Result/Wait to enforce event-loop deadlock checks.
  Async consumers must compose/await operations; this is an explicit API difference,
  not a claim that the CLR guards synchronous misuse or arbitrary cyclic awaits.
- The old maxListenerStackDepth JVM property does not configure native dispatch.
  ExecutorCompletion has a shared per-thread depth bound, and ImmediateEventExecutor
  trampolines reentrant execute calls; native completion-chain tests exercise both.

## Pending write ownership

Pinned util.internal.PendingWrite pairs a message with a producer and recycles a
node. Its failure releases one message reference before completing; success and
plain recycling leave message ownership with the next consumer. Pinned all-module
search finds no import or fully qualified consumer of this exact common class.
Transport PendingWriteQueue, SPDY and ChunkedWriteHandler have distinct nested
PendingWrite implementations; they do not use this two-argument helper. There is
no original standalone common PendingWrite test.

The existing pooled helper is retained with native Rent/Message/Completion and
Recycle/FailAndRecycle/SucceedAndRecycle/RecycleAndGetCompletionSource APIs.
A caller-supplied non-generic TCS owns the result. Observers see only Completion;
the explicit transfer method returns mutation authority to the next producer.
Claiming a node prevents repeated terminal actions before another rent. This is
a borrowed queue node: retaining a reference after recycling is invalid, and a
reused node is not an async operation identity. No new pool-performance claim or
extra retain on the message is introduced.

Native TrySetResult/TrySetException means a canceled/already-settled producer cannot
prevent failed-write message cleanup. This intentionally fixes the pinned helper's
release-then-setFailure path, which could throw before returning the node. Fields
are cleared and recycling runs in finally even if message release throws. Such a
release error propagates and does not pretend the result was settled; the operation
owner still retains its TCS to finish error handling. Null failure is rejected
before consuming ownership. The original seven comments remain beside the mapped
implementation, including its misleading RecyclableArrayList factory comment as
source provenance; Rent actually creates a pending-write node.

## Scenario mapping and validation

All 20 DefaultPromiseTest method identities remain. No-listener outcomes do not
schedule executor work. Cancellation cases use native canceled Tasks and token
observation. Both signal values retain exact identity. Listener notification order
keeps 100000 iterations and the reentrant fourth listener. Deferred/late listeners
still update write state before the next queued read, on success and failure.
Both chain shapes retain 20000 operations, inside/outside immediate and dedicated
executors. Their producer permits inline continuation to exercise the actual
dispatcher recursion bound, as the pinned synchronous Promise chain does; their
original two-second bound is retained. Other producer/competition fixtures use
RunContinuationsAsynchronously. Signal competition retains all 4096 real producer threads and its
10-second bound. setUncancellableGetNow now verifies the actual native submission
commitment rather than preserving a public setter/null-result facade.

TaskPromiseStateContractTest retains 2000 competing reader/producer iterations for
each success/fault/canceled outcome; its notification must carry the exact published
Task, value/error/token. Its CLR-only CancellationFailurePreservesItsTokenAndOriginalCause
probe is renamed CancellationAndFaultPublishDistinctTerminalStates, documenting
the explicit distinction between native cancellation and a supplied fault exception.
No original fixture identity is removed. Eight PendingWrite cases verify failure,
success, producer transfer, existing terminal states, exact fault identity, release
failure cleanup, null validation and unobserved ownership.

The first full Debug run failed the dedicated-executor A chain's two-second bound:
the initial translation forced 20000 ThreadPool round trips with RCAA, measuring
pool scheduling latency instead of the original synchronous reentrancy scenario.
The chain now uses inline-capable TCS to exercise stack bounds; no iteration or
timeout is relaxed. The failed native-future-retirement-full-debug.trx is retained.
Affected Debug selection: 236 passed, zero failed, zero skipped
(native-future-retirement-final-contracts-debug.trx). After correcting the chain
translation, the chain/completion/state selection passes 54, zero failed/skipped
(native-future-retirement-chain-contracts-debug.trx). Default Windows/net10.0 Debug
and Release each discover 1365 cases: 1351 passed, zero failed, 14 unchanged skips,
recorded in native-future-retirement-final-full-debug.trx and
native-future-retirement-final-full-release.trx in ignored TestResults. All 759
non-Porting fixture identities and 14 skip identities match the preceding wrapper
cleanup checkpoint. Differences are exactly the eight new PendingWrite cases and
the one CLR cancellation probe rename documented above; see
native-future-retirement-identity-comparison.json. No test source is excluded.
The comment audit records zero missing in all 98 verified source/test entries,
all 70 comments of the eight retired source entries, all seven PendingWrite and
12 DefaultPromiseTest comments. All implementation/candidate paths exist and
git diff --check passes. Existing compiler/analyzer warnings remain.
Common completion is not established by this migration: executor shutdown/config
policies, collections/queues, strings/encoding/platform and all unreviewed source
decisions remain required work. The subsequent unordered graceful-shutdown review
implements quiet/timeout admission and actual drain from the documented group
contract; see common-unordered-graceful-shutdown.md. The subsequent native queue
review removes public mutators and implements submission cancellation withdrawal;
see common-unordered-native-queue.md. Immutable constructor settings and native
diagnostics retire inherited configuration; see common-unordered-native-configuration.md.
Concrete immediate stop is subsequently implemented in common-unordered-cooperative-stop.md;
shared/group immediate API and private queue costs remain open.

## Original pinned comment provenance

The following comments are copied from pinned Git objects, in source order, with
original source lines. They document removed Java APIs and are not native API docs.

### DefaultPromise.java

Upstream: common/src/main/java/io/netty/util/concurrent/DefaultPromise.java

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

Original line 38:

```java
/**
     * System property with integer type value, that determine the max reentrancy/recursion level for when
     * listener notifications prompt other listeners to be notified.
     * <p>
     * When the reentrancy/recursion level becomes greater than this number, a new task will instead be scheduled
     * on the event loop, to finish notifying any subsequent listners.
     * <p>
     * The default value is {@code 8}.
     */
```

Original line 66:

```java
/**
     * One or more listeners. Can be a {@link GenericFutureListener} or a {@link DefaultFutureListeners}.
     * If {@code null}, it means either 1) no listeners were added yet or 2) all listeners were notified.
     * <p>
     * Threading - synchronized(this). We must support adding listeners when there is no EventExecutor.
     */
```

Original line 74:

```java
/**
     * Threading - synchronized(this). We are required to hold the monitor to use Java's underlying wait()/notifyAll().
     */
```

Original line 79:

```java
/**
     * Threading - synchronized(this). We must prevent concurrent notification and FIFO listener notification if the
     * executor changes.
     */
```

Original line 85:

```java
/**
     * Creates a new instance.
     * <p>
     * It is preferable to use {@link EventExecutor#newPromise()} to create a new promise
     *
     * @param executor
     *        the {@link EventExecutor} which is used to notify the promise once it is complete.
     *        It is assumed this executor will protect against {@link StackOverflowError} exceptions.
     *        The executor may be used to avoid {@link StackOverflowError} by executing a {@link Runnable} if the stack
     *        depth exceeds a threshold.
     *
     */
```

Original line 101:

```java
/**
     * See {@link #executor()} for expectations of the executor.
     */
```

Original line 105:

```java
// only for subclasses
```

Original line 158:

```java
// Suppress a warning since the method doesn't need synchronization
```

Original line 292:

```java
// Interrupted while waiting.
```

Original line 322:

```java
// Should not be raised at all.
```

Original line 332:

```java
// Should not be raised at all.
```

Original line 391:

```java
/**
     * {@inheritDoc}
     *
     * @param mayInterruptIfRunning this value has no effect in this implementation.
     */
```

Original line 462:

```java
/**
     * Get the executor used to notify listeners when this promise is complete.
     * <p>
     * It is assumed this executor will protect against {@link StackOverflowError} exceptions.
     * The executor may be used to avoid {@link StackOverflowError} by executing a {@link Runnable} if the stack
     * depth exceeds a threshold.
     * @return The executor used to notify listeners when this promise is complete.
     */
```

Original line 481:

```java
/**
     * Notify a listener that a future has completed.
     * <p>
     * This method has a fixed depth of {@link #MAX_LISTENER_STACK_DEPTH} that will limit recursion to prevent
     * {@link StackOverflowError} and will stop notifying listeners added after this threshold is exceeded.
     * @param eventExecutor the executor to use to notify the listener {@code listener}.
     * @param future the future that is complete.
     * @param listener the listener to notify.
     */
```

Original line 522:

```java
/**
     * The logic in this method should be identical to {@link #notifyListeners()} but
     * cannot share code because the listener(s) cannot be cached for an instance of {@link DefaultPromise} since the
     * listener(s) may be changed and is protected by a synchronized operation.
     */
```

Original line 558:

```java
// Only proceed if there are listeners to notify and we are not already notifying listeners.
```

Original line 577:

```java
// Nothing can throw from within this method, so setting notifyingListeners back to false does not
```

Original line 578:

```java
// need to be in a finally block.
```

Original line 631:

```java
// Removal is rare, no need for compaction
```

Original line 657:

```java
/**
     * Check if there are any waiters and if so notify these.
     * @return {@code true} if there are any listeners attached to the promise, {@code false} otherwise.
     */
```

Original line 706:

```java
// Start counting time from here instead of the first line of this method,
```

Original line 707:

```java
// to avoid/postpone performance cost of System.nanoTime().
```

Original line 726:

```java
// Check isDone() in advance, try to avoid calculating the elapsed time later.
```

Original line 730:

```java
// Calculate the elapsed time here instead of in the while condition,
```

Original line 731:

```java
// try to avoid performance cost of System.nanoTime() in the first loop of while.
```

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

Original line 891:

```java
// Override fillInStackTrace() so we not populate the backtrace via a native call and so leak the
```

Original line 892:

```java
// Classloader.
```


### AbstractFuture.java

Upstream: common/src/main/java/io/netty/util/concurrent/AbstractFuture.java

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

Original line 24:

```java
/**
 * Abstract {@link Future} implementation which does not allow for cancellation.
 *
 * @param <V>
 */
```


### DefaultFutureListeners.java

Upstream: common/src/main/java/io/netty/util/concurrent/DefaultFutureListeners.java

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

Original line 24:

```java
// the number of progressive listeners
```


### Future.java

Upstream: common/src/main/java/io/netty/util/concurrent/Future.java

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

Original line 22:

```java
/**
 * The result of an asynchronous operation.
 */
```

Original line 28:

```java
/**
     * Returns {@code true} if and only if the I/O operation was completed
     * successfully.
     */
```

Original line 34:

```java
/**
     * returns {@code true} if and only if the operation can be cancelled via {@link #cancel(boolean)}.
     */
```

Original line 39:

```java
/**
     * Returns the cause of the failed I/O operation if the I/O operation has
     * failed.
     *
     * @return the cause of the failure.
     *         {@code null} if succeeded or this future is not
     *         completed yet.
     */
```

Original line 49:

```java
/**
     * Adds the specified listener to this future.  The
     * specified listener is notified when this future is
     * {@linkplain #isDone() done}.  If this future is already
     * completed, the specified listener is notified immediately.
     */
```

Original line 57:

```java
/**
     * Adds the specified listeners to this future.  The
     * specified listeners are notified when this future is
     * {@linkplain #isDone() done}.  If this future is already
     * completed, the specified listeners are notified immediately.
     */
```

Original line 65:

```java
/**
     * Removes the first occurrence of the specified listener from this future.
     * The specified listener is no longer notified when this
     * future is {@linkplain #isDone() done}.  If the specified
     * listener is not associated with this future, this method
     * does nothing and returns silently.
     */
```

Original line 74:

```java
/**
     * Removes the first occurrence for each of the listeners from this future.
     * The specified listeners are no longer notified when this
     * future is {@linkplain #isDone() done}.  If the specified
     * listeners are not associated with this future, this method
     * does nothing and returns silently.
     */
```

Original line 83:

```java
/**
     * Waits for this future until it is done, and rethrows the cause of the failure if this future
     * failed.
     */
```

Original line 89:

```java
/**
     * Waits for this future until it is done, and rethrows the cause of the failure if this future
     * failed.
     */
```

Original line 95:

```java
/**
     * Waits for this future to be completed.
     *
     * @throws InterruptedException
     *         if the current thread was interrupted
     */
```

Original line 103:

```java
/**
     * Waits for this future to be completed without
     * interruption.  This method catches an {@link InterruptedException} and
     * discards it silently.
     */
```

Original line 110:

```java
/**
     * Waits for this future to be completed within the
     * specified time limit.
     *
     * @return {@code true} if and only if the future was completed within
     *         the specified time limit
     *
     * @throws InterruptedException
     *         if the current thread was interrupted
     */
```

Original line 122:

```java
/**
     * Waits for this future to be completed within the
     * specified time limit.
     *
     * @return {@code true} if and only if the future was completed within
     *         the specified time limit
     *
     * @throws InterruptedException
     *         if the current thread was interrupted
     */
```

Original line 134:

```java
/**
     * Waits for this future to be completed within the
     * specified time limit without interruption.  This method catches an
     * {@link InterruptedException} and discards it silently.
     *
     * @return {@code true} if and only if the future was completed within
     *         the specified time limit
     */
```

Original line 144:

```java
/**
     * Waits for this future to be completed within the
     * specified time limit without interruption.  This method catches an
     * {@link InterruptedException} and discards it silently.
     *
     * @return {@code true} if and only if the future was completed within
     *         the specified time limit
     */
```

Original line 154:

```java
/**
     * Return the result without blocking. If the future is not done yet this will return {@code null}.
     * <p>
     * As it is possible that a {@code null} value is used to mark the future as successful you also need to check
     * if the future is really done with {@link #isDone()} and not rely on the returned {@code null} value.
     */
```

Original line 162:

```java
/**
     * {@inheritDoc}
     *
     * If the cancellation was successful it will fail the future with a {@link CancellationException}.
     */
```


### Promise.java

Upstream: common/src/main/java/io/netty/util/concurrent/Promise.java

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
 * Special {@link Future} which is writable.
 */
```

Original line 23:

```java
/**
     * Marks this future as a success and notifies all
     * listeners.
     *
     * If it is success or failed already it will throw an {@link IllegalStateException}.
     */
```

Original line 31:

```java
/**
     * Marks this future as a success and notifies all
     * listeners.
     *
     * @return {@code true} if and only if successfully marked this future as
     *         a success. Otherwise {@code false} because this future is
     *         already marked as either a success or a failure.
     */
```

Original line 41:

```java
/**
     * Marks this future as a failure and notifies all
     * listeners.
     *
     * If it is success or failed already it will throw an {@link IllegalStateException}.
     */
```

Original line 49:

```java
/**
     * Marks this future as a failure and notifies all
     * listeners.
     *
     * @return {@code true} if and only if successfully marked this future as
     *         a failure. Otherwise {@code false} because this future is
     *         already marked as either a success or a failure.
     */
```

Original line 59:

```java
/**
     * Make this future impossible to cancel.
     *
     * @return {@code true} if and only if successfully marked this future as uncancellable or it is already done
     *         without being cancelled.  {@code false} if this future has been cancelled already.
     */
```


### FutureListener.java

Upstream: common/src/main/java/io/netty/util/concurrent/FutureListener.java

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
 * A subtype of {@link GenericFutureListener} that hides type parameter for convenience.
 * <pre>
 * Future f = new DefaultPromise(..);
 * f.addListener(new FutureListener() {
 *     public void operationComplete(Future f) { .. }
 * });
 * </pre>
 */
```


### GenericFutureListener.java

Upstream: common/src/main/java/io/netty/util/concurrent/GenericFutureListener.java

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
 * Listens to the result of a {@link Future}.  The result of the asynchronous operation is notified once this listener
 * is added by calling {@link Future#addListener(GenericFutureListener)}.
 */
```

Original line 26:

```java
/**
     * Invoked when the operation associated with the {@link Future} has been completed.
     *
     * @param future  the source {@link Future} which called this callback
     */
```


### BlockingOperationException.java

Upstream: common/src/main/java/io/netty/util/concurrent/BlockingOperationException.java

Original line 1:

```java
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
```

Original line 18:

```java
/**
 * An {@link IllegalStateException} which is raised when a user performed a blocking operation
 * when the user is in an event loop thread.  If a blocking operation is performed in an event loop
 * thread, the blocking operation will most likely enter a dead lock state, hence throwing this
 * exception.
 */
```

The subsequent CLR worker replacement failure boundary is implemented in
[common-unordered-worker-failure.md](common-unordered-worker-failure.md).
