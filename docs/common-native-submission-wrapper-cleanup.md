# Native submission owns invocation without PromiseTask

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and tests; other modules are read only.

## Source evidence and decision

The pinned PromiseTask.java is package-private. An all-module search finds its
construction only in AbstractEventExecutor.newTaskFor and inheritance in the
ordered ScheduledFutureTask and unordered RunnableScheduledFutureTask decorators.
No original test directly constructs PromiseTask. Those submission/bulk/scheduling
paths have already migrated to native delegates and Task results; their Java
facades and result wrappers are removed. A complete C# source/test search finds
no remaining PromiseTask/IRunnableFuture caller or subclass.

The source requirements still apply: claim invocation once, prevent pre-start
canceled work, protect producer completion from consumer mutation, publish exact
failure/value, and release callback references after ownership ends. Native
EventExecutorExtensions.SubmittedTask and NativeScheduledWork implement those
boundaries with a sole private TCS result. One-shot cancellation after a claim is
cooperative; normal return retains success. Periodic scheduling owns a reservation
lifetime separately from one invocation. Queue membership is independent of Task
identity. ExecutionContext and asynchronous delegate completion have their native
policies, rather than another Future result hierarchy.

The unused PromiseTask class and IRunnableFuture interface are removed. Native
submission/scheduling work remain private implementation details. Standard Action,
Func<T> and token/Task delegate forms replace erased Runnable/Callable object storage
and the explicit-result RunnableAdapter. Task results have no public setSuccess,
trySuccess or setUncancellable methods; externally completed operations receive a
producer-owned TCS instead. PromiseTask's identity/diagnostic sentinel text is not
a required native consumer API. Its seven original comments are preserved below,
including license, sentinel and cancellation-race explanation.

The same search exposes unused CLR-only CallableAdapter, AnonymousCallable,
ICallable and the three Queueing*TaskNode types. They have no standalone Netty
source counterpart and no remaining C# source/test consumer. Their Java framework
Callable role is already served by native Func delegates; queueing submission
uses the actual private native claim runner. They are removed as orphaned glue,
including the alternative TCS wrapper with an unsynchronized cancellation flag.
The native code does not adopt its duplicate runner or completion state.

This completes the mapped PromiseTask invocation/result design review, not common
or final executor backend review. The subsequent native fixture migration removes
DefaultPromise, AbstractFuture and their listener/waiting facades; see
common-native-future-retirement.md. Pool configuration/public queue/shutdown, other pending source decisions
and measured hot-path performance remain separate required work. No performance
improvement is claimed from deletion alone.

## Verification

The existing native consumer and executor tests exercise actual Task API outcomes,
claims, cancellation, typed/null values, failure identity, context and reference
lifetime. No test is added just to mirror an unreachable class deletion.
Affected Debug selection: 195 passed, zero failed, zero skipped, recorded in
native-submission-wrapper-cleanup-contracts-debug.trx. Default Windows/net10.0
Debug and Release each discover 1357 cases: 1343 passed, zero failed, 14 skipped,
recorded in native-submission-wrapper-cleanup-full-debug.trx and
native-submission-wrapper-cleanup-full-release.trx in ignored TestResults.
All discovered identities, 759 non-Porting fixtures and 14 skip identities match
the preceding native unordered scheduling checkpoint in each configuration.
The identity comparison and comment audit are recorded in the corresponding
native-submission-wrapper-cleanup JSON artifacts. All 104 verified source/test
entries have zero missing comments, including seven archived PromiseTask comments;
all manifest implementation/candidate paths exist. git diff --check passes.
Original fixture scenarios remain; none depends on these removed wrappers.

## Original pinned comment provenance

Comments below are copied verbatim from PromiseTask.java at the pinned Git object,
in source order. They document the removed Java helper and stay separate from
the native delegate API's documentation.

Upstream: common/src/main/java/io/netty/util/concurrent/PromiseTask.java

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

```java
// no-op
```

```java
// Strictly of type Callable<V> or Runnable
```

```java
// The only time where it might be possible for the sentinel task
```

```java
// to be called is in the case of a periodic ScheduledFutureTask,
```

```java
// in which case it's a benign race with cancellation and the (null)
```

```java
// return value is not used.
```
