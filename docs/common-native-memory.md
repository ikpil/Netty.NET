# Netty common: native allocation, ownership and memory views

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `../netty`.
The allocation/lifetime foundation is implemented using CLR memory abstractions.
This does not certify the remaining PlatformDependent methods, future buffer
retain/release integration, or every common component as complete.

## Original requirements and actual consumers

The pinned CleanableDirectBuffer/Cleaner/DirectCleaner separate allocation,
storage access and deterministic deallocation. Cleaner.reallocate consumes the
old owner and preserves the retained prefix. DirectCleaner adjusts the memory
reservation by the capacity delta and restores it on failure. The zero-capacity
allocation still obtains at least one native byte so its address is nonzero.

| Original consumer | Required ownership/use | Native CLR expression |
| --- | --- | --- |
| `buffer/.../PoolArena.java` | Keep the allocation owner separately from its aligned memory slice; clean the base allocation when the chunk dies. | NativeMemoryOwner plus Memory<byte> slices; GetAlignedMemory returns a bounded view and never another owner. |
| `buffer/.../PoolArena.java` memoryCopy | Copy byte ranges without changing shared buffer positions. | Memory/Span slices and CopyTo, including overlapping regions. No mutable position facade. |
| `buffer/.../UnpooledUnsafeNoCleanerDirectByteBuf.java` | Reallocate after all old users stop, preserve the prefix, install a replacement owner. | NativeMemoryAllocator.Reallocate consumes the old owner on success; saved views reject new access. |
| `buffer/.../WrappedUnpooledUnsafeDirectByteBuf.java` | Describe an externally supplied address/length and keep its external ownership rule explicit. | NativeMemoryView is borrowed and never frees the address. The external owner must cover every span/pin use. |
| `buffer/.../UnsafeByteBufUtil.java` and native I/O consumers | Access a stable address for a bounded byte region. | Memory.Pin produces an offset-aware MemoryHandle whose lease keeps owned allocation alive. |
| `common/.../AsciiString.java` | Copy a non-array-backed memory range into independent string storage. | Existing ReadOnlyMemory constructor copies the native range; the string remains valid after owner disposal. |

No buffer module implementation is claimed here. These common consumer tests
establish usable C# ownership and memory contracts for its later implementation.

## Removal of the incomplete Java facade

The old ByteBuffer returned -1 capacity, false directness and empty slices. Its
constructor ignored the native address. NoopCleaner allocated with AllocHGlobal
but its clean method did nothing, leaking the allocation. The normal cleaner
fields were never initialized. Keeping these APIs would publish fake success or
fail at stubs instead of establishing resource ownership.

ByteBuffer, ICleaner, ICleanableDirectBuffer, DirectCleaner and NoopCleaner are
removed. PlatformDependent/PlatformDependent0 no longer expose their Java direct
buffer construction, alignment, cleanup selection and memory-counter facades.
The Java default-direct preference/expensive-cleaner selection and JVM direct
memory estimator are also removed: native allocation is an explicit choice,
its budget is explicit, and CLR owners do not need JDK reflection/linker providers.
Heap-vs-native allocation policy remains a consumer choice for future allocators.

CleanableDirectBuffer becomes IMemoryOwner<byte>/IDisposable with Memory<byte>.
Cleaner/DirectCleaner use NativeMemory, SafeHandle and MemoryManager instead of
a replicated provider hierarchy. CleanerJava6/9/24Linker/25 are JVM provider
implementations, with their native allocation/deallocation purpose fulfilled by
the CLR owner. Their exact original comments are retained as provenance below.
The unused OutOfDirectMemoryErrorException adapter remains a separate pending
review; allocation failures here use the standard OutOfMemoryException.

## Native API and lifetime rules

NativeMemoryAllocator.Allocate(length, clear) returns a NativeMemoryOwner.
Initial contents are unspecified unless clear is true. Zero length exposes empty
Memory but allocates one native byte, preserving a stable nonzero pin address.
The allocator's reservation count includes that byte and excludes malloc metadata.
The explicit budget is scoped to an allocator, not an inferred managed-heap cap.

