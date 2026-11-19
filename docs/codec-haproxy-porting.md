# codec-haproxy porting

Netty.NET ports `netty-codec-haproxy` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.HAProxy`, with its original tests in
`test/Netty.NET.Handler.Codec.HAProxy.Tests`. The [manifest](codec-haproxy-porting-manifest.json) records every pinned
file of `codec-haproxy/src` (18 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not
ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-haproxy
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files, that every declared JUnit test is mapped to a C# test of the same name
(case may differ) or carries a recorded disposition, and the facts and evidence of every unit that is not ported. It
fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 10 |
| `verified` (tests whose every original method is mapped) | 4 |
| `jvm-only-replaced` | 4 |

Of the 62 declared original tests, 61 are mapped to C# tests with the same names and 1 belongs to the GraalVM metadata
test, which is not ported. The C# run executes 66 test cases, because the `@ValueSource` tests run once per input. The
module has no `@Deprecated` members.

## Namespaces and assembly

The project, folder and assembly are `Netty.NET.Handler.Codec.HAProxy`, the namespace root of the module (owner
decision 2026-11-19, see [codec-base-porting.md](codec-base-porting.md)); the tests use
`Netty.NET.Handler.Codec.HAProxy.Tests`. The package files sit at the project root.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.haproxy` | `Netty.NET.Handler.Codec.HAProxy` | Decodes an HAProxy proxy protocol header (see the link to `proxy-protocol.txt` in the source). |
| test `io.netty.handler.codec.haproxy` | `Netty.NET.Handler.Codec.HAProxy.Tests` | |

## Shared CLR adaptations

