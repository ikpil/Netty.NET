# codec-mqtt porting

Netty.NET ports `netty-codec-mqtt` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Mqtt`, with its original tests in
`test/Netty.NET.Handler.Codec.Mqtt.Tests`. The [manifest](codec-mqtt-porting-manifest.json) records every pinned file
of `codec-mqtt/src` (50 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not
ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-mqtt
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files or recorded as the comment of a not-ported `@Deprecated` member, that every
declared JUnit test is mapped to a C# test of the same name (case may differ) or carries a recorded disposition, and the
facts and evidence of every unit that is not ported. It fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 39 |
| `verified` (tests whose every original method is mapped) | 7 |
| `jvm-only-replaced` | 4 |

Of the 108 declared original tests, 104 are mapped to C# tests with the same names, 3 are CLR-inapplicable (the
`packetId(short)` tests, see below) and 1 belongs to the GraalVM metadata test, which is not ported. The C# run executes
122 test cases, because the three `@EnumSource(MqttVersion.class)` and three `@MethodSource("data")` tests run once per
input.

## Namespaces and assembly

The project, folder and assembly are `Netty.NET.Handler.Codec.Mqtt`, the namespace root of the module (owner decision
2026-11-19, see [codec-base-porting.md](codec-base-porting.md)); the tests use `Netty.NET.Handler.Codec.Mqtt.Tests`.
The package files sit at the project root.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.mqtt` | `Netty.NET.Handler.Codec.Mqtt` | Encoder, decoder and different Message Types for MQTT. |
| test `io.netty.handler.codec.mqtt` | `Netty.NET.Handler.Codec.Mqtt.Tests` | |

## Shared CLR adaptations