NativeMemoryAllocator.Shared supplies a shared Netty reservation domain. A positive
io.netty.maxDirectMemory environment setting is read on its first use and limits
all its owners. Zero/negative/unset settings impose no inferred CLR heap limit;
constructing a separate allocator creates an independent explicit domain. The
one-byte empty-allocation accounting deliberately differs from Java's zero logical
capacity count. No native allocation is permitted by an explicit zero-byte budget.

NativeMemoryOwner.Memory and slices do not transfer ownership. New data access
and pin acquisition reject disposed owners. Dispose is idempotent. Already
obtained native spans cannot be revoked: keep the owner alive and exclude disposal
or reallocation throughout unpinned access. A Memory view is not a shared-buffer
retain/release token; that policy remains necessary for pooled buffer storage.

Pins acquire SafeHandle references before exposing pointers. Disposal invalidates
the owner but the physical allocation and quota remain until the last pin ends.
Every returned MemoryHandle owns an independent release lease; struct copies
share that lease, and repeated/copied disposal cannot release another pin. A copy
is not a new lifetime claim and must not be used after its shared lease ends.
Pin and disposal races are serialized; either a valid retained pointer or an
ObjectDisposedException results. Pins can span async operations.

SafeHandle provides abandoned-owner fallback. Pin leases also release their
SafeHandle reference on finalization when a handle is abandoned. Cleanup contains
no user callback and does not need an owner-monitor acquisition on the finalizer
thread. The allocator adds/removes GC memory pressure for its physical allocation
sizes; successful reallocation transfers pressure and reservation ownership, and
failed growth restores both. This remains fallback, not a deterministic deadline.

NativeMemoryView describes borrowed address metadata and never frees its storage.
The caller must supply a valid externally owned region for any actual byte access.
Its pins retain the descriptor, not the external allocation. Disposing a descriptor
rejects new access but does not alter its external owner's memory reservation.
A null address is allowed only for empty memory. Arbitrary address-bit metadata
can be described without dereferencing it, as in the original constructor tests.

## Reallocation and alignment

Reallocate validates the allocator domain and requires no active pin. It prepares
replacement managed objects before reallocating, reserves positive growth with
an overflow-safe CAS limit check, and transfers ownership only after success.
Failure leaves the old owner and bytes usable; success invalidates all its saved
views and makes later disposal of the old owner harmless. Retained prefix bytes
survive growth/shrink; newly added bytes are unspecified. Reallocation to zero
keeps the one-byte backing allocation. Concurrent reallocation/disposal has one
ownership winner. Ordinary span users must have stopped before these operations.

GetAlignedMemory requires a positive power of two and leaves the source unchanged.
Its view starts at the next aligned address and truncates its end to an alignment
boundary. If no interval fits, it returns empty memory, whose pointer need not be
aligned. This follows the original alignedSlice interpretation; the JVM legacy
fallback mutated position and aligned only the beginning, which no C# consumer
needs. The PoolArena pattern of allocating payload+alignment still supplies the
required aligned payload capacity.

## Original test mapping and validation

| Original scenario | Native verification |
| --- | --- |
| PlatformDependent0Test negative address | Preserves signed pointer-bit metadata and capacity through a borrowed view; never dereferences -1. |
| PlatformDependent0Test non-negative address | Preserves address 10 and capacity as metadata; never dereferences it. |
| PlatformDependent0Test zero address | Rejects null/nonempty memory explicitly; accepts null/empty memory. Unlike Java's metadata-only ByteBuffer, CLR nonempty Span requires a nonzero pointer. Real pinned allocation consumers supply valid addresses. |
| PlatformDependent0Test Java version/parser and SecurityManager methods | Remain JVM-specific exclusions with exact decisions/comments in common-platform-runtime.md. |
| PlatformDependentTest zero-capacity allocation | Allocates an empty native owner, asserts its nonzero pin address and verifies deterministic reservation return. |
| PlatformDependentTest Java-25 cleaner provider | Replaced by ClrNativeAllocationSupportsDeterministicCleanup, exercising actual zeroed native storage and release. No unsupported-runtime guard hides the operation. |
| PlatformDependentTest equality, constant-time equality, zero and ASCII hash | Original cases remain and execute with the CLR primitive implementation. This is not a certification of all PlatformDependent APIs. |

