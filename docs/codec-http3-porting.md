# codec-http3 porting

Netty.NET ports `netty-codec-http3` of Netty 4.2.19.Final (pinned commit `64cc10f38ea5f5bd7eae48507817c66680d0afdc`)
to `src/Netty.NET.Handler.Codec.Http3`, with its original tests in `test/Netty.NET.Handler.Codec.Http3.Tests`. The
[manifest](codec-http3-porting-manifest.json) records every pinned file of `codec-http3/src` (119 files: 79 main and 39
test Java sources and `module-info.yml`) with its C# counterpart, its CLR adaptations and evidence, the reason it is not
ported, or what it waits for. No entry is `pending`. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tools/Test-CodecPortingManifest.ps1 -Module codec-http3"
```

The checker verifies the file inventory and source hashes, that every original comment of a ported unit is present in
its C# file (or, for a `@Deprecated` member that is not ported, recorded with the member), that every declared JUnit
test of a ported test file is mapped to a C# test of the same name (case may differ), the facts and evidence of every
unit that is not ported, and for every deferred unit its blocking imports (recomputed from the source), the deferred
types it names, its declared tests and the count of its comments. It also checks that no ported unit imports a
blocking type or names a deferred type of its package. Since 2026-11-27 nothing is deferred: codec-classes-quic is
ported on System.Net.Quic ([codec-classes-quic-porting.md](codec-classes-quic-porting.md)), and every unit of this
module is ported on it.

| Status | Files |
| --- | --- |
| `clr-adapted` | 85 (every main source except the 2 JVM-only ones, the test helpers, adapted tests and the 3 example programs) |
| `verified` (tests whose every original method is mapped) | 31 |
| `jvm-only-replaced` | 3 (`module-info.yml`, `package-info.java`, `NotNullByDefault.java`) |

Of the 362 declared original tests, 359 are mapped to C# tests with the same names and 3 are CLR-inapplicable with
their reasons (`testDeprecatedMethodsStillWork` exercises the deprecated `get` that is not ported, `testSpliterator`
needs `java.util.Spliterator`, `testNullValueRejected` passes null where C# takes a `long`).

## Staging

1. **First pass (2026-11-27): the QUIC-free closure.** Before codec-classes-quic had a C# project, only the 21 main
   units that need no QUIC type, directly or through another unit, were ported (headers, frame interfaces, error codes,
   QPACK header field, Huffman decoder, sensitivity detector, `HttpConversionUtil`), with `HttpConversionUtilTest`,
   `QpackSensitivityDetectorTest` and the abstract `Http3FrameTypeValidatorTest`.
2. **Second pass (2026-11-27): the main sources on the QUIC API.** codec-classes-quic stage 1 ported the QUIC API layer
   to `src/Netty.NET.Handler.Codec.Quic` ([codec-classes-quic-porting.md](codec-classes-quic-porting.md)): `IQuicChannel`,
   `IQuicStreamChannel`, `QuicStreamType`, `IQuicStreamFrame`, `QuicException`, `QuicStreamChannelBootstrap` and so on.
   The 56 main units that waited for it are ported on that API: `Http3CodecUtils` (the hub of the closure), the frame
   types and their default implementations, `Http3Settings`, `Http3FrameCodec`, QPACK, the frame-type validation, the
   connection, control, unidirectional and push stream handlers, the request stream handlers and the HTTP/1.x
   conversion.
   - **`Http3` waits for stage 2 in three members.** `newQuicServerCodecBuilder()`, `newQuicClientCodecBuilder()` and
     the private `configure(builder)` need `QuicServerCodecBuilder`, `QuicClientCodecBuilder` and `QuicCodecBuilder`,
     which stage 2 adds (they are `pending` in the codec-classes-quic manifest). Their Javadoc stays in place in
     `Http3.cs` with a `// CLR:` note, so the entry is `clr-adapted` with every original comment; the three members are
     added with the builders. Everything else of `Http3` is ported (the attribute accessors, `newRequestStream`,
     `newRequestStreamBootstrap`, `supportedApplicationProtocols` and the constants).
   The three `Http3` builder members were added once codec-classes-quic stage 2 ported the builders.
