# Netty common: CLR runtime and managed allocation

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `../netty`.
This is a partial platform review. Native allocation, address views, memory limits,
alignment, ownership and the public platform API remain unfinished.

## Runtime selection

The former C# `javaVersion()` returned the .NET runtime major version. Callers
then compared that number with JDK thresholds: Java 8/9 memory operations,
Graal reflection access and Java 25 Unsafe policy. These are unrelated version
domains. The extra parser also treated a Java regex split as a literal .NET
separator and tried to parse `.NET 10.0.7` as an integer.

Remove `javaVersion`, `dotnetVersion`, `majorVersion` and
`majorVersionFromDotNetSpecificationVersion`. CLR consumers can use the typed
`Environment.Version` directly if they need diagnostic runtime information;
no Netty wrapper or JDK-compatible parser is required. The declared net10.0
target provides Span equality without a JDK-version branch.

Android detection uses `OperatingSystem.IsAndroid()`, rather than accepting
`java.vm.name=Dalvik` as evidence of the actual OS. The explicit Netty Unsafe
preference remains diagnostic, but `sun.misc.unsafe.memory.access` and future
.NET version numbers do not select JDK Unsafe policies. `hasUnsafe()` still
reports that JVM Unsafe is unavailable, independently of that preference.
Graal native-image properties also do not enable reflective access. The explicit
Netty reflection preference still has its configured meaning; this change does
not certify the remaining reflection adapters as correct CLR APIs.

The two raw-memory copy stubs were subsequently retired in the bounded native
access checkpoint (common-clr-design.md). At this historical runtime checkpoint
they still threw NotImplementedException. Removing a
JDK-version branch does not implement memory access. Native-memory ownership
must be implemented before these operations can be claimed as ported.

## Original consumer review

In the pinned common, the runtime-version methods select JDK reflection,
Unsafe, array allocation and ByteBuffer operations. The actual requirements
must be replaced individually, rather than expressing a fictitious Java version.
Other original consumers include:

| Consumer | Original version-dependent purpose | CLR consequence |
| --- | --- | --- |
| `buffer/.../AdaptivePoolingAllocator.java` | NIO ByteBuffer slicing and absolute copying added in particular JDK releases | Use Memory/Span owner and slice contracts when buffer is ported; no JDK threshold. |
| `transport/.../channel/socket/nio/SelectorProviderUtil.java` and domain socket channels | Availability of JDK selector/socket APIs | Review .NET socket APIs and actual OS support during transport work. |
| `handler/.../ssl/SslHandler.java` and ALPN utilities | JDK SSLEngine, TLS and ALPN capabilities/workarounds | Review the selected .NET TLS backend and required behavior; .NET 10 is not Java 10. |
| `resolver-dns/.../DefaultDnsServerAddressStreamProvider.java` | JDK resolver access/fallback | Establish .NET/OS resolver behavior when that module is ported. |
| `codec-compression/.../ByteBufChecksum.java` | JDK checksum update APIs | Use the chosen CLR checksum implementation with slice and result verification. |

These are evidence for removing the version facade, not claims that downstream
modules or all associated capabilities have already been ported.

## Managed allocation and caller migration

`PlatformDependent.allocateUninitializedArray` and its private JVM reflection
helpers are removed. All seven actual common call sites now use
`GC.AllocateUninitializedArray<byte>` directly: five in AsciiString and two in
AsciiStringUtil. No factory facade or has-method reflection probe is needed.

The result's initial contents are unspecified. Construction, case conversion,
concatenation and replacement must write the entire logical result before it
becomes observable. Character/sequence constructors map every input code unit;
concatenation writes both regions; replacement copies the prefix and writes the
remainder; case conversion writes all output bytes. Failure during construction
does not publish the partially initialized array. No zero-content assumption,
pinned allocation, pooling or shared ownership is introduced.

The original `buffer/.../PoolArena.java`, `ByteBufUtil.java`,
`UnpooledUnsafeHeapByteBuf.java` and `Unpooled.java` also consume this allocation
facility. Their eventual C# implementations should use the standard allocator
and preserve initialization/ownership rules. For example ByteBufUtil.getBytes
writes the requested payload with getBytes before returning its allocated array.
There is no performance-equivalence claim; the CLR may still zero-initialize.

## Original tests and CLR regressions

