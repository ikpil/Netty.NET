# Object collection cleanup: CLR runtime replacement

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in the clean `../netty` checkout.

## Original purpose, consumers and defect

`common/.../internal/ObjectCleaner.java` is deprecated for removal. The entire
pinned source tree has no production registration call; its three own tests
exercise an unreachable completed thread, throwing callbacks, and a daemon
worker. FastThreadLocalTest imports it without a registration call. Deterministic
CLR resource owners should use IDisposable/SafeHandle; the remaining fallback
purpose is an arbitrary-object collection notification with failure isolation.

The former C# LIVE_SET strongly retained AutomaticCleanerReference objects,
while WeakReferenceQueueEntry tried to enqueue a target from the registration
object finalizer. The registration could not become collectible while the live
set retained it. Its finalizer also attempted to queue the target rather than the
registration, after its short weak reference could already be cleared. The two
original cleanup scenarios timed out in both previous full default suites.

The CLR implementation now uses the existing CollectedObjectWatch primitive,
which ResourceLeakDetector also consumes. ConditionalWeakTable ties a finalizable
value to the target by reference identity without a global callback live set.
The internal notification only queues an Action to ThreadPool.UnsafeQueueUserWorkItem.
Normal background pool workers run user callbacks; callback failures are ignored
without logging, matching the original failure-isolation policy. Interlocked and
Volatile maintain a diagnostic pending count without retaining registrations.

## API and explicit semantic decisions

`ObjectCleaner.Register(object, Action)` and `PendingCount` are the native API.
The upstream deprecation is retained as Obsolete. IRunnable registration, named
worker access, Java atomic wrappers and live-set inspection are removed. No
production C# caller requires an adapter. AutomaticCleanerReference,
WeakReferenceQueue and WeakReferenceQueueEntry have no remaining consumer and
are removed rather than exposing a fake Java reference queue.

Each registration is invoked at most once, only after its target loses external
reachability. Multiple registrations for one target remain independent. Callbacks
stay reachable until invocation and are then released. A callback may reference
its conditional key without globally rooting it. No registrar ExecutionContext
is captured or implicitly flowed. Callbacks have no executor affinity and may
run concurrently; no production pinned consumer requires the old single-worker
ordering. Register rejects null targets and callbacks synchronously.

PendingCount includes work waiting for collection and queued/running callbacks;
it decreases on completion or dispatch failure. It is a diagnostic count, not a
completion handle or a resource ownership API. GC timing and process shutdown
provide no deterministic cleanup deadline. Explicit resource ownership remains
the primary model; the fallback does not replace retain/release or buffer leases.

## Original test mapping and CLR verification

All three original methods remain in ObjectCleanerTest, with all six original
comments. Java atomic wrappers become Interlocked/Volatile; the completion-thread
scenario still retains a Thread until it joins, then clears the owner reference.
Throwing cleanup still validates both invocations. The daemon test now collects
the target and inspects the actual callback worker, asserting both IsBackground
and IsThreadPoolThread rather than manufacturing a named Java polling thread.

Eight CLR regressions verify invalid inputs, collection/at-most-once behavior,
conditional callback-to-key cycles, cleanup payload lifetime, reference identity,
64 concurrent registrations, execution-context isolation, and a blocked callback
that cannot block finalizers or other cleanup. GC helpers use bounded asynchronous
waits and short-lived setup frames. The tests share one collection to make the
utility pending-count assertions independent of other registration tests.

The targeted Debug selection passes all 11 cases. All 11 also pass in both default
Debug/Release runs: the cleanup checkpoint has 1050 passed / 7 failed / 14 skipped;
the later native runtime checkpoint has 1068 passed / 5 failed / 14 skipped.
Current results and remaining platform failures are recorded in common-porting.md.
These tests are included in the default suite. The temporary PortingBatch
selection has been removed; the batch results above are historical records.

## Original comment provenance