3. **Tests and examples (2026-11-27).** The 36 test sources are ported (the in-memory `EmbeddedQuicChannel` and
   `EmbeddedQuicStreamChannel` helpers implement the QUIC interfaces over `EmbeddedChannel`), and the example programs
   and `Http3SpecTestServer` run on the System.Net.Quic implementation; see [Test results](#test-results).

## Namespaces and assembly

The project, folder and assembly are `Netty.NET.Handler.Codec.Http3`, the namespace of the module's package, as for
every codec module (owner decision 2026-11-19, see [codec-base-porting.md](codec-base-porting.md)). The project
references `Netty.NET.Handler.Codec.Http` (which brings codec-compression, handler, codec-base, Transport, Resolver,
Buffer and Common) and `Netty.NET.Handler.Codec.Quic`, whose `InternalsVisibleTo` opens its internals to this assembly
and its tests; `InternalsVisibleTo` opens this assembly's internals to `Netty.NET.Handler.Codec.Http3.Tests` only. Inside
`Netty.NET.Handler.Codec.*` namespaces the simple name `Quic` is the namespace `Netty.NET.Handler.Codec.Quic`, so code
here that needs the class `Quic` qualifies or aliases it (no ported unit uses it). The test project references the
module and the xUnit v3 packages that the other codec test projects use (no Moq).

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.http3` | `Netty.NET.Handler.Codec.Http3` (project root) | `<a href="https://tools.ietf.org/html/draft-ietf-quic-http-32">HTTP/3</a> implementation.` |
| test `io.netty.handler.codec.http3` | `Netty.NET.Handler.Codec.Http3.Tests` (project root) | |
| test `io.netty.handler.codec.http3.example` | `Netty.NET.Handler.Codec.Http3.Example` in `test/Netty.NET.Handler.Codec.Http3.Examples` (an executable with a CLR-only `ExampleLauncher`, as `src/Netty.NET.Example`; `Http3SpecTestServer` is there too, in the root namespace) | |

## Shared CLR adaptations

The adaptations follow the codec-http2 port ([codec-http2-porting.md](codec-http2-porting.md), Shared CLR
adaptations); each C# file states its own in a `// CLR:` comment, and each manifest entry repeats the type-level one as
its review scope.

| Java | C# | Notes |
| --- | --- | --- |
| Interfaces | `I`-prefixed interfaces (`IHttp3Headers`, `IHttp3DataFrame`, `IHttp3SettingsFrame`, `IQpackSensitivityDetector`, ...), nested ones too (`Http3Settings.INonStandardHttp3SettingsValidator`, `Http3FrameCodec.IHttp3FrameCodecFactory`, `QpackEncoder.Indices.IIndexConsumer`) | Interface constants are static readonly interface fields and their lambdas or anonymous classes private nested classes (`NO_VALIDATION`, `NO_STATE`, `NEVER_SENSITIVE`, `ALWAYS_SENSITIVE`); static and default methods are static and default interface members. A default method that overrides a base interface method (`type()` of the frame interfaces) is an explicit default member (`long IHttp3Frame.Type()`), reached through an interface reference. Re-declarations that only narrow a return type (`Http3DataFrame.copy()` ... `touch(Object)`, `Http3UnknownFrame`) or repeat a member for its Javadoc are inherited, with the comment kept in place (owner decision 2026-11-19); the classes narrow them with covariant overrides. |
| QUIC types | codec-quic's `IQuicChannel`, `IQuicStreamChannel`, the C# enum `QuicStreamType`, `IQuicStreamFrame`, `QuicException`, `QuicStreamChannelBootstrap` | [codec-classes-quic-porting.md](codec-classes-quic-porting.md), API shapes: `createStream(type, handler)` is `CreateStreamAsync`, `close(applicationClose, error, reason)` `CloseAsync(bool, int, ByteBuf)`, `shutdownOutput(error)` `ShutdownOutputAsync(int)`, `streamId()`/`type()` methods, `parent()` the `Parent` property (cast to `IQuicChannel`). `QuicStreamChannel.SHUTDOWN_OUTPUT`, which codec-quic does not port, is a listener that calls `ShutdownOutputAsync()`. |
| Package-private types and members | `internal` | Fields that other classes read keep their Java names (`Http3ConnectionHandler.localControlStreamHandler`, `Http3ErrorCode.code`, `QpackHeaderField.name`). A package-private abstract method that a subclass widens to `protected` (`initBidirectionalStream`/`initUnidirectionalStream`) or that another instance calls (`Http3PushStreamServerInitializer.initPushStream`) is `protected internal` (an override cannot change accessibility; CS1540). |
| Enums with fields and methods (`Http3ErrorCode`, `Http3SettingIdentifier`, `Http3Headers.PseudoHeaderName`, `HttpConversionUtil.ExtensionHeaderNames`) | sealed classes with static readonly instances | Compared by reference (Java `switch` statements are if/else chains); `Values()` stands for `values()` and `ToString()` is the constant name. Simple enums (`Http3HeadersSink.HeaderType`, `Http3RequestStreamEncodeStateValidator.State`, `QpackUtil.IndexType`) are C# enums. |
| `ChannelFuture`, `Future<T>`, `ChannelPromise`, `Promise<T>` | `Task`, `Task<T>`, `TaskCompletionSource`, `TaskCompletionSource<T>` | Methods that return a future take the `Async` suffix (`Http3.NewRequestStreamAsync`, `Http3ServerPushStreamManager.NewPushStreamAsync`); the Promise overloads fold into the Task overload, as in `IQuicChannel`. The codec-http2 helpers are copied into `Http3CodecUtils` because codec-http3 does not depend on codec-http2: `NewPromise()` (no `RunContinuationsAsynchronously`), `Write`/`WriteAndFlush(ctx, msg, promise)`, `Cascade` (the `PromiseNotifier` mapping), `AddListener(ctx, future, listener)` (a continuation on Common's `EventExecutorTaskScheduler` over `ctx.Executor`, codec-http2's Listener timing) and `Cause(future)`. A public `write(ctx, msg, promise)` is `WriteAsync(ctx, msg, CancellationToken)`, which creates the promise, runs the Java body and returns the promise's Task; the internal helpers keep a `TaskCompletionSource` parameter. There is no void promise: `ctx.voidPromise()` is a promise whose Task is not observed and the `isVoid()` branches are left out. `PromiseCombiner` is a `List<Task>` with `Task.WhenAll` and `Cascade`. |
| `closeOnFailure(future)`, `ChannelFutureListener.CLOSE_ON_FAILURE` | `Http3CodecUtils.CloseOnFailure(IChannel, Task)` | A Task has no `channel()`, so the caller passes the channel of the write (`CloseOnFailure(ctx.Channel, ctx.WriteAndFlushAsync(buf))`); the continuation is synchronous, as `Channel.close()` may be called from any thread. |
| `BiConsumer`, `Runnable`, `Supplier`, `BooleanSupplier`, `LongFunction`, `BiFunction`, `GenericFutureListener<Future<X>>` | `Action<,>`, `Action`, `Func<T>`, `Func<bool>`, `Func<long, T>`, `Func<,,>`, `Action<Task<X>>` | A class that implements the functional interface keeps its method (`Http3HeadersSink.Accept`, `ReadResumptionListener.Run`, `OperationComplete`) and is passed as a method group, as the Common `ByteProcessor` mapping keeps `process(byte)`. Method references are method groups (`localControlStreamHandler.IsGoAwayReceived`). |
| `ByteProcessor` | `Func<byte, bool>` | Delegates created once (`QpackHuffmanDecoder`, `QpackHuffmanEncoder`, `DefaultHttp3Headers`, `HttpConversionUtil`). |
| `io.netty.util.collection` maps, `ConcurrentMap`, `ArrayDeque` | `Dictionary<long, V>`/`Dictionary<int, V>`, a `Dictionary` under a lock, `Queue<T>` | Owner decision 2026-11-19 for the primitive maps (entries enumerate in insertion order); `computeIfAbsent` is `TryGetValue` plus `Add`. `Http3ServerPushStreamManager`'s `ConcurrentMap` is a `Dictionary` under a lock so that `compute`/`computeIfPresent` stay atomic, and its `AtomicLongFieldUpdater` a `long` with `Volatile.Read`/`Interlocked.Increment`. |
| `AttributeKey<Boolean>` | `AttributeKey<object>` holding a boxed `bool` | `AttributeKey<T>` needs a reference type (as handler-ssl-ocsp and resolver-dns). |
| Checked exceptions, `RuntimeException`, `Throwable`, `Error` | `Exception`, `UnreachableException`/`InvalidOperationException` | The `throws` clauses have no counterpart; `catch (X e) { throw e; }` is `throw;`; `IllegalArgumentException` is `ArgumentException`, `IllegalStateException` `InvalidOperationException`, `UnsupportedOperationException` `NotSupportedException`. `QpackException`'s `writableStackTrace = false` keeps the first stack trace, as `StacklessHttp2Exception` does. `Math.toIntExact` is a checked conversion (`OverflowException` for `ArithmeticException`). |
| `Objects.hash`, `Objects.equals`, `getClass() != o.getClass()` | spelled out, `object.Equals(a, b)`, `GetType() != o.GetType()` | `Objects.hash(id)` is `31 + Long.hashCode(id)` and so on, so the hash codes keep the Java values (as codec-quic's `QuicStreamAddress`). |
| `String.format`, string concatenation, `Long.toHexString` | concatenation with the Java text | Numbers print with the invariant culture; a CharSequence that may be null prints `null`; `toHexString` is the lowercase `"x"` format (two's complement for a negative value, as Java's unsigned text). |
| `@Deprecated` members whose `@deprecated` text names a replacement | not ported | Owner decision 2026-11-16 ([codec-http-porting.md](codec-http-porting.md)): the four `Http3SettingsFrame` constants `HTTP3_SETTINGS_*` (replacement `Http3SettingIdentifier`), `Http3SettingsFrame.get(long)`/`getOrDefault(long, long)` and `DefaultHttp3SettingsFrame.get(long)`/`put(long, Long)` (replacement `settings()`). Their 9 comments are deprecated-member dispositions in the manifest. `Http3SettingsFrame.put(long, Long)` has a `@deprecated` tag but no `@Deprecated` annotation and is ported (without `[Obsolete]`); `DefaultHttp3SettingsFrame.copyOf` calls it through the interface. |
| `@Nullable`/`@NotNull` (org.jetbrains.annotations, javax.annotation), `@NotNullByDefault` | none | The projects do not enable the nullable annotation context (see JVM-only units). |
| `assert` | `Debug.Assert` | Compiled out of a Release build. |
| JUnit (`@Test`, `@ParameterizedTest` with `@MethodSource`/`@ValueSource`, `assertThrows`) | xUnit v3 `[Fact]`, `[Theory]` with `[MemberData]`/`[InlineData]`, `Assert.ThrowsAny` | `List<String>` arguments are `string[]`; the abstract `Http3FrameTypeValidatorTest` keeps `newValidator()` `private protected`, because it returns the internal validator interface (CS0050). |

## Areas

### Frames, settings and the frame codec

`Http3CodecUtils`, the frame interfaces `IHttp3DataFrame`, `IHttp3HeadersFrame`, `IHttp3SettingsFrame`,
`IHttp3GoAwayFrame`, `IHttp3MaxPushIdFrame`, `IHttp3CancelPushFrame`, `IHttp3PushPromiseFrame` with their `Default*`
implementations, `DefaultHttp3UnknownFrame`, `Http3Settings` and `Http3FrameCodec`.

- **Http3CodecUtils** is an internal static class. The two `numBytesForVariableLengthInteger` overloads keep their
  name: the `byte` overload is the one for a CLR byte (`in.GetByte(in.ReaderIndex)`; only bits 6 and 7 are read, so the
  sign of Java's byte does not matter), callers pass a `long` to the other. `getConnectionHandlerOrClose` is
  `Pipeline.Get<Http3ConnectionHandler>()`. It holds the CLR-only promise helpers (see the shared table).
- **Frames.** `DefaultHttp3DataFrame` and `DefaultHttp3UnknownFrame` derive from the Buffer port's
  `DefaultByteBufHolder` with covariant overrides returning `IHttp3DataFrame`/`IHttp3UnknownFrame`.
- **Http3Settings** wraps a `Dictionary<long, long>` (Java: `LongObjectHashMap<Long>`, like codec-http2's
  `Http2Settings`), so it enumerates in insertion order; `Equals`/`GetHashCode` keep `LongObjectHashMap`'s content
  equality (size and values per key; `size ^` the XOR of `Long.hashCode(key)`). A `Long` parameter is a `long` (the
  `checkNotNull(value)` has no counterpart) and a `Long`/`Boolean` result that may be null is `long?`/`bool?`.
  `Http3SettingIdentifier.valueOf(String.valueOf(identifier))` in a message is the identifier itself.
- **Http3FrameCodec** is a codec-base `ByteToMessageDecoder` and an `IChannelOutboundHandler`. The factory lambda of
  `newFactory` is the private class `CodecFactory`; `ReadResumptionListener` and `WriteResumptionListener` keep `run()`
  and `operationComplete(future)` and are registered with `QpackDecoder.Decode` (`whenDecoded`) and
  `QpackAttributes.WhenDecoderStreamAvailable`/`WhenEncoderStreamAvailable`. The `BiFunction` writers of
  `writeDynamicFrame` are `Func<T, ByteBuf, bool>` lambdas.

### QPACK

`QpackUtil`, `QpackStaticTable`, `QpackDecoderDynamicTable`, `QpackEncoderDynamicTable`, `QpackHuffmanEncoder`,
`QpackDecoder`, `QpackEncoder`, `QpackAttributes`, `QpackDecoderHandler` and `QpackEncoderHandler`, next to the
first-pass `QpackHeaderField`, `QpackHuffmanDecoder`, `QpackException`, `IQpackDecoderStateSyncStrategy` and
`IQpackSensitivityDetector`. They follow codec-http2's HPACK port.

- **QpackUtil**: the prefix masks are CLR bytes with Java's low-eight-bit casts; `Math.floorDiv` is the CLR-only
  `QpackUtil.FloorDiv`; the Huffman tables are copied with their comments.
- **QpackStaticTable**: `Arrays.asList` is a read-only array list; `CharSequenceMap<List<Integer>>` is
  `CharSequenceMap<List<int>>` with `UnsupportedValueConverter`.
- **Dynamic tables**: `Arrays.fill`/`System.arraycopy` are `Array.Clear`/`Array.Copy`; the encoder table's
  `HeaderEntry` derives from `QpackHeaderField`; `toIntExact` is a checked conversion.
- **QpackDecoder/QpackEncoder**: the header sink is an `Action<ICharSequence, ICharSequence>` (callers pass
  `Http3HeadersSink.Accept`), `whenDecoded` an `Action`; the blocked-stream map is a `Dictionary<int, List<...>>` and the
  encoder's section trackers a `Dictionary<long, Queue<Indices>>`; the decoder and encoder stream writes are
  `Http3CodecUtils.CloseOnFailure(stream, stream.WriteAndFlushAsync(...))`. The headers are enumerated as codec-base
  `IEntry` values. `Indices.IndexConsumer` is `Indices.IIndexConsumer`, and the method references passed to it are a
  private class around a method group.
- **QpackAttributes**: the stream promises are `TaskCompletionSource<IQuicStreamChannel>`; the listeners of
  `whenEncoderStreamAvailable`/`whenDecoderStreamAvailable` are `Action<Task<IQuicStreamChannel>>` continuations on
  `EventExecutorTaskScheduler` over the channel's event loop, as listeners of `eventLoop().newPromise()` are notified.
- **QpackDecoderHandler/QpackEncoderHandler** are codec-base `ByteToMessageDecoder`s; the "Unknown ... instruction" texts
  print Java's signed byte through `(sbyte)`; the encoder handler takes `long? maxTableCapacity`.

### Frame-type validation

`IHttp3FrameTypeValidator` (first pass), `Http3ControlStreamFrameTypeValidator`, `Http3PushStreamFrameTypeValidator`,
`Http3RequestStreamFrameTypeValidator`, `Http3FrameValidationUtils`, `Http3FrameTypeInboundValidationHandler`,
`Http3FrameTypeOutboundValidationHandler`, `Http3FrameTypeDuplexValidationHandler`, `Http3RequestStreamValidationHandler`,
`Http3RequestStreamValidationUtils`, `Http3RequestStreamEncodeStateValidator`, `Http3RequestStreamDecodeStateValidator`,
`Http3PushStreamClientValidationHandler` and `Http3PushStreamServerValidationHandler`.

- `Class<T>` is `Type` (`isInstance` is `IsInstanceOfType`); the validation handlers keep the constructor argument
  (`typeof(IHttp3ControlStreamFrame)`), as Java passes the class for its erased `T`.
- The public `final` `channelRead(ctx, Object)`/`write(ctx, Object, promise)` are sealed or non-virtual; the
  package-private overloads `channelRead(ctx, T)`, `readFrameDiscarded`, `write(ctx, T, promise)` and
  `writeFrameDiscarded` are `internal virtual` members, the write ones with a `TaskCompletionSource`.
- `Http3RequestStreamEncodeStateValidator.evaluateFrame` returns `State?` (null for an unexpected frame); its `Error` is
  `UnreachableException`. As in Java, the promise of an unexpected frame is left incomplete after the connection error.
- `HttpUtil.normalizeAndGetContentLength` is codec-http's `NormalizeAndGetContentLength`; the input-shutdown events are
  compared by reference with Transport's `INSTANCE`s.

### Connection, control, unidirectional and push stream handlers

`Http3ConnectionHandler`, `Http3ClientConnectionHandler`, `Http3ServerConnectionHandler`,
`Http3ControlStreamInboundHandler`, `Http3ControlStreamOutboundHandler`, `Http3UnidirectionalStreamInboundHandler` with
its client and server subclasses, `Http3RequestStreamInitializer`, `Http3PushStreamClientInitializer`,
`Http3PushStreamServerInitializer`, `Http3ServerPushStreamManager`, `Http3RequestStreamInboundHandler` and `Http3`.

- The control stream handlers derive from the validation handlers above. Their `Long` ids (`receivedGoawayId`,
  `receivedMaxPushId`, `sentMaxPushId`, `sendGoAwayId`) are `long?`. `createStream(...).addListener(...)` is
  `Http3CodecUtils.AddListener` over the `Task<IQuicStreamChannel>` of `CreateStreamAsync`. `java.nio`'s
  `ClosedChannelException` is Transport's `ClosedChannelException` with Common's `UnknownStackTrace`.
- `Http3UnidirectionalStreamInboundHandler` decodes the stream type with the `byte` overload of
  `numBytesForVariableLengthInteger`; its handler factories are `Func<IChannelHandler>`/`Func<long, IChannelHandler>`.
- The initializers derive from Transport's `ChannelInitializer<IQuicStreamChannel>`; `Http3RequestStreamInitializer`
  keeps `protected sealed override InitChannel` and `protected abstract InitRequestStream`, and `Http3`'s anonymous
  initializer is a private nested class.
- `Http3ServerPushStreamManager.newPushStream(pushId, handler[, promise])` is `NewPushStreamAsync` returning
  `Task<IQuicStreamChannel>` (with a `Func<QuicStreamChannelBootstrap, QuicStreamChannelBootstrap>` overload for the
  bootstrap configurator); its anonymous handlers are nested classes.
- `Http3RequestStreamInboundHandler` keeps the typed `channelRead` overloads next to the sealed `ChannelRead(ctx,
  object)`; its `QuicException` is codec-quic's, not System.Net.Quic's.

### HTTP/1.x conversion

`HttpConversionUtil` (first pass) and `Http3FrameToHttpObjectCodec`, which mirrors codec-http2's
`Http2StreamFrameToHttpObjectCodec` port: the `@Deprecated` `DefaultLastHttpContent(ByteBuf, boolean)` (not ported in
codec-http) is the `HttpHeadersFactory` constructor with `trailersFactory().withValidation(validateHeaders)`, which the
deprecated constructor calls in Java; `WriteAsync` creates the promise with `NewPromise()`, the `PromiseCombiner` is a
`List<Task>` that codec-base's `ExecutorPromiseCombiner.Combine` completes on the event loop (see Test results), and
`SHUTDOWN_OUTPUT` is an `AddListener` that calls `ShutdownOutputAsync()`.
`HttpConversionUtil`'s `java.net.URI` use is the private `CreateUri`/`UriScheme`/`UriPort`/`UriAuthority` helpers of
codec-http2's port (`System.Uri` canonicalizes).

## JVM-only units

| Unit | Status | Reason |
| --- | --- | --- |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.Handler.Codec.Http3.csproj` references codec-http and codec-quic, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments or attributes; the summary is in the namespace table. |
| `NotNullByDefault.java` | `jvm-only-replaced` | A JSR-305 `@TypeQualifierDefault` package annotation with class retention, read by Java static-analysis tools only. Its C# counterpart would be the nullable annotation context, a compiler setting that the Netty.NET projects do not enable; no placeholder attribute is created. |

## Test results

Windows 11 (10.0.26300), .NET 10.0.11, 2026-11-27, Debug:

| Project | Cases | Result |
| --- | --- | --- |
| `Netty.NET.Handler.Codec.Http3.Tests` | 839 | 810 passed, 29 skipped (the cases JUnit aborts with `assumeTrue`, `Assert.SkipUnless` here), 0 failed; three repeated runs |

The 839 cases are the 837 cases of the ported tests and 2 of the CLR supplemental `Http3InteropTest`.
`Http3FrameToHttpObjectCodecTest.MultipleFramesInFin` and `Http3InteropTest` run over System.Net.Quic on loopback.

- **Event-loop completion of combined writes.** Ten `Http3FrameToHttpObjectCodecTest` cases first failed at
  `assertTrue(ch.isOutputShutdown())`: the counterpart of `PromiseCombiner` completed the promise through
  `Task.WhenAll`, whose completion runs where the last write Task completes, and the Transport write Tasks run their
  continuations asynchronously, so the shutdown listener ran after the write call had returned. The fix listens to each
  write on `ctx.Executor` and completes the promise with the last one, as the Java combiner does on its executor. The
  cases have passed since. It was first `Http3CodecUtils.Combine`. It is now codec-base's `ExecutorPromiseCombiner`,
  which codec-base, codec-http and codec-http2 share
  ([common-task-composition.md](common-task-composition.md), Executor affinity of a combined write).
- **End to end (loopback, System.Net.Quic).** `Http3ServerExample` with `Http3ClientExample` (`-Dport`, a CLR-only
  property since the client takes no argument) prints `Hello World!`. `Http3SpecTestServer` prints
  `H3SPEC_SERVER_READY` and answers the same client. .NET's own HTTP/3 client (`HttpClient`,
  `HttpVersion.Version30`, `RequestVersionExact`) gets status 404 (what the Java example answers), version 3.0,
  `server: netty` and the 14-byte body from both servers for GET and POST; `Http3InteropTest` checks a GET and a
  100,000-byte POST that way. h3spec itself was not run (it is not installed).

Run the examples with:

```powershell
dotnet run --project test/Netty.NET.Handler.Codec.Http3.Examples -- example.Http3ServerExample
```

```powershell
dotnet run --project test/Netty.NET.Handler.Codec.Http3.Examples -- example.Http3ClientExample
```
