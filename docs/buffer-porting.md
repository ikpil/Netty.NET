# Netty buffer port

Baseline: `64cc10f38ea5f5bd7eae48507817c66680d0afdc` in `../netty`.
Scope: buffer implementation/tests and common changes required by real buffer consumers.
The common backlog stays open; unrelated common API polishing is not a buffer prerequisite.

`buffer-porting-manifest.json` inventories every original Java source/test file.
Regenerate with `pwsh tools/Update-BufferPortingManifest.ps1`; existing decisions
are retained. Partial source/API and test coverage is explicitly in-progress.
No throwing placeholder API or artificial skipped test is used to represent
unported work. Use the existing common design records for actual shared decisions.

## First implementation unit: heap buffers and shared views

Add net10.0 Buffer and Buffer.Tests projects to the default solution.
ByteBuf combines the original ByteBuf/AbstractByteBuf index and data contracts;
AbstractReferenceCountedByteBuf reuses common's atomic ref-int counter. Heap
storage uses managed arrays. Primitive wire access uses BinaryPrimitives,
explicit BE/LE methods, 24-bit signed extension and bit-preserving float/double
conversion. Bulk copies use overlapping-safe spans. These CLR choices replace
NIO/Unsafe strategy dispatch; no separate Java provider hierarchy is added.

Get/set operations address capacity without moving indices. Sequential reads
require written bytes; writes grow with the original 64-byte/power-of-two and
4-MiB step policy. Clear changes indices only. Discard compacts readable bytes
and adjusts marks. Capacity changes preserve the prefix and trim indices;
allocation/copy succeeds before the replacement is published.

Slice and Duplicate have independent indices and shared storage/reference count.
Only retained variants acquire another lifetime claim. The private ByteBufView
uses fixed-capacity slices or a duplicate's current parent capacity. It checks
each actual parent range, including valid slice prefixes after parent shrinking.
Release delegates to the common counter and deallocates exactly once. GC and
IDisposable do not substitute for this shared ownership contract.

CLR bytes are unsigned octets; unsigned 16-/32-bit reads return ushort/uint.
Explicit integer setters truncate to wire width. Index/capacity bounds use
ArgumentOutOfRangeException; common ownership validation retains ArgumentException
and IllegalReferenceCountException. Memory/Span are borrowed views, not retain
tokens. Exclude final release/reallocation throughout borrowed access. Saved heap
memory describes the original array after replacement, rather than retargeting.

Original class and implemented-operation documentation is kept beside the native
implementation. AbstractReferenceCountedByteBuf preserves all its original
comments. Comments for still-unported operations remain upstream, with the source
entry in-progress; this checkpoint does not claim full ByteBuf API/comment coverage.

Tests translate all six original AbstractReferenceCountedByteBufTest scenarios
(the two resurrection variants share a theory), plus selected AbstractByteBufTest
index/capacity/discard/copy/derived-view scenarios. CLR tests cover common-interface
ownership, 50 competing releases, overlap, unsigned octets, integer overflow and
floating bit patterns. Ten word/endianness cases each retain the original 100
consistency iterations, using an independent byte-by-byte wire oracle.

The ignored Java probe compiles the exact pinned HeapByteBufUtil and
VarHandleByteBufferAccess. A probe-only PlatformDependent selects Java scalar or
standard VarHandle views. Both Java modes and the compiled native Buffer consumer
agree on all 1000 normalized wire rows (including poisoned neighboring bytes).
This is word-kernel evidence, not execution of the whole Java ByteBuf/allocator.

Validation on Windows/net10.0: Buffer.Tests passes 52/52 in Debug and checked
Release. Default whole-solution Debug and Release each execute Buffer 52 passed/
0 failed/0 skipped and unchanged Common 2676 passed/0 failed/14 existing skips.
Combined: 2742 discovered, 2728 passed, zero failed, 14 existing skips.
All 159 inventory paths match pinned Git objects (93 sources, 66 tests).
The full reference-count source's eight comments and original fixture's two
comments are preserved. Other source/test entries explicitly retain partial
coverage and pending methods. Evidence: buffer-foundation-*.trx in each test
project's ignored TestResults, and artifacts/buffer-foundation-validation.
The new projects add no source exclusion or synthetic skip. Common source and
its test configuration are unchanged. Existing common warnings remain.

## Native storage and the shared empty buffer

Unpooled.DirectBuffer and UnpooledDirectByteBuf connect actual Buffer operations
to common NativeMemoryAllocator/Owner. Allocation is explicitly native and zeroed;
a constructor can accept an independent reservation domain. Capacity replacement
allocates/copies before publication, preserves the prefix, trims indices and frees
the previous owner. Failure leaves the original memory and indices usable. The
budget covers the transient old/new allocations; no in-place realloc cost or
allocator performance equivalence is claimed. Copy preserves native storage and
has independent lifetime.

Retained ByteBuf views preserve logical ownership; MemoryHandle pins preserve
physical allocation. Final Release rejects new buffer access and invalidates new
native-memory access, while outstanding pins keep their original bytes and quota
alive until disposed. A saved Memory is invalid after native owner replacement;
an acquired pin remains bound to the previous allocation. This differs from a
managed array view, which GC can keep alive after replacement.

Growing WriteBytes(ReadOnlySpan<byte>) pins its current native allocation across
replacement. That covers a source span aliasing the old storage, including writes
through a duplicate. Otherwise disposing the previous owner could invalidate the
source span before copying. This protects self-aliasing growth; it does not acquire
ownership of an arbitrary external span or make data access thread-safe.

Unpooled.EmptyBuffer restores Netty's shared zero-capacity sentinel for empty
factories/reads/views. Its reference count is permanently one and Release returns
false. As in original EmptyByteBuf, ownership increments/decrements are ignored,
including nonpositive values. Capacity changes are unsupported even for zero.
Explicit native construction at zero capacity remains an owned, growable buffer
and uses common's documented one-byte physical allocation/accounting policy.

The selected heap index/capacity/compaction/derived-view scenarios now also run
against native memory. Additional cases check quota restoration, allocation failure,
independent native copies, retained ownership, old-allocation pins and aliased growth.
Buffer.Tests passes all 95 cases in Debug, Release and checked Release, with no
skips. Both CLR heap and native consumers match all 1000 pinned Java word rows.
Evidence: buffer-native-*.trx and artifacts/buffer-foundation-validation.
Focused solution Debug/Release integration runs each pass Buffer 95 and Common
101 native-memory/reference-count cases, with zero failures or skips.
The common source, tests and configuration remain unchanged from the preceding
full Debug/Release checkpoint; no repeated whole-common run is claimed here.

## Remaining work

The initial Unpooled factory covers heap allocation and a single wrapped/copied
byte range, native allocation and the shared empty sentinel. Allocator interfaces/metrics,
read-only/swapped/composite buffers, encoding/search/utilities, streams/native I/O,
borrowed-address wrapping, leak-aware wrappers and pooled/adaptive allocators remain unported. Most original
test classes and the rest of AbstractByteBufTest remain pending/in-progress.
Next units implement encoding/search/utilities, read-only/composite views and
broaden original tests, then real allocator/cache integration. Common
changes must cite the actual buffer contract that requires them.
