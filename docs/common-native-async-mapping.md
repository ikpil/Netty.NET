# Native asynchronous mapping

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common AsyncMapping and its native consumer contract. TLS/handler modules
are inspected as consumers; their decoding and pipeline implementations are not
ported by this change.

## Original requirements and consumers

common/src/main/java/io/netty/util/AsyncMapping.java accepts an input and a
caller-provided Promise, and returns a Future for the result. There is no
dedicated common AsyncMapping test at the pinned commit. The original interface
does not define a cancellation policy, null/default mapping, cache ownership or
reference-count transfer for the mapped value.

handler/src/main/java/io/netty/handler/ssl/SniHandler.java uses the interface in
lookup: it passes the hostname and an executor-bound Promise. Its synchronous
AsyncMappingAdapter converts a Mapping result or thrown exception into that
Promise's success/failure. SNI can pass a null hostname for default selection.
The asynchronous provider's returned Future supplies the selection result.

SslClientHelloHandler.select distinguishes completed lookup from deferred lookup.
Deferred lookup suppresses reads and transfers the retained ClientHello to a
completion listener. That listener releases the buffer before processing the
lookup result and resumes a pending read even when result processing fails.
SniHandler.onLookupComplete publishes a successful selection and handles pipeline
replacement failures separately. AbstractSniHandler cancels the handshake timeout
on lookup completion; the timeout closes an active channel, without canceling the
mapping Future. SniHandler explicitly warns that replacement can run after the
client disconnects. An observer timeout must not prematurely release a buffer
still owned by a pending lookup callback.

## CLR decision

IAsyncMapping now exposes MapAsync(TInput, CancellationToken) returning Task<TOutput>.
The provider owns completion, using its own asynchronous method, cached Task or
TaskCompletionSource. No caller-provided Promise, Java Future result adapter or
second completion state remains in this interface. Argument validation can throw
during invocation; asynchronous failures are delivered through the returned Task.
The Task must be non-null; null input/result policy belongs to the provider and
its consumer. Task.FromResult/Task.FromException are sufficient for synchronous
adapters; no extra common adapter class is introduced just to reproduce Java.

Input contravariance is legal in CLR. Output remains invariant because Task<T>
is invariant; Java wildcard covariance does not become a cast between different
Task<T> instances. An output conversion must be explicit in a typed async
provider/consumer. CLR value-type results do not require Java boxing or Void.

CancellationToken adds an optional cooperative cancellation request. The mapper
decides whether it can honor it. A successful provider result remains successful
even if the token was requested; cancellation is represented by the returned Task.
Canceling Task.WaitAsync cancels only that caller's wait. A future handler port
must decide separately whether disconnect/timeout should request producer
cancellation; the pinned handler does not automatically cancel its mapping.

Task carries no Netty executor affinity. Invoke mapping and mutate handler-owned
state on the chosen executor. For a synchronous completion callback, keep an
ExecutorCompletion observation and its CompletionRegistration alive until
NotificationCompleted finishes, and catch/report result-processing exceptions
inside the callback. An ordinary async consumer can instead await the Task and
explicitly SubmitAsync subsequent executor-owned work. Await alone does not move
that code onto the loop. Callback completion and operation completion remain
distinct. Buffer and callback ownership must survive a canceled external wait.

For example, native provider implementations have this shape:

```csharp
public Task<Context> MapAsync(string hostname, CancellationToken cancellationToken = default)
{
    // The provider owns its asynchronous result, not an externally supplied Promise.
    return LoadContextAsync(hostname, cancellationToken);
}
```

Context/LoadContextAsync represent the future handler/provider's own types and
work; the executable common consumer example is AsyncMappingContractTest.

## Verification and remaining work

AsyncMappingContractTest models the original lookup/retained-ClientHello/read
consumer using a real DefaultEventExecutor and native mapping implementations.
It does not implement TLS parsing or claim to execute SniHandlerTest. Ten Debug
cases pass in async-mapping-contracts-debug.trx:

- Six immediate/deferred success, failure and cancellation combinations preserve
  provider Task identity, input contravariance, hostname/default null input, token identity, invocation
  and callback affinity, release-before-result ordering and pending-read resume.
- Two invocation-error/null-Task cases release the retained message on the loop.
- One requested producer token still permits the provider's successful result.
- One canceled observer wait preserves the pending provider result and message
  ownership until the callback can finish and resume the queued read.

The first full Debug run discovered 1311 cases: 1296 passed, one failed and 14
skipped (async-mapping-full-debug.trx). All ten mapping cases passed. The existing
UnorderedExecutorContractTest.ShutdownKeepsDelayedOneShotWorkAndCancelsPeriodicWork
failed its assertion that a one-shot scheduled for 100ms was still pending after
shutdown. Its 346ms test duration and completed one-shot do not indicate lost
shutdown work: the precondition depended on elapsed machine scheduling time.
The test now blocks the single worker before scheduling, verifies the one-shot
is pending after shutdown and the periodic work canceled, then releases the
worker and checks result 42 and actual pool termination. The delay and shutdown
expectations are unchanged. All eleven affected Debug cases pass in
async-mapping-final-contracts-debug.trx. The final contravariant consumer selection
also passes eleven cases in Debug/Release (async-mapping-final-contracts-debug-current.trx
and async-mapping-final-contracts-release.trx). Default full Debug/Release each
discover 1311 cases: 1297 passed, zero failed, 14 skipped. Evidence:
async-mapping-full-debug-final.trx and async-mapping-full-release-final.trx.
All ten mapping cases pass in each. The failed run is retained.

The original interface license and method comment are preserved next to MapAsync,
with the Promise/Future comment marked as upstream provenance and the native
completion contract documented separately. Original comment count: two. The old
method comment had changed Promise to IPromise; this restores the exact pinned
text. Passing common consumer tests does not establish handler/pipeline, TLS
configuration ownership, disconnect policy or public Future/Promise removal
elsewhere. Those remain separate porting work.
