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
pending with VarHandleFactory/native transport integration: ordinary word access
here provides neither atomicity nor acquire/release synchronization.

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
VarHandleFactory also supplies endian byte-memory views and remains pending.

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