- **Enums with state.** `HAProxyCommand`, `HAProxyProtocolVersion`, `HAProxyProxiedProtocol` with its nested
  `AddressFamily` and `TransportProtocol`, and `HAProxyTLV.Type` carry fields or methods, so they are sealed classes with
  static readonly constants, as `Base64Dialect` in codec-base and the enums of codec-mqtt: a C# enum cannot define
  methods ([Enumeration types](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/enum)) and a
  Java enum class has no instances other than its constants
  ([JLS 8.9](https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html#jls-8.9)). `Name()`, `ToString()` and
  `Values()` stand for the `java.lang.Enum` members; equality stays reference based. `HAProxyProxiedProtocol` also has
  `ValueOf(string)`, because the source calls the implicit `valueOf(String)` of the enum when it decodes a v1 header;
  it throws `ArgumentException("No enum constant ...")` and `ArgumentNullException("Name is null")` as
  `Enum.valueOf` does.
- **Switch over enum constants.** A case label is a pattern, so a switch over the constants uses case guards,
  `case var _ when type == PP2_TYPE_ALPN:`
  ([selection statements](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/selection-statements)).
  The labels, their order, grouping and `default` are kept; a null value reaches `default` instead of the
  `NullPointerException` of a Java switch.
- **Accessor names that clash with nested types (CS0102).** `HAProxyTLV.type()`,
  `HAProxyProxiedProtocol.addressFamily()` and `HAProxyProxiedProtocol.transportProtocol()` would be methods named like
  the nested `Type`, `AddressFamily` and `TransportProtocol` classes, which C# rejects
  ([CS0102](https://learn.microsoft.com/dotnet/csharp/misc/cs0102); the Transport port meets the same clash for
  `EmbeddedChannel.builder()`). The nested types keep the Java names and the methods are `Type_()`,
  `AddressFamily_()` and `TransportProtocol_()`.
- **Exceptions.** `RuntimeException` and `Throwable` are `Exception`, `IllegalArgumentException` is
  `ArgumentException`, `NumberFormatException` of `Integer.parseInt` is the `FormatException`/`OverflowException`/
  `ArgumentNullException` of `int.Parse`, and `NullPointerException` from `checkNotNull` is `ArgumentNullException`
  (docs/codec-base-porting.md). The two `catch (Throwable)` blocks release the read TLVs and rethrow, so they need no
  `Signal` filter.
- **Reference counting.** `HAProxyTLV` extends `DefaultByteBufHolder` and narrows `copy()`/`duplicate()`/
  `retainedDuplicate()`/`replace()`/`retain()`/`touch()` with covariant overrides
  ([override](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/override)). `HAProxyMessage`
  extends `AbstractReferenceCounted`, whose `Retain()`/`Touch()`/`Release()`/`Release(int)` are virtual, as in Java
  (owner decision 2026-11-19); `retain()`/`touch()` override them with covariant `HAProxyMessage` results and
  `release()`/`release(int)` override them, each calling the base member as the source calls `super`. Like the Java
  `super.retain()`/`super.release()`, the Common `Retain()`/`Release()` update the count directly, so each records once.
- **Text.** `toString()` and exception texts keep the Java string conversion
  ([JLS 5.1.11](https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11)): `null` is written as
  `"null"`, booleans as `"true"`/`"false"`, numbers with the invariant culture.
- **Java `byte` (owner decision).** Type bytes, client bits and the version/command byte stay `byte`. Where the Java
  value depends on the sign, it goes through `sbyte`:
  - `HAProxyProtocolVersion.ValueOf(byte)` computes `verCmdByte & VERSION_MASK` on the sign-extended operands
    (`unchecked((sbyte) verCmdByte & (sbyte) VERSION_MASK)`), so the `unknown version: ` text matches the source;
  - `HAProxyProxiedProtocol.AddressFamily.ValueOf(byte)` masks with `(byte) 0xf0` and prints the masked value as the
    source does;
  - `HAProxyMessage.DecodeHeader` (`version 1 unsupported: 0x` + `Integer.toHexString(verCmdByte)`) widens the byte
    through `sbyte` to `int` before the `x` format, which prints a negative int in two's complement as `toHexString`
    does (`0xffffff..`);
  - `HAProxyTLV.ToString()` (`typeByteValue`) and `HAProxySSLTLV.ToString()` (`typeByteValue` and `client`), where
    `0xE0` prints `-32`.
  `getUnsignedByte()` is `GetByte()`, which is unsigned in the Buffer port.
- **Access.** Package-private members are `internal` (`HAProxyConstants`, `HAProxyTLV.TotalNumBytes()`/
  `ContentNumBytes()`, the four-argument `HAProxySSLTLV` constructor, `HAProxyMessage.DecodeHeader(...)`), reached by
  the tests through `InternalsVisibleTo` as the Java tests reach them through package access.
- **Nested classes.** The `HeaderExtractor` inner classes of `HAProxyMessageDecoder` read the decoder's fields
  implicitly in Java; C# nested classes have no enclosing instance, so the decoder is passed to their constructors
  ([nested types](https://learn.microsoft.com/dotnet/csharp/programming-guide/classes-and-structs/nested-types)).
- **Single decode.** `HAProxyMessageDecoder` overrides the virtual `ByteToMessageDecoder.IsSingleDecode()` of
  codec-base to return `true`, as the source overrides `isSingleDecode()` (owner decision 2026-11-19).
- **Encoder.** `HAProxyMessageEncoder` is `[Sharable]`. `writeCharSequence` is `WriteCharSequence(ReadOnlySpan<char>,
  Encoding)`, which reads a null string as empty; the source throws a `NullPointerException` for the null addresses of
  an `UNKNOWN` v1 message, so `encodeV1` checks them with `ObjectUtil.CheckNotNull` (`ArgumentNullException`).

## Not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.Handler.Codec.HAProxy.csproj` references codec-base, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |

## Test notes

- `@BeforeEach setUp()` of `HAProxyMessageDecoderTest` runs from the test class constructor.
- `buffer()` is `Unpooled.Buffer()`: inside the `Netty.NET` namespaces the simple name `Buffer` binds to the
  `Netty.NET.Buffer` namespace.
- Java byte literals below zero in `byte[]` initializers (`-55`, `-90`) are `unchecked((byte) -55)`, and char literals
  are cast to `byte`, which Java narrows implicitly. `ByteArrayOutputStream` is `MemoryStream` and the big-endian
  `ByteBuffer.putShort` is `BinaryPrimitives.WriteInt16BigEndian`.
- `@ParameterizedTest` with `@ValueSource(shorts|chars)` is `[Theory]` with `[InlineData]`.
- `testCloseOnInvalid` awaits the close `Task` for up to 5 seconds with `Task.WhenAny` where the source calls
  `awaitUninterruptibly(5000)`; `HAProxyIntegrationTest` awaits the Transport port's asynchronous bootstrap API where
  the source calls `sync()`, and its `CountDownLatch` is a `TaskCompletionSource`.
- `ByteBuf.array().length` of the copied ASCII buffers in `HaProxyMessageEncoderTest` is their readable length, because
  the Buffer port has no `array()` accessor.
- `assertEquals(n, size())` and `contains(...)` keep the source assertions under documented `xUnit2013`/`xUnit2017`
  pragmas.
