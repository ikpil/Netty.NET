# Netty common: CLR design decisions

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `../netty`.
Implementation scope: common and its tests. Other original modules are read to
establish consumers; their implementations are outside this stage.

The destination is a .NET Netty with usable native APIs and the required Netty
execution/resource contracts. A matching Java class hierarchy, a green translated
test, and a comment count are separate evidence; none establishes that destination.
Previously verified components must also pass this design review.

Task aggregation/transfer now uses CLR framework replacements, documented with
source consumers and all original test-method decisions in
[common-task-composition.md](common-task-composition.md). The four Java-shaped
helper classes have been removed rather than wrapped in another public facade.

## Actual CLR identity and ordered classifier state

Six fresh-load identity scenarios expose JVM os.name/os.arch environment keys
selecting another OS/architecture in the preceding port. Six classifier settings
and two malformed settings expose the uninitialized null cache: the first 16 new
cases have 14 failures and two passing filesystem cases. Fix the concrete runtime
identity/cache defects; do not re-review already-certified hash/memory owners.

OperatingSystem supplies actual Windows/macOS flags before temporary-directory
fallback uses them. Native OS naming and RuntimeInformation.ProcessArchitecture
feed the existing Netty artifact normalization; JVM identity overrides no longer
select CLR code paths or native artifact architecture. Add the CLR ARMv6 spelling
to the existing arm_32 artifact aliases. The Windows run exercises x64; other OS/
architecture branches are source decisions, not execution validation.

| Pinned purpose and actual consumers | CLR decision |
| --- | --- |
| PlatformDependent.java:261-267, 1747-1749; OpenSsl.java:747-768 | The original LinkedHashSet/unmodifiableSet is iterated to rank distro-specific native library candidates. Use a List builder with ordinal allowed-ID deduplication and an Array.AsReadOnly snapshot exposed as IReadOnlyList. Neither mutable cached state nor unspecified set iteration order satisfies this priority contract. |
| addPropertyOsClassifiers/processOsReleaseFile | Keep explicit empty suppression, at most two property fields, JVM trailing-comma behavior, quoted ID/ID_LIKE parsing, allowed fedora/suse/arch, ID then ID_LIKE priority and filesystem fallback only when the first file is absent. ICollection accepts native ordered builders and existing original set fixtures. CLR UnauthorizedAccessException maps the original best-effort Java IOException access-denial branch. |
| Cached classifier failure | Standard Lazy with ExecutionAndPublication initializes once on first classifier use and caches the same invalid-setting exception. Original Java fails whole PlatformDependent initialization; CLR confines that configuration failure to its cache, so unrelated OS/hash/memory operations remain available. Native original parser fixtures still run independently of cached preference initialization. |
| NetUtil.java:180-185, native/DNS platform users, transport-classes-epoll Native.java:326-331, Quiche.java:77-78, macOS DNS provider:85-89 | Runtime identity chooses the actual platform and existing native artifact identifiers. Keep NetUtil's current common use and reviewed normalized names; downstream loading/DNS/transport implementations remain open. |
| bitMode/bitMode0 and Unix Buffer.java:75-78 addressSize | No original production consumer calls bitMode. CLR process width is IntPtr.Size, borrowed views use nint and existing memory owners preserve pointer bits. Remove these duplicate public width facades, JVM guesses and cached fields; do not allow environment preferences to report another process layout. Future Unix I/O uses real native pointer layout. |
| maybeSuperUser/0; AbstractChannel.java:415-430, DefaultDatagramChannelConfig.java:157-173 | The two users gate advisory broadcast logging only, never admission or exception handling. JVM user.name heuristics do not establish effective CLR/OS privileges. Remove the unused current C# hint/default-false cache. Future transport must review actual broadcast warning policy separately; successful bind is not proof of broadcast receive permission. No replacement username privilege facade is added. |

Seven cached snapshots (including absent property), six identity cases, two
malformed preference cases and three real UTF-8 file cases verify the new boundary.
Priority/dedup, read-only exposure, stable snapshot after environment changes and
cache-local repeated exception identity are covered. File absence permits fallback;
an existing empty file completes selection. Synthetic files run on Windows without
changing OS release files. Unix permission-denial execution is not claimed.
The existing 14 runtime/classifier/address scenarios and original source/test
identities remain; current full/checked results are in common-porting.md.

Exact comments from retired heuristics follow. The stranded direct-memory-estimate
Javadoc is moved out of Tmpdir0 into its original provenance, without changing
temporary-file or allocator policy. Remaining platform APIs and actual downstream
module integration are still open.

Retired platform runtime/username heuristics: BitMode/AddressSize/MaybeSuperUser and their private helpers. Exact existing Java-derived and CLR comments follow.

```java
/**
     * Returns the bit mode of the current VM (usually 32 or 64.)
     */

// Check user-specified bit mode first.

// And then the vendor specific ones which is probably most reliable.

// os.arch also gives us a good hint.

// Last resort: guess from VM name and then fall back to most common 64-bit mode.

/**
     * Return the address size of the OS.
     * 4 (for 32 bits systems ) and 8 (for 64 bits systems).
     */

/**
     * Return {@code true} if the current user may be a super-user. Be aware that this is just an hint and so it may
     * return false-positives.
     */

// Check for root and toor as some BSDs have a toor user that is basically the same as root.
```

```csharp
// CLR adaptation: use the running process width rather than a JVM-name guess.

// CLR pointer width is available independently of JVM Unsafe.
```

Original estimateMaxDirectMemory Javadoc left above Tmpdir0 by an earlier retirement; archive it under its original purpose.

```java
    /**
     * Compute an estimate of the maximum amount of direct memory available to this JVM.
     * <p>
     * The computation is not cached, so you probably want to use {@link #maxDirectMemory()} instead.
     * <p>
     * This will produce debug log output when called.
     *
     * @return The estimated max direct memory, in bytes.
     */
```

## CLR operations replace JVM feature bootstrap

Remove the six remaining JVM feature facades (HasUnsafe, its unavailability
cause, virtual-thread, J9, IKVM and JFR detection), three private probes/four
fields, and PlatformDependent0.cs. Its static constructor contained only
commented JVM initialization; its provider diagnostics were consumed only by
translated CLR fixtures. That is a concrete remaining CLR API/design problem,
not a reason to re-review already-certified memory or executor implementations.

| Pinned consumer / purpose | Native decision and scope |
| --- | --- |
| PlatformDependent0.java:58-72/74-589, 631-695: Unsafe, JEP, Graal/provider/reflection bootstrap | NativeMemoryAllocator/Owner/View, typed counters, MemoryMarshal/BinaryPrimitives and ordinary reflection already provide their reviewed common purposes. JVM preferences do not gate these CLR operations. No general capability flag is inferred from one backend. |
| AsciiString.java:332, RefCnt.java:40/399, ReferenceCountUpdater.java:160, CleanerJava9.java:41 | Native byte access, Interlocked ref-int state and explicit memory ownership were migrated in prior units. Retain those implementations/tests; no Unsafe selection remains. |
| PlatformDependent0.java:1131-1151 and 1212-1230 | Move the exact wrapping hash arithmetic/constants and OperatingSystem Android probe privately into PlatformDependent. Preserve all adjacent explanations, masks, byte order and overload behavior; expose no helper provider class. |
| Recycler.java:590-593, PlatformDependent.java:1493-1512 | Existing CLR terminated/unstarted-thread handling owns recycler lifetime; the J9 performance workaround and IKVM VM-name diagnostic are JVM-specific. Their comments remain. |
| VirtualThreadCheckTest.java and PlatformDependent.java:441-442 | The provider probe has no production Java consumer beyond itself, and CLR ordinary/thread-pool workers are not Java virtual threads. Keep the prior explicit test exclusion; do not export an always-false API. |
| PlatformDependent.java:255-284/1914-1918, buffer AdaptivePoolingAllocator.java:1618/1687/2177/2220/2659 and PooledByteBufAllocator.java:803-848 | JFR event-provider selection is JVM-only. Allocation/lifetime observation needs the chosen CLR diagnostic backend during buffer integration; deleting this flag does not certify that work. |
| buffer Unpooled.java:194, Unix Buffer.java:75, NioIoHandler.java:189, ReferenceCountedOpenSslEngine.java:2406 and OpenSslX509TrustManagerWrapper.java:60 | Native buffer byte/address access, I/O layout, selector field access and TLS trust wrapping need each CLR module/backend's real operations and failure handling. Earlier native-field/memory reviews cover common; downstream modules remain open. Returning false for JVM Unsafe would wrongly make it a CLR memory/backend gate. |

The historical diagnostic preference in common-platform-runtime.md is now
retired. Existing fresh-load fixture identities remain, but the two preference
cases now allocate/write/read/release real native owners and compare the eight-
byte hash with the existing independently generated Java oracle. Explicit
noUnsafe=true, tryUnsafe=false and legacy JBoss=false settings all retain native
operations. Shared io.netty.maxDirectMemory remains meaningful and its existing
native reservation-domain fixture is unchanged. Android and nonpublic member
access are still tested against the actual CLR OS/reflection behavior.

The original platform test identities/scenarios and the complete hash-tail Java
oracle remain. Removing a nonportable provider assertion is recorded explicitly;
native operation success replaces it, not another constant capability result.
Current full/checked results and comment/inventory checks are in common-porting.md.
Class-level original-source status stays in-progress until remaining native/module
and platform decisions are reconciled; deleting a helper class is not completion.
The eleven remaining exact pinned PlatformDependent0 comments below supplement
prior provenance, so all 81 are accounted for without copying existing archives.
Commented placeholder implementation statements are discarded.

### Retired JVM feature bootstrap comments

The following exact pinned PlatformDependent0 comments complete the archived
provenance not already present in surviving implementations and prior design
records. They describe the removed JVM bootstrap, not CLR capabilities.

```java
/**
 * The {@link PlatformDependent} operations which requires access to {@code sun.misc.*}.
 */

// See https://github.com/oracle/graal/blob/master/sdk/src/org.graalvm.nativeimage/src/org/graalvm/nativeimage/

// ImageInfo.java

// Package-private for testing.

// Call once to make sure the invocation works.

/**
     * @param thread The thread to be checked.
     * @return {@code true} if this {@link Thread} is a virtual thread, {@code false} otherwise.
     */

// See JDK 23 JEP 471 https://openjdk.org/jeps/471 and sun.misc.Unsafe.beforeMemoryAccess() on JDK 23+.

// And JDK 24 JEP 498 https://openjdk.org/jeps/498, that enable warnings by default.

// Due to JDK bugs, we only actually disable Unsafe by default on Java 25+, where we have memory segment APIs

// available, and working.

// Legacy properties
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, six retired feature facade Javadocs.

```java
/**
     * @param thread The thread to be checked.
     * @return {@code true} if this {@link Thread} is a virtual thread, {@code false} otherwise.
     */

/**
     * Return {@code true} if {@code sun.misc.Unsafe} was found on the classpath and can be used for accelerated
     * direct memory access.
     */

/**
     * Return the reason (if any) why {@code sun.misc.Unsafe} was not available.
     */

/**
     * Returns {@code true} if the running JVM is either <a href="https://developer.ibm.com/javasdk/">IBM J9</a> or
     * <a href="https://www.eclipse.org/openj9/">Eclipse OpenJ9</a>, {@code false} otherwise.
     */

/**
     * Returns {@code true} if the running JVM is <a href="https://www.ikvm.net">IKVM.NET</a>, {@code false} otherwise.
     */

/**
     * Check if JFR events are supported on this platform.
     */

// Probably failed to initialize PlatformDependent0.
```

Retired CLR diagnostic explanations, preserved as historical port provenance.

```csharp
// CLR adaptation: JVM Unsafe is unavailable; native CLR operations are ported explicitly.

// CLR adaptation: Graal native-image properties do not describe this runtime.

// CLR adaptation: JEP 471/498 and JDK 25 do not select CLR features.
// Only the explicit Netty preference is relevant to this diagnostic.

// CLR thread-pool workers are native threads, not Java virtual threads.

// CLR adaptation: native pointer width is available without sun.misc.Unsafe.
```

## Native MPSC handoffs and removed provider scaffolding

The pinned three newMpscQueue overloads and nested Mpsc in PlatformDependent.java:
1245-1328 have these real consumers:

| Consumer | Queue purpose | Native common decision or later module integration |
| --- | --- | --- |
| HashedWheelTimer.java:114-115 | Unbounded timeout/cancellation publication, worker drain and stop ownership | Keep its existing two ConcurrentQueue fields; protect publication/drain from CLR interruption as below. |
| NonStickyEventExecutorGroup.java:218 | FIFO tasks, serialized runner ownership, rejection recovery | Its existing native Queue plus owner gate already couples membership and runner reservations; no provider facade is used. |
| Recycler.java:549 | Explicit chunk/max configuration, bounded cross-thread returns, no eviction of accepted handles | Existing CAS-reserved ConcurrentQueue preserves the rounded maximum and rejection; chunk allocation differs in CLR. Previous bounded-pool tests remain unchanged. |
| transport/SingleThreadIoEventLoop.java:337-341 | Unbounded versus bounded event-loop admission | Future transport owner must choose effective capacity/rejection/wakeup/closure together. Original one-argument factory clamps to [2048, 2^30] then the JCTools queue rounds upward; explicit chunk overload has a separate constructor policy. Removing the common alias does not silently redefine a future caller's capacity. |
| transport/ManualIoEventLoop.java:61/178/476/534/539 | FIFO commands, wakeup sentinels, removal after closure | Future owner needs atomic admission/closure and withdrawal membership. ConcurrentQueue or bounded Channel alone does not supply arbitrary item removal; do not substitute a fake TryRemove. |
| transport-classes-epoll/AbstractEpollStreamChannel.java:851/855 | FIFO splice tasks, peek/head removal | Direct native FIFO collection with owner lifetime; epoll integration is outside common. |

All tracked C# callers of the remaining provider APIs were the provider definitions
themselves. Remove three PlatformDependent overloads, their three JCTools-specific
capacity constants and 13 unused implementation/scaffolding files: the JCTools
directory, ConcurrentCircularArrayQueue, ConcurrentQueueAdapter, AbstractQueue
and IntegerExtensions. Ordinary IQueue/LinkedBlockingQueue consumers and the
separately reviewed MpscAtomicIntegerArrayQueue remain. No replacement collection
class, JVM padding hierarchy, feature probe or advertised chunk-size facade is
introduced. This closes these common aliases, not future transport integration.

Review of the timer's actual existing unbounded queues reproduced two missing
handoffs at the preceding commit: ThreadInterruptedException from EnqueueSlow
after pending admission, or after cancellation's terminal CAS. In the latter
case the timeout is cancelled but its worker cleanup notification is missing.
Both new public NewTimeout/Cancel scenarios fail before repair. Reflection holds
the real net10 cross-segment gate only to control contention; timer APIs and IO
operations are unmodified. After repair, all 33 accepted timeouts cancel, every
cleanup callback runs, no task expires, pending count reaches zero and the caller
still observes a later interrupt. The worker is held by an ordinary timer task
during the handoff and released before callback/drain assertions.

The broader checked timer selection also exposes two existing maximum-delay
failures: deadline addition throws before the original overflow guard. Pinned
HashedWheelTimer.java:454-459 relies on Java long wrap followed by that guard.
Explicit unchecked addition/subtraction retains the same CLR default result and
allows the original and native saturation fixtures to pass in checked builds.
Keep both failed identities in mpsc-retirement-before-deadline-checked-release.trx.

A shared internal ConcurrentQueueOperations boundary now supplies only native
enqueue/try-dequeue with interruption retry/restoration. It is needed by two actual
resource owners, not an IQueue/factory wrapper: queues, admission limits and lifetime
remain owned by Recycler and timer. Move the preceding recycler boundary/comments
here without changing reservations or guard/batch behavior; apply it to both timer
writers and all three worker drains. Reviewed runtime v10.0.7 ConcurrentQueue and
ConcurrentQueueSegment throw on these waits before this item is published/claimed;
no user callback runs inside the retried operations. Retry only interruption,
restore it on completion/error, and retain ordinary failure propagation. Other
owner operations (Count, snapshots, arbitrary removal) are not certified by this
two-operation boundary. No claim of JVM lock-free progress or performance parity.

The original provider-selection fixture's six Java class identities remain an
explicit JVM-only assertion mapping; all existing portable identities/assertions
remain. Full results and inventory/semantic/comment checks are in common-porting.md
and mpsc-retirement-* records. Original ten comments of the removed pinned nested
class/overloads and existing C# explanatory comments follow. Commented placeholder
bootstrap/return statements are not explanatory provenance and are not archived.
Original licenses in surviving Java-derived source and the repository remain.

Runtime sources: [ConcurrentQueue](https://github.com/dotnet/runtime/blob/v10.0.7/src/libraries/System.Private.CoreLib/src/System/Collections/Concurrent/ConcurrentQueue.cs)
and [ConcurrentQueueSegment](https://github.com/dotnet/runtime/blob/v10.0.7/src/libraries/System.Private.CoreLib/src/System/Collections/Concurrent/ConcurrentQueueSegment.cs).

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, nested Mpsc at line 1252 and three newMpscQueue overloads.

```java
// jctools goes through its own process of initializing unsafe; of

// course, this requires permissions which might not be granted to calling code, so we

// must mark this block as privileged too

// force JCTools to initialize unsafe

// Calculate the max capacity which can not be bigger than MAX_ALLOWED_MPSC_CAPACITY.

// This is forced by the MpscChunkedArrayQueue implementation as will try to round it

// up to the next power of two and so will overflow otherwise.

/**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!).
     * @return A MPSC queue which may be unbounded.
     */

/**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!).
     */

/**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!).
     * The queue will grow and shrink its capacity in units of the given chunk size.
     */
```

Retired C# explanatory comments: src/Netty.NET.Common/Collections/JCTools/MpscArrayQueue.cs.

```csharp
/// <summary>
/// Forked from <a href="https://github.com/JCTools/JCTools">JCTools</a>.
/// A Multi-Producer-Single-Consumer queue based on a <see cref="ConcurrentCircularArrayQueue{T}"/>. This implies
/// that any thread may call the Enqueue methods, but only a single thread may call poll/peek for correctness to
/// maintained.
/// <para>
/// This implementation follows patterns documented on the package level for False Sharing protection.
/// </para>
/// <para>
/// This implementation is using the <a href="http://sourceforge.net/projects/mc-fastflow/">Fast Flow</a>
/// method for polling from the queue (with minor change to correctly publish the index) and an extension of
/// the Leslie Lamport concurrent queue algorithm (originated by Martin Thompson) on the producer side.
/// </para>
/// </summary>
/// <typeparam name="T">The type of each item in the queue.</typeparam>
// padded reference
/// <summary>
/// Lock free Enqueue operation, using a single compare-and-swap. As the class name suggests, access is
/// permitted to many threads concurrently.
/// </summary>
/// <param name="e">The item to enqueue.</param>
/// <returns><c>true</c> if the item was added successfully, otherwise <c>false</c>.</returns>
/// <seealso cref="IQueue{T}.tryEnqueue"/>
// use a cached view on consumer index (potentially updated in loop)
// LoadLoad
// LoadLoad
// LoadLoad
// FULL :(
// update shared cached value of the consumerIndex
// StoreLoad
// update on stack copy, we might need this value again if we lose the CAS.
// NOTE: the new producer index value is made visible BEFORE the element in the array. If we relied on
// the index visibility to poll() we would need to handle the case where the element is not visible.
// Won CAS, move on to storing
// StoreStore
// AWESOME :)
/// <summary>
/// A wait-free alternative to <see cref="tryEnqueue"/>, which fails on compare-and-swap failure.
/// </summary>
/// <param name="e">The item to enqueue.</param>
/// <returns><c>1</c> if next element cannot be filled, <c>-1</c> if CAS failed, and <c>0</c> if successful.</returns>
// LoadLoad
// LoadLoad
// LoadLoad
// FULL :(
// StoreLoad
// look Ma, no loop!
// CAS FAIL :(
// Won CAS, move on to storing
// AWESOME :)
/// <summary>
/// Lock free poll using ordered loads/stores. As class name suggests, access is limited to a single thread.
/// </summary>
/// <param name="item">The dequeued item.</param>
/// <returns><c>true</c> if an item was retrieved, otherwise <c>false</c>.</returns>
/// <seealso cref="IQueue{T}.tryDequeue"/>
// LoadLoad
// Copy field to avoid re-reading after volatile load
// If we can't see the next available element we can't poll
// LoadLoad
// NOTE: Queue may not actually be empty in the case of a producer (P1) being interrupted after
// winning the CAS on offer but before storing the element in the queue. Other producers may go on
// to fill up the queue after this element.
// StoreStore
/// <summary>
/// Lock free peek using ordered loads. As class name suggests access is limited to a single thread.
/// </summary>
/// <param name="item">The peeked item.</param>
/// <returns><c>true</c> if an item was retrieved, otherwise <c>false</c>.</returns>
/// <seealso cref="IQueue{T}.tryPeek"/>
// Copy field to avoid re-reading after volatile load
// LoadLoad
// NOTE: Queue may not actually be empty in the case of a producer (P1) being interrupted after
// winning the CAS on offer but before storing the element in the queue. Other producers may go on
// to fill up the queue after this element.
/// <summary>
/// Returns the number of items in this <see cref="MpscArrayQueue{T}"/>.
/// </summary>
// It is possible for a thread to be interrupted or reschedule between the read of the producer and
// consumer indices, therefore protection is required to ensure size is within valid range. In the
// event of concurrent polls/offers to this method the size is OVER estimated as we read consumer
// index BEFORE the producer index.
// Order matters!
// Loading consumer before producer allows for producer increments after consumer index is read.
// This ensures the correctness of this method at least for the consumer thread. Other threads POV is
// not really
// something we can fix here.
```

Retired C# explanatory comments: src/Netty.NET.Common/Collections/JCTools/MpscArrayQueueConsumerField.cs.

```csharp
// todo: revisit: UNSAFE.putOrderedLong -- StoreStore fence
```

Retired C# explanatory comments: src/Netty.NET.Common/Collections/JCTools/MpscArrayQueueL1Pad.cs.

```csharp
// padded reference
```

Retired C# explanatory comments: src/Netty.NET.Common/Collections/JCTools/MpscArrayQueueL2Pad.cs.

```csharp
// padded reference
```

Retired C# explanatory comments: src/Netty.NET.Common/Collections/JCTools/MpscArrayQueueMidPad.cs.

```csharp
// padded reference
```

Retired C# explanatory comments: src/Netty.NET.Common/Collections/JCTools/RefArrayAccessUtil.cs.

```csharp
/// <summary>
/// A plain store (no ordering/fences) of an element to a given offset.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <param name="buffer">The source buffer.</param>
/// <param name="offset">Computed via <see cref="ConcurrentCircularArrayQueue{T}.CalcElementOffset"/></param>
/// <param name="e">An orderly kitty.</param>
/// <summary>
/// An ordered store(store + StoreStore barrier) of an element to a given offset.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <param name="buffer">The source buffer.</param>
/// <param name="offset">Computed via <see cref="ConcurrentCircularArrayQueue{T}.CalcElementOffset"/></param>
/// <param name="e"></param>
/// <summary>
/// A plain load (no ordering/fences) of an element from a given offset.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <param name="buffer">The source buffer.</param>
/// <param name="offset">Computed via <see cref="ConcurrentCircularArrayQueue{T}.CalcElementOffset"/></param>
/// <returns>The element at the given <paramref name="offset"/> in the given <paramref name="buffer"/>.</returns>
/// <summary>
/// A volatile load (load + LoadLoad barrier) of an element from a given offset.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <param name="buffer">The source buffer.</param>
/// <param name="offset">Computed via <see cref="ConcurrentCircularArrayQueue{T}.CalcElementOffset"/></param>
/// <returns>The element at the given <paramref name="offset"/> in the given <paramref name="buffer"/>.</returns>
/// <summary>
/// Gets the offset in bytes within the array for a given index.
/// </summary>
/// <param name="index">The desired element index.</param>
/// <param name="mask">Mask for the index.</param>
/// <returns>The offset (in bytes) within the array for a given index.</returns>
```

Retired C# explanatory comments: src/Netty.NET.Common/Collections/ConcurrentCircularArrayQueue.cs.

```csharp
/// Forked from
/// <a href="https://github.com/JCTools/JCTools">JCTools</a>
/// .
/// A concurrent access enabling class used by circular array based queues this class exposes an offset computation
/// method along with differently memory fenced load/store methods into the underlying array. The class is pre-padded and
/// the array is padded on either side to help with False sharing prvention. It is expected theat subclasses handle post
/// padding.
/// <p />
/// Offset calculation is separate from access to enable the reuse of a give compute offset.
/// <p />
/// Load/Store methods using a
/// <i>buffer</i>
/// parameter are provided to allow the prevention of field reload after a
/// LoadLoad barrier.
/// <p />
// pad data on either end with some empty slots.
/// <summary>
/// Calculates an element offset based on a given array index.
/// </summary>
/// <param name="index">The desirable element index.</param>
/// <returns>The offset in bytes within the array for a given index.</returns>
/// <summary>
/// A plain store (no ordering/fences) of an element to a given offset.
/// </summary>
/// <param name="offset">Computed via <see cref="CalcElementOffset"/>.</param>
/// <param name="e">A kitty.</param>
/// <summary>
/// An ordered store(store + StoreStore barrier) of an element to a given offset.
/// </summary>
/// <param name="offset">Computed via <see cref="CalcElementOffset"/>.</param>
/// <param name="e">An orderly kitty.</param>
/// <summary>
/// A plain load (no ordering/fences) of an element from a given offset.
/// </summary>
/// <param name="offset">Computed via <see cref="CalcElementOffset"/>.</param>
/// <returns>The element at the offset.</returns>
/// <summary>
/// A volatile load (load + LoadLoad barrier) of an element from a given offset.
/// </summary>
/// <param name="offset">Computed via <see cref="CalcElementOffset"/>.</param>
/// <returns>The element at the offset.</returns>
// looping
// padded reference
```

Retired C# explanatory comments: src/Netty.NET.Common/Collections/ConcurrentQueueAdapter.cs.

```csharp
// CLR adaptation for Netty's unbounded JCTools queue. ConcurrentQueue supplies
// concurrent publication and FIFO without Java Unsafe/VarHandle dependencies.
// Its segment sizes and multi-consumer support differ from the upstream queue.
```

Retired C# explanatory comments: src/Netty.NET.Common/IntegerExtensions.cs.

```csharp
// first round down to one less than a power of 2
```

## Native fixed queues and recycler publication

Pinned PlatformDependent.java:1329-1375/1397-1402 contains five collection-provider
factories, selecting JVM Unsafe/VarHandle/atomic JCTools classes. Remove their five
unused C# throwing declarations after the following actual-consumer review:

| Factory | Pinned consumers and required purpose | CLR decision |
| --- | --- | --- |
| newSpscQueue | transport/local/LocalChannel.java:68; add, poll, peek, empty/drain, FIFO handoff | Direct ConcurrentQueue provides native FIFO publication; no SPSC/provider wrapper is required. Channel transport integration remains outside common. |
| newFixedMpscQueue | microbench/BurstCostExecutorsBenchmark.java:71/77/90/161; reject full admission, retain poison-pill/accepted work | A native bounded Channel in Wait full mode uses TryWrite to reject fullness. An owner must explicitly handle rejection and choose its effective capacity. No benchmark-only Java provider factory is ported. |
| newFixedMpscUnpaddedQueue | buffer/PoolThreadCache.java:336; offer/poll, full-return rejection and freeing rejected entries | Bounded native collection with owner cleanup for rejected entries; padded/unpadded JVM choices are not CLR APIs. The buffer consumer is a future module integration, not certified by common tests. |
| newFixedMpmcQueue | common/Recycler.java:539 and buffer/AdaptivePoolingAllocator.java:928; bounded offer/poll, FIFO returns, no eviction of accepted resources, multiple consumers | Current Recycler uses ConcurrentQueue plus CAS capacity reservations. Retain its explicit rounded bound/ownership and repair CLR interruption below. Adaptive shared chunk scanning/re-offering remains buffer work. |
| newConcurrentDeque | transport/pool/SimpleChannelPool.java:46/374/385; offerLast, pollFirst or pollLast according to recency policy | Owner-synchronized LinkedList or another real native double-ended collection is needed when porting the channel pool. The removed IQueue-returning stub never provided this contract; ConcurrentQueue would lose pollLast. |

These are common-factory CLR decisions, not completed implementations of the
downstream transport/buffer consumers. At this earlier fixed-queue checkpoint the unbounded MPSC adapter and two
bounded/chunked MPSC overloads remained open; their subsequent removal and actual
consumer boundaries are recorded above. Do not port padding hierarchies or publish a fake native deque.

Original PlatformDependentTest.testVarHandleQueuesWhenUnsafeIsUnavailable contains
six exact JCTools class-name assertions behind JVM feature assumptions. These
identities are superseded by native consumer behavior, not recreated as CLR class
names. Its fixed-capacity purposes are exercised through Recycler; ordinary FIFO
publication is exercised by the existing NonSticky scenarios. Its chunked-provider
class identity is JVM-only; the common aliases and actual native consumer purposes
are subsequently reviewed above, with downstream module integration still pending.
No existing portable test is removed, weakened or newly skipped.

The existing Recycler reservation/ConcurrentQueue backend has a demonstrated CLR
defect: EnqueueSlow/TryDequeueSlow acquire a contended monitor at segment changes.
Thread.Interrupt can throw before a return is published or a borrow is claimed.
Two public Recycler scenarios fail at the preceding checkpoint with
ThreadInterruptedException; reflection only holds the real net10 runtime gate to
force those waits. Native channel TryWrite/TryRead also use monitor entry, so its
thread-safe/bounded label alone would not preserve this resource-return contract.

Retry interrupted ConcurrentQueue operations inside the private return queue.
Enqueue retains its single CAS reservation during retries; other failures still
roll it back. Dequeue releases exactly one reservation after claiming an item.
Restore the consumed CLR interrupt in finally, for a later interruptible wait.
Clear drains through that same Poll boundary. No delegate/callback runs inside
these BCL operations; the reviewed net10 source throws on these waits before the
item-specific side effect, so retry cannot duplicate a publication or claim.
Keep the existing backend, guard/unguarded handles, rounded limits, debug monitor
strategy, owner batching and reclamation policies. This is not a claim of JVM
lock-free progress or a performance equivalence result.

Eight new cases cover the two interrupted segment changes, full-pool FIFO/reuse
at requested capacities 3 and 17 (effective 4 and 32) and concurrent guarded/
unguarded returns that preserve already accepted objects. The two interrupted
cases fail before repair; all eight pass afterward and preserve a later pending
interrupt. Original recycler workloads/case identities/comments remain unchanged.
Validation is Windows/net10.0; private gate scheduling is explicitly tied to the
reviewed runtime layout and must fail clearly if that layout changes.

Sources: [CLR ConcurrentQueue segment implementation](https://github.com/dotnet/runtime/blob/v10.0.7/src/libraries/System.Private.CoreLib/src/System/Collections/Concurrent/ConcurrentQueue.cs),
[native bounded channel full-mode contract](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels),
[CLR bounded-channel monitor entry](https://github.com/dotnet/runtime/blob/v10.0.7/src/libraries/System.Threading.Channels/src/System/Threading/Channels/BoundedChannel.cs),
and [Thread.Interrupt](https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.interrupt?view=net-10.0).
The retired five factory comments follow verbatim; placeholder implementation
statements are not original explanatory comments and are not archived.

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, newSpscQueue, line 1330.

```java
/**
     * Create a new {@link Queue} which is safe to use for single producer (one thread!) and a single
     * consumer (one thread!).
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, newFixedMpscQueue, line 1341.

```java
/**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!) with the given fixes {@code capacity}.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, newFixedMpscUnpaddedQueue, line 1352.

```java
/**
     * Create a new un-padded {@link Queue} which is safe to use for multiple producers (different threads) and a single
     * consumer (one thread!) with the given fixes {@code capacity}.<br>
     * This should be preferred to {@link #newFixedMpscQueue(int)} when the queue is not to be heavily contended.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, newFixedMpmcQueue, line 1365.

```java
/**
     * Create a new {@link Queue} which is safe to use for multiple producers (different threads) and multiple
     * consumers with the given fixes {@code capacity}.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, newConcurrentDeque, line 1397.

```java
/**
     * Returns a new concurrent {@link Deque}.
     */
