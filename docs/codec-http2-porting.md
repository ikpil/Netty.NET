# codec-http2 porting

Netty.NET ports `netty-codec-http2` of Netty 4.2.19.Final (pinned commit `64cc10f38ea5f5bd7eae48507817c66680d0afdc`)
to `src/Netty.NET.Handler.Codec.Http2`, with its original tests in `test/Netty.NET.Handler.Codec.Http2.Tests`. The
[manifest](codec-http2-porting-manifest.json) records every pinned file of `codec-http2/src` (223 files: 130 main and 75
test Java sources, `module-info.yml`, two main resources and 15 test resources) with its C# counterpart, its CLR
adaptations and evidence, or the reason it is not ported. No entry is `pending`. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-http2
```

`codec-http2` is also in the checker's default module list, so a run without `-Module` checks it with the other codec,
pkitesting and handler manifests.

The checker verifies the file inventory, the source hashes, the original comments, the mapping of every declared JUnit
test to a C# test of the same name (case may differ) or a recorded disposition, and the facts and evidence of every unit
that is not ported. It fails on a `pending` entry.

- **Order.** The port was done on 2026-11-25 in three groups. The base group's lead wrote a foundation (constants,
  errors, flags, settings, 35 Java interfaces, the test helper `Http2TestUtil`), then six parts ran in parallel (h1 HPACK,
  h2 headers and frames, h3 connection and flow control, h4 frame I/O, h5 encoder and decoder, h6 connection handler).
  The conversion group ported the HTTP/1.x adapters, the compression decorators and `Http2SecurityUtil`; the multiplex
  group the child-channel layer and the upgrade codecs.
- **testsuite-http2.** The separate Maven module `testsuite-http2` (an h2spec conformance server: `HelloWorldHttp2Handler`,
  `Http2Server` and so on) is outside this module; it was ported on 2026-11-26 as `Netty.NET.Testsuite.Http2`, see
  [testsuite-http2-porting.md](testsuite-http2-porting.md).

## Owner decisions (2026-11-25)

- **Project and namespace.** One project, `Netty.NET.Handler.Codec.Http2`, for the Maven module (one package,
  `io.netty.handler.codec.http2`, namespace `Netty.NET.Handler.Codec.Http2`; tests `Netty.NET.Handler.Codec.Http2.Tests`).
  It references `Netty.NET.Handler.Codec.Http`.
- **io.netty.util.collection.** The BCL `Dictionary` stays (owner decision 2026-11-19, revisited here as planned).
  `Http2Settings`, which extends `CharObjectHashMap<Long>` in Java, is built on `Dictionary<char, long>`: it keeps its own
  Java methods (`headerTableSize()` and the others) and inherits the BCL members. The `IntObjectHashMap` users keep
  `Dictionary<int, V>`. `Dictionary` enumerates in insertion order, so SETTINGS entries (in frames and in the
  `HTTP2-Settings` header) may be written in another order than Java's hash-slot order; the order carries no meaning
  because a SETTINGS frame holds each identifier once.

## Status

Ported (2026-11-25). The six `clr-adapted` test files are `DefaultHttp2ConnectionEncoderTest`, `Http2ServerUpgradeCodecTest`,
`Http2ClientUpgradeCodecTest` and `Http2MultiplexTransportTest` (each with CLR-inapplicable methods), `Http2FrameCodecTest`
and `H2PrefaceTest`; the 14 verified test resources are the HPACK `testdata/*.json` files, copied byte for byte.

| Kind | clr-adapted | verified | deprecated-not-ported | jvm-only-replaced | library-not-ported | Total |
| --- | --- | --- | --- | --- | --- | --- |
| main Java sources | 127 | 0 | 2 | 1 (`package-info.java`) | 0 | 130 |
| test Java sources | 6 | 64 | 3 | 1 | 1 | 75 |
| test resources (HPACK JSON) | 0 | 14 | 0 | 0 | 0 | 14 |
| metadata (`module-info.yml`, two native-image files, `junit-platform.properties`) | 0 | 0 | 0 | 4 | 0 | 4 |
| **total** | 133 | 78 | 5 | 6 | 1 | 223 |

The checker reports:

```
codec-http2 manifest verified: 223 pinned files (133 clr-adapted, 5 deprecated-not-ported, 6 jvm-only-replaced, 1 library-not-ported, 78 verified), 934 mapped original tests, 7 CLR-inapplicable and 5 not-ported original tests, 3 deprecated-member, 0 JVM-only-API and 0 library-not-ported comment dispositions; 5 deprecated-not-ported, 6 JVM-only-replaced and 1 library-not-ported entries carry 59 original comments.
```

Of the 946 declared original tests, 934 are mapped to C# tests with the same names, 7 are CLR-inapplicable
(`DefaultHttp2ConnectionEncoderTest.dataFramesShouldMergeUseVoidPromise`, three tests that only exercise the deprecated
`Http2MultiplexCodec` family and three OpenSSL tests) and 5 are not ported: the four methods of the
deprecated-not-ported `Http2MultiplexCodecBuilderTest` and `NativeImageHandlerMetadataTest.collectAndCompareMetadata`
(JVM-only). The three deprecated-member comment dispositions are the two `DelegatingDecompressorFrameListener`
constructors and the Javadoc of `Http2StreamChannelBootstrap.open0`. `HttpConversionUtilFuzzTest` declares no JUnit
method (`@FuzzTest` is not a JUnit `@Test`).

The C# run of `Netty.NET.Handler.Codec.Http2.Tests` executes 1550 test cases (2026-11-27; 1222 of the base group with
the CLR supplement `CompressorHttp2ConnectionEncoderTest.CombinedDataWritesCompleteWhenFlushed`, 133 of the
conversion group, 195 of the multiplex group): 1547 pass, 0 fail and 3 are skipped exactly where the source skips them
(two `@Disabled` and one `@DisabledOnOs(WINDOWS)` test of `Http2MultiplexTransportTest`). Theories run once per data
row and the tests of abstract fixtures run for each derived class, so there are more cases than mapped methods. A
mutation check (2026-11-25) rejected 14 of 14 applicable mutations (5 were not applicable). The child channel reworked
onto the Transport's overridable Unsafe members (owner decision 2026-11-25, see [The child channel on the Transport's
AbstractChannel](#the-child-channel-on-the-transports-abstractchannel)) was verified on 2026-11-25 with two runs of the
real test project.

| Area | Main files | Test files | Declared original tests | Mapped | CLR-inapplicable | Not ported |
| --- | --- | --- | --- | --- | --- | --- |
| [Constants, errors and settings](#constants-errors-and-settings) | 8 | 3 (with the helper `Http2TestUtil`) | 26 | 26 | 0 | 0 |
| [Headers and HPACK](#headers-and-hpack) | 17 (3 interfaces) | 16 (4 without tests) | 160 | 160 | 0 | 0 |
| [Frame reading and writing](#frame-reading-and-writing) | 15 (5 interfaces) | 5 (one helper) | 82 | 82 | 0 | 0 |
| [Connection, streams and flow control](#connection-streams-and-flow-control) | 14 (7 interfaces) | 9 (3 without own methods) | 165 | 165 | 0 | 0 |
| [Connection encoder and decoder](#connection-encoder-and-decoder) | 15 (5 interfaces) | 10 | 149 | 148 | 1 | 0 |
| [Connection handler and builders](#connection-handler-and-builders) | 3 | 2 | 72 | 72 | 0 | 0 |
| [Frame value types](#frame-value-types) | 30 (15 interfaces) | 1 | 4 | 4 | 0 | 0 |
| [HTTP/1.x conversion](#http1x-conversion) | 8 | 4 ported, 1 library-not-ported | 78 | 78 | 0 | 0 |
| [Compression](#compression) | 2 | 2 | 11 | 11 | 0 | 0 |
| [Multiplex, frame codec and upgrade](#multiplex-frame-codec-and-upgrade) | 15 ported, 2 deprecated-not-ported | 18 ported (two helpers), 3 deprecated-not-ported | 198 | 188 | 6 | 4 |
| [JVM-only units](#not-ported-jvm-only-units) | 1 (`package-info.java`) | 1 | 1 | 0 | 0 | 1 |
| **total** | 127 ported, 2 deprecated-not-ported, 1 JVM-only | 70 ported, 3 deprecated-not-ported, 1 library-not-ported, 1 JVM-only | 946 | 934 | 7 | 5 |

The 35 Java interfaces of the base group are counted with the area that uses them.

## Namespaces and assembly

The project references `Netty.NET.Handler.Codec.Http`, which brings codec-compression, handler, codec-base, Transport,
Resolver, Buffer and Common. `InternalsVisibleTo` opens its internals to `Netty.NET.Handler.Codec.Http2.Tests` only;
codec-base opens its internals to this assembly for the CLR-only `ExecutorPromiseCombiner` (see `PromiseCombiner` below). The
test project also references `Netty.NET.PkiTesting` and Moq, and deploys the 14 HPACK resources from
`Resources/testdata/` next to the test assembly under the class-path directory `io/netty/handler/codec/http2/testdata/`,
because `HpackTest` lists that directory (`ResourcesUtil.getFile(HpackTest.class, TEST_DIR).listFiles()`).

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.http2` | `Netty.NET.Handler.Codec.Http2` (project root) | Handlers for sending and receiving HTTP/2 frames. |
| test package | `Netty.NET.Handler.Codec.Http2.Tests` (project root) | (no test package-info) |

## Shared CLR adaptations

The adaptations below apply across the areas; each area section lists only what is specific to it.

| Java | C# | Notes and evidence |
| --- | --- | --- |
| Interfaces, nested interfaces, interface constants | `I`-prefixed interfaces; nested interfaces stay nested (`IHttp2Connection.IListener/IEndpoint/IPropertyKey`, `IHttp2RemoteFlowController.IFlowControlled/IListener`, `IStreamByteDistributor.IStreamState/IWriter`, `IHttp2HeadersEncoder.IConfiguration/ISensitivityDetector`, `IHttp2HeadersDecoder.IConfiguration`, `IHttp2FrameWriter.IConfiguration`, `IHttp2FrameReader.IConfiguration`); constants are static readonly interface fields, with private nested classes for the anonymous implementations (`NEVER_SENSITIVE`, `ALWAYS_SENSITIVE`, `IHttp2PromisedRequestVerifier.ALWAYS_VERIFY`, `IHttp2SettingsAckFrame.INSTANCE`) | Re-declarations that only narrow a return type or repeat a member for its Javadoc are inherited, with the comment kept in place: `copy/duplicate/retainedDuplicate/replace/retain/touch` of the ByteBufHolder frames, `stream(Http2FrameStream)` of the stream frames, `name()` of the settings frames, `writeFrame` of `Http2ConnectionEncoder`, `iterator()` of `Http2Headers` (owner decision 2026-11-19, [transport-porting.md](transport-porting.md) Decisions; a repeated interface member would hide the inherited one, CS0108). `Http2Headers.valueIterator(CharSequence)` is declared, because codec-base `IHeaders` has none. |
| Narrowed return types | covariant class overrides; public methods with the narrowed type and explicit interface implementations | C# covariant returns cover class overrides only (owner decision 2026-11-19, [codec-http-porting.md](codec-http-porting.md), Shared CLR adaptations): `AbstractHttp2StreamFrame.Stream(IHttp2FrameStream)` and the holder members of the frames, `DefaultHttp2LocalFlowController.FrameWriter(...)`, `DefaultHttp2Connection`'s `createStream`/`reservePushStream` returning `DefaultStream`, the pseudo-header setters of `EmptyHttp2Headers`. |
| `ChannelPromise`, `ChannelFuture`, `Promise<Void>`, `Future<Void>` | `TaskCompletionSource`, `Task` | The codec-http mapping where code stores, completes or listens to a promise ([codec-http-porting.md](codec-http-porting.md), Shared CLR adaptations). Every Java method that returns a future takes the `Async` suffix and returns the promise's Task (see [API shape](#api-shape)); ChannelFuture parameters (`closeStream(stream, future)`) are Task. `setFailure`/`tryFailure` returning the promise are `SetException`/`TrySetException` followed by `promise.Task`; `ctx.newSucceededFuture()` is `Task.CompletedTask`. |
| `ctx.newPromise()`, `ctx.voidPromise()`, `promise.unvoid()`, `isVoid()` | internal `Http2CodecUtil.NewPromise()`, the promise itself, `false` | A new `TaskCompletionSource` without `RunContinuationsAsynchronously` (see [Listener timing](#listener-timing)). There is no void promise ([transport-porting.md](transport-porting.md): VoidChannelPromise, "discard the returned Task"), so `unvoid()` is the promise itself and the `isVoid()` branches are left out. |
| `ctx.write(msg, promise)`, `writeAndFlush(msg, promise)`, `close(promise)`, `future.addListener(l)`, `future.cause()`, `PromiseNotifier.cascade` | internal `Http2CodecUtil.Write`, `WriteAndFlush`, `Close`, `AddListener`, `Cause`, `Cascade` | `Cascade` copies the operation Task into the promise (`TrySetFromTask`, the PromiseNotifier mapping of [common-task-composition.md](common-task-composition.md)). `Cause` is the faulted Task's first inner exception, a `TaskCanceledException` for a canceled Task and null otherwise. The timing is described under [Listener timing](#listener-timing). |
| `Http2CodecUtil.SimpleChannelPromiseAggregator extends DefaultChannelPromise` | internal sealed class deriving from `TaskCompletionSource` | The completion methods of `TaskCompletionSource` are not virtual, so the overridden `setSuccess/trySuccess/setFailure/tryFailure` are the class's own methods; `newPromise()` returns a child promise whose synchronous continuation calls `TrySuccess()`/`TryFailure(cause)`, and `doneAllocatingPromises()` returns the aggregate promise. |
| `PromiseCombiner` | `List<Task>` completed into the promise by codec-base's internal `ExecutorPromiseCombiner.Combine(ctx, futures, promise)` | It counts the completions on `ctx.Executor` and completes the promise with the outcome of `Task.WhenAll` when the last one completes, as `new PromiseCombiner(ctx.executor())` does. See [common-task-composition.md](common-task-composition.md), Executor affinity of a combined write (`CompressorHttp2ConnectionEncoder.writeData`). The buffer promises come from `NewPromise()`, so the earlier `Task.WhenAll` + `Cascade` already completed on the event loop; `CompressorHttp2ConnectionEncoderTest.CombinedDataWritesCompleteWhenFlushed` checks it over a real encoder, frame writer and channel. |
| Outbound handler operations (`write(ctx, msg, promise)`, `bind`, `connect`, `close`, ...) | `WriteAsync(ctx, msg, CancellationToken)`, `BindAsync`, `ConnectAsync`, `CloseAsync(ctx, CancellationToken)`, ... | The returned Task replaces the promise ([codec-base-porting.md](codec-base-porting.md), Outbound operations). A handler that completes a promise itself creates it with `Http2CodecUtil.NewPromise()` and returns its Task (`Http2ConnectionHandler`, `HttpToHttp2ConnectionHandler`). |
| `String.format(fmt, args)` (104 `%d`, 38 `%s`, 1 `%b` in the package), `String.valueOf` and string concatenation | internal `Http2CodecUtil.Format`, `Http2CodecUtil.ToJavaString` | `Format` keeps the Java text ([Formatter](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Formatter.html): `%s` of null is "null", `%b` of null is "false", `%%` is "a literal '%'"); integers use the invariant culture. `ToJavaString` is the `String.valueOf` text (null, `true`/`false`, invariant numbers) for concatenations and `toString()`. |
| Enums with fields and methods (`Http2Error`, `Http2Stream.State`, `Http2Headers.PseudoHeaderName`) | sealed classes with static readonly instances | As codec-base `Base64Dialect` and codec-http `HttpStatusClass` ("You can't define a method inside the definition of an enumeration type", [enum](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum)). They are compared by reference, so the Java `switch` statements on a `State` are if/else chains; `ToString()` is the constant name; `PseudoHeaderName.Values()` stands for `values()`. Simple enums (`Http2Exception.ShutdownHint`, `Http2FrameLogger.Direction`, `Http2FrameStreamEvent.Type`, `HpackUtil.IndexType`) are C# enums. |
| Members named like a nested type | trailing `_` | CS0102, owner rule ([codec-http-porting.md](codec-http-porting.md), Shared CLR adaptations): `IHttp2Stream.State_()`, `Http2Exception.ShutdownHint_()`, `Http2FrameStreamEvent.Type_()`, `State_(...)` of the two stream byte distributors and the nested `DefaultHttp2Connection.ConnectionStream_` class next to `ConnectionStream()`. |
| `Http2Connection.Endpoint<F extends Http2FlowController>`, wildcard `Endpoint<?>`, `DefaultEndpoint<?>` | `IHttp2Connection.IEndpoint` (non-generic) and `IEndpoint<F> : IEndpoint`; abstract `DefaultEndpoint` and `DefaultEndpoint<F> : DefaultEndpoint` | The non-generic interface has every member that does not use F (`opposite()` returns it); `IEndpoint<F>` adds `FlowController()`/`FlowController(F)`. `IEndpoint<F>` cannot be covariant because F is also a parameter type ([variance](https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance): covariant type parameters "can be used as the return types of the interface's methods"). C# has no wildcard types. |
| `io.netty.util.collection` (`CharObjectHashMap<Long>`, `IntObjectMap<V>`/`IntObjectHashMap<V>`) | `Dictionary<char, long>` (`Http2Settings`), `Dictionary<int, V>` | Owner decision 2026-11-19, confirmed 2026-11-25 ([Owner decisions](#owner-decisions-2026-11-25)). `Iterator.remove()` while iterating the stream map or the WFQ children map is `Dictionary.Remove` of the current key: "This mutating method may be safely called without invalidating active enumerators" (.NET Core 3.0+, [Dictionary.Remove](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.remove?view=net-10.0)). |
| `Closeable` (`Http2FrameWriter`, `Http2FrameReader`, `Http2ConnectionDecoder`, `DefaultHttp2HeadersEncoder`) | `IDisposable`, `Close()` kept | [handler-porting.md](handler-porting.md): "Closeable keeps close() as Close() and implements IDisposable.Dispose() with it"; the interfaces implement `Dispose()` with a default interface member. |
| `Throwable.getMessage()` | `Message` | Never null in .NET, so `Http2CodecUtil.ToByteBuf` keeps only the shape of the null check (see [Behaviour differences](#behaviour-differences)). |
| `assert` | `Debug.Assert` | Compiled out of a Release build; the tests that assert Java's `AssertionError` need a Debug build of the library (see [Connection, streams and flow control](#connection-streams-and-flow-control)). |
| Inner (non-static) and anonymous classes | nested classes that receive the enclosing instance as a constructor argument; named private nested classes | A C# nested class has no enclosing instance. The area sections name the main cases. |
| Package access, `@Override` widening in builders | `internal`, `protected internal`; `public new` members that call `base` | Package-private types and members are `internal`, members that the package reaches through a subclass `protected internal`. Java builders widen protected methods to public with `@Override`; "An `override` declaration can't change the accessibility of the `virtual` method" ([override](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override)), so subclasses "explicitly hide a member that a base class provides" ([new modifier](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier)) with `public new`. |
| Exceptions | `IllegalArgumentException` is `ArgumentException`, `IllegalStateException` `InvalidOperationException`, `Throwable` `Exception`; `catch (Http2Exception \| RuntimeException e)` is `catch (Exception)`; `catch (Http2Exception e) { throw e; }` is `throw;`; `PlatformDependent.throwException(x)` is `throw x` | C# has no checked exceptions, so `throws Http2Exception` clauses have no counterpart and the unreachable `return` after `throwException` is left out ([handler-porting.md](handler-porting.md)). |
| Collections | `ArrayDeque` is `Queue<T>` (`LinkedList<T>` where `peekLast`/`addFirst`/`remove(Object)` are used); `TreeMap` is `SortedDictionary`; Netty `PriorityQueue`/`DefaultPriorityQueue`/`EmptyPriorityQueue`/`PriorityQueueNode` are Common `IPriorityQueue`/`DefaultPriorityQueue`/`EmptyPriorityQueue`/`IPriorityQueueNode`; `System.identityHashCode` is `RuntimeHelpers.GetHashCode` | ArrayDeque is `Queue<T>` ([handler-porting.md](handler-porting.md)), which has no tail or head insertion. `peek`/`poll`/`offer`/`size`/`removeTyped`/`containsTyped` are `TryPeek`/`TryDequeue`/`TryEnqueue`/`Count`/`Remove`/`Contains`. [RuntimeHelpers.GetHashCode](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.runtimehelpers.gethashcode?view=net-10.0) returns "a hash code that indicates object identity". |
| `ByteProcessor` | `Process(byte)` as the Common `Func<byte, bool>` | The codec-http mapping ([codec-http-porting.md](codec-http-porting.md), Shared CLR adaptations): `HpackHuffmanEncoder`, `HpackHuffmanDecoder` (delegates created once) and `DefaultHttp2Headers`. |
| `java.net.URI` (`HttpConversionUtil`) | private `CreateUri`/`UriScheme`/`UriPort`/`UriAuthority` | `System.Uri` canonicalizes; the helpers keep the parts of the [OpenJDK URI parser](https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/java/net/URI.java) that the conversion uses (see [HTTP/1.x conversion](#http1x-conversion)). |
| `TimeUnit`, `Ticker`, `long timeout, TimeUnit unit` | nanosecond arithmetic, Common `Ticker`, `TimeSpan` | `TimeUnit.SECONDS.toNanos(s)` is `s * 10^9` and `NANOSECONDS.toSeconds(n)` the truncating division ([TimeUnit](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/TimeUnit.html): "Conversions from finer to coarser granularities truncate"). |
| `X.class` in messages | `typeof(X)` | `Type.ToString()` is the full name, where Java's `Class.toString()` prints "interface " or "class " and the name ([Class.toString](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Class.html#toString())). |
| `delegate` parameters and fields | `@delegate` | `delegate` is a C# keyword (`DecoratingHttp2FrameWriter`, `DecoratingHttp2ConnectionEncoder`/`Decoder`, `StreamBufferingEncoder`, `CompressorHttp2ConnectionEncoder`). |
| `byte` | `System.Byte` | The wire octets are CLR bytes; the places where Java's sign matters keep it with `(sbyte)` (see [Byte-sign spots](#byte-sign-spots)). |
| gson (`HpackTestCase`) | `System.Text.Json` `JsonDocument` | The snake_case field mapping is written out ([JsonDocument](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsondocument?view=net-10.0), [EnumerateObject](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.enumerateobject?view=net-10.0)). |
| Mockito, `@BeforeEach`/`@AfterEach`, `CountDownLatch`, `assertThrows` (tests) | Moq, test-class constructor/`Dispose()` or `IAsyncLifetime.DisposeAsync`, `CountdownEvent`, `Assert.ThrowsAny` | `verify`/`never`/`times`/`atMost`/`atLeastOnce` are `Verify` with `Times` (a plain `verify(mock)` is `Times.Once()`); `verifyNoMoreInteractions` is `VerifyNoOtherCalls`; `verifyNoInteractions` asserts an empty `Invocations` list; `reset(mock)` is `Mock.Reset()` or `Invocations.Clear()`; `doAnswer`/`doThrow`/`doNothing` are `Setup` with `Callback`/`Throws` (later setups win, as in Mockito); `ArgumentCaptor` reads the verified invocations or a `Callback`; `streamEq` is `Match.Create` ([Moq Quickstart](https://github.com/devlooped/moq/wiki/Quickstart)). xUnit runs the constructor and `Dispose()` per test ([xUnit shared context](https://xunit.net/docs/shared-context)). The count-down of the latch stops at zero. Latch waits and EmbeddedChannel calls pass `TestContext.Current.CancellationToken` (xUnit1051). |

### Listener timing

Java's `DefaultPromise` notifies a listener at once when the promise completes on its event loop, and otherwise through
`executor.execute`. The Transport completes its operation Tasks with `RunContinuationsAsynchronously`
([codec-http-porting.md](codec-http-porting.md), Listener timing), so codec-http2 keeps Java's timing with Common's
`EventExecutorTaskScheduler` over `ctx.Executor`:

- **`Http2CodecUtil.NewPromise()`** creates a `TaskCompletionSource` without `RunContinuationsAsynchronously`, so a
  listener added with `ExecuteSynchronously` runs when the promise completes, as codec-http's handshakers do.
- **`Http2CodecUtil.Cascade(ctx, future, promise)`** copies a completed operation Task into the promise at once;
  otherwise the copy is an `ExecuteSynchronously` continuation on the `EventExecutorTaskScheduler`, so it runs on the
  event loop when the operation completes there. `Write`, `WriteAndFlush` and `Close` call it with the context's Task.
- **`Http2CodecUtil.AddListener(ctx, future, listener)`** is a continuation on the same scheduler: at once when the future
  completes on the event loop, otherwise through the executor; a listener added to a completed future runs at once on
  the event loop, as Java notifies it from `addListener`. The `ChannelFutureListener`s of `Http2ConnectionHandler` are
  `Action<Task>`s added this way.
- **Without a context.** `Http2ConnectionHandler.closeStream(stream, future)` has no context in Java; the handler keeps
  the context of `handlerAdded` in the CLR-only field `listenerCtx` to notify that listener on the event loop.
  `DefaultHttp2Connection.close(promise)` and `SimpleChannelPromiseAggregator` use synchronous continuations.
  `ChannelFutureListener.CLOSE_ON_FAILURE` closes `ctx.Channel`, because a Task has no `channel()`.

## Constants, errors and settings

The foundation of the base group: `Http2CodecUtil`, `Http2Error`, `Http2Exception`, `Http2NoMoreStreamIdsException`,
`Http2Flags`, `Http2FrameTypes`, `Http2Settings` and `Http2ConnectionPrefaceAndSettingsFrameWrittenEvent` (8 main
files), with the test files `Http2ExceptionTest`, `Http2SettingsTest` and the shared helper `Http2TestUtil`. All 26
declared tests are mapped and pass.

- **Http2CodecUtil** keeps the Java constants and helpers and holds the CLR-only internal helpers of the shared
  adaptations (`NewPromise`, `Write`, `WriteAndFlush`, `Close`, `Cascade`, `AddListener`, `Cause`, `Format`,
  `ToJavaString`, `SimpleChannelPromiseAggregator`). `ToByteBuf(ctx, cause)` sends `Message` as GOAWAY debug data (see
  [Behaviour differences](#behaviour-differences)).
- **Http2Error** is a sealed class (shared row); the tests enumerate it with `Http2Error.ValueOf(0..13)`.
  **Http2Exception** keeps its `ShutdownHint` enum, read with `ShutdownHint_()` (CS0102), and the nested
  `Http2Exception.StreamException`.
- **Http2Settings : Dictionary\<char, long\>** (owner decision). `Put(char, long)` verifies the value, the inherited BCL
  members keep their names (`TryGetValue` for Java's `get(char)`), and the Java getters return `long?`/`int?`/`bool?`.
  `Equals`/`GetHashCode` are overridden with CharObjectHashMap's content equality, because `Dictionary` keeps reference
  equality: it compares size and values per key and hashes `size ^ key...`
  ([KObjectHashMap.template](https://github.com/netty/netty/blob/64cc10f38ea5f5bd7eae48507817c66680d0afdc/common/src/main/templates/io/netty/util/collection/KObjectHashMap.template),
  `(int) key` from common/src/main/script/codegen.groovy); `DefaultHttp2SettingsFrame.equals` uses it. `ToString()`
  keeps `{KEY=value, ...}`. Entries enumerate in insertion order (owner decision).

### Settings test notes

| Java test | Methods | Notes |
| --- | --- | --- |
| `Http2ExceptionTest` | 3 | Two tests pass the null that `new RuntimeException().getMessage()` returns, because `Exception.Message` is never null in .NET. |
| `Http2SettingsTest` | 23 | `size()` is `Count` (`Assert.Empty`, xUnit2013), `get(char)` the indexer, `put(char, Long)` `Put(char, long)`. |
| `Http2TestUtil` (helper) | 0 | `newVoidPromise(channel)` is a `TaskCompletionSource` whose failure fires `exceptionCaught` on the channel pipeline (the observable parts of Java's void-promise overrides); `mockedFrameWriter()` is a Moq mock whose setups complete the promise argument; `FAKE_EXCEPTION` and `TestStreamByteDistributorStreamState` serve the remote flow controller, distributor and connection tests. |

## Headers and HPACK

The header types `DefaultHttp2Headers`, `ReadOnlyHttp2Headers`, `EmptyHttp2Headers` and `CharSequenceMap` (part h2), the
HPACK implementation `HpackUtil`, `HpackHeaderField`, `HpackStaticTable`, `HpackDynamicTable`, `HpackHuffmanEncoder`,
`HpackHuffmanDecoder`, `HpackEncoder`, `HpackDecoder` and the header block codecs `DefaultHttp2HeadersEncoder` and
`DefaultHttp2HeadersDecoder` (part h1), with the interfaces `IHttp2Headers`, `IHttp2HeadersEncoder` and
`IHttp2HeadersDecoder`. All 160 declared tests of the 16 test files are mapped and pass (`HpackDecoderTest`'s 47 methods
run 58 xUnit cases with the `@CsvSource` rows). Every original comment is kept; nothing is deprecated or
library-dependent.

### Headers

- **DefaultHttp2Headers** derives from codec-base `DefaultHeaders<ICharSequence, ICharSequence, IHttp2Headers>` and
  implements `IHttp2Headers` ([codec-base-porting.md](codec-base-porting.md), Headers): `HashingStrategy` is
  `IEqualityComparer`, the validators are `DefaultHeaders.INameValidator/IValueValidator<ICharSequence>` (private nested
  classes for the anonymous ones), and the anonymous `ByteProcessor` is a `Func<byte, bool>`. The pseudo-header
  accessors are virtual, and the static validators keep the Java spelling `DefaultHtt2NameValidator()`. Names and values
  are `ICharSequence`: callers pass `AsciiString` or `StringCharSequence`.
- **ReadOnlyHttp2Headers** is sealed and its mutators throw `NotSupportedException`; **EmptyHttp2Headers** derives from
  codec-base `EmptyHeaders`; **CharSequenceMap\<V\>** requires `V : class`.
- **Inner classes.** `Http2HeaderEntry`, `ReadOnlyValueIterator` and `ReadOnlyIterator` read the enclosing instance and
  take it as a constructor parameter. The field initializer `firstNonPseudo = head` is a constructor assignment
  ([CS0236](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236): "A field initializer cannot reference the
  non-static field"). `Http2HeaderEntry.Remove()` overrides codec-base's `protected internal` member as
  `protected internal`. codec-base opens its internals to this assembly, and an override of a `protected internal`
  member it can see keeps both modifiers (CS0507). The other protected codec-base members are overridden as
  `protected`.

### HPACK

- **Shapes.** `HpackEncoder`, `HpackDecoder` (package-private) and `HpackHeaderField` are internal; the tests reach their
  hooks (`Length()`, `Size()`, `GetHeaderField(int)`, `HpackDecoder.DecodeULE128`, `HUFF_CODE_THRESHOLD`) through
  `InternalsVisibleTo`. `DefaultHttp2HeadersEncoder` and `DefaultHttp2HeadersDecoder` are public with virtual members and
  internal constructors that take an `HpackEncoder`/`HpackDecoder`; the encoder implements `IDisposable.Dispose()` with
  `Close()`.
- **ByteProcessor.** `HpackHuffmanEncoder`'s processors and `HpackHuffmanDecoder` keep `process(byte)` as `Process(byte)`
  and pass a delegate created once to `forEachByte`. The `catch (Exception e) { PlatformDependent.throwException(e); }`
  blocks that only served Java's checked `process(...) throws Exception` are left out.
- **Static initialization.** `HpackStaticTable`'s two static blocks are private methods that the single static
  constructor runs in order after the field initializers ("A class or struct can only have one static constructor",
  [Static constructors](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors)).
  The Common `AsciiString.HashCode` keeps Java's hash, so the one-entry-per-bucket tables need no change.
- **ISO-8859-1 writes.** `out.writeCharSequence(s, ISO_8859_1)` takes Java's `ByteBufUtil.writeAscii` path
  (`AbstractByteBuf.setCharSequence0` handles US_ASCII and ISO_8859_1 alike), so `HpackEncoder` calls
  `ByteBufUtil.WriteAscii` (c2b per char; chars above 0xFF become `?`, which `HpackEncoderTest.testSanitization`
  checks).
- **Exceptions.** `catch (IllegalArgumentException)` in `HpackDecoder.Http2HeadersSink` filters out
  `ArgumentNullException`/`ArgumentOutOfRangeException` (BCL subclasses of `ArgumentException` that stand for Java's
  NPE/IOOBE), as multipart's `HttpPostBodyUtil.IsIllegalArgument` does.

### HPACK byte values

| Place | Java | C# |
| --- | --- | --- |
| `HpackDecoder.decode` header representation byte | `byte b = in.readByte(); if (b < 0)` (indexed header bit) | read as `sbyte`; the masks (`& 0x7F`, `& 0xE0`, ...) give the same values |
| `HpackDecoder.decodeDynamicTableSizeUpdates`, `decodeULE128` | masks and comparisons with non-negative constants | `System.Byte` unchanged (same results) |
| `HpackHuffmanDecoder.process` | `input >> 4` of a signed byte, then `& 0x0F` | unsigned byte; the mask removes the difference |
| `HpackHuffmanEncoder` | `value & 0xFF` index; `long \|= int` codes | same index; non-negative codes OR-ed as `uint` (CS0675) |
| `HpackEncoder.hashMask` | `(byte) (length - 1)`, length ≤ 128 | `byte`, always below 128 |
| `DefaultHttp2Headers` name validation (`isUpperCase(byte)`), `PseudoHeaderName.hasPseudoHeaderFormat` (`byteAt(0) == ':'`), `ReadOnlyHttp2Headers` (`byteAt(0) != ':'`) | byte compared with ASCII constants | unchanged: every constant is below 0x80 |
| tests | `(byte)` of negative ints in expected encodings; byte literal `-1` | same octets; `unchecked((byte) -1)` |

### HPACK test notes

| Java test | Methods | Notes |
| --- | --- | --- |
| `HpackDynamicTableTest`, `HpackStaticTableTest`, `HpackHuffmanTest` | 7, 10, 11 | `IndexOutOfBoundsException` is `ArgumentOutOfRangeException`; Strings passed as CharSequence are `StringCharSequence`; `Random(123456789L)` is `System.Random(123456789)` (sequence not reproduced, the round trip does not depend on it). |
| `HpackEncoderTest` | 13 | `Random(0)` is `System.Random(0)` (sequence not reproduced); header equality calls `Equals`. |
| `HpackDecoderTest` | 47 (58 xUnit cases) | Moq `Verify`/`VerifyNoOtherCalls`/`Reset`; the Mockito `mockingDetails` loop that marks `authority()`/`contains(":authority")` verified is `Verify(..., Times.AtMost(int.MaxValue))`. |
| `HpackTest` with `HpackTestCase` | 1 (14 files) | gson is `System.Text.Json` (shared row); the resources are deployed under `io/netty/handler/codec/http2/testdata/`. `TestHeaderListener` (an internal `DefaultHttp2Headers` subclass over a list of `HpackHeaderField`) and `InOrderHttp2Headers` (a public `DefaultHeaders<ICharSequence, ICharSequence, IHttp2Headers>` with an internal constructor) are helpers. |
| `DefaultHttp2HeadersEncoderTest`, `DefaultHttp2HeadersDecoderTest`, `Http2HeaderBlockIOTest` | 2, 15, 3 | `@MethodSource` lists are `TheoryData<int>`; AssertJ string checks are `Assert.Equal(..., ignoreCase: true)`. `DefaultHttp2HeadersDecoderTest` line 198 needed the checker fix of [Shared-API requests](#shared-api-requests). |
| `DefaultHttp2HeadersTest` | 21 | Java Strings passed as CharSequence are `StringCharSequence` (`Cs`), `AsciiString.of(String)` is `new AsciiString(s)` (`Of`); `assertEquals` of two CharSequences calls `Equals` (`AssertCsEquals`), since xUnit compares an `ICharSequence` element by element; `assertThrows(expectedType, ...)` with a runtime `Class` is `Record.Exception` with `Assert.IsAssignableFrom`. |
| `ReadOnlyHttp2HeadersTest` | 27 | `nullTrailersNotAllowed`: Java's NPE is a dereference of the null array, so the test asserts `NullReferenceException`. `NoSuchElementException` is `InvalidOperationException`, `UnsupportedOperationException` `NotSupportedException`. |
| `Http2HeadersTest` | 3 | `new StringBuilder(value)` as a CharSequence is the Common `AppendableCharSequence`; `assertSame` of the interned literals holds (`AsciiString.Cached` keeps the string). |
| `HashCollisionTest` | 0 (`@Disabled`, `main` only) | `main` is `Main_`: the xunit.v3 project has its own entry point ([CS0017](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0017)). |

## Frame reading and writing

Part h4 of the base group: `DefaultHttp2FrameReader`, `DefaultHttp2FrameWriter`, `DecoratingHttp2FrameWriter`,
`Http2FrameLogger`, `Http2InboundFrameLogger`, `Http2OutboundFrameLogger`, `Http2FrameAdapter`,
`Http2FrameListenerDecorator`, `Http2EventAdapter` and `StreamBufferingEncoder`, with the interfaces `IHttp2FrameReader`,
`IHttp2FrameWriter`, `IHttp2FrameListener`, `IHttp2FrameSizePolicy` and `IHttp2DataWriter` (15 main files). All 82
declared tests of `DefaultHttp2FrameReaderTest` (23), `DefaultHttp2FrameWriterTest` (13), `Http2FrameRoundtripTest`
(28) and `StreamBufferingEncoderTest` (18, 23 xUnit cases with the theory rows) are mapped and pass;
`Http2FrameInboundWriter` is a test helper.

- **Shapes.** `Http2FrameLogger` takes the handler port's `Netty.NET.Handler.Logging.LogLevel`. The public non-final Java
  methods of the adapters, the decorators, the default reader and writer and `Http2FrameLogger` are `virtual`, since
  subclasses such as `Http2EmptyDataFrameListener` and `StreamBufferingEncoder` override them. `Http2FrameListenerDecorator.listener` is
  `protected internal readonly`: Java's `protected` includes package access, which `Http2EmptyDataFrameConnectionDecoder`
  and `Http2MaxRstFrameDecoder` use to unwrap the decorated listener.
- **Inner classes.** `HeadersContinuation` and `HeadersBlockBuilder` (reader) and `HeadersFrame`/`DataFrame`
  (`StreamBufferingEncoder`) take their outer instance as a constructor argument.
- **Close.** `DefaultHttp2FrameWriter.close()` tests its headers encoder for `IDisposable`, the counterpart of
  `instanceof Closeable`, and wraps a failure as `new Exception(ThrowableToString(e), e)`.
- **SETTINGS order.** `writeSettings` writes the entries in `Dictionary` (insertion) order (owner decision 2026-11-25);
  no test compares multi-entry SETTINGS bytes (the round-trip test compares the decoded `Http2Settings` with `Equals`).
- **StreamBufferingEncoder collections.** `TreeMap` is `SortedDictionary` with a private `PollFirstEntry()` helper; the
  removing values iterator of `cancelGoAwayStreams` walks a snapshot; `ArrayDeque` is `Queue`. `GoAwayDetail` is internal
  (CS0051: the internal `Http2GoAwayException(GoAwayDetail)` constructor takes it).

### Frame I/O byte values

| Place | Java | C# |
| --- | --- | --- |
| `DefaultHttp2FrameReader` exception texts (`verifyContinuationFrame`, `verifyNotProcessingHeaders`, `verifyAssociatedWithAStream`) | `%s` of the signed frame type | the frame type is passed as `(sbyte)`, so 0x80-0xFF print the negative Java value |
| `DefaultHttp2FrameReader` frame type switch, `onUnknownFrame(byte frameType)` | signed `byte` | unsigned `System.Byte` (same octet; the switch labels are 0-9) |
| `Http2FrameLogger.logUnknownFrame` | `frameType & 0xFF` | same value from the unsigned byte |
| `DefaultHttp2FrameWriter` `(int) (0x80000000L \| streamDependency)` | int widening and narrowing | explicit `(long)` widening and `unchecked` narrowing, same value |

### Frame I/O test notes

- `DefaultHttp2FrameWriterTest`: the mocked `ctx.write` answer is the `WriteAsync(msg, CancellationToken)` callback that
  returns a never-completing Task (the mocked future). `OutOfMemoryError` is `OutOfMemoryException`.
- `Http2FrameRoundtripTest`: `alloc.buffer()`/`buffer(int)` are the one `Buffer(int = 256, int = int.MaxValue)` member;
  `buf instanceof EmptyByteBuf` compares the runtime type name (`EmptyByteBuf` is internal to Netty.NET.Buffer).
- `StreamBufferingEncoderTest.closedBufferedStreamReleasesByteBuf`: the mocked buffer is a real `Unpooled.Buffer()` whose
  `RefCnt` must drop to 0, and a `TaskCompletionSource` cannot be mocked (its members are not virtual), so the RST
  promise is a real one checked with `IsCompletedSuccessfully`. `channel.unsafe()` has no C# counterpart and no stub.
- `Http2FrameInboundWriter` (internal) blocks on `GetAwaiter().GetResult()` like `syncUninterruptibly()`
  ([TaskAwaiter.GetResult](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.taskawaiter.getresult?view=net-10.0)).

## Connection, streams and flow control

Part h3 of the base group: `DefaultHttp2Connection`, `Http2ConnectionAdapter`, `Http2ConnectionPropertyKeys`,
`DefaultHttp2LocalFlowController`, `DefaultHttp2RemoteFlowController`, `UniformStreamByteDistributor` and
`WeightedFairQueueByteDistributor`, with the interfaces `IHttp2Connection`, `IHttp2Stream`, `IHttp2StreamVisitor`,
`IHttp2FlowController`, `IHttp2LocalFlowController`, `IHttp2RemoteFlowController` and `IStreamByteDistributor` (14 main
files). All 165 declared tests of the 9 test files are mapped and pass; the remote flow controller tests run once per
distributor through `UniformStreamByteDistributorFlowControllerTest` and `WeightedFairQueueRemoteFlowControllerTest`.

- **DefaultHttp2Connection.** All public members are virtual. The stream map is `Dictionary<int, V>`, and the Java
  `Iterator<?> itr` parameters are the enumerator, used only as the marker that a removal happens during that iteration
  (shared row). `close(Promise<Void>)` is `CloseAsync(TaskCompletionSource)`; `PromiseNotifier.cascade` is a
  synchronous continuation with `TrySetFromTask` ([common-task-composition.md](common-task-composition.md),
  PromiseNotifier), and the `isVoid()` branch is left out.
- **Stream properties.** `<V> V getProperty(key)` relies on an unchecked cast; the C# `V GetProperty<V>(key)` returns the
  value when it `is V` and `default(V)` for a missing value or a value of another type. Java's cast is erased and never
  fails; the local flow controller replaces a `ReducedFlowState` with a `DefaultState`, and Java casts the previous
  value to the new type unchecked (measured: a checked C# cast threw `InvalidCastException` inside the listener, which
  the connection swallowed).
- **Flow controllers.** `DefaultHttp2LocalFlowController.FrameWriter(IHttp2FrameWriter)` returns the class and
  implements the interface member explicitly. `Double.compare(ratio, 0.0)` is `double.CompareTo`: NaN is ordered
  differently, but a NaN ratio fails one of the two range checks in both, and `-0.0` fails the first in both. The Java
  `assert` statements of `DefaultHttp2RemoteFlowController.updateDependencyTree` are `Debug.Assert`.
- **Distributors.** The WFQ state-only map from `IntCollections.emptyMap()` is an empty `Dictionary` that is never added
  to, because the IntCollections empty map returns `null` from `remove` where a read-only dictionary throws; the empty
  children map is one shared sentinel compared by reference, and `takeChild` passes the dictionary being enumerated as
  its iterator marker. The anonymous `Http2ConnectionAdapter`, `Http2StreamVisitor`, `Event` and `FlowState` classes are
  named private nested classes.

### Flow control byte values

`DefaultStream.metaState` and the WFQ `State.flags` are Java `byte` bit sets of positive constants (at most `0x3F` and
`0x07`); `~CONST` is cast back to `byte` unchecked, which clears only that bit as Java's byte arithmetic does, and
`toString()` prints the same (positive) numbers. No other code of this area depends on a byte's sign.

### Flow control test notes

- **Assert statements.** `DefaultHttp2RemoteFlowControllerTest`'s six `assertThrows(AssertionError.class, ...)` tests
  check `updateDependencyTree`'s Java `assert` statements, which the JUnit run enables (`-ea`). An xUnit v3 run turns a
  failing `Debug.Assert` into `TraceAssertException`
  ([TraceAssertException.cs](https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/Exceptions/TraceAssertException.cs);
  measured with `Record.Exception(() => Debug.Assert(false, "boom"))`), so these tests need a Debug build of the
  library, as the JUnit tests need `-ea`.
- **Promises.** `DefaultHttp2ConnectionTest`'s `EventLoopGroup` only created promises; a promise is a
  `TaskCompletionSource`, so the group and its `@BeforeAll`/`@AfterAll` are gone, and listeners are continuations.
- **Mocked promises.** `DefaultHttp2LocalFlowControllerTest` verified `writeWindowUpdate(..., eq(promise))` with the
  promise of a stubbed `ctx.newPromise()`; the C# controller creates its promise itself, so the verification uses
  `It.IsAny<TaskCompletionSource>()`.

## Connection encoder and decoder

Part h5 of the base group (10 main files and the interfaces `IHttp2ConnectionEncoder`, `IHttp2ConnectionDecoder`,
`IHttp2LifecycleManager`, `IHttp2PromisedRequestVerifier` and `IHttp2SettingsReceivedConsumer`). Of the 149 declared
tests of its 10 test files, 148 are mapped and pass and 1 is CLR-inapplicable.

| Java | C# | Notes |
| --- | --- | --- |
| `DefaultHttp2ConnectionEncoder` | public, virtual members | `FlowControlledBase` stays a public abstract nested class (internal constructor taking the encoder; protected `stream`, `promise`, `endOfStream`, `padding`; `OperationComplete(Task)`). `FlowControlledData`/`FlowControlledHeaders`/`OutstandingLocalSettings` are private nested classes with the enclosing encoder passed in. `FlowController()` is not virtual. |
| `DefaultHttp2ConnectionDecoder` | public, virtual members | The `@Deprecated` constructor `(connection, encoder, frameReader, requestVerifier, autoAckSettings, autoAckPing)` has no `@deprecated` text: ported with `[Obsolete]`. `CalculateMaxHeaderListSizeGoAway` is protected virtual; `OnGoAwayRead0` internal. |
| `DecoratingHttp2ConnectionEncoder` | public, derives from `DecoratingHttp2FrameWriter` | virtual members (`CompressorHttp2ConnectionEncoder` overrides them) |
| `DecoratingHttp2ConnectionDecoder` | public | virtual members |
| `Http2ControlFrameLimitEncoder`, `Http2MaxRstFrameLimitEncoder`, `Http2MaxRstFrameDecoder`, `Http2MaxRstFrameListener`, `Http2EmptyDataFrameConnectionDecoder`, `Http2EmptyDataFrameListener` | internal sealed (package-private final in Java) | internal constructors; `FrameListener0()` internal (package-private for testing). |

- **Flow-controlled frames.** `FlowControlledData` creates its Transport `CoalescingBufferQueue` for `ctx.Channel`: Java
  uses `promise.channel()`, and a `TaskCompletionSource` has no channel. The outstanding local settings queue is a
  `LinkedList`, because `operationComplete` removes an arbitrary element (`Queue<T>` has no `Remove(T)`); the remote
  settings queue is a `Queue<Http2Settings>`.
- **Content length.** `catch (IllegalArgumentException)` around `HttpUtil.NormalizeAndGetContentLength` is
  `catch (ArgumentException)`, which the codec-http helper throws for every invalid value (it wraps its
  `FormatException`, as Java's `NumberFormatException` is an `IllegalArgumentException`). `TimeUnit`, `Ticker` and
  `X.class` follow the shared rows.

### Encoder and decoder not ported

| Test | Reason |
| --- | --- |
| `DefaultHttp2ConnectionEncoderTest.dataFramesShouldMergeUseVoidPromise` (CLR-inapplicable) | Asserts that void promises stay incomplete: Java's `unvoid()` returns a new promise, so the void promise is never completed; here `unvoid()` is identity and the same promise completes. The merge is covered by `dataFramesShouldMerge`. |

`writeHeadersUsingVoidPromise` and `asynchronousSettingsWriteFailureSupportsVoidPromise` are ported with
`Http2TestUtil.NewVoidPromise(IChannel)` (a promise whose failure calls `channel.Pipeline.FireExceptionCaught`);
`isVoid()` has no counterpart.

### Encoder and decoder test notes

- `atLeast(0)` has no Moq counterpart (`Times.AtLeast` needs a positive count), so the check runs only for a positive
  count; `InOrder` is a list recorded by callbacks; `Mock.Get(x)` reaches the mock of a mocked object
  ([Moq Quickstart](https://github.com/devlooped/moq/wiki/Quickstart)).
- A mocked `ByteBuf` is `new Mock<ByteBuf>(MockBehavior.Default, 0)` (the abstract class needs `maxCapacity`; a bare `0`
  binds to `Mock(MockBehavior)`); `release()` is `Release(1)`. `ByteBuf.IsReadable()` is not virtual, so the "readable"
  mock of `Http2EmptyDataFrameListenerTest` is a one-byte buffer.
- Generic `getProperty`/`setProperty` stubs use `It.IsAnyType` with `InvocationFunc`; the mocked `newKey()` returns null,
  which `IdentityHashMap` accepts and a `Dictionary` rejects, so the stub maps it to a sentinel key.
- `@EnumSource(Http2Error.class)` is `MemberData` over `Http2Error.ValueOf(0..13)`.
- Results (h5 scratch, 2026-11-25): `DefaultHttp2ConnectionEncoderTest` 54, `DefaultHttp2ConnectionDecoderTest` 79 (76
  methods, two theories with 2 and 3 rows), `Http2ControlFrameLimitEncoderTest` 5, `Http2MaxRstFrameLimitEncoderTest` 28
  (2 theories x 14 errors), the decorator and listener tests 13; all pass.

## Connection handler and builders

Part h6 of the base group: `Http2ConnectionHandler`, `AbstractHttp2ConnectionHandlerBuilder` and
`Http2ConnectionHandlerBuilder` (3 main files). Every original comment is in the C# files; all 50 + 22 declared tests of
`Http2ConnectionHandlerTest` and `Http2ConnectionRoundtripTest` are mapped (75 xUnit cases: the three `@ValueSource`
theories run twice each) and pass in three consecutive runs (about 5 s).

- **Outbound operations.** `Http2ConnectionHandler : ByteToMessageDecoder, IHttp2LifecycleManager,
  IChannelOutboundHandler`. `BindAsync`/`ConnectAsync` create the `TaskCompletionSource` that `PrefaceSendListener`
  completes after the preface is sent; `CloseAsync(ctx, CancellationToken)` returns the Task of its own promise
  (completed by `ClosingChannelFutureListener`; the token reaches the context only in the `decoupleCloseAndGoAway`
  close); disconnect, deregister, read and write pass to the context with the caller's `CancellationToken`.
- **Graceful shutdown timeout.** `ClosingChannelFutureListener` schedules its timeout with `ctx.Executor.ScheduleAsync`
  and a `CancellationTokenSource` (`timeoutTask.cancel(false)` is `Cancel(false)`), which schedules only on an
  `AbstractScheduledEventExecutor` (`src/Netty.NET.Common/Concurrent/EventExecutorSchedulingExtensions.cs`; see
  [Behaviour differences](#behaviour-differences)).
- **Package access.** The three protected constructors and `onConnectionError(...)` are `protected internal` (the builder
  and the tests reach them through package access); `channelReadComplete0` is `internal`. The switches on
  `Http2Stream.State` are if/else chains on reference equality.
- **Derived classes** (`HttpToHttp2ConnectionHandler`, `Http2FrameCodec`, `Http2MultiplexCodec`): `UserEventTriggered` is
  ByteToMessageDecoder's public override (override it again to see
  `Http2ConnectionPrefaceAndSettingsFrameWrittenEvent`); a class in this assembly overrides `OnConnectionError` with
  `protected internal override`.
- **Byte values.** `getUnsignedByte` is `GetByte`, which returns the unsigned octet; the frame type and flags are compared
  with non-negative constants, so the Java sign plays no role.

### Builders

- `AbstractHttp2ConnectionHandlerBuilder<T, B> where T : Http2ConnectionHandler where B :
  AbstractHttp2ConnectionHandlerBuilder<T, B>`; `Self()` casts through `object`. Nullable Java `Boolean`/`Integer`/`Long`
  fields are `bool?`/`int?`/`long?`.
- The protected non-final Java methods are `protected virtual` with the Java overload pairs
  (`InitialSettings()`/`InitialSettings(Http2Settings)`, `IsServer()`/`Server(bool)`, ...). Subclasses expose them as
  `public new` members that call `base` (shared row); the abstract `Build(decoder, encoder, initialSettings)` is
  implemented with `protected override`, and a public `build()` is `public new T Build()`.
- `initialHuffmanDecodeCapacity(int)` is `[Obsolete]` (see [Deprecated rule cases](#deprecated-rule-cases)); calls from
  obsolete members do not warn.

### Handler test notes

| Java test | Declared | xUnit cases | Notes |
| --- | --- | --- | --- |
| `Http2ConnectionHandlerTest` | 50 | 53 | Moq mocks; no `ctx.newPromise()` in the Transport port, so `eq(promise)` of a handler-created promise is `It.IsAny<TaskCompletionSource>()`; the mocked failed future is `Task.FromException(FAKE_EXCEPTION)`; the mocked executor is `RecordingEventExecutor` (records scheduled delays through `ValidateScheduled`, always in its event loop); `unsafe().close()` is a close from the handler's own context (goes to the head only). |
| `Http2ConnectionRoundtripTest` | 22 | 22 | Local transport (`MultiThreadIoEventLoopGroup` + `LocalIoHandler`); `IAsyncLifetime.DisposeAsync` for `@AfterEach`; `CountdownEvent` for `CountDownLatch` (a count-down at zero is a no-op through `SignalIfPending`); `Future.get()` in `assertThrows(ExecutionException)` is `Assert.ThrowsAnyAsync` of the cause on the Task. |

`canSendGoAwayUsingVoidPromise` and `writeRstStreamFor*StreamUsingVoidPromise` rely on `Http2TestUtil.NewVoidPromise`
firing `pipeline.FireExceptionCaught` when the promise fails (Java's `unvoid()` listener); `promise.isVoid()` has no
counterpart. `connectionErrorShouldStartShutdown` releases the captured GOAWAY debug buffer only while its count is
positive (see [Behaviour differences](#behaviour-differences)).

## Frame value types

The frame classes of part h2 and the foundation (`AbstractHttp2StreamFrame`, the ten `Default*Frame` classes of the
frame types and `DefaultHttp2SettingsAckFrame`), `Http2FrameStreamEvent`, `Http2FrameStreamException` and
`MaxCapacityQueue`, with the 15 frame and stream interfaces (`IHttp2Frame`, `IHttp2StreamFrame`, `IHttp2FrameStream`,
`IHttp2FrameStreamVisitor` and one per frame type). `Http2DefaultFramesTest` (4) is mapped and passes; part h2's 7 test
files (with the header and settings tests above) run 226 xUnit cases, all passing.

- **Stream frames.** `AbstractHttp2StreamFrame.Stream(IHttp2FrameStream)` is virtual and returns
  `AbstractHttp2StreamFrame`; each frame narrows it covariantly to its own type, and the interface members with other
  return types are implemented explicitly. `DefaultHttp2PushPromiseFrame.Stream(...)` returns `IHttp2PushPromiseFrame`
  and `PushStream(...)` returns `IHttp2StreamFrame`.
- **Holder frames.** The holder members of `DefaultHttp2DataFrame` (`Copy()`, `Retain()`, ...) return
  `DefaultHttp2DataFrame`, those of `DefaultHttp2GoAwayFrame` return `IHttp2GoAwayFrame`; `content()` is the `Content`
  property. The package-private constructors of `DefaultHttp2GoAwayFrame` and `DefaultHttp2PushPromiseFrame` are
  internal.
- **Http2FrameStreamEvent** keeps the nested `enum Type { State, Writability }`, read with `Type_()` (CS0102);
  **Http2FrameStreamException** derives from `Exception`.
- **MaxCapacityQueue\<E\>** (`java.util.Queue`) is an internal sealed `IEnumerable<E>` that wraps the BCL `Queue<E>` and
  keeps the Java method names (`Offer`, `Poll`, `Peek`, ...); `remove(Object)`, `removeAll` and `retainAll` rebuild the
  queue in order. `toString()` texts use `Http2CodecUtil.ToJavaString`.
- **Byte values.** `DefaultHttp2UnknownFrame.toString()`/`hashCode()` use the signed Java `byte frameType` (0xFF prints
  -1), kept as `(sbyte) frameType`. `Http2DefaultFramesTest` needed no change.

## HTTP/1.x conversion

`HttpConversionUtil` (part h2 of the base group) and the conversion group's `InboundHttp2ToHttpAdapter`,
`AbstractInboundHttp2ToHttpAdapterBuilder`, `InboundHttp2ToHttpAdapterBuilder`, `InboundHttpToHttp2Adapter`,
`HttpToHttp2ConnectionHandler`, `HttpToHttp2ConnectionHandlerBuilder` and `Http2SecurityUtil` (8 main files). All 78
declared tests are mapped and pass: `HttpConversionUtilTest` (45 methods, 49 xUnit cases),
`InboundHttp2ToHttpAdapterTest` (13), `HttpToHttp2ConnectionHandlerTest` (18) and `Http2SecurityUtilTest` (2);
`HttpConversionUtilFuzzTest` is library-not-ported. No code of this area depends on the sign of a Java `byte`.

- **HttpConversionUtil** is a public static class; its enum `ExtensionHeaderNames` is a nested sealed class (`Text()`,
  `Values()`), and the package-private helpers that the tests call (`SetHttp2Authority`, `IsValidScheme`,
  `HasSchemeAndAuthority`, `QueryOrFragmentStart`) are internal.
- **URI parsing.** `System.Uri` canonicalizes (host case, user info and default port left out of `Authority`), so the
  private `CreateUri`/`UriScheme`/`UriPort`/`UriAuthority` keep the parts of the
  [OpenJDK URI parser](https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/java/net/URI.java)
  that apply to `""` and `scheme://authority[/?#]`: an empty authority is allowed before `/`, `?`, `#`; a reg_name
  authority is accepted as registry-based; a bracketed authority must parse as `[userinfo@][IPv6][:port]`
  (`IPAddress.TryParse`); illegal characters, malformed `%`-escapes or a failed server parse throw `ArgumentException`;
  `getAuthority()` decodes `%`-escapes (UTF-8) outside brackets; `getPort()` is -1 if undefined. This was checked
  against `HttpConversionUtilTest`'s absolute-form cases (user info, IPv6, explicit port, `http://?x=1#frag`, empty
  query/fragment, `http://[bad host]`).
- **Deprecated codec-http members.** The deprecated `validateHeaders` message constructors are the `HttpHeadersFactory`
  constructors with `headersFactory()/trailersFactory().withValidation(v)`, which they call in Java;
  `KEEP_ALIVE`/`PROXY_CONNECTION` are `AsciiString.Cached("keep-alive")`/`("proxy-connection")`, codec-http's own
  replacement ([codec-http-porting.md](codec-http-porting.md), Core not ported).
- **Adapters.** `InboundHttp2ToHttpAdapter : Http2EventAdapter` has a `protected internal` constructor and keeps its
  protected extension points; its builders expose the protected virtual builder members with `public new`.
  `InboundHttpToHttp2Adapter` keeps the package-private static `handle(...)` as `internal static void Handle(...)`, which
  `Http2ServerUpgradeCodec` uses.
- **HttpToHttp2ConnectionHandler : Http2ConnectionHandler** has `protected internal` constructors and overrides
  `WriteAsync(ctx, msg, CancellationToken)`: non-HTTP messages go to `ctx.WriteAsync(msg, token)`; otherwise the handler
  creates the promise (`Http2CodecUtil.NewPromise`) that `SimpleChannelPromiseAggregator` completes and returns its
  Task. `HttpToHttp2ConnectionHandlerBuilder.InitialHuffmanDecodeCapacity` is `[Obsolete]` with the base member's text.
- **Http2SecurityUtil** is a static class whose `CIPHERS` is an `IList<string>` (`ReadOnlyCollection`). The JSSE names
  are the IANA names and the `TlsCipherSuite` member names ("Both field names and values are based on the TLS Cipher
  Suites list ... (IANA)", [TlsCipherSuite](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.tlsciphersuite?view=net-10.0);
  all nine are members), so the list goes to `SslContextBuilder.Ciphers` unchanged. Restricting SslStream needs a
  `CipherSuitesPolicy`, marked `UnsupportedOSPlatform("windows")`/`("android")`
  ([CipherSuitesPolicy](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.ciphersuitespolicy?view=net-10.0)),
  so a context built with these ciphers fails when it creates a handler there ([handler-porting.md](handler-porting.md));
  `SslContext.CipherSuites()` reports the filtered list everywhere. The static block is the static constructor
  ([static constructors](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors)).

### Conversion not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `HttpConversionUtilFuzzTest` | `library-not-ported` (Jazzer, `com.code-intelligence:jazzer-junit:0.30.0`, `com.code_intelligence.jazzer`) | "Jazzer is a coverage-guided, in-process fuzzer for the JVM platform"; `@FuzzTest` runs in regression mode by default and fuzzes only with `JAZZER_FUZZ=1` ([Jazzer README](https://raw.githubusercontent.com/CodeIntelligenceTesting/jazzer/main/README.md)). The BCL has no fuzzing engine (owner decision 2026-11-25, codec-http2 brief, decision 5); `HttpConversionUtilTest`'s absolute-form, origin-form, empty, query/fragment and malformed request-target cases run against the same `HttpConversionUtil.ToHttp2Headers`. `@FuzzTest` is not a JUnit `@Test`, so the inventory declares no method. |

### Conversion test notes

- **Transport.** `InboundHttp2ToHttpAdapterTest` and `HttpToHttp2ConnectionHandlerTest` run over the Local transport.
  `@AfterEach` is `IAsyncLifetime.DisposeAsync`, and the anonymous `Http2Runnable`s are a nested `ActionRunnable`.
- **Promises in tests.** `channel.write(msg, promise)`/`writeAndFlush(msg, promise)` are private helpers that complete the
  test's promise from the `WriteAsync`/`WriteAndFlushAsync` Task (`Http2CodecUtil.Cascade`).
  `awaitUninterruptibly(timeout)` is `Task.WhenAny(task, Task.Delay(timeout))`, because `Task.Wait(TimeSpan)` throws an
  `AggregateException` when "an exception was thrown during the execution of the task"
  ([Task.Wait](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.wait?view=net-10.0)).
- **Mocks.** Moq cannot proxy a private interface (Castle DynamicProxy), so `InboundHttp2ToHttpAdapterTest`'s private
  listener interfaces are the public nested `IHttpResponseListener` and `IHttpSettingsListener`. Captors are the
  recorded `Invocations`.
- **Deprecated message constructors.** `DefaultHttpRequest`/`DefaultFullHttpRequest`/`DefaultFullHttpResponse(...,
  boolean validateHeaders)` (always `true`) are the constructors without the flag (same default validating factories);
  `new DefaultHttpHeaders(false)` is the factory form; `KEEP_ALIVE`/`PROXY_CONNECTION` are local constants.
- **HttpConversionUtilTest.** `assertSame` of a Java String literal compares text (the headers hold an `ICharSequence`);
  `assertSame(TRAILERS, ...)` stays a reference check. `HttpHeaderValues.GZIP/TRAILERS` are qualified (CS0229 between
  `HttpHeaderNames.UPGRADE` and `HttpHeaderValues.UPGRADE`).
- **Http2SecurityUtilTest.** `engine.getEnabledCipherSuites()` is `SslContext.CipherSuites()` (no `newEngine` in the
  SslStream port); `assumeTrue` is `Assert.SkipUnless`; TLSv1.3 is supported on this host, so both tests run.

## Compression

The conversion group's `CompressorHttp2ConnectionEncoder` and `DelegatingDecompressorFrameListener` (2 main files). All
11 declared tests of `CompressorHttp2ConnectionEncoderTest` (5, 9 xUnit cases) and `DataCompressionHttp2Test` (6, 42
xUnit cases) are mapped and pass. No code of this area depends on the sign of a Java `byte`.

- **zstd.** zstd-jni is library-not-ported ([codec-compression-porting.md](codec-compression-porting.md)), so
  `ZstdOptions`, `ZstdEncoder`, `ZstdDecoder` and `Zstd.isAvailable()` have no C# type: both classes behave as Java with
  `Zstd.isAvailable() == false`, as codec-http's `HttpContentCompressor`/`HttpContentDecompressor`
  ([codec-http-porting.md](codec-http-porting.md)). The `zstdOptions` field of the encoder stays an always-null
  `ICompressionOptions`, and a `zstd` content-encoding is neither compressed nor decompressed.
- **brotli.** codec-compression ports Brotli on the BCL: `Brotli.IsAvailable()` is always true, the encoder creates
  `new BrotliEncoder(BrotliOptions)` (`BrotliOptions.parameters()` returns a brotli4j type and is not ported), and
  `DelegatingDecompressorFrameListener` keeps Java's `new BrotliDecoder(maxAllocation)`, which passes `maxAllocation` as
  the *input* buffer size (0 selects the default).
- **CompressorHttp2ConnectionEncoder.** `PromiseCombiner` follows the shared row. The `@Deprecated` constructor
  `(delegate, int, int, int)` has no `@deprecated` text and is ported with `[Obsolete]`; a window size or memory level
  other than 15/8 makes `ZlibCodecFactory.NewZlibEncoder` throw `NotSupportedException` (jzlib is not ported). The two
  anonymous `Http2ConnectionAdapter` instances of the constructors are one nested `StreamRemovedListener`; the
  package-private `cleanup(...)` is internal. `EmbeddedChannel.builder()...build()` is
  `new EmbeddedChannel.Builder()...Build()`.
- **DelegatingDecompressorFrameListener.** The inner `ConsumedBytesConverter` receives the enclosing listener; the
  anonymous classes are the nested `StreamRemovedListener` and `DecompressorHandler`. The package-private
  `decompressor(stream)` is `private`, because it returns the private `Http2Decompressor` ("The return type and parameter
  types of a method shall be at least as accessible as the method itself",
  [C# specification, Accessibility constraints](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/basic-concepts)).

### Compression not ported

| Member | Status | Reason |
| --- | --- | --- |
| `DelegatingDecompressorFrameListener(Http2Connection, Http2FrameListener)`, `(..., boolean strict)` | deprecated member (2 comments recorded) | The `@deprecated` texts name the replacements `(..., int maxAllocation)` and `(..., boolean strict, int maxAllocation)` (owner decision 2026-11-16). |

### Compression test notes

- **CompressorHttp2ConnectionEncoderTest.** `TestEmbeddedChannel` overrides the virtual
  `EmbeddedChannel.FinishAndReleaseAll()` and counts the calls as in Java (see [Shared-API
  requests](#shared-api-requests)). The mocked context returns `ImmediateEventExecutor.INSTANCE` for `ctx.Executor`.
- **DataCompressionHttp2Test** runs over NIO sockets (IPv4 wildcard bind, client to `NetUtil.LOCALHOST4`, as codec-http's
  `HttpClientCodecTest`). JUnit passes only the first value of each `paddingAndCompression()` row to
  `deflateEncodingWriteLargeMessage(int)` ("those additional arguments are ignored",
  [JUnit user guide](https://docs.junit.org/5.12.2/user-guide/index.html), Argument Count Validation); the xUnit theory
  takes a discard parameter `_`. `allocateUninitializedArray` is `GC.AllocateUninitializedArray`
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.gc.allocateuninitializedarray?view=net-10.0)).
- **Measured encodings.** The ZSTD rows stay and pass uncompressed (Java's rows compress with zstd-jni on the test class
  path). `encodingTooBigMessage` (512 KiB, `maxAllocation` 64 KiB): gzip is rejected (root cause
  `DecompressionException: Decompression buffer has reached maximum size: 65536`), brotli and snappy are bounded (65536
  and 32767), and zstd passes uncompressed as 16384-byte DATA frames.

## Multiplex, frame codec and upgrade

The multiplex group ports the child-channel layer: 15 of its 17 main units and 18 of its 21 test units; the other 2
main and 3 test units are the deprecated `Http2MultiplexCodec` family (`deprecated-not-ported`). Of the 198 declared
tests, 188 are mapped and pass (195 xUnit cases), 6 are CLR-inapplicable and the 4 of `Http2MultiplexCodecBuilderTest`
are not ported. The group's lead ported the core types and the test helpers, five helpers (multiplex h1-h5) the rest in
parallel; no base file was edited.

| Part | Main files | Test files |
| --- | --- | --- |
| lead | `IHttp2StreamChannel`, `Http2StreamChannelId`, `Http2StreamChannelOption`, `Http2MultiplexActiveStreamsException`, `Http2ChannelDuplexHandler`, `Http2FrameCodec`, `Http2FrameCodecBuilder`, `AbstractHttp2StreamChannel`, `Http2MultiplexHandler`, `Http2StreamChannelBootstrap` | the helpers `LastInboundHandler`, `TestChannelInitializer` |
| h1 | `Http2DataChunkedInput`, `Http2StreamFrameToHttpObjectCodec` | their tests, `Http2StreamChannelIdTest` |
| h2 | `Http2ServerUpgradeCodec`, `Http2ClientUpgradeCodec`, `CleartextHttp2ServerUpgradeHandler` | their tests, `Http2MultiplexClientUpgradeTest`, `Http2MultiplexHandlerClientUpgradeTest` |
| h3 | | `Http2FrameCodecTest`, `Http2FrameCodecSubClassTest`, `DefaultHttp2PushPromiseFrameTest`, `H2PrefaceTest` |
| h4 | | `Http2MultiplexTransportTest`, `Http2StreamChannelBootstrapTest` |
| h5 | | `Http2MultiplexTest`, `Http2MultiplexHandlerTest` |

### The child channel on the Transport's AbstractChannel

Java's `AbstractHttp2StreamChannel` implements `Channel` itself, with its own `Unsafe` (`Http2ChannelUnsafe`) and an
anonymous `DefaultChannelPipeline`. In the C# Transport the `DefaultChannelPipeline` only runs on `AbstractChannel`,
which folds `Channel.Unsafe` into the channel (owner decision 2026-11-16, [transport-porting.md](transport-porting.md)).
Since the owner decision of 2026-11-25 ([transport-porting.md](transport-porting.md), "Overridable Unsafe"; the CLR
comment above `AbstractChannel.CloseForcibly`) the folded Unsafe operations are `protected internal virtual` and
`IsWritable`, `BytesBeforeUnwritable`, `BytesBeforeWritable` are `virtual`; overrides in another assembly "have
protected access" ([protected internal](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal)).
`AbstractHttp2StreamChannel` therefore derives from `AbstractChannel`, keeps the Java `Http2ChannelUnsafe` as a nested
class with the Java code, and overrides the folded operations with its members:

| Java `Http2ChannelUnsafe` / channel member | C# override (protected unless noted) | Notes |
| --- | --- | --- |
| `write(msg, promise)` | `WriteTransportAsync` | Java's code with a `TaskCompletionSource` per write (`Http2CodecUtil.NewPromise`, no asynchronous continuations), completed from the parent write's own outcome; `setUncancellable()` is the write's `CancellationToken` check. |
| `flush()` | `FlushTransport` | Java's code (`writeDoneAndNoFlush`, parent read in progress, `flush0(parentContext())`). |
| `close(promise)`, `close(promise, error)` | `Close` | Java's body with `closeInitiated`. At `closePromise.setSuccess()`/`fireChannelInactiveAndDeregister(voidPromise(), wasActive)` it runs `AbstractChannel`'s close, which completes `CloseFuture` and runs `channelInactive` and, while registered, `channelUnregistered` in one later event-loop task (Java's `invokeLater` task); a never-registered channel completes `CloseFuture` through `AbstractChannel`'s `CloseForcibly` without events. `DoClose()` is empty. |
| `closeForcibly()` | `CloseForcibly` | `close(voidPromise())`. |
| `disconnect(promise)` | `DisconnectTransportAsync` | `close(promise)`. |
| `deregister(promise)` | `DeregisterTransportAsync` | `fireChannelInactiveAndDeregister(promise, false)`: the registered flag is `AbstractChannel`'s, so the `invokeLater` task is `AbstractChannel`'s deregistration (same steps); `safeSetSuccess` completes the promise from its Task. |
| `beginRead()` | `BeginRead` | Java's code. |
| `bind`, `connect` | `BindTransportAsync`, `ConnectTransportAsync` | `NotSupportedException`. |
| `register(eventLoop, promise)` | `RegisterAsync` (public) | The re-registration check, then `AbstractChannel.RegisterAsync`, which owns the registered flag and the event loop and fires `channelRegistered`/`channelActive` before its Task completes (Java completes the promise first). |
| `isWritable()`, `bytesBeforeUnwritable()`, `bytesBeforeWritable()` | `IsWritable`, `BytesBeforeUnwritable`, `BytesBeforeWritable` (public) | Java's `totalPendingSize`/`unwritable` fields with `Interlocked` for the atomic field updaters; `setWritable`/`setUnwritable`/`fireChannelWritabilityChanged`/`trySetWritable` are Java's code. No `ChannelOutboundBuffer` is involved. |
| `outboundBuffer()` (returns null) | `AbstractChannel.OutboundBuffer` exists but is never used | The writes never reach it. |
| `Channel.unsafe()` | internal `Unsafe()` | `Http2MultiplexHandler`, `Http2StreamChannelBootstrap` and the tests call `Unsafe().CloseForcibly()`/`Unsafe().Close(promise)` as Java does. |

Three parts stay `AbstractChannel`'s, as the CLR comment of the class records: the registration (`base.RegisterAsync`
owns `IsRegistered` and `EventLoop`, the parent's loop here), the close promise (`CloseFuture`, completed through
`base.Close()` or, for a never-registered channel, `base.CloseForcibly()`) and the deregistration
(`base.DeregisterTransportAsync()`).

Measured (2026-11-25) with scratch probes over the `Http2MultiplexHandler` pipeline of `Http2MultiplexTest` and with the
ported tests:

| Behaviour | Measured | Evidence |
| --- | --- | --- |
| write completion order | a write after a pending flow-controlled DATA frame completes while the DATA write is still pending | probe: DATA 2048 bytes over a 1024-byte window pending, a following frame complete |
| a write the parent completed at once | the write Task is complete when `WriteAsync` returns, without a flush or a pending task | probe |
| a child write pending in the parent at close | a DATA write pending at close (both sides ended the stream, so no RST_STREAM) stays pending and completes successfully after the WINDOW_UPDATE | probe (no `writeRstStream`) |
| writability when the parent becomes writable | `channelWritabilityChanged` fires synchronously (`WritabilityStates()` "false,true" before any pending task runs) | probe with a parent water mark of 256/512 and a child water mark of 100/200 |
| `IsWritable`/`BytesBeforeUnwritable` after close | true and high water mark + 1 (65537) | probe |
| continuation of a write Task | an `ExecuteSynchronously` continuation runs on the thread that completes the parent write | probe |

The writability numbers of `Http2MultiplexTest` (`bytesBeforeUnwritable()` 0 after a 16 MiB write, 4097 while the parent
is unwritable, the pending write completing only after the WINDOW_UPDATE), the event order, the RST suppression and the
re-entrant read/EOS tests (the 10000-frame stack-overflow test included) match Java. The full test project (all three
groups) runs 1549 xUnit cases, 1546 pass and 3 are skipped, identical in five full runs and eight runs of the multiplex
classes. The differences that remain are forced by `AbstractChannel` and listed under [Behaviour
differences](#behaviour-differences).

### Frame codec, multiplex handler and stream channels

| Java | C# | Notes |
| --- | --- | --- |
| `Http2StreamChannelOption<T> extends ChannelOption<T>` (private constructor, one constant) | static class `Http2StreamChannelOption` with `ChannelOption<bool> AUTO_STREAM_FLOW_CONTROL` | A static field of a generic class needs a type argument at every use; the Transport keeps its built-in options on the non-generic `ChannelOption` (`src/Netty.NET.Transport/Channels/ChannelOption.cs`). |
| `Http2FrameCodec` package access | internal members; the protected constructor and the protected `streamKey` are `protected internal` | Java's protected includes package access (`Http2FrameCodecBuilder`, `Http2MultiplexClientUpgradeTest`). The hooks the deprecated `Http2MultiplexCodec` overrides are `internal virtual`. |
| `DefaultHttp2FrameStream` field initializers using `this` | assigned in the constructor | "Keyword 'this' is not available in the current context" ([CS0027](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0027)). |
| `Http2FrameCodecBuilder` `@Override public` widenings | `public new` members calling `base` | As the base group's `Http2ConnectionHandlerBuilder` (shared row). |
| `Http2MultiplexHandler.registerDone(ChannelFuture)`, the shared close listener | `RegisterDone(Task, IChannel)`; one close listener per outbound child channel | A Task has no `channel()`. |
| `Http2StreamChannelBootstrap.open()`/`open(Promise)` | `OpenAsync()`/`OpenAsync(TaskCompletionSource<IHttp2StreamChannel>)` | Options and attributes as the Transport's `Bootstrap` keeps them. The `@Deprecated open0(ctx, promise)` (replacement `open()`/`open(Promise)` named) is the private `Open0`; its Javadoc is a deprecated-member comment disposition. |
| `SSLException` in `Http2MultiplexHandler.exceptionCaught` | `AuthenticationException` | [handler-porting.md](handler-porting.md), SSL. |

- `Http2StreamChannel` is the interface `IHttp2StreamChannel`. `Http2ChannelDuplexHandler.requireHttp2FrameCodec` fails a
  multiplexer added before its codec ("Http2FrameCodec was not found in the channel pipeline.", measured).
- **Test helpers.** `LastInboundHandler` uses `IConsumer<in T>` for the nested `Consumer` (contravariant, so the shared
  no-op consumer of object serves every reference type, as Java's unchecked cast does), generic readers such as
  `ReadInbound<T>()`, and `ExceptionDispatchInfo.Throw` for `throwException`. `TestChannelInitializer` is `[Sharable]`
  with the anonymous `ExtendedHandle` as a private nested class.

### Upgrade codecs

`Http2ServerUpgradeCodec`, `Http2ClientUpgradeCodec` and `CleartextHttp2ServerUpgradeHandler` (multiplex h2). Every
original comment is in the C# files, including the Javadoc of the two absent `Http2MultiplexCodec` constructors.

- **Interfaces.** The upgrade codecs implement codec-http's `IUpgradeCodec` interfaces with `virtual` methods
  ([codec-http-porting.md](codec-http-porting.md), Shared CLR adaptations); `Collection<CharSequence>` is
  `ICollection<ICharSequence>`, and a String passed as a CharSequence (the `HTTP2-Settings` value) a `StringCharSequence`.
  A codec from `Http2FrameCodecBuilder` is upgraded with `Http2ServerUpgradeCodec(Http2FrameCodec, params
  IChannelHandler[])`, the `Http2MultiplexHandler` among the handlers.
- **Encoding.** `ByteBufUtil.encodeString(alloc, CharBuffer.wrap(csq), UTF_8)` is `ByteBufUtil.EncodeString(alloc,
  csq.ToString(), Encoding.UTF8)`: `CharBuffer.wrap(csq)` is a read-only buffer whose "content ... will be the content of
  the given character sequence" ([CharBuffer](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/CharBuffer.html)).
  `Base64.encode/decode(buf, URL_SAFE)` is codec-base `Base64.Encode/Decode(buf, Base64Dialect.URL_SAFE)` with
  `using Netty.NET.Handler.Codec.Base64Codec;`.
- **Settings header.** `Http2Settings.entries()` is a `foreach` over the `Dictionary`, and `entry.value().intValue()`
  ("a narrowing primitive conversion", [Long](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Long.html))
  is `unchecked((int) entry.Value)`, which truncates the same way
  ([numeric conversions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions)).
- **Dictionary order (measured).** `decoder().localSettings()` adds six entries in the order initialWindowSize (4),
  maxConcurrentStreams (3), headerTableSize (1), maxFrameSize (5), maxHeaderListSize (6), pushEnabled (2). For
  `Http2FrameCodecBuilder.forClient().build()` the port writes `AAQAAP__AAMAAABkAAEAABAAAAUAAEAAAAYAACAAAAIAAAAB`
  (4=65535, 3=100, 1=4096, 5=16384, 6=8192, 2=1); Java's CharObjectHashMap (capacity 16, slot = key & 15, walked from
  slot 0, [KObjectHashMap.template](https://github.com/netty/netty/blob/64cc10f38ea5f5bd7eae48507817c66680d0afdc/common/src/main/templates/io/netty/util/collection/KObjectHashMap.template))
  gives the ascending order 1..6, `AAEAABAAAAIAAAABAAMAAABkAAQAAP__AAUAAEAAAAYAACAA`. Both carry the same settings;
  no test compares the header bytes.
- `CleartextHttp2ServerUpgradeHandler.PriorKnowledgeUpgradeEvent.INSTANCE` (private, read by the enclosing class) is
  `internal static readonly`: C# does not open a nested class's private members to the enclosing class
  ([handler-porting.md](handler-porting.md), Shared CLR adaptations, Access and names).

### Chunked input and stream-frame/HTTP object codec

`Http2DataChunkedInput` and `Http2StreamFrameToHttpObjectCodec` (multiplex h1).

- **Http2DataChunkedInput** implements the handler port's `IChunkedInput<IHttp2DataFrame>` and wraps an
  `IChunkedInput<ByteBuf>` (`src/Netty.NET.Handler/Stream/IChunkedInput.cs`), as codec-http's `HttpChunkedInput`. The
  `@Deprecated readChunk(ChannelHandlerContext)` override has no counterpart, because the interface member it overrides
  is deprecated with the named replacement `readChunk(ByteBufAllocator)` and not ported (owner decision 2026-11-16);
  callers pass `ctx.Alloc()`.
- **Http2StreamFrameToHttpObjectCodec** is `[Sharable]` over codec-base `MessageToMessageCodec<IHttp2StreamFrame,
  IHttpObject>` ([codec-base-porting.md](codec-base-porting.md), Type matching). The deprecated `new
  DefaultLastHttpContent(content, validateHeaders)` (not ported in codec-http) is the `HttpHeadersFactory` constructor
  with `TrailersFactory().WithValidation(validateHeaders)`, what it does in Java. The private `AttributeKey`'s pool name
  uses `Type.FullName` where Java uses `Class.getName()` (not observable). `pipeline().get(SslHandler.class)` is
  `Pipeline.Get<SslHandler>()` on the handler port's SslStream `SslHandler` ([handler-porting.md](handler-porting.md), SSL).

### Multiplex byte values

No code of the group depends on the sign of a Java `byte`:

| Place | Java | C# |
| --- | --- | --- |
| `Http2StreamFrameToHttpObjectCodec.isInformationalResponseHeaderFrame`/`isContentAlwaysEmpty` | `char` comparisons of the `:status` value | unchanged (UTF-16 chars, no byte) |
| `CleartextHttp2ServerUpgradeHandler.decode`: `ByteBufUtil.equals(CONNECTION_PREFACE, ..., in, ..., bytesRead)` | octet equality | `ByteBufUtil.Equals`, octet equality |
| `Http2DataChunkedInputTest.check`: `assertEquals(BYTES[i++], buffer.readByte())` with `BYTES[i] = (byte) i` | two signed bytes | two unsigned `System.Byte` values (owner decision: `ReadByte` is unsigned); equal for every octet |
| payloads of the two auto-read tests of `Http2MultiplexTransportTest`: `payloadBytes[i] = (byte) i` | signed bytes, compared as octets | the same low eight bits; octet comparison |
| tests: unknown frame types `(byte) 0xb`, `(byte) 20`, `(byte) 99`, `invalidPayloadLength`, `writeByte(0x4)` | constants below 0x80 | unchanged |

### Multiplex not ported

| Unit or method | Disposition | Reason or replacement |
| --- | --- | --- |
| `Http2MultiplexCodec`, `Http2MultiplexCodecBuilder` | `deprecated-not-ported` ("use Http2FrameCodecBuilder together with Http2MultiplexHandler") | `Http2FrameCodecBuilder` + `Http2MultiplexHandler`, `Http2StreamChannelBootstrap` |
| `Http2MultiplexCodecTest`, `Http2MultiplexCodecClientUpgradeTest` (fixture subclasses, no own methods) | `deprecated-not-ported` (families `Http2MultiplexCodecBuilder`, `Http2MultiplexCodec`) | `Http2MultiplexHandlerTest`, `Http2MultiplexHandlerClientUpgradeTest` run the same fixtures over the replacement |
| `Http2MultiplexCodecBuilderTest` (4 methods) | `deprecated-not-ported` | `Http2StreamChannelBootstrapTest`, `Http2MultiplexTransportTest`, `Http2MultiplexTest` |
| `Http2ServerUpgradeCodec(Http2MultiplexCodec)`, `(String, Http2MultiplexCodec)` | absent (the type is not ported); Javadoc kept next to a CLR comment | the `Http2FrameCodec` constructors |
| `Http2ServerUpgradeCodecTest.testUpgradeToHttp2MultiplexCodec`, `Http2ClientUpgradeCodecTest.testUpgradeToHttp2MultiplexCodec` | CLR-inapplicable | Only exercise the deprecated `Http2MultiplexCodecBuilder`; the replacement is the separate `testUpgradeToHttp2FrameCodecWithMultiplexer`. |
| `Http2MultiplexTransportTest.asyncSettingsAckWithMultiplexCodec` | CLR-inapplicable | Only exercises the deprecated `Http2MultiplexCodec`; with the replacement it is `asyncSettingsAckWithMultiplexHandler`, which is ported. |
| `Http2MultiplexTransportTest.testSSLExceptionOpenSslTLSv12`, `testSSLExceptionOpenSslTLSv13`, `testFireChannelReadAfterHandshakeSuccess_OPENSSL` | CLR-inapplicable (library-not-ported) | `SslProvider.OPENSSL` needs netty-tcnative; `IsAlpnSupported(OPENSSL)` is false and an OPENSSL context cannot be built ([handler-porting.md](handler-porting.md), Context layer). The JDK variants are ported. |

### Multiplex test notes

| Java test | Methods | Notes |
| --- | --- | --- |
| `Http2FrameCodecTest` | 43 | `setUp()`/`tearDown()` run from the constructor and `Dispose()`; both are private (xUnit1013) and keep their names because tests call `setUp(builder, settings[, preface])` again. The frame writer is `Http2TestUtil.MockedFrameWriter()` injected with the internal `Http2FrameCodecBuilder.FrameWriter(...)`; `same(x)` is a `ReferenceEquals` predicate. `f.await()` is an await with `ConfigureAwaitOptions.SuppressThrowing` ([ConfigureAwaitOptions](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.configureawaitoptions?view=net-10.0)); `@Timeout(5000 ms)` is `[Fact(Timeout = 5000)]`. Listeners are synchronous continuations of the codec's own promise Task, so they run at once as in Java. |
| `Http2FrameCodecSubClassTest` | 1 | The nested subclass calls the `protected internal` `Http2FrameCodec` constructor through `base(...)`. |
| `DefaultHttp2PushPromiseFrameTest` | 1 | Real NIO sockets; `setup()`/`shutdown()` run from `IAsyncLifetime`. As in Java, `send()` only writes the request and the graceful shutdown closes the channels at once. Measured with a temporary 2 s pause after the write (not in the port): PUSH_PROMISE (stream 3, promised 2), HEADERS and DATA "Meow" on stream 3, HEADERS and DATA "Meow, I am Pushed via HTTP/2" on stream 2, no exception. |
| `H2PrefaceTest` | 1 (3 rows) | The Transport's `Bootstrap.ConnectAsync()` yields the channel only when the connect completes, while the test builds the `Http2StreamChannelBootstrap` on `cf.channel()` at once, so `connect()` is its two Java steps: `Bootstrap.RegisterAsync()` (`initAndRegister()`), then `channel.ConnectAsync(Config.RemoteAddress)`; `Http2ConnectionHandler.ConnectAsync` (preface before the connect promise completes, netty/netty#16636) is on the path. Measured: the `Listener`-mode continuation runs with `InEventLoop()` true, as in Java. |
| `Http2MultiplexTransportTest` | 13 (9 mapped: 6 pass, 3 skipped as in the source; 4 CLR-inapplicable) | NIO and Local transports of the Transport port. `@Disabled`/`@DisabledOnOs(OS.WINDOWS)`/`assumeTrue` are `[Fact(Skip = ...)]`, `Assert.SkipWhen(OperatingSystem.IsWindows(), reason)`, `Assert.SkipUnless` ([xUnit v3 what's new](https://xunit.net/docs/getting-started/v3/whats-new)). `CountDownLatch.countDown()` is a private `CountDown(latch)`, because `CountdownEvent.Signal()` throws at zero ([Signal](https://learn.microsoft.com/en-us/dotnet/api/system.threading.countdownevent.signal?view=net-10.0)); with `Signal()` the second SETTINGS ACK of `asyncSettingsAck0` threw and the test timed out (measured). A failing `X509TrustManager` is a `RemoteCertificateValidationCallback` returning false. |
| `Http2StreamChannelBootstrapTest` | 2 | `testStreamIsNotCreatedIfParentConnectionIsClosedConcurrently` blocks the single event loop, then closes the client channel on it; `promise.get(3, SECONDS)` is `Assert.ThrowsAnyAsync<ClosedChannelException>` on `promise.Task.WaitAsync(3 s)`. `open0FailsPromiseOnHttp2MultiplexHandlerError` reaches the private `Open0` through `OpenAsync(promise)` with a mocked channel whose pipeline returns the mocked context for `Context(typeof(Http2MultiplexHandler))`. |
| `Http2MultiplexTest` (abstract `Http2MultiplexTest<C> where C : Http2FrameCodec`) with `Http2MultiplexHandlerTest` | 63 (61 `@Test`, 2 `@ParameterizedTest` with 3 rows) + 3 own | 70 xUnit cases. The `@EnumSource`/`@MethodSource` theories are `[MemberData]` over public members ([xUnit1016](https://xunit.net/xunit.analyzers/rules/xUnit1016)), and the package-private `RstFrameTestMode` is public because it is a theory parameter type ([CS0051](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0051)); overloads named like a test take the suffix `0` ([xUnit1024](https://xunit.net/xunit.analyzers/rules/xUnit1024)). `unsafe().close(voidPromise())`/`unsafe().close(promise)` are `Unsafe().Close(Http2CodecUtil.NewPromise())`/`Unsafe().Close(promise)` through the internal accessor; `parentChannel.unsafe().outboundBuffer().addMessage(...)` is the parent's public `OutboundBuffer.AddMessage`. Listeners added by a test are continuations on `EventExecutorTaskScheduler`; the child's write and close Tasks are its own promises without asynchronous continuations, so the listener has run when the awaited operation returns, as in Java. |
| `Http2ServerUpgradeCodecTest`, `Http2ClientUpgradeCodecTest` | 4 + 4 (3 + 3 mapped, 1 + 1 CLR-inapplicable) | `pipeline().remove(handler.getClass())` is `Remove<T>()` with T inferred in the generic helper `TestUpgrade<T>`. |
| `CleartextHttp2ServerUpgradeHandlerTest` | 7 | `UpgradeEvent.protocol()` is a `StringCharSequence` compared by text with "h2c". `usedHttp2MultiplexCodec` is ported with `new Http2FrameCodecBuilder(true).Build()` and `new Http2MultiplexHandler(childHandler)`, installed in that order by a nested `ChannelInitializer` (the multiplexer first fails its `handlerAdded`, measured); all assertions are unchanged. |
| `Http2MultiplexClientUpgradeTest` (abstract) with `Http2MultiplexHandlerClientUpgradeTest` | 2 + 0 | Abstract generic fixture `where C : Http2FrameCodec`; `codec.streamKey` needs the `protected internal` field. |
| `Http2DataChunkedInputTest`, `Http2StreamFrameToHttpObjectCodecTest`, `Http2StreamChannelIdTest` | 5, 44 (45 cases), 1 | The chunked-input mappings of codec-http's `HttpChunkedInputTest`. The two TLS tests of the codec use a JDK client `SslHandler`, which writes its ClientHello when the EmbeddedChannel becomes active, as in Java. Java object serialization of the ID is the Transport's System.Text.Json replacement: the test registers a nested `JsonConverter<Http2StreamChannelId>` that writes Java's two serialized fields `parentId` and `id` (a registered converter ranks above `[JsonConverter]`, [How to write custom converters](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to)). |

- **UpgradeEvent by reflection.** `upgradeEventNoRefCntError` and `upgradeWithoutFlowControlling` build codec-http's
  `UpgradeEvent` through `getDeclaredConstructor(...)`, `trySetAccessible` and `newInstance(...)` in Java too, so the C#
  `Type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, types)` and `ConstructorInfo.Invoke(...)` are
  faithful ([GetConstructor](https://learn.microsoft.com/en-us/dotnet/api/system.type.getconstructor?view=net-10.0),
  [ConstructorInfo.Invoke](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.constructorinfo.invoke?view=net-10.0));
  the `trySetAccessible` assumption has no counterpart ([common-clr-design.md](common-clr-design.md)).
- **TLS tests.** `testSSLExceptionJDKTLSv12`/`13` are statically skipped with the `@Disabled` text ("JDK SSLEngine does not
  produce an alert"); their body is ported. Measured with the cipher restriction removed (Windows has no
  `CipherSuitesPolicy`): the TLSv1.2 body fails (see [Behaviour differences](#behaviour-differences);
  [handler-porting.md](handler-porting.md), Measurements), the TLSv1.3 body passed in 3 of 3 runs, and the body of
  `testFireChannelReadAfterHandshakeSuccess_JDK` (`@DisabledOnOs(WINDOWS)`, netty issue 11542) passed in 3 of 3 runs on
  this host (Linux and macOS were not run).
- **`tortureCrossThreadSetAutoReadRace`.** `LockSupport.parkNanos(1000)` asks for at most 1 microsecond
  ([LockSupport](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/locks/LockSupport.html)),
  and the BCL has no timed sleep below a whole millisecond
  ([Thread.Sleep](https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.sleep?view=net-10.0): "uses the total
  number of whole milliseconds"). Measured per 4 MiB transfer (about 70 ms), 3 runs each: `Thread.Sleep(1)` 5 toggles,
  `Thread.SpinWait(20)` 47 758 to 67 075, `Thread.Yield()` 164 921 to 193 103; all passed. HotSpot on Windows coarsens
  such a park to 1 ms ("Must coarsen from nanos to millis", `Parker::park` in
  [os_windows.cpp](https://raw.githubusercontent.com/openjdk/jdk17u/master/src/hotspot/os/windows/os_windows.cpp)), but the
  source asks for "hammering setAutoRead() as fast as possible", so the port uses `Thread.Yield()`
  ([Thread.Yield](https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.yield?view=net-10.0)).
- **Java form.** The close and write-listener tests of `Http2MultiplexTest` keep the Java form: `callUnsafeCloseMultipleTimes` calls `Unsafe().Close(voidPromise())` and `Unsafe().Close(promise)` and awaits
  the promise and `closeFuture()`; `channelClosedWhenWriteFutureFails`, `channelClosedWhenChannelClosePromiseCompletes`
  and `channelClosedWhenCloseListenerCompletes` assert right after the awaited operation (the listener has run). The last
  one still adds its listener to the Task of `CloseAsync()`, because the `ChannelPromise` overloads fold into the Task
  members ([transport-porting.md](transport-porting.md), Decisions).

## Not ported: JVM-only units

| Unit | Status | Reason |
| --- | --- | --- |
| `package-info.java` | `jvm-only-replaced` | C# documentation comments must precede a type or member and cannot be added to a namespace; the summary ("Handlers for sending and receiving HTTP/2 frames.") is kept verbatim in its manifest entry and mapped in the namespace table above (owner decision 2026-11-16). |
| `module-info.yml` | `jvm-only-replaced` | Input of the plugin that generates the JPMS descriptor of `io.netty.codec.http2` (it also requires brotli4j); `Netty.NET.Handler.Codec.Http2.csproj` references `Netty.NET.Handler.Codec.Http`, and `InternalsVisibleTo` opens internals only to the test assembly. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata of the codec-http2 handlers; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter` ([DynamicallyAccessedMembersAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.codeanalysis.dynamicallyaccessedmembersattribute?view=net-10.0)). |
| `META-INF/native-image/.../native-image.properties` | `jvm-only-replaced` | `--initialize-at-run-time` for `Http2CodecUtil`, `Http2ClientUpgradeCodec`, `Http2ConnectionHandler` and `DefaultHttp2FrameWriter`, whose static state holds buffers that must not be baked into the image ([build configuration](https://www.graalvm.org/latest/reference-manual/native-image/overview/BuildConfiguration/), [class initialization](https://www.graalvm.org/latest/reference-manual/native-image/optimizations-and-performance/ClassInitialization/)); NativeAOT describes no build-time class initialization option ([Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)), and C# static constructors run before first use at run time ([static constructors](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors)). |
| `NativeImageHandlerMetadataTest` (1 test) | `jvm-only-replaced` | Generates the handler reflection records and compares them with the committed JSON (owner decision 2026-11-16, as for the Transport `ChannelHandlerMetadataUtil`). |
| `junit-platform.properties` | `jvm-only-replaced` | Concurrent JUnit execution of classes and methods; xUnit v3 runs a collection per test class in parallel and the tests of one class sequentially ([xUnit](https://xunit.net/docs/running-tests-in-parallel)), as codec-http recorded. |

## Owner review items

### Deprecated rule cases

- **Ported with `[Obsolete]`** because no replacement is named: the `DefaultHttp2ConnectionDecoder` constructor
  `(connection, encoder, frameReader, requestVerifier, autoAckSettings, autoAckPing)` (no `@deprecated` text),
  `initialHuffmanDecodeCapacity(int)` of `AbstractHttp2ConnectionHandlerBuilder` ("Huffman decoding no longer depends on
  having a decode capacity.") and its overrides in `Http2ConnectionHandlerBuilder` and
  `HttpToHttp2ConnectionHandlerBuilder` (no `@deprecated` text; they take the base member's `[Obsolete]` text), and the
  `CompressorHttp2ConnectionEncoder(delegate, int, int, int)` constructor (no `@deprecated` text).
- **Not ported** because the `@deprecated` text names a replacement: the `DelegatingDecompressorFrameListener`
  constructors `(connection, listener)` and `(connection, listener, strict)`, replaced by the `maxAllocation` overloads;
  their 2 comments are deprecated-member dispositions. The deprecated codec-http members that Java calls are replaced as
  described in [HTTP/1.x conversion](#http1x-conversion).
- **The `Http2MultiplexCodec` family** (two main units and three tests) is `deprecated-not-ported`: "use
  Http2FrameCodecBuilder together with Http2MultiplexHandler". The two `Http2ServerUpgradeCodec(Http2MultiplexCodec)`
  constructors are not `@Deprecated` themselves but are absent, because their parameter type is not ported. The
  `@Deprecated` `Http2StreamChannelBootstrap.open0(ctx, promise)` (replacement `open()`/`open(Promise)` named) is the
  private `Open0`, its Javadoc the third deprecated-member disposition.
- **Tests rewritten over the replacement.** `usedHttp2MultiplexCodec`, `asyncSettingsAckWithMultiplexHandler` (whose
  client used `Http2MultiplexCodecBuilder.forClient(...)`) and `open0FailsPromiseOnHttp2MultiplexHandlerError` run over
  `Http2FrameCodecBuilder` + `Http2MultiplexHandler` (or `OpenAsync`) with unchanged assertions; the three tests that only
  exercise the deprecated family are CLR-inapplicable (see [Multiplex not ported](#multiplex-not-ported)).

### Behaviour differences

- **GOAWAY debug data.** `Http2CodecUtil.toByteBuf(ctx, cause)` sends `getMessage()` as GOAWAY debug data and an empty
  buffer when it is null. A CLR exception always has a `Message` (the default text "Exception of type ... was thrown."
  when created without one), so a connection error from an `Http2Exception` without a message sends that text where
  Java sends empty debug data (measured in `Http2ConnectionHandlerTest.connectionErrorShouldStartShutdown`), as
  codec-http's `SpdyProtocolException` does ([codec-http-porting.md](codec-http-porting.md), Owner review items).
- **Debug build for the assert tests.** The six `DefaultHttp2RemoteFlowControllerTest` tests that assert Java's
  `AssertionError` (run for the uniform and the WFQ distributor) check `Debug.Assert` and pass only against a Debug
  build of the library.
- **Stream properties.** `DefaultHttp2Connection`'s property lookup returns the value when it `is V` and `default(V)`
  otherwise, where Java's erased cast returns the stored object of any type.
- **Graceful shutdown timeout.** `ClosingChannelFutureListener` uses `ScheduleAsync`, which schedules only on an
  `AbstractScheduledEventExecutor`; on another executor the timeout never fires (the Task faults with
  `NotSupportedException`), while the close on GOAWAY completion still happens.
- **SETTINGS order.** SETTINGS frames and the `HTTP2-Settings` header list the entries in `Dictionary` insertion order,
  not Java's hash-slot order (owner decision 2026-11-25). Measured for `Http2FrameCodecBuilder.forClient().build()`: the
  header lists the settings 4, 3, 1, 5, 6, 2 where Java lists 1..6, with the same values (see [Upgrade
  codecs](#upgrade-codecs)).
- **Child channel** ([The child channel on the Transport's
  AbstractChannel](#the-child-channel-on-the-transports-abstractchannel)); each remaining difference is forced by
  `AbstractChannel` and recorded in a CLR comment:
  - `CloseFuture` is `AbstractChannel`'s `TaskCompletionSource` created with `RunContinuationsAsynchronously`: a
    default-scheduler continuation of it runs asynchronously (measured); a listener on the event-loop scheduler
    (`Http2CodecUtil.AddListener`) runs at once on the loop (measured), which gives Java's listener timing.
  - The registration Task completes after `channelRegistered`/`channelActive` were fired; Java completes the promise
    first.
  - `EventLoop` is the loop of the registration (the parent's) and throws before it.
- **CLR-inapplicable multiplex tests.** Three only exercise the deprecated `Http2MultiplexCodec` family and three need
  OpenSSL (netty-tcnative); see [Multiplex not ported](#multiplex-not-ported).
- **TLSv1.2 SSL exception under SslStream.** The `@Disabled` `testSSLExceptionJDKTLSv12` body, run with the cipher
  restriction removed, fails with its own "TLSv1.3 expected" in 3 of 3 runs (SslStream completes the client handshake
  before the server rejects the client certificate), so it would stay disabled even without the annotation.
- **`parkNanos(1000)` is `Thread.Yield()`** in `tortureCrossThreadSetAutoReadRace` (maximal contention, as the source's
  comment asks; measured 164 921 to 193 103 toggles per transfer), rather than `Thread.Sleep(1)`, which is what HotSpot
  does on Windows (5 toggles per transfer here).
- **Void promises.** `DefaultHttp2ConnectionEncoderTest.dataFramesShouldMergeUseVoidPromise` is CLR-inapplicable: there
  is no void promise and `unvoid()` is identity, so the promise completes.
- **Http2SecurityUtil on Windows and Android.** A context built with `CIPHERS` fails when it creates a handler there,
  because restricting SslStream needs the unsupported `CipherSuitesPolicy`; `SslContext.CipherSuites()` still reports the
  filtered list, which `Http2SecurityUtilTest` checks instead of an engine's enabled suites.
- **Compression.** A `zstd` content-encoding is neither compressed nor decompressed (zstd-jni is library-not-ported);
  `CompressorHttp2ConnectionEncoder` cannot compress with a window other than 15 or a memory level other than 8 (jzlib).
- **Texts.** `X.class` in exception texts prints the full `Type` name without Java's "interface "/"class " prefix.
- **Tests.** `DataCompressionHttp2Test`'s ZSTD rows test the uncompressed pass-through; `HttpConversionUtilTest`'s two
  String `assertSame` checks compare text; the seeded `java.util.Random` sequences of the HPACK tests are not reproduced
  (the round trips do not depend on them); `Http2MultiplexTest.channelClosedWhenCloseListenerCompletes` still adds its
  listener to the Task of `CloseAsync()`, because the `ChannelPromise` overloads fold into the Task members.

### Byte-sign spots

Java's sign is kept with `(sbyte)` in three places: the header representation byte of `HpackDecoder.decode` (`b < 0`
is the indexed header bit), the frame type in `DefaultHttp2FrameReader`'s exception texts, and
`DefaultHttp2UnknownFrame.toString()`/`hashCode()`. Everywhere else (HPACK masks, the stream and WFQ bit sets, the frame
writer's stream dependency, `Http2ConnectionHandler`'s `GetByte`, the ASCII header checks) the unsigned `System.Byte`
gives Java's results; the conversion, compression and multiplex code has no sign-dependent byte arithmetic, and the
multiplex tests' byte literals are below 0x80 or compared as octets. The details are in the area tables.

### API shape

- **Async suffixes.** Every Java method that returns a future takes the `Async` suffix and returns the promise's Task:
  `WriteDataAsync`, `WriteHeadersAsync`, `WritePriorityAsync`, `WriteRstStreamAsync`, `WriteSettingsAsync`,
  `WriteSettingsAckAsync`, `WritePingAsync`, `WritePushPromiseAsync`, `WriteGoAwayAsync`, `WriteWindowUpdateAsync`,
  `WriteFrameAsync`, `ResetStreamAsync`, `GoAwayAsync` and `IHttp2Connection.CloseAsync(TaskCompletionSource)`; the
  handlers' outbound members are the Transport's `*Async` members.
- **Trailing underscores (CS0102).** `IHttp2Stream.State_()`, `Http2Exception.ShutdownHint_()`,
  `Http2FrameStreamEvent.Type_()`, the distributors' `State_(...)` and the nested `DefaultHttp2Connection.ConnectionStream_`.
- **Http2Settings** derives from `Dictionary<char, long>`: the Java getters return `long?`/`int?`/`bool?`, and
  `Equals`/`GetHashCode` are overridden with CharObjectHashMap's content equality.
- **IHttp2Headers.ValueIterator(ICharSequence)** is declared on the interface, because codec-base `IHeaders` has no
  value iterator; the other re-declarations of `Http2Headers` are inherited.
- **Test interfaces.** `InboundHttp2ToHttpAdapterTest` declares its listener interfaces as the public nested
  `IHttpResponseListener` and `IHttpSettingsListener` (private in Java), because Moq (Castle DynamicProxy) cannot proxy a
  private interface.
- **Accessibility.** `protected internal`: the constructors of `Http2ConnectionHandler`, `HttpToHttp2ConnectionHandler`
  and `InboundHttp2ToHttpAdapter`, `Http2ConnectionHandler.OnConnectionError` and `Http2FrameListenerDecorator.listener`;
  the builders expose the base members with `public new`. `IHttp2Connection.IEndpoint` and `IEndpoint<F>` split the
  wildcard endpoint type. `DefaultHttp2Headers.DefaultHtt2NameValidator()` keeps Java's spelling.
- **Multiplex.** `Http2StreamChannelOption` is a static class with `ChannelOption<bool> AUTO_STREAM_FLOW_CONTROL`;
  `Http2StreamChannelBootstrap.open()` is `OpenAsync()`; the `Http2FrameCodec` constructor and `streamKey` are
  `protected internal`, and the hooks of the not-ported `Http2MultiplexCodec` are `internal virtual`;
  `AbstractHttp2StreamChannel` derives from the Transport's `AbstractChannel`, overrides its folded Unsafe operations with
  `protected override` and exposes `Channel.unsafe()` as the internal `Unsafe()` (see [Shared-API
  requests](#shared-api-requests) item 3). `Http2MultiplexTest.RstFrameTestMode` is public (theory parameter type).
- **Http2StreamChannelId** has no JSON contract, unlike the Transport's `DefaultChannelId` and `EmbeddedChannelId`
  (`[JsonConverter(typeof(Converter))]`), so `Http2StreamChannelIdTest` keeps its converter; it could move to the type as
  a nested `Converter` if that precedent should apply to every channel ID, and the test would work unchanged.

## Shared-API requests

Done on 2026-11-25:

1. **Test declarations without a space before `throws`.** `DefaultHttp2HeadersDecoderTest.java` line 198 declares
   `...(int illegalFirstChar)throws Exception {`, which the test-declaration regex of the checker and the generator
   (`[ \t\r\n]+throws`) rejected ("Cannot account for every declared JUnit method exactly once"); `[ \t\r\n]*throws`
   accepts it. The lead fixed it in the codec, Transport and Buffer checkers (`Test-CodecPortingManifest.ps1`,
   `Test-TransportPortingManifest.ps1`, `Update-BufferPortingManifest.ps1`).

2. **A virtual `EmbeddedChannel.FinishAndReleaseAll()`.** Resolved 2026-11-25: the lead made
   `EmbeddedChannel.FinishAndReleaseAll()` virtual (`src/Netty.NET.Transport/Channels/Embedded/EmbeddedChannel.cs`, CLR
   comment), as Java's public non-final method; `CompressorHttp2ConnectionEncoderTest`'s `TestEmbeddedChannel` overrides
   it and counts the calls as in Java.

3. **Overridable Unsafe members of the Transport's `AbstractChannel`.** Resolved 2026-11-25 (owner approval): the lead
   made the folded Unsafe operations (`WriteTransportAsync`, `FlushTransport`, `Close`, `CloseForcibly`, `BeginRead`,
   `BindTransportAsync`, `ConnectTransportAsync`, `DisconnectTransportAsync`, `DeregisterTransportAsync`)
   `protected internal virtual` and `IsWritable`, `BytesBeforeUnwritable` and `BytesBeforeWritable` `virtual`
   ([transport-porting.md](transport-porting.md), "Overridable Unsafe (owner decision 2026-11-25)"). The overrides in
   codec-http2 are `protected override`: "If you define the derived class in a different assembly from the base class,
   overridden members have protected access"
   ([protected internal](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal)).
   `AbstractHttp2StreamChannel` overrides them with the code of Java's `Http2ChannelUnsafe` (see [The child channel on
   the Transport's AbstractChannel](#the-child-channel-on-the-transports-abstractchannel)). Java's
   `AbstractChannel.AbstractUnsafe` is a protected nested class, which subclasses in other packages extend and return
   from the protected abstract `newUnsafe()` (`io.netty.channel.nio.AbstractNioChannel.AbstractNioUnsafe extends
   AbstractUnsafe`). Noted, not requested: a synchronous-continuation or virtual `CloseFuture` in the Transport would
   remove the remaining `CloseFuture` timing difference.