- **Enums with state.** `MqttQoS`, `MqttMessageType`, `MqttVersion`, `MqttConnectReturnCode`,
  `MqttSubscriptionOption.RetainedHandlingPolicy`, `MqttProperties.MqttPropertyType` and the eight `MqttReasonCodes`
  enums carry fields and methods, so they are sealed classes with static readonly constants, as `Base64Dialect` in
  codec-base: a C# enum cannot define methods
  ([Enumeration types](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/enum)) and a Java enum
  class has no instances other than its constants
  ([JLS 8.9](https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html#jls-8.9)). `Name()`, `ToString()` and
  `Values()` stand for the `java.lang.Enum` members; equality stays reference based. `MqttDecoder.DecoderState` has no
  state and is a C# enum.
- **Switch over enum constants.** A case label is a pattern, so `switch (messageType) { case CONNECT: ... }` is a switch
  whose labels are case guards, `case var _ when messageType == MqttMessageType.CONNECT:`
  ([selection statements](https://learn.microsoft.com/dotnet/csharp/language-reference/statements/selection-statements)).
  The labels, their order, grouping and `default` are kept. The fall-through between the `MqttDecoder` states is
  `goto case` after each `try` statement.
- **Signals.** `Signal` is an `Exception` in Netty.NET.Common and a `java.lang.Error` in Java, so every
  `catch (Exception)` of `MqttDecoder` has a `when (cause is not Signal)` filter, as in codec-base.
- **Covariant results.** `variableHeader()`/`payload()` overrides use C# covariant return types
  ([override](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/override)). `MqttPublishMessage`
  implements `IByteBufHolder` with explicit interface members, as `DatagramPacket` in Transport: `MqttMessage` has no
  reference-counting members to override, and the Buffer port's `IByteBufHolder` re-declares `retain()`/`touch()`. The
  module declares no interface of its own that only narrows a parent member (owner decision 2026-11-19), and the one
  `new` member, `MqttProperty<T>.Value()`, hides the `object Value()` of the raw-type base (see **Raw types**), because
  `T` may be a value type and a covariant override needs a reference conversion.
- **Text.** `toString()` and exception texts keep the Java string conversion
  ([JLS 5.1.11](https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11)): `null` is written as
  `"null"`, booleans as `"true"`/`"false"`, numbers with the invariant culture, and a `java.util.List` as
  `[a, b]` (`AbstractCollection.toString()`).
- **Java `byte` (owner decision).** Reason codes, return codes and the protocol level stay `byte`. Where the Java text
  depends on the sign, the value is printed through `sbyte`:
  - `MqttPubReplyMessageVariableHeader.ToString()` and `MqttReasonCodeAndPropertiesVariableHeader.ToString()` (reason
    code, `0x87` prints `-121`);
  - `MqttReasonCodes` `valueOfHelper` (`unknown reason code: ` + b);
  - `MqttVersion.FromProtocolNameAndLevel` (the `don't match` message);
  - `MqttConnectPayload.ToString()` (`Arrays.toString(willMessage)`) and the `ArraysToString` helper of `MqttCodecTest`.
  `MqttConnectReturnCode.ValueOf` and `MqttSubAckPayload`/`MqttUnsubAckPayload` already mask with `& 0xFF`.
- **Primitive map (brief rule 7).** `MqttProperties` uses `Dictionary<int, MqttProperty>` for `IntObjectHashMap`: Java
  type arguments must be reference types
  ([JLS 4.5.1](https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.5.1)) and .NET specializes generic
  types for value types
  ([generics in the runtime](https://learn.microsoft.com/dotnet/csharp/programming-guide/generics/generics-in-the-run-time)).
  Dictionary enumeration order is undefined and `IntObjectHashMap` iterates in hash-slot order, so `listAll()` and the
  encoded property order may differ from the JVM; neither order is documented and the tests compare by property id.
  The owner kept `Dictionary` instead of porting `io.netty.util.collection` (2026-11-19); the Common manifest
  ([common-porting-manifest.json](common-porting-manifest.json)) records this order difference with the templates.
- **Raw types.** The raw `MqttProperty` is a non-generic abstract base `MqttProperty` (with `propertyId`, `PropertyId()`
  and an `object Value()`) from which `MqttProperty<T>` derives. `Collection<? extends MqttProperty>` and
  `List<? extends MqttProperty>` are the covariant `IReadOnlyCollection<MqttProperty>`/`IReadOnlyList<MqttProperty>`.
  `UserProperties` compares and hashes its list value element by element, as `java.util.List` does.
- **Boxed values.** `Integer`/`Short` elements are `int`/`short`, which cannot be null, so the null checks of the
  `Iterable<Integer>`/`Iterable<Short>` payload constructors have no counterpart; boxed builder fields are nullable value
  types.
- **Strict UTF-8.** The `CharsetDecoder` with `CodingErrorAction.REPORT` is `UTF8Encoding(false, true)`, which throws an
  `ArgumentException` for invalid bytes
  ([UTF8Encoding constructor](https://learn.microsoft.com/dotnet/api/system.text.utf8encoding.-ctor)).
  `nioBufferCount()`/`nioBuffer()` are `AsReadOnlySequence` and `IsSingleSegment`.
- **Access.** Package-private members are `internal`; `MqttDecoder.DecoderState` is public because it is the type
  argument of the public base class; the private `ConnAckBuilder` constructor and the fields of the decoder's private
  `Result<T>` are `internal`, because C# private members are not visible to the enclosing class. An internal
  `MqttDecoder.InvokeDecode` forwarder serves the tests that call the protected `decode(...)`.

## Deprecated members (owner decision 2026-11-16)

| Member | `@deprecated` text | Disposition |
| --- | --- | --- |
| `MqttConnectPayload(String, String, String, String, String)` | use `MqttConnectPayload(String, MqttProperties, String, byte[], String, byte[])` | not ported |
| `MqttConnectPayload.willMessage()` | use `willMessageInBytes()` | not ported |
| `MqttConnectPayload.password()` | use `passwordInBytes()` | not ported |
| `MqttMessageBuilders.ConnectBuilder.willMessage(String)` | use `willMessage(byte[])` | not ported |
| `MqttMessageBuilders.ConnectBuilder.password(String)` | use `password(byte[])` | not ported |
| `MqttMessageBuilders.PubAckBuilder/SubAckBuilder/UnsubAckBuilder.packetId(short)` | use `packetId(int)` | not ported |
| `MqttPublishVariableHeader.messageId()` | Use `packetId()` | not ported |
| `MqttTopicSubscription.topicName()` | use topicFilter | not ported |
| `MqttProperties.MqttPropertyType` | none (no `@deprecated` tag) | ported with `[Obsolete]` |

The Javadoc of each not-ported member is recorded in the manifest entry of its file (`omittedComments`). `MqttEncoder`
calls `TopicFilter()` where the source calls `topicName()`.

## Not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.Handler.Codec.Mqtt.csproj` references codec-base, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |

## Test notes

- Mockito is Moq: the mocked `ChannelHandlerContext`, `Channel` and `Attribute<MqttVersion>` are `Mock<T>` fields,
  `fireChannelRead` collects the decoded messages through a callback, and `verify`/`clearInvocations`/
  `verifyNoMoreInteractions` are `Verify`/`Invocations.Clear()`/`VerifyNoOtherCalls`.
- `MqttMessageBuildersPacketIdTest.testUnsubAckMessageIdAsShort`, `testSubAckMessageIdAsShort` and
  `testPubAckMessageIdAsShort` only exercise the not-ported `packetId(short)` overloads and are CLR-inapplicable; the
  `...AsInt` tests cover `packetId(int)` with the same inputs.
- `MqttConnectPayloadTest` and `MqttCodecTest` also use the deprecated String accessors and builder overloads next to
  other assertions; they are ported with the replacements, which the tests already assert.
- `@EnumSource(MqttVersion.class)` is `[MemberData]` over `MqttVersion.Values()`; `assertEquals(n, size())` keeps the
  source assertions under an `xUnit2013` pragma.