```

## Native temporary-file creation

Pinned PlatformDependent.java:1751-1756 delegates to Files.createTempFile, not
File.createTempFile. The inherited JDK 21 TempFileHelper creates with exclusive
ownership, accepts null/empty/short prefixes, defaults a null suffix to .tmp,
rejects path components, retries name collisions and defaults POSIX files to owner
read/write. Actual consumers are common/NativeLibraryLoader.java:199 (WORKDIR),
handler/ssl/util/SelfSignedCertificate.java:308/339 (.key/.crt), and
codec-http/multipart/AbstractDiskHttpData.java:92-95 (default or configured directory).
Transport/buffer/stream fixtures reopen the path as a file; EpollSpliceTest.java:194
passes a null suffix. This review changes the common helper, not those modules.

Use DirectoryInfo for the optional directory and FileInfo for the returned file.
No C# caller currently supplies the removed FileInfo directory parameter. Default
directory comes from Path.GetTempPath; JVM java.io.tmpdir and Netty's separate
io.netty.tmpdir probe do not select this native helper's default. Prefix/suffix
must be native filename components, validated before creation. Short/null/empty
prefixes remain valid; no Java File three-character rule is introduced. Native
platform filename constraints and exception types apply; random name formatting
is deliberately unspecified.

FileStreamOptions with CreateNew atomically reserves a filename without truncating
or following an existing file/link. The stream is closed before returning the
path; deletion belongs to the caller, with no automatic exit hook. UnixCreateMode
requests UserRead|UserWrite during creation, subject to umask; Windows inherits its
native directory ACL rather than inventing POSIX permissions. Retry only native
collision errors (Windows HRESULT FILE_EXISTS/ALREADY_EXISTS, Unix errno EEXIST=17),
at most 100 retries/101 attempts as in the CLR Windows temp-file implementation.
Unlike JDK's unbounded collision loop, exhaustion propagates IOException. Other
IO errors propagate immediately; close errors are outside the collision filter.
The helper neither creates a missing directory nor silently changes destination.

The previous code omitted .tmp for null suffix, allowed directory escape through
prefix and used truncating File.Create. Eight new public contract cases fail
against the preceding implementation. The repaired helper has 17 passing Windows
cases covering these boundaries, named directory/ownership, missing/non-directory
errors and 256 parallel reopenable files with preserved existing content. The Unix
permission assertion is OS-guarded and its branch was not executed on Windows.
Fourteen normalized rows from the exact pinned Java wrapper on Corretto 21.0.11
match the real checked CLR library. A separate copy of the production method
changes only Path.GetRandomFileName to a controlled name supplier: it verifies
actual FileStream collision retry, preserved sentinel content, released handle,
101-attempt exhaustion and immediate unrelated IO failure. It is an instrumented
Windows probe, not a forced-collision run of the unmodified compiled library.
No existing Java comment or fixture is removed; this original method has no
method comment. Whole-platform capability/bootstrap/queue reviews remain open.

Framework sources: [Java Files.createTempFile](https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/nio/file/Files.html#createTempFile(java.nio.file.Path,java.lang.String,java.lang.String,java.nio.file.attribute.FileAttribute...)),
[FileMode.CreateNew](https://learn.microsoft.com/en-us/dotnet/api/system.io.filemode?view=net-10.0),
[UnixCreateMode](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestreamoptions.unixcreatemode?view=net-10.0),
[CLR Windows temporary-file retries](https://github.com/dotnet/runtime/blob/v10.0.7/src/libraries/System.Private.CoreLib/src/System/IO/Path.Windows.cs#L188-L237),
and [CLR Unix IO exception mapping](https://github.com/dotnet/runtime/blob/v10.0.7/src/libraries/Common/src/Interop/Unix/Interop.IOErrors.cs#L143-L148).

## CLR endian views and JVM access strategies

Pinned VarHandleFactory.java supplies twelve 16/32/64-bit LE/BE array/ByteBuffer
views and a private int-field lookup, with a JVM availability/failure probe.
PlatformDependent.java:717-805 exposes those handles. Actual byte consumers are
buffer/VarHandleByteBufferAccess, HeapByteBufUtil, UnpooledDirectByteBuf and Unsafe
buffer fallbacks. The sole findVarHandleOfIntField consumer is RefCnt.java:320;
ReferenceCountUpdater selects that provider. ConcurrentSkipListIntObjMultimap uses
the capability flag to choose an acquire fence, independently of the view factory.

Record VarHandleFactory as a CLR framework replacement; no MethodHandle/VarHandle
factory or capability facade is needed. Use BinaryPrimitives on bounded spans for
explicit wire byte order, and MemoryMarshal for host-order words. Byte offsets may
be unaligned, within the runtime span contract; no JVM Unsafe/architecture flag
selects this operation. ByteBuffer position, limit and mutable order become an
explicit logical Memory/Span slice and explicit endian operation. Plain byte reads
and writes do not provide atomicity or acquire/release publication.

The native ref-int counter already uses Interlocked/Volatile against the owner's
typed field. The current ordered map serializes state through its CLR lock; its
whole public API review remains open. Do not reproduce private JVM field lookup,
an unrelated whole-object layout, or an arbitrary fence on ordinary endian access.
Real io_uring SubmissionQueue:279-281 and CompletionQueue:125/166 use volatile/
release operations through their own ByteBuffer handles, not this common factory.
Their shared native ring needs an OS/ABI-specific aligned atomic/publication and
lifetime design during transport work; endian parity does not certify that module.

Retire fifteen remaining C# platform declarations: eleven array word/index
forwarders plus ToIntExact, and three IsUnaligned/UnalignedAccess methods. Remove their
default-false UNALIGNED field and AsciiString.ByteAt's JVM Unsafe branch. ByteAt
keeps its logical substring range guard and uses the actual array index. Existing
CLR fixtures now call MemoryMarshal or array indexing directly, retaining case
identities and assertions. A native array long index fails with IndexOutOfRangeException,
rather than the removed wrapper's OverflowException, and never truncates to the
low 32 bits; this intentional exception change is recorded in the typed-array case.
Other platform APIs and JVM bootstrap scaffolding remain a separate review.

The unchanged pinned factory was executed on Corretto 21.0.11. Across 16/32/64-bit
widths, both endian orders, offsets 0-7, six signed/high-bit values and array/heap
ByteBuffer/direct ByteBuffer storage, 864 rows match the checked CLR byte payloads
and read results. CLR storage uses heap bytes, NativeMemoryOwner and pinned borrowed
NativeMemoryView. Java view order is verified independently of mutable ByteBuffer
order/position; ByteBuffer limit and CLR logical slice rejection remain checked.
Six integration cases verify the common owner/view bounds and no mutation after
short-region failure, with byte expectations assembled independently of the BCL.

Checked validation exposed an existing Select(-1) unsigned-cast overflow in
ConcurrentOrderedMultiMap; explicit negative/upper bound checks restore absence.
A subsequent run exposed the original AsciiStringMemoryTest byte increment at 255.
Java byte ++ wraps; its two original increments now use explicit unchecked blocks,
retaining every original assertion/workload. A seventh, deterministic boundary
case invokes both original shared/copied-memory scenarios with 255 and fails before
that fix. The first two checked runs each fail one distinct case; the final affected
checked Release selection passes all 395. Initial fixture migration compile errors
(explicit in on constants, array-expression syntax and remaining wrapper calls)
were corrected before behavioral verification. Existing unrelated warnings remain.

Framework contracts: [BinaryPrimitives](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.binary.binaryprimitives?view=net-10.0),
[MemoryMarshal.Read](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.memorymarshal.read?view=net-10.0),
and [Volatile ordering](https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile.read?view=net-10.0).
This verifies ordinary byte interpretation and common storage integration on
Windows/net10.0, not throughput, cross-architecture execution or native ring ordering.
Original comments removed by these framework replacements follow verbatim.

Source: common/src/main/java/io/netty/util/internal/VarHandleFactory.java, line 1.

```java
/*
 * Copyright 2025 The Netty Project
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

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 467.

```java
/**
     * {@code true} if and only if the platform supports unaligned access.
     *
     * @see <a href="https://en.wikipedia.org/wiki/Segmentation_fault#Bus_error">Wikipedia on segfault</a>
     */
```

Source: common/src/main/java/io/netty/util/AsciiString.java, line 331.

```java
// Try to use unsafe to avoid double checking the index bounds
```

## Native assembly version diagnostics

Pinned Version.java:49-153 discovers META-INF/io.netty.versions.properties through
an explicit/current Java class loader, merges later resource properties, skips
incomplete six-field artifacts, sorts Maven IDs and prints version/hash/status.
The all-module pinned search finds no import, construction or identify call
outside Version itself; its main method is the standalone diagnostic entry point.
There is no common Version test source. The purpose is useful diagnostics, not a
requirement to retain a Java property-file parser or a Netty-specific DTO.

Retire Netty.NET.Common.Version. CLR already exposes Assembly.GetName and
AssemblyInformationalVersionAttribute; the net10.0 SDK emits both, with the Git
SourceRevisionId included in the informational version for this repository.
Reading the caller-selected Assembly preserves its actual identity. Select an
explicit AssemblyLoadContext, or explicitly honor CurrentContextualReflectionContext,
when listing loaded modules. Enumerate that context's Assemblies and sort with
StringComparer.Ordinal if a stable diagnostic order is wanted. Retain context
identity in the report rather than silently overwriting assemblies with the same
short name. Discovery covers loaded CLR assemblies, not every unloaded resource
on a Java class path; it does not load plugins or enumerate all contexts implicitly.

For an explicitly selected assembly, a native diagnostic consumer can use:

```csharp
Assembly assembly = typeof(PlatformDependent).Assembly;
string name = assembly.GetName().Name;
string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? assembly.GetName().Version?.ToString()
    ?? "unknown";
Console.Error.WriteLine($"{name}: {version}");
```

This is a consumer example using System.Reflection, not a replacement facade.
InformationalVersion is descriptive text and may contain semantic-version build
metadata; do not parse it as System.Version or promise arbitrary '+' suffixes are
commit hashes. The executed build's full informational version is compared to
the known SourceRevisionId. PackageId/Maven artifact ID, assembly short name,
assembly identity version and informational product version are distinct values.
The CLR report uses the actual assembly name and informational version.

BuildDate, CommitDate and RepositoryStatus are not supplied by ordinary SDK builds.
A packaging pipeline needing them can explicitly provide AssemblyMetadata items
with those keys and read AssemblyMetadataAttribute from that assembly. Absent
metadata remains absent/unknown: do not infer a build time from file modification
times, substitute the current clock, or claim the repository was clean. The
original six-required-properties filter, zero timestamp sentinel, Java date parser,
seven-character hash and combined toString formatting are deliberately retired;
basic CLR version diagnostics remain available even without optional provenance.
Metadata/reflection failures should be handled at the diagnostic caller's boundary;
there is no generic catch that invents a successful report.

Before retirement, a consumer of the committed DLL reproduces two real defects:
a working-directory property file creates an invented artifact, and Identify on
both common and corelib returns that same artifact while ignoring the Assembly.
An initial hypothesis that the Java numeric timezone offsets would fail parsing
was rejected by execution: both dates match the pinned Java epoch milliseconds.
It is not counted as a defect. The unchanged pinned Version, with only the context
loader dependency shim, confirms resource scope/overwrite, ordering, required
fields, numeric dates/invalid=0 and clean/dirty text on Corretto 21.0.11.

An isolated native consumer reads the actual new Debug/Release common DLLs in a
collectible context, checks the emitted name/version/full Git revision, explicit
and contextual scope, absence of invented optional provenance, independence from
the spoofed working-directory file and absence of the retired facade. These are
build/consumer checks, not new permanent tests of BCL internals. Whole default
Debug/Release compile all existing consumers/fixtures; none is removed or skipped.
Trimming/AOT, custom packaging metadata and unloaded-plugin discovery are outside
this verification. No throughput or complete common-port claim is made.

Framework contracts: [SDK assembly attributes and SourceRevisionId](https://learn.microsoft.com/dotnet/standard/assembly/set-attributes-project-file),
[AssemblyLoadContext.Assemblies](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext.assemblies?view=net-10.0),
and [AssemblyMetadataAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assemblymetadataattribute?view=net-10.0).
The installed 10.0.203 Microsoft.NET.GenerateAssemblyInfo.targets and the executed
net10.0 consumer independently establish the SDK metadata behavior. All ten
original comments, including the removed source license, follow verbatim.

Source: common/src/main/java/io/netty/util/Version.java at the pinned commit.

Line 1:

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

Line 32:

```java
/**
 * Retrieves the version information of available Netty artifacts.
 * <p>
 * This class retrieves the version information from {@code META-INF/io.netty.versions.properties}, which is
 * generated in build time.  Note that it may not be possible to retrieve the information completely, depending on
 * your environment, such as the specified {@link ClassLoader}, the current {@link SecurityManager}.
 * </p>
 */
```

Line 49:

```java
/**
     * Retrieves the version information of Netty artifacts using the current
     * {@linkplain Thread#getContextClassLoader() context class loader}.
     *
     * @return A {@link Map} whose keys are Maven artifact IDs and whose values are {@link Version}s
     */
```

Line 59:

```java
/**
     * Retrieves the version information of Netty artifacts using the specified {@link ClassLoader}.
     *
     * @return A {@link Map} whose keys are Maven artifact IDs and whose values are {@link Version}s
     */
```

Line 69:

```java
// Collect all properties.
```

Line 82:

```java
// Ignore.
```

Line 87:

```java
// Not critical. Just ignore.
```

Line 90:

```java
// Collect all artifactIds.
```

Line 102:

```java
// Skip the entries without required information.
```

Line 140:

```java
/**
     * Prints the version information to {@link System#err}.
     */
```

## CLR initialization, reflection and exception origins

Pinned ClassInitializerUtil.java calls Class.forName(name, true, loader): it
initializes the class, rather than only loading metadata. Its actual bootstrap
users are Unix, epoll, kqueue, io_uring, Quiche and the macOS DNS provider. Unix's
OnLoad comment identifies avoiding a JNI class-loader deadlock as the purpose.
Those native module integrations remain future work.

ClassInitializerUtil.TryInitialize accepts exact CLR Types and calls
RuntimeHelpers.RunClassConstructor on each TypeHandle in order. The former
loadingType.Assembly.GetType(name) lookup could select another type identity;
reading TypeInitializer metadata did not execute the initializer. There is no
loading-class anchor or alias for a Java loader. Initialization completes before
returning, runs once under concurrent/repeated calls, and preserves the supplied
AssemblyLoadContext identity. Closed generic types have distinct CLR static state;
open generic definitions and null lists/entries fail explicitly. Empty lists and
types without initializers are valid. Only type-loading/security failures are
best effort; constructor failures propagate as TypeInitializationException with
their cause and are not retried. The API carries the BCL trimming annotation;
trimmed/AOT/native bootstrap integration is not established by these tests.

The unchanged pinned Java initializer, with only a same-loader lookup shim for
PlatformDependent, was executed on Corretto 21.0.11. It confirms synchronous and
once-only initialization, 64 concurrent calls, failure propagation/no retry, empty
lists/no-initializer types and null rejection. JVM failures use
ExceptionInInitializerError followed by NoClassDefFoundError; CLR uses
TypeInitializationException. The harness does not exercise JNI, different Java
class loaders, SecurityManager or the complete common module. Seven CLR cases
add exact collectible-load-context identity and reified generic validation.

Retire GetClassLoader/GetContextClassLoader/GetSystemClassLoader in both platform
classes. A Type's Assembly owns its metadata/resources; it is not a Java loader.
Thread.CurrentThread.GetType().Assembly returned corelib, and GetEntryAssembly
could return null; neither represents contextual/system loading. The original
Version identifies resources through the context loader, ResourceLeakDetectorFactory
resolves a configured detector through the system loader, and native compression,
transport, DNS, Quiche and SSL code resolves native libraries/optional classes.
ClassResolvers uses contextual/owner loading for serialization. Future CLR module
work must choose explicit Assembly resource ownership and AssemblyLoadContext or
contextual-reflection policy for each purpose. The native custom leak factory and
NativeLibraryUtil already use real CLR reflection/loading; their existing tests
remain. The Version working-directory file/ignored Assembly defect was still
open at this checkpoint; it is subsequently resolved by the standard assembly
metadata replacement above, without a Java resource-loader facade.

Retire the no-op ReflectionUtil.TrySetAccessible, its unreachable JDK access-error
translator and the platform reflective-access flag. JVM AccessibleObject has a
mutable accessible flag; CLR members do not. Use actual MemberInfo/BindingFlags
operations and handle their real failures. Original Unsafe/direct-buffer probes,
NioIoHandler selector fields and SslMasterKeyHandler's private JDK TLS fields do
not create equivalent CLR access contracts. Their module policies remain open.
ReflectionUtil's previously reviewed constructed-generic resolver remains intact,
with seven portable matcher cases and seven CLR contracts. The existing Graal
runtime-test identity now invokes a real nonpublic method, rather than checking a
retired flag: absent/false/true Java reflection properties do not change CLR access.

C# has no checked-exception compiler bypass to emulate. Retire ThrowException and
the unused throwing RethrowIfPossible stub. FastThreadLocal propagates InitialValue
and OnRemoval failures directly; initialization failure leaves the binding unset
and removal clears it before invoking the callback. StringBuilder.Append needs
no Java Appendable IOException catch. Shutdown startup uses bare throw after
publishing termination failure, retaining the original exception and stack. Four
CLR cases reproduce lost callback/startup origins before repair and verify retry,
cleanup and faulted Termination. Synthetic fatal exceptions do not exhaust the
host. Stored/asynchronous exception transfer remains a distinct Task/EDI policy.

Framework contracts: [explicit static initialization](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.runtimehelpers.runclassconstructor?view=net-10.0),
[assembly load contexts](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext),
and [exception propagation](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions).
Original explanatory comments for the retired JVM-specific operations follow;
existing source licenses and generic-resolution comments remain beside the code.

### Pinned PlatformDependent comments

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java

Line 552:

```java
/**
     * Raises an exception bypassing compiler checks for checked exceptions.
     */
```

Line 1376:

```java
/**
     * Return the {@link ClassLoader} for the given {@link Class}.
     */
```

Line 1383:

```java
/**
     * Return the context {@link ClassLoader} for the current {@link Thread}.
     */
```

Line 1390:

```java
/**
     * Return the system {@link ClassLoader}.
     */
```

### Pinned PlatformDependent0 comments

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java

Line 1229:

```java
// we disable reflective access
```

### Pinned ReflectionUtil comments

Source: common/src/main/java/io/netty/util/internal/ReflectionUtil.java

Line 29:

```java
/**
     * Try to call {@link AccessibleObject#setAccessible(boolean)} but will catch any {@link SecurityException} and
     * {@link java.lang.reflect.InaccessibleObjectException} and return it.
     * The caller must check if it returns {@code null} and if not handle the returned exception.
     */
```

Line 49:

```java
// JDK 9 can throw an inaccessible object exception here; since Netty compiles
```

Line 50:

```java
// against JDK 7 and this exception was only added in JDK 9, we have to weakly
```

Line 51:

```java
// check the type
```

### Pinned ClassInitializerUtil comments

Source: common/src/main/java/io/netty/util/internal/ClassInitializerUtil.java

Line 18:

```java
/**
 * Utility which ensures that classes are loaded by the {@link ClassLoader}.
 */
```

Line 25:

```java
/**
     * Preload the given classes and so ensure the {@link ClassLoader} has these loaded after this method call.
     *
     * @param loadingClass      the {@link Class} that wants to load the classes.
     * @param classes           the classes to load.
     */
```

Line 40:

```java
// Load the class and also ensure we init it which means its linked etc.
```

Line 43:

```java
// Ignore
```

## Native byte comparison, zero checks and ASCII hashing

Pinned common AsciiString uses logical-slice equality and the low-five-bit ASCII
hash; NetUtil uses zero checks for parsed IPv6 storage. HPACK HpackUtil and QPACK
QpackUtil establish fixed-time byte comparison consumers. Their buffer/protocol
implementations remain outside this common stage. Ordinary comparison may stop on
a mismatch; fixed-time comparison must retain its separate byte-content contract.

PlatformDependent equality uses bounded Span.SequenceEqual; zero checks use
IndexOfAnyExcept(0). ConstantTimeUtils's byte entry point uses
[CryptographicOperations.FixedTimeEquals](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals?view=net-10.0),
and PlatformDependent delegates to it. Equal-length spans supply the BCL's
content-independent comparison, including aliasing inputs. Preserve 0/1 results
for existing cascading callers. This is reliance on the documented runtime
primitive, not a timing guarantee established by functional tests or a benchmark.
Integer and ICharSequence constant-time helpers remain separately pending review.

ASCII hashing has one native-order MemoryMarshal word/tail implementation with
the pinned unchecked integer arithmetic and existing constants/compute/sanitize
helpers. Hash values stay identical; this low-five-bit hash is not a Unicode
comparer or a content-equality proof. Retire six scalar/strategy helpers and four
PlatformDependent0 throwing array/hash stubs, plus five unused layout fields.
Remove HasUnsafe/UnalignedAccess dispatch from these four operations. The pure
hash arithmetic in PlatformDependent0 is retained; neither platform class is
declared complete by this unit.

Nonnull comparisons/zero checks keep the original nonpositive-length empty
results, including -1. Positive ranges validate before any mismatch/overflow can
hide invalid storage. Hash ranges validate even when length is zero and reject
negative lengths. Null arrays now fail explicitly with ArgumentNullException,
including empty comparisons; these are stated CLR adaptations of Java's
caller-validated range contract. Bounds checking does not inspect byte contents.

The original TestHashCodeAscii keeps all 1000 byte/string comparisons; the
redundant scalar-versus-Unsafe strategy assertion retires with that JVM strategy.
AsciiStringHashContractTest compares the single public kernel with its independent
pinned Java tables for every tail/word boundary. Twenty-one new cases cover
logical slices, every mismatch lane, high-bit bytes, zero checks, overflow-hidden
bounds, invalid second ranges, nulls and empty results. Eight fail before repair.
The 17024-input oracle matches exact extracted scalar and Unsafe Java methods.
Full/checked verification is recorded in common-porting.md. No feature MD added.

Retired placeholder code stored as C# line comments is implementation scaffolding,
not original Java explanatory comments. Remove that dead code; keep all original
comments below or at their retained implementation. Historical comments for the
removed scalar strategy and accumulator optimization remain exact provenance.

### Retired scalar strategy/accumulator comments

```java
/**
     * Package private for testing purposes only!
     */
// Benchmarking demonstrates that using an int to accumulate is faster than other data types.
```

### Pinned PlatformDependent0 hash-tail comments

```java
// 1, 3, 5, 7
// 2, 3, 6, 7
// 4, 5, 6, 7
```

## Native byte access through bounded CLR memory

Retire 39 declarations in PlatformDependent/PlatformDependent0: raw long-address
allocation/free/reallocation, word reads/writes, mixed object-offset/native copies
and fills, array-header offsets and unsupported ordered-address adapters. Eighteen
lower-level throwing stubs disappear. This is a reviewed CLR replacement of their
purposes; both source files stay in-progress for remaining APIs and stubs.

Pinned UnsafeByteBufUtil and UnsafeDirectSwappedByteBuf require byte/short/int/long
access, including unaligned regions. Use indexed bounded spans and MemoryMarshal
for host order; use BinaryPrimitives for declared wire order. Heap/native copies
and fills use Memory/Span slices, CopyTo and Fill with validation before mutation.
Overlapping CLR copies preserve snapshot semantics in either direction; no claim
is made that every JVM Unsafe copy implementation does the same.

PoolArena.memoryCopy and ReadOnlyUnsafeDirectByteBuf confirm native-to-native and
native-to-heap purposes. NativeMemoryAllocator/Owner already supply allocation,
reallocation, quota and deterministic release. WrappedUnpooledUnsafeDirectByteBuf
requires an explicit borrowed NativeMemoryView and external lifetime. ByteBufUtil
unsafeWriteUtf8's object-plus-array-header offsets become indexed bounded byte
storage; its actual charset/writer implementation belongs to the future buffer
port. Pin an owner for native I/O and keep its lease alive throughout pointer use.

No pinned Java caller uses PlatformDependent0.putShortOrdered; this C# tree's
long-address GetIntVolatile/PutIntOrdered adapters have no matching pinned Java
public methods. Removing their throwing placeholders does not declare native
publication unnecessary. SubmissionQueue/CompletionQueue use direct ByteBuffer
VarHandle volatile/release operations. That genuine ordered-memory contract stays
pending for native transport integration: the VarHandleFactory framework
replacement is reviewed above, while ordinary word access here provides neither
atomicity nor acquire/release synchronization.

NativeMemoryView now rejects unsigned address-plus-length wrap, including an
unrepresentable exclusive end needed by end-slice pins. Addresses retain unsigned
bits across nint.MaxValue; pointer conversions use explicit unchecked bit casts
after validation, including in checked builds. This cannot establish whether an
external allocation actually owns the described range. Borrowed pins retain the
descriptor and never extend the external allocation's lifetime.

Pinned PlatformDependent0Test's (-1, 10) address metadata wraps across zero and now
fails explicitly, as its null/nonempty case already does in CLR. The same fixture
also checks valid negative address bits (-16, 10); no synthetic pointer is read.
All original case identities and other outcomes remain, with this documented
assertion adaptation. Thirteen additional native cases cover words, bounds,
heap/native copies, borrowed aliases and pointer arithmetic. The 236-input exact
extracted Java word/copy/fill oracle matches both copy branches on Windows x64.
Checked runs include the original platform fixtures. Full-suite evidence is in
common-porting.md; this does not finish common or port any native transport.

### Retired PlatformDependent0 port comments (historical)

```csharp
//return UNSAFE.getByte(address);
//return UNSAFE.getShort(address);
//return UNSAFE.getInt(address);
//return UNSAFE.getLong(address);
//return UNSAFE.getIntVolatile(null, address);
//UNSAFE.putOrderedInt(null, address, newValue);
//UNSAFE.putByte(address, value);
//UNSAFE.putShort(address, value);
// UNSAFE.storeFence();
// UNSAFE.putShort(null, address, newValue);
//UNSAFE.putInt(address, value);
//UNSAFE.putLong(address, value);
// Manual safe-point polling is only needed prior Java9:
// See https://bugs.openjdk.java.net/browse/JDK-8149596
// CLR adaptation: this JDK threshold does not apply. Native address
// ownership and copying are still unported, so failure remains explicit.
// Manual safe-point polling is only needed prior Java9:
// See https://bugs.openjdk.java.net/browse/JDK-8149596
// CLR adaptation: no JDK-version branch can select a CLR memory API.
// The object-offset Unsafe model still requires a native replacement.
//UNSAFE.setMemory(address, bytes, value);
//UNSAFE.setMemory(o, offset, bytes, value);
//return UNSAFE.allocateMemory(size);
//UNSAFE.freeMemory(address);
//return UNSAFE.reallocateMemory(address, newSize);
```

### Pinned PlatformDependent0 copy comments

```java
/**
     * Limits the number of bytes to copy per {@link Unsafe#copyMemory(long, long, long)} to allow safepoint polling
     * during a large copy.
     */
