# resolver-dns porting

Netty.NET ports `netty-resolver-dns` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Resolver.Dns`, with its original tests in
`test/Netty.NET.Resolver.Dns.Tests`. The [manifest](resolver-dns-porting-manifest.json) records every pinned file of
`resolver-dns/src` (84 files: 61 main and 20 test Java sources, `module-info.yml` and two native-image resources) with
its C# counterpart, its CLR adaptations and evidence, or the reason it is not ported. No entry is `pending`. Check it
with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module resolver-dns
```

The checker verifies the file inventory and source hashes, that every original comment is present in the mapped C#
files, that every declared JUnit test is mapped to a C# test of the same name (case may differ) or carries a recorded
disposition, and the facts and evidence of every unit and member that is not ported. It fails on a `pending` entry.

The port was done on 2026-11-25 in three groups: **infra** (caches, name server addresses, streams and providers,
comparators, query lifecycle observers, `DnsNameResolverException`, `DnsQueryIdSpace`, `DnsAddressDecoder`), **core**
(`DnsNameResolver` and its builder, the query and resolve contexts, `InflightNameResolver`, the address resolver groups)
and **server** (the test DNS server on an apacheds stand-in, then the tests that run against it).

| Kind | clr-adapted | verified | jvm-only-replaced | Total |
| --- | --- | --- | --- | --- |
| main Java sources | 60 | 0 | 1 (`package-info.java`) | 61 |
| test Java sources | 2 (`DnsNameResolverTest`, `TestDnsServer`) | 17 | 1 (`NativeImageHandlerMetadataTest`) | 20 |
| metadata (`module-info.yml`, `reflect-config.json`, `native-image.properties`) | 0 | 0 | 3 | 3 |
| **total** | 62 | 17 | 5 | 84 |

| Group | Main files | Test files | Declared original tests | Mapped | CLR-inapplicable | Not ported |
| --- | --- | --- | --- | --- | --- | --- |
| infra | 46 | 10 | 61 | 61 | 0 | 0 |
| core | 14 | 4 | 13 | 13 | 0 | 0 |
| server | 0 | 5 (`TestDnsServer` and 4 test classes) | 119 | 118 | 1 | 0 |
| JVM-only units | 1 (`package-info.java`) + 3 metadata | 1 | 1 | 0 | 0 | 1 |
| **total** | 64 | 20 | 194 | 192 | 1 | 1 |

