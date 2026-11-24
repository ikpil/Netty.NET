# codec-compression porting

Netty.NET ports `netty-codec-compression` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Compression`, with its original tests in
`test/Netty.NET.Handler.Codec.Compression.Tests`. The [manifest](codec-compression-porting-manifest.json) records
every pinned file of `codec-compression/src` (125 files: 72 main and 49 test Java sources, three JVM metadata files and
one binary test resource) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not ported.
Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-compression
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files or recorded as belonging to a member that is not ported, that every
declared JUnit test is mapped to a C# test of the same name (case may differ) or carries a recorded disposition, and
the facts and evidence of every unit that is not ported. This module adds the `library-not-ported` status (owner
decision 2026-11-23): such an entry records the library (name, Maven coordinates, Java packages), and the checker
requires the unit to import one of those packages or to be linked by code to a recorded unit of the same library that
does. A comment recorded as `library-not-ported` inside a ported file names the library type its member uses and
carries evidence. The binary test resource `multiple.gz` must equal its C# copy byte for byte. The checker fails while
any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 51 |
| `verified` (tests whose every original method is mapped, and the test resource) | 32 |
| `library-not-ported` | 36 |
| `jvm-only-replaced` | 4 |
| `deprecated-not-ported` | 2 |

Of the 186 declared original tests, 142 are mapped to C# tests with the same names and 44 belong to test classes of
`library-not-ported` units. No test is CLR-inapplicable. Nine comments of deprecated members and five comments of
members that need brotli4j or return the Zstd options are recorded instead of ported, and the 42 entries that are not
ported carry 292 original comments. The C# run executes 348 test cases, because the tests of the abstract base classes
run for every subclass and theories run once per data row: 330 pass and 18 are skipped (see Test notes).

## Owner decisions (2026-11-23)

- **Project.** The project, folder and assembly are `Netty.NET.Handler.Codec.Compression`, the namespace root of the
  module, as for every codec module (owner decision 2026-11-19, see [codec-base-porting.md](codec-base-porting.md)).
- **.NET 10 and the BCL zlib streams.** The solution stays on .NET 10. zlib, deflate and gzip run on the BCL
  `System.IO.Compression` streams behind internal stand-ins for `java.util.zip.Inflater`/`Deflater` (see
  [zlib, deflate and gzip](#zlib-deflate-and-gzip)). Netty keeps its own gzip header and trailer handling and feeds
  the deflate body to the BCL stream one byte per `Read`, so that the end of the deflate data, and with it the trailer,
  is exact; ZLIB and raw input are fed whole buffers. Measured on this host, whole buffers inflate at about 290-381 MB/s
  and one byte per `Read` at about 21-31 MB/s, so gzip decoding is about ten times slower than ZLIB or raw decoding.
  .NET 11 adds `DeflateDecoder`
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.deflatedecoder?view=net-11.0), net-11.0
  only), which reports the consumed bytes and could remove the one-byte feeding after a move to .NET 11.
- **Libraries without a BCL counterpart.** Units that need lz4-java, compress-lzf, lzma-java, jzlib or zstd-jni are
  `library-not-ported`; no third-party .NET library is added for them. The .NET 10 `System.IO.Compression` namespace
  has Brotli, Deflate, GZip and ZLib types but no LZ4, LZF or LZMA type and no zlib dictionary API
  ([System.IO.Compression](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression?view=net-10.0)).
  `ZstandardStream`/`ZstandardEncoder`/`ZstandardDecoder` exist only from .NET 11
  ([ZstandardStream](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandardstream); the API page
  lists net-11.0 only). ZlibCodecFactory, Snappy, FastLz, Bzip2 and Brotli remain available.
- **Brotli.** brotli4j 1.23.0 (a JNI binding) is replaced by the BCL `System.IO.Compression.BrotliEncoder` and
  `BrotliDecoder` structs, which exist since .NET Core 2.1
  ([BrotliEncoder](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder?view=net-10.0),
  [BrotliDecoder](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlidecoder?view=net-10.0)).
- **Checksums.** `java.util.zip.Checksum`, `CRC32` and `Adler32` are `System.IO.Hashing`'s
  `NonCryptographicHashAlgorithm`, `Crc32` and `Adler32`
  ([NonCryptographicHashAlgorithm](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm?view=net-10.0-pp)),
  from the `System.IO.Hashing` package 11.0.0 (Microsoft, MIT), the only package the main project references.
- **SharpZipLib (tests only).** The test project references SharpZipLib 1.4.2 as the independent bzip2 implementation
  for the cross-checks that the Java tests run against commons-compress.
- **`status()` and `builder()`.** `Decompressor.status()` is `Status_()`, and the static `builder()` of the five
  decompressors (Bzip2, Snappy, FastLz, JdkZlib, Brotli) is `Builder_()`: a method cannot be declared next to the
  nested `Status` enum or `Builder` class of the same name
  ([CS0102](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102)). The nested types keep their Java names, as
  `HAProxyTLV.type()` is `Type_()` ([codec-haproxy-porting.md](codec-haproxy-porting.md)). The Transport port met the
  same clash for `EmbeddedChannel.builder()` and replaced it with a public `Builder` constructor
  ([transport-porting.md](transport-porting.md)); here the package-private `Builder()` constructors stay `internal`,
  so `Builder_()` is the only public entry.

## Namespaces and assembly

The package files sit at the project root. The project references `Netty.NET.Handler.Codec` (codec-base) and
`System.IO.Hashing`, and `InternalsVisibleTo` opens internals to the test assembly only.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.compression` | `Netty.NET.Handler.Codec.Compression` | Encoder and decoder which compresses and decompresses `ByteBuf`s in a compression format such as zlib, gzip, and Snappy (the source links each format). |
| test `io.netty.handler.codec.compression` | `Netty.NET.Handler.Codec.Compression.Tests` | |

## Shared CLR adaptations

- **Access.** Package-private classes are `internal` (`internal sealed` for the final ones), and package-private
  members are `internal`; the tests reach them through `InternalsVisibleTo` as the Java tests reach them through
  package access. The package-private or private fields of a nested `Builder` that the enclosing class reads are
  `internal`, because "You can access private members only within the body of the class or the struct where you
  declare them" ([private](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/private)).
- **Types.** `CompressionOptions` is the empty marker interface `ICompressionOptions`. `ZlibWrapper` and `BrotliMode`
  are simple C# enums; as value types they cannot be null, so the `checkNotNull`/`requireNonNull` checks of their users
  have no counterpart. `CompressionException` and `DecompressionException` derive from codec-base's
  `EncoderException`/`DecoderException` with the four Java constructors; `serialVersionUID` has no counterpart. The
  anonymous `ByteProcessor`s are `Func<byte, bool>`, and static imports of `Snappy`/`FastLz` members are
  `using static`.
- **Exceptions and text.** `IllegalArgumentException` and `IllegalStateException` are `ArgumentException` and
  `InvalidOperationException` ([codec-base-porting.md](codec-base-porting.md)), and `AssertionError` is
  `UnreachableException` (the `java.lang.Error` mapping of [codec-memcache-porting.md](codec-memcache-porting.md)).
  Numbers in exception messages use the invariant culture.
- **Promises.** `ChannelFuture`/`ChannelPromise` are `Task` ([transport-porting.md](transport-porting.md)). Where a
  promise is completed from another Task, a `TaskCompletionSource` copies the outcome with `TrySetFromTask`, which
  copies "the completion status (including exception or cancellation information)"
  ([TrySetFromTask](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetfromtask?view=net-10.0);
  the PromiseNotifier mapping of [common-task-composition.md](common-task-composition.md)).
- **EncoderUtil.** `closeAfterFinishEncode(ctx, finishFuture, promise)` is
  `Task CloseAfterFinishEncode(IChannelHandlerContext ctx, Task finishFuture, CancellationToken cancellationToken)`;
  the promise is the returned Task, which copies the outcome of `ctx.CloseAsync`. The 10-second fallback is
  `ctx.Executor.ScheduleAsync` with the token of a `CancellationTokenSource` (`future.cancel(true)` is `Cancel()`). As
  in Java, `EmbeddedChannel.close()` cancels the remaining scheduled tasks; scratch tests exercised the immediate,
  after-finish and 10-second paths.