// Manual safe-point polling is only needed prior Java9:
// See https://bugs.openjdk.java.net/browse/JDK-8149596
// Manual safe-point polling is only needed prior Java9:
// See https://bugs.openjdk.java.net/browse/JDK-8149596
```

## C# method naming

At the user's request, every tracked C# method and local-function declaration in
src, test and the queue-cost tool now starts with an uppercase letter. Capitalize
only the first character; retain behavior, parameter/return types, field names and
Java provenance comments. Interfaces, overrides, explicit implementations,
method-group/delegate references, nameof and callers change together. Reflection
lookup names and stack/exclusion method names follow the new names. Qualified type
expressions resolve method/type name hiding; identical EmptyPriorityQueue toArray/
ToArray aliases become one ToArray implementation. Lowercase compatibility aliases
are not retained. Java comment links/examples remain exact original provenance;
historical design/checkpoint method spellings refer to their recorded version.
This naming pass changes the public API, without completing pending porting work.

## Dependency review

Paths in the evidence column are relative to the pinned original repository.
These decisions do not claim that every method of a listed component is complete.

| Area | Original implementation and consumers | Required behavior and CLR decision | Remaining review |
| --- | --- | --- | --- |
| Completion | `common/.../concurrent/DefaultPromise.java`, `PromiseTask.java`; `transport/.../channel/AbstractChannel.java`, `ChannelOutboundBuffer.java` | Task/TCS owns the result and terminal state. Keep a cancellation boundary for operations that have committed to execution. Published completion must agree with every observer. | Reduce public Future/Promise compatibility surfaces and migrate their actual callers; do not keep blocking/JDK methods merely for tests. |
| Listeners and progress | `DefaultPromise.java`; resolver termination, channel-group close, SSL timeout, chunked-write and outbound-buffer consumers | Task owns the operation result. ExecutorProgress implements IProgress<TransferProgress> with unique detachable report/terminal registrations; ExecutorCompletion supplies ordered detachable Action<Task> registrations with independent notification Tasks. Both use native queue-removal policy and scoped executor context. The progressive hierarchy/factories are removed. See common-progress-subscriptions.md and common-native-completion.md. | Plain Future/Promise producers/callers and backend APIs still need migration. Combiners/notifiers use standard Task composition where callback affinity is unnecessary. |
| Execution | `SingleThreadEventExecutor.java`, `AbstractScheduledEventExecutor.java`; `transport/.../channel/AbstractChannel.java` | Preserve serial invocation, executor-owned state, deadlines and shutdown. Native Task-based submission/scheduling use the executor's queues and one TCS result; lifecycle exposes Task termination. | Legacy scheduling/listener callers and the final executor backend still need migration. See common-executor-lifecycle.md and common-native-scheduling.md. |
| Unordered execution | `UnorderedThreadPoolEventExecutor.java` and its inherited JDK scheduler | Dedicated workers, native Task results and private BCL deadline membership. PendingTaskCount replaces the inherited mutable queue; owner cancellation withdraws direct submissions and schedules. Termination waits for queue/worker/start reservations after quiet/timeout closure. Immutable constructor settings and native worker diagnostics replace inherited configuration/statistics. Concrete StopAsync/StopToken provide cooperative immediate stop with notification drain/failures; see common-unordered-cooperative-stop.md. | Shared/group immediate API remains open; local queue costs are measured in common-unordered-queue-costs.md, with contention/cancellation-heavy workloads still unmeasured. Global ThreadPool settings do not configure a local executor pool. |
| Constant registry | `common/.../ConstantPool.java`; `AttributeKey.java`, `Signal.java` and channel configuration constants | ConcurrentDictionary publishes one reference identity per name; Interlocked allocates IDs. Competing factories and ID gaps match the original. AbstractConstant now seals identity methods and uses a non-generic native uniquifier sequence; pools require reference constants. | Further registry/public API naming decisions are separate from the verified concurrency/reference/generic-static contracts. See common-task-composition.md for the repaired identity regressions. |
| Ordinary queues and maps | `PlatformDependent.java`, executor queues, `Recycler.java`; `buffer/.../PoolChunk.java` | Prefer ConcurrentQueue/Dictionary with explicit capacity and ownership policy where needed. PoolChunk's LongLongHashMap can use Dictionary<long,long> with explicit missing-value and remove/put result handling at its callers. | Recheck each queue's compound operations, reservation publication, overload/backpressure and iteration. Do not infer completion from a collection's thread-safe label. |
| Specialized integer queue | `MpscIntQueue.java`; `buffer/.../AdaptivePoolingAllocator.java` free lists | Fixed capacity, integer empty sentinel, fill/drain and weak reduction have actual allocator consumers. These requirements justify an adapter; generic CLR integers require no boxing specialization. | Compare the current ring with CLR collection alternatives against those operations; performance has not been measured. |
| Indexed priority queue | `DefaultPriorityQueue.java`, scheduled-task removal; `codec-http2/.../WeightedFairQueueByteDistributor.java` priority updates | Retain the indexed reference-node heap for mutable priorities and independent queue membership. Ordinary value/immutable entries use BCL PriorityQueue. Stopped scheduler queues clear references and indices. | Core source/interfaces reviewed; bounded indexed/BCL/tree costs below. Remaining scheduler/runtime review stays open. |
| Thread-local state | `FastThreadLocal.java`, `InternalThreadLocalMap.java`; allocator caches and event-loop workers | Physical-worker caches remain thread-local. AsyncLocal describes logical execution context and is a separate purpose. A sealed CLR Thread may be owned/wrapped where cleanup policy requires it. | Remove unnecessary ThreadGroup/JDK facade surface after caller review; keep cleanup/ownership behavior. |
| Resource lifetime | `AbstractReferenceCounted.java`, `ReferenceCountUtil.java`, `Recycler.java`; `buffer/.../AbstractReferenceCountedByteBuf.java` | GC does not decide when shared pooled/native storage is reusable. Retain/release must deallocate exactly once and never resurrect returned storage. Native typed ref-int counter operations replace the JVM RefCnt/updater providers; see CLR reference-count fields below. Dispose alone does not define shared ownership. | Native owners, borrowed views and pin leases are implemented; integrate them with future pooled retain/release consumers. Ordinary CLR object cleanup and shared storage ownership must remain distinct. |
| Text and memory views | `AsciiString.java`, `CharsetUtil.java`; buffer/codec callers | AsciiString now uses lossless byte widening, native string/span construction and bounded memory views; MemoryStream constructors were replaced by ReadOnlyMemory. Cached text agrees with mapped bytes. CharsetUtil is replaced by native Encoding/fallback policies and operation-owned codecs; explicit Java/CLR framing and replacement differences are recorded below. Integer parsing uses bounded byte spans, native Parse/TryParse APIs and checked-safe generic math. Floating-point parsing uses invariant BCL span conversion plus Java grammar and hexadecimal rounding; see the numeric decisions below. Seven allocation callers use GC.AllocateUninitializedArray directly. See common-ascii-memory.md and common-platform-runtime.md. | Unused regex facades are replaced by native Regex/literal string splitting. Native delimiter ranges/character search are reviewed below. Full native sequence API and future protocol framing/streaming integration remain. Raw platform addresses and pooled-buffer integration remain separate reviews. |
| Runtime selection | `PlatformDependent.java`, `PlatformDependent0.java`; buffer/transport/TLS/resolver consumers | JDK-version facades and JVM reflective array allocation are removed. CLR consumers use Environment.Version and GC directly. Android detection uses the actual OS; JVM/Graal properties do not select CLR features. See common-platform-runtime.md. | NativeMemory owners/views replace the JVM cleaner hierarchy; managed words and copy/fill use CLR spans. Managed field-offset stubs are removed after typed ref-int counter migration; raw native-address APIs remain in progress. See common-native-memory.md and common-heap-memory.md. |
| Ordinary object GC fallback | Deprecated `ObjectCleaner.java`; no production registration consumer in the pinned tree | ConditionalWeakTable lifetime notification and CLR pool dispatch replace the Java live-set/weak-queue/worker loop. Action registration, diagnostic count and concurrent/context-isolated cleanup are verified. See common-object-cleanup.md. | This runtime replacement does not define pooled/native storage ownership or deterministic resource disposal. |

## Implemented Task completion boundary

Task/TCS now owns the outcome directly. DefaultPromise, AbstractFuture, their
waiting/listener interfaces and erased listener storage are removed; see
common-native-future-retirement.md. Private native operation claims protect
commitment/cancellation boundaries, and ExecutorCompletion owns ordered detachable
notifications separately from result completion. PendingWrite uses a caller-owned
non-generic TCS and explicit pooled message/producer transfer.

TrySetCanceled(token) publishes cancellation. SetException(OperationCanceledException)
publishes that exact fault, without turning it into a canceled Task. A canceled
Task retains its token and has no synthetic cause object. The remaining native
submission policies distinguish queued cancellation from cooperative requests after
claiming execution. No public setUncancellable/getNow/blocking Future facade remains.

At the earlier adapter checkpoint a concurrent reader could observe the Netty result
as done before its supplementary Task completed; three regressions exposed that
split state and another exposed lost cancellation-token identity. Native consumer
competition tests now compare actual result/notification Task identity and original
value/error/token across 2000 iterations per outcome, without a second result view.

## Native executor submission

`EventExecutorExtensions.SubmitAsync` accepts Action/Func, including token-aware
and Task-returning delegates, and exposes Task/Task<T>. It does not construct a
Java Future/Promise for the submitted operation. An internal IRunnable is only
the boundary to the existing executor backend.

The same overloads now accept IEventExecutorGroup. AbstractEventExecutorGroup's
pinned submit methods select a child per submission; NonStickyEventExecutorGroup's
submit methods instead delegate to the underlying group. Native group submission
preserves that distinction. Explicitly submitting to a child returned by
NonSticky.next() still uses that child's ordered runner. Pre-canceled or invalid
submissions do not select a child; selection/admission failures fault the returned
Task with their original exception. No Future/Promise result adapter is created.

Direct submission to UnorderedThreadPoolEventExecutor now uses a queue reservation
without JdkFutureTask or PromiseTask. A narrow internal INativeSubmission exposes
only pre-start cancellation to queue removal; the submitted TCS still owns the
single result. Native admission rejects shutdown even if a legacy discard handler
would silently drop work, worker-start failure removes its queued reservation,
and shutdownNow cancels native submissions removed from the queue. Three targeted
cases failed before repair and pass afterwards. The broader submission/scheduling,
unordered and NonSticky selection passes 118 cases.

NonSticky ordered children now use BCL queues and per-admission native runner
reservations. Initial rejection faults pending native work and permits retry;
graceful shutdown drains admitted work while immediate shutdown cancels queued
work. Unordered execute recognizes native reservations through forwarding
executors, and each runner is bound to its actual queue owner. Eleven new cases
and the original workloads verify those contracts, thread handoff and bounded
inline dispatch. See common-nonsticky-runner.md.

This does not finish the unordered backend review. Submission and scheduling use
native results, while raw execute owns no result facade. Java scheduling overloads,
JDK/Promise decorators and cancellation-maintenance APIs are removed; see
common-native-unordered-scheduling-migration.md. The public mutable queue is removed
and PendingTaskCount supplies diagnostics; see common-unordered-native-queue.md.
Immutable constructor configuration and native worker diagnostics replace inherited
JDK settings; see common-unordered-native-configuration.md. Concrete immediate stop
uses explicit cooperative cancellation; see common-unordered-cooperative-stop.md.
Shared/group immediate API remains open. Local queue costs are measured in
common-unordered-queue-costs.md; contention/cancellation-heavy workloads remain
unmeasured. Worker replacement failure is implemented in
common-unordered-worker-failure.md. Graceful quiet/timeout admission
and actual drain are implemented; see common-unordered-graceful-shutdown.md. Detachable
native completion observers are implemented in ExecutorCompletion. Plain Future/Promise
fixtures now use those native APIs and the waiting/listener hierarchy is removed;
see common-native-future-retirement.md.
Shared/ordered scheduling facades and result adapters are removed; see
common-native-ordered-scheduling-migration.md. Dynamic native progress
registration is implemented; see common-progress-subscriptions.md.

Unordered worker registration now belongs to workerLoop instead of a factory
wrapper, so replacing the factory preserves native scheduled callback identity.
The prior false-affinity probe is explicitly replaced by the native contract;
source quirk evidence and regressions are in common-unordered-worker-identity.md.

The event-loop utilization writer now uses Interlocked.Add against the monitor's
Interlocked.Exchange. An independent known-budget test reproduced duplicate
accounting before the repair; I/O and actual task-batch reporting preserve the
budget afterwards. This fixes that counter race, while the cause of the existing
timing-sensitive high-load auto-scaling assertion remains unproven. See
common-utilization-accounting.md.

Six additional group cases exercise every delegate family, deterministic child
selection, pre-cancellation/argument checks, selector and queue failures,
NonSticky group-versus-child behavior, and a real two-worker group consumer.
Combined submission, progress, scheduling and original NonSticky tests pass
98 cases. Group submissions order initial invocation on their selected child;
they do not serialize an entire asynchronous operation across await suspension.
Keep a selected IEventExecutor when later work must access the same executor-owned
state: another submission to the group can select a different child.
The original NonSticky ordering workload now submits native delegates and joins
their Tasks with Task.WhenAll. It retains 10,000 tasks per producer, all four
batch sizes, the original concurrency/affinity assertions and every source
comment. This workload does not exercise JVM interruption semantics.

The native completion-observer boundary supports removal, not just a fixed
completion callback. AddressResolverGroup removes termination listeners when its
resolver group closes; DefaultChannelGroup removes a close listener when a channel
is removed. SslHandler's handshake completion cancels its scheduled timeout.
These pinned consumers require detachable callbacks without canceling the source
Task, prompt release of captured owners, executor invocation and registration
order. DefaultPromise removes pending listeners by identity; its already-claimed
notification snapshot can still run, and reentrant registrations follow that
snapshot. Independent ContinueWith calls do not by themselves establish all of
these policies. ExecutorCompletion now implements these policies with unique
disposable registration handles, BCL snapshot ownership and native queue reservations.
Its 29 contract cases include resolver-style removal and actual scheduler timeout
cancellation. Existing public Future/Promise methods are still present; do not infer
their migration from this native policy. See common-native-completion.md.

- Cancellation before invocation claims the queued work and releases the
  delegate/captured context, even while its empty work item still awaits dequeue.
- After invocation starts, cancellation is cooperative. A synchronous function
  returning normally succeeds. Throwing OperationCanceledException cancels only
  when its token matches the requested submission token; other exceptions fault.
- A Task-returning delegate is unwrapped. Its Task determines the eventual
  result/failure/cancellation after the delegate has started.
  Returning a null Task faults with InvalidOperationException in every async
  delegate family; it must not appear as cancellation. Four regression cases
  reproduced TaskCanceledException before the correction.
- Rejection faults the returned Task; invalid arguments throw synchronously.
- A single invocation claim arbitrates execution, queued cancellation and
  rejection. It is not a second result/terminal-state store.
- Cancellation registration uses UnsafeRegister and explicit unregistering.
  The cancellation callback does not access an incompletely published registration
  handle. An event loop never waits for a losing cancellation callback.
- Caller ExecutionContext is captured unless flow is suppressed. ExecutionContext.Run
  scopes invocation changes; AsyncLocal values cannot leak from one invocation
  into the worker/caller. Physical FastThreadLocal storage is unaffected.
- Delegate invocation is ordered on an ordered executor. Async suspension does
  not hold the executor or promise whole-operation serialization. Awaiting the
  returned Task does not confer event-loop affinity.

An async delegate has normal .NET await-context behavior. After external I/O,
executor-owned state is accessed by explicitly submitting work again:

```csharp
Task<int> operation = executor.SubmitAsync(async cancellationToken =>
{
    // Initial synchronous invocation is on the executor.
    byte[] response = await FetchAsync(cancellationToken).ConfigureAwait(false);
    return await executor.SubmitAsync(() => UpdateExecutorOwnedState(response));
}, cancellationToken);
int result = await operation;
```

The real DefaultEventExecutor consumer test proves that the executor processes
another job during the I/O wait, the suspended continuation can leave the loop,
and the explicit submission returns state access to the loop. It also verifies
graceful shutdown in its cleanup.

## Review and verification gates

The current target is net10.0. Verification is on Windows; this does not establish
other OS support. No performance improvement is claimed for these changes.

The native submission tests exercise all delegate families, original failure
identity, requested/mismatched tokens, queued-cancellation/invocation races,
context flow/suppression, async operation completion and early release of user
references. The constant registry tests force simultaneous factories and check
the published identity, creation-only rejection and concurrent ID allocation.

DefaultPromise's 33 original comment blocks and ConstantPool's 8 original comment
blocks remain in their source files. Original tests remain enabled in the default
suite. The temporary batch import was removed after all portable tests built by
default. The native additions supplement their behavior checks.

Initial completion/submission checkpoint (superseded by the current Task
composition checkpoint in common-porting.md):

- `dotnet test Netty.NET.sln -p:PortingBatch=true --no-restore`: Debug,
  956 passed / 14 skipped / 0 failed (970 total).
- The same command with `-c Release`: 956 passed / 14 skipped / 0 failed.
- `dotnet build Netty.NET.sln --no-restore`: at that checkpoint failed with 286
  diagnostics in AsciiStringCharacterTest. The later AsciiString checkpoint
  restores compilation; later native/heap memory checkpoints pass default full tests.

Detailed results are in the ignored TestResults directory, including
`clr-foundation-debug.trx`, `clr-foundation-release.trx`, and the before/after
completion-regression logs. Build/test analyzer warnings remain.

The preceding completion/progress/accounting checkpoint passes the default full Debug
and Release suites: 1287 passed / 0 failed / 14 skipped (1301 total) on Windows/net10.0.
Evidence: native-completion-termination-full-debug.trx and
native-completion-termination-full-release.trx. Initial runs failed an existing
auto-scaling timing assertion and a native observer-cancellation test precondition.
The independently reproduced accounting race is fixed; its role in the former
assertion remains unproven. The latter test now explicitly guarantees a pending
producer. Failed runs and distinctions remain in common-porting.md.
Remaining work
includes public API migration, executor backend/lifecycle decisions, raw
platform/encoding review and the pending source/test entries. Full test success
does not establish reviewed completeness of that work. See common-porting.md.

The subsequent AsyncMapping checkpoint migrates that interface to a provider-owned
Task<T> and optional cooperative CancellationToken, with input contravariance.
Native SNI-shaped consumers verify message ownership and explicit loop dispatch;
handler/TLS implementation remains outside this change. That checkpoint's default
Debug/Release results are 1297 passed / zero failed / 14 skipped (1311 discovered)
on Windows/net10.0. Evidence: async-mapping-full-debug-final.trx and
async-mapping-full-release-final.trx. Original comment coverage is zero missing
across 112 verified source/test entries. See common-native-async-mapping.md.

Completed-result classes and executor success/failure factories are now replaced
by standard Tasks. ExecutorCompletion retains the explicit callback policy;
DNS membership uses unique reservations instead of Task identity, and channel
owner/loop metadata remains in consumers. The current full Debug/Release matrix
on Windows/net10.0 each passes 1303 cases / zero failures / 14 skips (1317 total).
Evidence: completed-result-full-debug.trx and completed-result-full-release.trx.
At the completed-result checkpoint, three source entries move to clr-replacement;
all 109 then-verified entries
have zero missing comments. The three removed classes' 13 comments and the two
removed factory comments are also preserved. See common-completed-results.md.

Subsequent progress and external-producer checkpoints remove all executor
result factories. External producers create TCS and expose Tasks; notification
policy and invocation/cancellation claims remain separate. Native provider/pool
consumer tests and translated CLR scenarios are recorded in
common-native-producers.md. The manifest now has 98 verified source/test entries
and four additional progressive CLR replacements. Unused PromiseTask and Callable
glue are removed; see common-native-submission-wrapper-cleanup.md. DefaultPromise and
the remaining Future/Promise hierarchy are CLR replacements with native fixtures;
see common-native-future-retirement.md. Native queue ownership and graceful
quiet/timeout admission and native constructor configuration are implemented.
Concrete immediate stop uses StopToken/StopAsync and includes callback drain/failures;
see common-unordered-cooperative-stop.md. Shared/group immediate API and private queue
costs were open at that checkpoint; the subsequent local measurement is recorded
below. The concrete legacy scheduler/result backend has been removed.
The deprecated UnaryPromiseNotifier alias is also a standard Task/TCS replacement,
with no pinned caller; see common-task-composition.md for transfer coverage and
original comment provenance. No new result/listener facade is required.

The unordered private removal path now uses net10.0 PriorityQueue.Remove with
reference identity instead of copying/clearing/reinserting the whole heap. Measured
capacity reuse and public cancellation support keeping the BCL heap; SortedSet is
faster for large scattered removal but allocates nodes on reusable insertion.
See common-unordered-queue-costs.md for conditions, extrema and the remaining
contention/cancellation-heavy workload limits. Shared/group immediate APIs are
subsequently implemented below.

## Native stop across executors and groups

`IEventExecutorGroup.StopAsync()` requests the backend's stop policy and returns
its persistent `Termination` Task. Ordered workers close admission, drain accepted
invocations and cancel outstanding schedules; unordered workers withdraw waiting
work and request their explicit cooperative `StopToken`. No thread interruption
is injected. Yielded asynchronous bodies remain caller-owned, and canceling an
observer's `WaitAsync` does not cancel shutdown. NonSticky groups and selected
children forward to the underlying executor. Global/Immediate executors return
their existing failed lifecycle Task because they cannot terminate.

The pinned AbstractEventExecutor.java:85 delegates shutdownNow to shutdown;
SingleThreadEventExecutor.java:923 drains accepted invocations. The producer loop
in transport/NioEventLoopTest.java:204-238 needs admission closure and actual
termination, not withdrawal of every accepted ordered invocation. Native ordered
stop additionally escalates an existing graceful quiet period into admission
closure. This is an explicit CLR API decision: the pinned shutdown0 at line 782
ignores requests after graceful shutdown starts. Legacy entry points retain that
non-escalating policy. Checking the CAS state snapshot prevents a concurrent
graceful request from reopening admission after native stop.

Multithread groups request every child and retain the pinned termination counting
policy (MultithreadEventExecutorGroup.java:114-123): the group completes successfully
after every child signal completes, including failed children. A synchronous stop
request exception is collected independently; later children are still requested,
then an AggregateException reports those request failures. No second lifecycle
Task or Task.WhenAll replaces the original completion signal. Abstract custom
backends default to their shutdown primitive and may override the native policy.
ExecutorLifecycleContractTest covers these distinctions; current results and
remaining backend/API work are in common-porting.md.

## Ordered scheduler and indexed queue ownership

The indexed heap supplies O(log n) removal and mutable-priority repair with no
per-insertion tree node allocation. The pinned HTTP/2 distributor uses
priorityChanged (WeightedFairQueueByteDistributor.java:376) and separate
state-only/pseudo-time queue indices (lines 738-749). A BCL PriorityQueue stores
priority alongside each element; its reference-aware Remove scans membership.
SortedSet offers keyed removal but priority changes require removal/reinsertion
and allocate tree nodes. Retain the indexed heap for these requirements and use
BCL PriorityQueue for ordinary value/immutable entries. The CLR-only integer queue
scenario now uses that BCL type; there is no general linear-scan fallback in
DefaultPriorityQueue. Nodes must be references and implement indexed membership.

CLR membership checks the actual reference at the recorded index. The pinned
DefaultPriorityQueue.java uses node.equals at its contains helper; actual scheduler
and HTTP/2 nodes use reference identity, while CLR value-equal objects/records must
not remove another reservation through a stale index. Comparer order and per-queue
indices still determine priority repair. Equal-priority FIFO requires a sequence
tie breaker, as used by scheduled work; the generic heap adds none. Enumeration
remains live and unordered; toArray is a typed snapshot rather than Java's erased
array/iterator convenience overloads. BCL Array.Resize owns array capacity, capped
at Array.MaxLength; failed allocation cannot desynchronize a parallel capacity
field. Extreme OOM capacity limits are reviewed structurally, not forced in tests.

The pinned scheduler cancels a snapshot then calls clearIgnoringIndexes
(AbstractScheduledEventExecutor.java:170). That method explicitly assumes the
queue is about to be garbage collected and nodes will not be reused
(PriorityQueue.java clearIgnoringIndexes documentation). A stopped CLR executor can
remain reachable, retaining its queue and scheduled work in the backing array.
Shutdown now clears references and indices. A real retained executor with a
weakly observed canceled reservation reproduces retention before and collection
after the change. The terminal-only clearIgnoringIndexes API remains available
for its original specialized purpose; ordinary clear permits node reuse.

The reproducible bounded probe runs with -IndexedQueues in
tools/Measure-UnorderedQueueCosts.ps1, using an explicit Release library path.
Two sequential processes run after full tests, on Windows 10.0.26300 X64/.NET
10.0.7 (16 logical processors), with tiered compilation disabled only in the
probe, two warmups and seven samples per row. Random seed 42 selects 32 scattered
members; removal rows combine 32 hits and 32 repeated misses. Independent checks
cover ties, compact signed-clock wrap and hostile equality/foreign indices.
Reuse fill/drain rows divide by 2N push/pop calls. Setup and post-interval drain
are excluded; allocation is current-thread managed bytes only.

| At 16384 entries, median ranges across two processes | Indexed heap | BCL PriorityQueue | SortedSet |
| --- | --- | --- | --- |
| Mixed removal, ns/call | 35.94-37.50 | 23237.50-23570.31 | 242.19-262.50 |
| Reused fill/drain, ns/call | 139.87-141.56 | 123.17-124.24 | 155.52-155.94 |
| Reused fill/drain, bytes/call | 0 | 0 | 24 (48 per insertion) |

The BCL heap is faster for reused fill/drain in this probe, while indexed removal
avoids the scan and the tree's insertion allocation. This supports retaining the
indexed representation for mutable priorities/removal; it makes no overall
scheduler throughput, contention or cross-thread allocation claim. All sizes,
sample extrema and median allocations are in common-indexed-queue-costs.csv;
raw outputs and library hash are under ignored artifacts/indexed-queue-costs-*.
Current test results, inventory decisions and remaining work are in common-porting.md.

Relevant CLR specifications:

- [TaskCompletionSource producer/consumer separation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1?view=net-10.0).
- [Cooperative Task cancellation](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-cancellation).
- [GetOrAdd factory execution](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.getoradd?view=net-10.0).
- [UnsafeRegister context and immediate-callback behavior](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.unsaferegister?view=net-10.0).
- [Memory ownership, consumers and leases](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines).

The ordered chunk-cache consumer now has native atomic ceiling/conditional
removal and snapshot APIs. Original scenarios and CLR ownership/concurrency
regressions pass. Broader source review remains; see common-ordered-multimap.md.

The subsequent CLR worker replacement failure boundary is implemented in
[common-unordered-worker-failure.md](common-unordered-worker-failure.md).

## CLR replacements for concurrent sets and read-only iterators

ConcurrentSet.java is deprecated in the pinned source in favor of JDK concurrent
map key sets. An all-repository pinned search finds no consumer outside its own
class, and there is no C# class/caller/test to preserve. Use
ConcurrentDictionary<T, byte> for concurrent membership: TryAdd/TryRemove supply
the admission/removal result, ContainsKey/Count/Clear expose membership, and
pair enumeration supplies a concurrent view without a Java AbstractSet wrapper.
Choose the equality comparer for the domain; membership here is set equality,
not the indexed heap's reference ownership. A HashSet snapshot can supply set
algebra when needed. JVM Serializable/serialVersionUID creates no CLR requirement.
No ConcurrentSet facade is added and no unported consumer is claimed implemented.

ReadOnlyIterator.java exists to forward traversal while rejecting Java
Iterator.remove. Its actual consumers are transport ThreadPerChannelEventLoopGroup
(line 147) and AbstractChannelPoolMap (line 115); same-named nested HTTP header
iterators are separate classes. CLR IEnumerable<T>/IEnumerator<T> provide
traversal and disposal without a Remove member. They preserve this restriction
without an adapter and do not make the collection or referenced elements
immutable. Future channel/pool collections still own ordering, consistency,
mutation and resource policy. The existing common group enumeration contract
verifies that child traversal exposes no mutable collection; it does not port
those transport consumers. No ReadOnlyIterator facade is added.

Original comments at the pinned common/src/main/java/io/netty/util/internal/
ConcurrentSet.java and ReadOnlyIterator.java are preserved below. Both originals
share this identical license header; it is attributed to both files.

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

ConcurrentSet.java additionally contains these two documentation blocks. The
constructor actually creates its own map; the source wording is archived as written.

```java
/**
 * @deprecated For removal in Netty 4.2. Please use {@link ConcurrentHashMap#newKeySet()} instead
 */

/**
     * Creates a new instance which wraps the specified {@code map}.
     */
```

## Monotonic time and duration conversion

Pinned Ticker.java:24-84, SystemTicker.java:21-43 and MockTicker.java:25-50
separate elapsed nanoseconds from the raw initial timestamp. Scheduler deadlines,
SingleThreadEventExecutor work metrics and AutoScalingEventExecutorChooserFactory
consume elapsed time; transport I/O handlers pass raw System.nanoTime values to
subtract the same initial offset. EmbeddedEventLoop and ManualIoEventLoop choose
an executor clock. Preserve that boundary and signed long wrap instead of using
UTC, DateTime or a TimeSpan wall-clock value.

SystemTimer reads TimeProvider.System.GetTimestamp/TimestampFrequency. Integer
scaling avoids floating-point precision loss; an integral nanosecond multiplier
is cached for the ordinary clock path. Other ratios use integer division or Int128
intermediate multiplication, wrapping only the final timestamp. Duration conversion
is different: shared TimeUtil saturates TimeSpan's integer 100ns ticks and long
milliseconds to signed nanoseconds, matching the local Corretto 21.0.11
java.base/java/util/concurrent/TimeUnit.java toNanos contract. No double
TotalNanoseconds or intermediate TimeSpan.FromMilliseconds(long) is needed.
SystemTimer's existing public conversion constants remain available, but the
clock no longer uses them for floating-point conversion.

SystemTicker's positive waits round a submillisecond remainder up and split long
waits into Int32-millisecond Thread.Sleep chunks. Nonpositive waits return without
consuming a pending interrupt, matching TimeUnit.sleep. The native TimeSpan and
millisecond overloads preserve their full duration rather than first saturating
to the nanosecond horizon. Positive waits remain interruptible; timer resolution
and OS scheduling are not nanosecond accuracy guarantees. MockTicker advancement
saturates each converted duration, while accumulation still wraps as the original
AtomicLong does. Original TimeUnit comments are retained verbatim beside CLR notes.

Ticker remains the executor's elapsed-nanosecond and synchronous-wait policy.
The controlled mock supplies nanosecond advancement and blocking sleeper observation
that TimeProvider alone does not specify; inheriting TimeProvider's default timer
behavior would silently introduce real-clock timers into a controlled mock.

DefaultMockTicker implements the useful fair-wakeup policy with an explicit FIFO
tick queue. Each advance notifies registered sleepers in registration order; new
sleep phases, observers and subsequent advances wait for those notifications to
be processed. This prevents awaitSleepingThread from observing the stale old phase
after its deadline. CLR Monitor still supplies no general fair-mutex guarantee,
and application code after sleep returns has no FIFO execution guarantee. A reusable
LinkedListNode per sleeper avoids new tick-node allocation on every advance;
Interlocked owns the native long timestamp, HashSet with ReferenceEqualityComparer
owns thread membership, and interrupted sleepers remove registration and pending
notification nodes in finally. The original fairness comment remains alongside
the precise CLR policy note.

Advance entry and pending-notification waits preserve a consumed interrupt for the
next interruptible wait, as Java ReentrantLock.lock does; sleep and observer waits
remain interruptible. Four CLR regressions cover 128 consecutive sleep phases,
signed clock wrap, interrupted-registration cleanup and contended advance. The
before-run fails the stale phase and contended-advance rows. For the latter, the
test only holds the private gate to force contention and checks public clock/interrupt
outcomes. A separate probe compiled the five unchanged pinned Java sources
(four clock types plus ObjectUtil) with local Corretto 21.0.11; 20 x 128 consecutive
sleep phases pass. That confirms the expected original behavior, without claiming
to run the entire Java suite. The six original C# mock scenarios remain unchanged.

### Native provider selection for ordered scheduling

Ticker.FromTimeProvider accepts a CLR timestamp source. TimeProvider.System selects
the existing singleton and shared epoch. A custom selection captures a stable
positive TimestampFrequency and its native timestamp origin; its elapsed clock
starts at zero. It subtracts native ticks with signed wrap before integer scaling,
avoiding fractional-frequency origin rounding and native timestamp rollover errors.
The initialNanoTime metadata is the scaled captured origin. Raw-clock legacy helpers
must not mix system timestamps with a custom provider's epoch or units.

AbstractScheduledEventExecutor(parent, timeProvider) and the allocated-queue
SingleThreadEventExecutor core constructor select that clock; DefaultEventExecutor
exposes a public TimeProvider constructor. Existing constructors retain the system
singleton. Provider input is immutable for the executor, and the caller owns the
provider. Native Task scheduling, cancellation, deadline/tie ordering, metrics and
callback dispatch still use the existing executor state and queue.

Pinned transport EmbeddedEventLoop.java:43-45/90-92 selects a clock and runs due
work when driven. ManualIoEventLoop.java:145-154 explicitly requires manual wakeup
when the supplied clock advances faster than I/O system time. Preserve that contract:
provider advancement makes deadlines due, and the owner must pump/wake the executor
(e.g. submit ordinary work through SubmitAsync for a dedicated DefaultEventExecutor).
Do not dispatch callbacks from TimeProvider.CreateTimer or read GetUtcNow for deadlines.
A timestamp-only custom adapter rejects synchronous sleep, as the pinned embedded
FreezableTicker does. SystemTicker and DefaultMockTicker retain their explicit waits.

Eleven native provider rows cover system identity/null input, invalid frequencies,
nonzero/fractional-frequency origins, subnanosecond conversion, native tick wrap,
unsupported sleep and due/tie/affinity behavior on manual and actual dedicated
executors. Their provider throws if wall time or provider timers are accessed.
This reviews common ordered clock selection; it does not implement those transport
event loops or add provider injection to the pinned fixed-system unordered backend.

## Native primitive supplier delegates

BooleanSupplier.java and UncheckedBooleanSupplier.java become Func<bool>;
IntSupplier.java becomes Func<int>. CLR exceptions have no checked/unchecked
signature distinction, so an unchecked sub-interface adds no contract. A Func
can still throw; preserve the consumer's exception boundary rather than asserting
that the delegate cannot fail. Constant predicates are static () => true/false
at their consumer, without Java singleton implementation classes.

Pinned transport DefaultSelectStrategy.java:30-32 invokes its IntSupplier only
when hasTasks is true; preserve that lazy invocation and its result/exception.
NioIoHandler, EpollIoHandler and KQueueIoHandler also supply selector/poll callbacks.
RecvByteBufAllocator/DefaultMaxMessagesRecvByteBufAllocator and native receive
handles use UncheckedBooleanSupplier for lazy continueReading decisions. Preserve
short-circuit order, invocation counts and buffer/read budgets when those consumers
are ported. No direct BooleanSupplier import consumer exists outside its own class;
UncheckedBooleanSupplier is its meaningful subtype. The JDK java.util.function.
IntSupplier in common MpscIntQueue is a separate source type and queue review.

The four C# supplier/interface/constant-helper types have no callers in src/test
outside their own definitions and are removed. No IntSupplier C# type exists to
remove. Use native delegates when porting the actual transport consumers; their
implementations are not claimed by this common replacement. No replacement facade,
no-op, test exclusion or trivial test of BCL delegate invocation is added.
Original comments from the three pinned common/src/main/java/io/netty/util files
are preserved verbatim below, including licenses and constant-predicate contracts.

BooleanSupplier.java original comments:

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
 * Represents a supplier of {@code boolean}-valued results.
 */

/**
     * Gets a boolean value.
     * @return a boolean value.
     * @throws Exception If an exception occurs.
     */

/**
     * A supplier which always returns {@code false} and never throws.
     */

/**
     * A supplier which always returns {@code true} and never throws.
     */
```

UncheckedBooleanSupplier.java original comments:

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

/**
 * Represents a supplier of {@code boolean}-valued results which doesn't throw any checked exceptions.
 */

