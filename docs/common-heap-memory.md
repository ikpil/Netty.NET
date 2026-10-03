# CLR byte storage and ASCII word operations

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `D:/workspace/netty`.
This review covers managed byte primitives and the AsciiString case-conversion
consumer. It does not complete every PlatformDependent method.

## Original requirements and consumers

- `common/.../AsciiStringUtil.java` scans and transforms native-order words with
  SWAR arithmetic. It copies the logical slice only when an ASCII letter changes;
  punctuation and all bytes above 127 remain unchanged. Source storage is unchanged.
- `buffer/.../UnsafeHeapSwappedByteBuf.java` reads/writes native-order integers;
  its enclosing swapped view determines whether the caller needs byte reversal.
- `buffer/.../ByteBufUtil.java` writes encoded bytes into array ranges.
- `buffer/.../PooledHeapByteBuf.java`, `UnpooledHeapByteBuf.java` and
  `HeapByteBufUtil.java` establish bounded byte-range copying and fill requirements.
  Their complete C# implementations remain outside this common stage.

The previous PlatformDependent array writes, copy and fill delegated to explicit
Unsafe stubs. AsciiStringUtil's word branch was disabled by an uninitialized
JVM-derived UNALIGNED flag, so successful scalar tests did not exercise that branch.

## Native design and API migration

The actual common case-conversion caller now uses bounded `MemoryMarshal.Read`
and `MemoryMarshal.Write` directly. CLR unaligned access does not depend on the
presence of sun.misc.Unsafe. Four redundant scalar fallback helpers are removed;
the original word/tail algorithm, allocation identity and all nine original
comments remain at their corresponding code. Short-word narrowing explicitly
uses Java-compatible wrapping arithmetic.

PlatformDependent's existing typed array entry points now use direct array
indexing or MemoryMarshal. Copy/fill use Span.CopyTo/Fill after null/range checks.
The eleven obsolete PlatformDependent0 array primitive stubs are removed.
No new facade or JVM object-header offset is required by these operations.

These existing PlatformDependent methods remain adapters while the broader
public API review proceeds. New C# byte consumers should use Span/Memory and
MemoryMarshal, or BinaryPrimitives when the data format specifies byte order.
Primitive operations here use **host native order**, not a claimed network order.
Raw pointer stubs are now retired after original consumer review in
common-clr-design.md: native owners/views expose bounded Memory and spans.
Ordinary word/copy/fill operations do not provide ordered publication.
Managed object-field-offset stubs are now removed after typed ref-int counter
migration; see common-clr-design.md CLR reference-count fields and JVM field access.

CLR ranges reject negative or out-of-storage offsets and lengths. Long sizes
must fit Int32 before slicing; overflow is rejected instead of truncating.
Destination validation precedes copying/filling so invalid ranges cannot partly
change the destination. Null arrays are rejected even for zero-length operations.
Valid zero-length end slices are accepted. Unlike JVM unchecked Unsafe accesses,
invalid managed byte ranges cannot access adjacent storage.
CopyTo supports overlapping source/destination ranges with snapshot-equivalent
results. This is an explicit bounded CLR contract, rather than an assertion that
every JVM Unsafe implementation provides the same overlap behavior.

## Verification

HeapMemoryContractTest has 12 executed cases: unaligned signed/high-bit integer
representations against BitConverter's independent native-order byte output,
overlap in both directions, equal ranges, zero-length end ranges, nonoverlap copy,
fill, no partial write on invalid bounds, null rejection and nontruncating indices.
Before repair **11 fail** at NotImplementedException and one typed-array case passes.

AsciiStringCaseConversionContractTest has 20 executed cases. A scalar oracle
independent of the production helpers verifies word/tail lengths, offsets 0-15,
all 256 repeated byte values and every byte lane with high-bit neighbors.
It checks result bytes, source immutability and same-instance reuse for unchanged
values. Together with existing allocation, original character/memory, platform
and native memory selection, the targeted Debug run passes 146 cases.

Evidence: `heap-memory-before.trx`, `heap-memory-targeted-debug.trx` and complete
default Debug/Release results in [common-porting.md](common-porting.md).
Tests establish correctness on Windows/net10.0. No throughput or allocation
performance comparison has been measured, and other OS/endian hosts are unverified.

CLR sources: [bounded structure reads](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.memorymarshal.read?view=net-10.0),
[bounded structure writes](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.memorymarshal.write?view=net-10.0),
[overlapping CopyTo](https://learn.microsoft.com/en-us/dotnet/api/system.span-1.copyto?view=net-10.0).
