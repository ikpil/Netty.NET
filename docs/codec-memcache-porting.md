# codec-memcache porting

Netty.NET ports `netty-codec-memcache` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Memcache`, with its original tests in
`test/Netty.NET.Handler.Codec.Memcache.Tests`. The [manifest](codec-memcache-porting-manifest.json) records every
pinned file of `codec-memcache/src` (45 files) with its C# counterpart, its CLR adaptations and evidence, or the reason
it is not ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-memcache
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files, that every declared JUnit test is mapped to a C# test of the same name
(case may differ) or carries a recorded disposition, and the facts and evidence of every unit that is not ported. It
fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 32 |
| `verified` (tests whose every original method is mapped) | 8 |
| `jvm-only-replaced` | 5 |

Of the 30 declared original tests, 29 are mapped to C# tests with the same names (35 xUnit cases, because the six
`@ParameterizedTest` methods run for `request = false` and `request = true`) and 1 belongs to the GraalVM metadata
test, which is not ported. No test is CLR-inapplicable and nothing in the module is `@Deprecated`.

## Namespaces and assembly (owner decisions, 2026-11-19)

The project, folder and assembly are `Netty.NET.Handler.Codec.Memcache`, the namespace of the module's root package,
as for every codec module (see [codec-base-porting.md](codec-base-porting.md)). The binary package sits in the folder
`Binary/`.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.memcache` | `Netty.NET.Handler.Codec.Memcache` | Common superset of ascii and binary classes. |
| `io.netty.handler.codec.memcache.binary` | `Netty.NET.Handler.Codec.Memcache.Binary` | Implementations and Interfaces for the Memcache Binary protocol. |
| test `io.netty.handler.codec.memcache.binary` | `Netty.NET.Handler.Codec.Memcache.Tests.Binary` | (all original tests are in the binary package) |

`@UnstableApi` on every type is the Common `[UnstableApi]` attribute.

## Shared CLR adaptations

- **Covariant returns (owner decision 2026-11-19).** The message interfaces re-declare the
  `ReferenceCounted`/`ByteBufHolder` methods (`retain`, `touch`, `copy`, `duplicate`, `retainedDuplicate`,
  `replace`) only to narrow the return type. These are inherited, not repeated, the rule of
  [transport-porting.md](transport-porting.md) (Decisions).
  - The Javadoc of a removed re-declaration stays at its place in the interface, followed by a `// CLR:` line.
  - In the diamonds `IFullMemcacheMessage` and `IFullBinaryMemcacheRequest/Response`, `IByteBufHolder` hides the
    `IReferenceCounted` members, so the inherited calls stay unambiguous.
  - The classes override `retain()`, `retain(int)`, `touch()` and `touch(Object)`, which are virtual in the Common
    `AbstractReferenceCounted` as in Java, and the holder methods, with covariant returns
    ([override](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override)).
  - The shared `IByteBufHolder` re-declares its members with its own return type. Classes that do not derive from
    `DefaultByteBufHolder` therefore implement those eight members explicitly, forwarding to the public members, as
    Transport's `DatagramPacket` does
    ([explicit interface implementation](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/interfaces/explicit-interface-implementation)).
    This applies to `DefaultMemcacheContent`, `DefaultFullBinaryMemcacheRequest`/`Response` and the nested
    `EmptyLastMemcacheContent`, which also bridges `IReferenceCounted`.
- **Holders.** `content()` is the read-only `IByteBufHolder.Content` property of the Buffer port
  ([buffer-porting.md](buffer-porting.md), Buffer holders and conversion).
- **Interface constant.** `LastMemcacheContent.EMPTY_LAST_CONTENT` is a static readonly field of
  `ILastMemcacheContent`, and its anonymous class is the private nested class `EmptyLastMemcacheContent`
  ([interface](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface)).
- **Name lookup.** Inside a class with a `DecoderResult()` method the simple name `DecoderResult` is that method, so
  `DecoderResult.SUCCESS` is written `global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS`.
- **Exceptions and types.** As in codec-base: `IllegalStateException` is `InvalidOperationException`,
  `UnsupportedOperationException` is `NotSupportedException`, `java.lang.Error` is `UnreachableException`,
  `MemcacheObject.class` is `typeof(IMemcacheObject)`, `FileRegion` is the Transport `IFileRegion`, and `AtomicLong`
  is the Common `AtomicLong`. Numbers in exception messages use the invariant culture.
- **Switch fall-through.** `AbstractBinaryMemcacheDecoder.decode` falls through its states; each C# section ends with
  `goto case` to the next state
  ([switch statement](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements)).
- **Inner classes.** `BinaryMemcacheClientCodec.Encoder`/`Decoder` receive the codec in their constructors, because a
  C# nested class has no enclosing instance.

## Byte values

`ByteBuf.getByte`/`readByte` return `System.Byte` (owner decision), so the Java `byte` protocol values (`magic`,
`opcode`, `extrasLength`, `dataType`, the opcode constants and `REQUEST_MAGIC_BYTE`/`RESPONSE_MAGIC_BYTE`) are C#
`byte`. Where the Java result depends on the sign, the port reads the value as `(sbyte)` and says so in a `// CLR:`
comment (Java promotes a `byte` operand by sign extension,
[JLS 5.6](https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.6)):

| Place | Java expression | C# |
| --- | --- | --- |
| `AbstractBinaryMemcacheMessage(ByteBuf, ByteBuf)` | `totalBodyLength = keyLength + extrasLength` | `keyLength + (sbyte) extrasLength` |
| `AbstractBinaryMemcacheMessage.setExtras` | `short oldExtrasLength = extrasLength; totalBodyLength + extrasLength - oldExtrasLength` | both read as `(sbyte)` |
| `DefaultFullBinaryMemcacheRequest/Response(key, extras, content)` | `keyLength() + extrasLength() + content.readableBytes()` | `(sbyte) ExtrasLength()` |
| `AbstractBinaryMemcacheEncoder.encodeMessage` | `buffer(MINIMUM_HEADER_SIZE + extrasLength() + keyLength())` | `(sbyte) msg.ExtrasLength()` |

The other byte uses do not depend on the sign and keep the source code:

- `extrasLength() & 0xFF` in the decoder gives the same unsigned length.
- `writeByte(int)` writes the same low eight bits.
- The magic constants `(byte) 0x80`/`(byte) 0x81` are 128/129 instead of -128/-127; they are only written to and
  compared with bytes.
- In the tests, `(byte) 0x80`, `(byte) 0xAA` and the `(byte)` array elements compare with `ReadByte()` values of the
  same bits.

`keyLength`, `reserved` and `status` are `short` in both languages.

## Test notes

- xUnit creates one test class instance per test, so `@BeforeEach`/`@AfterEach` are the constructor and `Dispose()`
  ([shared context](https://xunit.net/docs/shared-context)).
- `assertInstanceOf` is `Assert.IsAssignableFrom`; `@ParameterizedTest @ValueSource(booleans = {false, true})` is a
  `[Theory]` with two `[InlineData]` rows.
- `BinaryMemcacheDecoderTest`: `substring(7, 9)` is `Substring(7, 9 - 7)`, and the int literal `0x80000000` is
  `unchecked((int) 0x80000000)` because a C# hex literal above `int.MaxValue` is a `uint`.

## Not ported

| Unit | Status | Reason |
| --- | --- | --- |
| 2 `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summaries are in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.Handler.Codec.Memcache.csproj` references codec-base, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |
