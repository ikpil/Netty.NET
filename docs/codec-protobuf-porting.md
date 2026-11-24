# codec-protobuf porting

Netty.NET ports `netty-codec-protobuf` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Protobuf`, with its original tests in
`test/Netty.NET.Handler.Codec.Protobuf.Tests`. The [manifest](codec-protobuf-porting-manifest.json) records every
pinned file of `codec-protobuf/src` (11 files: 7 main and 2 test Java sources, `module-info.yml` and the GraalVM
`reflect-config.json`) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not ported.
Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-protobuf
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files, that every declared JUnit test is mapped to a C# test of the same name
(case may differ) or carries a recorded disposition, and the facts and evidence of every unit that is not ported. A
`library-not-ported` entry (owner decision 2026-11-23, introduced with
[codec-compression](codec-compression-porting.md)) records the library (name, Maven coordinates, Java packages), and
the checker requires the unit to import one of those packages. The checker fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 2 |
| `verified` (tests whose every original method is mapped) | 2 |
| `library-not-ported` | 4 |
| `jvm-only-replaced` | 3 |

All 16 declared original tests are mapped to C# tests with the same names and pass. No test is CLR-inapplicable, and
the module has no tests of the four message codecs that are not ported. The three `jvm-only-replaced` and four
`library-not-ported` entries carry 15 original comments.

## Owner decisions (2026-11-23)

- **Project.** The project, folder and assembly are `Netty.NET.Handler.Codec.Protobuf`, the namespace of the module's
  package, as for every codec module (owner decision 2026-11-19, see [codec-base-porting.md](codec-base-porting.md)).
- **Libraries without a BCL counterpart.** `ProtobufDecoder` and `ProtobufEncoder` need protobuf-java
  (`com.google.protobuf`), and `ProtobufDecoderNano` and `ProtobufEncoderNano` need protobuf-javanano
  (`com.google.protobuf.nano`); both are optional Maven dependencies of Netty. The .NET protobuf runtime is the
  separate NuGet package [Google.Protobuf](https://www.nuget.org/packages/Google.Protobuf) ("C# runtime library for
  Protocol Buffers"), not part of the BCL, and the owner keeps third-party packages out of the library projects. The
  four units are therefore `library-not-ported`, the rule codec-compression applies to lz4-java, zstd-jni and the
  others.
- **Varint32 framing.** `ProtobufVarint32FrameDecoder` and `ProtobufVarint32LengthFieldPrepender` encode and decode
  the base 128 varint length themselves. protobuf-java's `CodedInputStream`/`CodedOutputStream` and protobuf-javanano's
  `CodedInputByteBufferNano`/`CodedOutputByteBufferNano` appear only in their imports and Javadoc `@see` links, so
  both classes are ported. The `@see` text is kept verbatim, and the imports have no C# `using` (stated in a
  `// CLR:` comment on each class).