The license, utility/registration documentation and callback failure comments are
preserved beside the native implementation. The original JVM queue/worker,
interrupt and ClassLoader commentary describes runtime mechanisms that are now
replaced, so it is preserved below with source line locations rather than attached
to invented CLR worker state. The manifest records this as a CLR runtime
replacement. Preserving this provenance is separate from functional verification.

Mapping of replaced mechanisms:

| Original commentary | CLR decision |
| --- | --- |
| LIVE_SET, registration roots and private reference helper | Conditional values retain callbacks only with their target; no global set retains keys or callback-to-key cycles. |
| ReferenceQueue polling, worker start/stop races and atomics | CLR GC notification and the CLR pool work queue own these mechanisms. The utility only counts pending work. |
| Interrupt consumption/restoration | No dedicated poll loop or interrupt protocol is retained. Cooperative cancellation belongs to explicit owned operations. |
| Context ClassLoader reset | No JVM ClassLoader API; no registrar ExecutionContext capture and unsafe pool dispatch avoid implicit context retention. |
| Low priority, daemon and named thread | Shared CLR background pool workers; worker properties are verified from the actual callback. |
| Logging avoidance after callback failure | Preserved directly in the native callback catch block. |
| Static-only utility and private helper clear/get | Native static utility; conditional registrations are internal and expose no Java weak-reference API. |

CLR specifications: [conditional lifetime and reference identity](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2?view=net-10.0),
[pool dispatch without execution-context flow](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool.unsafequeueuserworkitem?view=net-10.0).

## Current whole-suite evidence

The later ordered-multimap checkpoint runs all eleven cleanup cases in the
default Debug and Release suites: each run has 1161 passed / 0 failed / 14 skipped.
Earlier failure counts above are historical checkpoints. See common-porting.md.

## Pinned original comments

Source line 1

```java
/*
 * Copyright 2017 The Netty Project
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

Source line 31

```java
/**
 * Allows a way to register some {@link Runnable} that will executed once there are no references to an {@link Object}
 * anymore.
 *
 * @deprecated The object cleaner is deprecated for removal.
 */
```

Source line 42

```java
// Package-private for testing
```

Source line 44

```java
// This will hold a reference to the AutomaticCleanerReference which will be removed once we called cleanup()
```

Source line 53

```java
// Keep on processing as long as the LIVE_SET is not empty and once it becomes empty
```

Source line 54

```java
// See if we can let this thread complete.
```

Source line 60

```java
// Just consume and move on
```

Source line 68

```java
// ignore exceptions, and don't log in case the logger throws an exception, blocks, or has
```

Source line 69

```java
// other unexpected side effects.
```

Source line 76

```java
// Its important to first access the LIVE_SET and then CLEANER_RUNNING to ensure correct
```

Source line 77

```java
// behavior in multi-threaded environments.
```

Source line 79

```java
// There was nothing added after we set STARTED to false or some other cleanup Thread
```

Source line 80

```java
// was started already so its safe to let this Thread complete now.
```

Source line 85

```java
// As we caught the InterruptedException above we should mark the Thread as interrupted.
```

Source line 91

```java
/**
     * Register the given {@link Object} for which the {@link Runnable} will be executed once there are no references
     * to the object anymore.
     *
     * This should only be used if there are no other ways to execute some cleanup once the Object is not reachable
     * anymore because it is not a cheap way to handle the cleanup.
     */
```

Source line 101

```java
// Its important to add the reference to the LIVE_SET before we access CLEANER_RUNNING to ensure correct
```

Source line 102

```java
// behavior in multi-threaded environments.
```

Source line 105

```java
// Check if there is already a cleaner running.
```

Source line 109

```java
// Set to null to ensure we not create classloader leaks by holding a strong reference to the inherited
```

Source line 110

```java
// classloader.
```

Source line 111

```java
// See:
```

Source line 112

```java
// - https://github.com/netty/netty/issues/7290
```

Source line 113

```java
// - https://bugs.openjdk.java.net/browse/JDK-7008595
```

Source line 123

```java
// Mark this as a daemon thread to ensure that we the JVM can exit if this is the only thread that is
```

Source line 124

```java
// running.
```

Source line 135

```java
// Only contains a static method.
```