NativeMemoryContractTest adds 26 cases covering native/empty ownership, bounded
slices, copied pins, quota failures, retained bytes, invalidation, same-size/grow/
shrink/zero reallocation, pinned rejection, concurrency, aligned PoolArena-style
views, borrowing, null/negative bounds, overlapping copy, endian payloads,
AsciiString independent copying, async native I/O and abandoned owner/pin fallback.
PlatformRuntimeContractTest adds a shared-limit case in a fresh collectible
AssemblyLoadContext, with restored environment variables and exclusive execution.

The targeted Debug selection passes 53 cases: 26 native ownership cases, five
runtime selection cases, all nine platform cases and 13 prior AsciiString memory
cases. Whole default Debug/Release results are recorded in common-porting.md.
These are correctness/lifetime checks, not allocation-throughput or latency claims.
Windows/net10.0 is the actually verified environment; other OS behavior is unverified.

The managed-byte default Debug/Release checkpoint passed **1132 / 0 failed /
14 skipped**, including all 26 ownership and nine original platform cases.
The later ordered-multimap checkpoint passes 1161 with zero failures and 14 skips.
Heap-memory builds had 415 existing warnings / 0 errors; latest incremental builds
emit 338 test warnings with the library up to date. Evidence is heap-memory-full-debug.trx,
heap-memory-full-release.trx and common-porting.md. Removed-provider comment
provenance is audited within each code fence; all seven providers have zero missing.

## Remaining review

- PlatformDependent and PlatformDependent0 still contain raw object-field-offset,
  native-address primitive/copy/set operations and other JVM-shaped methods.
  Managed byte operations now use CLR spans; see common-heap-memory.md. Their
  genuine consumer purposes need CLR API migration or supported implementation;
  existing explicit stubs are not treated as ported.
- Future buffer pool/reference-count integration must use ownership/leases correctly;
  disposing an owner is not a substitute for shared retain/release.
- Process-wide allocator configuration, native preference and cost policy require
  consumer integration; the declared shared reservation domain alone is not that
  entire review.
- Allocation/reallocation OOM from actual host exhaustion is handled, but tests
  force bounded quota failures rather than exhausting the user's machine.

CLR specifications: [SafeHandle-backed Memory](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.memorymanager-1?view=net-10.0),
[retention and paired release](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle.dangerousaddref?view=net-10.0),
[native memory APIs](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.nativememory?view=net-10.0).

## Pinned original comment provenance

The following comments describe the removed JVM provider/ByteBuffer mechanisms
and the original allocation/lifetime contracts. They are kept with source paths
and line positions; no fake Java class or unused field is introduced to host them.
Unrelated platform work is still in progress even where its historical initializer
comments are preserved here.

### CleanableDirectBuffer.java

Source: common/src/main/java/io/netty/util/internal/CleanableDirectBuffer.java, line 1

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

Source: common/src/main/java/io/netty/util/internal/CleanableDirectBuffer.java, line 20

```java
/**
 * Encapsulates a direct {@link ByteBuffer} and its mechanism for immediate deallocation, if any.
 */
```

Source: common/src/main/java/io/netty/util/internal/CleanableDirectBuffer.java, line 24

```java
/**
     * Get the buffer instance.
     * <p>
     * Note: the buffer must not be accessed after the {@link #clean()} method has been called.
     *
     * @return The {@link ByteBuffer} instance.
     */
```

Source: common/src/main/java/io/netty/util/internal/CleanableDirectBuffer.java, line 33

```java
/**
     * Deallocate the buffer. This method can only be called once per instance,
     * and all usages of the buffer must have ceased before this method is called,
     * and the buffer must not be accessed again after this method has been called.
     */
```

Source: common/src/main/java/io/netty/util/internal/CleanableDirectBuffer.java, line 40

```java
/**
     * @return {@code true} if the {@linkplain #memoryAddress() native memory address} is available,
     * otherwise {@code false}.
     */
```

Source: common/src/main/java/io/netty/util/internal/CleanableDirectBuffer.java, line 48