## Namespaces and assembly

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.protobuf` | `Netty.NET.Handler.Codec.Protobuf` | Encoder and decoder which transform a [Google Protocol Buffers](https://github.com/google/protobuf) `com.google.protobuf.Message` and `com.google.protobuf.nano.MessageNano` into a `io.netty.buffer.ByteBuf` and vice versa. |
| test `io.netty.handler.codec.protobuf` | `Netty.NET.Handler.Codec.Protobuf.Tests` | |

The namespace holds only the two varint32 framing codecs; the message codecs that the package summary describes are
not ported (see below). The files sit at the project root.

## Shared CLR adaptations

- **Handler signatures.** `decode(ctx, in, out)` is `Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object>
  @out)` and `encode(ctx, msg, out)` is `Encode(IChannelHandlerContext ctx, ByteBuf msg, ByteBuf @out)`, without
  throws clauses. `@Sharable` is `[Sharable]`, and `super(ByteBuf.class)` is `base(typeof(ByteBuf))`, as in
  codec-base ([codec-base-porting.md](codec-base-porting.md)).
- **Package access.** The package-private `readRawVarint32`, `writeRawVarint32` and `computeRawVarint32Size` are
  `internal`; `InternalsVisibleTo` opens them to the test assembly, which calls them directly as the Java tests do.
- **Messages.** `checkPositive` is the Common `ObjectUtil.CheckPositive` (`ArgumentException`). The numbers in the
  `CorruptedFrameException` and `TooLongFrameException` messages use the invariant culture, because `Int32.ToString()`
  uses "the NumberFormatInfo object of the current culture"
  ([Int32.ToString](https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring)).
- **Integer literals.** Java hexadecimal `int` literals "can represent a positive, zero, or negative integer"
  ([JLS 3.10.1](https://docs.oracle.com/javase/specs/jls/se17/html/jls-3.html#jls-3.10.1)), so `0x80808080` and
  `0xffffffff` are negative `int` values. A C# literal without suffix takes "the first of the following types in which
  its value can be represented: int, uint, long, ulong"
  ([integral numeric types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types)),
  so both are `uint`, and `int & uint` would be computed as `long`. They are written `unchecked((int) 0x80808080)` and
  `unchecked((int) 0xffffffff)`, the same 32 bits.
- **Bit operations.** `Integer.numberOfTrailingZeros` is `BitOperations.TrailingZeroCount(int)`
  ([Integer](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html),
  [TrailingZeroCount](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.bitoperations.trailingzerocount?view=net-10.0)).
  `>>` is an arithmetic and `>>>` a logical shift of an `int` in both languages
  ([JLS 15.19](https://docs.oracle.com/javase/specs/jls/se17/html/jls-15.html#jls-15.19),
  [bitwise and shift operators](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators)),
  so the shifts keep the source text, including `value >>>= 7`.
- **Overflow.** In `readRawVarint32`, `firstOneOnStop - 1` overflows when `firstOneOnStop` is `0x80000000` (a
  four-byte varint). Java keeps "the low-order bits"
  ([JLS 15.18.2](https://docs.oracle.com/javase/specs/jls/se17/html/jls-15.html#jls-15.18.2)); C# does the same in
  the default unchecked context, which applies because no project sets `CheckForOverflowUnderflow`
  ([checked and unchecked](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/checked-and-unchecked)).

## Byte values

`ByteBuf.getByte`/`readByte` return `System.Byte` (owner decision). The varint decoder tests the continuation bit
through the sign of the Java `byte` (-128 to 127,
[JLS 4.2.1](https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1)), so these places read the byte
as `sbyte` and keep the Java result. Between types of the same size "the source value is treated as a value of the
destination type"
([numeric conversions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions)),
and `sbyte` operands of the bitwise and shift operators are converted to `int`, as Java widens a `byte` with sign
extension ([JLS 5.6](https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.6)).

| Place | Java | C# | Test that fails without it |
| --- | --- | --- | --- |
| `ProtobufVarint32FrameDecoder.readRawVarint40` | `byte lastByte = buffer.getByte(...)`; `lastByte < 0` rejects a set continuation bit in the fifth byte; `lastByte << 28` | `sbyte lastByte = (sbyte) buffer.GetByte(...)` | `testReadRawVarint32RejectsSixByteVarint` |
| `ProtobufVarint32FrameDecoder.readRawVarint24` | `byte tmp = buffer.readByte()`; `tmp >= 0` ends the varint; `tmp & 127`, `tmp << 7`, `tmp << 14` | each of the three reads is `(sbyte) buffer.ReadByte()` | `testReadRawVarint32WaitsForIncompleteVarint` |

The "fails without it" column was checked by building the decoder with the unsigned byte in a scratch run: an
unsigned `lastByte` is never negative, so a sixth varint byte is accepted; an unsigned `tmp` is never negative, so an
incomplete one-, two- or three-byte varint moves the reader index.

The other sign-related places keep the source code:

- `readRawVarint32` reads four bytes with `GetIntLE`, an `int` in both ports; only the `0x80808080` literal needs the
  cast described above.
- `ProtobufVarint32LengthFieldPrepender.writeRawVarint32` shifts with `>>>=`, the same logical shift, so a negative
  `int` is written as a five-byte varint as in Java. `writeByte(int)` ignores the 24 high-order bits in both ports:
  the Buffer port keeps that Javadoc on `ByteBuf.WriteByte`, and `WriteByteCore` stores `unchecked((byte) value)`.
- In the tests, the Java `byte` literal `-2` is `unchecked((byte) -2)` (0xFE, the same bits; "Constant expressions are
  evaluated by default in a checked context"), and `(byte) 0x80` and `(byte) (0x8A & 0xFF)` are the same bit patterns
  in both languages.

## Not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `ProtobufDecoder` | `library-not-ported` (protobuf-java, `com.google.protobuf:protobuf-java:3.25.9`, optional) | Decodes a `ByteBuf` into a protobuf `MessageLite` through its parser or `ExtensionRegistry`. |
| `ProtobufEncoder` | `library-not-ported` (protobuf-java) | Encodes a `MessageLite`/`MessageLite.Builder` into a `ByteBuf`. |
| `ProtobufDecoderNano` | `library-not-ported` (protobuf-javanano, `com.google.protobuf.nano:protobuf-javanano:3.1.0`, optional) | Decodes a `ByteBuf` into a `MessageNano`. |
| `ProtobufEncoderNano` | `library-not-ported` (protobuf-javanano) | Encodes a `MessageNano` into a `ByteBuf` with `CodedOutputByteBufferNano`. |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor (requires `io.netty.common`, `io.netty.buffer`, `io.netty.transport`, `io.netty.codec`, `protobuf.javanano`, `com.google.protobuf`); `Netty.NET.Handler.Codec.Protobuf.csproj` references codec-base only, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |

The manifest keeps every original comment of the four codecs and records the library, the reason and the evidence.
The module has no `NativeImageHandlerMetadataTest`.

## Test notes

- xUnit creates one test class instance per test, so `@BeforeEach setUp()` is a private method called from the
  constructor ([shared context](https://xunit.net/docs/shared-context)); it is private because xUnit1013 reports public
  non-test methods on a test class.
- `ProtobufVarint32FrameDecoderTest` uses `XunitAssertMessages.AssertM` for the JUnit assertions with a message. The
  thrown exceptions have the exact expected types, so `assertThrows` is `Assert.Throws`.
- `buffer()` is `Unpooled.Buffer()`, qualified because inside `Netty.NET.*` the simple name `Buffer` binds to the
  namespace `Netty.NET.Buffer` (CS0118 in a scratch build).
- The test messages and `String.valueOf(0x10000000)` use invariant-culture text, as the decoder's
  `TooLongFrameException` message does.
- `ProtobufVarint32LengthFieldPrependerTest.testSize2Varint` keeps the source argument order of
  `assertEquals(actual, expected)`; the `final int` locals are `const int`.