/**
     * Gets a boolean value.
     * @return a boolean value.
     */

/**
     * A supplier which always returns {@code false} and never throws.
     */

/**
     * A supplier which always returns {@code true} and never throws.
     */
```

IntSupplier.java original comments:

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
 * Represents a supplier of {@code int}-valued results.
 */

/**
     * Gets a result.
     *
     * @return a result
     */
```

## Native character-sequence equality comparers

The pinned HashingStrategy.java is a combined equality/hash contract. CLR
IEqualityComparer<T> already supplies that contract; remove IHashingStrategy<T>
and its Java hashCode alias. Its default JAVA_HASHER maps to
EqualityComparer<T>.Default at typed consumers, including CLR IEquatable<T>
dispatch. The unused DefaultHashingStrategy<T> helper has no C# callers and is
removed rather than kept as a second default-comparer implementation.

Pinned codec-base DefaultHeaders.java:95-142 injects a name comparer and chooses
JAVA_HASHER for its defaults. DefaultHttpHeaders, CombinedHttpHeaders and HTTP/2
CharSequenceMap/DefaultHttp2Headers choose AsciiString's specialized comparers.
Future codec/header consumers must accept IEqualityComparer<T>; their port is
outside this common change. Dictionary is useful for keyed storage, but this
decision does not replace ordered duplicate-header storage with Dictionary.

AsciiString.CASE_INSENSITIVE_HASHER and CASE_SENSITIVE_HASHER now expose native
IEqualityComparer<ICharSequence>. Their concrete implementations retain the
pinned AsciiString hash/content comparison algorithms and use GetHashCode/Equals
without the extra Java method. Only ASCII A-Z folds to a-z. StringComparer's
Unicode casing does not supply this protocol comparison. The sensitive comparer
intentionally retains the insensitive hash: unequal case variants can collide
and remain distinct keys. Slices and AsciiString/StringCharSequence/appendable
representations must produce the same hash for equal content.

Both comparers preserve null/null equality, one-null inequality and a zero null
hash. HashSet accepts null elements; Dictionary rejects null keys using the CLR
ArgumentNullException policy. Shared byte arrays and appendable sequences are
mutable: do not mutate keys while resident in either collection. Remove a shared
AsciiString key before mutation, call arrayChanged to reset its cached state,
and reinsert it. No comparer can repair a mutated resident key's bucket.

CharacterSequenceComparerContractTest exercises actual native collections,
mixed sliced representations, deliberate hash collisions, null policies, all
65,536 Latin-1 pairs with an independent ASCII-only reference, six non-ASCII
casing pairs, and safe removal/reset/reinsertion after backing-array mutation.
AsciiString's broader encoding/parsing/API review remains in progress.

HashingStrategy.java original comments, preserved verbatim from
common/src/main/java/io/netty/util/HashingStrategy.java at the pinned commit:

```java
/*
 * Copyright 2015 The Netty Project
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
 * Abstraction for hash code generation and equality comparison.
 */

/**
     * Generate a hash code for {@code obj}.
     * <p>
     * This method must obey the same relationship that {@link java.lang.Object#hashCode()} has with
     * {@link java.lang.Object#equals(Object)}:
     * <ul>
     * <li>Calling this method multiple times with the same {@code obj} should return the same result</li>
     * <li>If {@link #equals(Object, Object)} with parameters {@code a} and {@code b} returns {@code true}
     * then the return value for this method for parameters {@code a} and {@code b} must return the same result</li>
     * <li>If {@link #equals(Object, Object)} with parameters {@code a} and {@code b} returns {@code false}
     * then the return value for this method for parameters {@code a} and {@code b} does <strong>not</strong> have to
     * return different results results. However this property is desirable.</li>
     * <li>if {@code obj} is {@code null} then this method return {@code 0}</li>
     * </ul>
     */

/**
     * Returns {@code true} if the arguments are equal to each other and {@code false} otherwise.
     * This method has the following restrictions:
     * <ul>
     * <li><i>reflexive</i> - {@code equals(a, a)} should return true</li>
     * <li><i>symmetric</i> - {@code equals(a, b)} returns {@code true} if {@code equals(b, a)} returns
     * {@code true}</li>
     * <li><i>transitive</i> - if {@code equals(a, b)} returns {@code true} and {@code equals(a, c)} returns
     * {@code true} then {@code equals(b, c)} should also return {@code true}</li>
     * <li><i>consistent</i> - {@code equals(a, b)} should return the same result when called multiple times
     * assuming {@code a} and {@code b} remain unchanged relative to the comparison criteria</li>
     * <li>if {@code a} and {@code b} are both {@code null} then this method returns {@code true}</li>
     * <li>if {@code a} is {@code null} and {@code b} is non-{@code null}, or {@code a} is non-{@code null} and
     * {@code b} is {@code null} then this method returns {@code false}</li>
     * </ul>
     */

/**
     * A {@link HashingStrategy} which delegates to java's {@link Object#hashCode()}
     * and {@link Object#equals(Object)}.
     */
```

## Native byte-string integer parsing

Pinned AsciiString.java:1203-1336 supplies short/int/long parsing. Actual consumers
are codec-base's CharSequenceValueConverter.java:96-117 and HTTP response status
parsing in HttpResponseStatus.java:518-524. The common character/memory fixtures
have no integer parsing cases. Keep numeric results and the byte-string grammar:
radix 2..36, ASCII digits/letters, optional leading minus, leading zeros and full
input consumption. Plus, whitespace, non-ASCII bytes and radix prefixes are not
accepted. In higher radices, ordinary letters remain digits; no prefix recognition
is added. Native invariant decimal parsing alone cannot supply all these contracts.

ParseInt16/32/64 and matching TryParse methods replace the Java-named overloads;
the current CharUtil AsciiString consumer calls ParseInt64. Indexed overloads use
logical [start,end) ranges. A single private IBinaryInteger/IMinMaxValue core reads
ReadOnlySpan<byte> directly. Negative accumulation represents MinValue; bound
checks precede multiplication/subtraction, with identical checked-build behavior.
No Unicode Character.digit facade, string conversion or byte decoding is needed.
The broader CharUtil Unicode/string helpers remain a separate review.

Intentional CLR differences: malformed numbers throw FormatException; overflow
throws OverflowException; invalid radix or logical range throws
ArgumentOutOfRangeException. TryParse returns false and zero for numeric failures,
but still rejects invalid arguments. Exact exception messages are not preserved.
Pinned Java also reads beyond a logical view for an oversized end and returns zero
for a reversed range; executing the extracted methods reproduces both. Rejecting
those accidental outcomes preserves byte-view boundaries. All 99 original
AsciiString comments remain; the replaced integer block contains no comments.
Validation and bounded allocation/throughput evidence: common-porting.md.
The following decision covers floating-point grammar. The regex facade decision below and pending native sequence API/
future protocol consumers are separate reviews; neither numeric decision establishes
complete AsciiString equivalence.

## Native byte-string floating-point parsing

