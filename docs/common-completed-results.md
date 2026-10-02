# Completed results use standard Tasks

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.

## Original requirements and consumers

CompleteFuture supplies already-terminal state, executor-bound listener dispatch,
non-cancellable results and immediate wait completion. SucceededFuture stores the
value; FailedFuture stores the exact cause and rethrows it from sync. Java get
wraps a failure in ExecutionException. CompleteFuture's interruptible await
consumes a pending Java thread interrupt even though there is no wait, whereas
DefaultPromise's completed await preserves it. Listener array traversal stops at
the first null entry, and completed-future listener removal is a no-op.

Actual consumers extend beyond the common factory methods:

- resolver AbstractAddressResolver.resolve/resolveAll return an already-resolved
  address or singleton list, or a completed failure for unsupported input and
  lookup exceptions. No lookup is needed on the success fast path.
- resolver-dns DnsNameResolver.query returns a completed failure after channel
  acquisition failure. DnsResolveContext.queryUnresolvedNameServer adds a newly
  constructed, successful-null Future to queriesInProgress as a unique marker;
  an auxiliary resolver listener removes that marker before continuing queries.
- transport CompleteChannelFuture inherits CompleteFuture but additionally owns
  the Channel identity and falls back to channel.eventLoop() when no executor
  was supplied. Owner metadata is a consumer requirement, not terminal state.
- ImmediateEventExecutor and GlobalEventExecutor originally hold a FailedFuture
  for unsupported termination. Their native Termination Tasks already replace
  that use. SNI and QUIC signing consumers also return completed results/failures.

These paths were read at the pinned local baseline. Resolver, DNS, transport and
TLS/QUIC implementations are not ported as part of this common change.

## CLR decisions

Use Task.FromResult<T>, Task.FromException<T> and Task.CompletedTask. A consumer
receives a read-only terminal result. The Task is the only terminal state; no
Netty completed-result class, executor factory, or Future-to-Task adapter is
needed. CompleteFuture, SucceededFuture, FailedFuture (including its unused CLR
static bridge) and newSucceededFuture/newFailedFuture on IEventExecutor and its
implementations are removed. Subsequent checkpoints also removed
newProgressivePromise and newPromise; see common-progress-subscriptions.md and
common-native-producers.md. Legacy scheduling remains migration work,
not a completed redesign.

Task.Status/IsCompletedSuccessfully/IsFaulted/IsCanceled replace status methods.
Await/GetAwaiter().GetResult preserve the original exception object; synchronous
Result/Wait use AggregateException. GetResult is safe in a completion callback
because its source is already complete; do not block an executor on pending work.
Task.FromException requires a non-null exception and represents failure, even
for an OperationCanceledException; actual cancellation requires a canceled Task.
A pre-canceled WaitAsync observer cannot change an already-completed producer
result; the completed Task is returned. There is no cancel(bool) result mutation
API on Task, and no Java interrupt-consumption step is added to completed waits.
The native completed-Task regression observes the pending interrupt on the next
actual CLR Thread.Sleep, for both FromResult and a completed TCS producer.

ExecutorCompletion supplies the remaining callback policy. Callbacks execute
inline on their executor when allowed, otherwise dispatch there; source completion
and notification completion remain distinct. Native consumers register each
callback explicitly, reject null callbacks and retain unique disposable handles.
Java null-terminated varargs/fluent returns are not emulated. Removing a pending
registration can prevent its invocation, while disposing an already-claimed
registration does not undo a callback. Existing callback order, recursion,
isolation, queue-removal and lifetime tests continue to cover this dispatcher.
The original CompleteFuture dispatches each listener separately: an unordered
executor can overlap them. The native policy serializes callbacks per observation
even there, consistently with the already-reviewed ExecutorCompletion contract.
Ordered-executor admission order remains preserved. This stronger serialization
is explicit, not a claim that the original unordered dispatch already had it.

Task.CompletedTask and Task.FromResult can share cached instances. Task identity
must not be used as the unique DNS placeholder identity: use a separately owned
reservation token in the executor-owned query registry. The completion source
for an auxiliary lookup still belongs to that operation. Do not allocate a fake
TCS or result wrapper merely to produce distinct completed Tasks. Real query
cancellation and owner lifetime must be tracked separately by the future resolver
port; a completed marker is not a cancelable lookup.