```java
/**
     * Get the native memory address, but only if {@link #hasMemoryAddress()} returns true,
     * otherwise this may return an unspecified value or throw an exception.
     * @return The native memory address of this buffer, if available.
     */
```


### Cleaner.java

Source: common/src/main/java/io/netty/util/internal/Cleaner.java, line 1

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

Source: common/src/main/java/io/netty/util/internal/Cleaner.java, line 20

```java
/**
 * Allows to free direct {@link ByteBuffer}s.
 */
```

Source: common/src/main/java/io/netty/util/internal/Cleaner.java, line 24

```java
/**
     * Create a direct {@link ByteBuffer} and return it alongside its cleaning mechanism,
     * in a {@link CleanableDirectBuffer}.
     *
     * @param capacity The desired capacity of the direct buffer.
     * @return The new {@link CleanableDirectBuffer} instance.
     */
```

Source: common/src/main/java/io/netty/util/internal/Cleaner.java, line 33

```java
/**
     * Reallocate a direct buffer with a new capacity. The old buffer is consumed and
     * must not be used after this call.
     * <p>
     * The default implementation allocates a new buffer, copies the data, and frees the old one.
     * Implementations may override this to provide more efficient reallocation (e.g. via
     * {@code Unsafe.reallocateMemory}).
     */
```

Source: common/src/main/java/io/netty/util/internal/Cleaner.java, line 53

```java
/**
     * Free a direct {@link ByteBuffer} if possible
     *
     * @deprecated Instead allocate buffers from {@link #allocate(int)}
     * and use the associated {@link CleanableDirectBuffer#clean()} method.
     */
```

Source: common/src/main/java/io/netty/util/internal/Cleaner.java, line 62

```java
/**
     * Check if the clean operation is "relatively expensive".
     * Expensive clean operations are fine for pooling allocators, but should be avoided for unpooled buffers.
     * @return {@code true} if this Cleaner has an expensive clean
     * (i.e. {@link CleanableDirectBuffer#clean()}) operation.
     */
```


### DirectCleaner.java

Source: common/src/main/java/io/netty/util/internal/DirectCleaner.java, line 1

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

Source: common/src/main/java/io/netty/util/internal/DirectCleaner.java, line 54

```java
// Used for normal allocation — allocates memory and increments counter
```

Source: common/src/main/java/io/netty/util/internal/DirectCleaner.java, line 65

```java
// Used for reallocation — memory already allocated, counter already adjusted
```


### CleanerJava6.java

Source: common/src/main/java/io/netty/util/internal/CleanerJava6.java, line 1

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
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava6.java, line 30

```java
/**
 * Allows to free direct {@link ByteBuffer} by using Cleaner. This is encapsulated in an extra class to be able
 * to use {@link PlatformDependent0} on Android without problems.
 * <p>
 * For more details see <a href="https://github.com/netty/netty/issues/2604">#2604</a>.
 */
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava6.java, line 54

```java
// Call clean() on the cleaner
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava6.java, line 57

```java
// But only if the cleaner is non-null
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava6.java, line 64

```java
// Change receiver to DirectBuffer, convert DirectBuffer to Cleaner by calling cleaner()
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava6.java, line 69

```java
// Change receiver to ByteBuffer, convert using explicit cast to DirectBuffer
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava6.java, line 85

```java
// We don't have ByteBuffer.cleaner().
```


### CleanerJava9.java

Source: common/src/main/java/io/netty/util/internal/CleanerJava9.java, line 1

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

Source: common/src/main/java/io/netty/util/internal/CleanerJava9.java, line 30

```java
/**
 * Provide a way to clean a ByteBuffer on Java9+.
 */
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava9.java, line 47

```java
// See https://bugs.openjdk.java.net/browse/JDK-8171377
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava9.java, line 101

```java
// Try to minimize overhead when there is no SecurityManager present.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava9.java, line 102

```java
// See https://bugs.openjdk.java.net/browse/JDK-8191053.
```


### CleanerJava24Linker.java

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 1

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

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 38

```java
// native image supports this since 25, but we don't use PlatformDependent0 here, since
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 39

```java
// we need to initialize CleanerJava24Linker at build time.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 46