Pinned AsciiString.java:1338-1351 delegates to Float/Double.parseFloat/parseDouble;
codec-base's CharSequenceValueConverter.java:136-148 is the actual consumer.
DefaultHeadersTest.java:241-247/300-304 checks float/double header values, while
the original common character/memory fixtures have no floating-point cases.
The [Java 21 parsing contract](https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Double.html#valueOf(java.lang.String))
accepts signed decimal/hex literals, f/F/d/D suffixes, exact-case NaN/Infinity and
outer control/space bytes <=0x20. It rejects grouping separators, underscores,
internal whitespace, missing hex binary exponents and non-ASCII bytes.
Signed zero, overflow to infinity, underflow and nearest-even rounding are retained.

Native ParseSingle/ParseDouble and TryParse methods replace Java numeric names;
whole input and logical [start,end) slices share bounded byte-span parsing.
The private NumericSlice guard is shared with the already-reviewed integer APIs.
Decimals use [BCL UTF-8 span TryParse](https://learn.microsoft.com/en-us/dotnet/api/system.single.tryparse?view=net-10.0)
with explicit invariant culture and leading-sign/decimal-point/exponent styles;
a lexical scan prevents broader CLR-only acceptance. No text copy is needed.
Both suffix types are grammar markers, not intermediate conversion instructions:
Single rounds directly to Single, and Double directly to Double.

BCL decimal parsing cannot consume hexadecimal literals. A bounded private
converter retains precision+1 leading bits and a sticky tail, then rounds once
to the target IEEE format. It handles normal/subnormal boundaries, zero/overflow
ties and carries without double-to-float rounding. Exponent saturation at +/-2^60
is beyond any mantissa offset in an Int32-length span; all exponent characters
are still validated. Long mantissas/exponents need no growing arithmetic storage.

Invalid ranges throw ArgumentOutOfRangeException, including out-of-view empty
ranges that pinned toString bypasses. Malformed input throws FormatException or
TryParse returns false and positive zero. Range overflow is a successful infinity
result. NaN intentionally uses the native canonical NaN, whose raw sign bit differs
from Java's positive canonical NaN on this runtime; no Java NaN payload is invented.
The actual converter consumers observe numeric values, not NaN raw bits.
All 99 original AsciiString comments remain; the replaced four methods have none.
Independent Java raw-bit comparisons, controlled rounding tests, checked-build
validation and measured allocation/throughput evidence are in common-porting.md.

## Native regular expressions and literal string splitting

Pinned AsciiString.java:1048-1073 delegates matches(String) and split(String,int)
to java.util.regex.Pattern. All-module Java call/member-reference review finds
no consumers of these AsciiString facades. Actual calls use String/Pattern (HTTP,
SSL, DNS, resolver and protocol examples); the IP-rule matches calls are unrelated.
Current C# code likewise has no callers. The old C# matches searched for any
substring; split omitted max and inherited CLR capture/trailing-empty semantics.
Four of eight focused full-match inputs expose that incorrect compatibility claim.

Remove these unused facades and use standard Regex on AsciiString.ToString().
Callers choose native pattern syntax, options and failure/timeout policy. Whole
matching uses absolute anchors \A(?:pattern)\z, preserving alternation and
backtracking. Do not test the length of the first unanchored match. Byte protocol
character classes should be explicit ASCII ranges; CLR \w is Unicode, and its
default dot accepts CR where Java's does not. Native
[Regex.Split](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex.split?view=net-10.0)
retains captured delimiters and trailing empty fields, including zero-width-edge
empties; captured fields are outside its count limit. This is an explicit native
framework choice, not a Java Pattern compiler or general split-limit emulation.
Future protocol consumers must select their actual field policy when ported.

StringUtilTest.java:69-111's nine tests exercise inherited JDK String.split, not
Netty StringUtil. Retain names, literal inputs and every expected array/assertion
using native String.Split char separators/count. Where those cases drop trailing
fields, TrimEnd explicitly removes only the trailing delimiters, preserving
leading/interior empties. The escaped Java dot/dollar regexes are literal char
separators in C#. Delete the otherwise unused test-only JavaStringTestExtensions.
This preserves these scenarios; it is not an arbitrary-input Java splitting API.
The separate delimiter-range decision below covers native byte-view/lifetime
policy. All other StringUtil methods remain unchanged.

Thirteen native consumer cases verify full matching of logical views, lossless
Latin-1, cache invalidation, reusable Regex, invariant case/ASCII policy and native
capture/count/trailing/zero-width splitting. Executed Java decisions and validation
results are recorded in common-porting.md. The regex, delimiter and pattern
comment archives below plus remaining source comments preserve all 99.

Original AsciiString.java regex facade comments and delegated implementations:

```java
    /**
     * Determines whether this string matches a given regular expression.
     *
     * @param expr the regular expression to be matched.
     * @return {@code true} if the expression matches, otherwise {@code false}.
     * @throws PatternSyntaxException if the syntax of the supplied regular expression is not valid.
     * @throws NullPointerException if {@code expr} is {@code null}.
     */
    public boolean matches(String expr) {
        return Pattern.matches(expr, this);
    }

    /**
     * Splits this string using the supplied regular expression {@code expr}. The parameter {@code max} controls the
     * behavior how many times the pattern is applied to the string.
     *
     * @param expr the regular expression used to divide the string.
     * @param max the number of entries in the resulting array.
     * @return an array of Strings created by separating the string along matches of the regular expression.
     * @throws NullPointerException if {@code expr} is {@code null}.
     * @throws PatternSyntaxException if the syntax of the supplied regular expression is not valid.
     * @see Pattern#split(CharSequence, int)
     */
    public AsciiString[] split(String expr, int max) {
        return toAsciiStringArray(Pattern.compile(expr).split(this, max));
    }
```

## Native delimiter ranges and bounded character search

Pinned AsciiString.java:1075-1113 implements split(char) with a thread-local
list of shared AsciiString slices, preserving leading/interior empty fields and
dropping trailing empties (an empty input instead returns itself). All-module
call/member-reference review finds no caller; original AsciiString tests do not
call it either. Remove this unused facade instead of maintaining a cached list
and allocating wrapper/array results. Use native byte span splitting:

```csharp
ReadOnlyMemory<byte> memory = value.AsMemory();
foreach (Range range in memory.Span.Split((byte)',')) {
    var (start, length) = range.GetOffsetAndLength(memory.Length);
    ReadOnlyMemory<byte> field = memory.Slice(start, length); // Shared array view.
    // Copy field.ToArray() only when the consumer needs independent storage.
}
```

[MemoryExtensions.Split<T>](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.split?view=net-10.0)
enumerates ranges relative to the logical span without decoding bytes to text.
Native splitting retains all empty fields, including trailing ones; an empty
input has one empty range. Future protocol consumers must choose their actual
empty-field/limit policy. A byte separator represents Latin-1 directly; do not
truncate a UTF-16 character above 255 to a byte. StringCharSequence now exposes
AsSpan/AsMemory over its complete logical UTF-16 view for native char splitting
and search, retaining the immutable backing string without allocating substrings.

Ranges alone do not retain storage; borrowed Span/enumerators stay synchronous.
Ordinary array-backed Memory retains that array but shares mutations, whereas
ToArray detaches bytes. Views do not acquire a pooled/native ownership lease.
Parent text/hash caches still require arrayChanged after external mutations;
native byte searches read current storage directly without those caches.

Character search has actual mixed-sequence consumers: NetUtil's IPv6 scope
separator and CharUtil.SubstringAfter, plus pinned HTTP header scanning through
AsciiString.indexOf. Keep the current ICharSequence bridge until its coordinated
native API review. AsciiString and StringCharSequence now perform bounded native
span IndexOf; negative starts clamp to zero and starts at/beyond the logical end
return -1. Byte strings reject search characters above 255. In the pinned Java
and old CLR byte implementation, start+arrayOffset can overflow for large positive
starts on nonzero-offset views and read a negative array index. Returning -1 for
those positions preserves the intended logical search boundary, not the accidental
overflow exception. Native direct slicing retains the BCL's own range exceptions.

Independent byte scans, exact extracted Java methods and original search test
bodies, UTF-16 views, native empty-field policies and shared/copy lifetimes are
covered by the evidence recorded in common-porting.md. No generalized Java
split adapter is introduced. The original delimiter comments remain archived
below. The next decision covers pattern search and the slicing bridge;
ICharSequence/StringExtensions review stays open.

Original AsciiString.java delimiter facade and all four original comments:

```java
    /**
     * Splits the specified {@link String} with the specified delimiter..
     */
    public AsciiString[] split(char delim) {
        final List<AsciiString> res = InternalThreadLocalMap.get().arrayList();

        int start = 0;
        final int length = length();
        for (int i = start; i < length; i++) {
            if (charAt(i) == delim) {
                if (start == i) {
                    res.add(EMPTY_STRING);
                } else {
                    res.add(new AsciiString(value, start + arrayOffset(), i - start, false));
                }
                start = i + 1;
            }
        }

        if (start == 0) { // If no delimiter was found in the value
            res.add(this);
        } else {
            if (start != length) {
                // Add the last element if it's not empty.
                res.add(new AsciiString(value, start + arrayOffset(), length - start, false));
            } else {
                // Truncate trailing empty elements.
                for (int i = res.size() - 1; i >= 0; i--) {
                    if (res.get(i).isEmpty()) {
                        res.remove(i);
                    } else {
                        break;
                    }
                }
            }
        }

        return res.toArray(EmptyArrays.EMPTY_ASCII_STRINGS);
    }
```

## CLR reference-count fields and JVM field access

Pinned AbstractReferenceCounted.java delegates to RefCnt, whose Atomic, VarHandle
and Unsafe providers implement one shared-storage lifetime contract. The current
C# AbstractReferenceCounted already uses a real int with Volatile/Interlocked;
there is no separately ported RefCnt object. Its duplicate generic
ReferenceCountUpdater/AtomicIntegerFieldUpdater adapter is unused, stale relative
to the pinned algorithm, and contains five unimplemented field-update operations.
Replace it with static typed ref-int operations, and route AbstractReferenceCounted
through those operations. This purpose is also required by actual original
buffer/AbstractReferenceCountedByteBuf.java:27-88 and
buffer/AdaptivePoolingAllocator.java:1595/1669-1673: accessibility checks,
quiescent resets, retain/release and final-release deallocation. The reusable helper
is justified by those distinct owners; no buffer implementation is claimed here.

Initialize a caller-owned int field to one; always pass that same field by ref.
CLR managed interior references need no reflected byte offsets or pinning for
Interlocked. GetCount/IsLive use acquire reads; SetCount/Reset use release writes,
with direct mutation restricted to a quiescent state. IsLive remains a best-effort
guard and does not acquire a storage lease. CAS applies each accepted retain or
release exactly once. Release returns true only for the live-to-zero transition;
the storage owner invokes deallocation. Even a throwing deallocator leaves zero
terminal. Ordinary managed GC, native ownership, pins, and shared reference counts
retain their distinct roles. A NativeMemoryOwner integration case validates final
shared release, without claiming a production pool/buffer lease implementation.
Framework contracts: [Interlocked.CompareExchange](https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.compareexchange?view=net-10.0)
and [Volatile](https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile?view=net-10.0).

Use the full positive CLR Int32 range, keeping the existing C# count contract.
Zero is the released value; nonpositive direct settings now normalize to zero,
matching the original externally visible released state. Positive direct reset is
an explicit quiescent owner operation. Invalid/nonpositive increments/decrements,
overflow, excessive release and retain-after-release fail without changing the
count. Exception diagnostics report the actual observed native count. Do not copy
Java's doubled raw integer encoding, provider probes or transient get-and-add with
rollback: at 2^30 boundaries the pinned Java branches reject valid native counts
or wrap large decrements; its overflow-retain diagnostic always reports zero.
Those intentional language/runtime decisions are separated from ordinary behavior
in the executed Java/CLR oracle. The former C# negative setter defect has two
reproduced failures. Existing tests remain unchanged. Checked validation also
exposes an unchanged ThreadLocalRandom timestamp-seed narrowing overflow; make
that single conversion explicitly unchecked to retain its intended low-bit seed
in checked builds. The random adapter's broader native API review is separate.

Remove managed object-field-offset getObject/getInt/safeConstructPutInt/putObject/
objectFieldOffset and object-offset byte-write stubs from PlatformDependent and
PlatformDependent0 after replacing their only C# dependent adapter. Original
Java uses additionally include transport/NioIoHandler.java:192-200 (replacing JDK
Selector key sets) and handler/ssl/OpenSslX509TrustManagerWrapper.java:105-116,
179-181 (accessing private JDK SSLContext/trust-manager fields). Neither JVM
object layout exists on CLR. Future transport must use native socket completion
APIs and explicit queues; TLS must use its selected CLR/native provider's public
certificate-validation contract. Their behavior is still future module work,
not an implemented CLR Selector/SSLContext facade. No reflective GetValue fallback,
Marshal.OffsetOf on managed reference types, fixed GC field-layout assumptions,
or unsafe object-offset emulation is introduced. Raw native-address allocation,
word access, ordered native writes and mixed address copying remain a separate
pending review against NativeMemoryAllocator/Owner/View and real buffer consumers.
VarHandleFactory also supplies endian byte-memory views; its framework
replacement is now reviewed above, with native transport ordering still pending.

### Original replaced counter/provider comments

All pinned comments are retained below, including implementation notes about
Java's doubled raw value and its provider optimizations. They are provenance;
the CLR field stores the actual count, as described above.

RefCnt.java:

```java
/*
 * Copyright 2025 The Netty Project
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
 * Monomorphic reference counter implementation that always use the most efficient available atomic updater.
 * This implementation is easier for the JIT compiler to optimize,
 * compared to when {@link ReferenceCountUpdater} is used.
 */

/*
     * Implementation notes:
     *
     * For the updated int field:
     *   Even => "real" refcount is (refCnt >>> 1)
     *   Odd  => "real" refcount is 0
     *
     * This field is package-private so that the AtomicRefCnt implementation can reach it, even on native-image.
     */

/**
     * Returns the current reference count of the given {@code RefCnt} instance with a load acquire semantic.
     *
     * @param ref the target RefCnt instance
     * @return the reference count
     */

/**
     * Increases the reference count of the given {@code RefCnt} instance by 1.
     *
     * @param ref the target RefCnt instance
     */

/**
     * Increases the reference count of the given {@code RefCnt} instance by the specified increment.
     *
     * @param ref       the target RefCnt instance
     * @param increment the amount to increase the reference count by
     * @throws IllegalArgumentException if increment is not positive
     */

/**
     * Decreases the reference count of the given {@code RefCnt} instance by 1.
     *
     * @param ref the target RefCnt instance
     * @return true if the reference count became 0 and the object should be deallocated
     */

/**
     * Decreases the reference count of the given {@code RefCnt} instance by the specified decrement.
     *
     * @param ref       the target RefCnt instance
     * @param decrement the amount to decrease the reference count by
     * @return true if the reference count became 0 and the object should be deallocated
     * @throws IllegalArgumentException if decrement is not positive
     */

/**
     * Returns {@code true} if and only if the given reference counter is alive.
     * This method is useful to check if the object is alive without incurring the cost of a volatile read.
     *
     * @param ref the target RefCnt instance
     * @return {@code true} if alive
     */

/**
     * <strong>WARNING:</strong>
     * An unsafe operation that sets the reference count of the given {@code RefCnt} instance directly.
     *
     * @param ref    the target RefCnt instance
     * @param refCnt new reference count
     */

/**
     * Resets the reference count of the given {@code RefCnt} instance to 1.
     * <p>
     * <strong>Warning:</strong> This method uses release memory semantics, meaning the change may not be
     * immediately visible to other threads. It should only be used in quiescent states where no other
     * threads are accessing the reference count.
     *
     * @param ref the target RefCnt instance
     */

// oldRef & 0x80000001 stands for oldRef < 0 || oldRef is odd

// NOTE: we're optimizing for inlined and constant folded increment here -> which will make

// Integer.MAX_VALUE - increment to be computed at compile time

// oldRef & 0x80000001 stands for oldRef < 0 || oldRef is odd

// NOTE: we're optimizing for inlined and constant folded increment here -> which will make

// Integer.MAX_VALUE - increment to be computed at compile time

// fall-back

// oldRef & 0x80000001 stands for oldRef < 0 || oldRef is odd

// NOTE: we're optimizing for inlined and constant folded increment here -> which will make

// Integer.MAX_VALUE - increment to be computed at compile time
```

ReferenceCountUpdater.java:

```java
/*
 * Copyright 2019 The Netty Project
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
 * Common logic for {@link ReferenceCounted} implementations
 * @deprecated Instead of extending this class, prefer instead to include a {@link RefCnt} field and delegate to that.
 * This approach has better compatibility with Graal Native Image.
 */

/*
     * Implementation notes:
     *
     * For the updated int field:
     *   Even => "real" refcount is (refCnt >>> 1)
     *   Odd  => "real" refcount is 0
     */

/**
     * An unsafe operation that sets the reference count directly
     */

// overflow OK here

/**
     * Resets the reference count to 1
     */

// no need of a volatile set, it should happen in a quiescent state

// oldRef & 0x80000001 stands for oldRef < 0 || oldRef is odd

// NOTE: we're optimizing for inlined and constant folded increment here -> which will make

// Integer.MAX_VALUE - increment to be computed at compile time

// fall-back
```

AtomicReferenceCountUpdater.java:

```java
/*
 * Copyright 2025 The Netty Project
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

UnsafeReferenceCountUpdater.java:

```java
/*
 * Copyright 2025 The Netty Project
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

VarHandleReferenceCountUpdater.java:

```java
/*
 * Copyright 2025 The Netty Project
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

## ASCII trim and word conversion

Retain AsciiString trim/case transforms: HttpHeaders.java:1616-1647 and
HTTP/2 HttpConversionUtil.java:531-569 (HTTP/3:463-502) actually consume them for
comma/header values, TE and connection tokens. Trim means code units 0-32,
distinct from SP/HTAB-only StringUtil OWS. Native IndexOfAnyExceptInRange and
LastIndexOfAnyExceptInRange select logical byte/known-char boundaries; indexed
sequences retain bounded scanning. A subsequence end is exclusive. Correct the
pinned generic trim's dropped final character/invalid all-control range and its
nonzero-offset unchanged-view identity. Null maps to ArgumentNullException(c).
Unsigned CLR bytes keep 0x80-0xff; the pinned signed-byte instance trim removes
them incorrectly at edges. Character slices keep their representation's ownership:
immutable string views share storage, builder subsequences copy, byte trim shares
the same array even when empty. Mutation still requires caller cache/lifetime care.
Framework searches: [range start](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.indexofanyexceptinrange?view=net-10.0),
[range end](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.lastindexofanyexceptinrange?view=net-10.0).

Case conversion is protocol A-Z/a-z only, culture-independent with unchanged input
identity and detached changed arrays. System.Text.Ascii.ToLower stops on non-ASCII
data (executed A/80/Z: InvalidData, one byte written), so it cannot directly replace
the complete Latin-1 payload conversion. Retain bounded native MemoryMarshal word
operations/SWAR, with explicitly unchecked modular word arithmetic and native
BitOperations zero counts. No global unchecked index/range policy is added.
Remove the now-unused CLR-only BitOperators facade after whole-workspace and
pinned-module review; there is no upstream Netty class or original comment to archive.
Pinned ByteBufUtil.java:601-607/742-749 consumes pattern/first-index kernels through
indexed word reads; the future CLR buffer/search API remains separate. Kernel
verification does not establish that buffer integration. The Java unaligned short
tail sign-extends a negative low byte, corrupting its neighbor: 80 41 lowercases to
80 FF; CLR gives 80 61 and agrees with the Java scalar path. Keep that existing CLR
unsigned conversion rather than reproducing the optimized-path bug. Original locale
comments remain with protocol notes. SWAR's exact Folly URL casing and Utility
comment are restored; all 19 original SWAR comments are now present. Execution and
bounded allocation/timing evidence are in the ASCII transform checkpoint.
Native conversion reference: [Ascii.ToLower source](https://github.com/dotnet/dotnet/blob/e2c1e00b3d0f96afb892fb261d5921565b400246/src/runtime/src/libraries/System.Private.CoreLib/src/System/Text/Ascii.CaseConversion.cs).

## Native OWS and scaffolding

StringUtil.java:664-675/707-731 trims only SP and HTAB, retains unchanged input
identity and returns empty for an all-OWS value. Its private boundaries also drive
escapeCsv(CharSequence, boolean):373-455. Actual consumers include
codec-http CombinedHttpHeaders.java:78/102/114 for header lookup and CSV values.
Use native span Trim with the explicit nonempty " \t" character set; Unicode
whitespace, CR/LF, NUL, unpaired surrogates and quoted interior whitespace remain.
Use IndexOfAnyExcept/LastIndexOfAnyExcept for CSV boundaries, retaining the
start == length sentinel for empty/all-OWS input. Null arguments map Java NPE to
ArgumentNullException(value). All 67 StringUtil original comments stay in place.
Framework contracts: [Trim](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.trim),
[IndexOfAnyExcept](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.indexofanyexcept?view=net-10.0),
[LastIndexOfAnyExcept](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.lastindexofanyexcept?view=net-10.0).

CharUtil has no counterpart in the pinned Java modules. Retire its unused numeric,
split, whitespace, digit-table, control/code-point and search facades; preserve
the actual general comparison bridge verbatim. The only noncomparison callers
were two CLR Porting assertions of ParseLong and two SubstringAfter assertions
over three representations. Move these to AsciiString.ParseInt64 and the actual
StringUtil.substringAfter with explicit fixture string materialization, retaining
names, inputs, expected results and assertion counts. No new compatibility API
or Character table is introduced, and no removed CharUtil comment represents
pinned Netty code. This public scaffolding removal is a source API change in
the unfinished port.
Execution evidence and remaining work are in the OWS/scaffolding checkpoint.

## Native UTF-16 comparison and ASCII protocol comparison

StringCharSequence and AppendableCharSequence contentEquals now compares exact
UTF-16 content. The previous CLR-only CharUtil helper incorrectly ignored case.
General ignore-case content/region comparisons use native OrdinalIgnoreCase,
independent of CurrentCulture, with one policy for strings, builders and indexed
fallback sequences. Known logical char views use MemoryExtensions.Equals directly.
The indexed fallback compares complete surrogate pairs with the BCL using two
stack-allocated code units, without converting the sequence to a string or bytes.
This explicitly chooses CLR Unicode casing rather than reproducing Java Character
tables: dotted/dotless I and supplementary case pairs may differ from Java's generic
per-char comparator. No normalization, culture comparison or multi-character case
expansion is added. A selected region never reads outside its logical bounds.

Preserve AsciiString's byte-oriented instance comparison and the explicitly ASCII
static content/region/contains APIs: only A-Z fold to a-z; non-ASCII bytes/code units
must match exactly. Correct the CLR ASCII comparator which used Unicode lowercasing.
General AsciiString.regionMatches keeps the pinned byte-receiver dispatch, but
routes other receiver representations through native general comparison. The
original general per-char comparator is replaced by BCL comparison and its comment
is archived below. The remaining ASCII comparator still has actual protocol use.

The bridge keeps false for invalid region bounds and its original nonpositive
length rule after bounds checks; no Java range wrapper is introduced for native
Span slicing. Instance region null arguments throw ArgumentNullException;
the static region dispatcher retains false for null inputs. Two unused CLR-only
CharUtil string overloads are removed after caller review. Its unused noncomparison
helpers are retired in the Native OWS and scaffolding decision; the whole
ICharSequence public API remains separate.

StringCharSequence object equality is confined to immutable StringCharSequence
values, using exact content and matching native hashes. Cross-type content remains
an explicit contentEquals/comparer operation; default object equality must not
claim equality with a byte string or mutable builder that returns false in reverse
or has a different hash policy. The existing ASCII IEqualityComparer fields remain
the explicit shared-header collection policy. Native string.GetHashCode over a
logical span replaces allocating ToString in StringCharSequence/Appendable hashing;
hashes are runtime/process values, not persistent or Java-compatible identifiers.

AppendableCharSequence.AsSpan exposes only the current logical length as a
synchronous borrowed view. Consume before append/reset/setLength. A previously
returned view does not track later length, mutations or replacement buffers, and
does not acquire an ownership lease or become safe for concurrent mutation.
No asynchronous memory view is introduced. Mutable builder hashes must not be
used as stable resident collection keys while contents change.

Checked validation also exposes missing wrap annotations in the existing ASCII
hash path. Keep the pinned Netty hash/collection algorithm: mark only hash multiply/
add expressions and 16-bit signed word reinterpretation as unchecked. Offsets and
range arithmetic are not globally unchecked. The native general string span hash
remains separate from Netty's explicitly ASCII hash. Exact Java hash methods on
540 deterministic byte/slice inputs agree on Windows little-endian order; this
does not claim execution on a big-endian CLR host. The original 1000-length hash
fixture and shared-comparer collection cases pass checked Release without changing
their assertions, workloads or inputs.

Framework APIs: [MemoryExtensions.Equals](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.equals?view=net-10.0)
and [String.GetHashCode(ReadOnlySpan<char>, StringComparison)](https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0).
Executed source/native comparisons, regressions, full test outcomes and input-specific
allocation measurements are recorded in common-porting.md. This completes this
comparison unit, not the whole common port or memory ownership/API redesign.

Original AsciiString.java:1567-1578 nested general comparator provenance:

```java
    private static final class GeneralCaseInsensitiveCharEqualityComparator implements CharEqualityComparator {
        static final GeneralCaseInsensitiveCharEqualityComparator
                INSTANCE = new GeneralCaseInsensitiveCharEqualityComparator();
        private GeneralCaseInsensitiveCharEqualityComparator() { }

        @Override
        public boolean equals(char a, char b) {
            //For motivation, why we need two checks, see comment in String#regionMatches
            return Character.toUpperCase(a) == Character.toUpperCase(b) ||
                Character.toLowerCase(a) == Character.toLowerCase(b);
        }
    }
```

## Native string consumers

Remove the CLR-only StringExtensions facade after migrating every compiler-confirmed
string call in the default production/test projects. Strings use Length, the UTF-16
indexer, native exclusive-end ranges, char IndexOf/LastIndexOf and explicit
OrdinalIgnoreCase equality. Existing ICharSequence calls remain separate; this
change does not remove its mixed byte/char contract or finish its public API review.
No original Java class or comment is represented by StringExtensions.cs itself.
The original comments remain in each affected Netty class and fixture.

StringUtil.commonSuffixOfLength compares bounded UTF-16 spans with SequenceEqual,
without allocating two temporary substrings. Null inputs, negative lengths and
lengths beyond either input return false; zero length matches two non-null inputs.
Validate before subtraction, including checked int.MinValue/int.MaxValue inputs.
DomainNameMapping.matches recognizes the literal wildcard prefix with Ordinal
StartsWith and compares raw UTF-16 spans. Preserve the pinned regionMatches rule:
a host shorter than the template tail can match its prefix, including an empty
host. Do not silently tighten that behavior to equal lengths. Suffix matching
remains case-sensitive; hostname normalization/IDNA is outside this change.
The initial soft-hyphen culture hypothesis did not reproduce on this runtime;
the two culture cases are preservation checks, not a demonstrated defect repair.

Native String.IndexOf(char,startIndex) rejects starts beyond the string. NetUtil's
IPv4 dot search instead uses a parser-local FindDot over a native suffix span and
returns -1 at/past the end. Its IPv6 embedded-IPv4 scope search clamps the suffix
window to the string end before native IndexOf. Preserve the original full-string
search window, including scope/bracket suffixes, rather than changing parser bounds.
These are Netty parser decisions, not public Java string compatibility extensions.
The mixed-sequence NetUtil entry points still convert their input to strings; a
native borrowed-view/ownership redesign is not claimed here.

Existing fixtures retain identities, assertions, inputs and original comments.
The two CLR-only extension boundary assertions use explicit native span windows;
eight new actual IP consumer cases cover truncated tails. Comparison with exact
pinned Java methods, checked/full runs and warm allocation evidence are recorded
in common-porting.md. The suffix allocation measurement is input-specific, with
no general throughput claim. Common and the remaining mixed-sequence API work
remain in progress.

## Native pattern windows and sequence slicing

Pinned all-module call/member-reference review finds no consumers of AsciiString's
instance contains(CharSequence), indexOf(CharSequence) or lastIndexOf(CharSequence).
The forward API is used only by the unused contains delegate; the reverse API is
used only by its original fixture. Protocol contains calls are String/collections/
headers (HttpObjectDecoder's newProtocol is a String); their semantics are separate.
Remove all five instance facades and the CLR StringCharSequence.indexOf(string),
which has only fixture consumers.
Byte consumers use AsSpan().IndexOf/LastIndexOf(pattern), and UTF-16 consumers
use ordinal native span search. No extra public search wrapper or byte-to-string
conversion is needed. ASCII protocol patterns may be u8 literals; non-ASCII UTF-8
is not Latin-1 and must not be substituted for losslessly widened byte content.

Native searches return indexes relative to their selected span. A consumer maps a
found suffix index back to its logical offset and keeps -1 unchanged. For a reverse
search with a maximum candidate start, the prefix window must also include the
pattern's length. Empty patterns use the native first/last endpoint rule; explicit
window selection defines any clamping. Native Slice rejects invalid ranges.
The two original search fixtures retain all input values, expected indexes, 50
assertions, names and comments, using fixture-local native window consumers with
ASCII byte literals. Those helpers are not library compatibility APIs and do not
validate arbitrary nullable CharSequence inputs or preserve removed facade exceptions.

subSequence remains an ICharSequence bridge for actual hex-error reporting
(StringUtil.decodeHexByte) and pinned HTTP/header slicing. Its endpoint guard now runs
before end-start, producing ArgumentOutOfRangeException in checked/unchecked builds.
Partial copy=true slices own detached arrays; copy=false shares the logical bytes.
Full-range requests keep the pinned source identity even with copy=true; empty
partial slices use EMPTY_STRING. Native Memory/Span slicing selects views directly,
and ToArray explicitly copies the full range when required. Separate borrowed
AsciiString text/hash caches require separate invalidation after shared mutations.
This bridge does not acquire pooled/native storage leases or finish the coordinated
ICharSequence public API review. Existing StringCharSequence immutable views share
their backing string and reject invalid endpoints before arithmetic already.

Validation and executed pinned source comparisons are in common-porting.md.
All eight original removed comments below join the six prior archived comments
and remaining 85 source comments to preserve AsciiString's 99-comment coverage.

Original public boolean contains(CharSequence cs) comments, AsciiString.java:427-435:

```java
/**
     * Determines if this {@code String} contains the sequence of characters in the {@code CharSequence} passed.
     *
     * @param cs the character sequence to search for.
     * @return {@code true} if the sequence of characters are contained in this string, otherwise {@code false}.
     */
```

Original public int indexOf(CharSequence string) comments, AsciiString.java:673-684:

```java
/**
     * Searches in this string for the first index of the specified string. The search for the string starts at the
     * beginning and moves towards the end of this string.
     *
     * @param string the string to find.
     * @return the index of the first character of the specified string in this string, -1 if the specified string is
     *         not a substring.
     * @throws NullPointerException if {@code string} is {@code null}.
     */
```

Original public int indexOf(CharSequence subString, int start) comments, AsciiString.java:686-726:

```java
/**
     * Searches in this string for the index of the specified string. The search for the string starts at the specified
     * offset and moves towards the end of this string.
     *
     * @param subString the string to find.
     * @param start the starting offset.
     * @return the index of the first character of the specified string in this string, -1 if the specified string is
     *         not a substring.
     * @throws NullPointerException if {@code subString} is {@code null}.
     */

// Intentionally empty
```

Original public int lastIndexOf(CharSequence string) comments, AsciiString.java:756-768:

```java
/**
     * Searches in this string for the last index of the specified string. The search for the string starts at the end
     * and moves towards the beginning of this string.
     *
     * @param string the string to find.
     * @return the index of the first character of the specified string in this string, -1 if the specified string is
     *         not a substring.
     * @throws NullPointerException if {@code string} is {@code null}.
     */

// Use count instead of count - 1 so lastIndexOf("") answers count
```

Original public int lastIndexOf(CharSequence subString, int start) comments, AsciiString.java:770-807:

```java
/**
     * Searches in this string for the index of the specified string. The search for the string starts at the specified
     * offset and moves towards the beginning of this string.
     *
     * @param subString the string to find.
     * @param start the starting offset.
     * @return the index of the first character of the specified string in this string , -1 if the specified string is
     *         not a substring.
     * @throws NullPointerException if {@code subString} is {@code null}.
     */

// Intentionally empty
```

## Native encoding and codec ownership

CharsetUtil.java supplies Java charset constants and cached/reset CharsetEncoder/
CharsetDecoder objects. Actual pinned consumers are AsciiString.java:199/246,
ByteBufUtil.java:73/1319 (replacement encoding), and ByteBufUtil.java:1805
(strict text validation). No C# production consumer uses CharsetUtil: AsciiString
already accepts caller-selected Encoding and uses span-based one-shot GetBytes.
Remove the unused Java utility and its two otherwise unused InternalThreadLocalMap
codec caches. Its three test references migrate directly to native encodings;
the two original encoding loops still execute six configurations and retain
all assertions/iterations/comments. No Java-shaped replacement utility is added.

The typed CLR configuration is Encoding, EncoderFallback and DecoderFallback.
One-shot construction uses Encoding.GetByteCount/GetBytes without global mutable
codec state. Incremental protocol consumers must own an Encoder/Decoder per
independent operation and flush/reset that operation explicitly, rather than
sharing a thread-local codec across nested operations or await continuations.
Buffer/codec consumers have not been ported by this common framework decision.

| Pinned Java charset/policy | CLR choice and explicit difference |
| --- | --- |
| UTF-16 | Choose big-/little-endian UnicodeEncoding explicitly. Java defaults to big-endian, emits FE FF for nonempty encoded input and detects BOM on decoding. CLR raw GetBytes does not prepend GetPreamble, and raw GetString does not perform BOM-driven endian selection. A framed protocol must write the chosen preamble once and select decoding byte order from its framing; StreamReader BOM detection is a text-stream policy. Do not alias Java UTF-16 to Encoding.Unicode. |
| UTF-16BE / UTF-16LE | new UnicodeEncoding(true, false) / new UnicodeEncoding(false, false) provide the byte orders without an implicit wire preamble. |
| UTF-8 | new UTF8Encoding(false, true) provides strict encoding/decoding; an explicit replacement fallback is a separate caller policy. GetBytes omits the preamble even when an Encoding advertises one. |
| ISO-8859-1 / US-ASCII | Encoding.Latin1 / Encoding.ASCII are native inputs. Explicit '?' EncoderReplacementFallback emits two '?' bytes for an unmappable surrogate pair on CLR, versus one for the Java encoders. A valid representable input has the same payload bytes. |
| Malformed and unmappable actions | CLR exposes one fallback policy per direction, including caller-defined fallback implementations. It has no pair of built-in Java CodingErrorAction settings. Remove the old overload that silently ignored its second argument. Encoding clones can configure ExceptionFallback, a specific replacement or a custom fallback without changing a shared Encoding instance. A caller needing distinct actions must implement that policy explicitly. |
| Default replacements | Java UTF-8 replacement encoding emits '?' for a lone surrogate; CLR UTF-8 defaults to U+FFFD (EF BF BD). Java decoding defaults to U+FFFD, whereas generic DecoderFallback.ReplacementFallback uses '?'. Preserve the supplied Encoding's fallback, and choose replacement bytes/text deliberately at each protocol boundary. |

The pinned CharsetUtil source was executed with Corretto 21.0.11 using minimal
ObjectUtil/fresh-map harness dependencies. The harness does not exercise Netty's
thread-local cache implementation. encoding-java-oracle.txt records six encoding
configurations, empty/malformed input, distinct REPORT/REPLACE actions and invalid
UTF-8 decoding. A separate net10.0 program calls BCL Encoding directly and records
preambles, valid/empty/malformed bytes in encoding-clr-oracle.txt (runtime 10.0.7).
Both oracle sources remain in ignored artifacts/encoding-validation; the pinned
source checkout is unchanged. They establish the differences above, rather than
claiming malformed/unmappable replacement and automatic UTF-16 framing are equal.

EncodingConstructorContractTest checks seventeen actual AsciiString integration
cases with literal expected bytes: six native configurations across string/span/
char-array/sliced-sequence inputs, no automatic preamble even for empty input,
custom and strict fallbacks, no policy change/state contamination after failure,
invalid text outside the selected range, and no hidden thread-local codec state.
Broader string parsing, sequence APIs and future streaming protocol integration
remain separate work. Original CharsetUtil comments are preserved verbatim below.

CharsetUtil.java original comments from
common/src/main/java/io/netty/util/CharsetUtil.java at the pinned commit:

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

/**
 * A utility class that provides various common operations and constants
 * related with {@link Charset} and its relevant classes.
 */

/**
     * 16-bit UTF (UCS Transformation Format) whose byte order is identified by
     * an optional byte-order mark
     */

/**
     * 16-bit UTF (UCS Transformation Format) whose byte order is big-endian
     */

/**
     * 16-bit UTF (UCS Transformation Format) whose byte order is little-endian
     */

/**
     * 8-bit UTF (UCS Transformation Format)
     */

/**
     * ISO Latin Alphabet No. 1, as known as <tt>ISO-LATIN-1</tt>
     */

/**
     * 7-bit ASCII, as known as ISO646-US or the Basic Latin block of the
     * Unicode character set
     */

/**
     * @deprecated Use {@link #encoder(Charset)}.
     */

/**
     * Returns a new {@link CharsetEncoder} for the {@link Charset} with specified error actions.
     *
     * @param charset The specified charset
     * @param malformedInputAction The encoder's action for malformed-input errors
     * @param unmappableCharacterAction The encoder's action for unmappable-character errors
     * @return The encoder for the specified {@code charset}
     */

/**
     * Returns a new {@link CharsetEncoder} for the {@link Charset} with the specified error action.
     *
     * @param charset The specified charset
     * @param codingErrorAction The encoder's action for malformed-input and unmappable-character errors
     * @return The encoder for the specified {@code charset}
     */

/**
     * Returns a cached thread-local {@link CharsetEncoder} for the specified {@link Charset}.
     *
     * @param charset The specified charset
     * @return The encoder for the specified {@code charset}
     */

/**
     * @deprecated Use {@link #decoder(Charset)}.
     */

/**
     * Returns a new {@link CharsetDecoder} for the {@link Charset} with specified error actions.
     *
     * @param charset The specified charset
     * @param malformedInputAction The decoder's action for malformed-input errors
     * @param unmappableCharacterAction The decoder's action for unmappable-character errors
     * @return The decoder for the specified {@code charset}
     */

/**
     * Returns a new {@link CharsetDecoder} for the {@link Charset} with the specified error action.
     *
     * @param charset The specified charset
     * @param codingErrorAction The decoder's action for malformed-input and unmappable-character errors
     * @return The decoder for the specified {@code charset}
     */

/**
     * Returns a cached thread-local {@link CharsetDecoder} for the specified {@link Charset}.
     *
     * @param charset The specified charset
     * @return The decoder for the specified {@code charset}
     */
```


## Native concurrent membership and map construction

Pinned ResourceLeakDetector.java:169-172, 329, 418-436 and 511-518 uses
ConcurrentHashMap key sets only for tracker membership and report deduplication.
The actual CLR owners now hold ConcurrentDictionary<DefaultResourceLeak<T>,byte>
with ReferenceEqualityComparer.Instance and ConcurrentDictionary<string,byte>
with StringComparer.Ordinal. TryAdd/TryRemove preserve single-key atomic claims:
exactly one Close/Dispose wins, and each report text is published once. No
check-then-act sequence, enumeration, set algebra or callback factory is needed.
Keep the existing GC registration, record atomics, reachability fence and
report/listener sequence. Existing concurrent-close, duplicate-report, disabled
reporting and multi-tracker lifetime contracts exercise these owners.

The C#-only ConcurrentHashSet and its unused IsEmpty overload retire. Their
unconsumed snapshot/set-algebra surface supplies no Netty common contract and
would require an additional comparer/atomicity policy. This applies the earlier
concurrent-set decision to the actual owner rather than adding another facade.

Pinned PlatformDependent.java:558-612 already deprecates all five map factories
in favor of direct ConcurrentHashMap construction. No tracked C# caller uses
these wrappers; three CLR overloads ignored capacity/load-factor/concurrency
arguments. Construct ConcurrentDictionary directly with the owner's comparer
and relevant native constructor. Java load factor has no corresponding CLR
constructor contract; validate/adapt future public input at its owning boundary.
No compatibility factory silently discards that input.

The remaining pinned all-module production caller is HTTP/3's
Http3ServerPushStreamManager.java:74-79 (initial push-stream capacity hint).
Its later computeIfPresent callback closes a stream and returns null to remove
the key. That atomic per-key transition must receive an explicit owner policy
when HTTP/3 is ported: ConcurrentDictionary GetOrAdd/AddOrUpdate factories may
run repeatedly and do not provide Java's side-effecting callback guarantee.
This common constructor retirement does not certify that downstream transition.

NormalizeRuntime is an unused C#-only product/backend classifier with no pinned
Java counterpart. FrameworkDescription or Environment.Version directly serves
runtime diagnostics; guessing CoreCLR/Mono/Unity from a product string or type
lookup does not establish the selected backend. Remove that classifier without
adding a replacement probe API. Whole PlatformDependent/common review stays open.

Original five Java Javadocs, the prior C# adaptations and retired helper comments
remain verbatim below. No original leak comments or portable tests are removed.

```java
/**
     * Creates a new fastest {@link ConcurrentMap} implementation for the current platform.
     * @deprecated please use new ConcurrentHashMap<K, V>() directly.
     */

/**
     * Creates a new fastest {@link ConcurrentMap} implementation for the current platform.
     * @deprecated please use new ConcurrentHashMap<K, V>() directly.
     */

/**
     * Creates a new fastest {@link ConcurrentMap} implementation for the current platform.
     * @deprecated please use new ConcurrentHashMap<K, V>() directly.
     */

/**
     * Creates a new fastest {@link ConcurrentMap} implementation for the current platform.
     * @deprecated please use new ConcurrentHashMap<K, V>() directly.
     */

/**
     * Creates a new fastest {@link ConcurrentMap} implementation for the current platform.
     * @deprecated please use new ConcurrentHashMap<K, V>() directly.
     */

/**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */

/**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */

/**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */

/**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */

/**
     * Creates a new fastest {@link ConcurrentDictionary} implementation for the current platform.
     * @deprecated please use new ConcurrentDictionary<K, V>() directly.
     */

// dotnet version

// 2 runtime check

// fallback (NativeAOT, Wasm 등)

/// <summary>

/// Represents a thread-safe, unordered collection of unique items.

/// </summary>

/// <typeparam name="T">The type of elements in the hash set.</typeparam>
```


## Native random consumers and scalar counters

Pinned deprecated ThreadLocalRandom.java owns JVM seed properties, entropy
bootstrap/timeout, interrupt restoration, an LCG, padding and bounded helpers.
No all-module consumer calls its seed setter; only its original common test
calls the seed getter. InternalThreadLocalMap.random and PlatformDependent's
deprecated accessor are the only production entries into that obsolete class.
Pinned ordinary common consumers use JDK ThreadLocalRandom for leak sampling,
record backoff and MacAddressUtil fallback; reference-count, bounded-stream and
priority-queue fixtures use it for data generation.

These CLR consumers now call Random.Shared directly. The installed net10.0
reference System.Runtime.xml P:System.Random.Shared explicitly guarantees use
concurrently from any thread. Retire the hand-seeded thread-static adapter,
the shadowed NextBytes extension and the unused map/platform accessors. No new
provider facade, seed property, background entropy thread or TLS lifecycle is
needed. Range validation at the leak sampling boundary and backoff shift cap
remain. Random values need not match Java's algorithm or sequence; this is a
native nondeterministic source. Explicit new Random(seed) remains the choice
for repeatable fixtures. The original dedicated-thread interruption scenario
now performs native generation and still consumes the pending interrupt only
at its test sleep. Existing original workloads/assertions remain otherwise.

Native integer/buffer operations cover actual consumers: Next(min,max),
Next(bound), NextBytes and, for future long ranges, NextInt64. Both runtimes use
an exclusive upper bound for ordinary positive bounds; Java rejects bound zero,
where native Next(0) returns zero, so retain the owning positive-bound guard.
Do not invent unused Gaussian or scaled floating-point helpers from the Java
class. Its long-range subtraction/LCG/seed-property quirks have no common caller.
Shared is a pseudorandom source, not a cryptographic API. Pinned handler's
ThreadLocalInsecureRandom.java actually imports JDK ThreadLocalRandom and is
explicitly insecure; its stale Javadoc names the retired platform accessor.
Future TLS/certificate security requirements require RandomNumberGenerator at
their owner, and Gaussian/other distribution needs require a separate consumer
decision. No handler behavior or random-quality/performance equivalence is claimed.

LongCounter and deprecated LongAdderCounter have no actual all-module consumer
outside their own definitions and PlatformDependent.newLongCounter. PoolArena's
two comments still mention LongCounter, but its live fields use JDK LongAdder.
Retain that downstream statistical-counter purpose, without resurrecting an
unused common interface/provider. Native long fields with Interlocked Add,
Increment, Decrement and Read provide atomic scalar updates/snapshots. They are
stronger snapshots than Java LongAdder's concurrent sum and do not promise its
striped contention cost. Actual common LeakPresenceDetector already uses native
Interlocked counting/exchange with the original quiescent-producer requirement;
its concurrent exactly-once contract remains the validation for that owner.
Allocator reservation is a separate hard-limit CAS policy and must not become
an approximate/statistical counter. Future arena metrics can use native scalar
owners initially; any striped optimization requires measured contention and an
explicit snapshot/reset policy. No new counter type or speculative test is added.

All 35 original random comments, both comments of each counter source, the
counter factory Javadoc and retired C# explanatory/accessor comments follow.
The three source entries use CLR replacement; other whole-source reviews remain
open. The prior internal-map lifecycle/counter placeholders are outside this
random-accessor change.

```java
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

/*
 * Written by Doug Lea with assistance from members of JCP JSR-166
 * Expert Group and released to the public domain, as explained at
 * https://creativecommons.org/publicdomain/zero/1.0/
 */

/**
 * A random number generator isolated to the current thread.  Like the
 * global {@link java.util.Random} generator used by the {@link
 * java.lang.Math} class, a {@code ThreadLocalRandom} is initialized
 * with an internally generated seed that may not otherwise be
 * modified. When applicable, use of {@code ThreadLocalRandom} rather
 * than shared {@code Random} objects in concurrent programs will
 * typically encounter much less overhead and contention.  Use of
 * {@code ThreadLocalRandom} is particularly appropriate when multiple
 * tasks (for example, each a {@link io.netty.util.internal.chmv8.ForkJoinTask}) use random numbers
 * in parallel in thread pools.
 *
 * <p>Usages of this class should typically be of the form:
 * {@code ThreadLocalRandom.current().nextX(...)} (where
 * {@code X} is {@code Int}, {@code Long}, etc).
 * When all usages are of this form, it is never possible to
 * accidentally share a {@code ThreadLocalRandom} across multiple threads.
 *
 * <p>This class also provides additional commonly used bounded random
 * generation methods.
 *
 * //since 1.7
 * //author Doug Lea
 */

// Try to generate a real random number from /dev/random.

// Get from a different thread to avoid blocking indefinitely on a machine without much entropy.

// Get the real random seed from /dev/random

// Use the value set via the setter.

// Get the random seed from the generator thread with timeout.

// Just in case the initialSeedUniquifier is zero or some other constant

// just a meaningless random number

// Restore the interrupt status because we don't know how to/don't need to handle it here.

// Interrupt the generator thread if it's still running,

// in the hope that the SecureRandom provider raises an exception on interruption.

// L'Ecuyer, "Tables of Linear Congruential Generators of Different Sizes and Good Lattice Structure", 1999

// Borrowed from

// http://gee.cs.oswego.edu/cgi-bin/viewcvs.cgi/jsr166/src/main/java/util/concurrent/ThreadLocalRandom.java

// same constants as Random, but must be redeclared because private

/**
     * The random seed. We can't use super.seed.
     */

/**
     * Initialization flag to permit calls to setSeed to succeed only
     * while executing the Random constructor.  We can't allow others
     * since it would cause setting seed in one part of a program to
     * unintentionally impact other usages by the thread.
     */

// Padding to help avoid memory contention among seed updates in

// different TLRs in the common case that they are located near

// each other.

/**
     * Constructor called only by localRandom.initialValue.
     */

/**
     * Returns the current thread's {@code ThreadLocalRandom}.
     *
     * @return the current thread's {@code ThreadLocalRandom}
     */

/**
     * Throws {@code UnsupportedOperationException}.  Setting seeds in
     * this generator is not supported.
     *
     * @throws UnsupportedOperationException always
     */

/**
     * Returns a pseudorandom, uniformly distributed value between the
     * given least value (inclusive) and bound (exclusive).
     *
     * @param least the least value returned
     * @param bound the upper bound (exclusive)
     * @throws IllegalArgumentException if least greater than or equal
     * to bound
     * @return the next value
     */

/**
     * Returns a pseudorandom, uniformly distributed value
     * between 0 (inclusive) and the specified value (exclusive).
     *
     * @param n the bound on the random number to be returned.  Must be
     *        positive.
     * @return the next value
     * @throws IllegalArgumentException if n is not positive
     */

// Divide n by two until small enough for nextInt. On each

// iteration (at most 31 of them but usually much less),

// randomly choose both whether to include high bit in result

// (offset) and whether to continue with the lower vs upper

// half (which makes a difference only if odd).

/**
     * Returns a pseudorandom, uniformly distributed value between the
     * given least value (inclusive) and bound (exclusive).
     *
     * @param least the least value returned
     * @param bound the upper bound (exclusive)
     * @return the next value
     * @throws IllegalArgumentException if least greater than or equal
     * to bound
     */

/**
     * Returns a pseudorandom, uniformly distributed {@code double} value
     * between 0 (inclusive) and the specified value (exclusive).
     *
     * @param n the bound on the random number to be returned.  Must be
     *        positive.
     * @return the next value
     * @throws IllegalArgumentException if n is not positive
     */

/**
     * Returns a pseudorandom, uniformly distributed value between the
     * given least value (inclusive) and bound (exclusive).
     *
     * @param least the least value returned
     * @param bound the upper bound (exclusive)
     * @return the next value
     * @throws IllegalArgumentException if least greater than or equal
     * to bound
     */

/*
 * Copyright 2015 The Netty Project
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
 * Counter for long.
 */

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

/**
 * @deprecated please use {@link LongAdder} instead.
 */

/**
     * Creates a new fastest {@link LongCounter} implementation for the current platform.
     * @deprecated please use {@link java.util.concurrent.atomic.LongAdder} instead.
     */

// The seed deliberately keeps the low 32 timestamp bits, including the sign bit.

/**
     * Return a {@link Random} which is not-threadsafe and so can only be used from the same thread.
     * @deprecated Use ThreadLocalRandom.current() instead.
     */

/**
     * @deprecated Use {@link java.util.concurrent.ThreadLocalRandom#current()} instead.
     */
```


## Weak handler cache and retired thread-local scaffolding

Reopen the handler cache for a concrete lifetime defect: pinned
InternalThreadLocalMap.java:301-307 uses WeakHashMap<Class<?>,Boolean>, while the
previous CLR Dictionary<Type,bool> retained keys. Its actual downstream owner,
transport/ChannelHandlerAdapter.java:44-63, caches a pure Sharable annotation
query per physical thread and deliberately uses weak keys. A live CLR worker
must likewise avoid rooting collectible handler types/assemblies solely through
that cache. Two new public-owner tests fail before repair: a RunAndCollect type
survives forced collections, and distinct Type wrappers comparing equal collide.

Use ConditionalWeakTable<Type,StrongBox<bool>> directly in the map's lazy field.
The native table supplies weak reference-identity keys and native StrongBox holds
the boolean value; no custom weak dictionary or provider is added. The field's
presence still counts as one cache in Size, regardless of live keys. For the
future channel adapter, TryGetValue preserves absence versus a cached false;
GetValue with a pure attribute-query factory is the natural compute path. That
factory must not acquire side effects: concurrent callbacks may run more than
once, and Add duplicate-key behavior is not Java Map.put replacement semantics.
No transport implementation or new Sharable attribute is created in common.
At this checkpoint the strong CLR matcher caches retained their separate original
strong-cache policy. Their later native retirement and collectible-type validation
are recorded in Direct CLR type resolution and matching below.

Keep physical-thread ownership, map cleanup, indexed storage, list type switching
and StringBuilder trimming. Existing ThreadLocalContractTest already checks those
reified list/reference-release contracts. Actual remaining list consumers include
AsciiString.split and downstream ClientCookieEncoder/HttpPostMultipartRequestDecoder;
StringUtil CSV helpers use the builder. No speculative list/builder refactor or
ThreadLocal/AsyncLocal substitution is made for those verified paths.

Pinned counterHashCode/setCounterHashCode simply creates the deprecated holder
and performs a no-op; an all-module search finds no consumer outside those
definitions. Remove both unused CLR accessors and IntegerHolder. Native int
state, or StrongBox<int> when a real shared mutable box is required, replaces its
purpose without a speculative compatibility type. The IntegerHolder source
entry becomes CLR replacement. The eight obsolete rp padding fields likewise
have no consumer and no established CLR layout/performance contract; remove them.
The empty UnpaddedInternalThreadLocalMap base exists only to preserve Netty 4.1
binary compatibility. CLR's existing sealed map owns all state directly; mark
that empty JVM compatibility source not applicable, preserving its three comments.

Original holder/base licenses and comments, deprecated padding documentation,
no-op explanation and the retired capacity explanation follow. Whole map,
index allocation, listener/local-channel depth and downstream reviews remain open.

```java
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

/**
 * @deprecated For removal in netty 4.2
 */

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

/**
 * @deprecated This class will be removed in the future.
 */

// We cannot remove this in 4.1 because it could break compatibility.

/** @deprecated These padding fields will be removed in the future. */

// No-op.

// Start with small capacity to keep memory overhead as low as possible.
```


## Native indexed-variable bounds and publication

Reopen indexed storage for a concrete CLR bound mismatch: pinned
InternalThreadLocalMap.java:42-56,149-159,324-361 uses the JDK ArrayList-derived
int.MaxValue-8 limit. Installed net10.0 System.Runtime.xml P:System.Array.MaxLength
defines the native upper element count. Before repair, seeding the actual global
counter four positions below that native bound permits 52 claims rather than
four, and the sequential native-limit assertion fails. No huge array is allocated
to reproduce those public NextVariableIndex failures.

Replace this owner's AtomicInteger with one non-generic static int and native
Volatile/Interlocked.CompareExchange. The counter is initialized before reserving
VARIABLES_TO_REMOVE_INDEX and uses a runtime bound initialized earlier still.
Every successful claim advances exactly once; reaching Array.MaxLength is sticky
and throws the existing InvalidOperationException without increment/reset races.
The last-index snapshot remains nextIndex-1. Saturation ensures index+1 cannot
overflow even with checked arithmetic. Constructors across all FastThreadLocal<V>
types share this same owner, preserving CLR reified-static isolation requirements.
Other actual AtomicInteger consumers remain separate; no project-wide facade
retirement or starvation/performance guarantee is claimed.

Use Array.Fill and Array.Resize directly for the physical-thread-owned object
table. A local resized array is filled with the exact UNSET sentinel and receives
the value before publication, so failed allocation/copy does not replace existing
storage. Existing null-as-set, old-value, reference identity and cleanup semantics
remain. The native capacity helper uses BitOperations.RoundUpToPowerOf2 on a
positive uint and clamps before narrowing to int. Native array-limit rejection
and negative-write ArgumentOutOfRangeException are explicit CLR boundary policy;
nonnegative out-of-table reads/removals still return UNSET and presence remains false.
Array.MaxLength is an upper bound, not a promise that every object array below
it can be allocated: actual runtime/type/memory restrictions still propagate.

Preserve original construction-boundary assertions/comments using the native
limit and reflection on the new private scalar, rather than the removed counter
wrapper. The original CI-only oversized allocation scenario remains unchanged
and excluded by its existing local CI policy. Five capacity cases safely cover
small growth, the original 1<<30 branch and the last native slot without allocating
multi-gigabyte arrays. Three rejected-write cases retain prior storage; the two
public allocation/concurrency regressions now pass. Existing indexed growth,
generic list/cache/cleanup contracts continue to exercise the owning map.
The rejected-write tests establish pre-validation preservation, while resize
failure publication follows the local temporary-array boundary; allocation
exhaustion is not deliberately forced in this local run.

Original indexed-variable Javadocs and all original fixture explanations remain.
The retired JDK capacity-reference explanation is preserved below. Whole map,
remaining listener/local-channel depth and downstream lifecycle reviews stay open.

```java
// Reference: https://hg.openjdk.java.net/jdk8/jdk8/jdk/file/tip/src/share/classes/java/util/ArrayList.java#l229
```


## Shared native notification recursion boundary

Reopen progress notification for a concrete cross-instance recursion defect:
ExecutorProgress's per-owner queue prevented same-instance recursive reporting,
but its InEventLoop fast path let 64 connected reporters nest 64 callbacks.
The corresponding mixed completion/progress scenario already passed because
ExecutorCompletion had a physical-thread depth limit. Preserve that distinction
in notification-depth-before-debug.trx (one failure/one pass); the old fixture's
implicit count was 64. Final fixtures also exercise 10,000 pure and mixed links.

Pinned DefaultPromise.java:497-548 bounds ordinary completion listeners through
InternalThreadLocalMap.futureListenerStackDepth, with finally restoration and
executor submission at the threshold. Its progressive path at 754-791 invokes
inline without that guard. The CLR Task/IProgress API need not reproduce that
unbounded progressive quirk: ordered native notifications now share one physical-
thread boundary across both kinds and all instances.

ExecutorNotificationScope is an internal non-generic stack-only scope: one native
ThreadStatic counter, inline threshold eight, and deterministic using disposal.
Both synchronous drains enter it; both dispatchers defer to their actual executor
at the threshold. It allocates no thread-local map and flows no AsyncLocal state.
Using restoration covers return/error paths. Standard ImmediateEventExecutor
queues reentrant Execute, while event-loop executors submit pending work; like
the original completion policy, arbitrary executors must provide that dispatch
boundary rather than recursively invoking Execute without a limit.
Deep callbacks can therefore be deferred; per-owner ordered batches, original
Task identity/outcome, admission/backpressure, removal and notification completion
remain. No observer lock is held while dispatching/invoking callbacks. Existing
context isolation, rejection, queue-removal, disposal and callback-failure contracts
remain the surrounding validation; no new Future/Promise result owner is added.

Remove the unused _futureListenerStackDepth field, getter/setter and Size branch
from InternalThreadLocalMap. The only pinned consumer is DefaultPromise's listener
backend, already replaced by the native callback owners. CLR tracked call search
finds no user of those map APIs. Their methods have no original explanatory
comments; all existing map/source comments stay. Do not remove the distinct
localChannelReaderStackDepth: pinned transport/local/LocalChannel.java:350-382
uses it to bound recursive reads, restore depth in finally, submit readTask and
close both peers on dispatch failure. That real downstream contract remains
pending native transport integration and does not follow from notification tests.
The unused deprecated cleanerFlags APIs also have no all-module consumer; their
unimplemented BitSet bookkeeping is not a required native cleanup mechanism.
Existing physical-thread map/cleanup policy remains separate.

Four native scenarios assert 64/10,000 pure/mixed delivery identities/order and
bounded callback depth after returning, without deliberately overflowing the
process stack. The new boundary exists for this missing shared policy; it is not
a new configurable Java-style provider. Whole map/common/backend review stays open.
No original test/comment/provenance is removed, and no new design MD is created.


## Native thread-local removal membership

Reopen the removal registry for a concrete CLR design issue: it used a
Dictionary<IFastThreadLocal,bool> whose values were always true. Pinned
FastThreadLocal.java:54-75/99-124 actually needs an identity Set; its Java
Collections.newSetFromMap(IdentityHashMap) construction is not required in C#.
Use HashSet<IFastThreadLocal>(ReferenceEqualityComparer.Instance) directly.
The non-generic registration helpers accept the existing heterogeneous removal
interface; they need neither erased type parameters nor dummy values. Update
InternalThreadLocalMap.java:203-207's native Size consumer to the same set type.
Keep strong ownership until removal, snapshot before callbacks, clear each
indexed binding and membership before OnRemoval, and detach the physical-thread
map in finally. Membership iteration order remains unspecified; callback failure
still propagates and aborts the remaining snapshot, as in the original.
Do not substitute AsyncLocal or ThreadLocal<T>: indexed worker caches and explicit
callback cleanup still need their existing ownership policy.
Four native scenarios verify mixed-type/null bindings, duplicate writes and
rebind, callback-driven peer removals on ordinary/scoped workers, and map
detachment/reuse after callback failure. They pass with the old dictionary before
replacement and with the native set afterward: this is a framework substitution,
not a claimed repair of an observed runtime failure. Existing overridden-equality
identity, original factory/index/removal and Recycler consumers remain validation.
All original source/test comments stay; no new MD or collection wrapper is added.
Full/checked outcomes and unchanged inventory are recorded in common-porting.md.
Whole map/common review remains open.


## Direct CLR type resolution and matching

Reopen the matcher backend for redundant CLR objects/caches and a concrete
collectible-type lifetime problem: the old live worker map roots both a dynamic
handler class and its generic payload. The RunAndCollect scenario fails before
retirement and passes with direct CLR resolution/checks.
Pinned TypeParameterMatcher.java:31-81 resolves superclass metadata, caches
matcher instances and forwards ordinary checks to Class.isInstance. Its real
codec-base consumers are ByteToMessageCodec:98, MessageToByteEncoder:95,
MessageToMessageCodec:139/148, MessageToMessageDecoder:79 and
MessageToMessageEncoder:78; transport SimpleChannelInboundHandler and
SimpleUserEventChannelHandler:89 use the same acceptance predicate.
In C#, generic handlers can use typeof(T)/message is T; runtime-selected types
can retain System.Type and call IsInstanceOfType directly. Preserve the existing
ReflectionUtil.ResolveTypeParameter for actual constructed superclass metadata,
including distinct superclass/name bindings, arrays, private/enclosing types
and retained CLR generic arguments. Neither purpose requires a matcher object,
reflection-check facade, artificial per-thread identity or map cache.
Retire TypeParameterMatcher, ReflectiveMatcher, both duplicate Noop/NoOp classes,
and both map cache fields/accessors/Size branches. Pinned standalone
NoOpTypeParameterMatcher.java has no all-module consumer; a real bypass policy
can skip the type check directly rather than allocate a constant-true object.
The original Object matcher also accepted null; CLR IsInstanceOfType and is T
reject it. Null therefore remains explicit caller policy, not an implicit new
no-op fallback. The original outbound context already checks msg nonnull at
transport/AbstractChannelHandlerContext.java:844. Inbound/user-event null policy
and codec forwarding/refcount rules still belong to future module integration.
This common decision does not certify those downstream ports.
Seven portable original tests keep their identities/assertions using Type checks;
two JVM-erasure cases remain skipped. Seven CLR scenarios keep constructed type
and variance semantics; rename the two cache-focused identities to their native
superclass isolation/no-thread-local-state purposes. The native metadata is shared
across physical workers rather than forcing different matcher instances.
Original licenses are retained below. No replacement matcher/helper is added.
Scratch storage is separate: StringUtil CSV still uses the native builder;
the remaining pinned list consumers are AsciiString.split and HTTP cookie/
multipart scratch lists. Current AsciiString uses an owned List already. No
speculative change to the unused CLR list cache or future HTTP lifetime policy
is made in this unit; whole map/common review remains open.

Pinned common/src/main/java/io/netty/util/internal/TypeParameterMatcher.java (original replacement provenance):

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

package io.netty.util.internal;

import java.util.HashMap;
import java.util.Map;

public abstract class TypeParameterMatcher {

    private static final TypeParameterMatcher NOOP = new TypeParameterMatcher() {
        @Override
        public boolean match(Object msg) {
            return true;
        }
    };

    public static TypeParameterMatcher get(final Class<?> parameterType) {
        final Map<Class<?>, TypeParameterMatcher> getCache =
                InternalThreadLocalMap.get().typeParameterMatcherGetCache();

        TypeParameterMatcher matcher = getCache.get(parameterType);
        if (matcher == null) {
            if (parameterType == Object.class) {
                matcher = NOOP;
            } else {
                matcher = new ReflectiveMatcher(parameterType);
            }
            getCache.put(parameterType, matcher);
        }

        return matcher;
    }

    public static TypeParameterMatcher find(
            final Object object, final Class<?> parametrizedSuperclass, final String typeParamName) {

        final Map<Class<?>, Map<String, TypeParameterMatcher>> findCache =
                InternalThreadLocalMap.get().typeParameterMatcherFindCache();
        final Class<?> thisClass = object.getClass();

        Map<String, TypeParameterMatcher> map = findCache.get(thisClass);
        if (map == null) {
            map = new HashMap<String, TypeParameterMatcher>();
            findCache.put(thisClass, map);
        }

        TypeParameterMatcher matcher = map.get(typeParamName);
        if (matcher == null) {
            matcher = get(ReflectionUtil.resolveTypeParameter(object, parametrizedSuperclass, typeParamName));
            map.put(typeParamName, matcher);
        }

        return matcher;
    }

    public abstract boolean match(Object msg);

    private static final class ReflectiveMatcher extends TypeParameterMatcher {
        private final Class<?> type;

        ReflectiveMatcher(Class<?> type) {
            this.type = type;
        }

        @Override
        public boolean match(Object msg) {
            return type.isInstance(msg);
        }
    }

    TypeParameterMatcher() { }
}
```


Pinned common/src/main/java/io/netty/util/internal/NoOpTypeParameterMatcher.java (original replacement provenance):

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

package io.netty.util.internal;

public final class NoOpTypeParameterMatcher extends TypeParameterMatcher {
    @Override
    public boolean match(Object msg) {
        return true;
    }
}
```


## Operation-owned CSV storage

Reopen StringUtil.java:458-584's single/multiple CSV parsers for worker scratch
retention and native null validation. Before migration both valid/invalid parsing
paths create/retain map state; multiple-field null input throws NullReferenceException.
Three of four native storage/null scenarios fail before migration, then pass.
Real downstream users are CombinedHttpHeaders.java:142/170 and HTTP/2/3
HttpConversionUtil.java:567/500. Preserve their decoded field order/content,
empty/trailing fields, doubled quotes, quoted CR/LF, unquoted special-character
rejection and exact error positions/messages; this is not a broader CSV dialect.
Single unquoted input still returns its original reference.

Parse immutable CLR strings with offsets and validated doubled-quote counts.
Use Substring for unchanged field ranges and string.Create for the final decoded
string when quotes collapse. One private result-construction routine serves both
parsers; no character buffer/provider/pool or thread-local builder remains.
ArgumentNullException(value) precedes multiple-field allocation/parsing.
168,511 inputs, including every UTF-16 unit raw/quoted and exhaustive strings of
length zero through five over eight CSV characters, produce 337,022 matching
outputs/error messages/nonempty single-field reference results against five exact
pinned Java methods. The Java clean-builder shim verifies parser content, not
thread-local lifecycle. Separate native tests verify no worker state and stable
owned outputs after large data, failure and following calls.

Retire both unused map scratch APIs/fields/Size branches, capacity settings and
their now-unused logger bootstrap. Current AsciiString.split already uses an owned
List; remaining pinned HTTP cookie/multipart list and cookie/HTTP conversion builder
users need native operation-owned List/StringBuilder or final-string construction
when those modules are ported. Their format/order/lifetime policies are not certified
by this common checkpoint. No erased cross-type scratch cache is invented in C#.
Replace one CLR-only cache fixture with CSV output ownership and explicitly map
its identity; no original Java fixture is changed. Original map comments follow.
Warm default CLR-baseline allocation: 20,000 small single calls use 640,000 bytes
in both versions; 20,000 small multiple calls use 3,840,000 in both; 128 large
escaped single calls use 79,094,784 before and 25,604,096 after. The harness uses
exact prior CLR parser bodies/default 1024/4096 cache policy. This is input-specific
allocation evidence, not Java performance, throughput or a general speed claim.
All 67 StringUtil comments stay in place; whole map/StringUtil/common review remains open.

Retired scratch storage translation of pinned InternalThreadLocalMap.java:74-101/213-264 (original comment provenance):

```csharp
    // String-related thread-locals
    private StringBuilder _stringBuilder;

    // ArrayList-related thread-locals
    private System.Collections.IList _arrayList;

    static InternalThreadLocalMap()
    {
        STRING_BUILDER_INITIAL_SIZE =
            SystemPropertyUtil.GetInt("io.netty.threadLocalMap.stringBuilder.initialSize", 1024);
        STRING_BUILDER_MAX_SIZE =
            SystemPropertyUtil.GetInt("io.netty.threadLocalMap.stringBuilder.maxSize", 1024 * 4);

        // Ensure the InternalLogger is initialized as last field in this class as InternalThreadLocalMap might be used
        // by the InternalLogger itself. For this its important that all the other static fields are correctly
        // initialized.
        //
        // See https://github.com/netty/netty/issues/12931.
        logger = InternalLoggerFactory.GetInstance(typeof(InternalThreadLocalMap));
        logger.Debug("-Dio.netty.threadLocalMap.stringBuilder.initialSize: {}", STRING_BUILDER_INITIAL_SIZE);
        logger.Debug("-Dio.netty.threadLocalMap.stringBuilder.maxSize: {}", STRING_BUILDER_MAX_SIZE);
    }


    public StringBuilder StringBuilder()
    {
        StringBuilder sb = _stringBuilder;
        if (sb == null)
        {
            return _stringBuilder = new StringBuilder(STRING_BUILDER_INITIAL_SIZE);
        }

        if (sb.Capacity > STRING_BUILDER_MAX_SIZE)
        {
            sb.Clear();
            sb.Capacity = STRING_BUILDER_INITIAL_SIZE;
        }

        sb.Length = 0;
        return sb;
    }

    public List<E> ArrayList<E>()
    {
        return ArrayList<E>(DEFAULT_ARRAY_LIST_INITIAL_CAPACITY);
    }

    //@SuppressWarnings("unchecked")
    public List<E> ArrayList<E>(int minCapacity)
    {
        // CLR generic lists cannot share storage across different element types.
        // Clear the old list before replacing it so cached objects are released.
        if (_arrayList is not List<E> list)
        {
            _arrayList?.Clear();
            _arrayList = new List<E>(minCapacity);
            return (List<E>)_arrayList;
        }

        list.Clear();
        list.EnsureCapacity(minCapacity);
        return list;
    }

```


## Native map access and physical ownership

Pinned InternalThreadLocalMap.java:317-342/365-379 indexes the Java array for
negative reads/removals/presence checks, so it throws a bounds error. The previous
CLR methods leaked IndexOutOfRangeException while writes already used native
ArgumentOutOfRangeException(index). Six -1/Int32.MinValue scenarios fail
before repair and pass after direct ThrowIfNegative guards on all three methods.
Positive indexes beyond current storage still return UNSET/false without growing
the table; do not reinterpret a negative index as an absent slot through an
unsigned comparison. Native slot coverage distinguishes unset/null/reference,
growth gaps, replacement return values, sentinel clearing and removal.

Pinned getIfSet/get/remove/destroy at 102-146 selects physical thread storage;
destroy calls ThreadLocal.remove only for the caller's fallback slot. It is not
global worker cleanup and does not remove an owned fast-thread map. Detachment
does not invoke OnRemoval or clear a separately retained map; explicit variable
removal does, and RemoveAll snapshots/clears callbacks before detaching in finally.
Three ordinary/scoped/factory-native worker scenarios run a captured logical
ExecutionContext/AsyncLocal marker while proving separate maps/cache/depth/values,
fallback-only Destroy, explicit removal from the detached current-worker map,
fresh subsequent maps and unaffected parent bindings. No owner synchronization
facade or AsyncLocal map substitution is added. Explicit map access still requires
the caller's physical-thread ownership; the original warning-only thread wrapper
diagnostics do not make cross-thread mutation supported or thread-safe.

The current common map source review is now verified: every remaining API is
covered by native access/ownership, saturated index/publication, weak handler cache
and identity-removal registry decisions/tests. Removed matcher/codec/random/counter/
scratch/JVM padding APIs have explicit prior replacement/exclusion provenance;
deprecated unused cleaner flags have no pinned all-module consumer.
Size counts remaining reader-depth/cache presence and tracked variable membership,
not every arbitrary indexed slot. LocalChannel recursive read policy and handler
annotation integration remain future transport work; verification of this map
does not certify FastThreadLocalThread, transport or whole common completion.
All original comments stay; current full/checked counts and inventory are in
common-porting.md. No new MD, public helper or compatibility object is added.


## Native ASCII hex decoding

Pinned StringUtil.java:255-312 permits only ASCII 0-9/a-f/A-F, decodes pairs
with their original input/index diagnostic and returns owned dump bytes. Actual
common MacAddressUtil.java:168/175 calls decodeHexByte on strings; downstream
QueryStringDecoder.java:394 and ByteBufUtil.java:175-190 use the same pair/dump
contract. The CLR string overload instead used byte.Parse(HexNumber), accepting
surrounding whitespace and allocating a temporary two-character string per pair.
Four pair/four MAC-consumer scenarios expose this acceptance bug; null/range,
logical dump bounds and allocation scenarios bring pre-repair failures to eleven.

Read native immutable string code units directly through the ASCII nibble decoder,
with no Java sequence wrapper or success-path pair strings. Standard conversion
alone would change ArgumentException's pair/index/full-input diagnostic, so the
existing nibble contract supplies both native string and actual sequence bridges.
Invalid pairs have the same original diagnostic in either overload; valid string
dumps allocate only their final byte array. Warm 10000 pair decodes allocate zero
bytes after repair, versus the observed 320000 bytes before. This is allocation
evidence for this call path, not a throughput claim. Encoding/builder review is
still separate; this unit does not certify the whole StringUtil source.

Native null arguments throw ArgumentNullException, pair bounds reject before any
read, and nonempty dump ranges validate logical length by subtraction before result
allocation or addition. Length parity/negative-length errors retain the original
message. The original explicit zero-length slice returns shared empty bytes before
accessing its sequence/start, even for null/invalid start; whole-input overloads
still reject null. Java signed byte -128..-1 indexes outside the lookup table;
CLR unsigned byte 128..255 has no such sign and returns -1 as non-ASCII. All 128
native high-byte values and original signed-byte exceptions are checked explicitly.

Five exact pinned methods and table initialization run with only an EmptyArrays
stub. 213577 matching Java/CLR rows cover all 65536 UTF-16 nibble values, 131072
fixed-neighbor Unicode pair outcomes, 128 ASCII byte values, all 16384 ASCII pair
results/exact diagnostics, and 457 dump/error/empty slices. String/sequence pair
outcomes also agree throughout the full UTF-16 domain. New tests preserve this
wire-format policy and MAC integration; no original fixture/comment is removed.
Current full/checked counts and scope are in common-porting.md; oracle and raw
outputs remain ignored artifacts/hex-decode-validation records.


## Native hex output and builder bounds

Pinned StringUtil.java:131-245 provides twelve byte/array/string/Appendable hex
overloads. Preserve lowercase ASCII output, low-eight-bit masking of integer byte
inputs, padded pairs, and numeric unpadded output (skip zero bytes, emit one nibble
for a first byte below 16, keep zero for nonempty all-zero input). Empty valid ranges
produce empty output, and builder calls return/append to the caller's same object.
Real downstream SocksCommonUtils.java:55 formats IPv6 two-byte groups through the
unpadded append overload; ByteBufUtil.java:1555 constructs padded byte entries.
Current common NetUtilTest also exercises single-byte formatting; no transport
port completion is inferred from these encoding contracts.

Previous CLR bulk methods calculated offset+length and length<<1 before validation.
Invalid ranges can silently skip or append a valid prefix before a later index
error; null/negative-length/empty-range errors also differ from native conventions.
Ten of seventeen new cases fail before repair; seven preserve existing content,
integer masking and native StringBuilder capacity-failure behavior.
One bounded private span slice checks null/offset/length with subtraction before
allocation or append. All encoding overloads require valid source/range/destination,
including empty ranges: this explicit native validation tightens incidental Java
no-access acceptance of invalid empty arguments. DecodeHexDump's previously
documented explicit zero-length slice short circuit remains its separate policy.
Valid input append failures still use CLR StringBuilder exceptions and partial
output, not a transactional rollback or Java IOException facade.

Padded string output uses Convert.ToHexStringLower on the validated native span.
Unpadded strings use native IndexOfAnyExcept to skip zero bytes (retain the last
zero), checked final character-count arithmetic and string.Create for only the
final immutable result. StringBuilder outputs append the retained immutable byte
format entries directly, without intermediate output strings or scratch maps.
The shared zero-skip helper carries the original implementation comment; all 67
StringUtil source comments stay. The two private bounded span helpers do not add
public API or framework substitutes. Remaining whitespace/search/null boundaries
and broader StringUtil/native thread-wrapper reviews are separate.

Twelve exact pinned Java methods/table initialization execute with an unused
throwException shim for checked Appendable IOException translation. All 133562
Java/CLR rows match: 2051 signed/masked integer inputs and 131511 full/sliced array
inputs, including every 65536 byte pair with ordinary/leading-zero forms, empty,
all-zero, shifted and seeded larger arrays. All overload content and builder
prefix/reference identities are verified; invalid native arguments are tested
separately. Warm Release comparison against the exact previous CLR methods yields
20000 small padded calls: 2880000 -> 960000 allocated bytes; small unpadded:
2720000 -> 800000. For 128 large calls both forms reduce 33566744 -> 16780312
bytes. These are allocation measurements for the stated inputs, not speed claims.
Raw oracle/allocation/validation records stay ignored in hex-encode-validation;
the current full/checked counts and preserved fixture inventory are in
common-porting.md. No new Markdown document or original-test exclusion is added.


## Native string token boundaries

StringUtil.java:604-628 searches with Character.isWhitespace. The actual consumer
UnixResolverDnsServerAddressStreamProvider.java:185-205 separates nameserver values
and comments; using general CLR char.IsWhiteSpace changes its tokens. On the
executed Corretto 21.0.11_10 reference, Java accepts U+001C..U+001F and rejects
NEL/NBSP/figure-space/narrow-NBSP (U+0085/U+00A0/U+2007/U+202F), unlike the CLR.
Eight classification cases and the resolver-style token/IP scenario fail before
repair. Native null/negative argument scenarios add one failure; two endpoint/
surrogate/content checks already pass. No full DNS resolver port is claimed.

Use one immutable BCL SearchValues<char> for the 25 reference delimiter code units
and native span IndexOfAny/IndexOfAnyExcept. This is the required token policy,
not a JDK character API facade; it avoids dependent runtime Unicode predicates.
Return absolute UTF-16 indexes, retaining -1 at/beyond the end (including MaxValue).
Null/negative inputs reject with native argument exceptions before slice creation.
Character suffixes and surrogate classification use native string.EndsWith(char)
and char.IsSurrogate; substring helpers now validate null explicitly while keeping
their original absent-delimiter/empty-endpoint semantics. Original source comments
stay alongside the implementation. OWS remains its separately reviewed SP/HTAB
contract; this set is not substituted into CSV/OWS trimming or general CLR text.

Six exact pinned methods run without shims: all 65536 UTF-16 code units at search
offsets 0/1/2, substring endpoints, surrogate classification and character suffixes,
plus 121 empty/end/oversize/NUL/paired-surrogate cases, produce 65657 matching rows.
The complete domain confirms precisely eight CLR predicate differences. This
records the executed JDK/runtime reference, not every historical JDK Unicode table.
Default and checked validation, preserved fixture identities/comment coverage and
remaining source scope are in common-porting.md; raw oracle records remain ignored
in artifacts/string-boundary-validation. No performance/whole-source completion
claim or new Markdown artifact is added.


## Native joining, metadata labels and platform separators

Original joining consumers are HttpContentEncoder.java:94-104 and the StompVersion
example:34-39. They need ordered string joining; use standard string.Join directly
and retire the common Join facade. Native null-element/separator handling belongs
to the BCL (empty text/separator); Java's singleton-null and first-null builder
quirks are not a second public API. Preserve original TestJoin's later-null text
scenario through explicit caller projection to "null", retaining every assertion,
input scenario and fixture identity. Nonnull joining matches the exact pinned
method on 3110 ordered UTF-16 cases. Future HTTP no-validation/malformed header
handling must make its own null policy explicit; this is not an HTTP module port.
The original method/comment is archived below, not dropped or housed in a facade.

Original className/simpleClassName at 318-348 label diagnostics and thread factories
(ReferenceCountUtil.java:204, DefaultThreadFactory.java:67-70, leak detection and codec
errors). Native Type.FullName/Name define CLR identity; short labels traverse native
declaring/element metadata with '+' and element suffixes, removing only numeric
generic arity suffixes on generic types. This avoids assembly argument text in
array/pointer/byref labels and preserves literal nongeneric backticks/digits.
Generic parameters remain their own Name even when CLR reports nested attributes;
function-pointer types use native signature text instead of an empty Name.
No regex, strong reflection cache or unused package-separator field remains.
Six pre-repair failures cover arrays/modifiers/literal names/default newline;
an intermediate generic-parameter mismatch was repaired before final verification.

The JVM populates line.separator; the CLR has Environment.NewLine. Use that native
platform default with the existing explicit environment override, snapshotted on
StringUtil initialization. ResourceLeakDetector/TraceRecord use it for reports;
future LineSeparator.DEFAULT at codec-base/LineSeparator.java:31 has the same
default-purpose requirement. Five isolated processes verify default Windows CRLF,
LF/CRLF/empty/custom overrides, post-initialization snapshot and actual leak-report
prefixes/cleanup. No host environment is mutated and no protocol CRLF constant is
changed. Type diagnostics and these runtime/override decisions are CLR adaptations,
not identical Java/CLR metadata spelling or multi-OS certification.

The current retained StringUtil source/native review is verified. This closes its
remaining joining/naming/newline decisions after earlier suffix/delimiter, hex,
CSV storage, OWS and token-boundary reviews; see their existing sections for pinned
consumers/oracles. Constants and byte/nibble lookup storage are native primitive
values; the static class replaces the private Java constructor. Private CSV quote/
validation and span-boundary helpers serve the reviewed contracts. Existing mixed
sequence hex bridges serve actual buffer/codec purposes; ordinary strings avoid
those adapters. Original test identities/comments stay; whole common, future
protocol integrations and untested platforms remain open. Current validation is in
common-porting.md; ignored string-metadata-validation records hold detailed evidence.

Retired pinned StringUtil.java:674-702 joining implementation/comment:

```java
    /**
     * Returns a char sequence that contains all {@code elements} joined by a given separator.
     *
     * @param separator for each element
     * @param elements to join together
     *
     * @return a char sequence joined by a given separator.
     */
    public static CharSequence join(CharSequence separator, Iterable<? extends CharSequence> elements) {
        ObjectUtil.checkNotNull(separator, "separator");
        ObjectUtil.checkNotNull(elements, "elements");

        Iterator<? extends CharSequence> iterator = elements.iterator();
        if (!iterator.hasNext()) {
            return EMPTY_STRING;
        }

        CharSequence firstElement = iterator.next();
        if (!iterator.hasNext()) {
            return firstElement;
        }

        StringBuilder builder = new StringBuilder(firstElement);
        do {
            builder.append(separator).append(iterator.next());
        } while (iterator.hasNext());

        return builder;
    }
```


## Native thread creation and invocation ownership

Pinned DefaultThreadFactory.java:104-121 and FastThreadLocalRunnable.java:20-37
require an unstarted named thread, best-effort daemon/priority configuration and
finally-based physical fast-local cleanup, including a custom factory's ordinary
thread. Consumers include HashedWheelTimer.java:310, ThreadDeathWatcher.java:104,
GlobalEventExecutor.java:249 and SingleThreadEventExecutor.java:141-175 through
ThreadPerTaskExecutor.java:32-34. ThreadExecutorMap.java:84-95 supplies worker-local
executor identity and restores the old mapping after invocation. Buffer allocator
tests also use target-based and targetless/subclass FastThreadLocalThread workers.

Use one Action-to-Thread factory boundary throughout common, custom test factories
and logical ThreadGroup creation; no public IRunnable overload is retained there.
FastThreadLocalThread takes Action and exposes its owned native Thread for start,
join and name access. Targetless/virtual Run remains for the original subclass
scenarios, with no invented automatic-cleanup promise. Existing IRunnable executor,
timer and watcher producers supply a bound Run delegate at this boundary; their
remaining public execution APIs are a separate review, not certified by this change.
The factory decorator runs Action directly, sharing mapping restoration policy
with its existing executor decorator. Global executor flow suppression remains.

FastThreadLocalRunnable is an internal sealed delegate cleanup policy, matching
the upstream package-private helper's purpose. Rewrapping its single callback is
idempotent. CLR multicast delegates need their own outer finally: a chain whose
last callback is already wrapped still requires cleanup if an earlier callback
throws before reaching that callback. No public Java Runnable hierarchy is needed.
Native Interlocked counters replace factory AtomicInteger objects; CLR priority
range/null errors use ArgumentOutOfRangeException/ArgumentNullException. Explicit
group and null-group creator inheritance and native metadata pool naming remain.

A before-repair regression proves that holding a completed native Thread retains
the invocation capture through weak-key ownership metadata. The key is intentionally
alive, so weak lookup alone cannot release the owner's target. Clear that target
in the owned thread's Entry finally, retaining cleanup metadata while releasing work
after normal/exceptional termination. A GC test retains both owner and Thread on
the exceptional path; neither capture survives. A subclass's own fields remain its
responsibility; clearing the base target does not clear arbitrary subclass state.

Standalone native threads follow CLR ExecutionContext capture at Thread.Start:
the starter's current AsyncLocal state flows unless flow is suppressed. Factory
construction does not snapshot the creator's logical state. Physical fast-local
membership/map/cleanup remains thread-static and never follows AsyncLocal. Existing
global/shared executor submission policies separately control caller context;
this factory change does not substitute native threads with Task.Run or a pool.
CLR unhandled worker exceptions retain native process-level behavior; contract
tests use an explicit catching custom factory/subclass to observe failure safely.

Ten native cases cover retained target release, exception cleanup, multicast,
start/context policy, null/range errors and direct Action consumption. Existing
original fixture identities/comments/scenarios remain, including the existing
SecurityManager runtime skip; no new exclusion is introduced. Full Debug/Release,
checked contracts and provenance/inventory results are in common-porting.md.
FastThreadLocalRunnable's full small source/native review is verified. Remaining
wrapper stack-size/constructor and blocking-policy decisions, wider executor APIs,
JVM-only ObjectCleaner/BlockHound consumers and whole common remain separate.
No throughput or allocation improvement is claimed by this boundary/lifetime unit.


## Native owned-thread construction and cleanup capability

Pinned FastThreadLocalThread.java:43-79 has targetless subclass and wrapped-target
constructors, with Java long stack hints. Use two C# constructors with optional
name/int maxStackSize/group arguments: native Thread owns lifecycle and validates
the CLR hint, whose zero/default and positive sizes remain hints, not guarantees
of allocated stack bytes. Reject negative hints and null targets with native
public parameter names. No CLR long-to-int cast/OverflowException facade remains.
Explicit groups stay weak metadata; default groups inherit the construction caller.
Actual DefaultThreadFactory creation uses the named group argument. Existing native
Thread name, priority/background and Start/context policies remain as reviewed in
the preceding section. Named-argument consumers test zero/128-KiB hints, groups,
owned execution/removal, native negative errors and targetless default behavior.

The original instance cleanup query is virtual, and both arbitrary/current-thread
queries invoke that method (FastThreadLocalThread.java:112-137). This is purposeful
allocator/recycler eligibility, independent of indexed-map capability. Actual
buffer consumers are AdaptivePoolingAllocator.java:271 and PooledByteBufAllocator
.java:584; AdaptiveByteBufAllocatorUseCacheForNonEventLoopThreadsTest.java:60-79
overrides the guarantee and Run. The old CLR method cannot be overridden (an
isolated consumer fails with CS0506), and queries bypassed subclasses via the
constructor field. Replace the instance Java method with virtual C# property
CleansFastThreadLocals; native metadata/current queries invoke that property.
Manual Run subclasses own cleanup and may truthfully declare it; a conservative
false declaration can disable pooling even while a target wrapper still cleans.
Real Recycler tests verify reuse versus NOOP handles, separate map capability,
normal removal and no invented automatic cleanup on targetless workers. Four
exact pinned Java queries match four CLR base/custom policy rows under Corretto
21.0.11_10. The oracle throws if its untested ordinary/fallback branch is reached;
scope/fallback behavior retains its earlier physical ownership validation.

Only common/InternalThreadLocalMap.java:105/120-122/139 accesses the original
worker's raw map in the pinned all-module tree. Make the CLR owner accessor an
internal property; preserve getter/setter comments and warning-only diagnostics.
It is not cross-thread synchronization. Normal consumers use the caller-owned
InternalThreadLocalMap/FastThreadLocal APIs, not public raw-owner mutation.

permitBlockingCalls at FastThreadLocalThread.java:184-196 is only queried by
Hidden.java:191-198, which registers a predicate with reactor BlockHound. Its
actual override test is in transport-blockhound-tests at 191-205; it controls JVM
Thread.sleep instrumentation. Hidden.java is wholly that optional integration:
JDK ServiceLoader visibility, JVM method/class allowlists and compareTo ordering.
The common META-INF/services/reactor.blockhound.integration.BlockHoundIntegration
resource registers Hidden$NettyBlockHoundIntegration. This has no CLR instrumentor
or common protocol/execution implementation. Exclude Hidden and retire the unused
CLR blocking flag rather than expose a pretend runtime blocking detector. Native
Thread/blocking behavior and actual executor-local deadlock/affinity rules remain;
no global CLR blocking instrumentation or replacement backend is certified.

The upstream fallback AtomicReference/immutable ID bitmap is replaced by physical
thread-static scope membership, as already verified by scope/map/cache/recycler
tests. Its source comments previously sat above a bool despite describing a map;
archive them with their original code below. Every original worker/Hidden comment
is retained near its implementation or in these provenance blocks. Current full/
checked counts, source decisions and remaining whole-common scope are recorded in
common-porting.md. This completes the retained worker's source/native review,
including earlier thread-map, fallback, subclass, cleanup and capture-lifetime
decisions; broader executor APIs and future allocator integration remain open.

Retired FastThreadLocalThread.java:32-36 fallback publication member:

```java
    /**
     * Set of thread IDs that are treated like {@link FastThreadLocalThread}.
     */
    private static final AtomicReference<FallbackThreadSet> fallbackThreads =
            new AtomicReference<>(FallbackThreadSet.EMPTY);
```

Retired FastThreadLocalThread.java:198-254 immutable fallback helper:

```java
    /**
     * Immutable, thread-safe helper class that wraps {@link LongLongHashMap}
     */
    private static final class FallbackThreadSet {
        static final FallbackThreadSet EMPTY = new FallbackThreadSet();
        private static final long EMPTY_VALUE = 0L;

        private final LongLongHashMap map;

        private FallbackThreadSet() {
            this.map = new LongLongHashMap(EMPTY_VALUE);
        }

        private FallbackThreadSet(LongLongHashMap map) {
            this.map = map;
        }

        public boolean contains(long threadId) {
            long key = threadId >>> 6;
            long bit = 1L << (threadId & 63);

            long bitmap = map.get(key);
            return (bitmap & bit) != 0;
        }

        public FallbackThreadSet add(long threadId) {
            long key = threadId >>> 6;
            long bit = 1L << (threadId & 63);

            LongLongHashMap newMap = new LongLongHashMap(map);
            long oldBitmap = newMap.get(key);
            long newBitmap = oldBitmap | bit;
            newMap.put(key, newBitmap);

            return new FallbackThreadSet(newMap);
        }

        public FallbackThreadSet remove(long threadId) {
            long key = threadId >>> 6;
            long bit = 1L << (threadId & 63);

            long oldBitmap = map.get(key);
            if ((oldBitmap & bit) == 0) {
                return this;
            }

            LongLongHashMap newMap = new LongLongHashMap(map);
            long newBitmap = oldBitmap & ~bit;

            if (newBitmap != EMPTY_VALUE) {
                newMap.put(key, newBitmap);
            } else {
                newMap.remove(key);
            }

            return new FallbackThreadSet(newMap);
        }
    }
```

Excluded FastThreadLocalThread.java:184-196 BlockHound hook:

```java
    /**
     * Query whether this thread is allowed to perform blocking calls or not.
     * {@link FastThreadLocalThread}s are often used in event-loops, where blocking calls are forbidden in order to
     * prevent event-loop stalls, so this method returns {@code false} by default.
     * <p>
     * Subclasses of {@link FastThreadLocalThread} can override this method if they are not meant to be used for
     * running event-loops.
     *
     * @return {@code false}, unless overridden by a subclass.
     */
    public boolean permitBlockingCalls() {
        return false;
    }
```

Excluded Hidden.java comments (pinned locations; entire source is JVM integration):

Hidden.java:1:

```java
/*
 * Copyright 2019 The Netty Project
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

Hidden.java:26:

```java
/**
 * Contains classes that must have public visibility but are not public API.
 */
```

Hidden.java:31:

```java
/**
     * This class integrates Netty with BlockHound.
     * <p>
     * It is public but only because of the ServiceLoader's limitations
     * and SHOULD NOT be considered a public API.
     */
```

Hidden.java:148:

```java
// Let's whitelist SSLEngineImpl.unwrap(...) for now as it may fail otherwise for TLS 1.3.
```

Hidden.java:149:

```java
// See https://mail.openjdk.java.net/pipermail/security-dev/2020-August/022271.html
```


## Native worker bootstrap boundary

Pinned ThreadPerTaskExecutor.java:23-34 creates and starts a physical worker for
an entry; it has no queue, completion result or event-loop admission policy.
SingleThreadEventExecutor.java:141-175/225/244/1197-1198, MultithreadEventExecutorGroup
.java:53/79-88/186 and DefaultEventExecutorGroup.java:58 use that entry dispatcher
to start long-lived workers. Actual future consumers include transport's
ThreadPerChannelEventLoopGroup.java:103/121 and transport-native-epoll's
EpollEventLoopTest.java:74. They require worker creation and child injection,
not a Java interface specifically. Common bootstrap constructors, the stored
backend and NewChild hook now use Action<Action>; ThreadPerTaskExecutor is sealed
with Execute(Action) and no IExecutor implementation. Factory chaining uses its
method group. No Java Runnable wrapper is allocated for the worker entry.
Original constructor comments remain with nearby CLR interpretation notes.

The supplied backend dispatches a long-lived entry which returns after worker
termination; it must run the entry independently for ordinary event-loop use.
ThreadPerTaskExecutor starts the configured native Thread synchronously and has
no Task.Run policy. Start retains the previously reviewed native ExecutionContext
flow. Factory/Start failures reach the caller without replacing exceptions;
null commands fail before factory side effects, and a null returned Thread has
an explicit InvalidOperationException instead of accidental NullReferenceException.
A previously started Thread retains native ThreadStateException. These are CLR
parameter/failure adaptations, not new completion signals or guaranteed uncaught
worker exception recovery. Task submission and Termination keep their existing
owners and state machine. Do not confuse dispatcher success with task completion.

ThreadExecutorMap.Apply(Action<Action>, IEventExecutor) installs the physical
FastThreadLocal mapping inside the dispatched entry and restores the previous
binding in finally. It neither installs a binding on the submitting thread nor
uses AsyncLocal flow. Deferred normal/exceptional entries and real single/group
workers verify placement and restoration. Native consumers use SubmitAsync for
serial tasks and Task termination while constructing backends with direct lambdas.
Factory/fresh-thread/null/start failures and actual group NewChild injection are
covered alongside original startup, suspension, chooser and lifecycle scenarios.
Ordinary queued IExecutor/IRunnable APIs and queue cancellation/identity remain
under separate review; this unit completes the small ThreadPerTaskExecutor source
and native review, not all SingleThreadEventExecutor or group APIs. Future
transport constructors will need the same worker-dispatcher adaptation.

The expanded checked Release selection also exposed an existing timed-drain
addition overflow at SingleThreadEventExecutor.java:529. At a positive clock of
320 and budget Long.MAX_VALUE, Java's deadline wraps negative and cuts off after
64 tasks even though the budget is huge; checked C# throws before running tasks.
An isolated Corretto 21.0.11_10 arithmetic/64-task-check probe uses the exact pinned
deadline expression and cutoff predicate. This is an overflow defect rather than
intended timeout behavior; original SingleThreadIoEventLoop.java:228 supplies a
normal task quantum. Compare elapsed time to the budget instead, with an explicit
unchecked timestamp difference. Nonpositive budgets retain the every-64-task
cutoff; large positive budgets drain 100 available tasks without overflow, and
finite budgets retain the same check cadence. Four deterministic CLR rows verify
zero, negative and adjacent extreme positive budgets from a nonzero ticker.
The original metric case that failed checked Release now passes unchanged.
This fix does not certify other timestamp arithmetic or untested platforms.
Results, original identity/comment audit and remaining work are in common-porting.md.


## Native timer callback dispatch

Pinned HashedWheelTimer.java:247-286/712-737 uses a caller-owned Executor for
short timeout callbacks, with ImmediateExecutor as the default. The timeout CAS
and pending-count removal precede dispatch; a synchronous dispatch failure is
logged after expiration, and task failures are isolated. HashedWheelTimerTest
.java:195-218 supplies an explicit inline dispatcher. The pinned tree has no
other production constructor call supplying this custom dispatcher; its public
extension contract and original test remain meaningful. This differs from the
long-lived worker-entry role reviewed in the preceding section.

Replace the timer constructor/stored IExecutor with Action<Action> and default
to a static inline callback. Expiration supplies the bound timeout Run Action;
the private timeout no longer implements IRunnable. All actual source/fixture
callers use native delegates. A before-change native consumer fails with CS1660;
the identical consumer compiles/runs against the changed net10.0 assembly.
Original comments and timeout/task reference identity remain. Inline execution
stays on the physical timer worker; ThreadPerTaskExecutor.Execute can also serve
as a native callback dispatcher without an executor/runnable adapter.

The dispatcher owns its queue and shutdown independently. MaxPendingTimeouts
limits wheel admission, not already expired callbacks waiting in an external
queue. Timer.Stop does not withdraw those expired callbacks or stop the supplied
dispatcher; their original handles are delivered when the host invokes them.
Cancel after expiration remains unsuccessful. Existing rejection and later-task
contracts retain the expired state, zero pending count and continued worker use.
This fire-and-forget boundary has no result Task and must not invent completion
or retry on rejected dispatch. A Task-returning future timer API will need its
own explicit result/rejection/cancellation policy; it is not certified here.

For the default raw callback model, native Thread.Start captures the first-start
caller ExecutionContext unless flow is suppressed, not constructor context or
every later NewTimeout publisher context. Two native rows verify both policies
and physical worker identity. No per-callback AsyncLocal isolation is added;
an externally configured dispatcher determines its own context policy. Five
native rows cover that flow, null configuration before worker construction,
external queue/stop/pending ownership and actual native callback-thread dispatch.
The original 16 timer cases retain assertions, 100,000-task timing scenario and
identities; broader ITimer/ITimerTask/ITimeout API, core queued executors, lifetime
and untested-platform review remain open. Results are in common-porting.md.


## Native queued Action execution

Pinned SingleThreadEventExecutor.java:991-1040/1132 and AbstractEventExecutor
.java:167-200 distinguish normal/lazy admission, wakeup policy and exception
isolation. ImmediateEventExecutor uses a reentrant FIFO; GlobalEventExecutor and
NonStickyEventExecutorGroup preserve their own queue/runner/lifecycle policies.
Real transport SingleThreadEventLoop.java:147 uses wakesUpForTask; SslContext
.java:1055/1109 uses ImmediateExecutor for delegated TLS work. Review this actual
execution boundary, not only the original JDK Executor syntax.

IExecutor.Execute, concrete/abstract event executor entry points, group forwarding
and virtual LazyExecute now accept Action only. AnonymousExecutor's configuration
and ThreadExecutorMap callback decoration use native delegates. No public
Execute(IRunnable) overload is retained. SubmitAsync still owns its result through
its existing TaskCompletionSource; raw Execute has no result Task or new implicit
ExecutionContext capture. Ordinary callback exceptions, inline/reentrant behavior,
lazy wakeup checks, rejection and physical affinity retain their source policies.
A separate native consumer implements IExecutor and overrides both virtual hooks
without importing IRunnable; it fails before with CS0535/CS0115 and passes after
for inline execution, Task submission, scheduling and explicit lazy work.

Internal queue work still carries deadline, cancellation and runner ownership.
Replacing it with an unrelated callback wrapper would hide INativeSubmission /
IScheduledWork and break pool cancellation/removal or ordered runner shutdown.
ExecutorWork is an internal sealed envelope with one original work reference and
one bound Action. Real common producers cross the public virtual Action hook;
known queues recover the exact work only for the envelope's identical delegate.
There is no second completion state. Multicast/composed callbacks are ordinary
caller work and execute in full; arbitrary targets are not treated as work owners.
The internal overload/extension is used by actual submission, scheduling, listener
and runner producers, not exposed solely to call original tests. Native subclass
forwarding into the real pool verifies pending-count removal on cancellation;
a composed prefix verifies multicast preservation. Native normal/lazy callbacks
visit their actual subclass hooks and preserve physical affinity. Existing original
fixture queues unwrap the same real payload while retaining original assertions.

The retained protected/internal queue APIs and wakeup markers need a later native
queue redesign against future transport consumers. ShutdownNow, rejection handlers,
shutdown hooks and other IRunnable/Future APIs are not certified by this entry-point
migration. Single-thread canceled work retains its original queue policy; only the
pool's existing cancel-removal contract is asserted here. All prior source comments
remain; common-porting.md records exact full/checked outcomes, inventory and the
allocation measurement of this transitional stateful dispatch envelope. This is
not a claim that all queued/executor native design or whole common is finished.


Allocation evidence for this bridge: the same Release Windows/x64/net10.0
ImmediateEventExecutor.SubmitAsync<int> probe uses one static Func<int>, 5,000
warmups and three 100,000-operation samples per version, checking completed results.
Steady allocation increases from 264 to 360 bytes/operation: exactly +96 for the
original-work envelope and its Action. Raw callback costs and controlled throughput
are not established by this probe. Preserve the native entry point and queue/state
contracts while reducing this transitional adapter cost during the native queue
redesign; do not describe the current bridge as allocation-free or fully optimized.
Records: artifacts/queued-action-validation/allocation-evidence.json and identical
before/after Perf.cs sources (ignored artifacts).

## Native shutdown hook delegates

Pinned SingleThreadEventExecutor.java:99/729-784 uses an insertion-ordered set,
loop-confined edits and a cleared snapshot on each shutdown pass. LocalChannel.java:
494/555/563 and LocalServerChannel.java:230/251/259 register a channel's close callback
and remove it on deregistration. An all-module search finds no additional consumers
of these executor methods; Runtime.addShutdownHook is a separate JVM lifecycle API.

AddShutdownHook/RemoveShutdownHook now take Action. Loop-confined LinkedList<Action>
and Dictionary<Action, LinkedListNode<Action>> preserve registration order and
indexed removal, with delegate equality for uniqueness and removal: newly created
method groups with the same target/method match. This
adapts Java's stable Runnable/equals keys to normal CLR delegate semantics; multicast
invocation lists are one key and follow CLR short-circuiting on exceptions. Readding
after removal moves a hook to the end. Native collections avoid a Java set clone
and dictionary iteration assumptions. One event loop may host many local channels,
so a List-only collection's linear membership
searches would degrade the original hash-indexed admission/deregistration cost.
The index and ordered chain are cleared together before each snapshot executes;
hash/equality cost follows CLR delegate invocation-list semantics.

Outside-loop edits pass the actual virtual Execute(Action) hook, in queue order;
on-loop edits stay inline, including during shutdown after ordinary admission closes.
Hook exceptions are logged independently and do not fail Termination or suppress
following registrations. Removing a hook already in the active snapshot cannot
withdraw that invocation; additions are processed on the next snapshot, and removing
an addition can still withdraw it. A hook repeatedly registering itself may prevent
shutdown, as in the original. No new cancellation or context-capture policy is added.
Null is rejected synchronously before dispatch, including on the event loop, rather
than accepting a Java null set member and later logging an invocation failure.
Original license/documentation/implementation comments remain in place.

NativeShutdownHookContractTest covers method-group equality/order, both edit paths,
snapshot mutation, exception/multicast behavior, null/closed-admission failure and
release of captured objects after removal or execution with the executor kept alive.
A standalone Java probe executes the pinned three hook methods with an inline
dispatch/clock/logging shim and agrees on first,second,third snapshot mutation and
throw,after failure traces. It is not a substitute for the real executor lifecycle
tests. An identical non-friend C# consumer fails before with CS1503 and passes after
using method groups, with no IRunnable import. No original fixture is changed.

Release Windows/x64/net10.0 allocation comparison against 9ec565e uses the real
DefaultEventExecutor queue with its worker blocked, one precreated hook, 5,000 warmup
add/remove pairs and three 100,000-pair samples. Both versions drain all accepted
commands and terminate successfully. Allocations per edit are approximately 264
before and 168 after, a steady 96-byte reduction by avoiding the intermediate
Runnable-to-Action envelope; the queue's raw-action wrapper remains. First-sample
runtime noise is retained in allocation-evidence.json. Elapsed times are recorded
but not presented as controlled throughput. A separate capture-only virtual-dispatch
probe measures 216 to 96 bytes/edit; it excludes queue unwrapping and must not be
substituted for the real-queue result. This does not fix the prior +96-byte
SubmitAsync envelope cost. Broader queue/rejection/JDK API review remains open.
Records: artifacts/shutdown-hook-validation (ignored), linked from the current
common-porting checkpoint; verification outcomes are recorded there after checks.

## Native bounded rejection callbacks

Pinned SingleThreadEventExecutor.java:400-417/1138-1148 runs a synchronous policy
only for capacity failure; shutdown throws directly. RejectedExecutionHandlers.java:
49-74 wakes, parks and offers at most the configured number of retries, never
waiting on the event loop. SingleThreadEventLoop.java:130-149 rejects tail tasks
through the same protected hook. DefaultEventExecutorGroup passes the policy to
each child; transport constructors expose the same configuration. The all-module
search finds no original Netty caller of backoff, but the original public policy
is useful bounded admission behavior and is retained. JDK rejection callbacks in
UnorderedThreadPoolEventExecutor are a separate remaining surface.

Single-thread and default/group constructors now accept the standard
Action<Action, SingleThreadEventExecutor>. Reject/Backoff factories return that
delegate; the Java functional interface and two port-only implementation classes
are removed. Public OfferTask(Action) does not start or wake a worker. False means
capacity failure; shutdown still throws. Protected Reject(Action) supports real
transport tail-queue consumers. Protected IRunnable queue hooks remain separate
pending work; there is no second public rejection configuration API.

The policy receives a replay callback for the actual queue payload. It is opaque,
not necessarily the caller's original delegate object. Reuse it unchanged with
OfferTask to retain stateful submission/schedule identity across admission and
post-start shutdown rollback; invoking it is an explicit caller-runs policy.
Composed/multicast callbacks remain ordinary new work, as at Execute. Policies
run on the submitting thread and may throw, forward or deliberately discard raw
work; returning without executing/queuing a result-bearing task can leave its
Task pending as in the original policy contract. No new result owner or per-raw
callback ExecutionContext capture is introduced. Synchronous Execute throws the
original policy exception; SubmitAsync reports it through its existing Task.

Backoff uses TimeSpan ticks and monotonic elapsed time. Non-positive durations
return immediately, matching parkNanos; positive durations round up to the CLR
millisecond Sleep granularity and chunk at int.MaxValue without floating-point
nanosecond overflow. A consumed CLR ThreadInterruptedException is restored and
the park returns early, matching the original pending Java interrupt behavior.
This is not per-submission cooperative cancellation, an interrupt-based executor
shutdown mechanism or a sub-millisecond precision claim. Spurious early Java
returns have no deterministic CLR counterpart; policy retry counts remain bounded.

NativeRejectionContractTest validates bounded capacity, off/on-loop policy dispatch,
retry success/exhaustion, negative/extreme durations, interrupt retention, native
offer/bootstrap/closed-admission behavior, exception identity and shutdown rollback.
No original fixture/assertion/comment changes. A Java probe compiles both exact
pinned policy sources against queue/wakeup shims, covering retry and interrupt
traces. CLR before/after compatibility probes isolate the prior Sleep/time-conversion
defects; independent native consumers validate delegate-only construction and tail
rejection. Results, allocation tradeoffs and the next queue API work are recorded
in the current checkpoint. Whole common and broader runtime reviews remain open.

Replaced RejectedExecutionHandler.java: the original functional-interface license
and documentation remain here because a BCL delegate has no source body to host them:

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
package io.netty.util.concurrent;

/**
 * Similar to {@link java.util.concurrent.RejectedExecutionHandler} but specific to {@link SingleThreadEventExecutor}.
 */
public interface RejectedExecutionHandler {

    /**
     * Called when someone tried to add a task to {@link SingleThreadEventExecutor} but this failed due capacity
     * restrictions.
     */
    void rejected(Runnable task, SingleThreadEventExecutor executor);
}

```

Prior port comment provenance for retired interface/link spellings (not current APIs):

```csharp
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

using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

/**
 * Similar to {@link java.util.concurrent.IRejectedExecutionHandler} but specific to {@link SingleThreadEventExecutor}.
 */
public interface IRejectedExecutionHandler
{
    /**
     * Called when someone tried to add a task to {@link SingleThreadEventExecutor} but this failed due capacity
     * restrictions.
     */
    void Rejected(IRunnable task, SingleThreadEventExecutor executor);
}
```

```csharp
/**
 * Expose helper methods which create different {@link IRejectedExecutionHandler}s.
 */
/**
 * Returns a {@link IRejectedExecutionHandler} that will always just throw a {@link RejectedExecutionException}.
 */
/**
 * Tries to backoff when the task can not be added due restrictions for an configured amount of time. This
 * is only done if the task was added from outside of the event loop which means
 * {@link IEventExecutor#inEventLoop()} returns {@code false}.
 */
//LockSupport.parkNanos(backOffNanos);
// 100
```

Rejection allocation tradeoff (Windows/x64/net10.0 Release): real 16-slot
DefaultEventExecutor queue with a blocked worker, static raw command, counting
discard policy, 5,000 warmups and three 100,000-operation samples. Prior d503bcc
allocates approximately 24 bytes/rejection; native replay callbacks allocate 120,
a steady additional 96 bytes for ExecutorWork/Action. This deliberately isolates
capacity rejection without exception or Task costs. Accepted execution is not
measured by this probe. Queue fullness/policy counts and eventual drain are checked;
elapsed times are retained without a controlled throughput claim. Preserve the
rollback/virtual-hook contract while reducing the adapter during the remaining
queue migration; no allocation-free or fully optimized claim. Ignored evidence:
artifacts/rejection-action-validation/allocation-evidence.json.

## Native unordered rejection and legacy stop callbacks

Pinned UnorderedThreadPoolEventExecutor.java:79-105 passes the inherited JDK
rejection policy to its scheduled pool. Its execute at 224-228 wraps raw commands
in NonNotifyRunnable; decorateTask at 174-178 keeps the JDK scheduled membership
for this branch. This policy is separate from Netty's bounded single-thread
policy. The two unordered constructors now accept
Action<Action, UnorderedThreadPoolEventExecutor>. A bound callback invokes the
existing membership's Run method without another result owner or ExecutorWork
envelope. The synchronous policy receives the actual owner on the submitting
thread, outside the queue gate. Throwing preserves exception identity and clears
the payload; deliberately discarding raw work remains supported. SubmitAsync
and ScheduleAsync admission failure still faults the sole Task regardless of a
raw discard policy. No new ExecutionContext capture or scheduler is introduced.

The exact pinned execute/NonNotifyRunnable fragments, exercised in a Java21 JDK
scheduled-pool shim, show that replay can claim raw work once during graceful
drain, but cannot start after actual termination. This exposed an existing CLR
CanRun gap: a saved callback could run after termination. The gate now also checks
the persistent Termination state. An identical before/after CLR probe keeps a
rejected callback across drain and submits another after drain: old 5887742 runs
two callbacks; the new version and JDK run zero. Immediate stop still suppresses
replay. These checks concern starting an invocation; a policy running arbitrary
caller code does not become an owned worker or extend Termination.

Shared ShutdownNow signatures now return List<Action> instead of List<IRunnable>;
NonSticky forwards the underlying result. AbstractEventExecutor/Group retain
their original shutdown-and-empty-list policy (original lines 85-88); unordered
retains the prior cooperative immediate stop and actual drain. Its returned
callbacks bind the exact canceled memberships. Repeated/concurrent invocation
cannot revive raw, submitted or scheduled work; retaining the owner/list/result
does not retain canceled payload captures. No arbitrary CLR thread interrupt is
introduced. StopAsync and its persistent Task remain the native stop result.
This list's legacy semantics remain a separate retirement/design-review item:
all-module original searches find forwarding in NonSticky and transport's
DefaultChannelPipeline wrapper, a NioEventLoopTest invocation ignoring the result,
and benchmark stubs. They do not establish a Netty production need to replay
unordered returned tasks. JDK returned tasks can remain uncanceled; the deliberate
CLR cancellation policy predates this change and is documented in
common-unordered-cooperative-stop.md. The broad inherited-API review stays open.

Eleven new CLR cases cover public shape, synchronous policy ownership, concurrent
one-time replay, immediate/graceful termination boundaries, throwing/discard
policies, stop reentry on another thread, three canceled payload lifetimes and
NonSticky forwarding. Six existing Porting fixtures only adapt handle types and
invocation syntax; original Java-derived fixtures/assertions/comments stay intact.
An identical non-friend C# consumer fails against 5887742 (CS1660/CS0029) and
passes against the new assembly for native policy and shared stop handles.

Windows/x64/net10.0 Release allocation probes use identical sources, 5,000 warmups
and three 100,000-operation samples, with tiered compilation disabled to avoid
mixed-tier allocation measurements. Closed raw Execute with a counting discard
policy rises from approximately 72 to 136 bytes per rejection. Constructing a
null-worker pool, admitting one raw callback, returning/invoking its canceled
ShutdownNow handle and checking actual drain rises from 2,088 to 2,152 bytes per
operation. Each difference is 64 bytes for the bound Action; the second includes
the whole unchanged pool lifecycle. No exception or result-bearing submission is
measured, and these are not accepted-execution or controlled throughput claims.
This callback cost and the previously measured 96-byte ordered replay/SubmitAsync
adapter remain optimization work. Ignored evidence: artifacts/unordered-action-validation
contains pinned Java/native/compat probes, allocation-evidence.json and logs;
TestResults contains the final default/targeted TRX, comment audit and inventory
summary. Whole common, inherited APIs and broader platforms remain incomplete.

## Retiring the inherited shutdown list API

Follow-up to the prior native callback checkpoint: the remaining ShutdownNow
surface duplicated native StopAsync and exposed canceled memberships as callbacks.
Pinned all-module searches find only common declarations/delegation, transport
test forwarding, NioEventLoopTest's stop request ignoring the list, and a benchmark
stub. SingleThreadEventExecutorTest's executorService is a separate JDK harness.
No original Netty consumer inspects or replays this list. Thus these signatures
are retired rather than retained solely for CLR probe compatibility. StopAsync
already expresses the required stop request, underlying backend policy and actual
Termination identity; it does not create another result or expose queue entries.

The port-only IExecutorService interface is removed. IEventExecutorGroup inherits
IExecutor directly and owns shutdown state and the retained synchronous wait.
The inherited JDK comments for those members remain in place; the full retired
interface and removed Netty member comments are archived below. Synchronous
Shutdown/AwaitTermination and protected queue hooks remain distinct open reviews.

Ordered StopAsync still drains accepted work and cancels schedules; unordered
withdraws waiting work, releases captures and requests only its owned StopToken.
The existing cancellable snapshot is still required because task cancellation
can remove membership during enumeration. Only the unused public handle/list
branch is removed. NonSticky forwards native stop; groups request every child.
Persistent completion, callback-failure propagation and observer cancellation
keep their existing policy. Native StopAsync costs are measured before/after,
without attributing the discarded legacy branch's cost to native execution.

Original Java-derived tests only switch CLR fallback cleanup to a native stop
request; scenarios, synchronization, workloads, assertions and original comments
remain. CLR-only list/replay probes now test native canceled results, capture
release, queue withdrawal and repeated/concurrent stop. Their renamed identities
are explicitly mapped; no original case is removed or remapped. The former legacy
stop theory row becomes a repeated native request, including the same two queued
results and one owned stop notification. Existing stop and raw rejection probes
remain. There is no replacement diagnostic list or test-only public API.

Retired Netty member comment provenance (pinned e66ce34777f9c4a0c57ac74bb97396ca2f54b43c):

common/src/main/java/io/netty/util/concurrent/AbstractEventExecutor.java:80

```java
    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    @Override
    @Deprecated
    public List<Runnable> shutdownNow() {
        shutdown();
        return Collections.emptyList();
    }
```

common/src/main/java/io/netty/util/concurrent/AbstractEventExecutorGroup.java:80

```java
    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    @Override
    @Deprecated
    public List<Runnable> shutdownNow() {
        shutdown();
        return Collections.emptyList();
    }
```

common/src/main/java/io/netty/util/concurrent/EventExecutorGroup.java:74

```java
    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    @Override
    @Deprecated
    List<Runnable> shutdownNow();
```

common/src/main/java/io/netty/util/concurrent/NonStickyEventExecutorGroup.java:106

```java
    @Override
    public List<Runnable> shutdownNow() {
        return group.shutdownNow();
    }
```

common/src/main/java/io/netty/util/concurrent/UnorderedThreadPoolEventExecutor.java:137

```java
    @Override
    public List<Runnable> shutdownNow() {
        List<Runnable> tasks = super.shutdownNow();
        terminationFuture.trySuccess(null);
        return tasks;
    }
```

Prior CLR spellings and JDK-interface comment provenance (retired APIs):

src/Netty.NET.Common/Concurrent/AbstractEventExecutor.cs

```csharp
    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    [Obsolete]
    public virtual List<Action> ShutdownNow()
    {
        Shutdown();
        return new List<Action>();
    }
```

src/Netty.NET.Common/Concurrent/AbstractEventExecutorGroup.cs

```csharp
    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    [Obsolete]
    public virtual List<Action> ShutdownNow()
    {
        Shutdown();
        return new List<Action>();
    }
```

src/Netty.NET.Common/Concurrent/NonStickyEventExecutorGroup.cs

```csharp
    //@SuppressWarnings("deprecation")
    public List<Action> ShutdownNow()
    {
        return _group.ShutdownNow();
    }
```

src/Netty.NET.Common/Concurrent/IEventExecutorGroup.cs

```csharp
    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    [Obsolete]
    new List<Action> ShutdownNow();
```

src/Netty.NET.Common/Concurrent/IExecutorService.cs

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public interface IExecutorService : IExecutor
{
    /**
     * Initiates an orderly shutdown in which previously submitted
     * tasks are executed, but no new tasks will be accepted.
     * Invocation has no additional effect if already shut down.
     *
     * <p>This method does not wait for previously submitted tasks to
     * complete execution.  Use {@link #awaitTermination awaitTermination}
     * to do that.
     *
     * @throws SecurityException if a security manager exists and
     *         shutting down this ExecutorService may manipulate
     *         threads that the caller is not permitted to modify
     *         because it does not hold {@link
     *         java.lang.RuntimePermission}{@code ("modifyThread")},
     *         or the security manager's {@code checkAccess} method
     *         denies access.
     */
    void Shutdown();

    /**
     * Attempts to stop all actively executing tasks, halts the
     * processing of waiting tasks, and returns a list of the tasks
     * that were awaiting execution.
     *
     * <p>This method does not wait for actively executing tasks to
     * terminate.  Use {@link #awaitTermination awaitTermination} to
     * do that.
     *
     * <p>There are no guarantees beyond best-effort attempts to stop
     * processing actively executing tasks.  For example, typical
     * implementations will cancel via {@link Thread#interrupt}, so any
     * task that fails to respond to interrupts may never terminate.
     *
     * @return list of tasks that never commenced execution
     * @throws SecurityException if a security manager exists and
     *         shutting down this ExecutorService may manipulate
     *         threads that the caller is not permitted to modify
     *         because it does not hold {@link
     *         java.lang.RuntimePermission}{@code ("modifyThread")},
     *         or the security manager's {@code checkAccess} method
     *         denies access.
     */
    List<Action> ShutdownNow();

    /**
     * Returns {@code true} if this executor has been shut down.
     *
     * @return {@code true} if this executor has been shut down
     */
    bool IsShutdown();

    /**
     * Returns {@code true} if all tasks have completed following shut down.
     * Note that {@code isTerminated} is never {@code true} unless
     * either {@code shutdown} or {@code shutdownNow} was called first.
     *
     * @return {@code true} if all tasks have completed following shut down
     */
    bool IsTerminated();

    /**
     * Blocks until all tasks have completed execution after a shutdown
     * request, or the timeout occurs, or the current thread is
     * interrupted, whichever happens first.
     *
     * @param timeout the maximum time to wait
     * @param unit the time unit of the timeout argument
     * @return {@code true} if this executor terminated and
     *         {@code false} if the timeout elapsed before termination
     * @throws ThreadInterruptedException if interrupted while waiting
     */
    bool AwaitTermination(TimeSpan timeout);

}

```

src/Netty.NET.Common/Concurrent/UnorderedThreadPoolEventExecutor.cs

```csharp
    // Legacy stop diagnostics expose native callbacks over canceled memberships.
    // StopAsync remains the result-bearing stop API; these callbacks cannot revive work.
    public List<Action> ShutdownNow() => StopCore(true);
```

Final retirement validation: default Debug/Release each 2,120 cases with 2,106
passed, zero failed and the same 14 skips; targeted Debug/checked Release each 304
passed. Original 759 non-Porting identities/outcomes remain; 19 CLR-only mappings
are explicit in stop-api-identity-and-inventory.json. All 271 comment rows have no
coverage loss, including 92/92 comments across the seven changed owners. The
identical non-friend consumer's native ordered/unordered/wrapper/group stops pass
before and after; only the exported-surface removal check fails before/passes after.
Suffix and four isolated worker-failure modes pass using the updated scripts.
Native stop allocation is unchanged at approximately 1,976 bytes per whole
null-worker construction/admission/StopAsync/drain operation: Release, tiered
compilation disabled, 5,000 warmups and three 100,000-operation samples per version.
No exception/result-bearing submission or controlled throughput claim. Ignored
consumer/perf evidence: artifacts/stop-api-validation/allocation-evidence.json.
Raw rejection and ordered replay/SubmitAsync callback costs remain; the deleted
legacy returned-handle path no longer exists. Broader API/queue/common review
remains open. Earlier legacy-list callback sections describe historical checkpoints.

## Required queue membership and native empty queues

The protected executor queue review exposes a lower shared-boundary problem:
IQueue<T>.Drain(IConsumer<T>, int) and the public Collections.BlockingMessageQueue
were inherited/port-only message-passing additions, without original Netty callers.
Pinned DefaultPriorityQueue/EmptyPriorityQueue and PriorityQueue extend Java Queue;
they do not expose JCTools drain. Recycler.java:557-615 uses relaxedPoll/relaxedOffer,
size and clear; its private BlockingMessageQueue implements extra JCTools methods
but no original pool consumer calls drain. Its CLR ReturnQueue and private monitor
queue already preserve the real pool contract. This private implementation and
all original Recycler comments remain; the unrelated public duplicate, including
its NotImplementedException removal stub, is deleted. Shared Drain and the unused
IConsumer functional interface are retired rather than changed into an unused
native delegate API. MpscIntQueue's required Drain(int, Action<int>) is separate
and unchanged. Needed enqueue/dequeue/peek/remove/count/clear and blocking waits
remain; their atomic membership/finite capacity requirements justify the existing
queue adapter. No wholesale BCL Queue substitution is claimed.

EmptyPriorityQueue has a real codec-http2 consumer: WeightedFairQueueByteDistributor
lines 97-106 chooses it when maxStateOnlySize is zero; 127/171-219/249-250/376 use
typed removal, size, peek/poll/add and priority updates. The native IPriorityQueue
provides these through Count, TryPeek/TryDequeue/TryEnqueue and typed operations.
Reference equality at 739-744 determines per-queue node index ownership. The empty
queue never admits/owns a node and cannot change another queue's stored index.
Its clear/priority operations deliberately do nothing because caching is disabled;
this is required disabled behavior, not a missing implementation hidden as success.

The Java singleton is globally shared via an unchecked generic cast; CLR types
are reified and each T has an immutable typed singleton. No mutable registry or
global counter is needed. Native Try methods distinguish absent entries from
default(T), including zero-valued structs. Redundant Size/Offer/Poll/Peek/Element
and no-argument Remove aliases are retired; bool Try operations carry the required
contract. Java's equals considers any empty PriorityQueue equal, while a mutable
queue uses object identity, making equality asymmetric. The exact pinned class
probe confirms this. No original consumer uses this content equality; actual
node-index owners use reference equality. CLR empty/mutable queues now both use
object identity, preserving symmetric equality and stable dictionary keys across
queue mutations. ToString uses the original simple name without CLR arity suffix.
This is an intentional documented equality adaptation, not Java parity.

Four CLR cases cover required surface, native delegate capacity/FIFO/first removal,
value-type absence and stable identity keys/foreign node indices. The initial key
test supplied a plain object to the indexed heap and correctly failed; the fixture
now uses an indexed node, without weakening heap admission. All existing original
fixtures remain unchanged. Before/after native and pinned Java probes plus final
matrices and allocation evidence are recorded in common-porting.md and ignored
artifacts/queue-surface-validation. Empty source/native review is scoped complete;
protected executor IRunnable storage/hooks, wider queue costs and common/platform
review remain open. No new Markdown file or public drain adapter is added.

Final queue validation: default Debug/Release each 2,124 cases with 2,110 passed,
zero failed and the same 14 skips; targeted Debug/checked Release each 214 cases
with 206 passed and eight existing Recycler skips. All 2,120 prior outcomes and
759 original non-Porting cases remain unchanged; four native queue cases added.
All 271 comment rows have no coverage loss, including 79/79 scoped owner comments.
The identical native consumer and exact Java probe support the stated membership,
disabled-cache and intentional CLR equality policies. Immutable empty membership
allocates zero bytes before/after: Release, tiered compilation disabled, 5,000
warmups and three 100,000-operation samples. No executor/Recycler allocation or
controlled throughput claim. Evidence: queue-surface-identity-and-inventory.json
and artifacts/queue-surface-validation/allocation-evidence.json. Empty review is
complete on Windows/x64/net10.0; protected executor hooks and broader review remain.

## Native executor ready queues

Pinned SingleThreadEventExecutor.java:250-421/482-553/1000-1069 defines overridable
queue creation/admission/removal/waking and task-taking helpers. Actual transport
DefaultEventLoop/ThreadPerChannelEventLoop take tasks; SingleThreadIoEventLoop:
334-342 substitutes a nonblocking queue; SingleThreadEventLoop:135-165 offers,
removes and drains a tail queue. These hooks remain, now using IQueue<Action> and
Action directly. DefaultEventExecutor consumes the native callback; native safe
execution helpers preserve the original exception boundary. Raw execution still
does not capture ExecutionContext; native SubmitAsync/scheduling keep their own
result, context, cancellation and invocation policies.

Action value equality can match a different queued method-group instance. The
default executor queue therefore selects ReferenceEqualityComparer explicitly;
custom executor queues must preserve exact callback membership for rollback.
LinkedBlockingQueue's ordinary overload retains native default value equality.
Capacity, FIFO, interruptible blocking waits, wake filtering, shutdown rollback,
suspension and virtual hooks remain. Rejection passes the admitted callback
directly without unwrap/rewrap; a composed multicast Action remains ordinary work.
The original lazy test uses a native method-group callback to classify its known
fixture receiver, retaining the original no-wake/flush scenario and assertions.

NativeScheduledWork caches one opaque QueueCallback at construction. The same
callback crosses virtual submission, due transfer, full-queue rollback, periodic
reinsertion and cancellation dispatch. Deadline membership remains IScheduledWork
with its original per-queue index/id. Exact callback recognition in ExecutorWork
still guards metadata recovery for other backends; no arbitrary Action.Target
inference, thread-static bridge, alternate public Execute, or per-repeat wrapper.
Four regressions cover distinct value-equal method-group removal, native tail
failure isolation/wake filtering, full/periodic/cancel callback lifetime and direct
rejection/reoffer identity. Existing original fixture scenarios/comments remain.
The independent non-friend consumer uses real custom Action hooks plus native
submit/cancel/schedule/lazy execution on the owned worker. Identical source cannot
compile against the prior IRunnable hooks; this proves an API migration, not an
upstream behavioral defect. Final matrix/conservation results are in the checkpoint.

Release allocation comparison (Windows/x64/net10.0, tiered compilation disabled,
identical programs, 5,000 warmups and three 100,000-operation samples per mode):
raw Execute/drain 72 -> 48 bytes, full-queue discard 120 -> 0, periodic due/run
88 -> 88, native SubmitAsync/drain/result 408 -> 408. Creating/executing a fresh
zero-delay schedule from inside the event loop is 696 -> 800 (+104) bytes: the
cached callback/property is paid once per new membership, outside the periodic
steady-state measurement. This is a measured tradeoff, not allocation parity for
all scheduling. No controlled throughput/contention claim. Initial scheduled
callback cost and broader bridge cleanup remain open. Raw unordered rejection's
separate +64 cost remains; old ordered rejection/queue-wrap costs are superseded
for this path, while native SubmitAsync's existing callback envelope remains.
Evidence: artifacts/native-queue-validation/allocation-evidence.json. Global,
Immediate and NonSticky internal Runnable storage, public scheduled membership,
obsolete lazy marker interfaces and synchronous shutdown/wait review remain open.

## Scheduled callback ownership and allocation

Reopened the native scheduling boundary for the measured +104-byte fresh on-loop
schedule cost from the ready-queue migration. Pinned ScheduledFutureTask.java:
145-203 retains the same membership across delayed admission, repeat/id/index,
cancel/remove and executor shutdown; AbstractScheduledEventExecutor.java:269-300
creates the membership with its owning executor. Native Task result/context and
cancellation policy stay unchanged.

NativeScheduledWork now binds QueueCallback directly to its Run method, avoiding
the extra ExecutorWork object. ExecutorWork recovers only an assembly-owned
ITaskScheduledWork's exact issued callback by ReferenceEquals. A cloned Action,
new method-group delegate or composed multicast remains ordinary work, retaining
every invocation. Exact issuance, rather than arbitrary target/method inference,
is the marker contract. The stable callback still serves full-queue rollback,
periodic transfer and cancellation; existing non-scheduled envelopes remain.
AbstractScheduledEventExecutor binds clock/can-run/enqueue/remove callbacks once
in its constructor. Binding invokes no virtual method during construction; calls
read the live virtual clock/shutdown state and retain each original owner. Readonly
owner fields avoid a mutable registry or concurrent lazy-initialization path.

Two regressions validate exact issuance/copy/composition and live fixed-delay time
plus shutdown. The initial test forgot to transfer/dequeue its deadline membership
before manually invoking the callback; that harness step is repaired without a
library behavior change. Original fixtures/scenarios/comments are unchanged.
The identical non-friend native consumer passes before and after. Validation and
six-mode allocation results are in the current checkpoint and ignored
artifacts/scheduled-callback-validation. Fresh on-loop scheduling is 800 -> 512
bytes, while manual executor construction is 1168 -> 1456 (+288 once). Creating
one executor and one schedule breaks even against the preceding commit; repeated
scheduling on that owner benefits, owners without scheduling pay the added cost.
This comparison includes a mock-clock probe subclass, not real worker startup.
Raw 48, discard 0, periodic 88 and SubmitAsync 408 bytes/operation stay unchanged.
Release Windows/x64/net10.0, tiered compilation disabled, identical sources,
5,000 warmups and three 100,000-operation samples per mode/version; no controlled
throughput/contention claim. The earlier on-loop +104 regression is superseded
for this measured path; broader backend/API/platform review remains open.

The expanded checked Release selection exposed the existing original
TestDeadlineNanosNotOverflow failure: the C# addition threw before its saturation
check. Pinned AbstractScheduledEventExecutor.java:95-99 deliberately wraps Java
long addition and then clamps a negative result. The CLR addition is now explicitly
unchecked before the unchanged clamp. Four identical operand pairs in the exact
isolated pinned Java method and corrected checked C# method pass; the prior checked
C# method fails the two overflow pairs. Original fixture and assertions are unchanged.
Ignored deadline-before/after/java probes record this additional contract repair.

A final targeted run also exposed a CLR-only test observer race: xUnit's exception
recording read Exception.Message under a resource lock while the submitting thread
still had the expected restored interrupt. The interruption escaped inside xUnit,
not the rejection policy. NativeRejectionContractTest now captures the rejection,
consumes/records the pending flag via Sleep(0), then asserts the same exact rejection,
wake count and interrupt expectation. Both original operands/scenarios remain;
no rejection implementation change, new skip or relaxed result assertion. The
failing stack and final matrices are retained in ignored validation evidence.

## Immediate native Action storage

Pinned ImmediateEventExecutor.java:36-55,104-128 queues reentrant caller-thread
work FIFO and drains it despite logged callback failures. DefaultPromiseTest.java:
200-225 exercises chained completion/stack limits; PromiseCombiner.java:74 uses
the singleton by default. Transport DefaultChannelGroupFuture.java:227-234 also
exempts it from the owned-worker deadlock check. Those consumers require the
execution contract even before transport is ported.

ImmediateEventExecutor stores Queue<Action> and invokes the supplied callback
directly. There is no Runnable recovery or wrapper for native Execute. The same
FastThreadLocal queue/running identities and cleanup remain, rather than creating
a separate CLR thread-local lifetime. A failed multicast stops its remaining
invocations as CLR delegates do; the executor logs it and continues later queued
callbacks. Raw Execute observes live caller ExecutionContext, including mutations
by preceding callbacks. SubmitAsync still owns capture/isolation/cooperative cancel
and Task outcome via the existing submission envelope; queue migration adds no
second completion path. Unsupported termination retains its existing failed Task.

Three CLR regressions cover native multicast failure/FIFO/null rejection, 100,000
reentries at callback depth one on the caller thread, and raw-context versus native
submission capture/cancel. Existing original fixtures stay unchanged. Identical
non-friend consumers additionally validate concurrent callers and FastThreadLocal
RemoveAll/reuse. Full/checked results and comment coverage are in the checkpoint.

Allocation comparison against b9d69e9: Windows/x64/net10.0 Release, tiered compilation
disabled, identical sources, 5,000 warmups and three 100,000-operation samples/mode.
Reused raw and two-no-op multicast Execute/full-drain each cost 24 -> 0 bytes/call.
One cached outer callback plus 64 cached queued callbacks costs 1560 -> 0 bytes/batch.
Native SubmitAsync(cached Func<int>)/invoke/Task result remains 360 bytes/call.
These are warmed queue/caller-thread paths; cold setup, queue growth, fresh caller
delegates, exception logging, legacy Runnable producers and context-bearing
submissions are excluded. No controlled throughput/contention claim. Evidence:
artifacts/immediate-action-validation/allocation-evidence.json. Shared FastThreadLocal
and inherited APIs remain under review; Global/NonSticky storage is the next unit.

## Global native Action storage

Pinned GlobalEventExecutor.java:100-148 transfers due memberships without starving
scheduling behind a busy queue; 278-328 detects the exact quiet-period task before
idle termination and arbitrates restart through the original CAS checks. Production
consumers include AutoScalingEventExecutorChooserFactory.java:173-175 monitoring and
transport AbstractBootstrap.java:531-534 fallback notification. Those contracts
remain required even before transport exists in C#.

The ready queue is LinkedBlockingQueue<Action> with reference identity; raw callbacks
invoke directly and scheduled work transfers its cached QueueCallback. A readonly
quiet callback reference, rather than delegate value equality/target inference,
preserves quiet-stop recognition. Deadline heap, repeat/cancel ownership, execution
context, exceptions and restart/CAS policy remain. The private worker runner no
longer implements Runnable. TakeTask is internal Action: upstream is package-private
on this sealed single-consumer owner and no real caller outside its runner exists.
The prior C# public Runnable dequeue was accidental exposure and is intentionally
removed; external code using it must submit work through native Execute/SubmitAsync.

Two CLR cases cover exact queue callback identity, FIFO after multicast failure,
native scheduling context/cancel and idle restart. Original busy-queue scheduling,
thread-group, lifecycle and context fixtures remain. Identical non-friend consumers
pass before/after. Against fresh 3f9a627 source, Windows/x64/net10.0 Release with tiered
compilation disabled, 5,000 warmups and three 100,000-operation samples per mode:
producer-thread admission of reused raw/multicast callbacks costs 72 -> 48 bytes;
native SubmitAsync(cached Func<int>) admission remains 408 bytes. Linked-list nodes
are included; a blocked worker is released and each batch drained outside measurement.
Worker/cold/start/restart allocations, new caller delegates, scheduling, logging and
context-bearing submissions are excluded; no throughput/contention claim.

One focused Debug repeat running alongside other matrices stalled after 91 results
and was terminated for diagnosis; it is not a passing run. Stacks/heap show xUnit's
assembly completion wait without executing Netty work. The cause remains unresolved.
The final isolated repeat with a 90-second hang watchdog passes all 166 cases;
both full matrices and checked Release also pass. Captured diagnostic/consumer/cost
evidence is under artifacts/global-action-validation. Broader runtime/API/platform
review remains open; NonSticky runner storage/ownership is the next unit.

## NonSticky native Action storage and settlement

Pinned NonStickyEventExecutorGroup.java:215-292,336-348 orders queued work, limits
each batch, retries after runner admission failure and restores executingThread.
Original tests retain four batch sizes, 10,000 tasks/producer, 5,000 two-submission
races and reschedule-failure affinity. All-module search finds this public utility
and its common tests, without another production caller to invent or exclude.

The selected child's Queue<Action> stores/invokes native callbacks under the
existing gate. Batch reservations still carry admission/stop identity for a real
underlying queue, separate from user Task ownership; inline handoff/stack bounding,
thread affinity and stale-runner guards remain. FinishPending snapshots/clears the
queue under the gate and settles native results outside it. ExecutorWork now shares
one exact issued-callback check between existing Unwrap and GetNativeSubmission.
The latter allocates no raw Runnable wrapper and accepts only an assembly-owned
submission's exact callback. Copies/composed delegates remain caller work, so cancel
or reject must not infer Task ownership from arbitrary targets. No new result owner,
callback invocation during settlement, thread-local bridge or public facade is added.

Three CLR cases validate exact versus copied/multicast ownership for stop/reject,
single settlement after stale runner attempts, and native FIFO/reentry/multicast
failure across one-task batches. Existing actual pool/forwarded stop, admission
failure/recovery, context, worker handoff and inline stack cases remain. Identical
non-friend consumers pass before/after for native callbacks/results and actual pool
stop. All original fixtures and comments remain; matrices/audits are in the checkpoint.

Against fresh 3bcfd15, Windows/x64/net10.0 Release with tiered compilation disabled:
queue capacity primed with 100,001 admissions, 5,000 warmups/mode, three 100,000-operation
samples/mode/version. Behind one held reservation, cached raw/multicast admission is
24 -> 0 bytes/call; native SubmitAsync(cached Func<int>) admission remains 360 bytes.
Raw Execute/manual native backend drain per operation, including each fresh runner
reservation/envelope, is 160 -> 136 bytes. Held-path reservation creation/drain,
cold setup/capacity growth, fresh caller delegates, result observation, logging and
shutdown allocation are excluded. No real-worker/throughput/contention claim.
Evidence: artifacts/nonsticky-action-validation/allocation-evidence.json. The prior
Global focused xUnit completion stall remains unresolved in its own design section;
this unit's matrices completed. Scheduled/lazy markers and broader synchronous
API/runtime/platform review remain open.

## Native lazy scheduling and wakeup callbacks

Pinned AbstractEventExecutor.java:159-168 and SingleThreadEventExecutor.java:996-1007,
1122-1134 separate explicit lazy admission from deprecated marker interfaces.
All-module source/test search finds only the two marker declarations, no implementer
or runtime check. Transport AbstractChannelHandlerContext.java:1034-1042 uses the
lazyExecute method. Keep LazyExecute(Action), WakesUpForTask(Action), queue policy
and submission hooks; remove the unused nested markers and two C# helper interfaces.
The latter provided no CLR behavior or additional ownership. Their comments remain
below, together with the original nested-marker comments.

Pinned AbstractScheduledEventExecutor.java:40-44,344-360 sends an empty wakeup after
lazy admission when the second hook detects a race. Store that callback as one
static Action, so dispatch reaches Execute(Action) without a new Runnable envelope
on each wakeup. Scheduled Task/cancellation/deadline ownership remains unchanged.
SingleThreadEventExecutor.java:286-293,314-321,343-348,1345-1349 uses that same
inherited marker for poll/take/drain; the C# loop previously had a different no-op
callback. Point its existing private alias at the shared native callback and use
ReferenceEquals, preserving Java object identity rather than delegate value equality.
A copied native Action remains ordinary queue work. Poll skips the marker, take
returns null on wakeup, and an otherwise empty drain reports no user work.
IScheduledWork's Runnable inheritance and synchronous lifecycle review stay open.

Retired nested markers from AbstractEventExecutor/SingleThreadEventExecutor:

```java
    /**
     *  @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
     *
     */
    public interface LazyRunnable extends Runnable { }

    /**
     * @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
     */
    protected interface NonWakeupRunnable extends LazyRunnable { }
```

Retired port-only helper interfaces, including their complete comments:

```csharp
using System;

namespace Netty.NET.Common.Functional;

/**
 *  @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
 *
 */
[Obsolete]
public interface ILazyRunnable : IRunnable
{
}
```

```csharp
using System;

namespace Netty.NET.Common.Functional;

/**
 * @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
 */
[Obsolete]
internal interface INonWakeupRunnable : ILazyRunnable
{

}
```

Five new CLR cases cover three native before/lazy/after hook branches with producer
context and cancellation before queue transfer, plus poll/take consumption of the
shared wakeup while retaining a copied Action. Before changes, the reusable-callback
case fails; before sharing the marker, both poll/take cases fail. Existing original
fixtures and native queue, scheduled ownership/context/deadline/capacity, and real
worker lazy/wakeup hook cases remain in the affected matrix. Identical non-friend
C# consumers retain results/context/cancellation before/after and expose shared-marker
consumption changing from false to true.

Windows/x64/net10.0 Release, tiered compilation disabled: cached Func<int> schedule
admission into a fixed native Action array, 5,000 warmups then three 100,000-operation
samples per mode/version. Three-sample medians: direct remains 424 bytes/call; lazy plus after-hook
wakeup falls from 520 to 424 (one before sample has eight extra measured bytes over
100,000 admissions). Thus the wakeup envelope's 96 bytes are removed. Array
storage/cold setup/new caller delegates/result observation/scheduled execution/drain/
cancel/stop/logging/real workers are outside measurement; no throughput/contention
claim. Fresh HEAD archive is the before build. Evidence and exact sample scope:
artifacts/lazy-action-validation/allocation-evidence.json; final matrix/inventory/
comment checks are in the current common-porting checkpoint. No source status or
broader completion claim changes.

## Native scheduled callback boundary

Pinned AbstractScheduledEventExecutor.java:174-233 returns Runnable from both
poll overloads and retains ScheduledFutureTask metadata only for heap rollback.
SingleThreadEventExecutor.java:361-376 and GlobalEventExecutor.java:139-145 execute
those due tasks. Transport EmbeddedEventLoop.java:73-84 polls with its clock and
runs each callback; ManualIoEventLoop.java:174-177 transfers into its ready queue.
ScheduledFutureTask.java:129-160 handles expiry, owned cancellation and periodic
reinsertion. All-module pollScheduledTask consumers were reviewed.

IScheduledWork keeps deadline/sequence/cancel membership with one stable QueueCallback
Action and no Runnable inheritance. Both protected poll overloads return Action.
A private PollScheduledWork retains the exact membership for full-ready-queue rollback;
consumers do not unwrap metadata from delegate targets. NativeScheduledWork's invocation
method is private; its existing callback, Task/TCS result and claiming/cancellation/
context remain. Ordered/global/unordered backends and suspension-aware removal dispatch
that callback directly. No additional callback or Task is created by the new boundary.

ExecutorWork now bridges only the remaining real Runnable producers. Its scheduled
fast path and metadata recovery are removed; original exact-envelope checks for native
submissions/runner reservations remain. Archived retired CLR bridge comments:

```csharp
        // Construction initializes this once; later scheduled transfers reuse it.
        if (work is ITaskScheduledWork scheduled && scheduled.QueueCallback is { } callback) return callback;

        // Scheduled work is an assembly-owned marker with an exact issued callback.
        // A caller's copy of its Run delegate or a multicast is still ordinary work.
        if (command.Target is ITaskScheduledWork scheduled && ReferenceEquals(command, scheduled.QueueCallback)) return scheduled;
```

The original AbstractScheduled fixture uses native callbacks with unchanged Java
scenarios, assertions, identities and comments. Other harnesses store/execute Action;
forwarding captures metadata only from actual deadline-queue membership. The CLR-only
ScheduledMetadataRecoveryAcceptsOnlyTheExactIssuedCallback case becomes
ScheduledCallbacksKeepOneInvocationWhenCopiedOrComposed: stable issued callback, value-equal
copy, forwarding/composition, due transfer and single-invocation result claiming remain,
while assertions about the retired Runnable metadata-recovery API are removed.
All other baseline identities and the original 759 results must remain unchanged.

The identical before/after consumer uses a validation-only Runnable overload for the
old poll return type; the after build binds its Action overload. A separate non-friend
consumer imports no Java functional types and compiles both protected poll overloads
as Action, checking heterogeneous FIFO/context, capacity rollback, multicast single
claim, detached cancellation and periodic callback/ID stability. Evidence:
artifacts/scheduled-action-validation/{before,after}-consumer.log and native-consumer.log.

Held ScheduleAsync(Func<int>) admissions in a fixed native Action array remain
424 bytes/call in both direct and lazy-plus-wakeup hook modes before/after. Release
Windows/x64/net10.0, tiered compilation disabled, 5,000 warmups and three samples of
100,000 operations/mode/version, fresh HEAD-archive baseline. Cold setup/array storage/
new caller delegates/result observation/execution/drain/cancel/stop/logging/real workers
excluded; no optimization/throughput claim. Sample scope is in ignored
artifacts/scheduled-action-validation/allocation-evidence.json. Final matrices,
identity remap and comment audit are in the current common-porting checkpoint;
source statuses and broader native/backend/platform completion remain unchanged.

## Native synchronous lifecycle and inactivity waits

Pinned transport ThreadPerChannelEventLoopGroup.java:185-188 and 243-267 still
uses child shutdown and bounded awaitTermination; ManualIoEventLoop.java:516-523
and common MultithreadEventExecutorGroup construction cleanup also use synchronous
lifecycle operations. Keep Shutdown/AwaitTermination where their contracts remain
useful: a bool state wait does not rethrow a failed Termination Task, and legacy
shutdown policy is distinct from native StopAsync. Async consumers use the existing
Task lifecycle; this review does not certify all remaining executor APIs.

GlobalEventExecutor.java:214-223 and ThreadDeathWatcher.java:132-142 capture one
worker and call Java join(unit.toMillis(timeout)). All-module pinned consumers of
awaitInactivity consist of these declarations and ThreadDeathWatcherTest.java:112,
which waits for Long.MAX_VALUE seconds. The earlier compatibility translation
made zero and every submillisecond value, even a small negative, wait indefinitely.
A fresh baseline build reproduces this while the worker is deliberately held live.
These public TimeSpan APIs now use the CLR convention: zero polls, a positive
duration has a bounded budget, Timeout.InfiniteTimeSpan explicitly waits indefinitely,
and all other negative values throw ArgumentOutOfRangeException. This intentionally
changes Java's truncation/zero-join convention, rather than claiming Java equivalence.

One internal ThreadJoin helper validates even an absent watcher, rounds positive
remaining ticks up to native milliseconds, and shares a monotonic deadline across
Int32-millisecond chunks. It introduces no thread/Task owner. Interrupts propagate
through native Join; snapshot-thread ownership, watcher polling/restart/context
suppression, Global's never-started error and unsupported termination remain.
The result does not promise that a later producer cannot restart the service.
Archived comments from the superseded CLR join implementations:

```csharp
        // CLR adaptation: Java join truncates to milliseconds and treats zero as unbounded.
        // CLR Join(TimeSpan) instead treats zero as a poll and caps its argument at Int32 milliseconds.

        // Java join truncates to milliseconds, treats zero as unbounded, and
        // permits waits beyond the CLR Join(Int32) range. Keep those semantics.
```

Only CLR probes for the former Java zero/submillisecond convention are remapped
to the native finite/negative contract (two Global and three watcher rows). Four
new rows cover explicit infinite completion and interruption in both services.
Existing huge interruptible waits, worker restart/context/identity/registration
cases and original Java fixture identities/comments remain. A separate identical
before/after C# consumer exercises cold negative validation, live zero/submillisecond/
negative/infinite/TimeSpan.MaxValue waits and completed-worker polls. Its blocked
observers are interrupted for bounded cleanup; production workers are released
after observation. Evidence: artifacts/inactivity-validation/{before,after}-probe.log.
No timing precision, allocation or throughput claim; final matrix and comment audit
are in the current common-porting checkpoint. Broader source/backend review remains open.

## Native thread death watcher callbacks

Pinned ThreadDeathWatcher.java:79-121,146-278 stores Runnable with a Thread,
removes one matching entry by reference identity, removes dead-thread membership
before invoking a callback, isolates callback failure and arbitrates one polling
worker with CAS. ReferenceCountUtil.java:160-165,184-216 is the only production
caller in an all-module pinned search. The buffer allocator test has an old watcher
comment, not a production registration. The three original watcher tests cover
live/dead delivery, unwatch and nonsticky factory group inheritance.

Watch/Unwatch now accept Action only, and Entry stores/invokes that exact delegate.
Keep the registered Action instance for cancellation: a value-equal clone or
newly combined delegate is another registration identity. Match both thread and
callback; remove one duplicate. No inferred delegate-target ownership or wrapper
is needed. Multicast uses native invocation order/exception semantics, while failure
of one registration still allows other registrations to run. Raw callback invocation
does not capture the producer's ExecutionContext; singleton worker startup still
suppresses inherited context, without promising isolation between callback bodies.
Private Watcher retains its list/worker loop without Runnable inheritance.

ReleaseLater supplies a bound ReleasingTask.Run delegate. Its state object remains
because it owns the counted object/decrement and the original diagnostic ToString;
the pending entry keeps it alive until callback consumption or cancellation.
Release timing, requested decrement, return identity and failure isolation remain.
This creates one bound Action for a deferred release, with no allocation reduction
claim. Deprecation and the original intended test-helper scope remain documented.

The original fixture uses native Action with unchanged identities/scenarios/assertions
and all nine comments. The existing CLR duplicate/equality probe uses an Action
clone in place of an equality-overriding Runnable. Two new cases cover thread-specific
cancellation of a shared callback and copied multicast/failure isolation. Existing
registration concurrency, reentrancy, worker interrupt/restart/context and deferred
release cases remain. A separate non-friend C# consumer uses only native Action,
Thread and Task plus reference-count APIs: it cannot compile against the fresh HEAD
baseline (Action-to-Runnable argument errors), then passes against the new Release
build, including deferred release/context/restart. Evidence:
artifacts/watcher-action-validation/{before-consumer-build,native-consumer}.log.
Final matrices, 55 original source/test comments and inventory checks are in the
current common-porting checkpoint. Broader executor/backend/platform review remains open.