Likewise, a channel owner and its event loop belong to the channel/consumer.
Different channels can use the same Task.CompletedTask result while maintaining
separate owner metadata and ExecutorCompletion dispatch targets. Task subclassing
is not required to attach channel metadata. This does not decide the full public
Channel API, registration transitions or disconnect policy in transport.

## Verification

The four existing completed-result rows in PromiseContractTest are migrated to
native Task semantics. They cover success/failure identity, observer cancellation,
native completed waits preserving thread interrupts, callback argument validation
and explicit registration/disposal. The original CompleteFuture-only interrupt
consumption and null-sentinel expectations are replaced by the CLR policies
above; this is an explicit adaptation, not a claim of Java byte-for-byte behavior.
No dedicated common test for these three completed-future classes exists at the
baseline; the remaining original DefaultPromise scenarios remain enabled.

CompletedResultConsumerContractTest adds six consumer cases using actual
DefaultEventExecutor workers: four resolved-address/failure × on/off-loop
registration combinations, two independent DNS reservations sharing one marker
Task, and two channel-like owners observing the same completed Task on separate
loops. The marker case forces sharing and does not rely on a particular runtime
cache optimization. These are common API consumer models, not networking module
implementations. Callback outcomes are checked outside isolated callbacks.

The affected Debug and Release selections each pass 59 cases: PromiseContractTest,
ExecutorCompletionContractTest and CompletedResultConsumerContractTest.
Evidence: completed-result-final-contracts-debug.trx and
completed-result-final-contracts-release.trx. Default full Debug/Release each
discover 1317 cases: 1303 passed, zero failed, 14 skipped on Windows/net10.0.
Evidence: completed-result-full-debug.trx and completed-result-full-release.trx.
All 109 remaining verified source/test entries have zero missing comments; the
three replaced classes' 13 comments and both removed factory comments also have
zero missing original provenance. No performance improvement is claimed.

## Original comment provenance

The following license, API and implementation comments are copied from pinned
Git objects. Removed JVM waiting/class-hierarchy comments are reference material;
the native terminal result and callback policies are described above. Two removed
EventExecutor factory comments also remain here; its other comments stay beside
the retained interface members.

### CompleteFuture.java

Upstream: common/src/main/java/io/netty/util/concurrent/CompleteFuture.java

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
 * A skeletal {@link Future} implementation which represents a {@link Future} which has been completed already.
 */
```

Original line 30:

```java
/**
     * Creates a new instance.
     *
     * @param executor the {@link EventExecutor} associated with this future
     */
```

Original line 39:

```java
/**
     * Return the {@link EventExecutor} which is used by this {@link CompleteFuture}.
     */
```

Original line 67:

```java
// NOOP
```

Original line 73:

```java
// NOOP
```

Original line 141:

```java
/**
     * {@inheritDoc}
     *
     * @param mayInterruptIfRunning this value has no effect in this implementation.
     */
```


### SucceededFuture.java

Upstream: common/src/main/java/io/netty/util/concurrent/SucceededFuture.java

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
 * The {@link CompleteFuture} which is succeeded already.  It is
 * recommended to use {@link EventExecutor#newSucceededFuture(Object)} instead of
 * calling the constructor of this future.
 */
```

Original line 26:

```java
/**
     * Creates a new instance.
     *
     * @param executor the {@link EventExecutor} associated with this future
     */
```


### FailedFuture.java

Upstream: common/src/main/java/io/netty/util/concurrent/FailedFuture.java

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

Original line 21:

```java
/**
 * The {@link CompleteFuture} which is failed already.  It is
 * recommended to use {@link EventExecutor#newFailedFuture(Throwable)}
 * instead of calling the constructor of this future.
 */
```

Original line 30:

```java
/**
     * Creates a new instance.
     *
     * @param executor the {@link EventExecutor} associated with this future
     * @param cause   the cause of failure
     */
```


### EventExecutor.java

Upstream: common/src/main/java/io/netty/util/concurrent/EventExecutor.java

Original line 66:

```java
/**
     * Create a new {@link Future} which is marked as succeeded already. So {@link Future#isSuccess()}
     * will return {@code true}. All {@link FutureListener} added to it will be notified directly. Also
     * every call of blocking methods will just return without blocking.
     */
```

Original line 75:

```java
/**
     * Create a new {@link Future} which is marked as failed already. So {@link Future#isSuccess()}
     * will return {@code false}. All {@link FutureListener} added to it will be notified directly. Also
     * every call of blocking methods will just return without blocking.
     */
```