```java
// also need to prevent initializing the logger at build time
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 49

```java
// Only attempt to use MemorySegments on Java 24 or greater, where warnings about Unsafe
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 50

```java
// memory access operations start to appear.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 51

```java
// The following JDK bugs do NOT affect our implementation because the memory segments we
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 52

```java
// create are associated with the GLOBAL_SESSION:
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 53

```java
// - https://bugs.openjdk.org/browse/JDK-8357145
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 54

```java
// - https://bugs.openjdk.org/browse/JDK-8357268
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 66

```java
// First, we need to check if we have access to "restricted" methods through the Java Module system.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 82

```java
// Second, we need to check the size of a pointer address. For simplicity, we'd like to assume the size
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 83

```java
// of an address is the same as a Java long. So effectively, we're only enabled on 64-bit platforms.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 98

```java
// Finally, we create three method handles, for malloc, free, and for wrapping an address in a
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 99

```java
// ByteBuffer. Effectively, we need the equivalent of these three code snippets:
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 100

```java
//
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 101

```java
//        MemorySegment mallocPtr = Linker.nativeLinker().defaultLookup().find("malloc").get();
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 102

```java
//        MethodHandle malloc = Linker.nativeLinker().downcallHandle(
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 103

```java
//                mallocPtr, FunctionDescriptor.of(ValueLayout.JAVA_LONG, ValueLayout.JAVA_LONG));
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 104

```java
//
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 105

```java
//        MemorySegment freePtr = Linker.nativeLinker().defaultLookup().find("free").get();
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 106

```java
//        MethodHandle free = Linker.nativeLinker().downcallHandle(
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 107

```java
//                freePtr, FunctionDescriptor.ofVoid(ValueLayout.JAVA_LONG));
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 108

```java
//
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 109

```java
//        ByteBuffer byteBuffer = MemorySegment.ofAddress(addr).asByteBuffer();
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 130

```java
// Constructing the malloc (long)long handle
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 145

```java
// Constructing the free (long)void handle
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 157

```java
// Constructing the wrapper (long, long)ByteBuffer handle
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 214

```java
// Always allocate at least 1 byte, to avoid relying on non-portable behavior
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 215

```java
// of malloc(3) for zero size allocations.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 218

```java
// Should not happen.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava24Linker.java, line 230

```java
// Should not happen.
```


### CleanerJava25.java

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 1

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

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 27

```java
/**
 * Provide a way to clean direct {@link ByteBuffer} instances on Java 24+,
 * where we don't have {@code Unsafe} available, but we have memory segments.
 */
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 39

```java
// native image supports this since 25, but we don't use PlatformDependent0 here, since
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 40

```java
// we need to initialize CleanerJava25 at build time.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 47

```java
// also need to prevent initializing the logger at build time
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 50

```java
// Only attempt to use MemorySegments on Java 25 or greater, because of the following JDK bugs:
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 51

```java
// - https://bugs.openjdk.org/browse/JDK-8357145
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 52

```java
// - https://bugs.openjdk.org/browse/JDK-8357268
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 61

```java
// Here we compose and construct a MethodHandle that takes an 'int' capacity argument,
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 62

```java
// and produces a 'CleanableDirectBufferImpl' instance.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 63

```java
// The method handle will create a new shared Arena instance, allocate a MemorySegment from it,
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 64

```java
// convert the MemorySegment to a ByteBuffer and a memory address, and then pass both the Arena,
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 65

```java
// the ByteBuffer, and the memory address to the CleanableDirectBufferImpl constructor,
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 66

```java
// returning the resulting object.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 67

```java
//
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 68

```java
// Effectively, we are recreating the following the Java code through MethodHandles alone:
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 69

```java
//
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 70

```java
//    Arena arena = Arena.ofShared();
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 71

```java
//    MemorySegment segment = arena.allocate(size);
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 72

```java
//    return new CleanableDirectBufferImpl(
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 73

```java
//              (AutoCloseable) arena,
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 74

```java
//              segment.asByteBuffer(),
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 75

```java
//              segment.address());
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 76

```java
//
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 77

```java
// First, we need the types we'll use to set this all up.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 81

