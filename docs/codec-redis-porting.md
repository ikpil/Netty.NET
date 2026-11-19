# codec-redis porting

Netty.NET ports `netty-codec-redis` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Redis`, with its original tests in
`test/Netty.NET.Handler.Codec.Redis.Tests`. The [manifest](codec-redis-porting-manifest.json) records every pinned
file of `codec-redis/src` (33 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not
ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-redis
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files or recorded as belonging to a deprecated member that is not ported, that
every declared JUnit test is mapped to a C# test of the same name (case may differ) or carries a recorded disposition,
and the facts and evidence of every unit that is not ported. It fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 24 |
| `verified` (tests whose every original method is mapped) | 5 |
| `jvm-only-replaced` | 4 |

Of the 39 declared original tests, 38 are mapped to C# tests with the same names and 1 belongs to the GraalVM metadata
test, which is not ported. No test is CLR-inapplicable.

## Namespaces and assembly (owner decisions, 2026-11-19)

The project, folder and assembly are `Netty.NET.Handler.Codec.Redis`, the namespace of the module's package, as for
every codec module (see [codec-base-porting.md](codec-base-porting.md)).

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.redis` | `Netty.NET.Handler.Codec.Redis` | Encoder, decoder for Redis. |
| test `io.netty.handler.codec.redis` | `Netty.NET.Handler.Codec.Redis.Tests` | |

`@UnstableApi` on every public type is the Common `[UnstableApi]` attribute. The package-private `RedisConstants`,
`RedisCodecUtil` and the test helper `RedisCodecTestUtil` are `internal sealed` classes.

## Shared CLR adaptations

- **Covariant returns (owner decision 2026-11-19).** `IBulkStringRedisContent` and `ILastBulkStringRedisContent`
  re-declare the `ByteBufHolder`/`ReferenceCounted` methods only to narrow the return type. These are inherited, not
  repeated, the rule of [transport-porting.md](transport-porting.md) (Decisions).
  - The classes override the virtual `DefaultByteBufHolder` members with covariant returns
    ([override](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override)), so they need
    no explicit implementations.
  - Only the nested `EmptyLastBulkStringRedisContent`, which implements the interfaces directly, bridges the
    `IByteBufHolder`/`IReferenceCounted` members, as Transport's `DatagramPacket` does.
  - `content()` and `refCnt()` are the Buffer port's `Content`/`RefCnt` properties
    ([buffer-porting.md](buffer-porting.md), Buffer holders and conversion).
- **Predefined instances.** The anonymous subclasses behind `ArrayRedisMessage.NULL_INSTANCE`/`EMPTY_INSTANCE`,
  `FullBulkStringRedisMessage.NULL_INSTANCE`/`EMPTY_INSTANCE` and `LastBulkStringRedisContent.EMPTY_LAST_CONTENT` are
  named private nested classes. `EMPTY_LAST_CONTENT` is a static readonly field of `ILastBulkStringRedisContent`.
  - The `ArrayRedisMessage` instances override `retain()`, `retain(int)`, `touch()` and `touch(Object)` with
    covariant returns, and `release()`/`release(int)` to return `false`, as in Java.
  - These `AbstractReferenceCounted` members are virtual in Common, as in Java.
- **Java enums.** `FixedRedisMessagePool.RedisReplyKey` is a C# enum. `RedisMessageType` and
  `FixedRedisMessagePool.RedisErrorKey` carry constructors, fields and methods, so they are sealed classes with static
  readonly instances, as codec-base's `Base64Dialect`
  ([enum](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum),
  [JLS 8.9](https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html#jls-8.9)).
  - `RedisErrorKey.Values()` returns the constants in declaration order.
  - `RedisDecoder`'s two `switch` statements on a `RedisMessageType` are `if`/`else` chains, because the constants are
    not compile-time constants.
- **Primitive collections.** `LongObjectHashMap`/`LongObjectMap` (io.netty.util.collection; the owner kept `Dictionary`
  instead of porting it on 2026-11-19, see [common-porting-manifest.json](common-porting-manifest.json)) are
  `Dictionary<long, V>`. Java needs the primitive maps because "Type arguments may be either reference types or
  wildcards" ([JLS 4.5.1](https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.5.1)); the CLR creates a
  specialized generic type for a value type argument, so `long` keys are not boxed
  ([generics in the runtime](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generics-in-the-run-time)).
- **Maps.** `HashMap.get` is `Dictionary.GetValueOrDefault`. A null string or `ByteBuf` key returns null, as
  `HashMap` "permits null values and the null key" while `Dictionary` throws `ArgumentNullException`.
- **Other mappings.**
  - `ArrayDeque` used as a stack is `Stack<T>`, and `ByteProcessor` is `Func<byte, bool>`.
  - `RedisMessage.class` is `typeof(IRedisMessage)`, and `UnsupportedOperationException` is `NotSupportedException`.
  - `ByteBuf.array()` has no C# counterpart, so `new String(buf.array())` is `buf.ToString(Encoding.Default)`.
  - Numbers in messages and `toString()` use the invariant culture.

## Byte values

`ByteBuf.getByte`/`readByte` return `System.Byte` (owner decision). The redis prefixes and delimiters are ASCII, so
most byte uses do not depend on the sign. The places that do keep the Java result with `(sbyte)` and a `// CLR:`
comment:

| Place | Java | C# |
| --- | --- | --- |
| `RedisDecoder.readEndOfLine` | `"delimiter: [" + bytes[0] + "," + bytes[1] + "]..."` prints signed bytes | `((sbyte) bytes[i])` |
| `RedisDecoder.ToPositiveLongProcessor.process` | `"bad byte in number: " + value` prints the signed byte | `((sbyte) value)` |

The other byte uses keep the source code:

- `ToPositiveLongProcessor` rejects a byte of 0x80..0xFF in both languages: as `System.Byte` it is above `'9'`, as a
  Java `byte` it is below `'0'`.
- `RedisMessageType`'s prefixes are ASCII bytes.
- `RedisCodecUtil.shortToBytes` stores the same bits.

## Deprecated and not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `RedisArrayAggregator()` (member) | comments recorded as `deprecated-not-ported` | `@deprecated Use RedisArrayAggregator(int, int) instead`; owner decision 2026-11-16 does not port deprecated members that name a replacement. Its private `DEFAULT_MAX_ARRAY_LENGTH`, used only by it, is not ported either. `testDoesNotLeakOnClose`/`testDoesNotLeakOnRemoval` check leaks rather than the constructor, so they use the replacement with the deprecated constructor's arguments, `(RedisConstants.REDIS_MAX_ARRAY_LENGTH, 1024)`. |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.Handler.Codec.Redis.csproj` references codec-base, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |

## Test notes

- xUnit creates one test class instance per test, so `@BeforeEach`/`@AfterEach` are the constructor and `Dispose()`
  ([shared context](https://xunit.net/docs/shared-context)).
- JUnit `assertThrows` accepts subtypes
  ([Assertions](https://docs.junit.org/current/api/org.junit.jupiter.api/org/junit/jupiter/api/Assertions.html)).
  - `RedisArrayAggregatorTest` expects `CodecException`, which arrives wrapped in a `DecoderException`, so those
    assertions are `Assert.ThrowsAny<CodecException>`.
  - The other assertions throw the exact type and stay `Assert.Throws`.
- The AssertJ chain of `RedisDecoderTest.shouldErrorOnTooLargeArray` (`rootCause()`) uses
  `Exception.GetBaseException()`.
- `FixedRedisMessagePoolTest` suppresses xUnit2000 with a reason, because it keeps the source argument order of
  `assertEquals(fromStr.content(), "OK")`.