- **Encoder close.** `close()` and `close(ChannelPromise)` of `ZlibEncoder` (and `JdkZlibEncoder`) and `Bzip2Encoder`
  fold into one `Task CloseAsync(CancellationToken cancellationToken = default)` with both Javadocs before it, as
  `EmbeddedChannel` folds them ([transport-porting.md](transport-porting.md): "The `ChannelPromise` overloads fold into
  the Task-returning members"). Off the event loop a `TaskCompletionSource` stands for the promise, and
  `PromiseNotifier.cascade(f, promise)` is `TrySetFromTask`.
  - The outbound `close(ctx, promise)` is `CloseAsync(ctx, cancellationToken)`. The `finishEncode` write that used
    `ctx.newPromise()` gets `CancellationToken.None`, `EncoderUtil.CloseAfterFinishEncode` gets the close's token, and
    `finishEncode` on a finished encoder returns `Task.CompletedTask` (`promise.setSuccess()`).
  - `ZlibEncoder.CloseAsync(CancellationToken)` is an overload next to the inherited handler method
    `CloseAsync(IChannelHandlerContext, CancellationToken)`.
- **Switch fall-through and labeled break.** "control can't fall through from one switch section to the next", and "To
  imitate the fall-through behavior ... you can use the goto statement"
  ([selection statements](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements)):
  each `// fall through` of the bzip2 decoder, decompressor and encoder, `Bzip2HuffmanAllocator`, the Snappy/FastLz
  state machines and the gzip header readers is followed by `goto case`, and the last sections end with `break`
  ([codec-memcache-porting.md](codec-memcache-porting.md)). A labeled `break` (`break outer` in `Snappy.encode`,
  `break loop` in bzip2's `RECEIVE_HUFFMAN_LENGTH`) is a `goto` to a label on the first statement after the loop:
  labeled break exists only "Starting in C# 15", and "you can use the goto statement to exit a nested loop"
  ([jump statements](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/jump-statements)),
  as the Common port does (`ConcurrentSkipListIntObjMultimap.cs`).
- **Local scope.** A C# local is in scope in its whole block (for a switch, the whole switch block), while Java's scope
  starts at the declaration. Where Java writes a field by its simple name before a same-named local is declared
  (`blockDecompressor` and `huffmanStageDecoder` in the bzip2 decoders, `blockCompressor` in the encoder, `currentTable`
  in `Bzip2HuffmanStageDecoder.nextSymbol`), C# writes `this.<field>` ("Cannot use local variable 'name' before it is
  declared", [CS0841](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0841)). Sibling locals that clash are
  nested blocks or one local declared before them
  ([CS0136](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0136)): the `COMPRESSED_DATA` section of
  `SnappyFrameDecoder`/`SnappyFrameDecompressor` (two `checksum` locals), the `DECOMPRESS_DATA` section of
  `FastLzFrameDecoder` (locals that shadow fields assigned in earlier sections) and the two `nextByte` locals of
  `Bzip2BlockDecompressor.decodeHuffmanData`.
- **Integers.** An unsuffixed hexadecimal literal above `int.MaxValue` is a `uint` in C#
  ([integral types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types)),
  so the CRC tables keep the Java literals as `uint[]` and cast the looked-up entry to `int`, and int masks are
  `unchecked((int) 0x...)`: an unchecked conversion between types of the same size "is treated as a value of the
  destination type"
  ([numeric conversions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions)).
  Wrapping `int`/`long` arithmetic is unchecked, `>>>` is C#'s `>>>`, and `(int)` of a `long` keeps the low 32 bits.
  Java narrows a constant `int` to `char` implicitly; C# converts a constant `int` implicitly only to "sbyte, byte,
  short, ushort, uint, ulong, nint, or nuint", so `Bzip2MTFAndRLE2StageEncoder` writes `(char)` (same page).
- **Bit operations.** `Integer.numberOfLeadingZeros(x)`, which "Returns 32 if the specified value ... is equal to
  zero" ([Integer](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html)), is
  `BitOperations.LeadingZeroCount((uint) x)`, whose implementation also returns 32 for zero
  ([LeadingZeroCount](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.bitoperations.leadingzerocount?view=net-10.0),
  [BitOperations.cs](https://raw.githubusercontent.com/dotnet/dotnet/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/runtime/src/libraries/System.Private.CoreLib/src/System/Numerics/BitOperations.cs)).
  `Integer.bitCount` is `BitOperations.PopCount((uint) x)`
  ([PopCount](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.bitoperations.popcount?view=net-10.0)).
  `Integer.highestOneBit(v)` has no BCL counterpart and is
  `v & (int.MinValue >>> BitOperations.LeadingZeroCount((uint) v))` (0 for 0, because the int shift masks 32 to 0).
- **Arrays.** `Arrays.fill(a, from, to, v)` is `Array.Fill(a, v, from, to - from)`
  ([Array.Fill](https://learn.microsoft.com/en-us/dotnet/api/system.array.fill?view=net-10.0)). `System.arraycopy`,
  also the overlapping copy of `Bzip2MoveToFrontTable.indexToFront`, is `Array.Copy`, which "behaves as if the original
  values of sourceArray were preserved in a temporary location" when the ranges overlap
  ([Array.Copy](https://learn.microsoft.com/en-us/dotnet/api/system.array.copy?view=net-10.0),
  [System.arraycopy](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/System.html#arraycopy(java.lang.Object,int,java.lang.Object,int,int))).
  `Arrays.sort(int[])` is `Array.Sort(int[])`; equal ints are indistinguishable, so stability does not matter
  ([Arrays](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html),
  [Array.Sort](https://learn.microsoft.com/en-us/dotnet/api/system.array.sort?view=net-10.0)). `new int[a][b]`
  allocates every row in Java, while C# jagged arrays start with `null` rows ("You must initialize a jagged array's
  elements before you can use them",
  [arrays](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/arrays)), so
  `Bzip2HuffmanStageDecoder`/`Bzip2HuffmanStageEncoder` allocate the rows in loops.
- **Buffers.** `getUnsignedByte`/`readUnsignedByte` are `GetByte`/`ReadByte`, which are unsigned in the Buffer port
  ([buffer-porting.md](buffer-porting.md), Raw integer access and CLR octets); the places where the sign of a Java
  `byte` matters are listed under [Byte values](#byte-values). The Buffer port has no `hasArray()`/`array()`/
  `arrayOffset()`; borrowed memory (`AsReadOnlySequence`) and `MemoryMarshal.TryGetArray` replace them
  ([buffer-porting.md](buffer-porting.md), Array extraction). `java.nio.ByteBuffer` is `ReadOnlyMemory<byte>` (see
  `CompressionUtil` below).

## Core: checksums and decompressors

### Checksums

Public Netty APIs that take a `Checksum` take a `NonCryptographicHashAlgorithm`. The bzip2 family's Netty `Crc32`
lives in this namespace (and the test namespace is inside it), so code and tests write `System.IO.Hashing.Crc32` for
the BCL type.

- **ByteBufChecksum** is an `internal abstract class` deriving from `NonCryptographicHashAlgorithm`, so it can be
  passed wherever that type is expected. It keeps the call shape of `Checksum`:
  - `Update(int b)`, `Update(byte[] b, int off, int len)` and `GetValue()` (the Java `long`) are its abstract members;
    `reset()` is the base class's `public abstract void Reset()`
    ([Reset](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm.reset?view=net-10.0-pp)).
  - The base class constructor takes "The number of bytes produced from this hash algorithm"
    ([ctor](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm.-ctor?view=net-10.0-pp)),
    so the constructor is `protected ByteBufChecksum(int hashLengthInBytes)`. Each subclass implements the abstract
    `GetCurrentHashCore(Span<byte>)`, which "must write exactly HashLengthInBytes bytes"
    ([page](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm.getcurrenthashcore?view=net-10.0-pp)).
  - `Append(ReadOnlySpan<byte>)`
    ([Append](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm.append?view=net-10.0-pp))
    is `Checksum`'s default `update(ByteBuffer)`: for a buffer "not backed by an accessible byte array" it copies at
    most 4096 bytes at a time into a temporary array and calls `update(b, 0, length)`
    ([Checksum](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Checksum.html)).
  - `Update(ByteBuf b, int off, int len)` (virtual) reads the range with `AsReadOnlySequence(off, len)`. One segment
    backed by a managed array takes the array branch (the segment offset is `arrayOffset() + off`); otherwise the bytes
    are visited with `ForEachByte`. A range in one array of a composite buffer takes the array branch where Java's
    `hasArray()` is false; both branches give the same checksum.
  - `WrapChecksum(NonCryptographicHashAlgorithm)` (internal static) returns a `ByteBufChecksum` unchanged and wraps
    anything else in the private `JdkByteBufChecksum` (Java name kept). `Update(int)` appends the low eight bits as a
    one-byte span, `Update(byte[], int, int)` appends the array range, and the CRC32/Adler32 branches of
    `Update(ByteBuf, int, int)` append the span of `CompressionUtil.SafeNioBuffer`. `getSafeBuffer`'s copy into
    `scratchBuffer` on Java 22 to 24 is a JVM-only work-around and is not ported; its comment linking JDK-8357145 is
    kept (the JDK bug pages returned HTTP 403 to the fetch, so the issue itself was not read).
  - A wrapper's `GetValue()` reads `GetCurrentHashAsUInt32()`, which "Gets the current computed hash value without
    modifying accumulated state", for `Crc32`, `Adler32` and `XxHash32`
    ([Crc32](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.crc32.getcurrenthashasuint32?view=net-10.0-pp),
    [Adler32](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32.getcurrenthashasuint32?view=net-10.0-pp),
    [XxHash32](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.xxhash32.getcurrenthashasuint32?view=net-10.0-pp)).
    Any other `NonCryptographicHashAlgorithm` can be wrapped, updated and reset, but its `GetValue()` throws
    `NotSupportedException`: the base class exposes the hash only as bytes, in a byte order each type defines.
  - As a `NonCryptographicHashAlgorithm`, a wrapper reports the wrapped `HashLengthInBytes` and writes the wrapped
    `GetCurrentHash` bytes. Crc32 "emits the answer in the Little Endian byte order"
    ([Crc32](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.crc32?view=net-10.0-pp)), XxHash32 writes
    "Big Endian" ([XxHash32](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.xxhash32?view=net-10.0-pp)),
    and Adler32 writes big endian (`BinaryPrimitives.WriteUInt32BigEndian` in its
    [source](https://raw.githubusercontent.com/dotnet/dotnet/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/runtime/src/libraries/System.IO.Hashing/src/System/IO/Hashing/Adler32.cs);
    the [Adler32 page](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32?view=net-10.0-pp) has no
    remarks on it).
- **Crc32c** (Netty's own CRC-32C, used by Snappy) keeps its algorithm, table and members and extends `ByteBufChecksum`
  (four bytes, internal constructor). The table keeps the Java literals as `uint[]` and casts the looked-up entry back
  to `int`. `crc32c(int, int)` is `Crc32c_(int, int)`: "member names cannot be the same as their enclosing type"
  ([CS0542](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0542)). `GetCurrentHashCore` writes `getValue()`
  little endian, as `Crc32` does. A scratch test confirmed that the value and the bytes equal `System.IO.Hashing.Crc32`
  with `Crc32ParameterSet.Crc32C`
  ([Crc32ParameterSet](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.crc32parameterset?view=net-10.0-pp),
  new in 11.0.0) and the CRC-32C check value `0xE3069283` of `"123456789"`. The owner kept Netty's class.

### Decompressors

- **IDecompressor.** `Decompressor extends AutoCloseable`, and callers use it in try-with-resources, which calls
  `close()` "automatically when exiting a try-with-resources block"
  ([AutoCloseable](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/AutoCloseable.html)). The
  interface keeps `Close()` as the Java member (as Transport's `IIoHandle` keeps `close()` for AutoCloseable), extends
  `IDisposable` and implements `IDisposable.Dispose()` as a call of `Close()` with the explicit implementation syntax
  ([interface](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface)). A `using`
  statement, which disposes "the acquired IDisposable instance" when control leaves the block
  ([using](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/using)), is therefore the
  counterpart of try-with-resources. Implementations implement `Close()` only.
- The nested `Status` enum and `AbstractDecompressorBuilder` stay nested in the interface. Implementing classes write
  `IDecompressor.Status` and `IDecompressor.AbstractDecompressorBuilder`, because a class does not inherit the nested
  types of its interfaces.
- **InputBufferingDecompressor** (package-private in Java) is `public abstract` with an `internal` constructor, because
  its subclasses `JdkZlibDecompressor` and `SnappyFrameDecompressor` are public and a C# class cannot be more
  accessible than its base. The package-private `processInput`/`processOutput`/`available()` are `internal`,
  `Status_()`/`EndOfInput()` are `public abstract`, and `Close()` is virtual.
- **DefensiveDecompressor** is `internal sealed`; `IllegalStateException` is `InvalidOperationException`, and a null
  status prints as `null`.
- **CompressionUtil** (internal sealed): `DEFAULT_MAX_FORWARD_BYTES` reads `io.netty.compression.defaultMaxForwardBytes`
  with the Common `SystemPropertyUtil`. `SafeNioBuffer` borrows a single-segment range (`AsReadOnlySequence`, the
  Buffer port's `nioBuffers(int, int)`) and copies a range spanning several components with `ByteBufUtil.GetBytes`, as
  `nioBuffer(int, int)` merges it (the mapping of codec-mqtt's `MqttDecoder.readStrictUtf8`).

## Snappy and FastLz

- **Checksums.** `FastLzFrameEncoder(int, Checksum)`, `FastLzFrameDecoder(Checksum)` and
  `FastLzFrameDecompressor.Builder.checksum(Checksum)` take a `NonCryptographicHashAlgorithm`; the `boolean`
  constructors and `defaultChecksum()` pass `new System.IO.Hashing.Adler32()`
  ([Adler32](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32?view=net-10.0-pp); Java
  [Adler32](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Adler32.html)). The checksum is
  wrapped with `ByteBufChecksum.WrapChecksum` and used through `Reset()`/`Update(ByteBuf, int, int)`/`GetValue()`.
  Snappy uses `Crc32c`.
- **Decompressors.** `SnappyFrameDecompressor` and `FastLzFrameDecompressor` have `Status_()` and `Builder_()`; the
  nested `Builder`'s package-private `validateChecksums` and private `checksum` fields are `internal`, because the Java
  constructors read `builder.validateChecksums`/`builder.checksum`. `AssertionError` in
  `FastLzFrameDecompressor.status()` is `UnreachableException`.
- **Arithmetic.**
  - `FastLz.decompress`'s `ref -= ofs` relies on the implied `(int)` cast of a compound assignment
    ([JLS 15.26.2](https://docs.oracle.com/javase/specs/jls/se17/html/jls-15.html#jls-15.26.2)); C# rejects
    `int -= long` (CS0266, verified with the scratch build), so it is `@ref = (int) (@ref - ofs)`, with the same low 32
    bits.
  - `Snappy.encode`'s preamble mask `0xFFFFFF80` is `unchecked((int) 0xFFFFFF80)`. `Snappy.maskChecksum` adds
    `unchecked((int) 0xa282ead8)`, the same sign-extended `long` addend as Java, and narrows with `(int)`.
  - The hash table keeps `(short) (inIndex - baseIndex)` and `table[h] & 0xffff`, and `Snappy.hash`
    (`getInt(i) * 0x1e35a7bd >>> shift`) wraps as in Java.
  - `Snappy.validateChecksum`'s `Integer.toHexString(int)` is `ToString("x", InvariantCulture)`, which prints a
    negative int in two's complement ([codec-haproxy-porting.md](codec-haproxy-porting.md)).
- `@SuppressWarnings("IdentityBinaryExpression")` on `FastLz.compress` silences an IDE inspection and has no C#
  counterpart (the compiler reports nothing).
- The codecs go through `ByteBuf.GetByte`/`SetByte`/`GetInt` per byte or word as the Java code does; no CLR-specific
  speed-up was added.
- The deprecated `SnappyFramedDecoder`/`SnappyFramedEncoder` are not ported (see [Not ported](#not-ported)).

## Bzip2

Every bzip2 unit is ported, and every original comment sits at its code.

- **Names.** `Bzip2Decoder`, `Bzip2Encoder` and `Bzip2Decompressor` are public; the other classes are `internal`.
  Package-private fields keep their Java names as `internal` fields, and methods are PascalCase (`readBits` is
  `ReadBits`, `rNums` is `RNums`, `bwt` is `Bwt`; `BUCKET_B`/`BUCKET_BSTAR` keep their names). Accessors next to a
  same-named field differ only in case (`crc`/`Crc()`, `mtfBlock`/`MtfBlock()`, `ctx`/`Ctx()`).
- **Netty's `Crc32`** (bzip2's CRC) keeps its name and algorithm (owner decision 2026-11-23); in this namespace the BCL
  type is always `System.IO.Hashing.Crc32`. Its table is a `uint[]` with the Java literals, as in `Crc32c`, and
  `trLog`'s masks in `Bzip2DivSufSort` are `unchecked((int) 0xffff0000)`/`unchecked((int) 0xff000000)`.
- **Bzip2Decompressor** has `Status_()` and `Builder_()`; the Builder's private `outputBufferSize` field is `internal`.
- **Bzip2Encoder** folds `close()`/`close(ChannelPromise)` into `CloseAsync` (see Shared CLR adaptations). The anonymous
  `ByteProcessor` of `Bzip2BlockCompressor` is a `Func<byte, bool>` created in the constructor (as in
  `ByteBufChecksum`).
- **Bzip2BitReader.refill()** keeps the octet of `ReadByte()` in a `byte` local, because or-ing an `int` into the
  `long` bit buffer raises
  [CS0675](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0675) ("Bitwise-or
  operator used on a sign-extended operand"); the value is the same. Java `char[]` (the MTF block) is C# `char[]`; both
  are unsigned 16-bit.
- **Differential check.** A scratch run (not in the repository) compiled the pinned Java bzip2 sources with javac 21
  against Netty's built classes and encoded 21 inputs (empty, 1-2 bytes, random 500 B-1 MB, 2 MB of zeros, runs of
  1-1000 bytes, periodic, Fibonacci word, text, small alphabet, the `testHugeDecompress` pattern) with `Bzip2Encoder(1)`
  and `Bzip2Encoder(9)` in Java and C#: all 42 outputs were byte-identical, and the C# `Bzip2Decoder` restored every
  input from the Java outputs. A normalized token diff of every main file against Java shows only the adaptations
  listed here (`Bzip2DivSufSort` differs only in the two `trLog` casts).

## zlib, deflate and gzip

`java.util.zip.Inflater` and `Deflater` are two internal stand-ins named after the JDK types (`Inflater.cs` and
`Deflater.cs`, which have no Java file and are listed with the JdkZlibDecoder/JdkZlibDecompressor and JdkZlibEncoder
entries), so JdkZlibDecoder, JdkZlibDecompressor and JdkZlibEncoder keep the Java call shape
([Inflater](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Inflater.html),
[Deflater](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Deflater.html)).
`DataFormatException` is the `InvalidDataException` the BCL streams throw for corrupt data
([page](https://learn.microsoft.com/en-us/dotnet/api/system.io.invaliddataexception?view=net-10.0); zlib's
`Z_DATA_ERROR` in
[Inflater.cs](https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/Inflater.cs)).
The BCL sources were read at tag v10.0.8, the runtime installed on this host.

### Inflater

The stand-in reads a raw `DeflateStream` (nowrap) or a
[ZLibStream](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibstream?view=net-10.0) from a
private feed stream that `SetInput` fills.

- `DeflateStream.ReadCore`
  ([DeflateStream.cs](https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/DeflateStream.cs))
  reads its source only when its zlib stream needs input, into an 8192-byte buffer. Fed whole buffers, it reads past
  the end of the deflate data, so `getRemaining()` is unknown after the end; fed one byte per `Read`, it takes exactly
  the deflate data.
- A finished stream returns 0 without reading its source; a stream that needs input returns 0 after one source read
  that returned 0. `inflate(b, off, len)` reads until `len` bytes are produced or a `Read` returns 0, as zlib's
  `inflate()` fills the output from the available input.
- When the output buffer was filled, Java's zlib may already have reached the end in the same call (it decodes the
  end-of-block code and the trailer without output space). The stand-in reads one byte ahead to learn it and returns
  that byte from the next `inflate()`. `testMaxAllocationWithIncompressibleInputInOneRead(1024)` depends on this: the
  output exactly fills the 1024-byte limit.
- **Feeding (owner decision).** The GZIP wrapper, whose header and trailer Netty parses itself while it inflates the
  body raw, uses `new Inflater(true, true)` (one byte per `Read`), so `getRemaining()` is exact at the end of each
  member. ZLIB, NONE and ZLIB_OR_NONE feed whole buffers, where the bytes after the end are not used. Measured through
  the stand-in (Release, 8 MB of output), whole buffers inflate at about 290-360 MB/s and one byte per `Read` at about
  28-31 MB/s (the owner's own probe: 381 and 21 MB/s), so GZIP decoding in JdkZlibDecoder/JdkZlibDecompressor is about
  ten times slower than ZLIB/NONE decoding. .NET 11's
  `DeflateDecoder.Decompress(ReadOnlySpan<byte>, Span<byte>, out int, out int)` reports the consumed bytes (see Owner
  decisions).
- **After the end of a ZLIB/NONE stream** the bytes the BCL stream read past the end count as consumed. JdkZlibDecoder
  skips them, where Java leaves them in the cumulation, which ByteToMessageDecoder forwards when the handler is removed;
  JdkZlibDecompressor's `consumeInput()` advances past them. The decoder or decompressor is finished then; no original
  test observes it.
- **Preset dictionaries.** The BCL streams have no dictionary API, and ZLibStream reports zlib's `Z_NEED_DICT` as an
  unexpected `ZLibException`. The feed hands the two zlib header bytes to the ZLibStream on their own and checks the
  FDICT flag of a valid header ([RFC 1950](https://www.rfc-editor.org/rfc/rfc1950), 2.2).
  - `needsDictionary()` becomes true once the header is seen; zlib would first read the four DICTID bytes too.
  - Without a configured dictionary, Netty's `DecompressionException("decompression failure, unable to set dictionary
    as non was specified")` is thrown as in Java.
  - With one, `setDictionary` throws `NotSupportedException`. JdkZlibDecoder surfaces it as a `DecoderException`
    (ByteToMessageDecoder wraps exceptions that are not DecoderExceptions); JdkZlibDecompressor throws it unwrapped.
  - The constructors and builder methods that take a dictionary stay (public Java API). Streams without FDICT decode
    normally with a dictionary configured.
- **Strict validation.** The `System.IO.Compression.UseStrictValidation` AppContext switch (off by default,
  `s_useStrictValidation` in DeflateStream.cs) makes DeflateStream throw `InvalidDataException("Found truncated data
  while decoding.")` when its source returns 0 before the end. The stand-in treats that exception, thrown right after
  an empty feed read, as "needs input". A scratch probe passed with the switch on and off.

### Deflater

The stand-in writes through a raw `DeflateStream` or a `ZLibStream` into a private sink.

- The level is `ZLibCompressionOptions.CompressionLevel`, which "Can accept any value between -1 and 9 (inclusive). 0
  gives no compression, 1 gives best speed, 9 gives best compression, and -1 requests the default compression level,
  which is equivalent to 6"; the page applies to .NET 9, 10 and 11
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibcompressionoptions.compressionlevel?view=net-10.0)).
- `deflate(..., SYNC_FLUSH)` is the stream's `Flush()`. The
  [Flush page](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.deflatestream.flush?view=net-10.0)
  only says it "flushes the internal buffer to the underlying stream"; the source shows `ZFlushCode.SyncFlush`
  (`FlushBuffers` in DeflateStream.cs and `Deflater.Flush` in
  [Deflater.cs](https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/DeflateZLib/Deflater.cs)).
- `finish()` makes the next `deflate()` dispose the stream (leaveOpen). A BCL stream that never received a byte writes
  nothing when it is disposed (`PurgeBuffers`: "make sure to only flush when we actually had some input"), while
  java.util.zip writes a complete empty stream, so the stand-in writes it itself: the final fixed-Huffman block holding
  only the end-of-block code, `03 00` ([RFC 1951](https://www.rfc-editor.org/rfc/rfc1951) 3.2.3 and 3.2.6), and for
  ZLIB the header zlib writes (CMF 0x78, FLEVEL from the level, `header += 31 - (header % 31)`,
  [zlib deflate.c](https://raw.githubusercontent.com/madler/zlib/v1.3.1/deflate.c)) and the Adler-32 of no data,
  `00 00 00 01`, giving `78 01`/`78 5E`/`78 9C`/`78 DA` + `03 00 00 00 00 01`. `testCompressNone` (ZLIB, NONE, GZIP)
  and `testGZIPCompressOnly(null)` need it.
- Input is written in chunks of at most 64 KiB while the sink is empty, so the sink holds about one chunk of output.
  `getTotalIn()` counts the written bytes.
- `setDictionary` throws `NotSupportedException`, so `JdkZlibEncoder(byte[])`, `JdkZlibEncoder(int, byte[])` and
  `ZlibCodecFactory.NewZlibEncoder(byte[])`/`(int, byte[])` throw `NotSupportedException`.
- Compressed bytes can differ from Java's zlib (.NET uses zlib-ng). No test of this family compares compressed bytes;
  all compare inflated data.

### Codecs

- `java.util.zip.CRC32` is `System.IO.Hashing.Crc32`. In JdkZlibEncoder `update(b, off, len)` is `Append` and
  `(int) getValue()` is `unchecked((int) GetCurrentHashAsUInt32())`
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.crc32.getcurrenthashasuint32?view=net-10.0-pp));
  the decoders use `ByteBufChecksum.WrapChecksum(new System.IO.Hashing.Crc32())`.
- `ZlibEncoder` and `ZlibDecoder` are public abstract classes over codec-base's `MessageToByteEncoder<ByteBuf>`
  (`super(ByteBuf.class, false)` is `base(typeof(ByteBuf), false)`) and `ByteToMessageDecoder`; the overridable
  `prepareDecompressBuffer` and `decompressionBufferExhausted` are virtual.
- `JdkZlibDecompressor` has `Status_()` and `Builder_()`; the Builder constructor and the fields the outer class reads
  are `internal`.
- `(int) Math.ceil(...)` in `allocateBuffer` saturates in Java and, since .NET 9, in C#
  ([page](https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/9.0/fp-to-integer)).
- **ZlibCodecFactory.** jzlib is not ported, so the `Class.forName` probe always takes its ClassNotFoundException
  branch and `isSupportingWindowSizeAndMemLevel()` returns false. The branches that would create a JZlib codec
  (`io.netty.noJdkZlibEncoder`/`noJdkZlibDecoder` set, or a window size or memory level other than 15/8) throw
  `NotSupportedException`, where a JVM without jzlib fails with `NoClassDefFoundError`.
- **GzipOptions/DeflateOptions** keep `windowBits`/`memLevel` and their ranges. The BCL streams use their own window
  and memory settings, `ZLibNative.Deflate_DefaultWindowBits` (15) and `Deflate_DefaultMemLevel` (8)
  ([ZLibNative.cs](https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/Common/src/System/IO/Compression/ZLibNative.cs)),
  which are the values ZlibCodecFactory routes to JdkZlibEncoder. `GzipOptions.DEFAULT` carries the `new` modifier
  ([page](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier)).
- The deprecated JdkZlibDecoder constructors and `ZlibCodecFactory.newZlibDecoder` overloads are not ported (see
  [Not ported](#not-ported)).

## Brotli

The BCL `BrotliEncoder`/`BrotliDecoder` have the same simple names as the Netty classes, so code writes them with their
namespace.

- **Brotli** (the brotli4j availability probe). The static initializer looks up `Brotli4jLoader` on the class path and
  asks it why the native library is missing; it is not ported, and neither are its logger or the `CNFE`/`cause`
  fields. `IsAvailable()` returns true, `EnsureAvailability()` does nothing and `Cause()` returns null, each with a CLR
  comment; the Javadoc that describes the brotli4j probe is kept verbatim.
- **BrotliOptions.** brotli4j's `Encoder.Parameters` has no counterpart. The options hold `quality`, `window` and
  `mode` as internal readonly fields, and the package-private `BrotliOptions(Encoder.Parameters)` is
  `internal BrotliOptions(int quality, int window, BrotliMode mode)`. `DEFAULT` is (4, 22, TEXT): Java's default
  parameters leave `lgwin` at -1, which brotli4j does not pass to the native encoder (`encoder_jni.cc` sets
  `BROTLI_PARAM_LGWIN` only when `lgwin >= 0`,
  [encoder_jni.cc](https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/natives/src/main/cpp/encoder_jni.cc)),
  so brotli uses `BROTLI_DEFAULT_WINDOW` 22
  ([encode.h](https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/brotli/include/brotli/encode.h)). The BCL
  constructor takes the window explicitly: quality "0 is the minimum (no compression), 11 is the maximum", window "The
  minimum value is 10, and the maximum value is 24"
  ([ctor](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder.-ctor?view=net-10.0)). The
  never-firing `Brotli.isAvailable()` check is kept.
- **BrotliMode** is a simple C# enum; `adapt()`, which returns brotli4j's `Encoder.Mode`, is not ported. The BCL
  encoder has no mode: its only constructor is `BrotliEncoder(int quality, int window)`, and its streaming encoder
  never sets `BROTLI_PARAM_MODE`, which stays at `BROTLI_MODE_GENERIC`
  ([BCL source](https://raw.githubusercontent.com/dotnet/dotnet/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/runtime/src/libraries/System.IO.Compression.Brotli/src/System/IO/Compression/enc/BrotliEncoder.cs)).
  The mode is a hint ("Tune encoder for specific input", encode.h), so TEXT and FONT give a valid stream that may
  differ in size from brotli4j's. BrotliOptions keeps the mode, but the encoder does not use it.
- **StandardCompressionOptions** is sealed with its private constructor. `brotli()` returns `BrotliOptions.DEFAULT`
  (an `internal static readonly` field); its Javadoc, which links `Encoder.Parameters#setQuality(int)`/
  `Encoder.Mode#TEXT`, is kept verbatim. `brotli(int quality, int window, BrotliMode mode)` keeps
  `checkInRange(quality, 0, 11, "quality")` and `checkInRange(window, 10, 24, "window")` and returns
  `new BrotliOptions(quality, window, mode)`; `mode.adapt()` and `checkNotNull(mode, "mode")` have no counterpart.
  `snappy()`, `gzip()` and `deflate()` with their overloads are unchanged. The deprecated `brotli(Encoder.Parameters)`
  and `zstd()`/`zstd(int, int, int)` are not ported (see [Not ported](#not-ported)).
- **BrotliEncoder.**
  - `BrotliEncoder(Encoder.Parameters parameters, boolean isSharable)` is `BrotliEncoder(BrotliOptions parameters,
    bool isSharable)`, with the Java parameter name and Javadoc, so that a non-sharable encoder can still be created.
    `BrotliEncoder(Encoder.Parameters)` is not ported: mapped, it would be `BrotliEncoder(BrotliOptions)`, which exists.
  - The brotli4j channel calls map to the BCL calls below. Each is repeated while it returns `DestinationTooSmall`
    ([OperationStatus](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.operationstatus?view=net-10.0)), and
    `InvalidData` throws `IOException("encoding failed")`, as brotli4j does.

    | brotli4j `BrotliEncoderChannel` | BCL `BrotliEncoder` |
    | --- | --- |
    | `write(nioBuffer)` | `Compress(src, outputBuffer, ..., isFinalBlock: false)` ([Compress](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder.compress?view=net-10.0)) |
    | `flush()` | `Flush(outputBuffer, ...)`, which "ensures that output is produced for all the processed input" ([Flush](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotliencoder.flush?view=net-10.0)) |
    | `close()` | `Compress(empty, ..., isFinalBlock: true)`, then `Dispose()` in a finally block, as brotli4j's `Encoder.close()` runs `encode(FINISH)` and destroys the native encoder ([Encoder.java](https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/brotli4j/src/main/java/com/aayushatharva/brotli4j/encoder/Encoder.java)) |

  - Each compressed chunk goes through `Writer.Write(ReadOnlySpan<byte>)`, as brotli4j's `pushOutput` hands output to
    the destination channel. `outputBuffer` is 16384 bytes, brotli4j's `BrotliEncoderChannel.DEFAULT_BUFFER_SIZE`.
    `WritableByteChannel` has no BCL counterpart, so `Writer` keeps `write`/`isOpen`/`close` without the interface.
  - `ctx.newPromise()` is a `TaskCompletionSource` and `setFailure` is `SetException`. `writeAndFlush(buf, promise)`
    copies the outcome of `WriteAndFlushAsync` into the promise with `TrySetFromTask`. `close(ctx, promise)` is
    `CloseAsync` returning `EncoderUtil.CloseAfterFinishEncode`.
  - `@Sharable` is `[Sharable]`, and `isSharable()` overrides the Transport `IsSharable` property, as codec-base's
    `DatagramPacketDecoder` does.
- **BrotliDecoder and BrotliDecompressor.**
  - brotli4j's `DecoderJNI.Wrapper` keeps an input buffer that `push(length)` decodes, keeps the decoded output in the
    native decoder until `pull()` takes it, and reports ERROR/DONE/NEEDS_MORE_INPUT/NEEDS_MORE_OUTPUT/OK
    ([DecoderJNI.java](https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/brotli4j/src/main/java/com/aayushatharva/brotli4j/decoder/DecoderJNI.java),
    [decoder_jni.cc](https://raw.githubusercontent.com/hyperxpro/Brotli4j/v1.23.0/natives/src/main/cpp/decoder_jni.cc)).
    The BCL decoder has a single `Decompress(source, destination, out consumed, out written)` call, which decodes and
    writes into the caller's span in one step
    ([Decompress](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlidecoder.decompress?view=net-10.0)).
  - Each class keeps the wrapper state in fields: `inputBuffer` with `inputOffset`/`inputLength`; `outputBuffer` with
    `outputLength`, which holds at most min(size, 64 KiB) per call (`MAX_OUTPUT_BUFFER_SIZE`), so a large
    `outputBufferSize`/`maxOutputChunkSize` does not allocate an array of that size and `pull(size)` may return less;
    and `lastStatus`.
  - Two CLR-only private methods stand for the wrapper calls. `Push(length)` runs one `Decompress` call into the
    emptied output buffer; if it returns `NeedMoreData` with a full buffer, the status becomes `DestinationTooSmall`,
    because the decoder may still hold output. `Pull()` runs after the output is copied; it releases the output and
    continues decoding after `DestinationTooSmall`.

    | brotli4j status | BCL `lastStatus` | Note |
    | --- | --- | --- |
    | ERROR | `InvalidData` | |
    | DONE | `Done` | Only once the output was taken. It is ERROR when pushed input is left, as in decoder_jni.cc ("Bytes after stream end are not allowed"). |
    | NEEDS_MORE_INPUT | `NeedMoreData` | |
    | NEEDS_MORE_OUTPUT | `DestinationTooSmall` | |
    | OK | does not occur | `Pull` continues at once. |

  - The BCL decoder is a mutable struct with lazy native state
    ([source](https://raw.githubusercontent.com/dotnet/dotnet/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/runtime/src/libraries/System.IO.Compression.Brotli/src/System/IO/Compression/dec/BrotliDecoder.cs)),
    so the field is not readonly, and `destroy()` is `Dispose()`. `BrotliDecompressor.close()` may call `Dispose()`
    twice: "If an object's Dispose method is called more than once, the object must ignore all calls after the first
    one" ([IDisposable.Dispose](https://learn.microsoft.com/en-us/dotnet/api/system.idisposable.dispose?view=net-10.0)).
  - The `IOException` that the wrapper constructor wraps in `DecompressionException` cannot occur in the constructor:
    the BCL decoder creates its native state on the first `Decompress` call, so a creation failure surfaces there as
    `IOException`.
  - The static initializers are static constructors calling `Brotli.EnsureAvailability()`. A static constructor
    exception reaches callers as `TypeInitializationException`
    ([static constructors](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors)),
    so the `ExceptionInInitializerError` try/catch is not repeated.
  - `BrotliDecompressor` has `Status_()` and `Builder_()`. `assert` is `Debug.Assert`, which "works only in debug
    builds"
    ([Debug.Assert](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.debug.assert?view=net-10.0)).
- **Scratch probe.** A console probe (not in the repository) passed for input buffers of 1 B to 1 MiB and output sizes
  of 1 B to `int.MaxValue`, input fed byte by byte or at once with random and compressible data, trailing bytes inside
  the pushed input (error) and after DONE (skipped), corrupt and truncated streams, four quality/window/mode options,
  sharable and non-sharable encoders, one encoder shared by two channels, and every flushed message decoding at once.

## Byte values

`ByteBuf.getByte`/`readByte` return `System.Byte` (owner decision). The places where the Java result depends on the
sign keep it with `(sbyte)` or `unchecked((int) ...)` and a `// CLR:` comment:

| Place | Java | C# |
| --- | --- | --- |
| `FastLz.decompress` level | `(input.getByte(inOffset) >> 5) + 1` on a signed byte: a first byte >= 0x80 reports `invalid level: -3`..`0` (unsigned it would be 5..8) | `((sbyte) input.GetByte(inOffset) >> 5) + 1` |
| `Bzip2HuffmanStageDecoder.createHuffmanDecodingTables` | reads the code lengths that the decoders store as `(byte) currLength` as signed bytes in `Math.max/min`, `tableBases[len + 1]` and `== bitLength`; observable for malformed streams whose code length leaves 0..127 | `(sbyte)` at each read |
| `JdkZlibDecoder` gzip header | `magic0`/`magic1` from `readByte()` (signed) | `unchecked((sbyte) @in.ReadByte())`; `magic0 != 31` and `crc.update(int)` give the same result either way |
| `SnappyTest.testValidateChecksumFails` | `maskChecksum(0xd6cb8b55)`, a negative int widened to `long` | `MaskChecksum(unchecked((int) 0xd6cb8b55))`; a positive `uint` would rotate differently in bits 17..31 |

The other byte uses keep the source code:

- Snappy's `tag` is always masked (`tag & 0x03`, `tag >> 2 & 0x3F`, `(tag & 0x01c) >> 2`, `(tag & 0x0e0) << 8 >> 5`).
  `mapChunkType`/`checkByte` of the Snappy decoder and decompressor compare unsigned bytes on both sides
  (`(byte) chunkTypeVal == (byte) 0xff`, `& 0x80`), and the FastLz options are masked (`options & 0x01`,
  `options & 0x10`).
- FastLz's byte comparisons and `output.getByte(0) | 1 << 5` compare for equality or keep the low 8 bits, and its
  `setByte` arguments (`(byte) (...)`, `(byte) 255`) write only the low 8 bits either way.
- Bzip2: `readByte() - '0'` in the INIT states of `Bzip2Decoder`/`Bzip2Decompressor` gives -128..127 in Java and 0..255
  in C#, but every byte >= 0x80 is an invalid block size in both (-176..-49 against 80..207), with the same exception.
  The `ByteProcessor` of `Bzip2BlockCompressor` only compares values with each other and passes them on `& 0xff`;
  `Bzip2DivSufSort`'s `SA[i] = T[...]` is read back only as `bwtBlock[i] & 0xff`, and its other reads of `T` order with
  `& 0xff` or compare for equality; `Bzip2MoveToFrontTable`'s byte casts keep the same bits and are compared for
  equality, masked or below 6; `currentTable = selectors[0]` reads MTF indexes 0..5.
- zlib: `JdkZlibDecoder.looksLikeZlib(short)` gets the signed short from `GetShort` in both ports, so `cmf_flg % 31`
  matches; `getTotalOut()`/`getTotalIn()` and the GZIP trailer CRC are `unchecked((int) ...)` casts.
- Brotli's main code has no byte arithmetic; it only copies bytes between buffers. `BrotliDecoderTest`'s
  `(byte) rng.nextInt()` is `(byte) rng.Next()`, with the same low eight bits of random data.

## Not ported

### Deprecated and JVM-only units

| Unit | Status | Reason |
| --- | --- | --- |
| `SnappyFramedDecoder` | `deprecated-not-ported` | Empty subclass with `@deprecated Use {@link SnappyFrameDecoder} instead.`; owner decision 2026-11-16 does not port deprecated units that name a ported replacement ([Deprecated](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Deprecated.html)). No original test uses it. |
| `SnappyFramedEncoder` | `deprecated-not-ported` | Empty subclass with `@deprecated Use {@link SnappyFrameEncoder} instead.`; no original test uses it. |
| `JdkZlibDecoder()`, `(byte[])`, `(ZlibWrapper)`, `(ZlibWrapper, boolean)`, `(boolean)` (members) | 5 comments recorded as `deprecated-not-ported` | Their `@deprecated` texts name the `(..., int maxAllocation)` overloads, which are ported. |
| `ZlibCodecFactory.newZlibDecoder()`, `(ZlibWrapper)`, `(byte[])` (members) | 3 comments recorded as `deprecated-not-ported` | Replaced by `NewZlibDecoder(..., int maxAllocation)`. |
| `StandardCompressionOptions.brotli(Encoder.Parameters)` (member) | comment recorded as `deprecated-not-ported` | `@deprecated Use {@link #brotli(int, int, BrotliMode)}`, which is ported. |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the namespace table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor (it also requires the optional zstd-jni, lzma-java, jzlib, lz4, brotli4j and compress-lzf modules); `Netty.NET.Handler.Codec.Compression.csproj` references codec-base, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `META-INF/native-image/.../native-image.properties` | `jvm-only-replaced` | `--initialize-at-run-time` for `BrotliDecoder`, whose static initializer probes brotli4j ([build configuration](https://www.graalvm.org/latest/reference-manual/native-image/overview/BuildConfiguration/), [class initialization](https://www.graalvm.org/latest/reference-manual/native-image/optimizations-and-performance/ClassInitialization/)). The BCL decoder needs no probe, the NativeAOT deployment documentation describes no build-time class initialization option ([Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)), and C# static constructors run before first use at run time ([static constructors](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors)). |

### brotli4j members

| Member | Status | Reason |
| --- | --- | --- |
| `Brotli` static initializer, `logger`, `CNFE`, `cause` | comment recorded as `library-not-ported` (`Brotli4jLoader`) | Class-path probe of brotli4j; the BCL needs none. `IsAvailable()` is true, `Cause()` is null. |
| `BrotliMode.adapt()` | Javadoc recorded as `library-not-ported` (`Mode`) | Returns brotli4j's `Encoder.Mode`; the BCL encoder has no mode. |
| `BrotliOptions.parameters()` | not ported (no comment) | Returns brotli4j's `Encoder.Parameters`; the encoder reads the internal fields. |
| `BrotliEncoder(Encoder.Parameters)` | Javadoc recorded as `library-not-ported` (`Encoder`) | Mapped, it is `BrotliEncoder(BrotliOptions)`, which exists. `BrotliEncoder(Encoder.Parameters, boolean)` is ported as `BrotliEncoder(BrotliOptions parameters, bool isSharable)`. |

### Libraries without a BCL counterpart

Every unit below is `library-not-ported` (owner decision 2026-11-23); the manifest records the library, its Maven
coordinates and Java packages, the reason and every original comment and declared test. The libraries are optional
Maven dependencies of Netty. The .NET 10 BCL has no counterpart
([System.IO.Compression](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression?view=net-10.0)).

**lz4-java** (`at.yawk.lz4:lz4-java:1.12.0`; `net.jpountz.lz4`, `net.jpountz.xxhash`): LZ4 block compression and the
xxHash32 checksum; System.IO.Compression has no LZ4 type.

| Unit | Status | Reason |
| --- | --- | --- |
| `Lz4FrameEncoder` | `library-not-ported` | LZ4 frame encoder over lz4-java's `LZ4Compressor` and the xxHash32 checksum. |
| `Lz4FrameDecoder` | `library-not-ported` | LZ4 frame decoder over lz4-java's `LZ4SafeDecompressor`. |
| `Lz4FrameDecompressor` | `library-not-ported` | LZ4 frame Decompressor over lz4-java's `LZ4SafeDecompressor`. |
| `Lz4XXHash32` | `library-not-ported` | Checksum over lz4-java's `XXHash32`, used by the LZ4 frames. |
| `Lz4Constants` | `library-not-ported` | Constants of the LZ4 frame format; serves `Lz4FrameEncoder`. |
| `Lz4FrameDecoderTest` (10 tests) | `library-not-ported` | Tests of `Lz4FrameDecoder` against lz4-java streams. |
| `Lz4FrameEncoderTest` (7 tests) | `library-not-ported` | Tests of `Lz4FrameEncoder` against lz4-java streams. |
| `Lz4DecompressorTest` (9 tests) | `library-not-ported` | Decompressor tests of `Lz4FrameDecompressor`. |
| `Lz4FrameIntegrationTest` (no own test) | `library-not-ported` | Integration tests of the LZ4 encoder/decoder pair. |

**compress-lzf** (`com.ning:compress-lzf:1.2.1`; `com.ning.compress`): LZF chunk encoding and decoding;
System.IO.Compression has no LZF type.

| Unit | Status | Reason |
| --- | --- | --- |
| `LzfEncoder` | `library-not-ported` | LZF encoder over compress-lzf's `ChunkEncoder`. |
| `LzfDecoder` | `library-not-ported` | LZF decoder over compress-lzf's `ChunkDecoder`. |
| `LzfDecompressor` | `library-not-ported` | LZF Decompressor over compress-lzf's `ChunkDecoder`. |
| `LzfDecoderTest` (2 tests) | `library-not-ported` | Tests of `LzfDecoder` against compress-lzf. |
| `LzfEncoderTest` (no own test) | `library-not-ported` | Tests of `LzfEncoder` against compress-lzf. |
| `LzfDecompressorTest` (3 tests) | `library-not-ported` | Decompressor tests of `LzfDecompressor`. |
| `LzfIntegrationTest` (no own test) | `library-not-ported` | Integration tests of the LZF encoder/decoder pair. |
| `LengthAwareLzfIntegrationTest` (no own test) | `library-not-ported` | Integration tests of the length-aware LZF encoder. |

**lzma-java** (`com.github.jponge:lzma-java:1.3`; `lzma.sdk`, `lzma.streams`): LZMA encoding; System.IO.Compression
has no LZMA type.

| Unit | Status | Reason |
| --- | --- | --- |
| `LzmaFrameEncoder` | `library-not-ported` | LZMA frame encoder over lzma-java's `Encoder`. |
| `LzmaFrameEncoderTest` (1 test) | `library-not-ported` | Tests of `LzmaFrameEncoder` against lzma-java streams. |

**jzlib** (`com.jcraft:jzlib:1.1.3`; `com.jcraft.jzlib`): Netty's zlib codecs over the pure-Java zlib port, which Netty
uses for custom window sizes, memory levels and preset dictionaries. System.IO.Compression has
`DeflateStream`/`ZLibStream`/`GZipStream` (which the Jdk codecs use) but no dictionary API, and `ZLibCompressionOptions`
on .NET 10 sets only the compression level and strategy
([ZLibCompressionOptions.cs](https://raw.githubusercontent.com/dotnet/runtime/v10.0.8/src/libraries/System.IO.Compression/src/System/IO/Compression/ZLibCompressionOptions.cs),
[CompressionLevel](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibcompressionoptions.compressionlevel?view=net-10.0)).
ZlibCodecFactory creates the Jdk codecs and throws `NotSupportedException` where it would create a JZlib codec.

| Unit | Status | Reason |
| --- | --- | --- |
| `JZlibEncoder` | `library-not-ported` | ZlibEncoder over `com.jcraft.jzlib.Deflater`. |
| `JZlibDecoder` | `library-not-ported` | ZlibDecoder over `com.jcraft.jzlib.Inflater`. |
| `ZlibUtil` | `library-not-ported` | Package-private helpers of the JZlib codecs (`fail`, `inflaterException`, `deflaterException`, `convertWrapperType`, `wrapperOverhead`); every member serves them only. |
| `JZlibTest` (1 test) | `library-not-ported` | ZlibTest with the JZlib codecs, and `testHighlyCompressibleRawStreamIsFullyDecoded`. |
| `JZlibIntegrationTest` (no own test) | `library-not-ported` | AbstractIntegrationTest with the JZlib codecs. |
| `ZlibCrossTest1` (no own test) | `library-not-ported` | ZlibTest with `JdkZlibEncoder` and `JZlibDecoder`. |
| `ZlibCrossTest2` (1 test) | `library-not-ported` | ZlibTest with `JZlibEncoder` and `JdkZlibDecoder`; overrides `testZLIB_OR_NONE3` to expect `DecompressionException`. |

**zstd-jni** (`com.github.luben:zstd-jni:1.5.7-7`; `com.github.luben.zstd`, a JNI binding): System.IO.Compression gains
`ZstandardStream`/`ZstandardEncoder`/`ZstandardDecoder` only in .NET 11
([ZstandardStream](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandardstream)), and the
solution stays on .NET 10.

| Unit | Status | Reason |
| --- | --- | --- |
| `ZstdEncoder` | `library-not-ported` | Zstandard encoder over `Zstd.compress`. |
| `ZstdDecoder` | `library-not-ported` | Zstandard decoder over `ZstdInputStreamNoFinalizer`. |
| `ZstdDecompressor` | `library-not-ported` | Zstandard Decompressor over zstd-jni. |
| `Zstd` | `library-not-ported` | Availability probe of zstd-jni (`Class.forName`, `Native.load`). |
| `ZstdConstants` | `library-not-ported` | Zstandard defaults read from zstd-jni. |
| `ZstdOptions` | `library-not-ported` | Options of the Zstandard encoder. `StandardCompressionOptions.zstd()`/`zstd(int, int, int)`, which return it, are not ported either; their comments are recorded as `library-not-ported`. |
| `ZstdDecoderTest` (2 tests) | `library-not-ported` | Tests of `ZstdDecoder` against zstd-jni. |
| `ZstdDecompressorTest` (4 tests) | `library-not-ported` | Decompressor tests of `ZstdDecompressor`. |
| `ZstdEncoderTest` (4 tests) | `library-not-ported` | Tests of `ZstdEncoder` against zstd-jni. |
| `ZstdIntegrationTest` (no own test) | `library-not-ported` | Integration tests of the Zstandard encoder/decoder pair. |

## Test notes

- **Lifecycle.** xUnit creates one test class instance per test and runs the inherited `[Fact]`/`[Theory]` methods of
  an abstract base for each derived class ([shared context](https://xunit.net/docs/shared-context)).
  - In the concrete classes `@BeforeEach`/`@AfterEach` are the constructor and `Dispose()`, and the static
    `@BeforeAll setUp()` methods run from static constructors. They are private, because xUnit1013 asks public methods
    of a test class to be tests. `SnappyFrameDecoderTest.tearDown()` asserts
    `Assert.False(channel.FinishAndReleaseAll())` in `Dispose()`.
  - In `AbstractDecoderTest` and `AbstractEncoderTest` the final `@BeforeEach initChannel()` runs from
    `IAsyncLifetime.InitializeAsync`, "Called immediately after the class has been created, before it is used"
    ([IAsyncLifetime](https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/IAsyncLifetime.cs)), so
    `createChannel()` runs after the derived constructor as in JUnit. `@AfterEach destroyChannel()` runs from
    `DisposeAsync()` and is virtual.
- **BrotliEncoderTest.destroyChannel()** overrides the base's `@AfterEach destroyChannel()` without the annotation.
  A JUnit lifecycle method is "any method that is directly annotated or meta-annotated with ... @AfterEach"
  ([definitions](https://docs.junit.org/5.14.3/writing-tests/definitions.html)), `@AfterEach` methods "are inherited
  unless they are overridden" ([annotations](https://docs.junit.org/5.14.3/writing-tests/annotations.html),
  [test classes and methods](https://docs.junit.org/5.14.3/writing-tests/test-classes-and-methods.html)), and a method
  annotation is not inherited by an overrider: `@Inherited` "has no effect if the annotated interface is used to
  annotate anything other than a class"
  ([Inherited](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/annotation/Inherited.html)). So
  JUnit 5.14.3 (Netty's version) runs no `destroyChannel()` for BrotliEncoderTest. The C# test behaves the same: it
  re-implements `IAsyncLifetime` and makes `IAsyncDisposable.DisposeAsync()` a no-op. A re-implementation "is also
  implicitly a re-implementation of all of the interface's base interfaces", and inherited public members take part in
  its mapping
  ([interface re-implementation](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/interfaces#1967-interface-re-implementation)),
  so the base `InitializeAsync` still runs and the `DestroyChannel()` override, ported unchanged, is never called (a
  scratch probe confirmed the mapping).
- **Data sources.** `@TestInstance(PER_CLASS)` creates "a new test instance ... once per test class"
  ([lifecycle](https://docs.junit.org/current/writing-tests/test-instance-lifecycle.html)) and lets
  `AbstractDecoderTest`/`AbstractDecompressorTest` use the instance methods `smallData()`/`largeData()` as
  `@MethodSource` factories
  ([parameterized tests](https://docs.junit.org/current/writing-tests/parameterized-classes-and-tests.html)). xUnit
  member data must be a "public static member on the test class"
  ([MemberDataAttributeBase](https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/Attributes/MemberDataAttributeBase.cs)),
  and here the data depends on the subclass's `compress()`/`createCompressor()`.
  - `SmallData()`/`LargeData()` therefore keep the Java shape, `ByteBuf[]` instance methods that
    `BrotliDecompressorTest` calls. The static `SmallDataIndexes()`/`LargeDataIndexes()` give the index of the heap (0)
    and direct (1) buffer; each theory takes `int dataIndex` and releases the other buffer. The compressed bytes are
    computed per test instance, not once per class.
  - The static `smallData()`/`largeData()` of `AbstractEncoderTest` and `BrotliDecoderTest` are member data
    `SmallData()`/`LargeData()` (`object[]` rows holding the heap and direct buffers) with
    `DisableDiscoveryEnumeration = true`, so the buffers are created only when the theory runs.
- **Random.**
  - `SplittableRandom` is not ported. `AbstractCompressionTest` uses unseeded `System.Random` instances.
  - `AbstractIntegrationTest`'s shared, thread-safe `java.util.Random`
    ([Random](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Random.html)) is `Random.Shared`
    ([Random.Shared](https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0)), because xUnit
    runs test classes in parallel and `System.Random` is "not thread safe"
    ([Random](https://learn.microsoft.com/en-us/dotnet/api/system.random?view=net-10.0)).
  - `new SplittableRandom(rand.nextLong())` is `new Random(rand.Next())` and `splittableRandomNextBytes` is
    `NextBytes` (`AbstractIntegrationTest`, `Bzip2IntegrationTest`, `BrotliDecoderTest`).
  - `SnappyIntegrationTest.testWithSeed(long)`: the seeds were found with `java.util.Random(seed).nextBytes`, and
    upstream commit 9a469345987dc731ef67d2a61ceff8ccbe4067ad (#16808) switched the method to `SplittableRandom`, so the
    seeded tests no longer depend on exact bytes even in Java. The C# test fills the 16 MiB array from
    `new Random(unchecked((int) seed))`
    ([Random(Int32)](https://learn.microsoft.com/en-us/dotnet/api/system.random.-ctor?view=net-10.0)), deterministic
    per test.
  - `SnappyIntegrationTest.testRandom`: `rand.nextLong()` covers all longs, so the source skips about half of the runs
    (negative seed); `Random.NextInt64()` is never negative, so the seed is `NextInt64(long.MinValue, long.MaxValue)`
    ([NextInt64](https://learn.microsoft.com/en-us/dotnet/api/system.random.nextint64?view=net-10.0)), which keeps the
    skip. `System.out.println` is `Console.WriteLine`
    ([page](https://learn.microsoft.com/en-us/dotnet/api/system.console.writeline?view=net-10.0)).
  - `FastLzIntegrationTest`: `rand.nextBoolean()` is `rand.Next(2) == 0`
    ([Random.Next](https://learn.microsoft.com/en-us/dotnet/api/system.random.next?view=net-10.0)).
- **testHugeDecompress** (256 MiB) always runs: `@DisabledForSlowLeakDetection` only skips it under paranoid leak
  detection. `MessageDigest` SHA-256 is `IncrementalHash`
  ([IncrementalHash](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash?view=net-10.0)).
  Each 1 MiB chunk is built in an array and written and hashed at once, and the end handler hashes the segments of
  each received buffer instead of using a `ByteProcessor`, which avoids one native hash call per byte (268 million).
  The pooled allocator is disposed at the end. In the full parallel run on this host (348 test cases in about 3
  minutes) it takes 78-135 s for the two Snappy classes, 171-172 s for each of the three FastLz classes, 76 s for
  Bzip2, 1.2 s for JdkZlib and 2.5 s for Brotli; every other test takes under 5 s. The codecs go through `ByteBuf` per
  byte or word as the Java code does.
- **SharpZipLib 1.4.2** replaces commons-compress in the bzip2 cross-checks.
  - `BZip2CompressorOutputStream(os, MIN_BLOCK_SIZE)` is `BZip2OutputStream(Stream, int blockSize)`: "Valid block
    sizes are in the range 1..9, with 1 giving the lowest compression and 9 the highest" (out-of-range values are
    clamped). `close()` is `Dispose()`, which on the first call ends the block, writes the end-of-stream marker and the
    combined CRC, and closes the base stream, because for `IsStreamOwner` "The default value is true"
    ([BZip2OutputStream.cs](https://raw.githubusercontent.com/icsharpcode/SharpZipLib/v1.4.2/src/ICSharpCode.SharpZipLib/BZip2/BZip2OutputStream.cs)).
    `MemoryStream.ToArray` "works when the MemoryStream is closed"
    ([ToArray](https://learn.microsoft.com/en-us/dotnet/api/system.io.memorystream.toarray?view=net-10.0)).
  - `BZip2CompressorInputStream(is)` is `BZip2InputStream(Stream)`, which reads the block size from the header ('h' and
    a digit 1..9). `Read(byte[], int, int)` returns "zero if the end of the stream is reached" and `ReadByte()` "byte
    read or -1 on end of stream", so `assertEquals(-1, bzip2Is.read())` is `Assert.Equal(-1, bzip2Is.ReadByte())`. It
    does not read concatenated streams, which is not needed because the encoder writes one stream
    ([BZip2InputStream.cs](https://raw.githubusercontent.com/icsharpcode/SharpZipLib/v1.4.2/src/ICSharpCode.SharpZipLib/BZip2/BZip2InputStream.cs)).
- **Bzip2 tests.**
  - `testRleOffByOneDoesNotCauseInfiniteLoop`: `assertTimeoutPreemptively` runs the executable "in a different thread
    than that of the calling code"
    ([Assertions](https://docs.junit.org/5.10.0/api/org.junit.jupiter.api/org/junit/jupiter/api/Assertions.html)). The
    C# test is `async Task`: `Task.Run` plus
    `WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)`, whose task "should be faulted with a
    TimeoutException if it hasn't otherwise completed"
    ([WaitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0)); the
    `TimeoutException` becomes `Assert.Fail` with the source message. A hung executable is not aborted (the CLR has no
    thread abort).
  - `assertThrows(DecompressionException.class, ..., msg)` is `Assert.Throws<DecompressionException>(..., msg)`
    (Xunit.v3.AssertMessages); `DecompressionException` has no subclasses, so the exact-type check accepts what JUnit
    accepts.
  - `Bzip2IntegrationTest` keeps the sizes 500/1100/2300 (3/4/5 Huffman tables). `Bzip2BlockDecompressorTest` drives the
    internal classes through `InternalsVisibleTo`.
  - `Bzip2MalformedStreams` (a helper without tests): `ByteArrayOutputStream.write(int)` writes "the eight low-order
    bits of the argument b"
    ([OutputStream](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/OutputStream.html)), so it is
    `MemoryStream.WriteByte((byte) value)`.
- **ZlibTest and JdkZlibTest.**
  - JdkZlibTest overrides `testZLIB_OR_NONE3`, but the checker accepts only a non-virtual `public void` `[Fact]`
    (tools/Test-CodecPortingManifest.ps1). ZlibTest's `[Fact] TestZLIB_OR_NONE3` therefore runs the virtual
    `TestZLIB_OR_NONE3Core()`, which JdkZlibTest overrides (`JdkZlibTest.super.testZLIB_OR_NONE3()` is
    `base.TestZLIB_OR_NONE3Core()`); the JdkZlibTest entry lists ZlibTest.cs too.
  - The java.util.zip helpers of the tests are the BCL streams (`GZipStream`, `ZLibStream`, `DeflateStream` with
    `ZLibCompressionOptions.CompressionLevel` 9 or -1), and `IOUtils.toByteArray` is `Stream.CopyTo`.
  - `multiple.gz` (two concatenated gzip members) is copied byte for byte and embedded as the manifest resource
    `multiple.gz`, which the tests read with `Assembly.GetManifestResourceStream` where Java calls
    `getClass().getResourceAsStream("/multiple.gz")`.
  - `testGZIP3`'s negative byte literals are `unchecked((byte) -117)` and so on.
- **JdkZlibDecompressorTest.** `@ParameterizedClass`/`@MethodSource("wrappers")` runs "all tests in a test class ...
  multiple times with different arguments"
  ([JUnit](https://docs.junit.org/current/writing-tests/parameterized-classes-and-tests.html)). In C# the class is
  abstract with the nested classes `ZLIB`, `GZIP` and `NONE`, so xUnit runs its tests and the inherited
  `AbstractDecompressorTest` tests per wrapper (3 x 18 cases). `assumeTrue` is `Assert.SkipUnless`
  ([SkipAsserts.cs](https://raw.githubusercontent.com/xunit/assert.xunit/main/SkipAsserts.cs)), as a failed JUnit
  assumption aborts the test ([assumptions](https://docs.junit.org/current/writing-tests/assumptions.html)): the nine
  tests that apply to GZIP or ZLIB only are skipped for the other two wrappers, which are the 18 skipped cases.
- **Snappy and FastLz tests.**
  - `FastLzIntegrationTest.TestWithChecksum` and `TestRandomChecksum` are public nested classes; xUnit discovers and
    runs them (11 inherited tests each). In the `testIdentity` override the two `in` locals are one `@in` declared
    before the loop (CS0136), as in `AbstractEncoderTest`.
  - Signed byte literals: short arrays use `unchecked((byte) -0x..)` and `(byte) 'c'`; the long arrays of
    `SnappyIntegrationTest.test1002` and `SnappyFrameEncoderTest.testInputBufferOverseek` are `sbyte[]` literals
    converted by a private `Bytes()` helper, as `DnsResponseTest` does.
  - `SnappyTest.encodeAndDecodeLongTextUsesCopy` compares the source string with
    `GetCharSequence(0, writerIndex, Encoding.ASCII).ToString()` where Java compares two `CharBuffer.wrap` views.
- **Brotli reference data.** brotli4j's `BrotliOutputStream` is the BCL `BrotliStream`
  ([BrotliStream](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlistream?view=net-10.0)).
  `new BrotliOutputStream(os)` uses quality and window -1, that is the native defaults 11 and 22 (encode.h), so the
  `BrotliStream` gets `BrotliCompressionOptions { Quality = 11 }`
  ([Quality](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlicompressionoptions.quality?view=net-10.0),
  default 4); its window is the BCL default 22 (`BrotliUtils.WindowBits_Default`,
  [source](https://raw.githubusercontent.com/dotnet/dotnet/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/runtime/src/libraries/System.IO.Compression.Brotli/src/System/IO/Compression/BrotliUtils.cs)).
- **ByteBufChecksumTest.** Both methods are ported. The two source lines that check `Lz4XXHash32` (library-not-ported
  with lz4-java) are dropped, with a CLR comment and the per-method adaptation saying so. The jpountz `XXHash32`
  reference is `System.IO.Hashing.XxHash32` with the same seed (`Lz4Constants.DEFAULT_SEED` `0x9747b28c`, repeated in
  the test), and the reference value is read with `GetCurrentHashAsUInt32()` of the BCL type, not through
  `ByteBufChecksum`. The Java test does not cover `Crc32c`; the scratch check above did, and the Snappy tests exercise
  it.
- `assertDoesNotThrow(x::close)` is `Assert.Null(Record.Exception(x.Close))`, which "Returns the exception that was
  thrown by the code; null, otherwise" ([Record](https://raw.githubusercontent.com/xunit/assert.xunit/main/Record.cs)).

## Owner review items

- **Byte signs.** `(sbyte)` keeps the Java value in `FastLz.decompress` (the level of a first byte >= 0x80),
  `Bzip2HuffmanStageDecoder.createHuffmanDecodingTables` (code lengths of malformed streams) and `JdkZlibDecoder` (gzip
  magic bytes, not observable), and `SnappyTest.testValidateChecksumFails` passes `unchecked((int) 0xd6cb8b55)`. The
  first two differ from an unsigned reading only for invalid input. See [Byte values](#byte-values).
- **BrotliMode is not applied.** The BCL encoder has no mode parameter, so `BrotliOptions` keeps TEXT/FONT/GENERIC but
  every stream is encoded as `BROTLI_MODE_GENERIC`; the output is valid and may differ in size from brotli4j's.
- **Preset dictionaries are unsupported.** `JdkZlibEncoder(byte[])`, `JdkZlibEncoder(int, byte[])` and
  `ZlibCodecFactory.NewZlibEncoder(byte[])`/`(int, byte[])` throw `NotSupportedException`. A decoder or decompressor
  with a dictionary throws `NotSupportedException` (wrapped in a `DecoderException` by JdkZlibDecoder) when a stream
  sets FDICT; streams without FDICT decode normally.
- **ZlibCodecFactory's JZlib branches** (`io.netty.noJdkZlibEncoder`/`noJdkZlibDecoder`, or a window size or memory
  level other than 15/8) throw `NotSupportedException`, where a JVM without jzlib fails with `NoClassDefFoundError`;
  `isSupportingWindowSizeAndMemLevel()` is false.
- **Trailing bytes after a ZLIB or raw stream count as consumed.** JdkZlibDecoder skips the bytes the BCL stream read
  past the end instead of leaving them in the cumulation (which Java forwards when the handler is removed), and
  JdkZlibDecompressor's `consumeInput()` advances past them. GZIP is exact. No original test observes it.
- **Crc32c** could become `System.IO.Hashing.Crc32` with `Crc32ParameterSet.Crc32C`, which gave the same value and
  bytes in a scratch test; the owner kept Netty's class for now.
- **IDecompressor extends IDisposable**, with `Dispose()` calling `Close()`, so that `using` stands for
  try-with-resources; the Java interface extends `AutoCloseable` only.
- **InputBufferingDecompressor is public** (package-private in Java), with an `internal` constructor, because its
  public subclasses `JdkZlibDecompressor` and `SnappyFrameDecompressor` cannot be more accessible than their base; it
  can be named but not subclassed outside the assembly.
