# Netty.NET
- https://github.com/netty/netty/commit/e66ce34777f9c4a0c57ac74bb97396ca2f54b43c

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
Its [worker identity](docs/common-unordered-worker-identity.md) also survives thread-factory replacement.
Its [queue API](docs/common-unordered-native-queue.md) exposes pending counts;
native token cancellation withdraws work without exporting mutable queue handles.