PlatformDependent0Test retains the three address-view purposes using
NativeMemoryView. All three execute and pass; native owning allocation and the
provider test are mapped in [common-native-memory.md](common-native-memory.md).

| Original test | Decision |
| --- | --- |
| `testMajorVersionFromJavaSpecificationVersion` | JVM SecurityManager denies java.specification.version, requiring the Java 6 fallback. No CLR SecurityManager/property contract or production parser remains; excluded as a JVM-specific scenario. |
| `testMajorVersion` | Tests JDK legacy `1.6`/`1.7`/`1.8` and modern version syntax. No JDK parser is needed; excluded as a JVM-specific scenario, without inventing a .NET parser for FrameworkDescription. |
| `testNewDirectBufferNegativeMemoryAddress` | Unsigned address bits preserved for valid ranges; (-1, 10) wrap explicitly rejected, (-16, 10) accepted; no fake address dereferenced. |
| `testNewDirectBufferNonNegativeMemoryAddress` | Borrowed address/length metadata preserved; no fake address is dereferenced. |
| `testNewDirectBufferZeroMemoryAddress` | Null empty views are supported; CLR rejects null nonempty spans explicitly. |

The original PlatformRuntimeContractTest runtime selection has four cases: a spoofed Dalvik name, a Graal property,
a JDK Unsafe deny flag and an explicit Netty preference. Each initializes the
platform code in a fresh collectible AssemblyLoadContext, restores environment
variables in finally and runs in an exclusive collection. The expected OS is
obtained independently from OperatingSystem. Environment.Version itself needs
no Netty implementation test.

AsciiStringAllocationContractTest has 14 cases covering exact byte initialization
for character/sequence slices and case/concat/replace results, including large
arrays, high bytes, offsets and lengths around word and 1024-byte boundaries.
Tests do not read allocated bytes before initialization or assume nonzero garbage.
Together with the original 52 character/memory tests and three existing platform
contracts, the targeted Debug selection passes all 73 cases. Latest whole default
Debug/Release results are recorded in [common-porting.md](common-porting.md).
The historical runtime-only full runs had 1068 passed / 5 failed / 14 skipped
out of 1087. Native owners/views resolve those five failures, and a fifth runtime
case verifies the explicit shared allocator limit. The managed-byte checkpoint passed 1132 cases; the current ordered-multimap
checkpoint passes 1161 cases with zero failures and 14 skips in both configurations.
Managed field-offset and raw native-pointer stubs were subsequently retired after
consumer review; remaining native ordered/public API integration is pending.

An initial full Release run also exposed unrelated global logger mocking and
physical-worker thread-local contamination. The factory fixture is now exclusive
and provides real loggers for unrelated categories; the thread-local fixture
starts with an empty map. All assertions remain, and both final full runs pass
those previously failing cases. This does not establish an exhaustive review of
logging, thread-local public APIs or executor policies.

CLR specifications: [typed runtime version](https://learn.microsoft.com/en-us/dotnet/api/system.environment.version?view=net-10.0),
[managed allocation and unspecified contents](https://learn.microsoft.com/en-us/dotnet/api/system.gc.allocateuninitializedarray?view=net-10.0).

## Removed original comment provenance

The removed API and JVM-only test comments are preserved below with their pinned
source locations. Applicable comments remain beside the implementations.

### PlatformDependent.java

Source: common/src/main/java/io/netty/util/internal/PlatformDependent.java, line 430

```java
/**
     * Return the version of Java under which this library is used.
     */
```

### PlatformDependent0.java

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 81

```java
/**
     * Limits the number of bytes to copy per {@link Unsafe#copyMemory(long, long, long)} to allow safepoint polling
     * during a large copy.
     */
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 1256

```java
// Package-private for testing only
```

Source: common/src/main/java/io/netty/util/internal/PlatformDependent0.java, line 1261

```java
// Package-private for testing only
```

### PlatformDependent0Test.java

Source: common/src/test/java/io/netty/util/internal/PlatformDependent0Test.java, line 1

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

Source: common/src/test/java/io/netty/util/internal/PlatformDependent0Test.java, line 68

```java
// deny
```

Source: common/src/test/java/io/netty/util/internal/PlatformDependent0Test.java, line 73

```java
// so we can restore the security manager
```

Source: common/src/test/java/io/netty/util/internal/PlatformDependent0Test.java, line 91

```java
// early version of JDK 9 before Project Verona
```
