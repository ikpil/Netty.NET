# Netty.NET
- https://github.com/netty/netty/commit/64cc10f38ea5f5bd7eae48507817c66680d0afdc

The buffer port has started; see [buffer porting status](docs/buffer-porting.md).
The transport port is in progress; its [source and test manifest](docs/transport-porting-manifest.json)
records channel dependencies, Channel/Pipeline, event loops, Embedded/Local channels,
Bootstrap/Resolver integration and native TCP/UDP work, with original test mappings
and explicit remaining implementation, test and comment review. Transport is not complete.
The codec-base port (namespace `Netty.NET.Handler.Codec`) covers the decoder/encoder bases, frame decoders,
ReplayingDecoder, Headers and the base64/bytes/json/string codecs with their original tests; see
[codec-base porting](docs/codec-base-porting.md).
The codec modules that need only codec-base are ported the same way, each as its own project
(`Netty.NET.Handler.Codec.<Name>`): [dns](docs/codec-dns-porting.md), [haproxy](docs/codec-haproxy-porting.md),
[memcache](docs/codec-memcache-porting.md), [mqtt](docs/codec-mqtt-porting.md), [redis](docs/codec-redis-porting.md),
[smtp](docs/codec-smtp-porting.md), [socks](docs/codec-socks-porting.md), [stomp](docs/codec-stomp-porting.md) and
[compression](docs/codec-compression-porting.md) (Snappy, FastLz, Bzip2, zlib/gzip on the BCL streams and Brotli on
the BCL encoder; the Lz4, Lzf, Lzma, JZlib and Zstd codecs need Java libraries without a .NET 10 BCL counterpart and
are recorded as not ported), [xml](docs/codec-xml-porting.md) (XmlFrameDecoder; XmlDecoder needs aalto-xml) and
[protobuf](docs/codec-protobuf-porting.md) (the varint32 framing; the message codecs need protobuf-java).
[codec-marshalling](docs/codec-marshalling-porting.md) needs JBoss Marshalling in every file and is recorded in its
manifest without a C# project.
The handler port (`Netty.NET.Handler`) covers all of netty-handler: address, flow, flush, ipfilter, logging, pcap,
stream, timeout, traffic and io.netty.handler.ssl, whose SslHandler runs on `System.Net.Security.SslStream` with the
BCL authentication options in place of SSLEngine (design: [handler-ssl-design.md](docs/handler-ssl-design.md)); the
OpenSSL (netty-tcnative), Bouncy Castle and Conscrypt units are recorded as library-not-ported; see
[handler porting](docs/handler-porting.md) (`tools/Test-CodecPortingManifest.ps1 -Module handler`). Its test-support
module [pkitesting](docs/pkitesting-porting.md) (`Netty.NET.PkiTesting`) is ported on the BCL certificate APIs.
Check every codec, pkitesting and handler manifest with `powershell -NoProfile -File tools/Test-CodecPortingManifest.ps1`
(or one with `-Module codec-dns`).
The resolver port implements the basic OS-backed resolver module; see its
[source, test and CLR adaptation manifest](docs/resolver-porting-manifest.json).
It uses Task/CancellationToken, DnsEndPoint/IPEndPoint and IDisposable; executor-affine
consumers dispatch explicitly through Common. The separate resolver-dns module
is outside the basic resolver port. Check the pinned sources, original
test mappings and preserved comments with `powershell -NoProfile -File tools/Test-ResolverPortingManifest.ps1`.
The common port is in progress. See [porting status](docs/common-porting.md),
[CLR design decisions](docs/common-clr-design.md),
[native Task scheduling](docs/common-native-scheduling.md),
[native progress reporting](docs/common-native-progress.md),
[detachable progress subscriptions](docs/common-progress-subscriptions.md),
[native completion observation](docs/common-native-completion.md),
[native asynchronous mapping](docs/common-native-async-mapping.md), and
[standard Task completed results](docs/common-completed-results.md).
External producers now own [TaskCompletionSource results](docs/common-native-producers.md)
without an executor Promise factory.
Java submission callers now use [native Task submission](docs/common-native-submission-migration.md).
Inherited JDK bulk APIs are replaced by [standard Task composition](docs/common-native-bulk-composition.md).
Ordered scheduling callers now use [native Tasks and cancellation tokens](docs/common-native-ordered-scheduling-migration.md).
The [unordered scheduler and raw queue work](docs/common-native-unordered-scheduling-migration.md)
also use native results without JDK Future/Promise decoration.
The unused [submission wrappers](docs/common-native-submission-wrapper-cleanup.md)
are removed after their consumers migrated to native delegates.
The remaining [Future/Promise result and listener hierarchy](docs/common-native-future-retirement.md)
is replaced by Task/TCS results, native operation claims and explicit completion observation.
The unordered executor's [termination signal](docs/common-unordered-termination.md)
waits for its queue and worker reservations to drain.
Its [graceful shutdown](docs/common-unordered-graceful-shutdown.md) accepts work
during quiet waiting and closes admission at quiet expiry or timeout, then drains.
Its [worker identity](docs/common-unordered-worker-identity.md) recognizes threads created by a stateful constructor factory.
Its [queue API](docs/common-unordered-native-queue.md) exposes pending counts;
native token cancellation withdraws work without exporting mutable queue handles.
Its [native configuration](docs/common-unordered-native-configuration.md) fixes worker limits and shutdown policy at construction, with read-only worker diagnostics.
Its [worker failure boundary](docs/common-unordered-worker-failure.md) faults waiting native work and termination after surviving workers drain if replacement creation fails.
Its [cooperative immediate stop](docs/common-unordered-cooperative-stop.md) uses StopToken and StopAsync, with cancellation callback drain and failures included in termination.
Its [deadline queue removal](docs/common-unordered-queue-costs.md) uses measured BCL identity removal instead of rebuilding the queue on cancellation.
Auto-scaling [monitoring windows](docs/common-autoscaling-monitor-windows.md)
coalesce catch-up callbacks while preserving fixed-rate cadence and actual elapsed utilization.
