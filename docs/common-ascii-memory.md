# AsciiString: CLR text and memory decisions

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `../netty`.
Scope of this checkpoint is representation, construction, views, cached text,
hash computation, and both original character/memory test files. AsciiString as
a whole remains **in progress**; the remaining API and parsing review is required.

## Required behavior and actual consumers

`common/.../AsciiString.java` stores bytes rather than UTF-16 characters. Its
`toString(int,int)` uses the old Java byte-widening String constructor: every
byte 0–255 becomes the corresponding character. `c2b(char)` preserves characters
0–255 and replaces each larger UTF-16 code unit with '?'. These are different
from ASCII decoding and from Unicode-to-Latin-1 best-fit encoding.

`codec-http/.../HttpHeaderNames.java` and `codec-http2/.../Http2Headers.java`
use cached byte strings for names. `codec-base/.../DefaultHeaders.java` and its
converters use heterogeneous character sequences, content checks and hashing.
`codec-base/.../CharSequenceValueConverter.java` calls numeric parsers. These
consumers justify retaining a byte-string representation and reviewing its
parsers separately; replacing the whole type with ordinary string would omit
byte views, sharing and byte-level access.

## CLR choices and repaired defects

| Requirement | CLR implementation and boundary |
| --- | --- |
| Lossless byte-to-text representation | Encoding.Latin1.GetString widens bytes 0–255. Previous ASCII decoding replaced all values above 127. All four initial representation regressions failed before the repair. |
| Native text input | string and ReadOnlySpan<char> constructors implement Netty's c2b mapping. A substring is expressed by a checked span range; no public Java StringBuilder/CharBuffer types are introduced. |
| Selected encoding | ReadOnlySpan<char>/string plus caller-provided Encoding produces exactly the selected range. Its CLR fallback policy is honored, with no automatically prepended preamble. Native UTF-8 reference bytes and strict fallback failures are tested. |
| ByteBuffer copy/view purpose | ReadOnlyMemory<byte> and Slice replace the three MemoryStream constructors. No production or test consumer of those stream constructors exists in the current project. Array-backed memory may share the exact segment; memory without accessible array storage is copied. |
| Native byte access | AsSpan/AsMemory expose only the logical byte range. Read-only access does not freeze backing arrays or transfer a pool lease. |
| Cache and input identity | Cached(string) preserves input reference identity for Latin-1-only strings, otherwise caches text reconstructed from mapped bytes. The old sequence-based factory incorrectly cached non-Latin-1 text inconsistent with its bytes. All six omitted pinned cached-string scenarios are restored. |
| Hashing | The existing eight-byte hash mixer was a NotImplementedException stub. Its original pure integer algorithm now uses explicit unchecked arithmetic; it requires no JVM Unsafe. |

The original memory constructors' three comments remain adjacent to the native
replacement and are followed by CLR adaptation explanations. The original
source comments are preserved, including those formerly edited to substitute
C# type names. One original nested-comparator comment belongs beside the split
CLR comparator implementation and is included in the source manifest paths.

## Ownership and usable native API

```csharp
byte[] packet = { 0, 0x68, 0xe9, 0xff, 0 };
ReadOnlyMemory<byte> payload = packet.AsMemory(1, 3);
AsciiString snapshot = new AsciiString(payload);          // owned copy
AsciiString shared = new AsciiString(payload, copy: false); // shared array view
string text = snapshot.ToString();                       // "héÿ"
ReadOnlyMemory<byte> encoded = shared.AsMemory();
AsciiString name = AsciiString.Cached("content-type");
```

The no-copy caller keeps backing storage valid, including any pool lease.
Mutation requires external synchronization and arrayChanged() to invalidate
cached text/hash. Returning an array to a pool while a shared string still uses
it is invalid. A read-only view is access policy, not a lifetime guarantee.
Custom/native memory without an exposed array is copied before its owner may be
disposed; the tests dispose a custom MemoryManager and verify independent bytes.

CharsetUtil is now replaced by native Encoding and caller-selected fallbacks;
the unused utility and thread-local codec caches are removed. Java UTF-16 emits
a BOM and detects byte order; CLR raw GetBytes/GetString require explicit framing
and byte order. Java UTF-8 malformed-input replacement is '?' versus CLR U+FFFD;
CLR single-byte '?' fallback replaces an unmappable surrogate pair twice versus
once in Java. Constructors retain supplied CLR Encoding behavior, without an
automatic preamble or silently ignored second error policy. Original encoding
tests still use six native configurations. See the native encoding decision and
all sixteen archived original comments in common-clr-design.md; pinned Java and
independent CLR oracles establish these differences. Future streaming codec and
protocol framing policies remain at their actual buffer/codec consumers.

## Test mapping and independent reference

All **42** pinned AsciiStringCharacterTest methods are present, including the
six cached-string tests that were absent in the old port. The **10** original
AsciiStringMemoryTest methods remain and use ordinary CLR counters for their
synchronous visitor loops. Original comments and assertions are preserved.
No original character or memory scenario is skipped.

StringBuilder input becomes native text at the constructor boundary. The
CharBuffer hashing case tests an offset character-sequence view using the
existing sequence adapter. A private test-only helper supplies heterogeneous
sequence inputs; it is not a new public Java conversion API. The remaining
ICharSequence public surface is still subject to the native API review.

Additional CLR verification is **4** representation cases, **13** memory cases,
and **1** independent hash test, for **70** affected cases total. The hash test
checks lengths 0–64, every tail size and eight-byte boundary, including high
bytes, nonzero offsets and cross-representation hash agreement. Its constants
were generated by executing exact method bodies extracted from the clean pinned
Java PlatformDependent/PlatformDependent0 sources with javac/java. Both endian
tables are retained; this host exercises the little-endian table. The temporary
oracle and its generator reside in the ignored TestResults directory.

## Remaining work

- Native comparison/search/sequence API decisions and migration of callers.
- Culture-independent numeric parsing, accepted lexical forms and overflow;
  Java floating-point suffixes/hex syntax must be considered before claiming equivalence.
- Regex match/split behavior and actual consumer requirements.
- Future protocol BOM/framing and operation-owned incremental encoding/decoding,
  applying the native Encoding decision in common-clr-design.md.
- Remaining raw PlatformDependent/public API work and integration of native
  owners with future pooled buffer consumers. Native owner/view contracts are
  implemented in common-native-memory.md; full default tests currently pass.

The default solution now builds in Debug and Release (existing warnings remain).
The whole default tests execute, replacing the previous 286-error compilation
gate with an executing full suite; later native-memory work resolves the failures. Final per-configuration results and current
manifest counts are recorded in [common-porting.md](common-porting.md).

CLR specifications: [Latin-1 encoding](https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.latin1?view=net-10.0),
[array extraction](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.memorymarshal.trygetarray?view=net-10.0),
[memory ownership and leases](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines).

## Later byte-word review

AsciiStringUtil directly uses bounded MemoryMarshal words/tails instead of JVM
unaligned/Unsafe probes. Twenty independent byte-case oracle cases cover all byte
values and lanes, offsets and tail lengths, with source immutability and unchanged
identity checks. All nine utility comments remain. See common-heap-memory.md.
The managed-byte checkpoint passed 1132 cases; the current ordered-multimap
whole Debug/Release checkpoint is 1161 passed / 0 failed / 14 skipped.