```java
// Acquire the private look up, so we can access the package-private 'CleanableDirectBufferImpl'
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 82

```java
// constructor.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 85

```java
// ofShared.type() = ()Arena
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 88

```java
// Try to access shared Arena which might fail on GraalVM 25.0.0 if not enabled
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 89

```java
// See https://github.com/netty/netty/issues/15762
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 93

```java
// allocate.type() = (Arena,long)MemorySegment
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 95

```java
// asByteBuffer.type() = (MemorySegment)ByteBuffer
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 97

```java
// address.type() = (MemorySegment)long
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 99

```java
// bufClsCtor.type() = (AutoCloseable,ByteBuffer,long)CleanableDirectBufferImpl
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 102

```java
// The 'allocate' method takes a 'long' capacity, but we'll be providing an 'int'.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 103

```java
// Explicitly cast the 'long' to 'int' so we can use 'invokeExact'.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 104

```java
// allocateInt.type() = (Arena,int)MemorySegment
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 107

```java
// Use the 'asByteBuffer' and 'address' methods as a filter, to transform the constructor into a method
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 108

```java
// that takes two MemorySegment arguments instead of a ByteBuffer and a long argument.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 109

```java
// ctorArenaMemsegMemseg.type() = (Arena,MemorySegment,MemorySegment)CleanableDirectBufferImpl
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 113

```java
// Our method now takes two MemorySegment arguments, but we actually only want to pass one.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 114

```java
// Specifically, we want to get both the ByteBuffer and the memory address from the same MemorySegment
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 115

```java
// instance.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 116

```java
// We permute the argument array such that the first MemorySegment argument gest passed to both
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 117

```java
// parameters, and then the second parameter value gets ignored.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 118

```java
// ctorArenaMemsegNull.type() = (Arena,MemorySegment,MemorySegment)CleanableDirectBufferImpl
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 121

```java
// With the second MemorySegment argument ignored, we can statically bind it to 'null' to effectively
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 122

```java
// drop it from our parameter list.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 125

```java
// Use the 'allocateInt' method to transform the last MemorySegment argument of the constructor,
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 126

```java
// into an (Arena,int) argument pair.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 127

```java
// ctorArenaArenaInt.type() = (Arena,Arena,int)CleanableDirectBufferImpl
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 129

```java
// Our method now takes two Arena arguments, but we actually only want to pass one. Specifically, it's
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 130

```java
// very important that it's the same arena we use for both allocation and deallocation.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 131

```java
// We permute the argument array such that the first Arena argument gets passed to both parameters,
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 132

```java
// and the second parameter value gets ignored.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 133

```java
// ctorArenaNullInt.type() = (Arena,Arena,int)CleanableDirectBufferImpl
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 136

```java
// With the second Arena parameter value ignored, we can statically bind it to 'null' to effectively
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 137

```java
// drop it from our parameter list.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 138

```java
// ctorArenaInt.type() = (Arena,int)CleanableDirectBufferImpl
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 140

```java
// Now we just need to create our Arena instance. We fold the Arena parameter into the 'ofShared'
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 141

```java
// static method, so we effectively bind the argument to the result of calling that method.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 142

```java
// Since 'ofShared' takes no further parameters, we effectively eliminate the first parameter.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 143

```java
// This creates our method handle that takes an 'int' and returns a 'CleanableDirectBufferImpl'.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 144

```java
// ctorInt.type() = (int)CleanableDirectBufferImpl
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 169

```java
// The cast is needed for 'invokeExact' semantics.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 177

```java
// Propagate the runtime exceptions that the Arena would normally throw.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 191

```java
// Closing shared arenas can be fairly expensive if we do it a lot,
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 192

```java
// because it relies on inter-thread handshakes.
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 201

```java
// NOTE: must be at least package-protected to allow calls from the method handles!
```

Source: common/src/main/java/io/netty/util/internal/CleanerJava25.java, line 219

```java
// Propagate the runtime exceptions that Arena would normally throw.
```


### PlatformDependent.java

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 178

```java
// Here is how the system property is used:
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 179

```java
//
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 180

```java
// * <  0  - Don't use cleaner, and inherit max direct memory from java. In this case the
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 181