Of the 194 declared original tests, 192 are mapped to C# tests with the same names, 1 is CLR-inapplicable
(`DnsNameResolverTest.testInstanceWithNullPreferredAddressType`, see [Test notes](#test-notes)) and 1 is not ported
(`NativeImageHandlerMetadataTest.collectAndCompareMetadata`, JVM-only). The manifest also records 9 deprecated-member
and 23 JVM-only-API comment dispositions (see [Not ported](#not-ported)).

The C# run of `Netty.NET.Resolver.Dns.Tests` executes 323 test cases: 310 pass, 0 fail and 13 are skipped. Theories
run once per data row (most `DnsNameResolverTest` methods once per `DnsNameResolverChannelStrategy`), so there are more
cases than methods. The 13 skips are:

- 2 disabled by the source: `DnsNameResolverClientSubnetTest.testSubnetQuery` (`@Disabled`, it queries real DNS
  servers) and `ResolvConfTest.readSystem` (`@DisabledOnOs(WINDOWS)`, `Assert.SkipWhen(OperatingSystem.IsWindows())`).
- 8 skipped by the source's assumption on this host: `testResolveLocalhostIpv6`, `testResolveHostNameIpv6`,
  `testResolveAllLocalhostIpv6` and `testResolveAllHostNameIpv6`, once per channel strategy, because
  `DEFAULT_RESOLVE_ADDRESS_TYPES` is not `IPV6_PREFERRED` here.
- 2 skipped on Windows by the source's `assumeThat(PlatformDependent.isWindows()).isFalse()`:
  `IsolatedDnsNameResolverTest.testResolveLocalhostViaDnsWhenDisabledOnNonWindows`, once per channel strategy.
- 1 CLR-inapplicable: `testInstanceWithNullPreferredAddressType`, kept compiled with `[Fact(Skip = ...)]`.

## Owner decisions

- The project is `Netty.NET.Resolver.Dns`, the namespace root of `io.netty.resolver.dns` (the 2026-11-19 rule). It
  references `Netty.NET.Handler.Codec.Dns` and `Netty.NET.Handler`, the modules that `module-info.yml` requires besides
  `java.naming`.
- 2026-11-25: the test DNS server, which extends the apacheds-protocol-dns `DnsServer` on Apache MINA, is ported on a
  C# stand-in of the apacheds-protocol-dns API the tests use (same type names, test-only, 45 files under
  `test/Netty.NET.Resolver.Dns.Tests/ApacheDs/`), so the tests that run against it are ported 1:1, as pkitesting was
  ported on the BCL instead of Bouncy Castle. The stand-in is built on BCL UDP sockets, and its wire codec reproduces the
  apacheds encoder and decoder on MINA's `IoBuffer`, because the tests depend on apacheds-specific behaviour (AAAA
  records appended after the sections, names decoded without the root dot, queries with an OPT record not answered).
  codec-dns is used on the client side only, as in the resolver. See [The apacheds stand-in](#the-apacheds-stand-in).
- 2026-11-25: `java.net.UnknownHostException` is Common's `Netty.NET.Common.Internal.UnknownHostException` (an
  `IOException` that keeps its cause) across Resolver, Transport and resolver-dns, because resolver-dns records the
  reason of a failed lookup as the cause (`DnsErrorCauseException`, a timeout, a transport setup failure) and
  `SocketException` cannot carry one (see [transport-porting.md](transport-porting.md)).
- `io.netty.util.collection` stays the BCL `Dictionary` (owner decision 2026-11-19, confirmed 2026-11-25).
  `HashMap`/`IntObjectHashMap` are
  `Dictionary`; their iteration order only feeds debug logs, except the one case under [Addresses, host names and
  order](#addresses-host-names-and-order).
- A `@Deprecated` member whose `@deprecated` text names a replacement is not ported, and a test that only exercises
  such a member is CLR-inapplicable (owner rule 2026-11-16). The members are listed under [Not ported](#not-ported).

## Namespaces and assembly

The project, folder and assembly are `Netty.NET.Resolver.Dns`; the tests use `Netty.NET.Resolver.Dns.Tests`. The
project references `Netty.NET.Handler.Codec.Dns` (which brings codec-base, Transport, Resolver, Buffer and Common) and
`Netty.NET.Handler` (`LogLevel` for `LoggingDnsQueryLifecycleObserver`); `InternalsVisibleTo` opens internals only to
the test assembly, which reaches the package-private members the tests use (the 25-argument `DnsNameResolver`
constructor, `resolveLocalhostWithoutDns`, `DnsResolveContext.TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS` and others).
`java.naming` (JNDI) has no CLR counterpart (see `DirContextUtils` under [JVM-only APIs](#jvm-only-apis)).

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.resolver.dns` | `Netty.NET.Resolver.Dns` | An alternative to Java's built-in domain name lookup mechanism that resolves a domain name asynchronously, which supports the queries of an arbitrary DNS record type as well. |
| test `io.netty.resolver.dns` | `Netty.NET.Resolver.Dns.Tests` | (no test package-info) |
| test dependencies `org.apache.directory.server.dns.*`, `org.apache.directory.server.protocol.shared.transport`, `org.apache.directory.api.util`, `org.apache.mina.*` | `Netty.NET.Resolver.Dns.Tests.ApacheDs` (test-only stand-in) | |

## Shared CLR adaptations

The adaptations below apply across the module; the manifest entries list what is specific to each file.

| Java | C# | Notes and evidence |
| --- | --- | --- |
| `resolve`/`resolveAll`/`query` returning `Future`, overloads that take a `Promise`, `close()` | `ResolveAsync`/`ResolveAllAsync`/`QueryAsync` returning `Task<...>` with a trailing `CancellationToken`; `Dispose()` | The Resolver API policy (`contractPolicy` of [resolver-porting-manifest.json](resolver-porting-manifest.json)): each `Promise` overload folds into the Task-returning overload with its Javadoc beside it. `Future<List<InetAddress>>` is `Task<IReadOnlyList<IPAddress>>`, `Future<AddressedEnvelope<DnsResponse, InetSocketAddress>>` is `Task<IAddressedEnvelope<IDnsResponse, IPEndPoint>>`. The protected `doResolve/doResolveAll(inetHost, additionals, promise, cache)` hooks are `protected virtual DoResolveAsync/DoResolveAllAsync(inetHost, additionals, cache, token)`. |
| `Promise`, `Future` | `TaskCompletionSource<T>` with `RunContinuationsAsynchronously`, its `Task` | [handler-porting.md](handler-porting.md) "Promise, ChannelPromise, Future". `isDone/isCancelled/isSuccess` are `IsCompleted/IsCanceled/IsCompletedSuccessfully`, `trySuccess/tryFailure` `TrySetResult/TrySetException`, `setSuccess/setFailure` `SetResult/SetException`, `cancel(false)` `TrySetCanceled()`; `future.cause()` is the internal `DnsNameResolver.Cause(Task)` (the inner exception of a faulted Task, a `TaskCanceledException` for a canceled one, else null). |
| Listeners | continuations on Common's `EventExecutorTaskScheduler` over the event loop | The `DefaultPromise.notifyListeners` timing ([DefaultPromise](https://github.com/netty/netty/blob/64cc10f38ea5f5bd7eae48507817c66680d0afdc/common/src/main/java/io/netty/util/concurrent/DefaultPromise.java)). A listener that throws faults its continuation Task where `DefaultPromise` logs a warning. In `doQuery`'s consolidation the release listener runs through `Executor.Execute(...)`, because a continuation added to a completed Task runs at once, while `DefaultPromise` appends a listener added during notification after the ones being notified; the release therefore still follows the other waiters' retains. |
| `Future.cancel(...)` by the caller | the caller's `CancellationToken` | [CancellationToken.Register](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.register?view=net-10.0) calls [TrySetCanceled](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1.trysetcanceled?view=net-10.0) on the caller's promise, which the resolve contexts read as `originalPromise.isCancelled()`; the registration is disposed when the promise completes (`DnsNameResolver.NewPromise<T>(token)`). A canceled resolve is a canceled Task (`TaskCanceledException`), where Java throws `CancellationException`. |
| `ChannelFactory<? extends DatagramChannel/SocketChannel>`, `Class` setters, `ChannelFuture`, `ChannelPromise` | `Func<IDatagramChannel>`/`Func<ISocketChannel>`, `ReflectiveChannelFactory<T>(Type).NewChannel`, `Task<IChannel>`, the Task a write returns | As `Bootstrap.Channel(Type)` builds it ([transport-porting.md](transport-porting.md)). A faulted `Task<IChannel>` has no channel, so the transport setup failure message ends after `couldn't setup transport` (the cause is the inner exception) and the TCP fallback debug log prints `null` for the channel. `new InetSocketAddress(0)` is `IPEndPoint(IPAddress.Any, 0)` (NioDatagramChannel binds it as IPv6Any on a dual-mode socket). `AttributeKey<Boolean>` is `AttributeKey<object>`; the deprecated `DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION` is used under `#pragma warning disable CS0612` (the source's `@SuppressWarnings("deprecation")`). |
| `executor()` with the covariant `EventLoop` | `protected internal new IEventLoop Executor` | Hides `SimpleNameResolver.Executor`. |
| Package access | `internal`; `protected internal` for `newRedirectDnsServerStream` | The tests override `newRedirectServerAddress` with `internal override` and `newRedirectDnsServerStream` with `protected internal override` (CS0507 otherwise, measured). The protected getters of the sealed `DnsNameResolverBuilder` are internal (CS0628). |
| Generic statics of `DnsResolveContext<T>` | the non-generic static class `DnsResolveContext` in the same file, imported with `using static` | Each closed constructed type has its own static fields ([C# spec 15.3.8](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#1538-static-and-instance-members)). The static `redirectAuthoritativeDnsServerCache(...)` is `RedirectAuthoritativeDnsServerCache_` (CS0102 with the nested class); `cache(...)` of `Cache<E>` is `Cache_` (CS0542). |
| `ScheduledFuture` of `loop.schedule(...)` in `Cache` | a private `ScheduledFuture` holding the `CancellationTokenSource` of `loop.ScheduleAsync` and the deadline from `loop.Ticker()` | `getDelay(SECONDS)` reads the deadline ([Delayed](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/Delayed.html)). |
| `Collections.sort` | `Enumerable.OrderBy`, written back (`DnsNameResolver.SortStable`) | `Collections.sort` is stable ([Collections](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Collections.html)), [List.Sort](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.list-1.sort?view=net-10.0) is not, [OrderBy](https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.orderby?view=net-10.0) "performs a stable sort". |
| `Atomic*FieldUpdater`, `AtomicLong`, `System.nanoTime()`, `ThreadLocalRandom`, `ConcurrentMap.remove(k, v)`, `putIfAbsent`, `SecureRandom` | `Interlocked` on fields, `SystemTimer.NanoTime()`, `Random.Shared`, [ConcurrentDictionary.TryRemove(KeyValuePair)](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.tryremove?view=net-10.0), [GetOrAdd](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.getoradd?view=net-10.0), [RandomNumberGenerator.GetInt32](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator.getint32?view=net-10.0) | `DnsQueryIdSpace`'s `nextBoolean()` is `GetInt32(2) != 0`. |
| Exceptions | `UnknownHostException` is Common's (owner decision); `initCause` is the `(string, Exception)` constructor; `setStackTrace(cause.getStackTrace())` is `ExceptionDispatchInfo.SetRemoteStackTrace`; `throwException(cause)` in the catch of that cause is `throw;` | The `(message, null, false, true)` constructors of `DnsErrorCauseException`, `DnsResolveContextException` and `SearchDomainUnknownHostException`, and the `fillInStackTrace()` overrides of those three and of `DnsNameResolverException`, have no counterpart: a CLR exception records its stack only when it is thrown ([Throwable](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Throwable.html), [Exception.StackTrace](https://learn.microsoft.com/en-us/dotnet/api/system.exception.stacktrace?view=net-10.0)). `IllegalArgumentException`/`IllegalStateException` are `ArgumentException`/`InvalidOperationException`. |
| Text and files (`ResolvConf`, `UnixResolverDnsServerAddressStreamProvider`) | `ResolvConf.JavaTrim`, `SplitWhitespace`, `int.Parse(s, NumberStyles.AllowLeadingSign, InvariantCulture)`; `FileInfo`/`DirectoryInfo` | `String.trim()` removes code points up to U+0020 and `split` drops trailing empty strings ([String](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html)); Java `\s` is ASCII white space. `FormatException`/`OverflowException` stand for `NumberFormatException`. Readers decode UTF-8 (Java: the platform default charset). `listFiles()` of a missing directory is null. |

### Addresses, host names and order

- **Addresses.** `InetSocketAddress` is `EndPoint`: an `IPEndPoint` when resolved and a `DnsEndPoint` when unresolved,
  so `isUnresolved()` is a `DnsEndPoint` test and `getHostString()` its `Host` ([handler-porting.md](handler-porting.md)
  Addresses, [codec-http-porting.md](codec-http-porting.md)). Name server streams carry `EndPoint` because they may hold
  unresolved name servers from the authoritative cache; the query contexts and `QueryAsync(IPEndPoint, ...)` take
  resolved `IPEndPoint`s. An address prints as
  [IPEndPoint.ToString](https://learn.microsoft.com/en-us/dotnet/api/system.net.ipendpoint.tostring?view=net-10.0).
  `Class<? extends InetAddress>` is `AddressFamily?`. The BCL has no `isIPv4CompatibleAddress()`, so
  `DnsQueryContextManager` tests the first twelve bytes. Transport IPv4-normalizes received senders, so the context
  lookup by `res.Sender` matches the query address.
- **Host names.** `IPAddress` carries no host name. `InetAddress.getByAddress(host, bytes)`
  ([InetAddress](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetAddress.html)) is
  `new IPAddress(bytes)` ([ctor](https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.-ctor?view=net-10.0)),
  so `DnsAddressDecoder` drops the record name and its `IDN.toUnicode` form. `getHostString()` of a resolved address is
  the address text (`DefaultAuthoritativeDnsServerCache.GetHostString`), so a resolved authoritative name server
  replaces a cached unresolved one only when the unresolved host is that address text; the JDK also matches the NS name
  stored in the resolved `InetAddress`. The tests that assert host names are listed under [Test notes](#test-notes).
- **SocketUtils.socketAddress** is the internal `DnsServerAddresses.SocketAddress(host, port)`: Resolver's
  `DefaultNameResolver` (JDK literal rules), and a `DnsEndPoint` when it fails, as Transport's
  `AbstractBootstrap.SocketAddress`.
- **Order.** `UnixResolverDnsServerAddressStreamProvider`'s `values().iterator().next()` takes the first inserted
  domain where `HashMap` takes the first in hash order; this only differs for a resolv.conf with several `domain` lines,
  none named like the file.

### JVM-only APIs

- **DirContextUtils (JNDI).** The `dns://` JNDI lookup of the platform name servers
  ([JNDI DNS](https://docs.oracle.com/javase/8/docs/technotes/guides/jndi/jndi-dns.html)) is
  [NetworkInterface.GetAllNetworkInterfaces](https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.networkinterface.getallnetworkinterfaces?view=net-10.0)
  and the [DnsAddresses](https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.ipinterfaceproperties.dnsaddresses?view=net-10.0)
  of the interfaces that are `Up` ([OperationalStatus](https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.operationalstatus?view=net-10.0))
  and not `Loopback` ([NetworkInterfaceType](https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.networkinterfacetype?view=net-10.0)),
  deduplicated, each an `IPEndPoint` on port 53. Measured on the Windows host: the loopback pseudo-interface reports
  `fec0:0:0:ffff::1..3`, which the filter leaves out.
- **sun.net.dns.ResolverConfiguration.** The `PlatformDependent.javaVersion() < 9` branches of the
  `DefaultDnsServerAddressStreamProvider` static initializer and of `DnsNameResolver.getSearchDomainsHack()` read it by
  reflection on Java 8 only; they are not ported (Common has no `javaVersion()`), and `GetSearchDomainsHack` returns the
  Java 9+ empty list. On Windows `DEFAULT_SEARCH_DOMAINS` is therefore empty, as on Java 9+.
- **Static initializer.** `InetAddress.getLocalHost().getHostName()` is
  [Dns.GetHostName](https://learn.microsoft.com/en-us/dotnet/api/system.net.dns.gethostname?view=net-10.0);
  `anyInterfaceSupportsIpV6()` reads `GetIPProperties().UnicastAddresses` with `IPv6Any`, `IsLoopback` and
  `IsIPv6LinkLocal`.
- **macOS provider.** `DnsServerAddressStreamProviders` keeps the reflective load with
  [Type.GetType](https://learn.microsoft.com/en-us/dotnet/api/system.type.gettype?view=net-10.0) of the forward CLR name
  `Netty.NET.Resolver.Dns.MacOS.MacOSDnsServerAddressStreamProvider, Netty.NET.Resolver.Dns.MacOS`;
  `TypeLoadException`/`FileNotFoundException` stand for `ClassNotFoundException`, and `AccessController.doPrivileged`
  runs its action directly ([AccessController](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/security/AccessController.html)).
  The log text names the CLR module instead of the Maven artifact.
- **DefaultDnsCache.copyThrowable.** An exact `UnknownHostException` (the only type the resolver caches) is copied as a
  new one with the same message and cause and the original's stack trace text (`SetRemoteStackTrace`). Any other
  exception was deep-copied through Java serialization, which has no counterpart (BinaryFormatter throws from .NET 9,
  [guide](https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-security-guide)); it is
  [Object.MemberwiseClone](https://learn.microsoft.com/en-us/dotnet/api/system.object.memberwiseclone?view=net-10.0)
  through reflection, a shallow copy. Suppressed exceptions stay independent because `ThrowableUtil.AddSuppressed` is
  keyed by identity; `Exception.Data`, if present, is shared.
- **GraalVM metadata.** See [Not ported](#not-ported).

## The apacheds stand-in

`TestDnsServer` extends the apacheds-protocol-dns 2.0.0.AM27 `DnsServer` on Apache MINA 2.2.3, the MINA version that
apacheds pins (`mina.core.version` in its [pom.xml](https://github.com/apache/directory-server/blob/2.0.0.AM27/pom.xml));
`Strings` comes from the Apache Directory LDAP API 2.1.5. `TestDnsServer.cs` is a 1:1 port with every original comment.
The 45 stand-in files in `ApacheDs/` (namespace `Netty.NET.Resolver.Dns.Tests.ApacheDs`) reproduce exactly the API that
`TestDnsServer` and the four test classes use, one file per type with the same type names (interfaces with the `I`
prefix) and Java member names; each starts with a `// CLR:` header naming the source it reproduces and its GitHub URL at
the pinned tag. No jar or package was added.

| Origin | Files | Types |
| --- | --- | --- |
| [apacheds-protocol-dns](https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/) | 25 | `DnsServer`, `DnsException`; messages `DnsMessage`, `DnsMessageModifier`, `QuestionRecord`, `IResourceRecord`, `ResourceRecordImpl`, `ResourceRecordModifier`, `RecordType`, `RecordClass`, `ResponseCode`, `MessageType`, `OpCode`; `DnsMessageDecoder`, `DnsMessageEncoder`, `IRecordEncoder`, `ResourceRecordEncoder`; `DnsProtocolHandler`, `DnsUdpEncoder`, `DnsUdpDecoder`, `DnsProtocolUdpCodecFactory`; `DnsContext`, `DomainNameService`; `DnsAttribute`, `IRecordStore` |
| [apacheds-protocol-shared](https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-shared/src/main/java/org/apache/directory/server/protocol/shared/) | 2 | `ITransport`, `UdpTransport` |
| [Directory LDAP API](https://github.com/apache/directory-ldap-api/blob/2.1.5/util/src/main/java/org/apache/directory/api/util/Strings.java) | 1 | `Strings` (internal; `ToLowerCaseAscii`, `IsEmpty`) |
| [Apache MINA](https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/) | 17 | `IoBuffer`, `IIoSession`, `IIoFilterChain`, `IIoFilter`, `IIoHandler`, `IIoAcceptor`, `IDatagramAcceptor`, `IDatagramSessionConfig`, `NioDatagramAcceptor`, `ProtocolCodecFilter`, `IProtocolCodecFactory`, `IProtocolEncoder`, `ProtocolEncoderAdapter`, `IProtocolEncoderOutput`, `IProtocolDecoder`, `ProtocolDecoderAdapter`, `IProtocolDecoderOutput` |

The apacheds enums with values (`RecordType`, `RecordClass`, `ResponseCode`, `MessageType`, `OpCode`) are sealed classes
with static readonly constants compared by reference (`RecordType.Convert(34)` is `CERT`, as `ReverseEnumMap`'s last put
wins). `IoBuffer` follows the `java.nio.Buffer` position/limit rules over a fixed-capacity big-endian `byte[]`.

**Behaviour reproduced** (read from the sources):

- **Decoding a query** (`DnsMessageDecoder`): a name gets a `.` only before a further label, so `netty.io.` arrives as
  `netty.io` and the root as an empty string; compression pointers use only their low byte (an upstream bug kept);
  label bytes are Java-signed chars; only A/NS/MX/AAAA records decode, and any other type throws. **A query with an EDNS
  OPT record therefore gets no response**, which is why the resolver tests disable `optResourceEnabled`.
- **Building the reply** (`DomainNameService`, `DnsProtocolHandler`): one `RecordStore.getRecords` call per question; a
  null or empty set answers `NAME_ERROR`, a `DnsException` an empty reply with its code, and any other exception no
  response. The reply copies the id, RD flag and questions; its answer list keeps the set's iteration order, and the
  authority/additional lists are the live lists that `filterMessage` adds to.
- **Encoding** (`DnsMessageEncoder`): the header counts every record, but only SOA/A/NS/CNAME/PTR/MX/SRV/TXT are
  written; an AAAA record is skipped with a logged `IOException`. **TestDnsServer's AAAA hack** appends the AAAA records
  of all three sections after the encoded message, which only reads correctly when the AAAA records are the only
  records or in the last section (as in the tests). Names are written uncompressed.
- **Transport** (`NioDatagramAcceptor` on a BCL UDP socket): bind applies `SO_REUSEADDR` and throws
  `IOException("Error while binding on ...")`; one session per client endpoint; datagrams are truncated to 2048 bytes;
  a null `filterMessage` result sends nothing; requests run one at a time on the receive thread; `Stop()` closes the
  socket and joins the thread, and `UdpTransport` recreates a disposed acceptor.

**Differences** (recorded in the `// CLR:` headers): `start(dropRecordType, address)` passes the address text instead of
`getHostName()` (same bound address, 127.0.0.1); `HashSet` iteration order is unspecified on both platforms, so a retry
of `startDnsServerAndCreateServerSocket` on the same server may pick another transport than Java (only after a failed UDP
bind); a Windows `WSAECONNRESET` on the server's receive (ICMP port-unreachable after replying to a closed client socket)
is skipped. Not reproduced, because nothing calls it: the LDAP-backed `DnsServer.start()` (it throws
`NotSupportedException`; `TestDnsServer` overrides it), the TCP transport and codec, session expiry, idle events, write
and close futures, and `DnsMessage.equals/hashCode`. The stand-in has no TCP transport; the TCP fallback tests use their
own server socket as in the source, replaying the response through `DnsMessageEncoder` and `IoBuffer`.

During the port, a selfcheck of 19 xUnit tests (outside the project) queried the stand-in with codec-dns over UDP and
the TCP replay path, and an end-to-end probe ran the resolver against it (10 scenarios, among them the TCP fallback);
both passed in repeated runs.

## Cross-module changes

- **UnknownHostException** (owner decision above): Common's `Netty.NET.Common.Internal.UnknownHostException` replaces
  `SocketException(SocketError.HostNotFound)` in Resolver's `DefaultNameResolver` (a `SocketException` from
  `Dns.GetHostAddressesAsync` becomes its cause) and in Transport's `AbstractBootstrap.SocketAddress`; see
  [transport-porting.md](transport-porting.md).
- **DnsQueryContext.RetryWithTcp** uses `(IPEndPoint) (tcpCh.RemoteAddress ?? nameServerAddr)`. A Java socket that was
  connected keeps reporting its remote address after it closes
  ([Socket](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/Socket.html)), while the Transport's
  `RemoteAddress` is null when the peer closed the connection before the address was read
  ([Socket.RemoteEndPoint](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.remoteendpoint?view=net-10.0)
  throws once the socket is closed). Without the fallback `testTcpFallbackFailedWhenTimeout` hung on an
  `ArgumentNullException` inside a discarded continuation. **Open follow-up:** if Transport cached the remote address at
  connect, this fallback would not be needed.

## Not ported

| Unit | Reason |
| --- | --- |
| `package-info.java` | Documentation-only package descriptor; the summary is kept in the manifest and the namespace table above. |
| `module-info.yml` | JPMS module descriptor input; the project references and `InternalsVisibleTo` are the CLR counterpart. |
| `reflect-config.json`, `native-image.properties` | GraalVM metadata; NativeAOT keeps handler metadata through `DynamicallyAccessedMembers` and initializes static state at run time. |
| `NativeImageHandlerMetadataTest` | Compares generated GraalVM metadata with the committed JSON; no CLR counterpart. |

Members, with their comments recorded in the manifest:

| Member | Disposition | Replacement or reason |
| --- | --- | --- |
| `DnsNameResolver`'s two public constructors | deprecated-not-ported | `@deprecated Use DnsNameResolverBuilder`; the internal 25-argument constructor they call is ported. |
| `DnsNameResolverBuilder.channelFactory(ChannelFactory)`, `channelType(Class)` | deprecated-not-ported | `DatagramChannelFactory(Func<IDatagramChannel>)`, `DatagramChannelType(Type)`. |
| `DnsNameResolverBuilder.authoritativeDnsServerCache(DnsCache)` | deprecated-not-ported | `AuthoritativeDnsServerCache(IAuthoritativeDnsServerCache)`; `new AuthoritativeDnsServerCacheAdapter(cache)` is the deprecated method's own body. |
| `DnsNameResolverBuilder.computeResolvedAddressTypes(InternetProtocolFamily...)` and its private `toSocketProtocolFamilies` | deprecated-not-ported | `ToResolvedAddressTypes(params SocketProtocolFamily[])`. |
| `DnsNameResolverBuilder.traceEnabled(boolean)` | deprecated-not-ported | A `LoggingDnsQueryLifeCycleObserverFactory` through `DnsQueryLifecycleObserverFactory(...)`; the field keeps its default. The `@Deprecated` `channelFactory()` getter names no replacement and is ported with `[Obsolete]`. |
| `DnsAddressResolverGroup.newResolver(EventLoop, ChannelFactory, DnsServerAddressStreamProvider)` | deprecated-not-ported | `@deprecated Override newNameResolver(...)`; its body runs in `NewResolver(IEventExecutor)`. |
| `DnsServerAddresses.defaultAddressList()`, `defaultAddresses()` | deprecated-not-ported | `DefaultDnsServerAddressStreamProvider.DefaultAddressList()`, `DefaultAddresses()`. |
| `javaVersion() < 9` branches of `DefaultDnsServerAddressStreamProvider` and `DnsNameResolver.getSearchDomainsHack()` | jvm-only-replaced | Reflective `sun.net.dns.ResolverConfiguration`, Java 8 only. |
| `DirContextUtils.addNameServers` JNDI lookup and `dns://` URL parsing | jvm-only-replaced | BCL network interface DNS addresses (see [JVM-only APIs](#jvm-only-apis)). |
| `fillInStackTrace()` overrides (`DefaultDnsCache` copy, `DnsErrorCauseException`, `DnsResolveContextException`, `SearchDomainUnknownHostException`, `DnsNameResolverException`) | jvm-only-replaced | A CLR exception records its stack only when it is thrown. |
| `serialVersionUID`, `Serializable` | no counterpart | No comments; recorded in the entry scopes. |

## Test notes

- **Lifecycle.** `@BeforeAll/@AfterAll` are class fixtures
  ([xUnit shared context](https://xunit.net/docs/shared-context)), `@BeforeEach/@AfterEach` the constructor and
  `Dispose()`. `@TempDir` is a per-test
  [Directory.CreateTempSubdirectory](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.createtempsubdirectory?view=net-10.0)
  deleted in `Dispose()`.
- **Parameters.** `@ParameterizedTest @EnumSource(DnsNameResolverChannelStrategy.class)` is
  `[Theory] [MemberData(nameof(ChannelStrategies))]`; `@MethodSource` rows holding an `IPAddress` use
  `DisableDiscoveryEnumeration = true` (not xUnit-serializable); `ResolvConfTest.scenarios` runs its 6 rows as a theory.
  `@Timeout(value, MILLISECONDS)` is the theory `Timeout` on an `async Task`.
- **Parallelism.** resolver-dns has no `junit-platform.properties`, so JUnit runs the classes one after another, while
  xUnit runs classes in parallel. `DnsNameResolverTest` (sets `TRY_FINAL_CNAME_ON_ADDRESS_LOOKUPS`, tight timeouts) and
  `IsolatedDnsNameResolverTest` (`@Isolated`, sets `resolveLocalhostWithoutDns`) change process-wide statics, so each
  runs in a collection with `DisableParallelization`
  ([xUnit parallelism](https://xunit.net/docs/running-tests-in-parallel)).
- **Futures in tests.** `syncUninterruptibly().getNow()` is an awaited Task; `awaitUninterruptibly()` is the CLR-only
  `AwaitUninterruptibly(task)`, `cause()` `DnsNameResolver.Cause`, `await(10, SECONDS)` `Task.WhenAny` with
  `Task.Delay`, `promise.cancel(true)` a `CancellationTokenSource`.
  `DnsAddressResolverGroupTest.testUseConfiguredEventLoop` puts its listener on `EventExecutorTaskScheduler(() => loop)`,
  because a Task has no executor; `loop.InEventLoop()` then holds by construction.
- **Anonymous classes and helpers.** Anonymous `DnsNameResolver`, `RecordStore`, `TestDnsServer` and cache classes are
  named nested classes taking the captured locals; a helper that overloads a test method name takes a trailing `_`
  (xUnit1024).
- **Host names** (`IPAddress` carries no host name). Every `getHostName()` assertion of `DnsNameResolverTest` (source
  lines 662/676 through `testResolve0`, 1053, 1164, 1300, 1557, 2092, 3286, 3314, 3361, 3803) is dropped or replaced by
  an address or family assertion with a `// CLR:` comment; `testResolve0` keys its results by the queried name. The test
  servers that need the name of a glue address (`testFollowNsRedirects*`, `testMultipleAdditionalRecordsForSameNSRecord*`,
  `testNSRecordsFromCache`) keep a name map beside the addresses.
  `DefaultAuthoritativeDnsServerCacheTest.testUnresolvedReplacedByResolved` uses `DnsEndPoint("10.0.0.1", 53)` as the
  unresolved entry (the host string of `IPEndPoint(10.0.0.1)`) instead of the name `ns1`; the in-place replacement,
  sizes and order are asserted as in the source.
- **Other adaptations.** `testResolveACachedWithDot` takes the first domain that is not `localhost` (see [Items for
  owner review](#items-for-owner-review)). `testAddressAlreadyInUse` binds a dual-mode socket, because the JDK default
  socket is IPv6 dual-stack (measured: an IPv4-only socket does not stop the resolver's dual-mode channel from
  binding). `testNegativeTtl` uses a volatile flag for the thread interrupt and rethrows an exception of the thread
  after the join. `testTimeoutNotCached` and `testCachesClearedOnClose` pass
  `new AuthoritativeDnsServerCacheAdapter(cache)` for the deprecated `authoritativeDnsServerCache(DnsCache)`.
- **CLR-inapplicable.** `testInstanceWithNullPreferredAddressType` only exercises the `@Deprecated` 17-argument
  constructor with a null `ResolvedAddressTypes`, which a C# enum cannot be; it stays compiled and skipped so the
  source's argument comments stay beside the values.
- **Timing.** A full run takes about 1 min 32 s. `testCNAMERecursiveResolveDifferentNameServersForDomains` waits a 5 s
  query timeout because the AAAA hack cannot encode its AAAA answer `1.2.3.4` (the same happens in Java), and several
  tests wait 2 s timeouts toward `10.0.0.x:53`.
- **Analyzers.** `Assert.Single/Empty` for size checks (xUnit2013); xUnit1051 (the resolver Tasks are called without the
  test token) and xUnit1069 are suppressed with the reason.

## Items for owner review

- **macOS provider.** `DnsServerAddressStreamProviders` loads
  `Netty.NET.Resolver.Dns.MacOS.MacOSDnsServerAddressStreamProvider, Netty.NET.Resolver.Dns.MacOS` by `Type.GetType`,
  a forward CLR name: `resolver-dns-classes-macos` is not ported, so on macOS the load fails and the resolver falls back
  to the system defaults with the source's warning.
- **DirContextUtils.** The JNDI lookup is replaced by the BCL `NetworkInterface` DNS addresses (up, non-loopback
  interfaces, port 53).
- **DefaultDnsCache.copyThrowable** copies a non-`UnknownHostException` with `MemberwiseClone` (a shallow copy) instead
  of a Java serialization round trip.
- **Cancellation** is the caller's `CancellationToken`; a canceled resolve is a canceled Task whose await throws
  `TaskCanceledException`, where Java's canceled future throws `CancellationException`.
- **Deprecated members** with named replacements are not ported (the table under [Not ported](#not-ported)): the two
  public `DnsNameResolver` constructors, the five `DnsNameResolverBuilder` members (`channelFactory(ChannelFactory)`,
  `channelType`, `authoritativeDnsServerCache(DnsCache)`, `computeResolvedAddressTypes`, `traceEnabled`),
  `DnsServerAddresses.defaultAddressList()` and `defaultAddresses()`, and
  `DnsAddressResolverGroup.newResolver(EventLoop, ChannelFactory, DnsServerAddressStreamProvider)`. A subclass can no
  longer override that last method; it overrides `NewNameResolver`/`NewAddressResolver` instead.
- **Host names.** `IPAddress` has no host name. The `getHostName()` assertions of `DnsNameResolverTest` were replaced
  or removed; as a result `testResolveDecodeUnicode` and `testResolveNotDecodeUnicode` now check the same thing
  (`decodeIdn` only changes the name stored in the JDK `InetAddress`).
  `DefaultAuthoritativeDnsServerCacheTest.testUnresolvedReplacedByResolved` changed its input (see
  [Test notes](#test-notes)).
- **testResolveACachedWithDot.** Java's `DOMAINS.iterator().next()` is always `google.co.in` (fixed `String.hashCode`);
  C# string hashes are randomized per process, so the first element could be `localhost`, which the resolver answers
  from the hosts file and never caches. The test picks the first domain that is not `localhost`.
- **Non-parallel collections.** `DnsNameResolverTest` and `IsolatedDnsNameResolverTest` run in non-parallel xUnit
  collections because they change process-wide statics.
- **Shared-API request (Resolver).** `DefaultNameResolver` resolves an IPv6 literal whose scope names no interface
  (`fe80::21c:42ff:fe00:18%nonexisting`; `IPAddress.TryParse` accepts an unknown scope name), where the JDK leaves it
  unresolved and `UnixResolverDnsServerAddressStreamProvider` filters it. The C# name server stream therefore holds that
  address; `UnixResolverDnsServerAddressStreamProviderTest.ipv6Nameserver` passes because, as in the source, it asserts
  the first server only.
- **Shared-API request (Transport).** Caching the remote address at connect would remove the `RetryWithTcp` fallback
  (see [Cross-module changes](#cross-module-changes)).