```java
//           "practical max direct memory" would be 2 * max memory as defined by the JDK.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 182

```java
// * == 0  - Use cleaner, Netty will not enforce max memory, and instead will defer to JDK.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 183

```java
// * >  0  - Don't use cleaner. This will limit Netty's total direct memory
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 184

```java
//           (note: that JDK's direct memory limit is independent of this).
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 187

```java
// Initialize the direct memory counter independently of Unsafe availability,
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 188

```java
// so that io.netty.maxDirectMemory is enforced even when Unsafe is not available (e.g. Java 25+).
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 208

```java
// only direct to method if we are not running on android.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 209

```java
// See https://github.com/netty/netty/issues/2604
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 211

```java
// Try Java 9 cleaner first, because it's based on Unsafe and can skip a few steps.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 215

```java
// On Java 24+ we'd like to not use Unsafe because it produces warnings. We have MemorySegment,
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 216

```java
// but we cannot use "shared" arenas due to JDK bugs.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 217

```java
// If the "linker" implementation is supported, then we have native access permissions
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 218

```java
// in the "io.netty.common" module, and we can link directly to malloc() and free() from libc.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 221

```java
// On Java 25+ we can't use Unsafe, but we have functioning MemorySegment support.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 222

```java
// We don't have native access permissions to link malloc() and free() directly, but we can
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 223

```java
// use shared memory segment instances.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 241

```java
// We should always prefer direct buffers by default if we can use a Cleaner to release direct buffers.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 250

```java
/*
         * We do not want to log this message if unsafe is explicitly disabled. Do not remove the explicit no unsafe
         * guard.
         */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 271

```java
//noinspection Since15
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 476

```java
/**
     * Returns {@code true} if the platform has reliable low-level direct buffer access API and a user has not specified
     * {@code -Dio.netty.noPreferDirect} option.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 484

```java
/**
     * Returns {@code true} if user has specified
     * {@code -Dio.netty.noPreferDirect=true} option.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 492

```java
/**
     * Return {@code true} if the selected cleaner can free direct buffers in a controlled way. This guarantee only
     * applies for buffers allocated via {@link #allocateDirect(int)} and when using the {@code clean} method of the
     * returned {@link CleanableDirectBuffer}.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 501

```java
/**
     * Returns the maximum memory reserved for direct buffer allocation.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 508

```java
/**
     * Returns the current memory reserved for direct buffer allocation.
     * This method returns -1 in case that a value is not available.
     *
     * @see #maxDirectMemory()
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 614

```java
/**
     * Allocate a direct {@link ByteBuffer} of the given capacity, and return it alongside its deallocation mechanism.
     * @param capacity The desired capacity of the direct byte buffer.
     * @return The {@link CleanableDirectBuffer} instance that contain the buffer and its deallocation mechanism.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 623

```java
/**
     * Allocate a direct {@link ByteBuffer} of the given capacity, and return it alongside its deallocation mechanism.
     * @param capacity The desired capacity of the direct byte buffer.
     * @param permitExpensiveClean Whether to allow expensive clean operations or not. If expensive clean operations
     * are not permitted ({@code false}), then the buffer cleaning may instead be delegated to the GC and reference
     * processing. Pooling allocators would typically permit expensive clean operations, while unpooled buffers
     * would not.
     * @return The {@link CleanableDirectBuffer} instance that contain the buffer and its deallocation mechanism.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 639

```java
/**
     * Reallocate a direct buffer with the given new capacity.
     * The old buffer is invalidated and must not be used after this call.
     *
     * @param buffer The old buffer to reallocate.
     * @param newCapacity The desired new capacity.
     * @return The new {@link CleanableDirectBuffer} with the given capacity.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 672

```java
/**
     * Obtain the native memory address of the given direct byte buffer, or throw an exception if it's not possible.
     * @param buffer The buffer to get the native memory address for.
     * @return The native memory address of the give buffer.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 1066

```java
// We don't have enough information to be able to align any buffers.
```


### PlatformDependent0.java

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 105

```java
// attempt to access field Unsafe#theUnsafe
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 111

```java
// We always want to try using Unsafe as the access still works on java9 as well and
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 112

```java
// we need it for out native-transports and many optimizations.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 117

```java
// the unsafe instance
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 122

```java
// Also catch NoClassDefFoundError in case someone uses for example OSGI and it made
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 123

```java
// Unsafe unloadable.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 129

```java
// the conditional check here can not be replaced with checking that maybeUnsafe
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 130

```java
// is an instanceof Unsafe and reversing the if and else blocks; this is because an
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 131

```java
// instanceof check against Unsafe will trigger a class load and we might not have
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 132

```java
// the runtime permission accessClassInPackage.sun.misc
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 146

```java
// ensure the unsafe supports all necessary methods to work around the mistake in the latest OpenJDK,
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 147

```java
// or that they haven't been removed by JEP 471.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 148

```java
// https://github.com/netty/netty/issues/1061
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 149

```java
// https://www.mail-archive.com/jdk6-dev@openjdk.java.net/msg00698.html
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 150

```java
// https://openjdk.org/jeps/471
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 157

```java
// Other methods like storeFence() and invokeCleaner() are tested for elsewhere.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 188

```java
// The following tests the methods are usable.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 189

```java
// Will throw UnsupportedOperationException if unsafe memory access is denied:
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 204

```java
// Unsafe.copyMemory(Object, long, Object, long, long) unavailable.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 218

```java
// attempt to access field Buffer#address
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 224

```java
// Use Unsafe to read value of the address field. This way it will not fail on JDK9+ which
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 225

```java
// will forbid changing the access level via reflection.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 229

```java
// if direct really is a direct buffer, address will be non-zero
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 252

```java
// If we cannot access the address of a direct buffer, there's no point of using unsafe.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 253

```java
// Let's just pretend unsafe is unavailable for overall simplicity.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 259

```java
// There are assumptions made where ever BYTE_ARRAY_BASE_OFFSET is used (equals, hashCodeAscii, and
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 260

```java
// primitive accessors) that arrayIndexScale == 1, and results are undefined if this is not the case.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 310

```java
// try to use the constructor now
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 344

```java
// using a known type to avoid loading new classes
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 360

```java
// Java9/10 use all lowercase and later versions all uppercase.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 362

```java
// On Java9 and later we try to directly access the field as we can do this without
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 363

```java
// adjust the accessible levels.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 372

```java
// ignore if can't access
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 382

```java
// There is something unexpected stored in the field,
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 383

```java
// let us fall-back and try to use a reflective method call as last resort.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 385

```java
// We did not find the field we expected, move on.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 406

```java
//noinspection DynamicRegexReplaceableByCompiledPattern
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 424

```java
// Java9 has jdk.internal.misc.Unsafe and not all methods are propagated to
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 425

```java
// sun.misc.Unsafe
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 552

```java
// We're recreating the following code snippet:
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 553

```java
// (long) MemorySegment.ofBuffer((Buffer) arg1).address();
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 682

```java
/**
     * Any value >= 0 should be considered as a valid max direct memory value.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 731

```java
// Calling malloc with capacity of 0 may return a null ptr or a memory address that can be used.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 732

```java
// Just use 1 to make it safe to use in all cases:
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 733

```java
// See: https://pubs.opengroup.org/onlinepubs/009695399/functions/malloc.html
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 832

```java
// MemorySegment.ofBuffer(buffer).address() includes the current position offset.
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 833

```java
// Netty/JNI GetDirectBufferAddress expects the base (index 0) address, so subtract position.
```


### PlatformDependentTest.java

Source: common/src/test/java/io/netty/util/internal/PlatformDependentTest.java, line 1

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
```

Source: common/src/test/java/io/netty/util/internal/PlatformDependentTest.java, line 155

```java
// byte[] and char[] need to be initialized such that there values are within valid "ascii" range
```

Source: common/src/test/java/io/netty/util/internal/PlatformDependentTest.java, line 186

```java
// Note: we're not testing on `PlatformDependent.directBufferPreferred()` because some builds
```

Source: common/src/test/java/io/netty/util/internal/PlatformDependentTest.java, line 187

```java
// might intentionally disable it, in order to exercise those code paths.
```
